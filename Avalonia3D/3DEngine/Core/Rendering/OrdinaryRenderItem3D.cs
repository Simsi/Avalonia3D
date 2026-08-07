using System.Numerics;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral ordinary mesh render item. It is the single extraction result used
/// by desktop OpenGL and browser WebGL retained renderers.
/// </summary>
internal readonly struct OrdinaryRenderItem3D
{
    public OrdinaryRenderItem3D(
        Object3D owner,
        Mesh3D mesh,
        MaterialBinding3D material,
        Matrix4x4 model,
        ColorRgba color,
        bool usesGpuSkinning,
        string logicalMeshBatchKey,
        string retainedBatchId)
    {
        Owner = owner;
        Mesh = mesh;
        Material = material;
        Model = model;
        Color = color;
        UsesGpuSkinning = usesGpuSkinning;
        LogicalMeshBatchKey = logicalMeshBatchKey;
        RetainedBatchId = retainedBatchId;
    }

    public Object3D Owner { get; }
    public Mesh3D Mesh { get; }
    public MaterialBinding3D Material { get; }
    public Matrix4x4 Model { get; }
    public ColorRgba Color { get; }
    public bool UsesGpuSkinning { get; }
    public string LogicalMeshBatchKey { get; }
    public string RetainedBatchId { get; }
    public bool Transparent => Material.Surface == ThreeDEngine.Core.Materials.SurfaceMode.Transparent || Material.BaseColor.A < 0.999f;
    public ModelPart3D? SkinnedPart => Owner as ModelPart3D;
    public int TriangleCount => Mesh.Indices.Length / 3;
}
