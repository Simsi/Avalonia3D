# PreviewerApp

Desktop Avalonia previewer for 3DEngine source-drop projects.

Manual run:

```powershell
dotnet build .\Avalonia3D.csproj -c Debug
dotnet run --project .\PreviewerApp\PreviewerApp.csproj -c Debug -- --assembly .\bin\Debug\net8.0\Avalonia3D.dll --type MyNamespace.MyControl3D
```

If the host project is not named `Avalonia3D.csproj`:

```powershell
dotnet run --project .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=.\MyHostProject.csproj -- --assembly .\bin\Debug\net8.0\MyHostProject.dll --type MyNamespace.MyControl3D
```

Supported previews:

- `CompositeObject3D` with public parameterless constructor.
- `[Preview3D] public static Object3D Preview()`.
- `[Preview3D] public static Scene3D PreviewScene()`.
- `[Preview3D] public static IEnumerable<PreviewScene3D> Previews()`.

The Visual Studio connector builds and launches this app automatically.
