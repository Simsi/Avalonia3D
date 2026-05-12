using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Rendering;

internal readonly record struct ControlPlaneRenderItem3D(
    ControlPlane3D Plane,
    Vector3 Corner0,
    Vector3 Corner1,
    Vector3 Corner2,
    Vector3 Corner3,
    Vector3 Center,
    float ExtentX,
    float ExtentY,
    float RollRadians,
    float Depth)
{
    public bool AlwaysFaceCamera => Plane.AlwaysFaceCamera;
    public string Id => Plane.Id;

    public void CopyCorners(Span<Vector3> destination)
    {
        if (destination.Length < 4)
        {
            throw new ArgumentException("At least four corners are required.", nameof(destination));
        }

        destination[0] = Corner0;
        destination[1] = Corner1;
        destination[2] = Corner2;
        destination[3] = Corner3;
    }
}

/// <summary>
/// Shared ControlPlane3D extraction for desktop OpenGL and browser WebGL.
/// Backends only own texture upload and draw execution; visibility, billboard corners,
/// extents and stable back-to-front order are planned here.
/// </summary>
internal static class ControlPlaneRenderPlanner3D
{
    public static void Build(SceneFrameSnapshot3D snapshot, Camera3D camera, List<ControlPlaneRenderItem3D> output)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (camera is null) throw new ArgumentNullException(nameof(camera));
        if (output is null) throw new ArgumentNullException(nameof(output));

        output.Clear();
        Span<Vector3> corners = stackalloc Vector3[4];
        for (var i = 0; i < snapshot.AllObjects.Length; i++)
        {
            if (snapshot.AllObjects[i] is not ControlPlane3D plane || !plane.IsVisible || plane.Snapshot is null)
            {
                continue;
            }

            ControlPlaneGeometry.GetWorldCorners(plane, camera, corners);
            var center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            var extentX = plane.Width * 0.5f;
            var extentY = plane.Height * 0.5f;
            if (plane.AlwaysFaceCamera)
            {
                var model = plane.GetModelMatrix();
                var worldScaleX = Vector3.TransformNormal(Vector3.UnitX, model).Length();
                var worldScaleY = Vector3.TransformNormal(Vector3.UnitY, model).Length();
                extentX = plane.Width * 0.5f * MathF.Max(worldScaleX, 0.0001f);
                extentY = plane.Height * 0.5f * MathF.Max(worldScaleY, 0.0001f);
            }

            var depth = 0f;
            for (var cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                depth += Vector3.DistanceSquared(camera.Position, corners[cornerIndex]);
            }

            output.Add(new ControlPlaneRenderItem3D(
                plane,
                corners[0],
                corners[1],
                corners[2],
                corners[3],
                center,
                extentX,
                extentY,
                plane.RotationDegrees.Z * (MathF.PI / 180f),
                depth / 4f));
        }

        output.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));
    }
}
