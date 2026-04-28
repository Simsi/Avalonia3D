# Benchmark Runtime v24

This drop targets the bottlenecks found in the second benchmark pass where GPU usage stayed low while FPS was still below the agreed highload contract.

## Main changes

- Added `Scene3DControl.ContinuousRendering` and `ContinuousRenderingFps` so benchmark runs can drive the backend every frame instead of relying only on scene dirty invalidation.
- Continuous rendering bypasses live-control snapshot refresh and only requests presenter frames, avoiding unnecessary UI snapshot work in stress tests.
- OpenGL high-scale rendering now builds a per-chunk LOD frame plan once per chunk. The previous path re-resolved LOD by scanning the same chunk for every `LOD x part` batch.
- High-scale stats now split plan, CPU buffer build, and GPU upload time: `HighScalePlanMilliseconds`, `HighScaleBufferBuildMilliseconds`, `HighScaleUploadMilliseconds`.
- High-scale retained batches now include a quantized camera LOD key so static camera frames stay retained, while camera movement correctly rebuilds LOD/fade batches.
- Benchmark `MainView` now defaults to continuous render and compact high-scale proxy visuals. The old full `Object3D` proxy path is still available by disabling `Compact high-scale proxies`.
- CSV includes continuous/proxy mode flags and high-scale timing columns.

## Expected benchmark difference

The next run should distinguish three separate bottlenecks:

- low GPU usage because render-on-demand was not feeding frames;
- CPU chunk/LOD planning cost;
- CPU/GPU upload cost.

For static scenes with continuous render and compact proxies, `instance_upload_mb` should be near zero after warm-up. If FPS is still low while upload is zero, the remaining issue is CPU planning/build or Avalonia/OpenGL scheduling rather than buffer transfer.
