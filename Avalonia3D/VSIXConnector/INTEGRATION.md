# Integration notes

Import `VSIXConnector\EngineDrop.Exclusions.targets` from the main host csproj if PreviewerApp and VSIXConnector are stored inside the same project folder as the source-drop engine.

Example:

```xml
<Import Project="VSIXConnector\EngineDrop.Exclusions.targets" Condition="Exists('VSIXConnector\EngineDrop.Exclusions.targets')" />
```

This prevents the main Avalonia app from compiling PreviewerApp and VSIXConnector source files as normal application source.

Build VSIX separately:

```powershell
cd .\VSIXConnector
dotnet build .\ThreeDEngine.PreviewerVsix.csproj -c Debug
```

Manual PreviewerApp build when host project name differs:

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=.\MyHostProject.csproj
```
