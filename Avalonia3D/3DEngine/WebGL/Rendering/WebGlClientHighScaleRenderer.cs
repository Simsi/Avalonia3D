using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Rendering;

/// <summary>
/// v57 browser-owned high-scale runtime. C# uploads structural retained buffers and compact
/// binary patches; JS owns per-frame culling, LOD draw-list generation and draw dispatch.
/// </summary>
internal sealed class WebGlClientHighScaleRenderer
{
    private const int TransformFloatStride = 16;
    private const int StateFloatStride = 4;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HighScaleLodLevel3D[] RuntimeLods =
    {
        HighScaleLodLevel3D.Detailed,
        HighScaleLodLevel3D.Simplified,
        HighScaleLodLevel3D.Proxy,
        HighScaleLodLevel3D.Billboard
    };

    private readonly Dictionary<string, LayerRuntime> _layers = new(StringComparer.Ordinal);
    private readonly List<int> _dirtyTransformIndices = new(1024);
    private int[] _dirtyTransformScratch = Array.Empty<int>();
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();

    public bool HasRuntimeState => _layers.Count != 0;

    public void Reset(int hostId)
    {
        foreach (var layer in _layers.Values)
        {
            foreach (var batch in layer.Batches)
            {
                WebGlInterop.DestroyRetainedBatch(hostId, batch.BatchId);
            }

            WebGlInterop.DestroyHighScaleLayer(hostId, layer.LayerId);
        }

        _layers.Clear();
    }

    public void RenderFrame(int hostId, Scene3D scene, float width, float height, Matrix4x4 viewProjection, RenderStats stats)
    {
        stats.WebGlClientHighScaleRuntime = true;
        stats.WebGlClientGpuTransformAnimation = scene.Performance.EnableWebGlClientGpuTransformAnimation;
        EnsureSnapshots(hostId, scene, stats);
        ApplyPatches(hostId, scene, stats);

        var light = ResolveLight(scene);
        var frameJson = JsonSerializer.Serialize(new
        {
            width,
            height,
            clearColor = new[] { scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A },
            viewProjection = ToArray(viewProjection),
            cameraPosition = new[] { scene.Camera.Position.X, scene.Camera.Position.Y, scene.Camera.Position.Z },
            ambientLight = light.Ambient,
            directionalLightDirection = light.Direction,
            directionalLightColor = light.DirectionalColor,
            pointLightPosition = light.PointPosition,
            pointLightColor = light.PointColor,
            clientAnimationEnabled = scene.Performance.EnableWebGlClientGpuTransformAnimation,
            clientAnimationTime = scene.Performance.EnableWebGlClientGpuTransformAnimation ? (float)_animationClock.Elapsed.TotalSeconds : 0f,
            clientAnimationAmplitude = scene.Performance.WebGlClientGpuTransformAnimationAmplitude
        }, JsonOptions);

        var metrics = WebGlInterop.RenderHighScaleFrame(hostId, frameJson);
        ApplyJsMetrics(metrics, stats);
    }

    private void EnsureSnapshots(int hostId, Scene3D scene, RenderStats stats)
    {
        var liveLayerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            if (!layer.IsVisible || layer.Instances.Count == 0)
            {
                continue;
            }

            liveLayerIds.Add(layer.Id);
            var hasRuntime = _layers.TryGetValue(layer.Id, out var runtime);

            // Hard guard for the browser Transform Animation benchmark path.
            // If the JS-owned high-scale runtime already has a snapshot for this layer and
            // the topology/cardinality did not change, no C# chunk rebuild, no structural
            // hash, and no full transform upload may run. The animated motion is purely
            // shader-side and must not mutate the retained transform buffers.
            if (scene.Performance.EnableWebGlClientGpuTransformAnimation &&
                hasRuntime &&
                runtime!.CanReuseForGpuAnimation(layer, scene))
            {
                if (layer.Chunks.RebuildRequested)
                {
                    layer.Chunks.ClearRebuildRequested();
                }

                ClearDirtyTransformsForGpuAnimation(layer, runtime);
                continue;
            }

            if (layer.Chunks.RebuildRequested)
            {
                layer.Chunks.Rebuild(layer.Instances, layer.Template.LocalBounds);
            }

            var structuralVersion = BuildStructuralVersion(layer, scene);
            if (hasRuntime)
            {
                if (runtime!.StructuralVersion == structuralVersion)
                {
                    continue;
                }

                DestroyLayer(hostId, runtime);
            }

            runtime = BuildAndUploadLayer(hostId, layer, scene, structuralVersion, stats);
            _layers[layer.Id] = runtime;
            WebGlInterop.UploadHighScaleLayerSnapshot(hostId, layer.Id, runtime.SnapshotJson);
            layer.StateBuffer.ClearDirty();
        }

        var dead = new List<string>();
        foreach (var id in _layers.Keys)
        {
            if (!liveLayerIds.Contains(id)) dead.Add(id);
        }

