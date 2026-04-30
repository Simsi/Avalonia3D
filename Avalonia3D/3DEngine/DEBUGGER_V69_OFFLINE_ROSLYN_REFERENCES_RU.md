# Debugger v69 — Roslyn export without mandatory NuGet restore

Проблема v67/v68: `PreviewerApp` получил `PackageReference` на `Microsoft.CodeAnalysis.CSharp`. В среде без доступа к `api.nuget.org` это ломало `dotnet restore/build`, хотя Roslyn assemblies уже обычно есть в установленном .NET SDK / MSBuild.

## Что изменено

`PreviewerApp.csproj` теперь по умолчанию использует offline-friendly режим:

- `Microsoft.CodeAnalysis.dll`
- `Microsoft.CodeAnalysis.CSharp.dll`

берутся из:

- `$(RoslynAssembliesPath)`, если свойство задано SDK/MSBuild;
- иначе из `$(MSBuildToolsPath)\Roslyn\bincore`.

NuGet-пакет включается только явно:

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -p:ThreeDEngineUseRoslynNuGet=true
```

## Если Roslyn assemblies лежат в другом месте

Можно передать путь вручную:

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=..\Avalonia3D.csproj -p:ThreeDEngineRoslynPath="C:\Program Files\dotnet\sdk\8.0.xxx\Roslyn\bincore"
```

## Примечание

Avalonia package references остались как были. Если они ещё не восстановлены в локальный NuGet cache, первичный restore всё равно потребует доступ к feed или локальный package source. Эта правка убирает именно новую Roslyn-зависимость, появившуюся в v67/v68.
