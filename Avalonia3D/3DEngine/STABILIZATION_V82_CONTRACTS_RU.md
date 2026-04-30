# V82 — stabilization contracts

## What this pass stabilizes

### Change model

- `Object3D` now raises `ChangedDetailed` with `Object3DChangedEventArgs`.
- Scene-level changes preserve the concrete `SceneChangeKind` instead of collapsing most object edits to `Unknown`.
- Added explicit kinds for collider, rigidbody, picking, selection, debug visual and high-scale structure.
- `Scene3D` subscribes to detailed object changes and invalidates the registry only for changes that can actually affect traversal, picking, bounds, collision or physics.

### Renderer boundaries

- Added `RendererInvalidationKind` and `RendererInvalidationPolicy`.
- Added renderer contracts:
  - `ISceneRenderBackend`
  - `IRenderResourceCache`
  - `IDebugDrawBackend`
  - `IHighScaleRenderRuntime`
  - `RenderBackendCapabilities`
- `Scene3DControl` now routes scene changes through `RendererInvalidationPolicy` before deciding whether to sync adapters and request render.

### Debugger services

- Extracted debugger physics pumping into `DebuggerPhysicsService`.
- `Scene3DPreviewControl` no longer owns the timer/physics loop directly.

### Physics lifecycle

- Fixed the v81 array snapshot regression in `BasicPhysicsCore`.
- `BasicPhysicsCore` now snapshots registry dynamic/static lists before stepping.
- `Scene3D.StepPhysics(...)` keeps physics mutations inside a scene update scope.

### Smoke tests

Added `SmokeTests/ThreeDEngine.SmokeTests.csproj`.

The smoke tests cover:

- scene/object creation;
- detailed change kinds;
- registry invalidation policy for material vs transform;
- composite child change propagation;
- basic rigidbody gravity step;
- VSCT Alt+Q keybinding placement outside `<Commands>`.

Run from repository root:

```powershell
dotnet run --project .\SmokeTests\ThreeDEngine.SmokeTests.csproj -- .
```

## Limitations

This pass stabilizes contracts but does not complete the larger renderer refactor. Existing renderers can migrate gradually to `ISceneRenderBackend` and `RendererInvalidationPolicy`.
