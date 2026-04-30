# Avalonia3D / 3DEngine — v91.3 build-fix

Дата: 2026-04-30

## Цель

Закрыть оставшиеся compile blockers после v91.2:

- host `Avalonia3D.csproj` при CLI build продолжал компилировать вложенный `SmokeTests/Program.cs`;
- host также захватывал generated `obj/**/*.cs`, что давало duplicate assembly attributes;
- sample/host code использовал `SceneFrameRenderedEventArgs.Kind`, а контракт уже переименован на `Backend`.

## Изменения

- `Directory.Build.props` теперь дополнительно исключает `bin/**` и `obj/**`.
- `Directory.Build.targets` фильтрует фактический список `@(Compile)` по `%(Compile.FullPath)` перед `CoreCompile/Csc`. Это покрывает старые host-проекты с явным `Compile Include="**/*.cs"`.
- `SceneFrameRenderedEventArgs` получил backward-compatible alias `Kind => Backend`.

## Проверки

В этом окружении `dotnet build` не запускался: нет dotnet/MSBuild.

Статически проверено:

- XML parse `Directory.Build.props` / `Directory.Build.targets`;
- баланс скобок в `SceneFrameRenderedEventArgs.cs`;
- отсутствие NuGet `Microsoft.CodeAnalysis.CSharp`;
- сохранён preview-first Roslyn/offline подход.

## Команды для проверки

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=..\Avalonia3D.csproj
dotnet run --project .\SmokeTests\ThreeDEngine.SmokeTests.csproj -- .
```

Если CLI build всё ещё компилирует `SmokeTests/Program.cs`, значит host project содержит очень поздний/custom `Compile Include`. Тогда исключения нужно добавить прямо в `Avalonia3D.csproj`.
