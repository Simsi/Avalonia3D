namespace ThreeDEngine.Core.Rendering;

public sealed class RenderStats
{
    public int ObjectCount { get; set; }
    public int RenderableCount { get; set; }
    public int PickableCount { get; set; }
    public int ColliderCount { get; set; }
    public int DynamicBodyCount { get; set; }
    public int StaticColliderCount { get; set; }
    public int VisibleMeshCount { get; set; }
    public int ControlPlaneCount { get; set; }
    public int TriangleCount { get; set; }
    public int DrawCallCount { get; set; }
    public int EstimatedDrawCallCount { get; set; }
    public int InstancedBatchCount { get; set; }
    public int HighScaleInstanceCount { get; set; }
    public int CulledObjectCount { get; set; }
    public int DirtyMeshUploads { get; set; }
    public int DirtyTextureUploads { get; set; }
    public long TextureUploadBytes { get; set; }
    public int RegistryVersion { get; set; }
    public int MeshCacheCount { get; set; }
    public int SceneTraversalCount { get; set; }
    public int TotalChunkCount { get; set; }
    public int VisibleChunkCount { get; set; }
    public int LodSimplifiedCount { get; set; }
    public int LodProxyCount { get; set; }
    public int LodBillboardCount { get; set; }
    public int InstanceBufferUploads { get; set; }
    public int InstanceBufferSubDataUploads { get; set; }
    public long InstanceUploadBytes { get; set; }
    public double PacketBuildMilliseconds { get; set; }
    public double SerializationMilliseconds { get; set; }
    public double UploadMilliseconds { get; set; }
    public double BackendMilliseconds { get; set; }
    public double PickingMilliseconds { get; set; }
    public double PhysicsMilliseconds { get; set; }
    public double LiveSnapshotMilliseconds { get; set; }
    public long ManagedAllocatedBytes { get; set; }

    public static RenderStats Empty { get; } = new RenderStats();
}
