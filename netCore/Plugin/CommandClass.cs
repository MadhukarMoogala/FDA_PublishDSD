namespace BatchPublishCommand
{
    using Autodesk.AutoCAD.ApplicationServices;
    using Autodesk.AutoCAD.ApplicationServices.Core;
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.EditorInput;
    using Autodesk.AutoCAD.GraphicsSystem;
    using Autodesk.AutoCAD.PlottingServices;
    using Autodesk.AutoCAD.Runtime;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Defines the <see cref="CommandClass" />.
    /// </summary>
    public static class CommandClass
    {
        static string rootDir = string.Empty;
        /// <summary>
        /// The BatchPublishCmd.
        /// </summary>
        [CommandMethod("BatchPublishCmd", CommandFlags.Session)]

        static public void BatchPublishCmd()
        {
            Editor ed = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage($"\n Current Directory {Directory.GetCurrentDirectory()}");
            rootDir = Path.Combine(Directory.GetCurrentDirectory(), "export");
            ed.WriteMessage($"\n Exported drawings are found here {rootDir}");
            Application.SetSystemVariable("PlotToFilePath", Path.Combine(rootDir, "result.pdf"));
            short bgPlot = (short)Application.GetSystemVariable("BACKGROUNDPLOT");
            Application.SetSystemVariable("BACKGROUNDPLOT", 0);

            System.Collections.Generic.List<string> docsToPlot = new System.Collections.Generic.List<string>();


            foreach (var file in Directory.GetFiles(rootDir, "*.dwg"))
            {
                var newFilePath = file.Contains(" ") ? file.Replace(" ", "") : file;
                File.Move(file, newFilePath);
                ed.WriteMessage($"\n\tDrawing to be plotted {newFilePath}");                
                docsToPlot.Add(newFilePath);
            }

            BatchPublish(docsToPlot);
        }

        /// <summary>
        /// The BatchPublish.
        /// </summary>
        /// <param name="docsToPlot">The docsToPlot<see cref="List{string}"/>.</param>
        private static void BatchPublish(List<string> docsToPlot)
        {

            using (DsdEntryCollection collection = new DsdEntryCollection())
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                foreach (string filename in docsToPlot)
                {
                    using (DocumentLock doclock = doc.LockDocument())
                    {
                        using (Database db = new Database(false, true))
                        {
                            db.ReadDwgFile(filename, System.IO.FileShare.Read, true, "");
                            System.IO.FileInfo fi = new FileInfo(filename);
                            string docName = fi.Name.Substring(0, fi.Name.Length - 4);
                            using (Transaction Tx = db.TransactionManager.StartTransaction())
                            {

                                foreach (ObjectId layoutId in GeLayoutIds(db))
                                {
                                    Layout layout = Tx.GetObject(layoutId, OpenMode.ForRead) as Layout;
                                    if (!layout.ModelType)
                                    {
                                        var ids = layout.GetViewports();
                                        if (ids.Count == 0)
                                        {
                                            doc.Editor.WriteMessage($"\n{layout.LayoutName} is not initialized, so no plottable sheets in the current drawing ");
                                        }
                                    }

                                    DsdEntry entry = new DsdEntry
                                    {
                                        DwgName = filename,
                                        Layout = layout.LayoutName,
                                        Title = docName + "_" + layout.LayoutName
                                    };
                                    entry.NpsSourceDwg = entry.DwgName;
                                    entry.Nps = "Setup1";
                                    collection.Add(entry);
                                }
                                Tx.Commit();
                            }
                        }


                    }
                }
                using (DsdData dsdData = new DsdData())
                {

                    dsdData.SheetType = SheetType.MultiPdf;
                    dsdData.ProjectPath = rootDir;
                    dsdData.SetDsdEntryCollection(collection);
                    string dsdFile = dsdData.ProjectPath + "\\dsdData.dsd";
                    dsdData.WriteDsd(dsdFile);
                    System.IO.StreamReader sr = new System.IO.StreamReader(dsdFile);
                    string str = sr.ReadToEnd();
                    sr.Close();
                    str = str.Replace("PromptForDwfName=TRUE", "PromptForDwfName=FALSE");
                    string strToApped = "[PublishOptions]\nInitLayouts = TRUE";
                    str += strToApped;
                    System.IO.StreamWriter sw = new System.IO.StreamWriter(dsdFile);
                    sw.Write(str);
                    sw.Close();
                    dsdData.ReadDsd(dsdFile);
                    System.IO.File.Delete(dsdFile);
                    PlotConfig plotConfig = Autodesk.AutoCAD.PlottingServices.PlotConfigManager.SetCurrentConfig("DWG To PDF.pc3");
                    Autodesk.AutoCAD.Publishing.Publisher publisher = Autodesk.AutoCAD.ApplicationServices.Core.Application.Publisher;
                    try
                    {
                        publisher.PublishExecute(dsdData, plotConfig);
                    }
                    catch (Exception ex){
                      doc.Editor.WriteMessage($"Exeception: {ex.StackTrace}");
                    }
                }
            }
            
           
            //This is hack to prevent from not finding result.pdf
            foreach (var file in Directory.GetFiles(rootDir, "*.pdf"))
            {

                System.IO.FileInfo fi = new System.IO.FileInfo(file);
                // Check if file is there  
                if (fi.Exists)
                {
                    // Move file with a new name. Hence renamed.  
                    fi.MoveTo(Path.Combine(Directory.GetCurrentDirectory(), "result.pdf"));
                    break;
                }
            }         
      }

        /// <summary>
        /// The GeLayoutIds.
        /// </summary>
        /// <param name="db">The db<see cref="Database"/>.</param>
        /// <returns>The <see cref="IEnumerable{ObjectId}"/>.</returns>
        private static IEnumerable<ObjectId> GeLayoutIds(Database db)
        {
            System.Collections.Generic.List<ObjectId> layoutIds = new System.Collections.Generic.List<ObjectId>();
            using (Transaction Tx = db.TransactionManager.StartTransaction())
            {
                DBDictionary layoutDic = Tx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead, false)
                as DBDictionary;
                foreach (DBDictionaryEntry entry in layoutDic)
                {
                    layoutIds.Add(entry.Value);
                }
            }
            return layoutIds;
        }
    }
}
