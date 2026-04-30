using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Preview;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;
using ProjectionHelper3D = ThreeDEngine.Core.Math.ProjectionHelper;

namespace ThreeDEngine.Avalonia.Preview;

public sealed partial class Scene3DPreviewControl
{
    private void UpdateSnippet(Object3D obj)
    {
        _snippetBox.Text = BuildSnippet(obj);
    }

    private string BuildSnippet(Object3D obj)
    {
        var variableName = obj.Parent is null ? "obj" : "part";
        var body = BuildObjectConstructionSnippet(obj, variableName, declareAndAdd: obj.Parent is null);
        if (obj.Parent is not null)
        {
            var path = _listedObjects.FirstOrDefault(e => ReferenceEquals(e.Object, obj))?.Path ?? obj.Name;
            var partName = path.Split('/').LastOrDefault() ?? obj.Name;
            return
                $"var part = root.FindPart(\"{Escape(partName)}\");\n" +
                "if (part is not null)\n{\n" +
                body +
                "}";
        }

        return body;
    }

    private string BuildObjectConstructionSnippet(Object3D obj, string variable, bool declareAndAdd = true)
    {
        var construction = BuildPrimitiveConstructionExpression(obj);
        if (declareAndAdd && construction.StartsWith("/*", StringComparison.Ordinal))
        {
            return $"// Construction export is not supported for {obj.GetType().FullName}. Create this object in source manually, then copy the property block from the inspector.\n";
        }

        var prefix = declareAndAdd
            ? $"var {variable} = {construction};\nscene.Add({variable});\n"
            : string.Empty;

        return
            prefix +
            $"{variable}.Name = \"{Escape(obj.Name)}\";\n" +
            $"{variable}.IsVisible = {FormatBool(obj.IsVisible)};\n" +
            $"{variable}.IsPickable = {FormatBool(obj.IsPickable)};\n" +
            $"{variable}.IsManipulationEnabled = {FormatBool(obj.IsManipulationEnabled)};\n" +
            $"{variable}.Position = new Vector3({FormatFloatCode(obj.Position.X)}, {FormatFloatCode(obj.Position.Y)}, {FormatFloatCode(obj.Position.Z)});\n" +
            $"{variable}.RotationDegrees = new Vector3({FormatFloatCode(obj.RotationDegrees.X)}, {FormatFloatCode(obj.RotationDegrees.Y)}, {FormatFloatCode(obj.RotationDegrees.Z)});\n" +
            $"{variable}.Scale = new Vector3({FormatFloatCode(obj.Scale.X)}, {FormatFloatCode(obj.Scale.Y)}, {FormatFloatCode(obj.Scale.Z)});\n" +
            $"{variable}.Material.BaseColor = new ColorRgba({FormatFloatCode(obj.Material.BaseColor.R)}, {FormatFloatCode(obj.Material.BaseColor.G)}, {FormatFloatCode(obj.Material.BaseColor.B)}, {FormatFloatCode(obj.Material.BaseColor.A)});\n" +
            $"{variable}.Material.Opacity = {FormatFloatCode(obj.Material.Opacity)};\n" +
            $"{variable}.Material.Lighting = LightingMode.{obj.Material.Lighting};\n" +
            $"{variable}.Material.Surface = SurfaceMode.{obj.Material.Surface};\n" +
            $"{variable}.Material.CullMode = CullMode.{obj.Material.CullMode};\n" +
            BuildRigidbodySnippet(variable, obj.Rigidbody);
    }

    private static void AppendRigidbodyBuildLines(StringBuilder sb, string targetExpression, Rigidbody3D? body)
    {
        if (body is null)
        {
            return;
        }

        sb.Append(targetExpression).Append(".Rigidbody = new Rigidbody3D { Mass = ").Append(FormatFloatCode(body.Mass))
            .Append(", IsKinematic = ").Append(FormatBool(body.IsKinematic))
            .Append(", UseGravity = ").Append(FormatBool(body.UseGravity))
            .Append(", Friction = ").Append(FormatFloatCode(body.Friction))
            .Append(", Restitution = ").Append(FormatFloatCode(body.Restitution))
            .AppendLine(" };");
    }

    private static string BuildRigidbodySnippet(string variable, Rigidbody3D? body)
    {
        if (body is null)
        {
            return string.Empty;
        }

        return $"{variable}.Rigidbody = new Rigidbody3D {{ Mass = {FormatFloatCode(body.Mass)}, IsKinematic = {FormatBool(body.IsKinematic)}, UseGravity = {FormatBool(body.UseGravity)}, Friction = {FormatFloatCode(body.Friction)}, Restitution = {FormatFloatCode(body.Restitution)} }};\n";
    }

    private static string BuildPrimitiveConstructionExpression(Object3D obj)
    {
        return obj switch
        {
            Box3D box => $"new Box3D {{ Width = {FormatFloatCode(box.Width)}, Height = {FormatFloatCode(box.Height)}, Depth = {FormatFloatCode(box.Depth)} }}",
            Rectangle3D rect => $"new Box3D {{ Width = {FormatFloatCode(rect.Width)}, Height = {FormatFloatCode(rect.Height)}, Depth = {FormatFloatCode(rect.Depth)} }}",
            Sphere3D sphere => $"new Sphere3D {{ Radius = {FormatFloatCode(sphere.Radius)}, Segments = {sphere.Segments}, Rings = {sphere.Rings} }}",
            Cylinder3D cylinder => $"new Cylinder3D {{ Radius = {FormatFloatCode(cylinder.Radius)}, Height = {FormatFloatCode(cylinder.Height)}, Segments = {cylinder.Segments} }}",
            Cone3D cone => $"new Cone3D {{ Radius = {FormatFloatCode(cone.Radius)}, Height = {FormatFloatCode(cone.Height)}, Segments = {cone.Segments} }}",
            Plane3D plane => $"new Plane3D {{ Width = {FormatFloatCode(plane.Width)}, Height = {FormatFloatCode(plane.Height)}, SegmentsX = {plane.SegmentsX}, SegmentsY = {plane.SegmentsY} }}",
            Ellipse3D ellipse => $"new Ellipse3D {{ Width = {FormatFloatCode(ellipse.Width)}, Height = {FormatFloatCode(ellipse.Height)}, Depth = {FormatFloatCode(ellipse.Depth)}, Segments = {ellipse.Segments} }}",
            _ => "/* Unsupported construction: create this type in source code, then apply the property block below. */"
        };
    }

}
