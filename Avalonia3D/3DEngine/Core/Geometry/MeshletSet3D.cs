using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Immutable meshlet preprocessing result. Triangle indices are local byte indices into each
/// meshlet's global vertex-index slice and are ready for future mesh-shader/compute backends.
/// </summary>
public sealed class MeshletSet3D
{
    private MeshletSet3D(Meshlet3D[] meshlets, int[] vertexIndices, byte[] localTriangleIndices)
    {
        Meshlets = GeometryBuffer3D<Meshlet3D>.TakeOwnership(meshlets);
        VertexIndices = GeometryBuffer3D<int>.TakeOwnership(vertexIndices);
        LocalTriangleIndices = GeometryBuffer3D<byte>.TakeOwnership(localTriangleIndices);
    }

    public GeometryBuffer3D<Meshlet3D> Meshlets { get; }
    public GeometryBuffer3D<int> VertexIndices { get; }
    public GeometryBuffer3D<byte> LocalTriangleIndices { get; }
    public int Count => Meshlets.Length;
    public long EstimatedResidentBytes => Meshlets.LongLength * 64L + VertexIndices.LongLength * sizeof(int) + LocalTriangleIndices.LongLength;

    internal static MeshletSet3D Build(RenderGeometry3D geometry, int maxVertices, int maxTriangles)
    {
        if (geometry is null) throw new ArgumentNullException(nameof(geometry));
        if (geometry.IndexCount == 0) return new MeshletSet3D(Array.Empty<Meshlet3D>(), Array.Empty<int>(), Array.Empty<byte>());
        if (maxVertices is < 3 or > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(maxVertices));
        if (maxTriangles <= 0) throw new ArgumentOutOfRangeException(nameof(maxTriangles));

        var meshlets = new List<Meshlet3D>((geometry.TriangleCount + maxTriangles - 1) / maxTriangles);
        var globalVertices = new List<int>(meshlets.Capacity * maxVertices);
        var localTriangles = new List<byte>(geometry.IndexCount);
        var localMap = new Dictionary<int, byte>(maxVertices);
        var currentVertices = new List<int>(maxVertices);
        var currentTriangles = new List<(byte A, byte B, byte C)>(maxTriangles);

        for (var triangle = 0; triangle < geometry.TriangleCount; triangle++)
        {
            var indexOffset = triangle * 3;
            var i0 = geometry.Indices[indexOffset];
            var i1 = geometry.Indices[indexOffset + 1];
            var i2 = geometry.Indices[indexOffset + 2];
            var newVertices = CountNew(i0, i1, i2);
            if (currentTriangles.Count > 0 && (currentTriangles.Count >= maxTriangles || currentVertices.Count + newVertices > maxVertices))
            {
                Flush();
            }

            var a = Resolve(i0);
            var b = Resolve(i1);
            var c = Resolve(i2);
            currentTriangles.Add((a, b, c));
        }
        Flush();
        return new MeshletSet3D(meshlets.ToArray(), globalVertices.ToArray(), localTriangles.ToArray());

        int CountNew(int a, int b, int c)
        {
            var count = localMap.ContainsKey(a) ? 0 : 1;
            if (b != a && !localMap.ContainsKey(b)) count++;
            if (c != a && c != b && !localMap.ContainsKey(c)) count++;
            return count;
        }

        byte Resolve(int vertex)
        {
            if (localMap.TryGetValue(vertex, out var existing)) return existing;
            var local = checked((byte)currentVertices.Count);
            localMap.Add(vertex, local);
            currentVertices.Add(vertex);
            return local;
        }

        void Flush()
        {
            if (currentTriangles.Count == 0) return;
            var vertexOffset = globalVertices.Count;
            var triangleOffset = localTriangles.Count / 3;
            globalVertices.AddRange(currentVertices);
            for (var i = 0; i < currentTriangles.Count; i++)
            {
                var triangle = currentTriangles[i];
                localTriangles.Add(triangle.A);
                localTriangles.Add(triangle.B);
                localTriangles.Add(triangle.C);
            }

            var bounds = Bounds3D.Empty;
            var coneAxis = Vector3.Zero;
            var validNormalCount = 0;
            for (var i = 0; i < currentTriangles.Count; i++)
            {
                var triangle = currentTriangles[i];
                var p0 = geometry.Positions[currentVertices[triangle.A]];
                var p1 = geometry.Positions[currentVertices[triangle.B]];
                var p2 = geometry.Positions[currentVertices[triangle.C]];
                bounds = bounds.Encapsulate(p0).Encapsulate(p1).Encapsulate(p2);
                var normal = Vector3.Cross(p1 - p0, p2 - p0);
                if (normal.LengthSquared() <= 1e-16f) continue;
                coneAxis += Vector3.Normalize(normal);
                validNormalCount++;
            }
            if (validNormalCount > 0 && coneAxis.LengthSquared() > 1e-16f) coneAxis = Vector3.Normalize(coneAxis);
            else coneAxis = Vector3.UnitY;
            var cutoff = 1f;
            if (validNormalCount > 0)
            {
                for (var i = 0; i < currentTriangles.Count; i++)
                {
                    var triangle = currentTriangles[i];
                    var p0 = geometry.Positions[currentVertices[triangle.A]];
                    var p1 = geometry.Positions[currentVertices[triangle.B]];
                    var p2 = geometry.Positions[currentVertices[triangle.C]];
                    var normal = Vector3.Cross(p1 - p0, p2 - p0);
                    if (normal.LengthSquared() <= 1e-16f) continue;
                    cutoff = global::System.MathF.Min(cutoff, Vector3.Dot(coneAxis, Vector3.Normalize(normal)));
                }
            }
            meshlets.Add(new Meshlet3D(vertexOffset, currentVertices.Count, triangleOffset, currentTriangles.Count, bounds, coneAxis, cutoff));
            localMap.Clear();
            currentVertices.Clear();
            currentTriangles.Clear();
        }
    }
}
