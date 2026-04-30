# V90 — hardening update

## Included fixes

- Added missing `Scene3DControl._pendingRendererInvalidation` field.
- Rolled back `ScenePerformanceOptions.ForceWebGlJsOwnedHighScaleRuntime` default to `false`.
- Replaced the silent `OpenGlSceneRenderer.ISceneRenderBackend.Render(...)` stub with an explicit `NotSupportedException`.
- Expanded smoke tests:
  - renderer invalidation policy mapping;
  - safe default for JS-owned WebGL runtime;
  - Roslyn source export preview-only flow.
- Started real `Scene3DPreviewControl` decomposition:
  - `Scene3DPreviewControl.SourceExport.cs`;
  - `Scene3DPreviewControl.Inspector.cs`;
  - `Scene3DPreviewControl.Layout.cs`.
- Added `BuildAndSmoke.ps1`.

## Notes

`ForceWebGlJsOwnedHighScaleRuntime` remains explicitly enabled in the `ExtremeScale` profile as an opt-in benchmark/runtime profile, but default balanced scenes no longer force JS-owned mixed rendering.
