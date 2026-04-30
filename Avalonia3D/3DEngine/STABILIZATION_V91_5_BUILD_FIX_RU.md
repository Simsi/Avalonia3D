# Avalonia3D / 3DEngine v91.5 build-fix

Дата: 2026-04-30

## Что исправлено

1. Source-drop utility projects больше не должны ломать host build даже если `Avalonia3D.csproj` рекурсивно компилирует `**/*.cs`.

2. `PreviewerApp/*.cs` обёрнуты в compile guard `THREE_DENGINE_PREVIEWER_APP`. Этот символ задаётся только в `PreviewerApp.csproj`. Если host-проект случайно подхватит эти файлы через globbing, они станут пустыми для host build.

3. `SmokeTests/Program.cs` обёрнут в compile guard `THREE_DENGINE_SMOKE_TESTS`. Этот символ задаётся только в `ThreeDEngine.SmokeTests.csproj`.

4. Добавлен root-level safety placeholder `RoslynDebuggerSourceExporter.cs`, чтобы затереть случайно скопированный/flattened Roslyn exporter в корне host-проекта. Реальный exporter остаётся в `PreviewerApp/RoslynDebuggerSourceExporter.cs`.

5. Усилены `DefaultItemExcludes` / `DefaultItemExcludesInProjectFolder` для `ProjectReference` и `Directory.Build.props`: добавлены `**/PreviewerApp/**/*`, `**/SmokeTests/**/*`, `**/VSIXConnector/**/*`, `**/bin/**/*`, `**/obj/**/*`, `**/RoslynDebuggerSourceExporter.cs`.

## Аудит против v80 stable

v80 stable не содержал `SmokeTests` как вложенный подпроект и не провоцировал SDK-style host globbing. В v90/v91 появились nested utility projects, поэтому host `Avalonia3D.csproj` начал компилировать чужие `.cs` файлы при сборке через `ProjectReference` из PreviewerApp.

## Проверки в этом окружении

`dotnet build` не запускался: dotnet/MSBuild недоступны. Выполнены статические проверки XML и баланс compile guards.
