namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Allocation-free journal record for one exact scene mutation. Sequence numbers are
/// monotonic for the lifetime of a scene and are suitable for retained consumer cursors.
/// </summary>
public readonly record struct SceneChangeRecord3D(
    long Sequence,
    SceneChangeKind Kind,
    Object3D? Source,
    long RegistryVersion,
    long BatchContentVersion,
    long BatchTransformVersion,
    long ParticleContentVersion,
    long CameraVersion,
    long StructureVersion);
