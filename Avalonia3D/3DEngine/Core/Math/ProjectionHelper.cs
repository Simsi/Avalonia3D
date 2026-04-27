using System;
using System.Numerics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Math;

public readonly record struct Ray(Vector3 Origin, Vector3 Direction);

public static class ProjectionHelper
{
    public static Ray CreateRay(Camera3D camera, Vector2 viewportPosition, Vector2 viewportSize)
    {
        viewportSize.X = System.MathF.Max(viewportSize.X, 1f);
        viewportSize.Y = System.MathF.Max(viewportSize.Y, 1f);

        var aspect = viewportSize.X / viewportSize.Y;
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspect);

        if (!Matrix4x4.Invert(view, out var invView) || !Matrix4x4.Invert(projection, out var invProjection))
        {
            return new Ray(camera.Position, camera.Forward);
        }

        var x = ((2f * viewportPosition.X) / viewportSize.X) - 1f;
        var y = 1f - ((2f * viewportPosition.Y) / viewportSize.Y);

        var near = Vector4.Transform(new Vector4(x, y, 0f, 1f), invProjection);
        var far = Vector4.Transform(new Vector4(x, y, 1f, 1f), invProjection);

        if (System.MathF.Abs(near.W) < 0.00001f || System.MathF.Abs(far.W) < 0.00001f)
        {
            return new Ray(camera.Position, camera.Forward);
        }

        near /= near.W;
        far /= far.W;

        var worldNear = Vector4.Transform(near, invView);
        var worldFar = Vector4.Transform(far, invView);

        var origin = new Vector3(worldNear.X, worldNear.Y, worldNear.Z);
        var farPoint = new Vector3(worldFar.X, worldFar.Y, worldFar.Z);
        var direction = farPoint - origin;
        if (direction.LengthSquared() < 0.000001f)
        {
            direction = camera.Forward;
        }

        return new Ray(origin, Vector3.Normalize(direction));
    }

    public static bool TryProjectToViewport(
        Camera3D camera,
        Vector3 worldPosition,
        Vector2 viewportSize,
        out Vector2 viewportPosition,
        out float normalizedDepth)
    {
        viewportSize.X = System.MathF.Max(viewportSize.X, 1f);
        viewportSize.Y = System.MathF.Max(viewportSize.Y, 1f);

        var aspect = viewportSize.X / viewportSize.Y;
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspect);
        var clip = Vector4.Transform(Vector4.Transform(new Vector4(worldPosition, 1f), view), projection);

        if (System.MathF.Abs(clip.W) < 0.00001f || clip.W <= 0f)
        {
            viewportPosition = default;
            normalizedDepth = 1f;
            return false;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        if (ndc.Z < -1.1f || ndc.Z > 1.1f || ndc.X < -1.5f || ndc.X > 1.5f || ndc.Y < -1.5f || ndc.Y > 1.5f)
        {
            viewportPosition = default;
            normalizedDepth = ndc.Z;
            return false;
        }

        viewportPosition = new Vector2(
            (ndc.X * 0.5f + 0.5f) * viewportSize.X,
            (1f - (ndc.Y * 0.5f + 0.5f)) * viewportSize.Y);
        normalizedDepth = ndc.Z;
        return true;
    }
}
