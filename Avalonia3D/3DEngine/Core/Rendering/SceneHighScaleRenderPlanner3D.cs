using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Shared high-scale planning primitives used by desktop and browser backends.
/// Backends still own their buffer residency/update strategy, but visibility, layer
/// discovery and coarse LOD policy must not drift between implementations.
/// </summary>
public static class SceneHighScaleRenderPlanner3D
{
    public static bool HasVisibleLayers(SceneFrameSnapshot3D snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        foreach (var layer in snapshot.HighScaleLayers)
        {
            if (IsVisible(layer)) return true;
        }

        return false;
    }

    public static void AddVisibleLayers(SceneFrameSnapshot3D snapshot, List<HighScaleInstanceLayer3D> output)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (output is null) throw new ArgumentNullException(nameof(output));
        output.Clear();
        foreach (var layer in snapshot.HighScaleLayers)
        {
            if (IsVisible(layer)) output.Add(layer);
        }
    }

    public static bool IsVisible(HighScaleInstanceLayer3D layer)
        => layer.IsVisible && layer.Instances.Count > 0;

    public static void EnsureChunks(HighScaleInstanceLayer3D layer)
    {
        if (layer.Chunks.RebuildRequested)
        {
            layer.Chunks.Rebuild(layer.Instances, layer.Template.LocalBounds);
        }
    }

    public static int ResolveVisibleChunkLimit(ScenePerformanceOptions performance, int visibleChunkCount)
        => performance.MaxVisibleHighScaleChunks > 0
            ? global::System.Math.Min(performance.MaxVisibleHighScaleChunks, visibleChunkCount)
            : visibleChunkCount;

    public static bool ShouldUseAggregateLayerBatches(HighScaleInstanceLayer3D layer, ScenePerformanceOptions performance)
        => performance.EnableHighScaleAggregateLayerBatches &&
           layer.Instances.Count > 0 &&
           layer.Instances.Count <= performance.HighScaleAggregateLayerInstanceThreshold;

    public static bool ShouldUseChunkLevelLodPlanning(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, ScenePerformanceOptions performance)
    {
        if (!performance.EnableHighScaleChunkLodPlanning) return false;
        if (layer.Instances.Count < performance.HighScaleChunkLodPlanningInstanceThreshold) return false;
        return chunk.InstanceIndices.Count >= performance.HighScaleChunkLodPlanningChunkThreshold;
    }

    public static HighScaleLodLevel3D ResolveChunkLod(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, Vector3 cameraPosition)
    {
        var center = chunk.Bounds.Center;
        var transform = Matrix4x4.CreateTranslation(center);
        return layer.LodPolicy.Resolve(cameraPosition, transform);
    }

    public static HighScaleLodSelectionPlan3D BuildLayerLodPlan(
        HighScaleInstanceLayer3D layer,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats,
        HighScaleLodSelectionPlan3D plan)
    {
        if (layer is null) throw new ArgumentNullException(nameof(layer));
        if (performance is null) throw new ArgumentNullException(nameof(performance));
        if (stats is null) throw new ArgumentNullException(nameof(stats));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        plan.Reset();
        var count = layer.Instances.Count;
        for (var index = 0; index < count; index++)
        {
            AddInstanceByLod(layer, index, cameraPosition, performance, stats, plan);
        }

        return plan;
    }

    public static HighScaleLodSelectionPlan3D BuildChunkLodPlan(
        HighScaleInstanceLayer3D layer,
        HighScaleChunk3D chunk,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats,
        HighScaleLodSelectionPlan3D plan)
    {
        if (layer is null) throw new ArgumentNullException(nameof(layer));
        if (chunk is null) throw new ArgumentNullException(nameof(chunk));
        if (performance is null) throw new ArgumentNullException(nameof(performance));
        if (stats is null) throw new ArgumentNullException(nameof(stats));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        plan.Reset();
        if (ShouldUseChunkLevelLodPlanning(layer, chunk, performance))
        {
            AddChunkAsSingleLod(layer, chunk, cameraPosition, performance, stats, plan);
            return plan;
        }

        foreach (var index in chunk.InstanceIndices)
        {
            AddInstanceByLod(layer, index, cameraPosition, performance, stats, plan);
        }

        return plan;
    }

    private static void AddInstanceByLod(
        HighScaleInstanceLayer3D layer,
        int instanceIndex,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats,
        HighScaleLodSelectionPlan3D plan)
    {
        if (performance.MaxHighScaleVisibleInstances > 0 && stats.HighScaleInstanceCount >= performance.MaxHighScaleVisibleInstances)
        {
            stats.LodCulledCount++;
            stats.CulledObjectCount++;
            return;
        }

        var record = layer.Instances[instanceIndex];
        var lod = layer.LodPolicy.Resolve(cameraPosition, record.Transform);
        if (lod == HighScaleLodLevel3D.Culled)
        {
            stats.LodCulledCount++;
            stats.CulledObjectCount++;
            return;
        }

        plan.Get(lod).Add(instanceIndex);
        stats.HighScaleInstanceCount++;
        AddLodStats(stats, lod, 1);
    }

    private static void AddChunkAsSingleLod(
        HighScaleInstanceLayer3D layer,
        HighScaleChunk3D chunk,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats,
        HighScaleLodSelectionPlan3D plan)
    {
        var lod = ResolveChunkLod(layer, chunk, cameraPosition);
        var indices = chunk.InstanceIndices;
        var remaining = performance.MaxHighScaleVisibleInstances > 0
            ? global::System.Math.Max(0, performance.MaxHighScaleVisibleInstances - stats.HighScaleInstanceCount)
            : indices.Count;
        var count = global::System.Math.Min(indices.Count, remaining);

        if (count <= 0 || lod == HighScaleLodLevel3D.Culled)
        {
            stats.LodCulledCount += indices.Count;
            stats.CulledObjectCount += indices.Count;
            return;
        }

        var target = plan.Get(lod);
        for (var i = 0; i < count; i++)
        {
            target.Add(indices[i]);
        }

        stats.HighScaleInstanceCount += count;
        AddLodStats(stats, lod, count);

        if (count < indices.Count)
        {
            var culled = indices.Count - count;
            stats.LodCulledCount += culled;
            stats.CulledObjectCount += culled;
        }
    }

    private static void AddLodStats(RenderStats stats, HighScaleLodLevel3D lod, int count)
    {
        if (lod == HighScaleLodLevel3D.Detailed) stats.LodDetailedCount += count;
        else if (lod == HighScaleLodLevel3D.Simplified) stats.LodSimplifiedCount += count;
        else if (lod == HighScaleLodLevel3D.Proxy) stats.LodProxyCount += count;
        else if (lod == HighScaleLodLevel3D.Billboard) stats.LodBillboardCount += count;
    }

}
