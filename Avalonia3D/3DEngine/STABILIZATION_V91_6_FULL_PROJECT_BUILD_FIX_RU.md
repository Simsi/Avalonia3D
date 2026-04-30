# Avalonia3D / 3DEngine — v91.6 full project build-fix

База: пользовательский восстановленный `Avalonia3D.zip`, который запускался локально.

Что исправлено:

- актуальный v91.5 source-drop перенесён в структуру полного Avalonia-проекта;
- `MainView.axaml` и `MainView.axaml.cs` положены в `Avalonia3D/Views`, а не в корень;
- удалены ошибочные дубликаты PreviewerApp-файлов из корня `Avalonia3D/`;
- `PreviewerApp`, `SmokeTests`, `VSIXConnector`, `bin`, `obj` жёстко исключены из компиляции host-проекта;
- host `Avalonia3D.csproj` больше не требует NuGet `Microsoft.CodeAnalysis.CSharp`;
- Roslyn остаётся только в `PreviewerApp`/`SmokeTests` и ищется offline из Visual Studio/.NET SDK;
- сохранён `SceneFrameRenderedEventArgs.Kind => Backend` для совместимости host-кода;
- сохранён default `ForceWebGlJsOwnedHighScaleRuntime = false`.

Проверки в окружении ассистента: `dotnet build` не запускался, потому что .NET SDK/MSBuild недоступны. Выполнены статические проверки структуры, XML и наличия ключевых guard-ов.

Дополнительно: `PreviewerApp/RoslynDebuggerSourceExporter.cs` компилируется и в `PreviewerApp`, и в `SmokeTests` через guard `THREE_DENGINE_PREVIEWER_APP || THREE_DENGINE_SMOKE_TESTS`.
