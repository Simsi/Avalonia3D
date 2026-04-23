using System;
using System.Numerics;
using Avalonia;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Controls;

internal readonly record struct ProjectedControlPlane(
    Point TopLeft,
    Point TopRight,
    Point BottomRight,
    Point BottomLeft,
    float Depth)
{
    public Rect Bounds
    {
        get
        {
            var minX = System.Math.Min(System.Math.Min(TopLeft.X, TopRight.X), System.Math.Min(BottomRight.X, BottomLeft.X));
            var minY = System.Math.Min(System.Math.Min(TopLeft.Y, TopRight.Y), System.Math.Min(BottomRight.Y, BottomLeft.Y));
            var maxX = System.Math.Max(System.Math.Max(TopLeft.X, TopRight.X), System.Math.Max(BottomRight.X, BottomLeft.X));
            var maxY = System.Math.Max(System.Math.Max(TopLeft.Y, TopRight.Y), System.Math.Max(BottomRight.Y, BottomLeft.Y));
            return new Rect(minX, minY, System.Math.Max(1.0, maxX - minX), System.Math.Max(1.0, maxY - minY));
        }
    }
}

internal static class ControlPlaneGeometry
{
    public static Vector3[] GetWorldCorners(ControlPlane3D plane, Camera3D camera)
    {
        var hw = plane.Width * 0.5f;
        var hh = plane.Height * 0.5f;

        if (plane.AlwaysFaceCamera)
        {
            var right = Vector3.Normalize(camera.Right);
            var up = Vector3.Normalize(camera.Up);

            var radiansZ = plane.RotationDegrees.Z * (System.MathF.PI / 180f);
            if (System.MathF.Abs(radiansZ) > 0.0001f)
            {
                var cos = System.MathF.Cos(radiansZ);
                var sin = System.MathF.Sin(radiansZ);
                var rotatedRight = (right * cos) + (up * sin);
                var rotatedUp = (-right * sin) + (up * cos);
                right = rotatedRight;
                up = rotatedUp;
            }

            right *= plane.Scale.X * hw;
            up *= plane.Scale.Y * hh;

            return new[]
            {
                plane.Position - right + up,
                plane.Position + right + up,
                plane.Position + right - up,
                plane.Position - right - up
            };
        }

        var model = plane.GetModelMatrix();
        return new[]
        {
            Vector3.Transform(new Vector3(-hw, hh, 0f), model),
            Vector3.Transform(new Vector3(hw, hh, 0f), model),
            Vector3.Transform(new Vector3(hw, -hh, 0f), model),
            Vector3.Transform(new Vector3(-hw, -hh, 0f), model)
        };
    }

    public static bool TryProject(ControlPlane3D plane, Camera3D camera, Vector2 viewportSize, out ProjectedControlPlane projected)
    {
        var corners = GetWorldCorners(plane, camera);
        var points = new Point[4];
        var depth = 0f;

        for (var i = 0; i < corners.Length; i++)
        {
            if (!ProjectionHelper.TryProjectToViewport(camera, corners[i], viewportSize, out var screen, out var z))
            {
                projected = default;
                return false;
            }

            points[i] = new Point(screen.X, screen.Y);
            depth += z;
        }

        projected = new ProjectedControlPlane(points[0], points[1], points[2], points[3], depth / 4f);
        return true;
    }

    public static bool TryMapWorldHitToControl(ControlPlane3D plane, Camera3D camera, Vector3 worldHit, out Point pixelPoint)
    {
        if (!TryMapWorldHitToControlUnclamped(plane, camera, worldHit, out pixelPoint))
        {
            return false;
        }

        return pixelPoint.X >= 0d && pixelPoint.X <= System.Math.Max(plane.RenderPixelWidth, 1) &&
               pixelPoint.Y >= 0d && pixelPoint.Y <= System.Math.Max(plane.RenderPixelHeight, 1);
    }

    public static bool TryMapWorldHitToControlUnclamped(ControlPlane3D plane, Camera3D camera, Vector3 worldHit, out Point pixelPoint)
    {
        float localX;
        float localY;

        if (plane.AlwaysFaceCamera)
        {
            var right = Vector3.Normalize(camera.Right);
            var up = Vector3.Normalize(camera.Up);

            var radiansZ = plane.RotationDegrees.Z * (System.MathF.PI / 180f);
            if (System.MathF.Abs(radiansZ) > 0.0001f)
            {
                var cos = System.MathF.Cos(radiansZ);
                var sin = System.MathF.Sin(radiansZ);
                var rotatedRight = (right * cos) + (up * sin);
                var rotatedUp = (-right * sin) + (up * cos);
                right = rotatedRight;
                up = rotatedUp;
            }

            var relative = worldHit - plane.Position;
            localX = Vector3.Dot(relative, right);
            localY = Vector3.Dot(relative, up);
        }
        else
        {
            if (!Matrix4x4.Invert(plane.GetModelMatrix(), out var invModel))
            {
                pixelPoint = default;
                return false;
            }

            var local = Vector3.Transform(worldHit, invModel);
            localX = local.X;
            localY = local.Y;
        }

        var u = (localX / System.MathF.Max(plane.Width * 0.5f, 0.0001f) + 1f) * 0.5f;
        var v = 1f - ((localY / System.MathF.Max(plane.Height * 0.5f, 0.0001f) + 1f) * 0.5f);

        pixelPoint = new Point(
            u * System.Math.Max(plane.RenderPixelWidth, 1),
            v * System.Math.Max(plane.RenderPixelHeight, 1));
        return true;
    }

    public static bool TryIntersectInfinitePlane(ControlPlane3D plane, Camera3D camera, Ray ray, out Vector3 worldPoint)
    {
        var corners = GetWorldCorners(plane, camera);
        var normal = Vector3.Normalize(Vector3.Cross(corners[1] - corners[0], corners[3] - corners[0]));
        var denominator = Vector3.Dot(normal, ray.Direction);
        if (System.MathF.Abs(denominator) < 0.00001f)
        {
            worldPoint = default;
            return false;
        }

        var t = Vector3.Dot(corners[0] - ray.Origin, normal) / denominator;
        if (t <= 0f)
        {
            worldPoint = default;
            return false;
        }

        worldPoint = ray.Origin + (ray.Direction * t);
        return true;
    }
}
