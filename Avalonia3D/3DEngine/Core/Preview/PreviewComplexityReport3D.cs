using System;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Preview;

public sealed class PreviewComplexityReport3D
{
    public int ObjectCount { get; init; }
    public int RenderableObjectCount { get; init; }
    public int CompositeCount { get; init; }
    public int HighScaleLayerCount { get; init; }
    public int HighScaleInstanceCount { get; init; }
    public int HighScaleTemplatePartCount { get; init; }
    public int EstimatedTriangles { get; init; }
    public int EstimatedDrawCalls { get; init; }
    public int WarningCount { get; init; }
    public string Summary { get; init; } = string.Empty;

    public static PreviewComplexityReport3D Analyze(Scene3D scene)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        var objectCount = 0;
        var renderableObjectCount = 0;
        var compositeCount = 0;
        var highScaleLayerCount = 0;
        var highScaleInstanceCount = 0;
        var highScaleTemplatePartCount = 0;
        var estimatedTriangles = 0;
        var estimatedDrawCalls = 0;
        var warningCount = 0;

        foreach (var obj in scene.Registry.AllObjects)
        {
            objectCount++;
            if (obj is CompositeObject3D) compositeCount++;

            if (obj is HighScaleInstanceLayer3D layer)
            {
                highScaleLayerCount++;
                highScaleInstanceCount += layer.Instances.Count;
                highScaleTemplatePartCount += layer.Template.Parts.Count;
                estimatedDrawCalls += System.Math.Max(1, layer.Template.Parts.Count) * System.Math.Max(1, layer.Chunks.Chunks.Count);
                foreach (var part in layer.Template.Parts)
                {
                    estimatedTriangles += (part.Mesh.Indices.Length / 3) * layer.Instances.Count;
                }

                continue;
            }

            if (!obj.UseMeshRendering || !obj.IsVisible) continue;
            renderableObjectCount++;
            var mesh = obj.GetMesh();
            estimatedTriangles += mesh.Indices.Length / 3;
            estimatedDrawCalls++;
        }

        if (objectCount > 2000) warningCount++;
        if (estimatedDrawCalls > 500) warningCount++;
        if (estimatedTriangles > 1_000_000) warningCount++;
        if (highScaleInstanceCount > 0 && highScaleTemplatePartCount == 0) warningCount++;

        return new PreviewComplexityReport3D
        {
            ObjectCount = objectCount,
            RenderableObjectCount = renderableObjectCount,
            CompositeCount = compositeCount,
            HighScaleLayerCount = highScaleLayerCount,
            HighScaleInstanceCount = highScaleInstanceCount,
            HighScaleTemplatePartCount = highScaleTemplatePartCount,
            EstimatedTriangles = estimatedTriangles,
            EstimatedDrawCalls = estimatedDrawCalls,
            WarningCount = warningCount,
            Summary = BuildSummary(objectCount, renderableObjectCount, compositeCount, highScaleLayerCount, highScaleInstanceCount, highScaleTemplatePartCount, estimatedTriangles, estimatedDrawCalls, warningCount)
        };
    }

    private static string BuildSummary(
        int objectCount,
        int renderableObjectCount,
        int compositeCount,
        int highScaleLayerCount,
        int highScaleInstanceCount,
        int highScaleTemplatePartCount,
        int estimatedTriangles,
        int estimatedDrawCalls,
        int warningCount)
    {
        var text =
            $"Objects: {objectCount}\n" +
            $"Renderable objects: {renderableObjectCount}\n" +
            $"Composites: {compositeCount}\n" +
            $"High-scale layers: {highScaleLayerCount}\n" +
            $"High-scale instances: {highScaleInstanceCount}\n" +
            $"Template parts: {highScaleTemplatePartCount}\n" +
            $"Estimated triangles: {estimatedTriangles}\n" +
            $"Estimated draw calls: {estimatedDrawCalls}\n";

        if (warningCount == 0)
        {
            return text + "Warnings: none";
        }

        return text +
               $"Warnings: {warningCount}\n" +
               "Check whether this component should expose simplified/proxy LOD and a high-scale template before using it thousands of times.";
    }
}
