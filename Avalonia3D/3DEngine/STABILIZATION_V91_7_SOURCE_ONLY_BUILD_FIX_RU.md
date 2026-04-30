# Avalonia3D / 3DEngine — v91.7 source-only build fix

Цель: исправить compile blockers в source-drop без изменения корневого проекта Avalonia3D.

Исправлено:

- `Scene3DPreviewControl.ApplyRigidbodyInspector(...)` больше не обращается к несуществующей локальной переменной `scene`; после изменения rigidbody вызывается `_viewport.Scene.Invalidate()`.
- Возвращён helper `RefreshSelectionAfterPhysics(Object3D?)`, который нужен `DebuggerPhysicsService` для безопасного обновления инспектора после physics step.

Граница зависимостей:

- `3DEngine` не содержит compile-time ссылок на namespace `PreviewerApp`.
- Roslyn exporter остаётся в `PreviewerApp`; engine принимает только абстрактный handler `Func<DebuggerSourceExportRequest, Task<DebuggerSourceExportResult>>`.

Проверки в текущем окружении:

- `dotnet build` не запускался: нет dotnet/MSBuild.
- Статически проверены XML-файлы `.csproj`/`.vsct`, отсутствие `using PreviewerApp` в `.cs` файлах `3DEngine`, наличие `RefreshSelectionAfterPhysics`, отсутствие `scene.Invalidate()` в `ApplyRigidbodyInspector`.
