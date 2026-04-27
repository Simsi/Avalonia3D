# Integration

Keep only this import in `Avalonia3D.csproj`:

```xml
<Import Project="VSIXConnector\EngineDrop.Exclusions.targets" Condition="Exists('VSIXConnector\EngineDrop.Exclusions.targets')" />
```

Remove old analyzer imports:

```xml
<Import Project="VSIXConnector\PreviewerAnalyzer.Optional.targets" Condition="Exists('VSIXConnector\PreviewerAnalyzer.Optional.targets')" />
```

Build VSIX:

```powershell
dotnet clean .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj
dotnet restore .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj
dotnet build .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj -c Debug
```

Install:

```text
Avalonia3D\VSIXConnector\bin\Debug\ThreeDEngine.PreviewerVsix.vsix
```

If that file is not present, check:

```text
Avalonia3D\VSIXConnector\bin\Debug\net472\ThreeDEngine.PreviewerVsix.vsix
```
