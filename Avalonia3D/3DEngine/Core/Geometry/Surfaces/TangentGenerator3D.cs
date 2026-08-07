using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeDEngine.Core.Geometry.Surfaces;

/// <summary>
/// Generates portable tangent-space data for normal-mapped imported/static meshes.
/// This is intentionally backend-neutral: OpenGL/WebGL receive the same vertex streams.
/// </summary>
public static class TangentGenerator3D
{
    public static Vector3[] GenerateNormals(Vector3[] positions, IReadOnlyList<int> indices)
    {
        if (positions.Length == 0) return Array.Empty<Vector3>();
        var normals = new Vector3[positions.Length];
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if (!IsValidTriangleIndex(i0, i1, i2, positions.Length)) continue;
            var p0 = positions[i0];
            var p1 = positions[i1];
            var p2 = positions[i2];
            var normal = Vector3.Cross(p1 - p0, p2 - p0);
            if (normal.LengthSquared() <= 0.00000001f) continue;
            normal = Vector3.Normalize(normal);
            normals[i0] += normal;
            normals[i1] += normal;
            normals[i2] += normal;
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > 0.00000001f ? Vector3.Normalize(normals[i]) : Vector3.UnitZ;
        }

        return normals;
    }

    public static Vector4[] GenerateTangents(Vector3[] positions, Vector3[] normals, Vector2[] texCoords0, IReadOnlyList<int> indices)
    {
        if (positions.Length == 0 || texCoords0.Length != positions.Length) return Array.Empty<Vector4>();
        var resolvedNormals = normals.Length == positions.Length ? normals : GenerateNormals(positions, indices);
        var tan1 = new Vector3[positions.Length];
        var tan2 = new Vector3[positions.Length];

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var i1 = indices[i];
            var i2 = indices[i + 1];
            var i3 = indices[i + 2];
            if (!IsValidTriangleIndex(i1, i2, i3, positions.Length)) continue;

            var v1 = positions[i1];
            var v2 = positions[i2];
            var v3 = positions[i3];
            var w1 = texCoords0[i1];
            var w2 = texCoords0[i2];
            var w3 = texCoords0[i3];

            var x1 = v2.X - v1.X;
            var x2 = v3.X - v1.X;
            var y1 = v2.Y - v1.Y;
            var y2 = v3.Y - v1.Y;
            var z1 = v2.Z - v1.Z;
            var z2 = v3.Z - v1.Z;

            var s1 = w2.X - w1.X;
            var s2 = w3.X - w1.X;
            var t1 = w2.Y - w1.Y;
            var t2 = w3.Y - w1.Y;
            var denom = s1 * t2 - s2 * t1;
            if (MathF.Abs(denom) < 0.00000001f) continue;
            var r = 1.0f / denom;

            var sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
            var tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);

            tan1[i1] += sdir; tan1[i2] += sdir; tan1[i3] += sdir;
            tan2[i1] += tdir; tan2[i2] += tdir; tan2[i3] += tdir;
        }

        var result = new Vector4[positions.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var n = resolvedNormals[i].LengthSquared() > 0.00000001f ? Vector3.Normalize(resolvedNormals[i]) : Vector3.UnitZ;
            var t = tan1[i];
            if (t.LengthSquared() <= 0.00000001f)
            {
                t = MathF.Abs(Vector3.Dot(n, Vector3.UnitY)) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            }

            t = Vector3.Normalize(t - n * Vector3.Dot(n, t));
            var handedness = Vector3.Dot(Vector3.Cross(n, t), tan2[i]) < 0.0f ? -1.0f : 1.0f;
            result[i] = new Vector4(t, handedness);
        }

        return result;
    }

    public static int[] BuildWireframeIndices(IReadOnlyList<int> triangleIndices)
    {
        if (triangleIndices.Count < 3) return Array.Empty<int>();
        var unique = new HashSet<long>();
        var edges = new List<int>(triangleIndices.Count * 2);
        for (var i = 0; i + 2 < triangleIndices.Count; i += 3)
        {
            var a = triangleIndices[i];
            var b = triangleIndices[i + 1];
            var c = triangleIndices[i + 2];
            AddEdge(a, b);
            AddEdge(b, c);
            AddEdge(c, a);
        }

        return edges.ToArray();

        void AddEdge(int first, int second)
        {
            var min = global::System.Math.Min(first, second);
            var max = global::System.Math.Max(first, second);
            var key = ((long)min << 32) | (uint)max;
            if (!unique.Add(key)) return;
            edges.Add(first);
            edges.Add(second);
        }
    }

    private static bool IsValidTriangleIndex(int i0, int i1, int i2, int vertexCount)
        => i0 >= 0 && i1 >= 0 && i2 >= 0 && i0 < vertexCount && i1 < vertexCount && i2 < vertexCount;
}
