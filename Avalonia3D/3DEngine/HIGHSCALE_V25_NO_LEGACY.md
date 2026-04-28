# HighScale v25 no-legacy fix

This patch removes the missing non-instanced HighScale fallback call from `OpenGlSceneRenderer`.

`HighScaleInstanceLayer3D` is now treated as an instancing-only rendering path. If the current OpenGL context does not expose instanced drawing, normal small-scene `Object3D` batches can still use the legacy path, but high-scale layers are intentionally not rendered through a per-object fallback.

Reason: the non-instanced fallback is not useful for the benchmark contract and makes heavy scenes unusable. Heavy scenes should fail visibly through missing high-scale rendering on unsupported contexts rather than silently running through an invalid slow path.
