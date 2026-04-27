# PreviewerApp

Standalone Avalonia process for 3D previews.

Expected location after copying into the existing application project:

```text
Avalonia3D/Avalonia3D/
  3DEngine/
  PreviewerApp/
  VSIXConnector/
  Views/
  Avalonia3D.csproj
```

Run from `Avalonia3D/Avalonia3D` after building the host project:

```powershell
dotnet run --project .\PreviewerApp\PreviewerApp.csproj -- --assembly .\bin\Debug\net8.0\Avalonia3D.dll --type Avalonia3D.Views.DemoServerRack3D
```

Arguments:

```text
--assembly <path-to-target-dll>
--type <optional-full-type-name>
--project <optional-project-path>
```
