using System;
using System.Collections.Generic;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Single owner of backend-neutral render planning: ordinary extraction/grouping/sorting,
/// retained particle discovery and high-scale layer discovery.
/// </summary>
internal static class SceneRenderPlanBuilder3D
{
    private const int DefaultTransparentObjectSortThreshold = 256;
    private const int DefaultTransparentDepthBinCount = 16;

    public static SceneRenderPlan3D Build(
        SceneRenderFrameContext3D frame,
        SceneRenderPlanScratch3D scratch,
        RenderStats? stats = null,
        bool includeOrdinary = true,
        bool includeParticles = true,
        bool includeHighScale = true,
        bool frustumCullParticles = true)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (scratch is null) throw new ArgumentNullException(nameof(scratch));

        scratch.BeginFrame(frame.Snapshot);
        if (includeOrdinary)
        {
            BuildOrdinaryBatches(
                frame,
                scratch.OrdinaryItemScratch,
                scratch.OrdinaryBatches,
                scratch.TransparentOrdinaryItems,
                scratch.TransparentOrdinaryBatches,
                scratch.OrdinaryBatchScratch,
                scratch.TransparentBatchScratch,
                scratch,
                stats);
        }

        if (includeParticles)
        {
            SceneParticleRenderPlanner3D.BuildVisible(frame, scratch.ParticleItems, scratch, stats, frustumCull: frustumCullParticles);
        }

        if (includeHighScale)
        {
            AddVisibleHighScaleLayers(frame.Snapshot, scratch.HighScaleLayers);
        }

        SceneRenderCommandStream3D.BuildInto(
            scratch.OrdinaryBatches,
            scratch.TransparentOrdinaryItems,
            scratch.TransparentOrdinaryBatches,
            scratch.ParticleItems,
            scratch.HighScaleLayers,
            scratch.DrawCommands,
            scratch);
        RenderResourcePlanBuilder3D.BuildInto(
            frame,
            scratch.OrdinaryBatches,
            scratch.TransparentOrdinaryItems,
            scratch.TransparentOrdinaryBatches,
            scratch.ParticleItems,
            scratch.HighScaleLayers,
            includeOrdinary,
            includeParticles,
            includeHighScale,
            scratch.Resources);
        ApplyGeometryMemoryStats(stats, scratch.Resources);

