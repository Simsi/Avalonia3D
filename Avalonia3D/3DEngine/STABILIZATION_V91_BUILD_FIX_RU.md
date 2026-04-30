# Avalonia3D / 3DEngine — v91 build-fix

Дата: 2026-04-30
База: v90 hardening

## Что исправлено

1. `PreviewerApp.csproj` теперь ищет Roslyn assemblies не только в layout .NET SDK
   `Roslyn\bincore`, но и в Visual Studio MSBuild layout `Roslyn`.

   Добавлены fallback paths:

   - `$(RoslynAssembliesPath)`;
   - `$(CscToolPath)`;
   - `$(MSBuildToolsPath)\Roslyn\bincore`;
   - `$(MSBuildToolsPath)\Roslyn`;
   - `$(MSBuildToolsPath)\..\Roslyn`;
   - `$(VsInstallRoot)MSBuild\Current\Bin\Roslyn`;
   - `$(VsInstallRoot)MSBuild\Current\Bin\amd64\Roslyn`.

   NuGet `Microsoft.CodeAnalysis.CSharp` по-прежнему не добавляется.

2. `SmokeTests/ThreeDEngine.SmokeTests.csproj` получил тот же offline Roslyn fallback.

3. Добавлен `Directory.Build.targets`, который не даёт основному `Avalonia3D.csproj`
   подхватывать исходники tool/test/VSIX проектов через SDK `**/*.cs` globbing:

   - `PreviewerApp/**/*.cs`;
   - `SmokeTests/**/*.cs`;
   - `VSIXConnector/**/*.cs`.

   Это исправляет ошибку, когда `SmokeTests/Program.cs` компилировался внутрь проекта
   `Avalonia3D` и падал на `using PreviewerApp`.

4. В `BuildAndSmoke.ps1` default `HostProject` заменён на `..\Avalonia3D.csproj`, потому
   что значение передаётся в `PreviewerApp.csproj` как `ProjectReference` и разрешается
   относительно папки `PreviewerApp`.

## Проверки

В этом окружении `dotnet build` не запускался: `dotnet/MSBuild` недоступен.

Статически проверено:

- XML parse для изменённых `.csproj` и `Directory.Build.targets`;
- наличие новых Roslyn fallback paths;
- отсутствие NuGet `PackageReference` на `Microsoft.CodeAnalysis.CSharp`;
- наличие exclude rules для `PreviewerApp`, `SmokeTests`, `VSIXConnector` в host-проекте.

## Что запускать локально

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=..\Avalonia3D.csproj
dotnet run --project .\SmokeTests\ThreeDEngine.SmokeTests.csproj -- .
```

или:

```powershell
.\BuildAndSmoke.ps1 -Configuration Debug
```

Если Visual Studio всё ещё не найдёт Roslyn assemblies, передать путь явно, например:

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=..\Avalonia3D.csproj -p:ThreeDEngineRoslynPath="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn"
```
