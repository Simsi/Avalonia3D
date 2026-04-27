# 3DEngine benchmark instrumentation v19

This drop adds the public `Scene3DControl.FrameRendered` event and extends the built-in performance overlay so application-level benchmark views can record frame samples without parsing UI text.

Use `MainView_benchmark.axaml.cs` as a replacement for `Avalonia3D/Views/MainView.axaml.cs` to run configurable stress tests.

CSV output defaults to:

`Documents/Avalonia3D/Benchmarks/benchmark_yyyyMMdd_HHmmss.csv`

Recommended progression:

1. 10k simple instances, 30 seconds.
2. 100k simple instances, 60 seconds.
3. 1M simple instances, 60 seconds.
4. 100k simple + 10k interactive proxies.
5. 10k rack composite, telemetry 50k/s.
6. 100k rack composite, telemetry 100k/s.

Key fields: fps, avg_frame_ms, alloc_mb_s, highscale_instances, total_chunks, visible_chunks, draw_calls, batches, triangles, instance_upload_mb, backend_ms, packet_ms, serialize_ms, upload_ms, picking_ms, physics_ms, live_ms.
