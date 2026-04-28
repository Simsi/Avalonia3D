# Benchmark optimization pass v23

This pass is based on the first benchmark CSV results.

## Main findings from the benchmark

The engine was CPU/upload-bound, not GPU-bound. GPU load around 70-76% meant that the GPU was not continuously fed with work. The biggest measured symptoms were:

- `highscale_instances` and LOD counters were not correctly reported in the OpenGL path.
- high-scale chunks stayed dirty after upload, so instance buffers were re-uploaded repeatedly even for static frames.
- high-scale batch invalidation used the global `InstanceStore3D.Version`, so a small telemetry update could invalidate all visible batches.
- default `Simplified` LOD was identical to `Detailed`, so rack composites remained expensive at medium/far distances.
- objects beyond `ProxyDistance` were resolved as `Billboard` and then skipped by the renderer, which caused abrupt draw-distance popping.
- the benchmark did not expose draw distance / fade distance controls.

## Implemented changes

- Added `HighScaleLodLevel3D.Culled`.
- Added `HighScaleLodPolicy3D.DrawDistance`, `FadeDistance`, `Version`, and fade alpha resolution.
- Changed default `CompositeTemplate3D.Simplified` to a real one-part simplified bounds model instead of full detailed parts.
- Renderer now treats `Billboard` as proxy geometry when no billboard pass is available, avoiding hard disappearance at `ProxyDistance`.
- OpenGL high-scale batch cache no longer depends on global `InstanceStore3D.Version`.
- High-scale chunks are marked clean after upload; static frames should now show near-zero `InstanceUploadBytes` after warm-up.
- Distance fade uses dithered opaque discard in the mesh shader, avoiding expensive transparent sorting.
- Added `ScenePerformanceOptions.DrawDistance`, `DistanceFadeBand`, `EnableDistanceFade`, and `ChunkCullingMargin`.
- Added benchmark UI controls for draw distance and fade band.
- Extended CSV and overlay counters with detailed/simplified/proxy/billboard/culled LOD and visible part instance count.
- WebGL path now respects draw distance, LOD culling, and proxy fallback for billboard level.

## Expected benchmark deltas

- Static high-scale scenes should stop uploading large instance buffers every frame after the first warm-up frame.
- 10k / 100k rack scenarios should submit far fewer parts because medium/far racks now use one simplified/proxy part.
- Far objects should no longer disappear at `ProxyDistance`; they should remain as proxy/billboard-proxy until `DrawDistance`.
- GPU usage may increase because CPU/driver upload stalls should be reduced. The goal is still stable frame time, not 100% GPU load.

## Still not fully solved

- True per-instance dirty-range `glBufferSubData` is still not implemented. Dirty chunks are rebuilt as chunks, not as individual sparse ranges.
- WebGL still serializes frame packets as JSON. For the browser contract, binary retained buffers are still the next major step.
- 10k interactive proxies are still normal scene objects in the benchmark. A compact proxy store remains the correct production solution.
