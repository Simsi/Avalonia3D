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

    public static Mesh3D CreatePlane(float width, float height, int segmentsX = 1, int segmentsY = 1)
    {
        width = System.MathF.Max(width, 0.001f);
        height = System.MathF.Max(height, 0.001f);
        segmentsX = System.Math.Max(1, segmentsX);
        segmentsY = System.Math.Max(1, segmentsY);

        var positions = new List<Vector3>((segmentsX + 1) * (segmentsY + 1));
        var normals = new List<Vector3>((segmentsX + 1) * (segmentsY + 1));
        var indices = new List<int>(segmentsX * segmentsY * 6);
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;

        for (var y = 0; y <= segmentsY; y++)
        {
            var z = -halfHeight + height * y / segmentsY;
            for (var x = 0; x <= segmentsX; x++)
            {
                var px = -halfWidth + width * x / segmentsX;
                positions.Add(new Vector3(px, 0f, z));
                normals.Add(Vector3.UnitY);
            }
        }

        var stride = segmentsX + 1;
        for (var y = 0; y < segmentsY; y++)
        {
            for (var x = 0; x < segmentsX; x++)
            {
                var i0 = y * stride + x;
                var i1 = i0 + 1;
                var i2 = i0 + stride + 1;
                var i3 = i0 + stride;

                indices.Add(i0);
                indices.Add(i3);
                indices.Add(i2);
                indices.Add(i0);
                indices.Add(i2);
                indices.Add(i1);
            }
        }

        return new Mesh3D(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }


    public static Mesh3D CreateCylinder(float radius, float height, int segments = 32)
    {
        radius = System.MathF.Max(radius, 0.001f);
        height = System.MathF.Max(height, 0.001f);
        segments = System.Math.Max(segments, 12);

        var halfHeight = height * 0.5f;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();

        var topCenter = positions.Count;
        positions.Add(new Vector3(0f, halfHeight, 0f));
        normals.Add(Vector3.UnitY);

        for (var i = 0; i < segments; i++)
        {
            var angle = System.MathF.PI * 2f * i / segments;
            positions.Add(new Vector3(System.MathF.Cos(angle) * radius, halfHeight, System.MathF.Sin(angle) * radius));
            normals.Add(Vector3.UnitY);
        }

        for (var i = 0; i < segments; i++)
        {
            indices.Add(topCenter);
            indices.Add(topCenter + 1 + ((i + 1) % segments));
            indices.Add(topCenter + 1 + i);
        }

        var bottomCenter = positions.Count;
        positions.Add(new Vector3(0f, -halfHeight, 0f));
        normals.Add(-Vector3.UnitY);

        for (var i = 0; i < segments; i++)
        {
            var angle = System.MathF.PI * 2f * i / segments;
            positions.Add(new Vector3(System.MathF.Cos(angle) * radius, -halfHeight, System.MathF.Sin(angle) * radius));
            normals.Add(-Vector3.UnitY);
        }

        for (var i = 0; i < segments; i++)
        {
            indices.Add(bottomCenter);
            indices.Add(bottomCenter + 1 + i);
            indices.Add(bottomCenter + 1 + ((i + 1) % segments));
        }

        for (var i = 0; i < segments; i++)
        {
            var angle0 = System.MathF.PI * 2f * i / segments;
            var angle1 = System.MathF.PI * 2f * ((i + 1) % segments) / segments;
            var n0 = Vector3.Normalize(new Vector3(System.MathF.Cos(angle0), 0f, System.MathF.Sin(angle0)));
            var n1 = Vector3.Normalize(new Vector3(System.MathF.Cos(angle1), 0f, System.MathF.Sin(angle1)));
            var p0 = new Vector3(n0.X * radius, -halfHeight, n0.Z * radius);
            var p1 = new Vector3(n1.X * radius, -halfHeight, n1.Z * radius);
            var p2 = new Vector3(n1.X * radius, halfHeight, n1.Z * radius);
            var p3 = new Vector3(n0.X * radius, halfHeight, n0.Z * radius);

            var baseIndex = positions.Count;
            positions.Add(p0); normals.Add(n0);
            positions.Add(p1); normals.Add(n1);
            positions.Add(p2); normals.Add(n1);
            positions.Add(p3); normals.Add(n0);

            indices.Add(baseIndex);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
        }

        return new Mesh3D(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    public static Mesh3D CreateCone(float radius, float height, int segments = 32)
    {
        radius = System.MathF.Max(radius, 0.001f);
        height = System.MathF.Max(height, 0.001f);
        segments = System.Math.Max(segments, 12);

        var halfHeight = height * 0.5f;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();

        var bottomCenter = positions.Count;
        positions.Add(new Vector3(0f, -halfHeight, 0f));
        normals.Add(-Vector3.UnitY);

        for (var i = 0; i < segments; i++)
        {
            var angle = System.MathF.PI * 2f * i / segments;
            positions.Add(new Vector3(System.MathF.Cos(angle) * radius, -halfHeight, System.MathF.Sin(angle) * radius));
            normals.Add(-Vector3.UnitY);
        }

        for (var i = 0; i < segments; i++)
        {
            indices.Add(bottomCenter);
            indices.Add(bottomCenter + 1 + i);
            indices.Add(bottomCenter + 1 + ((i + 1) % segments));
        }

        var apex = new Vector3(0f, halfHeight, 0f);
        for (var i = 0; i < segments; i++)
        {
            var angle0 = System.MathF.PI * 2f * i / segments;
            var angle1 = System.MathF.PI * 2f * ((i + 1) % segments) / segments;
            var p0 = new Vector3(System.MathF.Cos(angle0) * radius, -halfHeight, System.MathF.Sin(angle0) * radius);
            var p1 = new Vector3(System.MathF.Cos(angle1) * radius, -halfHeight, System.MathF.Sin(angle1) * radius);
            var faceNormal = Vector3.Normalize(Vector3.Cross(p1 - p0, apex - p0));

            var baseIndex = positions.Count;
            positions.Add(p0); normals.Add(faceNormal);
            positions.Add(p1); normals.Add(faceNormal);
            positions.Add(apex); normals.Add(faceNormal);

            indices.Add(baseIndex);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
        }

        return new Mesh3D(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    public static Mesh3D CreateSphere(float radius, int segments = 32, int rings = 16)
    {
        radius = System.MathF.Max(radius, 0.001f);
        segments = System.Math.Max(segments, 12);
        rings = System.Math.Max(rings, 6);

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();

        for (var y = 0; y <= rings; y++)
        {
            var v = (float)y / rings;
            var phi = -System.MathF.PI * 0.5f + v * System.MathF.PI;
            var cosPhi = System.MathF.Cos(phi);
            var sinPhi = System.MathF.Sin(phi);

            for (var x = 0; x <= segments; x++)
            {
                var u = (float)x / segments;
                var theta = u * System.MathF.PI * 2f;
                var normal = new Vector3(System.MathF.Cos(theta) * cosPhi, sinPhi, System.MathF.Sin(theta) * cosPhi);
                if (normal.LengthSquared() < 0.000001f)
                {
                    normal = y == 0 ? -Vector3.UnitY : Vector3.UnitY;
                }
                else
                {
                    normal = Vector3.Normalize(normal);
                }

                positions.Add(normal * radius);
                normals.Add(normal);
            }
        }

        var stride = segments + 1;
        for (var y = 0; y < rings; y++)
        {
            for (var x = 0; x < segments; x++)
            {
                var i0 = y * stride + x;
                var i1 = i0 + 1;
                var i2 = i0 + stride + 1;
                var i3 = i0 + stride;

                indices.Add(i0);
                indices.Add(i1);
                indices.Add(i2);
                indices.Add(i0);
                indices.Add(i2);
                indices.Add(i3);
            }
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
