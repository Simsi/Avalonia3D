# 3DEngine Previewer VSIX Connector

This is a Visual Studio extension project that adds an `Open 3D Preview` command.

Build:

```powershell
dotnet clean .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj
dotnet restore .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj
dotnet build .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj -c Debug
```

The project also runs `BuildVsix.ps1` after build, because SDK-style VSIX projects do not always emit a `.vsix` with `dotnet build` on every Visual Studio/MSBuild setup.

Expected outputs:

```text
Avalonia3D\VSIXConnector\bin\Debug\net472\ThreeDEngine.PreviewerVsix.vsix
Avalonia3D\VSIXConnector\bin\Debug\ThreeDEngine.PreviewerVsix.vsix
```

Install the `.vsix`, then restart Visual Studio.
