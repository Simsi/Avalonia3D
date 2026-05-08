using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

public readonly struct MeshRenderItem3D
{
    public MeshRenderItem3D(Object3D owner, RenderGeometry3D geometry, MaterialBinding3D material, Matrix4x4 modelMatrix, bool isHovered, bool isSelected)
    {
        Owner = owner;
        Geometry = geometry;
        Material = material;
        ModelMatrix = modelMatrix;
        IsHovered = isHovered;
        IsSelected = isSelected;
    }

    public Object3D Owner { get; }
    public RenderGeometry3D Geometry { get; }
    public MaterialBinding3D Material { get; }
    public Matrix4x4 ModelMatrix { get; }
    public bool IsHovered { get; }
    public bool IsSelected { get; }
    public RendererResourceKey MeshResourceKey => RendererResourceKey.Mesh(Geometry.ResourceKey, Owner.GeometryVersion);
    public RenderBatchKey BatchKey => new(new MeshResourceKey(Geometry.ResourceKey), Material.Key, (int)Material.Lighting, (int)Material.Surface);
}