        scratch.Plan.Reset(
            frame,
            scratch.OrdinaryBatches,
            scratch.TransparentOrdinaryItems,
            scratch.TransparentOrdinaryBatches,
            scratch.ParticleItems,
            scratch.HighScaleLayers,
            scratch.DrawCommands,
            scratch.Resources,
            includeOrdinary,
            includeParticles,
            includeHighScale);
        return scratch.Plan;
    }

    private static void ApplyGeometryMemoryStats(RenderStats? stats, RenderResourcePlan3D resources)
    {
        if (stats is null) return;
        var geometries = resources.Geometries;
        stats.GeometryResourceCount = geometries.Count;
        stats.GeometrySourceBytes = 0;
        stats.GeometryResidentBytes = 0;
        stats.GeometryCompactIndexBytesSaved = 0;
        stats.MaterializedWireframeGeometryCount = 0;
        for (var i = 0; i < geometries.Count; i++)
        {
            var geometry = geometries[i];
            stats.GeometrySourceBytes += geometry.EstimatedSourceVertexBytes + geometry.EstimatedIndexUploadBytes;
            stats.GeometryResidentBytes += geometry.EstimatedResidentBytes;
            stats.GeometryCompactIndexBytesSaved += geometry.Indices.LongLength * sizeof(int) - geometry.Indices.ByteCount;
            if (geometry.IsWireframeMaterialized) stats.MaterializedWireframeGeometryCount++;
        }
    }

    private static void BuildOrdinaryBatches(
        SceneRenderFrameContext3D frame,
        List<OrdinaryRenderItem3D> itemScratch,
        List<OrdinaryRenderBatch3D> output,
        List<TransparentOrdinaryRenderItem3D> transparentOutput,
        List<TransparentOrdinaryBatch3D> transparentBatchOutput,
        Dictionary<string, OrdinaryRenderBatch3D> batchScratch,
        Dictionary<string, TransparentOrdinaryBatch3D> transparentBatchScratch,
        SceneRenderPlanScratch3D scratch,
        RenderStats? stats = null)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (itemScratch is null) throw new ArgumentNullException(nameof(itemScratch));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (transparentOutput is null) throw new ArgumentNullException(nameof(transparentOutput));
        if (transparentBatchOutput is null) throw new ArgumentNullException(nameof(transparentBatchOutput));
        if (batchScratch is null) throw new ArgumentNullException(nameof(batchScratch));
        if (transparentBatchScratch is null) throw new ArgumentNullException(nameof(transparentBatchScratch));

        itemScratch.Clear();
        output.Clear();
        transparentOutput.Clear();
        transparentBatchOutput.Clear();
        batchScratch.Clear();
        transparentBatchScratch.Clear();

        SceneOrdinaryRenderItemBuilder3D.Build(frame.Scene, frame.Snapshot, itemScratch, scratch, stats);
        var cameraPosition = frame.Published.CameraPosition;
        for (var i = 0; i < itemScratch.Count; i++)
        {
            var item = itemScratch[i];
            if (item.Transparent)
            {
                transparentOutput.Add(TransparentOrdinaryRenderItem3D.FromItem(item, cameraPosition, i, scratch));
                continue;
            }

            var batchId = item.RetainedBatchId;
            if (!batchScratch.TryGetValue(batchId, out var batch))
            {
                batch = scratch.RentOrdinaryBatch();
                batch.Reset(batchId, item.LogicalMeshBatchKey, item.Mesh, item.Material);
                batchScratch.Add(batchId, batch);
                output.Add(batch);
            }

            batch.Add(item, cameraPosition);
        }

        output.Sort(OrdinaryRenderBatch3D.CompareForDraw);
        ApplyAdaptiveTransparentPolicy(frame, transparentOutput, transparentBatchOutput, transparentBatchScratch, scratch);
        itemScratch.Clear();
        batchScratch.Clear();
        transparentBatchScratch.Clear();
    }

    private static void ApplyAdaptiveTransparentPolicy(
        SceneRenderFrameContext3D frame,
        List<TransparentOrdinaryRenderItem3D> exactItems,
        List<TransparentOrdinaryBatch3D> adaptiveBatches,
        Dictionary<string, TransparentOrdinaryBatch3D> batchScratch,
        SceneRenderPlanScratch3D scratch)
    {
        exactItems.Sort(TransparentOrdinaryRenderItem3D.CompareForDraw);
        adaptiveBatches.Clear();
        batchScratch.Clear();

        var options = frame.Scene.Performance;
        var threshold = global::System.Math.Max(0, options.TransparentOrdinaryObjectSortThreshold < 0
            ? DefaultTransparentObjectSortThreshold
            : options.TransparentOrdinaryObjectSortThreshold);
        if (!options.EnableAdaptiveTransparentOrdinaryBatching || exactItems.Count <= threshold)
        {
            return;
        }

        BuildTransparentDepthBatches(exactItems, adaptiveBatches, batchScratch, scratch, global::System.Math.Max(1, options.TransparentOrdinaryDepthBinCount <= 0
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
        Dictionary<string, TransparentOrdinaryBatch3D> scratchMap,
        SceneRenderPlanScratch3D scratch,
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
        scratchMap.Clear();
        var sourceOrder = 0;
        for (var i = 0; i < exactItems.Count; i++)
        {
            var transparent = exactItems[i];
            var item = transparent.Item;
            var normalized = (transparent.SortDistanceSquared - minDistance) * inverseRange;
            var bin = global::System.Math.Clamp((int)(normalized * bins), 0, bins - 1);
            var batchId = scratch.GetTransparentDepthBatchId(item.RetainedBatchId, bin);
            if (!scratchMap.TryGetValue(batchId, out var batch))
            {
                batch = scratch.RentTransparentBatch();
                batch.Reset(batchId, item.LogicalMeshBatchKey, item.Mesh, item.Material, sourceOrder++, bin);
                scratchMap.Add(batchId, batch);
                output.Add(batch);
            }

            batch.Add(transparent);
        }
    }

    public static void AddVisibleHighScaleLayers(SceneFrameSnapshot3D snapshot, List<HighScaleInstanceLayer3D> output)
        => SceneHighScaleRenderPlanner3D.AddVisibleLayers(snapshot, output);
}
