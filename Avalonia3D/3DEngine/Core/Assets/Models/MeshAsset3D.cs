using System;
using System.Collections.Generic;
using System.Linq;
using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.Collision;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class MeshAsset3D
{
    public MeshAsset3D(int index, string name, IReadOnlyList<MeshPrimitiveAsset3D> primitives)
    {
        Index = Guard3D.NonNegative(index, nameof(index));
        Name = string.IsNullOrWhiteSpace(name) ? $"Mesh_{index}" : name;
        Primitives = Array.AsReadOnly((primitives ?? throw new ArgumentNullException(nameof(primitives))).ToArray());
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
