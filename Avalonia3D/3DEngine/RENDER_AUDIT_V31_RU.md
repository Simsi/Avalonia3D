# Render audit v31: high-scale telemetry, FPS cap, frame interpolation

## Benchmark findings

Uploaded benchmark set contains 22 CSV files. It mostly covers 10k rack-composite runs; no 50k CSV samples were present in this archive.

Key observed patterns:

- Small or simplified scenes remain capped at ~60 FPS even with `fps_locked=0`.
- Full detailed 10k rack scenes show 25-40 FPS with periodic stalls.
- `hs_build_ms` often reaches 13-24 ms under telemetry.
- `hs_upload_ms` can reach 14-17 ms under telemetry.
- `state_upload_kb` often reaches ~1.7 MB for 10k detailed racks.
- Telemetry target 50k/s often applies only ~24-35k/s when actual FPS drops to ~30 FPS.

## Root cause 1: global dirty count forced full state upload per batch

`InstanceStateBuffer3D.DirtyIndices` is a layer-global list. `OpenGlSceneRenderer.UpdateHighScaleStateBuffer` compared that global count against each chunk/LOD batch size. With 50k updates/sec and ~800 dirty logical instances per 60Hz tick, almost every visible batch decided to full-upload state, even when the batch contained only a small subset of those dirty instances.

Fix: collect dirty offsets that actually belong to the current batch first, then decide partial-vs-full update per batch.

## Root cause 2: telemetry auto budget used target FPS, not actual FPS

The benchmark drained `configuredTelemetryPerSecond / targetFps`. When the scene drops to 30 FPS, 50k/s becomes only ~25k applied/s. That makes the visual result look like only some objects update.

Fix: auto budget uses measured FPS when available, and performs limited catch-up when queue backlog exists.

## Root cause 3: telemetry queue drained LIFO

`TelemetryDiffQueue3D` previously popped dirty indices from the end of the list. Under sustained over-budget input this can bias updates toward recently enqueued indices and delay older ones.

Fix: drain FIFO with compaction. Latest update still wins per instance index.

## Root cause 4: 60 FPS cap is presentation-bound

The current desktop backend is `Avalonia.OpenGL.Controls.OpenGlControlBase`. `RequestNextFrameRendering()` is paced by Avalonia's render/compositor path. Posting another render request after each frame does not bypass monitor/compositor/VSync pacing. Therefore `FpsLockEnabled=false` currently means "do not use the engine's timer lock", not "render above Avalonia compositor refresh".

A real >60 FPS benchmark requires a separate native presenter/render loop, or a platform API path that exposes swap interval and is not paced by Avalonia composition.

## Root cause 5: current framegen is not real frame generation

The current `FrameInterpolator3D` is classical simulation interpolation. It only helps when simulation ticks are slower than render ticks and there is something interpolatable. It does not synthesize new presented frames above the presenter cap. Also, high-scale rendering still reads `record.Transform` directly, so high-scale rack transforms are not currently interpolated.

Next step: rename this feature to interpolation, then implement a separate temporal presentation architecture only if a >60 FPS presenter exists.

## Remaining render bottlenecks after this fix

- Per-frame LOD planning still scans visible chunk instances.
- Detailed rack LOD still draws every template part separately, so 10k racks can become ~110k visible part instances.
- `HighScaleGpuBatchData` still maintains a dictionary `instanceIndex -> batch offset` per batch; this is acceptable as a short fix but should become a compact offset map or sorted range map.
- True retained layer-level transform/state buffers are still not implemented. The renderer still stores GPU buffers per chunk/LOD batch.
- No OpenGL timer queries yet; `backend_ms` is CPU-side time.