        for (var i = 0; i < dead.Count; i++)
        {
            var runtime = _layers[dead[i]];
            DestroyLayer(hostId, runtime);
            _layers.Remove(dead[i]);
        }
    }

    private static int BuildStructuralVersion(HighScaleInstanceLayer3D layer, Scene3D scene)
    {
        // Browser client runtime buffers are structural only when the topology that feeds
        // retained batches changes. Use a deterministic sorted hash: Dictionary.Values
        // enumeration order is not a safe render-frame identity, and a volatile hash here
        // causes a full 10k transform/state upload every frame.
        var chunks = new List<HighScaleChunk3D>(layer.Chunks.Chunks);
        chunks.Sort(static (a, b) =>
        {
            var c = a.Key.X.CompareTo(b.Key.X);
            if (c != 0) return c;
            c = a.Key.Y.CompareTo(b.Key.Y);
            if (c != 0) return c;
            return a.Key.Z.CompareTo(b.Key.Z);
        });

        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + layer.Template.Id;
            hash = (hash * 31) + layer.Instances.Count;
            hash = (hash * 31) + layer.Template.Parts.Count;
            hash = (hash * 31) + (scene.Performance.EnableHighScalePaletteTexture ? 1 : 0);
            hash = (hash * 31) + layer.Chunks.CellSize.GetHashCode();
            hash = (hash * 31) + chunks.Count;
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                hash = (hash * 31) + chunk.Key.X;
                hash = (hash * 31) + chunk.Key.Y;
                hash = (hash * 31) + chunk.Key.Z;
                hash = (hash * 31) + chunk.InstanceIndices.Count;
            }

            return hash;
        }
    }

    private LayerRuntime BuildAndUploadLayer(int hostId, HighScaleInstanceLayer3D layer, Scene3D scene, int structuralVersion, RenderStats stats)
    {
        var runtime = new LayerRuntime(layer.Id, structuralVersion, layer.Template.Id, layer.Instances.Count, scene.Performance.EnableHighScalePaletteTexture);
        var chunks = new List<object>();

        foreach (var chunk in layer.Chunks.Chunks)
        {
            if (chunk.InstanceIndices.Count == 0)
            {
                continue;
            }

            var batchIdsByLod = new List<string>[4];
            for (var lodIndex = 0; lodIndex < batchIdsByLod.Length; lodIndex++)
            {
                batchIdsByLod[lodIndex] = new List<string>();
            }

            for (var lodIndex = 0; lodIndex < RuntimeLods.Length; lodIndex++)
            {
                var lod = RuntimeLods[lodIndex];
                var renderLod = lod == HighScaleLodLevel3D.Billboard ? HighScaleLodLevel3D.Proxy : lod;
                var parts = layer.Template.ResolveParts(renderLod);
                for (var partIndex = 0; partIndex < parts.Count; partIndex++)
                {
                    var part = parts[partIndex];
                    var batchId = BuildBatchId(layer, chunk.Key, renderLod, partIndex);
                    batchIdsByLod[lodIndex].Add(batchId);
                    if (runtime.BatchesById.ContainsKey(batchId))
                    {
                        continue;
                    }

                    var batch = BuildBatchRuntime(layer, scene, chunk.InstanceIndices, part, batchId, chunk.Bounds.Center);
                    runtime.Batches.Add(batch);
                    runtime.BatchesById[batch.BatchId] = batch;
                    UploadFullBatch(hostId, batch, stats);
                }
            }

            var center = chunk.Bounds.Center;
            var extents = chunk.Bounds.Size * 0.5f;
            // Conservative expansion compensates small animated movement between structural rebuilds.
            extents += new Vector3(System.MathF.Max(0.5f, layer.Chunks.CellSize * 0.10f));
            chunks.Add(new
            {
                id = chunk.Key.ToString(),
                cx = center.X,
                cy = center.Y,
                cz = center.Z,
                ex = extents.X,
                ey = extents.Y,
                ez = extents.Z,
                instanceCount = chunk.InstanceIndices.Count,
                batchesByLod = batchIdsByLod
            });
        }

        runtime.EnsureTransformVersionCapacity(layer.Instances.Count);
        for (var i = 0; i < layer.Instances.Count; i++)
        {
            runtime.TransformVersionsByInstance[i] = layer.Instances[i].TransformVersion;
        }
        ClearInitialTransformDirtyQueue(layer);

        runtime.SnapshotJson = JsonSerializer.Serialize(new
        {
            layerId = layer.Id,
            version = structuralVersion,
            visible = layer.IsVisible,
            detailedDistance = layer.LodPolicy.DetailedDistance,
            simplifiedDistance = layer.LodPolicy.SimplifiedDistance,
            proxyDistance = layer.LodPolicy.ProxyDistance,
            drawDistance = layer.LodPolicy.DrawDistance,
            enableBillboardFallback = layer.LodPolicy.EnableBillboardFallback,
            chunks
        }, JsonOptions);

        stats.TotalChunkCount += layer.Chunks.Chunks.Count;
        return runtime;
    }

    private static BatchRuntime BuildBatchRuntime(
        HighScaleInstanceLayer3D layer,
        Scene3D scene,
        IReadOnlyList<int> indices,
        CompositePartTemplate3D part,
        string batchId,
        Vector3 chunkCenter)
    {
        var alpha = ResolveChunkFadeAlpha(scene, layer, chunkCenter);
        var usePalette = scene.Performance.EnableHighScalePaletteTexture && part.UsesVertexMaterialSlots && layer.ColorResolver is null;
        var lighting = part.LightingMode == LightingMode.Lambert ? 1f : 0f;
        var batch = new BatchRuntime(batchId, part, usePalette, lighting, indices.Count)
        {
            StateVersion = layer.StateBuffer.Version,
            MaterialResolverVersion = layer.MaterialResolverVersion,
            LodPolicyVersion = layer.LodPolicy.Version,
            FadeAlpha = alpha
        };

        for (var i = 0; i < indices.Count; i++)
        {
            var instanceIndex = indices[i];
            batch.InstanceIndices[i] = instanceIndex;
            batch.InstanceOffsetMap[instanceIndex] = i;
            var record = layer.Instances[instanceIndex];
            batch.TransformVersions[i] = record.TransformVersion;
            WriteTransform(layer, instanceIndex, part, batch.TransformData, i * TransformFloatStride);
            if (usePalette) WritePaletteState(layer, instanceIndex, alpha, batch.StateData, i * StateFloatStride);
            else WriteColorState(layer, instanceIndex, part, alpha, batch.StateData, i * StateFloatStride);
        }

        if (usePalette)
        {
            batch.PaletteBytes = BuildPaletteBytes(layer.Template, part, out var width, out var height);
            batch.PaletteWidth = width;
            batch.PaletteHeight = height;
            batch.PaletteVersion = layer.MaterialResolverVersion;
        }

        return batch;
    }

    private static void UploadFullBatch(int hostId, BatchRuntime batch, RenderStats stats)
    {
        WebGlInterop.UploadRetainedBatchTransformsBytes(
            hostId,
            batch.BatchId,
            batch.Part.Mesh.ResourceKey,
            batch.LightingEnabled,
            batch.UsePalette,
            batch.InstanceCount,
            FloatsToBytes(batch.TransformData));
        WebGlInterop.UploadRetainedBatchStateBytes(
            hostId,
            batch.BatchId,
            batch.UsePalette,
            batch.PaletteWidth,
            batch.PaletteHeight,
            FloatsToBytes(batch.StateData),
            batch.PaletteBytes);
        stats.InstanceBufferUploads++;
        stats.StateBufferUploads++;
        stats.InstanceUploadBytes += batch.TransformData.Length * sizeof(float);
        stats.TransformUploadBytes += batch.TransformData.Length * sizeof(float);
        stats.StateUploadBytes += batch.StateData.Length * sizeof(float);
    }

    private void ApplyPatches(int hostId, Scene3D scene, RenderStats stats)
    {
        var start = Stopwatch.GetTimestamp();
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            if (!_layers.TryGetValue(layer.Id, out var runtime))
            {
                continue;
            }

            var dirtyTransformCount = scene.Performance.EnableWebGlClientGpuTransformAnimation
                ? ClearDirtyTransformsForGpuAnimation(layer, runtime)
                : BuildDirtyTransformIndices(layer, runtime);
            var stateDirty = layer.StateBuffer.HasDirtyState;
            if (dirtyTransformCount == 0 && !stateDirty)
            {
                continue;
            }

            foreach (var batch in runtime.Batches)
            {
                var transformDirtyCount = dirtyTransformCount == 0 ? 0 : UpdateDirtyTransforms(layer, batch, _dirtyTransformIndices);
                if (transformDirtyCount > 0)
                {
                    if (transformDirtyCount > System.Math.Max(32, batch.InstanceCount / 3))
                    {
                        WebGlInterop.UploadRetainedBatchTransformsBytes(
                            hostId,
                            batch.BatchId,
                            batch.Part.Mesh.ResourceKey,
                            batch.LightingEnabled,
                            batch.UsePalette,
                            batch.InstanceCount,
                            FloatsToBytes(batch.TransformData));
                        stats.InstanceBufferUploads++;
                        stats.InstanceUploadBytes += batch.TransformData.Length * sizeof(float);
                        stats.TransformUploadBytes += batch.TransformData.Length * sizeof(float);
                    }
                    else
                    {
                        batch.SortTransformDirtyOffsets();
                        UploadTransformRanges(hostId, batch, scene.Performance, stats);
                    }
                }

                var resolverChanged = batch.MaterialResolverVersion != layer.MaterialResolverVersion;
                var lodPolicyChanged = batch.LodPolicyVersion != layer.LodPolicy.Version;
                var forceFullState = resolverChanged || batch.StateVersion < 0;
                if (forceFullState)
                {
                    RebuildFullState(layer, batch);
                    WebGlInterop.UploadRetainedBatchStateBytes(hostId, batch.BatchId, batch.UsePalette, batch.PaletteWidth, batch.PaletteHeight, FloatsToBytes(batch.StateData), batch.PaletteBytes);
                    stats.StateBufferUploads++;
                    stats.StateUploadBytes += batch.StateData.Length * sizeof(float);
                    batch.StateVersion = layer.StateBuffer.Version;
                    batch.MaterialResolverVersion = layer.MaterialResolverVersion;
                    batch.LodPolicyVersion = layer.LodPolicy.Version;
                }
                else if (stateDirty)
                {
                    var stateDirtyCount = UpdateDirtyState(layer, batch);
                    if (stateDirtyCount > 0)
                    {
                        if (stateDirtyCount > System.Math.Max(32, batch.InstanceCount / 3))
                        {
                            WebGlInterop.UploadRetainedBatchStateBytes(hostId, batch.BatchId, batch.UsePalette, batch.PaletteWidth, batch.PaletteHeight, FloatsToBytes(batch.StateData), Array.Empty<byte>());
                            stats.StateBufferUploads++;
                            stats.StateUploadBytes += batch.StateData.Length * sizeof(float);
                        }
                        else
                        {
                            batch.SortStateDirtyOffsets();
                            UploadStateRanges(hostId, batch, scene.Performance, stats);
                        }
                    }

                    batch.StateVersion = layer.StateBuffer.Version;
                }
            }

            if (stateDirty)
            {
                foreach (var batch in runtime.Batches)
                {
                    batch.LodPolicyVersion = layer.LodPolicy.Version;
                }
            }

            layer.StateBuffer.ClearDirty();
        }

        stats.JsPatchMilliseconds += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    }

    private int BuildDirtyTransformIndices(HighScaleInstanceLayer3D layer, LayerRuntime runtime)
    {
        _dirtyTransformIndices.Clear();
        runtime.EnsureTransformVersionCapacity(layer.Instances.Count);
        EnsureTransformScratchCapacity(layer.Instances.Count);

        var drained = layer.Instances.DrainDirtyTransforms(_dirtyTransformScratch);
        for (var i = 0; i < drained; i++)
        {
            var instanceIndex = _dirtyTransformScratch[i];
            if ((uint)instanceIndex >= (uint)layer.Instances.Count)
            {
                continue;
            }

            var version = layer.Instances[instanceIndex].TransformVersion;
            if (runtime.TransformVersionsByInstance[instanceIndex] == version)
            {
                continue;
            }

            runtime.TransformVersionsByInstance[instanceIndex] = version;
            _dirtyTransformIndices.Add(instanceIndex);
        }

        return _dirtyTransformIndices.Count;
    }

    private int ClearDirtyTransformsForGpuAnimation(HighScaleInstanceLayer3D layer, LayerRuntime runtime)
    {
        _dirtyTransformIndices.Clear();
        runtime.EnsureTransformVersionCapacity(layer.Instances.Count);
        EnsureTransformScratchCapacity(layer.Instances.Count);

        var drainedTotal = 0;
        int drained;
        do
        {
            drained = layer.Instances.DrainDirtyTransforms(_dirtyTransformScratch);
            drainedTotal += drained;
            for (var i = 0; i < drained; i++)
            {
                var instanceIndex = _dirtyTransformScratch[i];
                if ((uint)instanceIndex < (uint)layer.Instances.Count)
                {
                    runtime.TransformVersionsByInstance[instanceIndex] = layer.Instances[instanceIndex].TransformVersion;
                }
            }
        }
        while (drained > 0);

        return 0;
    }

    private void ClearInitialTransformDirtyQueue(HighScaleInstanceLayer3D layer)
    {
        EnsureTransformScratchCapacity(layer.Instances.Count);
        while (layer.Instances.DrainDirtyTransforms(_dirtyTransformScratch) > 0)
        {
        }
    }

    private void EnsureTransformScratchCapacity(int count)
    {
        if (_dirtyTransformScratch.Length >= count)
        {
            return;
        }

        Array.Resize(ref _dirtyTransformScratch, System.Math.Max(1, count));
    }

    private static int UpdateDirtyTransforms(HighScaleInstanceLayer3D layer, BatchRuntime batch, List<int> dirtyIndices)
    {
        batch.ResetTransformDirtyOffsets();
        for (var i = 0; i < dirtyIndices.Count; i++)
        {
            var instanceIndex = dirtyIndices[i];
            if (!batch.InstanceOffsetMap.TryGetValue(instanceIndex, out var offset))
            {
                continue;
            }

            var version = layer.Instances[instanceIndex].TransformVersion;
            if (batch.TransformVersions[offset] == version)
            {
                continue;
            }

            WriteTransform(layer, instanceIndex, batch.Part, batch.TransformData, offset * TransformFloatStride);
            batch.TransformVersions[offset] = version;
            batch.AddTransformDirtyOffset(offset);
        }

        return batch.TransformDirtyOffsetCount;
    }

    private static int UpdateDirtyState(HighScaleInstanceLayer3D layer, BatchRuntime batch)
    {
        batch.ResetStateDirtyOffsets();
        var dirty = layer.StateBuffer.DirtyIndices;
        for (var i = 0; i < dirty.Count; i++)
        {
            var instanceIndex = dirty[i];
            if (!batch.InstanceOffsetMap.TryGetValue(instanceIndex, out var offset))
            {
                continue;
            }

            if (batch.UsePalette) WritePaletteState(layer, instanceIndex, batch.FadeAlpha, batch.StateData, offset * StateFloatStride);
            else WriteColorState(layer, instanceIndex, batch.Part, batch.FadeAlpha, batch.StateData, offset * StateFloatStride);
            batch.AddStateDirtyOffset(offset);
        }

        return batch.StateDirtyOffsetCount;
    }

    private static void RebuildFullState(HighScaleInstanceLayer3D layer, BatchRuntime batch)
    {
        for (var offset = 0; offset < batch.InstanceIndices.Length; offset++)
        {
            var instanceIndex = batch.InstanceIndices[offset];
            if (batch.UsePalette) WritePaletteState(layer, instanceIndex, batch.FadeAlpha, batch.StateData, offset * StateFloatStride);
            else WriteColorState(layer, instanceIndex, batch.Part, batch.FadeAlpha, batch.StateData, offset * StateFloatStride);
        }

        if (batch.UsePalette && batch.PaletteVersion != layer.MaterialResolverVersion)
        {
            batch.PaletteBytes = BuildPaletteBytes(layer.Template, batch.Part, out var width, out var height);
            batch.PaletteWidth = width;
            batch.PaletteHeight = height;
            batch.PaletteVersion = layer.MaterialResolverVersion;
        }
    }

    private static void UploadTransformRanges(int hostId, BatchRuntime batch, ScenePerformanceOptions performance, RenderStats stats)
    {
        UploadRanges(
            batch.TransformDirtyOffsetCount,
            batch.GetTransformDirtyOffsetAt,
            TransformFloatStride,
            batch.TransformData,
            performance,
            (startInstance, bytes) => WebGlInterop.UploadRetainedBatchTransformsRangeBytes(hostId, batch.BatchId, startInstance, bytes),
            rangeBytes =>
            {
                stats.InstanceBufferSubDataUploads++;
                stats.InstanceUploadBytes += rangeBytes;
                stats.TransformUploadBytes += rangeBytes;
                stats.JsTransformPatchRanges++;
                stats.JsTransformPatchBytes += rangeBytes;
            });
    }

    private static void UploadStateRanges(int hostId, BatchRuntime batch, ScenePerformanceOptions performance, RenderStats stats)
    {
        UploadRanges(
            batch.StateDirtyOffsetCount,
            batch.GetStateDirtyOffsetAt,
            StateFloatStride,
            batch.StateData,
            performance,
            (startInstance, bytes) => WebGlInterop.UploadRetainedBatchStateRangeBytes(hostId, batch.BatchId, startInstance, bytes),
            rangeBytes =>
            {
                stats.StateBufferSubDataUploads++;
                stats.StateUploadBytes += rangeBytes;
                stats.JsStatePatchRanges++;
                stats.JsStatePatchBytes += rangeBytes;
            });
    }

    private static void UploadRanges(int dirtyCount, Func<int, int> getOffset, int stride, float[] data, ScenePerformanceOptions performance, Action<int, byte[]> upload, Action<int> updateStats)
    {
        if (dirtyCount <= 0)
        {
            return;
        }

        var mergeGap = System.Math.Max(0, performance.HighScalePartialStateMergeGap);
        var rangeStart = getOffset(0);
        var previous = rangeStart;
        for (var i = 1; i <= dirtyCount; i++)
        {
            var current = i < dirtyCount ? getOffset(i) : -1;
            if (current >= 0 && current <= previous + 1 + mergeGap)
            {
                previous = current;
                continue;
            }

            var floatOffset = rangeStart * stride;
            var floatCount = (previous - rangeStart + 1) * stride;
            var bytes = FloatsToBytes(data, floatOffset, floatCount);
            upload(rangeStart, bytes);
            updateStats(bytes.Length);
            rangeStart = current;
            previous = current;
        }
    }

    private static void DestroyLayer(int hostId, LayerRuntime runtime)
    {
        foreach (var batch in runtime.Batches)
        {
            WebGlInterop.DestroyRetainedBatch(hostId, batch.BatchId);
        }

        WebGlInterop.DestroyHighScaleLayer(hostId, runtime.LayerId);
    }

    private static IEnumerable<HighScaleInstanceLayer3D> EnumerateHighScaleLayers(Scene3D scene)
    {
        foreach (var obj in scene.Registry.AllObjects)
        {
            if (obj is HighScaleInstanceLayer3D layer)
            {
                yield return layer;
            }
        }
    }

    private static void ApplyJsMetrics(string? metrics, RenderStats stats)
    {
        if (string.IsNullOrWhiteSpace(metrics)) return;
        var parts = metrics.Split(',');
        if (parts.Length < 16) return;
        static bool TryInt(string value, out int result) => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result);
        static bool TryDouble(string value, out double result) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);

        if (TryInt(parts[0], out var visibleChunks)) stats.VisibleChunkCount = visibleChunks;
        if (TryInt(parts[1], out var totalChunks)) stats.TotalChunkCount = totalChunks;
        if (TryInt(parts[2], out var culled)) stats.CulledObjectCount = culled;
        if (TryInt(parts[3], out var lodD)) stats.LodDetailedCount = lodD;
        if (TryInt(parts[4], out var lodS)) stats.LodSimplifiedCount = lodS;
        if (TryInt(parts[5], out var lodP)) stats.LodProxyCount = lodP;
        if (TryInt(parts[6], out var lodB)) stats.LodBillboardCount = lodB;
        if (TryInt(parts[7], out var lodC)) stats.LodCulledCount = lodC;
        if (TryInt(parts[8], out var drawCalls)) stats.DrawCallCount = drawCalls;
        if (TryInt(parts[9], out var batches)) { stats.InstancedBatchCount = batches; stats.JsDrawBatchCount = batches; }
        if (TryInt(parts[10], out var triangles)) stats.TriangleCount = triangles;
        if (TryInt(parts[11], out var partInstances)) { stats.HighScaleVisiblePartInstanceCount = partInstances; stats.VisibleMeshCount = partInstances; }
        if (TryDouble(parts[12], out var cullMs)) stats.JsCullMilliseconds = cullMs;
        if (TryDouble(parts[13], out var drawMs)) stats.JsDrawMilliseconds = drawMs;
        if (TryDouble(parts[14], out var frameMs)) stats.JsFrameMilliseconds = frameMs;
        if (TryInt(parts[15], out var webGlVersion)) stats.WebGlVersion = webGlVersion;
        if (parts.Length > 16 && TryInt(parts[16], out var animBatches)) stats.JsAnimationUploadBatches = animBatches;
        if (parts.Length > 17 && TryInt(parts[17], out var animBytes)) stats.JsAnimationUploadBytes = animBytes;
        if (parts.Length > 18 && TryInt(parts[18], out var textureErrors)) stats.JsTexturePayloadErrors = textureErrors;
        if (parts.Length > 19 && TryInt(parts[19], out var paletteErrors)) stats.JsPalettePayloadErrors = paletteErrors;
        stats.EstimatedDrawCallCount = stats.DrawCallCount;
    }

    private static void WriteTransform(HighScaleInstanceLayer3D layer, int instanceIndex, CompositePartTemplate3D part, float[] destination, int destinationOffset)
    {
        var record = layer.Instances[instanceIndex];
        var model = part.LocalTransform * record.Transform;
        destination[destinationOffset + 0] = model.M11; destination[destinationOffset + 1] = model.M12; destination[destinationOffset + 2] = model.M13; destination[destinationOffset + 3] = model.M14;
        destination[destinationOffset + 4] = model.M21; destination[destinationOffset + 5] = model.M22; destination[destinationOffset + 6] = model.M23; destination[destinationOffset + 7] = model.M24;
        destination[destinationOffset + 8] = model.M31; destination[destinationOffset + 9] = model.M32; destination[destinationOffset + 10] = model.M33; destination[destinationOffset + 11] = model.M34;
        destination[destinationOffset + 12] = model.M41; destination[destinationOffset + 13] = model.M42; destination[destinationOffset + 14] = model.M43; destination[destinationOffset + 15] = model.M44;
    }

    private static void WritePaletteState(HighScaleInstanceLayer3D layer, int instanceIndex, float alpha, float[] destination, int destinationOffset)
    {
        var record = layer.Instances[instanceIndex];
        var visible = (record.Flags & InstanceFlags3D.Visible) != 0 ? 1f : 0f;
        destination[destinationOffset + 0] = record.MaterialVariantId;
        destination[destinationOffset + 1] = visible;
        destination[destinationOffset + 2] = alpha;
        destination[destinationOffset + 3] = 0f;
    }

    private static void WriteColorState(HighScaleInstanceLayer3D layer, int instanceIndex, CompositePartTemplate3D part, float alpha, float[] destination, int destinationOffset)
    {
        var record = layer.Instances[instanceIndex];
        var visible = (record.Flags & InstanceFlags3D.Visible) != 0 ? 1f : 0f;
        var color = layer.ResolveColor(part, record);
        destination[destinationOffset + 0] = color.R;
        destination[destinationOffset + 1] = color.G;
        destination[destinationOffset + 2] = color.B;
        destination[destinationOffset + 3] = color.A * alpha * visible;
    }

    private static float ResolveChunkFadeAlpha(Scene3D scene, HighScaleInstanceLayer3D layer, Vector3 chunkCenter)
    {
        if (!scene.Performance.EnableHighScaleDynamicFadeState)
        {
            return 1f;
        }

        if (!scene.Performance.EnableDistanceFade || layer.LodPolicy.DrawDistance <= 0f || layer.LodPolicy.FadeDistance <= 0f)
        {
            return 1f;
        }

        var distance = Vector3.Distance(scene.Camera.Position, chunkCenter);
        if (distance > layer.LodPolicy.DrawDistance)
        {
            return 0f;
        }

        var fadeStart = MathF.Max(0f, layer.LodPolicy.DrawDistance - layer.LodPolicy.FadeDistance);
        if (distance <= fadeStart)
        {
            return 1f;
        }

        return System.Math.Clamp(1f - ((distance - fadeStart) / MathF.Max(layer.LodPolicy.FadeDistance, 0.001f)), 0f, 1f);
    }

    private static byte[] BuildPaletteBytes(CompositeTemplate3D template, CompositePartTemplate3D part, out int width, out int height)
    {
        width = System.Math.Max(1, part.MaterialSlotBaseColors.Count);
        var maxVariant = 0;
        foreach (var id in template.MaterialVariants.Keys)
        {
            if (id > maxVariant) maxVariant = id;
        }

        height = System.Math.Max(1, maxVariant + 1);
        var bytes = new byte[width * height * 4];
        for (var variant = 0; variant < height; variant++)
        {
            for (var slot = 0; slot < width; slot++)
            {
                var baseColor = slot < part.MaterialSlotBaseColors.Count ? part.MaterialSlotBaseColors[slot] : ColorRgba.White;
                var color = template.ResolveColor(slot, baseColor, variant);
                var o = ((variant * width) + slot) * 4;
                bytes[o + 0] = ToByte(color.R);
                bytes[o + 1] = ToByte(color.G);
                bytes[o + 2] = ToByte(color.B);
                bytes[o + 3] = ToByte(color.A);
            }
        }

        return bytes;
    }

    private static byte ToByte(float value) => (byte)System.Math.Clamp((int)MathF.Round(System.Math.Clamp(value, 0f, 1f) * 255f), 0, 255);

    private static byte[] FloatsToBytes(float[] values) => FloatsToBytes(values, 0, values.Length);

    private static byte[] FloatsToBytes(float[] values, int start, int count)
    {
        if (count <= 0) return Array.Empty<byte>();
        var bytes = new byte[count * sizeof(float)];
        Buffer.BlockCopy(values, start * sizeof(float), bytes, 0, bytes.Length);
        return bytes;
    }

    private static string BuildBatchId(HighScaleInstanceLayer3D layer, HighScaleChunkKey3D chunkKey, HighScaleLodLevel3D lod, int partIndex)
        => $"hs:{layer.Id}:{chunkKey.X}:{chunkKey.Y}:{chunkKey.Z}:{(int)lod}:{partIndex}";

    private static (float[] Ambient, float[] Direction, float[] DirectionalColor, float[] PointPosition, float[] PointColor) ResolveLight(Scene3D scene)
    {
        var ambient = new[] { 0.28f, 0.28f, 0.28f };
        var dir = new[] { -0.35f, -0.75f, -0.55f };
        var dirColor = new[] { 0f, 0f, 0f };
        foreach (var light in scene.Lights)
        {
            if (!light.IsEnabled) continue;
            var direction = light.Direction.LengthSquared() < 0.000001f ? new Vector3(-0.35f, -0.75f, -0.55f) : Vector3.Normalize(light.Direction);
            dir = new[] { direction.X, direction.Y, direction.Z };
            dirColor = new[] { light.Color.R * light.Intensity, light.Color.G * light.Intensity, light.Color.B * light.Intensity };
            break;
        }

        var pointPos = new[] { 0f, 0f, 0f, 1f };
        var pointColor = new[] { 0f, 0f, 0f, 0f };
        foreach (var light in scene.PointLights)
        {
            if (!light.IsEnabled) continue;
            pointPos = new[] { light.Position.X, light.Position.Y, light.Position.Z, light.Range };
            pointColor = new[] { light.Color.R * light.Intensity, light.Color.G * light.Intensity, light.Color.B * light.Intensity, 1f };
            break;
        }

        return (ambient, dir, dirColor, pointPos, pointColor);
    }

    private static float[] ToArray(Matrix4x4 matrix)
    {
        return new[]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        };
    }

    private sealed class LayerRuntime
    {
        public LayerRuntime(string layerId, int structuralVersion, int templateId, int instanceCount, bool paletteTextureEnabled)
        {
            LayerId = layerId;
            StructuralVersion = structuralVersion;
            TemplateId = templateId;
            InstanceCount = instanceCount;
            PaletteTextureEnabled = paletteTextureEnabled;
            _transformVersionsByInstance = new int[Math.Max(1, instanceCount)];
        }

        public string LayerId { get; }
        public int StructuralVersion { get; set; }
        public int TemplateId { get; }
        public int InstanceCount { get; }
        public bool PaletteTextureEnabled { get; }
        private int[] _transformVersionsByInstance;
        public string SnapshotJson { get; set; } = string.Empty;
        public List<BatchRuntime> Batches { get; } = new();
        public Dictionary<string, BatchRuntime> BatchesById { get; } = new(StringComparer.Ordinal);
        public int[] TransformVersionsByInstance => _transformVersionsByInstance;

        public bool CanReuseForGpuAnimation(HighScaleInstanceLayer3D layer, Scene3D scene)
            => TemplateId == layer.Template.Id &&
               InstanceCount == layer.Instances.Count &&
               PaletteTextureEnabled == scene.Performance.EnableHighScalePaletteTexture;

        public void EnsureTransformVersionCapacity(int count)
        {
            if (_transformVersionsByInstance.Length >= count) return;
            Array.Resize(ref _transformVersionsByInstance, count);
        }
    }

    private sealed class BatchRuntime
    {
        private int[] _stateDirtyOffsets = Array.Empty<int>();
        private int _stateDirtyOffsetCount;
        private int[] _transformDirtyOffsets = Array.Empty<int>();
        private int _transformDirtyOffsetCount;

        public BatchRuntime(string batchId, CompositePartTemplate3D part, bool usePalette, float lightingEnabled, int instanceCount)
        {
            BatchId = batchId;
            Part = part;
            UsePalette = usePalette;
            LightingEnabled = lightingEnabled;
            InstanceCount = instanceCount;
            InstanceIndices = new int[instanceCount];
            TransformVersions = new int[instanceCount];
            TransformData = new float[instanceCount * TransformFloatStride];
            StateData = new float[instanceCount * StateFloatStride];
        }

        public string BatchId { get; }
        public CompositePartTemplate3D Part { get; }
        public bool UsePalette { get; }
        public float LightingEnabled { get; }
        public int InstanceCount { get; }
        public int[] InstanceIndices { get; }
        public Dictionary<int, int> InstanceOffsetMap { get; } = new();
        public int[] TransformVersions { get; }
        public float[] TransformData { get; }
        public float[] StateData { get; }
        public int StateVersion { get; set; }
        public int MaterialResolverVersion { get; set; }
        public int LodPolicyVersion { get; set; }
        public float FadeAlpha { get; set; } = 1f;
        public int PaletteVersion { get; set; } = -1;
        public int PaletteWidth { get; set; } = 1;
        public int PaletteHeight { get; set; } = 1;
        public byte[] PaletteBytes { get; set; } = Array.Empty<byte>();
        public int StateDirtyOffsetCount => _stateDirtyOffsetCount;
        public int TransformDirtyOffsetCount => _transformDirtyOffsetCount;

        public void ResetStateDirtyOffsets() => _stateDirtyOffsetCount = 0;
        public void ResetTransformDirtyOffsets() => _transformDirtyOffsetCount = 0;

        public void AddStateDirtyOffset(int offset)
        {
            if (_stateDirtyOffsets.Length <= _stateDirtyOffsetCount)
            {
                Array.Resize(ref _stateDirtyOffsets, Math.Max(16, _stateDirtyOffsets.Length * 2));
            }

            _stateDirtyOffsets[_stateDirtyOffsetCount++] = offset;
        }

        public void AddTransformDirtyOffset(int offset)
        {
            if (_transformDirtyOffsets.Length <= _transformDirtyOffsetCount)
            {
                Array.Resize(ref _transformDirtyOffsets, Math.Max(16, _transformDirtyOffsets.Length * 2));
            }

            _transformDirtyOffsets[_transformDirtyOffsetCount++] = offset;
        }

        public void SortStateDirtyOffsets() => Array.Sort(_stateDirtyOffsets, 0, _stateDirtyOffsetCount);
        public void SortTransformDirtyOffsets() => Array.Sort(_transformDirtyOffsets, 0, _transformDirtyOffsetCount);
        public int GetStateDirtyOffsetAt(int index) => _stateDirtyOffsets[index];
        public int GetTransformDirtyOffsetAt(int index) => _transformDirtyOffsets[index];
    }
}
