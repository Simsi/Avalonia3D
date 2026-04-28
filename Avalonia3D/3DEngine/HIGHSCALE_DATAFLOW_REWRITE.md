# High-scale data-flow rewrite

This drop changes the high-scale runtime from chunk-rebuild-oriented telemetry to a retained transform/state split.

## Runtime contract

Transform/structure changes may dirty chunks and rebuild retained transform batches. Status, material variant and visibility telemetry must not dirty chunks. These updates now go through `HighScaleInstanceLayer3D.StateBuffer` and raise `SceneChangeKind.HighScaleState`.

`Scene3D` subscribes to high-scale state changes separately from `Object3D.Changed`. `HighScaleState`, camera, lighting and material changes no longer invalidate `SceneObjectRegistry`; structural, transform, geometry, visibility, physics, control and unknown changes still do.

## OpenGL renderer

High-scale batches now own two GPU buffers:

- transform buffer: 16 floats per instance, rebuilt only for chunk/LOD/transform structure changes;
- state buffer: 4 floats per instance, currently resolved state color RGBA, updated through `glBufferSubData` when available.

Telemetry material updates therefore update the small state buffer instead of rebuilding `matrix + color` instance buffers. Renderer metrics now distinguish transform uploads (`InstanceUploadBytes`) from state uploads (`StateUploadBytes`).

## Remaining limits

This is the first retained data-flow cut, not a final performance ceiling:

- state is still color-resolved on CPU; a shader palette / integer material-state buffer is the next step;
- LOD planning is still per instance inside visible chunks;
- batch lookup now avoids string-concatenated keys, but per-batch instance-index maps still cost memory;
- GPU timer queries are still not implemented in this drop.

## Previewer

The previewer now includes a complexity report in the inspector and uses safer assembly loading for shared `ThreeDEngine*` contracts to reduce type-identity failures. VSIX class detection was widened to generic and record-class declarations.


## v30 correction: logical-instance state buffer

Benchmark results showed that the previous data-flow rewrite still uploaded state per visible part-instance. 
For 10k rack composites that meant roughly 110k state entries and ~1.7 MB state upload whenever a full state refresh occurred.

This correction changes OpenGL high-scale batches to use one transform/state stream per layer+chunk+LOD, shared by all template parts in that LOD. 
The per-instance state now stores materialVariantId, visible flag and fade alpha. 
The current part resolves the final color through a small shader-side variant palette uniform. 
Expected effect: for 10k logical rack instances, full state refresh should be around 10k * 16 bytes, not parts * 10k * 16 bytes.

This still does not bypass Avalonia/OpenGlControlBase presentation pacing. 
If FPS remains capped near the monitor refresh rate in unlocked mode, the cap is likely VSync/compositor/render-loop bound rather than the engine's FpsLock flag.
