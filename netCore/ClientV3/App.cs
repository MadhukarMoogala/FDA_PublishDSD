namespace ClientV3
{
    using Autodesk.Forge;
    using Autodesk.Forge.Core;
    using Autodesk.Forge.DesignAutomation;
    using Autodesk.Forge.DesignAutomation.Model;
    using Autodesk.Forge.Model;
    using Microsoft.Extensions.Options;
    using ShellProgressBar;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;

    /// <summary>
    /// Defines the <see cref="App" />.
    /// </summary>
    class App
    {
        /// <summary>
        /// Defines the PackageName.
        /// </summary>
        static readonly string PackageName = "Adsk_BatchPublish_v3";

        /// <summary>
        /// Defines the BucketKey.
        /// </summary>
        static readonly string BucketKey = "batchpublishworks19042024";

        /// <summary>
        /// Defines the ActivityName.
        /// </summary>
        static readonly string ActivityName = "Adsk_BatchPublish_v3";

        /// <summary>
        /// Defines the Owner.
        /// </summary>
        static readonly string Owner = "batchworks";

        /// <summary>
        /// Defines the inputFileNameOSS.
        /// </summary>
        static string inputFileNameOSS = string.Format("{0}_input_{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), Path.GetFileName(FilePaths.InputFile));

        /// <summary>
        /// Defines the outputFileNameOSS.
        /// </summary>
        static string outputFileNameOSS = string.Format("{0}_output_{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), "result.pdf");

        /// <summary>
        /// Defines the UploadUrl.
        /// </summary>
        static string UploadUrl = "";

        /// <summary>
        /// Defines the DownloadUrl.
        /// </summary>
        static string DownloadUrl = "";

        /// <summary>
        /// Defines the Label.
        /// </summary>
        static readonly string Label = "prod";

        /// <summary>
        /// Defines the TargetEngine.
        /// </summary>
        static readonly string TargetEngine = "Autodesk.AutoCAD+24_3";

        /// <summary>
        /// Gets or sets the InternalToken.
        /// </summary>
        private static dynamic InternalToken { get; set; }

        /// <summary>
        /// Defines the api.
        /// </summary>
        public DesignAutomationClient api;

        /// <summary>
        /// Defines the config.
        /// </summary>
        public ForgeConfiguration config;

        /// <summary>
        /// Initializes a new instance of the <see cref="App"/> class.
        /// </summary>
        /// <param name="api">The api<see cref="DesignAutomationClient"/>.</param>
        /// <param name="config">The config<see cref="IOptions{ForgeConfiguration}"/>.</param>
        public App(DesignAutomationClient api, IOptions<ForgeConfiguration> config)
        {
            this.api = api;
            this.config = config.Value;
        }

        /// <summary>
        /// The GetInternalAsync.
        /// </summary>
        /// <returns>The <see cref="Task{dynamic}"/>.</returns>
        public async Task<dynamic> GetInternalAsync()
        {
            if (InternalToken == null || InternalToken.ExpiresAt < DateTime.UtcNow)
            {
                InternalToken = await Get2LeggedTokenAsync(new Scope[] { Scope.BucketCreate, Scope.BucketRead, Scope.BucketDelete, Scope.DataRead, Scope.DataWrite, Scope.DataCreate, Scope.CodeAll });
                InternalToken.ExpiresAt = DateTime.UtcNow.AddSeconds(InternalToken.expires_in);
            }

            return InternalToken;
        }

        /// <summary>
        /// Get the access token from Autodesk.
        /// </summary>
        /// <param name="scopes">The scopes<see cref="Scope[]"/>.</param>
        /// <returns>The <see cref="Task{dynamic}"/>.</returns>
        public async Task<dynamic> Get2LeggedTokenAsync(Scope[] scopes)
        {
            TwoLeggedApi oauth = new TwoLeggedApi();
            string grantType = "client_credentials";
            dynamic bearer = await oauth.AuthenticateAsync(config.ClientId,
              config.ClientSecret,
              grantType,
              scopes);
            return bearer;
        }

        /// <summary>
        /// The RunAsync.
        /// </summary>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task RunAsync()
        {
            if (string.IsNullOrEmpty(Owner))
            {
                Console.WriteLine("Please provide non-empty Owner.");
                return;
            }

            if (string.IsNullOrEmpty(UploadUrl))
            {
                Console.WriteLine("Creating Bucket and OSS Object");


                dynamic oauth = await GetInternalAsync();

                // 1. ensure bucket exists

                BucketsApi buckets = new BucketsApi();
                buckets.Configuration.AccessToken = oauth.access_token;
                try
                {
                    PostBucketsPayload bucketPayload = new PostBucketsPayload(BucketKey, null, PostBucketsPayload.PolicyKeyEnum.Transient);
                    dynamic bucketsRes = await buckets.CreateBucketAsync(bucketPayload, "US");
                }
                catch
                {
                    // in case bucket already exists
                    Console.WriteLine($"\tBucket {BucketKey} exists");
                };
                ObjectsApi objectsApi = new ObjectsApi();
                objectsApi.Configuration.AccessToken = oauth.access_token;
                dynamic objects = await objectsApi.GetObjectsAsync(BucketKey, 10, "2020");
                bool bFound = false;

#if DEBUG
                
                /*We will use existing input zip for testing
                 In release builds we will allow user input zip file*/
                foreach (KeyValuePair<string, dynamic> objInfo in new DynamicDictionaryItems(objects.items))
                {
                    if (objInfo.Value.objectKey.Contains("exported.zip"))
                        bFound = true;
                    inputFileNameOSS = objInfo.Value.objectKey;
                    break;
                }

#endif

                //2. Upload input file and get signed URL
                if (!bFound)
                {
                    long fileSize = (new FileInfo(FilePaths.InputFile)).Length;
                    long chunkSize = 2 * 1024 * 1024; // 100Kb
                    int numberOfChunks = (int)Math.Round((double)(fileSize / chunkSize)) + 1;
                    var options = new ProgressBarOptions
                    {
                        ProgressCharacter = '#',
                        ProgressBarOnBottom = false,
                        ForegroundColorDone = ConsoleColor.Green,
                        ForegroundColor = ConsoleColor.White
                    };

                    using var pbar = new ProgressBar(numberOfChunks, $"Uploading input file {inputFileNameOSS} to {BucketKey}..... ", options);
                    long start = 0;
                    chunkSize = (numberOfChunks > 1 ? chunkSize : fileSize);
                    long end = chunkSize;
                    string sessionId = Guid.NewGuid().ToString();
                    // upload one chunk at a time
                    using BinaryReader reader = new BinaryReader(new FileStream(FilePaths.InputFile, FileMode.Open));
                    for (int chunkIndex = 0; chunkIndex < numberOfChunks; chunkIndex++)
                    {
                        string range = string.Format("bytes {0}-{1}/{2}", start, end, fileSize);

                        long numberOfBytes = chunkSize + 1;
                        byte[] fileBytes = new byte[numberOfBytes];
                        MemoryStream memoryStream = new MemoryStream(fileBytes);
                        reader.BaseStream.Seek((int)start, SeekOrigin.Begin);
                        int count = reader.Read(fileBytes, 0, (int)numberOfBytes);
                        memoryStream.Write(fileBytes, 0, (int)numberOfBytes);
                        memoryStream.Position = 0;
                        
                        dynamic chunkUploadResponse = await objectsApi.UploadChunkAsyncWithHttpInfo(BucketKey, inputFileNameOSS, (int)numberOfBytes, range, sessionId, memoryStream);

                        start = end + 1;
                        chunkSize = ((start + chunkSize > fileSize) ? fileSize - start - 1 : chunkSize);
                        end = start + chunkSize;
                        double size = chunkIndex == 0 ? chunkSize / 1024 : (chunkIndex * chunkSize) / 1024;
                        var CustomText = $"{(fileSize-chunkSize)/ 1024} Kb uploaded...";
                        pbar.Tick(CustomText);

                    }



                }
                else
                {
                    Console.WriteLine($"{inputFileNameOSS} found in {BucketKey}.....\n\tCreating a signed resource.... ");
                }

                try
                {
                    PostBucketsSigned bucketsSigned = new PostBucketsSigned(60);
                    dynamic signedResp = await objectsApi.CreateSignedResourceAsync(BucketKey, inputFileNameOSS, bucketsSigned, "read");
                    DownloadUrl = signedResp.signedUrl;
                    signedResp = await objectsApi.CreateSignedResourceAsync(BucketKey, outputFileNameOSS, bucketsSigned, "readwrite");
                    UploadUrl = signedResp.signedUrl; 
                    Console.WriteLine($"\tSuccess: signed resource for input.zip created!\n\t{DownloadUrl}");
                    Console.WriteLine($"\tSuccess: signed resource for result.pdf created!\n\t{UploadUrl}");
                }
                catch { }


            }

            if (!await SetupOwnerAsync())
            {
                Console.WriteLine("Exiting.");
                return;
            }

            var myApp = await SetupAppBundleAsync();
            var myActivity = await SetupActivityAsync(myApp);

            await SubmitWorkItemAsync(myActivity);
        }

        /// <summary>
        /// The SubmitWorkItemAsync.
        /// </summary>
        /// <param name="myActivity">The myActivity<see cref="string"/>.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task SubmitWorkItemAsync(string myActivity)
        {
            Console.WriteLine("Submitting up workitem...");            
            var workItemStatus = await api.CreateWorkItemAsync(new Autodesk.Forge.DesignAutomation.Model.WorkItem()
            {
                ActivityId = myActivity,
                Arguments = new Dictionary<string, IArgument>() {
                              {
                               "inputFile",
                               new XrefTreeArgument() {
                                Url = "http://download.autodesk.com/us/support/files/autocad_2015_templates/acad.dwt"
                               }
                              }, {
                               "inputZip",
                               new XrefTreeArgument() {
                                Url = DownloadUrl, Verb = Verb.Get, LocalName = "export"
                               }
                              }, {
                               "Result",
                               new XrefTreeArgument() {
                                Verb = Verb.Put, Url = UploadUrl
                               }
                              }
                             }
            });

            Console.Write("\tPolling status");
            while (!workItemStatus.Status.IsDone())
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                workItemStatus = await api.GetWorkitemStatusAsync(workItemStatus.Id);
                Console.Write(".");
            }
            Console.WriteLine($"{workItemStatus.Status}.");
            var fname = await DownloadToDocsAsync(workItemStatus.ReportUrl, "Das-report.txt");
            if (workItemStatus.Status != Status.Success)
            {
                Console.WriteLine($"{workItemStatus.Status} Please refer log {fname} further details.. exiting! ");
                return;
            }
            Console.WriteLine($"Downloaded {fname}.");         
            var result = await DownloadToDocsAsync(UploadUrl, outputFileNameOSS, true);
            Console.WriteLine($"Downloaded {result}.");
        }

        /// <summary>
        /// The SetupActivityAsync.
        /// </summary>
        /// <param name="myApp">The myApp<see cref="string"/>.</param>
        /// <returns>The <see cref="Task{string}"/>.</returns>
        private async Task<string> SetupActivityAsync(string myApp)
        {
            Console.WriteLine("Setting up activity...");
            var myActivity = $"{Owner}.{ActivityName}+{Label}";
            var actResponse = await this.api.ActivitiesApi.GetActivityAsync(myActivity, throwOnError: false);
            var activity = new Activity()
            {
                Appbundles = new List<string>()
                    {
                        myApp
                    },
                CommandLine = new List<string>()
                    {
                        $"$(engine.path)\\accoreconsole.exe /i \"$(args[inputFile].path)\" /al \"$(appbundles[{PackageName}].path)\" /s \"$(settings[script].path)\""
                    },
                Engine = TargetEngine,
                Settings = new Dictionary<string, ISetting>()
                    {
                        { "script", new StringSetting() { Value = "BatchPublishCmd\n" } }
                    },
                Parameters = new Dictionary<string, Parameter>()
                    {
                        { "inputFile", new Parameter() { Verb= Verb.Get, LocalName = "$(HostDwg)",  Required = true } },
                        { "inputZip", new Parameter() { Verb= Verb.Get, Zip=true, LocalName = "export", Required = true} },
                        { "Result", new Parameter() { Verb= Verb.Put,  LocalName = "result.pdf", Required= true} }
                    },
                Id = ActivityName
            };
            if (actResponse.HttpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"Creating activity {myActivity}...");
                await api.CreateActivityAsync(activity, Label);
                return myActivity;
            }
            await actResponse.HttpResponse.EnsureSuccessStatusCodeAsync();
            Console.WriteLine("\tFound existing activity...");
            if (!Equals(activity, actResponse.Content))
            {
                Console.WriteLine($"\tUpdating activity {myActivity}...");
                await api.UpdateActivityAsync(activity, Label);
            }
            return myActivity;

            bool Equals(Autodesk.Forge.DesignAutomation.Model.Activity a, Autodesk.Forge.DesignAutomation.Model.Activity b)
            {
                Console.Write("\tComparing activities...");
                //ignore id and version
                b.Id = a.Id;
                b.Version = a.Version;
                var res = a.ToString() == b.ToString();
                Console.WriteLine(res ? "Same." : "Different");
                return res;
            }
        }

        /// <summary>
        /// The SetupAppBundleAsync.
        /// </summary>
        /// <returns>The <see cref="Task{string}"/>.</returns>
        private async Task<string> SetupAppBundleAsync()
        {
            Console.WriteLine("Setting up appbundle...");
            var myApp = $"{Owner}.{PackageName}+{Label}";
            var appResponse = await this.api.AppBundlesApi.GetAppBundleAsync(myApp, throwOnError: false);
            var app = new AppBundle()
            {
                Engine = TargetEngine,
                Id = PackageName
            };
            var package = CreateZip();
            if (appResponse.HttpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"\tCreating appbundle {myApp}...");
                await api.CreateAppBundleAsync(app, Label, package);
                return myApp;
            }
            await appResponse.HttpResponse.EnsureSuccessStatusCodeAsync();
            Console.WriteLine("\tFound existing appbundle...");
            if (!await EqualsAsync(package, appResponse.Content.Package))
            {
                Console.WriteLine($"\tUpdating appbundle {myApp}...");
                await api.UpdateAppBundleAsync(app, Label, package);
            }
            return myApp;

            async Task<bool> EqualsAsync(string a, string b)
            {
                Console.Write("\tComparing bundles...");
                using var aStream = File.OpenRead(a);
                var bLocal = await DownloadToDocsAsync(b, "das-appbundle.zip");
                using var bStream = File.OpenRead(bLocal);
                using var hasher = SHA256.Create();
                var res = hasher.ComputeHash(aStream).SequenceEqual(hasher.ComputeHash(bStream));
                Console.WriteLine(res ? "Same." : "Different");
                return res;
            }
        }

        /// <summary>
        /// The SetupOwnerAsync.
        /// </summary>
        /// <returns>The <see cref="Task{bool}"/>.</returns>
        private async Task<bool> SetupOwnerAsync()
        {
            Console.WriteLine("Setting up owner...");
            var nickname = await api.GetNicknameAsync("me");
            if (nickname == config.ClientId)
            {
                Console.WriteLine("\tNo nickname for this clientId yet. Attempting to create one...");
                HttpResponseMessage resp;
                resp = await api.ForgeAppsApi.CreateNicknameAsync("me", new NicknameRecord() { Nickname = Owner }, throwOnError: false);
                if (resp.StatusCode == HttpStatusCode.Conflict)
                {
                    Console.WriteLine("\tThere are already resources associated with this clientId or nickname is in use. Please use a different clientId or nickname.");
                    return false;
                }
                await resp.EnsureSuccessStatusCodeAsync();
            }
            return true;
        }

        /// <summary>
        /// The CreateZip.
        /// </summary>
        /// <returns>The <see cref="string"/>.</returns>
        static string CreateZip()
        {
            Console.WriteLine("\tGenerating autoloader zip...");
           
            string zip = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "package.zip");
            if (!File.Exists(zip))
            {
                //try, searching, probably we are in debugging.
                StackFrame CallStack = new StackFrame(1, true);
                Console.Write($"Error: !package.zip file not found\npass absolute path of zip package,\n\tFile: {CallStack.GetFileName()}  Line: {CallStack.GetFileLineNumber()}");
                throw new FileNotFoundException($"package.zip");
            }
            return zip;
        }

        /// <summary>
        /// The DownloadToDocsAsync.
        /// </summary>
        /// <param name="url">The url<see cref="string"/>.</param>
        /// <param name="localFile">The localFile<see cref="string"/>.</param>
        /// <param name="isOauthRequired">The isOauthRequired<see cref="bool"/>.</param>
        /// <returns>The <see cref="Task{string}"/>.</returns>
        public async Task<string> DownloadToDocsAsync(string url, string localFile, bool isOauthRequired = false)
        {
            var report = FilePaths.OutPut;
            var fname = Path.Combine(report, localFile);
            if (File.Exists(fname))
                File.Delete(fname);
            using var client = new HttpClient();
            if (isOauthRequired)
            {
                dynamic oAuth = await GetInternalAsync();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oAuth.access_token);

            }
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            using (var fs = new FileStream(fname, FileMode.CreateNew))
            {
                await response.Content.CopyToAsync(fs);
            }

            return fname;
        }
    }
}
