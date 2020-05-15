# Design Automation for AutoCAD Application

[![Data-Management](https://img.shields.io/badge/Data%20Management-v1-green.svg)](http://developer.autodesk.com/)
[![Design-Automation](https://img.shields.io/badge/Design%20Automation-v3-green.svg)](http://developer.autodesk.com/)
[![netCore](https://img.shields.io/badge/netcore-3.1-green)](https://dotnet.microsoft.com/download/dotnet-core/current/runtime)
### Description
A CLI utility based on .NET Core technology to print a Revit generated multiple view drawings  in to a single pdf.

Uses [Forge Design Automation V3](https://forge.autodesk.com/en/docs/design-automation/v3)

### Prerequisites
1. **Forge Account**: Learn how to create a Forge Account, activate subscription and create an app at [this tutorial](http://learnforge.autodesk.io/#/account/). 
2. **Visual Code**: Visual Code (Windows or MacOS)
3. **.netcore 3.1**: [dotnet core SDK](https://dotnet.microsoft.com/download/dotnet-core/current/runtime) 
4. **7z** [7z Zip Archive](https://www.7-zip.org/download.html)
5. Make sure `7z.exe` is available on your system path env.

`git clone https://git.autodesk.com/moogalm/da_publishdsd.git`

### Instructions To Build AutoCAD Addin
```bash
cd BatchPublishCommand
msbuild BatchPublishCommand.csproj -property:Configuration=Debug;Platform=x64
```

### Instructions To Build .NET Core

```bash
cd BatchPublishCommand\client
notepad appsettings.users.json `feed with your Forge Credentials`
dotnet build
dotnet run -c RELEASE -i "<inputZipFileWithDrawings>" -o "<outputFolder>"
```
`appsettings.users.json`

```
{
  "Forge": {
    "ClientId": "ForgeClientId",
    "ClientSecret": "ForgeClientSecret"
  }
}
```

`launchsettings.json`

```
{
  "profiles": {
    "ClientV3": {
      "commandName": "Project",
      "commandLineArgs": "-i \"C:\\Users\\moogalm\\Downloads\\exported.zip\" -o D:\\Work\\Arxprojects\\2020\\BatchPublishCommand\\output",
      "workingDirectory": "..\\bin\\Release\\netcoreapp3.1",
      "environmentVariables": {
        "FORGE_CLIENT_SECRET": "ForgeClientId",
        "FORGE_CLIENT_ID": "ForgeClientSecret"
      }
    }
  }
}
```




#### Instructions To Debug

##### .NETCore
```
edit launchsettings.json
launch ClientV3 profile.

```
#### AutoCAD Addin

`edit .csproj.user`

```

<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|x64'">
    <StartArguments>/s test.scr</StartArguments>
    <StartAction>Program</StartAction>
    <StartProgram>C:\Program Files\Autodesk\AutoCAD 2020\accoreconsole.exe</StartProgram>
  </PropertyGroup>
</Project>
```





### License
This sample is licensed under the terms of the [MIT License](http://opensource.org/licenses/MIT). Please see the [LICENSE](LICENSE) file for full details.

### Written by
Madhukar Moogala, [Forge Partner Development](http://forge.autodesk.com)  @galakar


