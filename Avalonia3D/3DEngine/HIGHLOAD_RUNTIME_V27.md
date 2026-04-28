# Highload Runtime v27

Built from `3DEngine_Avalonia3D_benchmark_runtime_v25_no_legacy.zip`.

## CPU/GPU pipeline changes

- Added explicit FPS lock control to `Scene3DControl`:
  - `FpsLockEnabled`
  - `TargetFps`
  - `UnlockedMaxFps`
- Continuous rendering now supports a locked timer path and an unlocked render-after-frame path.
- Added runtime frame/GC allocation metrics to `RenderStats`.
- Added high-scale adaptive caps through `ScenePerformanceOptions`:
  - `QualityScale`
  - `MaxVisibleHighScaleChunks`
  - `MaxHighScaleVisibleInstances`
  - `AllocationBudgetMegabytesPerSecond`
- OpenGL high-scale rendering now respects adaptive chunk and instance budgets.
- OpenGL normal-object rendering can sample interpolated transforms from `Scene3D.FrameInterpolator`.
- Resource sweep no longer uses `Dictionary.ToArray()` allocations.

## Frame interpolation

Added `FrameInterpolator3D` and `Scene3D.BeginSimulationTick()` / `Scene3D.EndSimulationTick()`.
This is classic transform interpolation between simulation/telemetry ticks. It is not synthetic GPU frame generation and cannot bypass the Avalonia/OpenGL presentation rate.
It does not synthesize real frames or use motion-vector reconstruction.

## Adaptive performance

Added `AdaptivePerformanceController3D`. It watches frame time, backend time, high-scale planning/build cost, allocation rate, and Gen2 collections. Under pressure it lowers `QualityScale`, draw distance, max visible high-scale chunks, and max visible high-scale instances. When stable, it slowly restores quality.

## Telemetry pipeline

Added `TelemetryDiffQueue3D` and `InstanceStateBuffer3D`. The updated benchmark `MainView.axaml.cs` uses the diff queue so telemetry is coalesced and drained per rendered frame, instead of applying one large batch every dispatcher tick.
