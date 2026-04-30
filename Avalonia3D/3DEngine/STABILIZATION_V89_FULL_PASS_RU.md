# V89 — stabilization + runtime hardening pass

Этот срез объединяет следующие направления из аудита: v84 smoke/change-model, v85 renderer invalidation, v86 debugger services, v87 physics lifecycle, v88 JS-owned WebGL runtime и v89 safe Roslyn export.

## v84 — smoke tests and change-model enforcement

Расширены smoke tests:

- material-only update не перестраивает registry;
- transform update перестраивает registry;
- picking update поднимает `SceneChangeKind.Picking`;
- composite child transform прокидывается как `Transform`;
- high-scale `AddInstance` поднимает `HighScaleStructure`;
- high-scale material/state update поднимает `HighScaleState` и не увеличивает `StructureVersion`;
- fixed-step physics lifecycle делает несколько шагов при накопленном delta;
- VSCT проверяет, что Alt+Q keybinding находится вне `<Commands>`.

Добавлен `BuildAndSmoke.ps1`.

## v85 — renderer invalidation connected to backends

- `IScenePresenter` получил `NotifySceneChanged(...)`.
- `Scene3DControl` накапливает `RendererInvalidationKind` и передаёт его presenter-у.
- OpenGL presenter/renderer принимают invalidation mask.
- WebGL presenter принимает invalidation mask и сбрасывает mesh/texture upload caches при resource/high-scale structural changes.
- `RenderStats` получил `RendererInvalidation` и `FullRebuildReason`.

## v86 — debugger services

- Добавлен `DebuggerSelectionService`.
- `DebuggerPhysicsService` остаётся отдельным lifecycle-сервисом для debugger physics pump.
- `Scene3DPreviewControl` начал отдавать selection state в сервис, уменьшая прямое сцепление inspector/list/viewport.

Это ещё не полная декомпозиция 138 KB control-а, но уже отделяет два критичных lifecycle-сервиса.

## v87 — physics lifecycle

- Добавлены `PhysicsSimulationMode` и `PhysicsSimulationSettings`.
- `Scene3D` получил `AdvancePhysics(...)`, fixed-step accumulator и `ResetPhysicsAccumulator()`.
- Debugger physics использует `AdvancePhysics(...)`, а не прямой свободный `StepPhysics(...)`.

## v88 — JS-owned WebGL runtime contract

- Добавлен `ForceWebGlJsOwnedHighScaleRuntime` в `ScenePerformanceOptions`.
- WebGL high-scale path теперь явно считает JS-owned runtime запрошенным и пишет fallback reason в `RenderStats.WebGlFallbackReason`.
- `WebGlClientHighScaleRenderer` получил `InvalidateStructure()` и флаг forced structural rebuild.

## v89 — safe Roslyn diff/export workflow

- `DebuggerSourceExportRequest` получил `PreviewOnly`.
- `DebuggerSourceExportResult` получил `DiffPreview`.
- Roslyn exporter умеет preview-only режим без записи файлов.
- Debugger сначала генерирует diff preview, показывает его в confirmation dialog, и только после подтверждения делает backup + запись.

## Ограничения

Полная декомпозиция debugger-а и полный перенос WebGL non-high-scale renderables в JS-owned retained runtime остаются следующими этапами. В этом срезе закреплены контракты и lifecycle hooks, чтобы эти изменения можно было внедрять без очередного монолита.
