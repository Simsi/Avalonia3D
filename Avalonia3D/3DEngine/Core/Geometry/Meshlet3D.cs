using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;

namespace ThreeDEngine.Core.Geometry;

public readonly struct Meshlet3D
{
    internal Meshlet3D(
        int vertexOffset,
        int vertexCount,
        int triangleOffset,
        int triangleCount,
        Bounds3D bounds,
        Vector3 normalConeAxis,
        float normalConeCutoff)
    {
        VertexOffset = vertexOffset;
        VertexCount = vertexCount;
        TriangleOffset = triangleOffset;
        TriangleCount = triangleCount;
        Bounds = bounds;
        NormalConeAxis = normalConeAxis;
        NormalConeCutoff = normalConeCutoff;
    }

    public int VertexOffset { get; }
    public int VertexCount { get; }
    public int TriangleOffset { get; }
    public int TriangleCount { get; }
    public Bounds3D Bounds { get; }
    public Vector3 NormalConeAxis { get; }
    public float NormalConeCutoff { get; }
}
