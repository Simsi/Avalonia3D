using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Particles;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral particle render item. It contains the per-system decisions that both
/// desktop OpenGL and WebGL need before they translate particles into their own instance buffers.
/// </summary>
public readonly struct ParticleRenderItem3D
{
    public ParticleRenderItem3D(
        ParticleSystem3D system,
        Mesh3D mesh,
        MaterialBinding3D material,
        Matrix4x4 parentModel,
        bool billboard,
        bool transparent,
        bool cameraDependentOrder,
        float sizeScale,
        string retainedBatchId,
        float sortDistanceSquared,
        int sourceOrder)
    {
        System = system;
        Mesh = mesh;
        Material = material;
        ParentModel = parentModel;
        Billboard = billboard;
        Transparent = transparent;
        CameraDependentOrder = cameraDependentOrder;
        SizeScale = sizeScale;
        RetainedBatchId = retainedBatchId;
        SortDistanceSquared = sortDistanceSquared;
        SourceOrder = sourceOrder;
    }

    public ParticleSystem3D System { get; }
    public Mesh3D Mesh { get; }
    public MaterialBinding3D Material { get; }
    public Matrix4x4 ParentModel { get; }
    public bool Billboard { get; }
    public bool Transparent { get; }
    public bool CameraDependentOrder { get; }
    public float SizeScale { get; }
    public string RetainedBatchId { get; }
    public float SortDistanceSquared { get; }
    public int SourceOrder { get; }
    public int AliveCount => System.AliveCount;

    public static int CompareForDraw(ParticleRenderItem3D a, ParticleRenderItem3D b)
        => SceneRenderDrawOrder3D.Compare(
            a.Transparent,
            a.SortDistanceSquared,
            a.SourceOrder,
            a.RetainedBatchId,
            b.Transparent,
            b.SortDistanceSquared,
            b.SourceOrder,
            b.RetainedBatchId);
}
