# Avalonia3D / 3DEngine — v91.1 build fix

Цель: исправить compile blockers, найденные после v91 build-fix.

## Исправлено

- Добавлен `Directory.Build.props`, который исключает `PreviewerApp`, `SmokeTests` и `VSIXConnector` из default SDK compile glob основного host-проекта до вычисления `Compile` items.
- `Directory.Build.targets` переведён с проверки `MSBuildProjectName == Avalonia3D` на определение корня source-drop по наличию `3DEngine`, `PreviewerApp`, `SmokeTests`. Это работает, даже если host `.csproj` переименован.
- `RoslynDebuggerSourceExporter` больше не обращается напрямую к `DebuggerSourceExportRequest.PreviewOnly`, `DebuggerSourceExportResult.Preview(...)` и 5-аргументному `Completed(...)`. Эти элементы используются через reflection, если они есть. Это сохраняет preview-first поведение на v90-контракте и даёт compile-compatible fallback для старого design-time контракта Visual Studio.

## Проверки

В текущем окружении `dotnet build` не запускался: `dotnet` отсутствует.

Статически проверено:

- XML parse для `Directory.Build.props`, `Directory.Build.targets`, `PreviewerApp.csproj`, `SmokeTests.csproj`;
- отсутствуют прямые обращения к `request.PreviewOnly`, `DebuggerSourceExportResult.Preview`, 5-аргументному `Completed` в exporter-е;
- Roslyn NuGet PackageReference не добавлен.
