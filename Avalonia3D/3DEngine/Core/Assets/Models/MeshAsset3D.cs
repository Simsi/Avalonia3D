using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Collision;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class MeshAsset3D
{
    public MeshAsset3D(int index, string name, IReadOnlyList<MeshPrimitiveAsset3D> primitives)
    {
        Index = index;
        Name = string.IsNullOrWhiteSpace(name) ? $"Mesh_{index}" : name;
        Primitives = primitives ?? Array.Empty<MeshPrimitiveAsset3D>();
        Bounds = ComputeBounds(Primitives);
    }

    public int Index { get; }
    public string Name { get; }
    public IReadOnlyList<MeshPrimitiveAsset3D> Primitives { get; }
    public Bounds3D Bounds { get; }
    public int PrimitiveCount => Primitives.Count;

    private static Bounds3D ComputeBounds(IReadOnlyList<MeshPrimitiveAsset3D> primitives)
    {
        var bounds = Bounds3D.Empty;
        foreach (var primitive in primitives)
        {
            bounds = bounds.Encapsulate(primitive.Bounds);
        }

        return bounds;
    }
}
