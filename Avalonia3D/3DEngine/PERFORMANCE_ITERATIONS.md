# 3DEngine performance stabilization iterations

## Iteration 1: hot-path audit and scene traversal

Findings:
- Rendering, WebGL upload, picking and physics repeatedly traversed CompositeObject3D trees.
- Scene changes were untyped and forced broad synchronization work.
- Large telemetry batches would create repeated invalidations.

Changes:
- Added SceneObjectRegistry with cached flat lists for all objects, renderables, pickables, colliders, dynamic bodies and static colliders.
- Added SceneChangeKind and SceneChangedEventArgs while keeping the old SceneChanged event for compatibility.
- Added Scene3D.BeginUpdate() to coalesce many mutations into one notification.
- Updated render, physics, collision, picking, preview and control synchronization to use registry hot paths.

## Iteration 2: geometry/resource caching and per-frame allocation reduction

Findings:
- Identical primitives generated duplicate Mesh3D instances and GPU buffers.
- OpenGL keyed GPU mesh resources by Object3D.Id, preventing sharing.
- Matrix uploads and control-plane vertex construction allocated every draw.
- Bounds3D.Transform allocated arrays on hot paths.

Changes:
- Added MeshResourceKey and MeshCache3D.
- Primitive BuildMesh methods now use shared cached meshes.
- Mesh3D now stores ResourceKey and LocalBounds.
- OpenGL renderer keys mesh GPU resources by Mesh3D.ResourceKey.
- ViewProjection uniform is uploaded once per mesh pass.
- Matrix upload and control-plane vertex data reuse fixed buffers.
- Bounds3D.Transform no longer allocates corner arrays.
- Object3D caches world matrix/world bounds and invalidates recursively through composite children.

## Iteration 3: high-scale foundation and runtime policy

Findings:
- A huge scene cannot be represented as one rich Object3D per visual detail.
- CompositeObject3D needs to stay an authoring model; scalable runtime must use templates and dense instances.
- Selection/hover propagation through every child causes event storms.

Changes:
- Added high-scale foundation types: CompositeTemplate3D, CompositePartTemplate3D, HighScaleTemplateCompiler, InstanceStore3D and InstanceFlags3D.
- Added RenderBatchKey for future instancing-first renderers.
- Composite hover/selection no longer mutates every child. Child rendering uses inherited effective hover/selection state.
- Added ScenePerformanceOptions and ScenePerformanceProfile.
- Live control snapshot updates now respect a per-frame budget.
- RenderStats now exposes registry/object/collider/cache and timing counters needed for future benchmark scenes.

Known next step:
- Implement real OpenGL glDrawElementsInstanced and a retained WebGL instance-buffer path. The current patch prepares shared mesh resources and registry hot paths but does not yet replace all draw calls with hardware instancing.
