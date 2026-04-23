using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeDEngine.Core.Geometry;

public static class MeshFactory
{
    public static Mesh3D CreateRectangle(float width, float height)
        => CreateExtrudedRectangle(width, height, 0.001f);

    public static Mesh3D CreateExtrudedRectangle(float width, float height, float depth)
    {
        width = System.MathF.Max(width, 0.001f);
        height = System.MathF.Max(height, 0.001f);
        depth = System.MathF.Max(depth, 0.001f);

        var hw = width * 0.5f;
        var hh = height * 0.5f;
        var hz = depth * 0.5f;

        var positions = new List<Vector3>(24);
        var normals = new List<Vector3>(24);
        var indices = new List<int>(36);

        AddQuad(positions, normals, indices,
            new Vector3(-hw, -hh, hz), new Vector3(hw, -hh, hz), new Vector3(hw, hh, hz), new Vector3(-hw, hh, hz),
            Vector3.UnitZ);

        AddQuad(positions, normals, indices,
            new Vector3(hw, -hh, -hz), new Vector3(-hw, -hh, -hz), new Vector3(-hw, hh, -hz), new Vector3(hw, hh, -hz),
            -Vector3.UnitZ);

        AddQuad(positions, normals, indices,
            new Vector3(hw, -hh, hz), new Vector3(hw, -hh, -hz), new Vector3(hw, hh, -hz), new Vector3(hw, hh, hz),
            Vector3.UnitX);

        AddQuad(positions, normals, indices,
            new Vector3(-hw, -hh, -hz), new Vector3(-hw, -hh, hz), new Vector3(-hw, hh, hz), new Vector3(-hw, hh, -hz),
            -Vector3.UnitX);

        AddQuad(positions, normals, indices,
            new Vector3(-hw, hh, hz), new Vector3(hw, hh, hz), new Vector3(hw, hh, -hz), new Vector3(-hw, hh, -hz),
            Vector3.UnitY);

        AddQuad(positions, normals, indices,
            new Vector3(-hw, -hh, -hz), new Vector3(hw, -hh, -hz), new Vector3(hw, -hh, hz), new Vector3(-hw, -hh, hz),
            -Vector3.UnitY);

        return new Mesh3D(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    public static Mesh3D CreateEllipse(float width, float height, int segments = 48)
        => CreateExtrudedEllipse(width, height, 0.001f, segments);

    public static Mesh3D CreateExtrudedEllipse(float width, float height, float depth, int segments = 48)
    {
        width = System.MathF.Max(width, 0.001f);
        height = System.MathF.Max(height, 0.001f);
        depth = System.MathF.Max(depth, 0.001f);
        segments = System.Math.Max(segments, 12);

        var rx = width * 0.5f;
        var ry = height * 0.5f;
        var hz = depth * 0.5f;

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();

        var frontCenterIndex = positions.Count;
        positions.Add(new Vector3(0f, 0f, hz));
        normals.Add(Vector3.UnitZ);
        for (var i = 0; i < segments; i++)
        {
            var angle = (System.MathF.PI * 2f * i) / segments;
            positions.Add(new Vector3(System.MathF.Cos(angle) * rx, System.MathF.Sin(angle) * ry, hz));
            normals.Add(Vector3.UnitZ);
        }
        for (var i = 0; i < segments; i++)
        {
            var current = frontCenterIndex + 1 + i;
            var next = frontCenterIndex + 1 + ((i + 1) % segments);
            indices.Add(frontCenterIndex);
            indices.Add(current);
            indices.Add(next);
        }

        var backCenterIndex = positions.Count;
        positions.Add(new Vector3(0f, 0f, -hz));
        normals.Add(-Vector3.UnitZ);
        for (var i = 0; i < segments; i++)
        {
            var angle = (System.MathF.PI * 2f * i) / segments;
            positions.Add(new Vector3(System.MathF.Cos(angle) * rx, System.MathF.Sin(angle) * ry, -hz));
            normals.Add(-Vector3.UnitZ);
        }
        for (var i = 0; i < segments; i++)
        {
            var current = backCenterIndex + 1 + i;
            var next = backCenterIndex + 1 + ((i + 1) % segments);
            indices.Add(backCenterIndex);
            indices.Add(next);
            indices.Add(current);
        }

        for (var i = 0; i < segments; i++)
        {
            var angle0 = (System.MathF.PI * 2f * i) / segments;
            var angle1 = (System.MathF.PI * 2f * ((i + 1) % segments)) / segments;

            var p0 = new Vector3(System.MathF.Cos(angle0) * rx, System.MathF.Sin(angle0) * ry, hz);
            var p1 = new Vector3(System.MathF.Cos(angle1) * rx, System.MathF.Sin(angle1) * ry, hz);
            var p2 = new Vector3(System.MathF.Cos(angle1) * rx, System.MathF.Sin(angle1) * ry, -hz);
            var p3 = new Vector3(System.MathF.Cos(angle0) * rx, System.MathF.Sin(angle0) * ry, -hz);

            var normal0 = Vector3.Normalize(new Vector3(System.MathF.Cos(angle0) / System.MathF.Max(rx, 0.0001f), System.MathF.Sin(angle0) / System.MathF.Max(ry, 0.0001f), 0f));
            var normal1 = Vector3.Normalize(new Vector3(System.MathF.Cos(angle1) / System.MathF.Max(rx, 0.0001f), System.MathF.Sin(angle1) / System.MathF.Max(ry, 0.0001f), 0f));

            var baseIndex = positions.Count;
            positions.Add(p0);
            positions.Add(p1);
            positions.Add(p2);
            positions.Add(p3);
            normals.Add(normal0);
            normals.Add(normal1);
            normals.Add(normal1);
            normals.Add(normal0);

            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
        }

        return new Mesh3D(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    private static void AddQuad(
        List<Vector3> positions,
        List<Vector3> normals,
        List<int> indices,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal)
    {
        var baseIndex = positions.Count;
        positions.Add(a);
        positions.Add(b);
        positions.Add(c);
        positions.Add(d);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 3);
    }
}
