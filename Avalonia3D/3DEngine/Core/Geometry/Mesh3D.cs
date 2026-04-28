using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Geometry;

public sealed class Mesh3D
{
    public static Mesh3D Empty { get; } = new Mesh3D(Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<int>(), "empty");

    public Mesh3D(Vector3[] positions, Vector3[] normals, int[] indices, string? resourceKey = null, float[]? materialSlots = null, ColorRgba[]? materialSlotBaseColors = null)
    {
        Positions = positions ?? Array.Empty<Vector3>();
        Normals = normals ?? Array.Empty<Vector3>();
        Indices = indices ?? Array.Empty<int>();
        MaterialSlots = materialSlots is not null && materialSlots.Length == Positions.Length ? materialSlots : Array.Empty<float>();
        MaterialSlotBaseColors = materialSlotBaseColors ?? Array.Empty<ColorRgba>();
        ResourceKey = string.IsNullOrWhiteSpace(resourceKey) ? "custom:" + Guid.NewGuid().ToString("N") : resourceKey;
        LocalBounds = ComputeLocalBounds(Positions);
        BoundingRadius = ComputeBoundingRadius(Positions);
    }

    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public int[] Indices { get; }
    public float[] MaterialSlots { get; }
    public ColorRgba[] MaterialSlotBaseColors { get; }
    public bool HasMaterialSlots => MaterialSlots.Length == Positions.Length && Positions.Length > 0;
    public int MaterialSlotCount => MaterialSlotBaseColors.Length > 0 ? MaterialSlotBaseColors.Length : ComputeMaterialSlotCount(MaterialSlots);
    public string ResourceKey { get; }
    public Bounds3D LocalBounds { get; }
    public float BoundingRadius { get; }

    private static int ComputeMaterialSlotCount(float[] slots)
    {
        var max = -1;
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = (int)MathF.Round(slots[i]);
            if (slot > max) max = slot;
        }

        return max + 1;
    }

    private static Bounds3D ComputeLocalBounds(Vector3[] positions)
    {
        if (positions.Length == 0)
        {
            return Bounds3D.Empty;
        }

        var min = positions[0];
        var max = positions[0];
        for (var i = 1; i < positions.Length; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }

        return new Bounds3D(min, max);
    }

    private static float ComputeBoundingRadius(Vector3[] positions)
    {
        var radiusSquared = 0f;
        for (var i = 0; i < positions.Length; i++)
        {
            radiusSquared = MathF.Max(radiusSquared, positions[i].LengthSquared());
        }

        return MathF.Sqrt(radiusSquared);
    }
}
