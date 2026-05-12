using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Rendering.Shadows;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Single owner of backend-neutral render planning: ordinary extraction/grouping/sorting,
/// retained particle discovery, high-scale layer discovery and shadow frame data.
/// </summary>
public static class SceneRenderPlanBuilder3D
{
    private const int DefaultTransparentObjectSortThreshold = 256;
    private const int DefaultTransparentDepthBinCount = 16;

    public static SceneRenderPlan3D Build(
        SceneRenderFrameContext3D frame,
        Func<ModelPart3D?, bool>? requiresCpuSkinFallback = null,
        RenderStats? stats = null,
        bool includeOrdinary = true,
        bool includeParticles = true,
        bool includeHighScale = true,
        bool frustumCullParticles = true)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));

        var ordinaryBatches = new List<OrdinaryRenderBatch3D>();
        var transparentOrdinaryItems = new List<TransparentOrdinaryRenderItem3D>();
        var transparentOrdinaryBatches = new List<TransparentOrdinaryBatch3D>();
        if (includeOrdinary)
        {
            var ordinaryItems = new List<OrdinaryRenderItem3D>(global::System.Math.Max(64, frame.Snapshot.Renderables.Length));
            var batchMap = new Dictionary<string, OrdinaryRenderBatch3D>(StringComparer.Ordinal);
            BuildOrdinaryBatches(frame, ordinaryItems, ordinaryBatches, transparentOrdinaryItems, transparentOrdinaryBatches, batchMap, requiresCpuSkinFallback, stats);
        }

        var particleItems = new List<ParticleRenderItem3D>();
        if (includeParticles)
        {
            SceneParticleRenderPlanner3D.BuildVisible(frame, particleItems, stats, frustumCull: frustumCullParticles);
        }

        var highScaleLayers = new List<HighScaleInstanceLayer3D>();
        if (includeHighScale)
        {
            AddVisibleHighScaleLayers(frame.Snapshot, highScaleLayers);
        }

        var shadow = DirectionalShadowResolver3D.Resolve(frame.Scene, frame.Snapshot);
        var drawCommands = SceneRenderCommandStream3D.Build(ordinaryBatches, transparentOrdinaryItems, transparentOrdinaryBatches, particleItems, highScaleLayers);
        var shadowCommands = SceneRenderCommandStream3D.BuildShadowCasterCommands(drawCommands);
        var resources = RenderResourcePlanBuilder3D.Build(
            frame,
            ordinaryBatches,
            transparentOrdinaryItems,
            transparentOrdinaryBatches,
            particleItems,
            highScaleLayers,
            includeOrdinary,
            includeParticles,
            includeHighScale);
        return new SceneRenderPlan3D(
            frame,
            shadow,
            ordinaryBatches,
            transparentOrdinaryItems,
            transparentOrdinaryBatches,
            particleItems,
            highScaleLayers,
            drawCommands,
            shadowCommands,
            resources,
            includeOrdinary,
            includeParticles,
            includeHighScale);
    }

    public static void BuildOrdinaryBatches(
        SceneRenderFrameContext3D frame,
        List<OrdinaryRenderItem3D> itemScratch,
        List<OrdinaryRenderBatch3D> output,
        List<TransparentOrdinaryRenderItem3D> transparentOutput,
        List<TransparentOrdinaryBatch3D> transparentBatchOutput,
        Dictionary<string, OrdinaryRenderBatch3D> batchScratch,
        Func<ModelPart3D?, bool>? requiresCpuSkinFallback = null,
        RenderStats? stats = null)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (itemScratch is null) throw new ArgumentNullException(nameof(itemScratch));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (transparentOutput is null) throw new ArgumentNullException(nameof(transparentOutput));
        if (transparentBatchOutput is null) throw new ArgumentNullException(nameof(transparentBatchOutput));
        if (batchScratch is null) throw new ArgumentNullException(nameof(batchScratch));

        itemScratch.Clear();
        output.Clear();
        transparentOutput.Clear();
        transparentBatchOutput.Clear();
        batchScratch.Clear();

        SceneOrdinaryRenderItemBuilder3D.Build(frame.Scene, frame.Snapshot, itemScratch, requiresCpuSkinFallback, stats);
        var cameraPosition = frame.Scene.Camera.Position;
        for (var i = 0; i < itemScratch.Count; i++)
        {
            var item = itemScratch[i];
            if (item.Transparent)
            {
                transparentOutput.Add(TransparentOrdinaryRenderItem3D.FromItem(item, cameraPosition, i));
                continue;
            }

            var batchId = item.RetainedBatchId;
            if (!batchScratch.TryGetValue(batchId, out var batch))
            {
                batch = new OrdinaryRenderBatch3D();
                batch.Reset(batchId, item.LogicalMeshBatchKey, item.Mesh, item.Material);
                batchScratch.Add(batchId, batch);
                output.Add(batch);
            }

            batch.Add(item, cameraPosition);
        }

        output.Sort(OrdinaryRenderBatch3D.CompareForDraw);
        ApplyAdaptiveTransparentPolicy(frame, transparentOutput, transparentBatchOutput);
        itemScratch.Clear();
        batchScratch.Clear();
    }

    private static void ApplyAdaptiveTransparentPolicy(
        SceneRenderFrameContext3D frame,
        List<TransparentOrdinaryRenderItem3D> exactItems,
        List<TransparentOrdinaryBatch3D> adaptiveBatches)
    {
        exactItems.Sort(TransparentOrdinaryRenderItem3D.CompareForDraw);
        adaptiveBatches.Clear();

        var options = frame.Scene.Performance;
        var threshold = global::System.Math.Max(0, options.TransparentOrdinaryObjectSortThreshold < 0
            ? DefaultTransparentObjectSortThreshold
            : options.TransparentOrdinaryObjectSortThreshold);
        if (!options.EnableAdaptiveTransparentOrdinaryBatching || exactItems.Count <= threshold)
        {
            return;
        }

        BuildTransparentDepthBatches(exactItems, adaptiveBatches, global::System.Math.Max(1, options.TransparentOrdinaryDepthBinCount <= 0
            ? DefaultTransparentDepthBinCount
            : options.TransparentOrdinaryDepthBinCount));
        if (adaptiveBatches.Count == 0 || adaptiveBatches.Count >= exactItems.Count)
        {
            adaptiveBatches.Clear();
            return;
        }

        exactItems.Clear();
        adaptiveBatches.Sort(TransparentOrdinaryBatch3D.CompareForDraw);
    }

    private static void BuildTransparentDepthBatches(
        List<TransparentOrdinaryRenderItem3D> exactItems,
        List<TransparentOrdinaryBatch3D> output,
        int binCount)
    {
        var minDistance = float.PositiveInfinity;
        var maxDistance = 0f;
        for (var i = 0; i < exactItems.Count; i++)
        {
            var distance = exactItems[i].SortDistanceSquared;
            if (distance < minDistance) minDistance = distance;
            if (distance > maxDistance) maxDistance = distance;
        }

        if (!float.IsFinite(minDistance) || maxDistance <= minDistance)
        {
            minDistance = 0f;
            maxDistance = 1f;
        }

        var bins = global::System.Math.Max(1, global::System.Math.Min(binCount, exactItems.Count));
        var inverseRange = 1f / global::System.Math.Max(0.0001f, maxDistance - minDistance);
        var scratch = new Dictionary<string, TransparentOrdinaryBatch3D>(StringComparer.Ordinal);
        var sourceOrder = 0;
        for (var i = 0; i < exactItems.Count; i++)
        {
            var transparent = exactItems[i];
            var item = transparent.Item;
            var normalized = (transparent.SortDistanceSquared - minDistance) * inverseRange;
            var bin = global::System.Math.Clamp((int)(normalized * bins), 0, bins - 1);
            var batchId = RenderId3D.StableHash(item.RetainedBatchId + ":tb:" + bin.ToString());
            if (!scratch.TryGetValue(batchId, out var batch))
            {
                batch = new TransparentOrdinaryBatch3D();
                batch.Reset(batchId, item.LogicalMeshBatchKey, item.Mesh, item.Material, sourceOrder++, bin);
                scratch.Add(batchId, batch);
                output.Add(batch);
            }

            batch.Add(transparent);
        }
    }

    public static void AddVisibleHighScaleLayers(SceneFrameSnapshot3D snapshot, List<HighScaleInstanceLayer3D> output)
        => SceneHighScaleRenderPlanner3D.AddVisibleLayers(snapshot, output);
}
