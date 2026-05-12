using ThreeDEngine.Core.HighScale;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Immutable per-registry-version scene view for render hot paths.
///
/// The renderer requests this once at the beginning of a frame and passes it through
/// upload/build/sweep stages. That removes repeated Snapshot* array allocations in
/// WebAssembly camera frames.
/// </summary>
public sealed class SceneFrameSnapshot3D
{
    public required int RegistryVersion { get; init; }
    public required Object3D[] AllObjects { get; init; }
    public required Object3D[] Renderables { get; init; }
    public required Object3D[] Pickables { get; init; }
    public required Object3D[] Colliders { get; init; }
    public required Object3D[] DynamicBodies { get; init; }
    public required Object3D[] StaticColliders { get; init; }
    public required HighScaleInstanceLayer3D[] HighScaleLayers { get; init; }
}
