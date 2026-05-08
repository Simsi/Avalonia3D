# 3DEngine Previewer VSIX Connector

Adds `Open 3D Preview` to Visual Studio.

Build:

```powershell
dotnet build .\ThreeDEngine.PreviewerVsix.csproj -c Debug
```

If restore fails because `https://api.nuget.org/v3/index.json` is unavailable, see `BUILD_OFFLINE_RU.md`.

Install the generated package:

```text
VSIXConnector\bin\Debug\ThreeDEngine.PreviewerVsix.vsix
```

Expected project layout:

```text
Avalonia3D.csproj or another host csproj
3DEngine\
PreviewerApp\PreviewerApp.csproj
VSIXConnector\ThreeDEngine.PreviewerVsix.csproj
```

The connector detects the active C# class under the caret, builds the host project, builds PreviewerApp with `ThreeDEngineHostProject=<host-csproj>`, and launches the previewer with `--assembly` and `--type`.


## v44 install note

Ручная упаковка через `BuildVsix.ps1` отключена. Устанавливай `.vsix`, созданный VSSDK BuildTools, из `bin\Debug\net472`. Подробности: `INSTALL_VSIX_RU.md`.

## Security note

The VSIX connector invokes `dotnet build` and launches the PreviewerApp for the active project. This is intended for trusted local development workspaces only. Opening previews from unknown repositories can execute arbitrary project code.
