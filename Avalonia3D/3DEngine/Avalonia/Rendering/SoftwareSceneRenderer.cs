using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Rendering;

internal static class SoftwareSceneRenderer
{
    private abstract record DrawCommand(double Depth);
    private sealed record TriangleCommand(Point A, Point B, Point C, double DepthValue, IBrush Brush) : DrawCommand(DepthValue);
    private sealed record ControlCommand(ProjectedControlPlane Projected, ControlPlane3D Plane, RenderTargetBitmap Bitmap, double DepthValue) : DrawCommand(DepthValue);

    public static void Render(DrawingContext context, Scene3D scene, Rect bounds)
    {
        var width = System.Math.Max(bounds.Width, 1.0);
        var height = System.Math.Max(bounds.Height, 1.0);
        context.FillRectangle(new SolidColorBrush(ToAvaloniaColor(scene.BackgroundColor)), bounds);

        var aspect = (float)(width / height);
        var view = scene.Camera.GetViewMatrix();
        var projection = scene.Camera.GetProjectionMatrix(aspect);
        var commands = new List<DrawCommand>();
        var viewport = new Vector2((float)width, (float)height);

        foreach (var obj in scene.Registry.AllObjects)
        {
            if (!obj.IsVisible)
            {
                continue;
            }

            if (obj is ControlPlane3D controlPlane)
            {
                if (controlPlane.Snapshot is not null && ControlPlaneGeometry.TryProject(controlPlane, scene.Camera, viewport, out var projected))
                {
                    var controlCorners = ControlPlaneGeometry.GetWorldCorners(controlPlane, scene.Camera);
                    var controlDepth = 0d;
                    for (var cornerIndex = 0; cornerIndex < controlCorners.Length; cornerIndex++)
                    {
                        controlDepth += (controlCorners[cornerIndex] - scene.Camera.Position).LengthSquared();
                    }

                    commands.Add(new ControlCommand(projected, controlPlane, controlPlane.Snapshot, controlDepth / controlCorners.Length));
                }

                continue;
            }

            if (!obj.UseMeshRendering)
            {
                continue;
            }

            var mesh = obj.GetMesh();
            var model = obj.GetModelMatrix();
            var mvp = model * view * projection;

            var color = obj.Material.EffectiveColor;
            if (obj.IsEffectivelyHovered)
            {
                color = color.BlendTowards(ColorRgba.White, 0.10f);
            }
            if (obj.IsEffectivelySelected)
            {
                color = color.BlendTowards(ColorRgba.White, 0.22f);
            }

            var brush = new SolidColorBrush(ToAvaloniaColor(color));

            for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                var i0 = mesh.Indices[i];
                var i1 = mesh.Indices[i + 1];
                var i2 = mesh.Indices[i + 2];

                var wp0 = Vector3.Transform(mesh.Positions[i0], model);
                var wp1 = Vector3.Transform(mesh.Positions[i1], model);
                var wp2 = Vector3.Transform(mesh.Positions[i2], model);
                var depth = (wp0 - scene.Camera.Position).LengthSquared() +
                            (wp1 - scene.Camera.Position).LengthSquared() +
                            (wp2 - scene.Camera.Position).LengthSquared();

                if (!TryProject(mesh.Positions[i0], mvp, width, height, out var p0) ||
                    !TryProject(mesh.Positions[i1], mvp, width, height, out var p1) ||
                    !TryProject(mesh.Positions[i2], mvp, width, height, out var p2))
                {
                    continue;
                }

                commands.Add(new TriangleCommand(p0, p1, p2, depth, brush));
            }
        }

        foreach (var command in commands.OrderByDescending(t => t.Depth))
        {
            if (command is TriangleCommand triangle)
            {
                var geometry = new StreamGeometry();
                var gc = geometry.Open();
                gc.BeginFigure(triangle.A, true);
                gc.LineTo(triangle.B);
                gc.LineTo(triangle.C);
                gc.EndFigure(true);
                gc.Dispose();
                context.DrawGeometry(triangle.Brush, null, geometry);
            }
            else if (command is ControlCommand control)
            {
                RenderControlPlane(context, control);
            }
        }
    }

    private static void RenderControlPlane(DrawingContext context, ControlCommand command)
    {
        var plane = command.Plane;
        var projected = command.Projected;
        var bitmap = command.Bitmap;
        var pixelWidth = System.Math.Max(plane.RenderPixelWidth, 1);
        var pixelHeight = System.Math.Max(plane.RenderPixelHeight, 1);

        var ux = (projected.TopRight.X - projected.TopLeft.X) / pixelWidth;
        var uy = (projected.TopRight.Y - projected.TopLeft.Y) / pixelWidth;
        var vx = (projected.BottomLeft.X - projected.TopLeft.X) / pixelHeight;
        var vy = (projected.BottomLeft.Y - projected.TopLeft.Y) / pixelHeight;

        var transform = new Matrix(ux, uy, vx, vy, projected.TopLeft.X, projected.TopLeft.Y);
        var pushed = context.PushTransform(transform);
        context.DrawImage(bitmap, new Rect(0, 0, pixelWidth, pixelHeight), new Rect(0, 0, pixelWidth, pixelHeight));
        pushed.Dispose();

        if (!plane.IsHovered && !plane.IsSelected)
        {
            return;
        }

        var stroke = plane.IsSelected
            ? new Pen(Brushes.White, 2)
            : new Pen(Brushes.White, 1);

        var outline = new StreamGeometry();
        var gc = outline.Open();
        gc.BeginFigure(projected.TopLeft, false);
        gc.LineTo(projected.TopRight);
        gc.LineTo(projected.BottomRight);
        gc.LineTo(projected.BottomLeft);
        gc.EndFigure(true);
        gc.Dispose();
        context.DrawGeometry(null, stroke, outline);
    }

    private static Color ToAvaloniaColor(ColorRgba color)
    {
        return Color.FromArgb(
            (byte)(System.Math.Clamp(color.A, 0f, 1f) * 255f),
            (byte)(System.Math.Clamp(color.R, 0f, 1f) * 255f),
            (byte)(System.Math.Clamp(color.G, 0f, 1f) * 255f),
            (byte)(System.Math.Clamp(color.B, 0f, 1f) * 255f));
    }

    private static bool TryProject(Vector3 position, Matrix4x4 mvp, double width, double height, out Point point)
    {
        var clip = Vector4.Transform(new Vector4(position, 1f), mvp);
        if (System.MathF.Abs(clip.W) < 0.00001f)
        {
            point = default;
            return false;
        }

        if (clip.W <= 0f)
        {
            point = default;
            return false;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        if (ndc.Z < -1.1f || ndc.Z > 1.1f || ndc.X < -1.5f || ndc.X > 1.5f || ndc.Y < -1.5f || ndc.Y > 1.5f)
        {
            point = default;
            return false;
        }

        var x = (ndc.X * 0.5 + 0.5) * width;
        var y = (1.0 - (ndc.Y * 0.5 + 0.5)) * height;
        point = new Point(x, y);
        return true;
    }
}
