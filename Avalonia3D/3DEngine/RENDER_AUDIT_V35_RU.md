# Render audit v35: rollback aggregate path

v34 aggregate layer batching is disabled by default.

Reason: the test result (about 30 FPS, GPU fluctuating around 55%) indicates that reducing draw calls by rendering one layer-wide retained batch per LOD was the wrong tradeoff for the current rack scene. The path can lose chunk/frustum locality, scan the full layer every frame, and rebuild/upload large LOD membership buffers when the camera or LOD distribution changes. In practice this starved the GPU instead of stabilizing it.

v35 keeps the useful non-aggregate changes from v34:

- synthetic benchmark telemetry is generated from the rendered-frame callback instead of an independent DispatcherTimer;
- continuous high-scale state changes do not spam RequestNextFrameRendering;
- v33 telemetry pacing and state-buffer fixes remain active.

For the 10k smooth target, use the chunk retained path. The next safe optimization should not be layer-wide aggregate batching. It should be either:

1. baked rack detailed mesh: merge the 11 rack parts into one mesh/template draw where possible;
2. chunk-local multi-part packing: keep chunk culling but reduce per-part uniforms/binds;
3. camera-move warmup/prefetch for chunks near the frustum;
4. fixed-rate telemetry visual diffusion, never catch-up waves.

Expected immediate result: performance should return close to v33 behavior while retaining the frame-paced benchmark telemetry generator from v34.
