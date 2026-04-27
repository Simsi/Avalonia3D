using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.HighScale;

public static class HighScaleTemplateCompiler
{
    public static CompositeTemplate3D Compile(int id, CompositeObject3D source)
    {
        var parts = new List<CompositePartTemplate3D>();
        var rootWorld = source.GetModelMatrix();
        if (!Matrix4x4.Invert(rootWorld, out var inverseRootWorld))
        {
            inverseRootWorld = Matrix4x4.Identity;
        }

        foreach (var part in source.EnumerateDescendants(includeSelf: false))
        {
            if (!part.UseMeshRendering || !part.IsVisible)
            {
                continue;
            }

            var mesh = part.GetMesh();
            var material = part.Material;
            var partWorld = part.GetModelMatrix();
            var localToTemplate = partWorld * inverseRootWorld;

            parts.Add(new CompositePartTemplate3D(
                part.Name,
                mesh,
                new MeshResourceKey(mesh.ResourceKey),
                materialSlot: parts.Count,
                localTransform: localToTemplate,
                baseColor: material.EffectiveColor,
                lightingMode: material.Lighting));
        }

        return new CompositeTemplate3D(id, source.Name, parts);
    }
}
