using System.Collections.Generic;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

public static class SceneRenderItemBuilder
{
    public static List<MeshRenderItem3D> BuildMeshItems(Scene3D scene)
    {
        var items = new List<MeshRenderItem3D>();
        AddMeshItems(scene, items);
        return items;
    }

    public static void AddMeshItems(Scene3D scene, ICollection<MeshRenderItem3D> output)
    {
        foreach (var obj in scene.Registry.Renderables)
        {
            var mesh = obj.GetMesh();
            output.Add(new MeshRenderItem3D(
                obj,
                mesh.RenderGeometry,
                MaterialBinding3D.FromMaterial(obj.Material),
                obj.GetModelMatrix(),
                obj.IsEffectivelyHovered,
                obj.IsEffectivelySelected));
        }
    }
}
