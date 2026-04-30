# Avalonia3D / 3DEngine — v91.2 build-fix

Цель: закрыть compile blocker, при котором `dotnet build PreviewerApp` через `ThreeDEngineHostProject=..\Avalonia3D.csproj` заставлял host-проект компилировать `SmokeTests/Program.cs` через SDK default glob.

Изменения:

- усилен `Directory.Build.props`: исключения добавлены и в `DefaultItemExcludes`, и в `DefaultItemExcludesInProjectFolder`;
- паттерны указаны в Windows- и slash-формах;
- усилен `Directory.Build.targets`: `Compile Remove` добавлен и как top-level safety net, и как target перед `CoreCompile`;
- добавлены absolute-path remove patterns через `$(MSBuildProjectDirectory)`.

Файлы `Directory.Build.props` и `Directory.Build.targets` должны лежать в той же папке, что и `Avalonia3D.csproj`.

Проверочная команда:

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=..\Avalonia3D.csproj
```

Если ошибка `SmokeTests\Program.cs ... PreviewerApp` останется, нужно проверить, что `Directory.Build.props` и `Directory.Build.targets` действительно находятся рядом с `Avalonia3D.csproj`, а не внутри подпапки или вложенной директории после распаковки.
