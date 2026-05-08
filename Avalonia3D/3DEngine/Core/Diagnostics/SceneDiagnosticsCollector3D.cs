using System.Collections.Generic;
using System.Text;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Rendering.Capabilities;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Diagnostics;

public static class SceneDiagnosticsCollector3D
{
    public static IReadOnlyList<string> Collect(Scene3D scene, RendererCapabilities3D capabilities)
    {
        var lines = new List<string>();
        foreach (var obj in scene.Registry.SnapshotAllObjects())
        {
            var materialDiagnostics = MaterialFeatureDiagnostics3D.Validate(obj.Material, capabilities);
            foreach (var diagnostic in materialDiagnostics)
            {
                lines.Add($"{obj.Name}: {diagnostic.Code}: {diagnostic.Message}");
            }

            if (obj is ImportedModel3D imported)
            {
                foreach (var part in imported.ModelParts)
                {
                    if (part.SkinningDiagnostics.FallbackToBindPose)
                    {
                        lines.Add($"{imported.Name}/{part.Name}: SKINNING_FALLBACK: {part.SkinningDiagnostics.Reason}");
                    }
                }
            }
        }
        return lines;
    }

    public static string Format(Scene3D scene, RendererCapabilities3D capabilities)
    {
        var diagnostics = Collect(scene, capabilities);
        if (diagnostics.Count == 0) return "No scene diagnostics.";
        var builder = new StringBuilder();
        foreach (var line in diagnostics) builder.AppendLine(line);
        return builder.ToString().TrimEnd();
    }
}
