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
        var ordinary = new List<OrdinaryRenderItem3D>();
        SceneOrdinaryRenderItemBuilder3D.Build(scene, scene.Registry.GetFrameSnapshot(), ordinary);
        for (var i = 0; i < ordinary.Count; i++)
        {
            var item = ordinary[i];
            output.Add(new MeshRenderItem3D(
                item.Owner,
                item.Mesh.RenderGeometry,
                item.Material,
                item.Model,
                item.Owner.IsEffectivelyHovered,
                item.Owner.IsEffectivelySelected));
        }
    }
}
