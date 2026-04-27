# HighScale Runtime v18

This drop extends the v17 high-scale renderer foundation with working runtime mechanics for large scenes.

Implemented mechanisms:

- Dense instance storage with transform/material/visibility versions and dirty queues.
- Spatial chunk index for high-scale layers.
- Semantic LOD policy: Detailed, Simplified, Proxy and Billboard levels.
- Automatic proxy LOD generated from composite local bounds.
- Per-template material variants.
- High-scale telemetry batch API for bulk status/material/visibility/transform changes.
- Scene registry spatial broadphase indexes for picking and collider raycasts.
- BasicPhysicsCore now queries collider broadphase instead of scanning all static colliders for every dynamic body.
- OpenGL renderer no longer expands high-scale layers into the normal Object3D batch. It renders high-scale layers through chunk/part/lod instance buffers.
- WebGL packet builder now uses chunk and LOD filtering for high-scale layers before serializing the frame packet.
- Performance counters expanded with chunk, LOD and instance-buffer metrics.

Remaining runtime validation required in a real .NET/Avalonia environment:

- C# compile and VSIX packaging.
- Visual validation of high-scale LOD transitions.
- Benchmark scenes for 10k / 100k / 1M simple instances.
- Browser profiling for JSON packet size. WebGL is now chunked/LOD-aware but should still move to a typed-array retained transport for maximum scale.
