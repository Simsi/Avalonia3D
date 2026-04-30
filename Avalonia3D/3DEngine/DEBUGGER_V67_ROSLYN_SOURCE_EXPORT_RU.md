# Debugger v67 — Roslyn source export

v67 заменяет прежний текстовый experimental source patcher на Roslyn-backed exporter.

## Что изменено

- `Scene3DPreviewControl` больше не правит C#-файл строковыми индексами и поиском скобок.
- Контрол формирует `DebuggerSourceExportRequest`: целевой файл, класс, сгенерированный `Build(...)` и fallback-класс.
- `PreviewerApp` подключает `RoslynDebuggerSourceExporter.ExportAsync` через `SetSourceExportHandler(...)`.
- Roslyn exporter парсит исходник через `CSharpSyntaxTree`, находит `ClassDeclarationSyntax`, затем:
  - если найден `Build(...)`, заменяет только `MethodDeclarationSyntax`;
  - если `Build(...)` нет, заменяет весь `ClassDeclarationSyntax`;
  - перед записью создаёт `<file>.3ddebugger.bak`.

## Почему так

String-based patching опасен: комментарии, строки, вложенные классы, file-scoped namespace, атрибуты и partial-классы легко ломают поиск фигурных скобок. Roslyn работает с syntax tree и заменяет конкретные узлы дерева, поэтому операция стала более предсказуемой.

## Где находится Roslyn-зависимость

Roslyn package добавлен только в `PreviewerApp`, а не в core/source-drop часть движка. Это сделано намеренно: основной 3DEngine-код не должен тянуть `Microsoft.CodeAnalysis.CSharp` в пользовательский runtime-проект.

Файлы:

- `PreviewerApp/RoslynDebuggerSourceExporter.cs`
- `PreviewerApp/PreviewerApp.csproj`
- `PreviewerApp/PreviewerWindow.cs`
- `3DEngine/Avalonia/Preview/DebuggerSourceExportRequest.cs`
- `3DEngine/Avalonia/Preview/Scene3DPreviewControl.cs`

## Ограничения

- Экспорт пока целится в class declaration, а не record/class hybrids.
- Если в файле несколько классов с одинаковым short name, exporter старается выбрать exact full type name, потом ближайший class span.
- VSIX-side patch preview ещё не реализован; запись идёт напрямую после подтверждения в debugger UI.
