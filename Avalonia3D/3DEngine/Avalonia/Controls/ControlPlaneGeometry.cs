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
            GetBillboardBasis(plane, camera, out var center, out var right, out var up, out var extentX, out var extentY);
            right *= extentX;
            up *= extentY;

            return new[]
            {
                center - right + up,
                center + right + up,
                center + right - up,
                center - right - up
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
        float u;
        float v;

        if (plane.AlwaysFaceCamera)
        {
            GetBillboardBasis(plane, camera, out var center, out var right, out var up, out var extentX, out var extentY);
            var relative = worldHit - center;
            var localX = Vector3.Dot(relative, right);
            var localY = Vector3.Dot(relative, up);
            u = (localX / System.MathF.Max(extentX, 0.0001f) + 1f) * 0.5f;
            v = 1f - ((localY / System.MathF.Max(extentY, 0.0001f) + 1f) * 0.5f);
        }
        else
        {
            if (!Matrix4x4.Invert(plane.GetModelMatrix(), out var invModel))
            {
                pixelPoint = default;
                return false;
            }

            var local = Vector3.Transform(worldHit, invModel);
            u = (local.X / System.MathF.Max(plane.Width * 0.5f, 0.0001f) + 1f) * 0.5f;
            v = 1f - ((local.Y / System.MathF.Max(plane.Height * 0.5f, 0.0001f) + 1f) * 0.5f);
        }

        pixelPoint = new Point(
            u * System.Math.Max(plane.RenderPixelWidth, 1),
            v * System.Math.Max(plane.RenderPixelHeight, 1));
        return true;
    }

    public static bool TryIntersectInfinitePlane(ControlPlane3D plane, Camera3D camera, Ray ray, out Vector3 worldPoint)
    {
        var corners = GetWorldCorners(plane, camera);
        var normalVector = Vector3.Cross(corners[1] - corners[0], corners[3] - corners[0]);
        if (normalVector.LengthSquared() < 0.000001f)
        {
            worldPoint = default;
            return false;
        }

        var normal = Vector3.Normalize(normalVector);
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

    private static void GetBillboardBasis(
        ControlPlane3D plane,
        Camera3D camera,
        out Vector3 center,
        out Vector3 right,
        out Vector3 up,
        out float extentX,
        out float extentY)
    {
        var model = plane.GetModelMatrix();
        center = Vector3.Transform(Vector3.Zero, model);

        right = camera.Right;
        if (right.LengthSquared() < 0.000001f)
        {
            right = Vector3.UnitX;
        }
        else
        {
            right = Vector3.Normalize(right);
        }

        up = Vector3.Cross(camera.Forward, right);
        if (up.LengthSquared() < 0.000001f)
        {
            up = camera.SafeUp;
        }
        if (up.LengthSquared() < 0.000001f)
        {
            up = Vector3.UnitY;
        }
        else
        {
            up = Vector3.Normalize(up);
        }

        var radiansZ = plane.RotationDegrees.Z * (System.MathF.PI / 180f);
        if (System.MathF.Abs(radiansZ) > 0.0001f)
        {
            var cos = System.MathF.Cos(radiansZ);
            var sin = System.MathF.Sin(radiansZ);
            var rotatedRight = (right * cos) + (up * sin);
            var rotatedUp = (-right * sin) + (up * cos);
            right = SafeNormalize(rotatedRight, Vector3.UnitX);
            up = SafeNormalize(rotatedUp, Vector3.UnitY);
        }

        var worldScaleX = Vector3.TransformNormal(Vector3.UnitX, model).Length();
        var worldScaleY = Vector3.TransformNormal(Vector3.UnitY, model).Length();
        extentX = plane.Width * 0.5f * System.MathF.Max(worldScaleX, 0.0001f);
        extentY = plane.Height * 0.5f * System.MathF.Max(worldScaleY, 0.0001f);
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        => value.LengthSquared() < 0.000001f ? fallback : Vector3.Normalize(value);
}
