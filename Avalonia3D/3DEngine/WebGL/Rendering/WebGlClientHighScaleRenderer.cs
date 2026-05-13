using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.IO;
using System.Text;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Shadows;
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
    private const int SnapshotMagic = 0x314C5348; // HSL1, little-endian
    private const int SnapshotProtocolVersion = 1;
    private static readonly HighScaleLodLevel3D[] RuntimeLods =
    {
        HighScaleLodLevel3D.Detailed,
        HighScaleLodLevel3D.Simplified,
        HighScaleLodLevel3D.Proxy,
        HighScaleLodLevel3D.Billboard
    };

    private readonly Dictionary<string, LayerRuntime> _layers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveLayerIdsScratch = new(StringComparer.Ordinal);
    private readonly List<string> _deadLayerIdsScratch = new(16);
    private readonly List<int> _dirtyTransformIndices = new(1024);
    private readonly List<BatchRuntime> _touchedTransformBatches = new(256);
    private readonly List<BatchRuntime> _touchedStateBatches = new(256);
    private int[] _dirtyTransformScratch = Array.Empty<int>();
    private int _patchMarker;
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private readonly byte[] _viewProjectionBytes = new byte[16 * sizeof(float)];
    private readonly byte[] _cameraBytes = new byte[12 * sizeof(float)];
    private readonly byte[] _lightingBytes = new byte[33 * sizeof(float)];
    private readonly byte[] _styleBytes = new byte[24 * sizeof(float)];

    private ulong _version;

    public bool HasRuntimeState => _layers.Count != 0;
    public ulong Version => _version;

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
        _version++;
    }

    public void SyncFrame(int hostId, Scene3D scene, IReadOnlyList<HighScaleInstanceLayer3D> visibleLayers, float width, float height, Matrix4x4 viewProjection, DirectionalShadowSnapshot3D shadow, RenderStats stats)
    {
        stats.WebGlClientHighScaleRuntime = true;
        stats.WebGlClientGpuTransformAnimation = scene.Performance.EnableWebGlClientGpuTransformAnimation;
        stats.SkyboxEnabled = scene.Environment.Skybox.Mode != SkyboxMode3D.None;
        stats.SkyboxMode = (int)scene.Environment.Skybox.Mode;
        if (visibleLayers is null) throw new ArgumentNullException(nameof(visibleLayers));
        if (shadow is null) throw new ArgumentNullException(nameof(shadow));
        stats.DirectionalShadowEnabled = shadow.IsEnabled;
        stats.ShadowMapResolution = shadow.Resolution;
        stats.ShadowMapReason = shadow.Reason;
        EnsureSnapshots(hostId, scene, visibleLayers, stats);
        ApplyPatches(hostId, scene, visibleLayers, stats);

        var light = SceneLightingResolver3D.Resolve(scene);
        var skybox = scene.Environment.Skybox;

        Span<float> view = stackalloc float[16];
        WriteMatrix(view, viewProjection);

        Span<float> camera = stackalloc float[12];
        WriteVector3(camera, 0, scene.Camera.Position);
        WriteVector3(camera, 3, scene.Camera.Right);
        WriteVector3(camera, 6, scene.Camera.SafeUp);
        WriteVector3(camera, 9, scene.Camera.Forward);

        Span<float> lighting = stackalloc float[33];
        WriteVector3(lighting, 0, light.Ambient);
        WriteVector3(lighting, 3, light.DirectionalDirection);
        WriteVector3(lighting, 6, light.DirectionalColor);
        WriteVector4(lighting, 9, light.PointPosition);
        WriteVector4(lighting, 13, light.PointColor);
        // Spots are not currently resolved by the high-scale lighting helper; reserve slots for ABI parity.
        lighting[17] = 0f; lighting[18] = 0f; lighting[19] = 0f; lighting[20] = 1f;
        lighting[21] = 0f; lighting[22] = -1f; lighting[23] = 0f; lighting[24] = 0f;
        lighting[25] = 0f; lighting[26] = 0f; lighting[27] = 0f; lighting[28] = 0f;
        lighting[29] = 0.95f; lighting[30] = 0.85f; lighting[31] = 1f; lighting[32] = 0f;

        Span<float> style = stackalloc float[24];
        WriteColor(style, 0, scene.BackgroundColor);
        WriteVector3Array(style, 4, skybox.TopColor.ToVector3());
        WriteVector3Array(style, 7, skybox.HorizonColor.ToVector3());
        WriteVector3Array(style, 10, skybox.BottomColor.ToVector3());
        style[13] = skybox.Intensity;
        style[14] = scene.RenderPipeline.ToneMapping.Exposure;
        style[15] = scene.RenderPipeline.ToneMapping.Gamma;
        style[16] = scene.RenderPipeline.Ssao.Strength;
        style[17] = scene.RenderPipeline.Ssao.Radius;
        style[18] = scene.RenderPipeline.Ssao.Bias;
        style[19] = scene.RenderPipeline.Ssao.SampleCount;
        style[20] = scene.Performance.EnableWebGlClientGpuTransformAnimation ? (float)_animationClock.Elapsed.TotalSeconds : 0f;
        style[21] = scene.Performance.WebGlClientGpuTransformAnimationAmplitude;
        style[22] = shadow.Strength;
        style[23] = shadow.Bias;

        var flags = 0;
        if (skybox.Mode != SkyboxMode3D.None) flags |= 1;
        if (scene.Performance.EnableWebGlClientGpuTransformAnimation) flags |= 2;
        if (shadow.IsEnabled) flags |= 4;
        if (scene.RenderPipeline.Ssao.Enabled) flags |= 8;
        if (scene.RenderPipeline.EnableHdr || scene.RenderPipeline.ToneMapping.Enabled) flags |= 16;

        WebGlInterop.SyncHighScaleFrameDirect(
            hostId,
            width,
            height,
            flags,
            (int)skybox.Mode,
            shadow.Resolution,
            shadow.Reason ?? string.Empty,
            CopyFloatsToFrameBuffer(view, _viewProjectionBytes),
            CopyFloatsToFrameBuffer(camera, _cameraBytes),
            CopyFloatsToFrameBuffer(lighting, _lightingBytes),
            CopyFloatsToFrameBuffer(style, _styleBytes));
    }

    private void EnsureSnapshots(int hostId, Scene3D scene, IReadOnlyList<HighScaleInstanceLayer3D> visibleLayers, RenderStats stats)
    {
        var liveLayerIds = _liveLayerIdsScratch;
        liveLayerIds.Clear();
        foreach (var layer in visibleLayers)
        {
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
            WebGlInterop.UploadHighScaleLayerSnapshotBytes(hostId, layer.Id, runtime.SnapshotBytes);
            _version++;
            layer.StateBuffer.ClearDirty();
        }

        var dead = _deadLayerIdsScratch;
        dead.Clear();
        foreach (var id in _layers.Keys)
        {
            if (!liveLayerIds.Contains(id)) dead.Add(id);
        }

        for (var i = 0; i < dead.Count; i++)
        {
            var runtime = _layers[dead[i]];
            DestroyLayer(hostId, runtime);
            _layers.Remove(dead[i]);
            _version++;
        }
    }

    private static int BuildStructuralVersion(HighScaleInstanceLayer3D layer, Scene3D scene)
    {
        // The chunk index owns topology/versioning. LOD policy is included because the JS
        // high-scale snapshot stores LOD thresholds and billboard fallback flags.
        return HashCode.Combine(
            layer.Template.Id,
            layer.Instances.Count,
            layer.Template.Parts.Count,
            layer.Chunks.Version,
            layer.Chunks.CellSize,
            layer.LodPolicy.Version,
            scene.Performance.EnableHighScalePaletteTexture);
    }

    private LayerRuntime BuildAndUploadLayer(int hostId, HighScaleInstanceLayer3D layer, Scene3D scene, int structuralVersion, RenderStats stats)
    {
        var runtime = new LayerRuntime(layer.Id, structuralVersion, layer.Template.Id, layer.Instances.Count, scene.Performance.EnableHighScalePaletteTexture);
        var chunks = new List<HighScaleSnapshotChunk>();

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

                    var batch = BuildBatchRuntime(runtime, layer, scene, chunk.InstanceIndices, part, batchId, chunk.Bounds.Center);
                    runtime.Batches.Add(batch);
                    runtime.BatchesById[batch.BatchId] = batch;
                    UploadFullBatch(hostId, batch, stats);
                }
            }

            var center = chunk.Bounds.Center;
            var extents = chunk.Bounds.Size * 0.5f;
            // Conservative expansion compensates small animated movement between structural rebuilds.
            extents += new Vector3(System.MathF.Max(0.5f, layer.Chunks.CellSize * 0.10f));
            chunks.Add(new HighScaleSnapshotChunk(
                chunk.Key.ToString(),
                center,
                extents,
                chunk.InstanceIndices.Count,
                batchIdsByLod));
        }

        runtime.EnsureTransformVersionCapacity(layer.Instances.Count);
        for (var i = 0; i < layer.Instances.Count; i++)
        {
            runtime.TransformVersionsByInstance[i] = layer.Instances[i].TransformVersion;
        }
        ClearInitialTransformDirtyQueue(layer);

        runtime.StateVersion = layer.StateBuffer.Version;
        runtime.MaterialResolverVersion = layer.MaterialResolverVersion;
        runtime.LodPolicyVersion = layer.LodPolicy.Version;
        runtime.SnapshotBytes = BuildSnapshotBytes(layer, structuralVersion, chunks);

        stats.TotalChunkCount += layer.Chunks.Chunks.Count;
        return runtime;
    }


    private static byte[] BuildSnapshotBytes(HighScaleInstanceLayer3D layer, int structuralVersion, IReadOnlyList<HighScaleSnapshotChunk> chunks)
    {
        using var stream = new MemoryStream(Math.Max(128, 96 + chunks.Count * 96));
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(SnapshotMagic);
        writer.Write(SnapshotProtocolVersion);
        WriteString(writer, layer.Id);
        writer.Write(structuralVersion);
        writer.Write(layer.IsVisible ? 1 : 0);
        writer.Write(layer.LodPolicy.DetailedDistance);
        writer.Write(layer.LodPolicy.SimplifiedDistance);
        writer.Write(layer.LodPolicy.ProxyDistance);
        writer.Write(layer.LodPolicy.DrawDistance);
        writer.Write(layer.LodPolicy.EnableBillboardFallback ? 1 : 0);
        writer.Write(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            WriteString(writer, chunk.Id);
            writer.Write(chunk.Center.X);
            writer.Write(chunk.Center.Y);
            writer.Write(chunk.Center.Z);
            writer.Write(chunk.Extents.X);
            writer.Write(chunk.Extents.Y);
            writer.Write(chunk.Extents.Z);
            writer.Write(chunk.InstanceCount);
            for (var lod = 0; lod < 4; lod++)
            {
                var ids = chunk.BatchIdsByLod[lod];
                writer.Write(ids.Count);
                for (var j = 0; j < ids.Count; j++)
                {
                    WriteString(writer, ids[j]);
                }
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write(0);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static BatchRuntime BuildBatchRuntime(
        LayerRuntime runtime,
        HighScaleInstanceLayer3D layer,
        Scene3D scene,
        IReadOnlyList<int> indices,
        CompositePartTemplate3D part,
        string batchId,
        Vector3 chunkCenter)
    {
        var alpha = ResolveChunkFadeAlpha(scene, layer, chunkCenter);
        var usePalette = scene.Performance.EnableHighScalePaletteTexture && part.UsesVertexMaterialSlots && layer.ColorResolver is null;
        var lighting = ToLightingUniform(part.LightingMode);
        var batch = new BatchRuntime(batchId, part, usePalette, lighting, indices.Count, chunkCenter)
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
            runtime.AddBatchRef(instanceIndex, batch, i);
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
            batch.CopyTransformBytes());
        WebGlInterop.UploadRetainedBatchStateBytes(
            hostId,
            batch.BatchId,
            batch.UsePalette,
            batch.PaletteWidth,
            batch.PaletteHeight,
            batch.CopyStateBytes(),
            batch.PaletteBytes);
        stats.InstanceBufferUploads++;
        stats.StateBufferUploads++;
        stats.InstanceUploadBytes += batch.TransformData.Length * sizeof(float);
        stats.TransformUploadBytes += batch.TransformData.Length * sizeof(float);
        stats.StateUploadBytes += batch.StateData.Length * sizeof(float);
    }

    private void ApplyPatches(int hostId, Scene3D scene, IReadOnlyList<HighScaleInstanceLayer3D> visibleLayers, RenderStats stats)
    {
        var start = Stopwatch.GetTimestamp();
        foreach (var layer in visibleLayers)
        {
            if (!_layers.TryGetValue(layer.Id, out var runtime))
            {
                continue;
            }

            var dirtyTransformCount = scene.Performance.EnableWebGlClientGpuTransformAnimation
                ? ClearDirtyTransformsForGpuAnimation(layer, runtime)
                : BuildDirtyTransformIndices(layer, runtime);
            var stateDirty = layer.StateBuffer.HasDirtyState;
            var requiresStateSync = runtime.RequiresStateVersionSync(layer);
            if (dirtyTransformCount == 0 && !stateDirty && !requiresStateSync)
            {
                continue;
            }

            if (dirtyTransformCount > 0)
            {
                stats.JsHighScaleDirtyTransformInstances += dirtyTransformCount;
                var marker = NextPatchMarker();
                var routedRefCount = UpdateDirtyTransformsByRoute(layer, runtime, _dirtyTransformIndices, _touchedTransformBatches, marker);
                stats.JsHighScalePatchRoutedTransformRefs += routedRefCount;
                stats.JsHighScalePatchTouchedTransformBatches += _touchedTransformBatches.Count;
                UploadTouchedTransformBatches(hostId, _touchedTransformBatches, scene.Performance, stats);
            }

            if (requiresStateSync)
            {
                SyncFullStateBatchesIfNeeded(hostId, scene, layer, runtime, stats);
            }

            if (stateDirty)
            {
                stats.JsHighScaleDirtyStateInstances += layer.StateBuffer.DirtyIndices.Count;
                var marker = NextPatchMarker();
                var routedRefCount = UpdateDirtyStateByRoute(layer, runtime, _touchedStateBatches, marker);
                stats.JsHighScalePatchRoutedStateRefs += routedRefCount;
                stats.JsHighScalePatchTouchedStateBatches += _touchedStateBatches.Count;
                UploadTouchedStateBatches(hostId, layer, _touchedStateBatches, scene.Performance, stats);
                runtime.StateVersion = layer.StateBuffer.Version;
                runtime.MaterialResolverVersion = layer.MaterialResolverVersion;
                runtime.LodPolicyVersion = layer.LodPolicy.Version;
                layer.StateBuffer.ClearDirty();
            }
        }

        stats.JsPatchMilliseconds += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    }

    private int NextPatchMarker()
    {
        unchecked
        {
            _patchMarker++;
        }

        if (_patchMarker <= 0)
        {
            _patchMarker = 1;
        }

        return _patchMarker;
    }

    private static int UpdateDirtyTransformsByRoute(
        HighScaleInstanceLayer3D layer,
        LayerRuntime runtime,
        IReadOnlyList<int> dirtyIndices,
        List<BatchRuntime> touchedBatches,
        int marker)
    {
        touchedBatches.Clear();
        var routedRefCount = 0;
        for (var i = 0; i < dirtyIndices.Count; i++)
        {
            var instanceIndex = dirtyIndices[i];
            if ((uint)instanceIndex >= (uint)layer.Instances.Count)
            {
                continue;
            }

            var version = layer.Instances[instanceIndex].TransformVersion;
            for (var refIndex = runtime.GetFirstBatchRef(instanceIndex); refIndex >= 0; refIndex = runtime.GetNextBatchRef(refIndex))
            {
                var batchRef = runtime.GetBatchRef(refIndex);
                var batch = batchRef.Batch;
                var offset = batchRef.Offset;
                routedRefCount++;
                if (batch.TransformVersions[offset] == version)
                {
                    continue;
                }

                if (batch.BeginTransformPatch(marker))
                {
                    touchedBatches.Add(batch);
                }

                WriteTransform(layer, instanceIndex, batch.Part, batch.TransformData, offset * TransformFloatStride);
                batch.TransformVersions[offset] = version;
                batch.AddTransformDirtyOffset(offset);
            }
        }

        return routedRefCount;
    }

    private static int UpdateDirtyStateByRoute(
        HighScaleInstanceLayer3D layer,
        LayerRuntime runtime,
        List<BatchRuntime> touchedBatches,
        int marker)
    {
        touchedBatches.Clear();
        var dirty = layer.StateBuffer.DirtyIndices;
        var routedRefCount = 0;
        for (var i = 0; i < dirty.Count; i++)
        {
            var instanceIndex = dirty[i];
            if ((uint)instanceIndex >= (uint)layer.Instances.Count)
            {
                continue;
            }

            for (var refIndex = runtime.GetFirstBatchRef(instanceIndex); refIndex >= 0; refIndex = runtime.GetNextBatchRef(refIndex))
            {
                var batchRef = runtime.GetBatchRef(refIndex);
                var batch = batchRef.Batch;
                if (batch.StateVersion == layer.StateBuffer.Version &&
                    batch.MaterialResolverVersion == layer.MaterialResolverVersion &&
                    batch.LodPolicyVersion == layer.LodPolicy.Version)
                {
                    continue;
                }

                routedRefCount++;
                var offset = batchRef.Offset;
                if (batch.BeginStatePatch(marker))
                {
                    touchedBatches.Add(batch);
                }

                if (batch.UsePalette) WritePaletteState(layer, instanceIndex, batch.FadeAlpha, batch.StateData, offset * StateFloatStride);
                else WriteColorState(layer, instanceIndex, batch.Part, batch.FadeAlpha, batch.StateData, offset * StateFloatStride);
                batch.AddStateDirtyOffset(offset);
            }
        }

        return routedRefCount;
    }

    private static void SyncFullStateBatchesIfNeeded(int hostId, Scene3D scene, HighScaleInstanceLayer3D layer, LayerRuntime runtime, RenderStats stats)
    {
        foreach (var batch in runtime.Batches)
        {
            var resolverChanged = batch.MaterialResolverVersion != layer.MaterialResolverVersion;
            var lodPolicyChanged = batch.LodPolicyVersion != layer.LodPolicy.Version;
            var forceFullState = resolverChanged || batch.StateVersion < 0 || lodPolicyChanged;
            if (!forceFullState)
            {
                continue;
            }

            if (lodPolicyChanged)
            {
                batch.FadeAlpha = ResolveChunkFadeAlpha(scene, layer, batch.ChunkCenter);
            }
            RebuildFullState(layer, batch);
            WebGlInterop.UploadRetainedBatchStateBytes(hostId, batch.BatchId, batch.UsePalette, batch.PaletteWidth, batch.PaletteHeight, batch.CopyStateBytes(), batch.PaletteBytes);
            stats.StateBufferUploads++;
            stats.StateUploadBytes += batch.StateData.Length * sizeof(float);
            batch.StateVersion = layer.StateBuffer.Version;
            batch.MaterialResolverVersion = layer.MaterialResolverVersion;
            batch.LodPolicyVersion = layer.LodPolicy.Version;
        }

        runtime.StateVersion = layer.StateBuffer.Version;
        runtime.MaterialResolverVersion = layer.MaterialResolverVersion;
        runtime.LodPolicyVersion = layer.LodPolicy.Version;
    }

    private static void UploadTouchedTransformBatches(int hostId, IReadOnlyList<BatchRuntime> touchedBatches, ScenePerformanceOptions performance, RenderStats stats)
    {
        for (var i = 0; i < touchedBatches.Count; i++)
        {
            var batch = touchedBatches[i];
            if (batch.TransformDirtyOffsetCount <= 0)
            {
                continue;
            }

            if (batch.TransformDirtyOffsetCount > System.Math.Max(32, batch.InstanceCount / 3))
            {
                WebGlInterop.UploadRetainedBatchTransformsBytes(
                    hostId,
                    batch.BatchId,
                    batch.Part.Mesh.ResourceKey,
                    batch.LightingEnabled,
                    batch.UsePalette,
                    batch.InstanceCount,
                    batch.CopyTransformBytes());
                stats.InstanceBufferUploads++;
                stats.InstanceUploadBytes += batch.TransformData.Length * sizeof(float);
                stats.TransformUploadBytes += batch.TransformData.Length * sizeof(float);
            }
            else
            {
                batch.SortTransformDirtyOffsets();
                UploadTransformRanges(hostId, batch, performance, stats);
            }
        }
    }

    private static void UploadTouchedStateBatches(int hostId, HighScaleInstanceLayer3D layer, IReadOnlyList<BatchRuntime> touchedBatches, ScenePerformanceOptions performance, RenderStats stats)
    {
        for (var i = 0; i < touchedBatches.Count; i++)
        {
            var batch = touchedBatches[i];
            if (batch.StateDirtyOffsetCount <= 0)
            {
                continue;
            }

            if (batch.StateDirtyOffsetCount > System.Math.Max(32, batch.InstanceCount / 3))
            {
                WebGlInterop.UploadRetainedBatchStateBytes(hostId, batch.BatchId, batch.UsePalette, batch.PaletteWidth, batch.PaletteHeight, batch.CopyStateBytes(), Array.Empty<byte>());
                stats.StateBufferUploads++;
                stats.StateUploadBytes += batch.StateData.Length * sizeof(float);
            }
            else
            {
                batch.SortStateDirtyOffsets();
                UploadStateRanges(hostId, batch, performance, stats);
            }

            batch.StateVersion = layer.StateBuffer.Version;
            batch.MaterialResolverVersion = layer.MaterialResolverVersion;
            batch.LodPolicyVersion = layer.LodPolicy.Version;
        }
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
        var dirtyCount = batch.TransformDirtyOffsetCount;
        if (dirtyCount <= 0) return;

        var mergeGap = System.Math.Max(0, performance.HighScalePartialStateMergeGap);
        var rangeStart = batch.GetTransformDirtyOffsetAt(0);
        var previous = rangeStart;
        for (var i = 1; i <= dirtyCount; i++)
        {
            var current = i < dirtyCount ? batch.GetTransformDirtyOffsetAt(i) : -1;
            if (current >= 0 && current <= previous + 1 + mergeGap)
            {
                previous = current;
                continue;
            }

            var floatOffset = rangeStart * TransformFloatStride;
            var floatCount = (previous - rangeStart + 1) * TransformFloatStride;
            var bytes = batch.CopyTransformRangeBytes(floatOffset, floatCount);
            WebGlInterop.UploadRetainedBatchTransformsRangeBytes(hostId, batch.BatchId, rangeStart, bytes);
            stats.InstanceBufferSubDataUploads++;
            stats.InstanceUploadBytes += bytes.Length;
            stats.TransformUploadBytes += bytes.Length;
            stats.JsTransformPatchRanges++;
            stats.JsTransformPatchBytes += bytes.Length;
            rangeStart = current;
            previous = current;
        }
    }

    private static void UploadStateRanges(int hostId, BatchRuntime batch, ScenePerformanceOptions performance, RenderStats stats)
    {
        var dirtyCount = batch.StateDirtyOffsetCount;
        if (dirtyCount <= 0) return;

        var mergeGap = System.Math.Max(0, performance.HighScalePartialStateMergeGap);
        var rangeStart = batch.GetStateDirtyOffsetAt(0);
        var previous = rangeStart;
        for (var i = 1; i <= dirtyCount; i++)
        {
            var current = i < dirtyCount ? batch.GetStateDirtyOffsetAt(i) : -1;
            if (current >= 0 && current <= previous + 1 + mergeGap)
            {
                previous = current;
                continue;
            }

            var floatOffset = rangeStart * StateFloatStride;
            var floatCount = (previous - rangeStart + 1) * StateFloatStride;
            var bytes = batch.CopyStateRangeBytes(floatOffset, floatCount);
            WebGlInterop.UploadRetainedBatchStateRangeBytes(hostId, batch.BatchId, rangeStart, bytes);
            stats.StateBufferSubDataUploads++;
            stats.StateUploadBytes += bytes.Length;
            stats.JsStatePatchRanges++;
            stats.JsStatePatchBytes += bytes.Length;
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


    private static void WriteMatrix(Span<float> buffer, Matrix4x4 matrix)
    {
        buffer[0] = matrix.M11; buffer[1] = matrix.M12; buffer[2] = matrix.M13; buffer[3] = matrix.M14;
        buffer[4] = matrix.M21; buffer[5] = matrix.M22; buffer[6] = matrix.M23; buffer[7] = matrix.M24;
        buffer[8] = matrix.M31; buffer[9] = matrix.M32; buffer[10] = matrix.M33; buffer[11] = matrix.M34;
        buffer[12] = matrix.M41; buffer[13] = matrix.M42; buffer[14] = matrix.M43; buffer[15] = matrix.M44;
    }

    private static void WriteVector3(Span<float> buffer, int offset, Vector3 value)
    {
        buffer[offset] = value.X;
        buffer[offset + 1] = value.Y;
        buffer[offset + 2] = value.Z;
    }

    private static void WriteVector3Array(Span<float> buffer, int offset, float[] value)
    {
        buffer[offset] = value.Length > 0 ? value[0] : 0f;
        buffer[offset + 1] = value.Length > 1 ? value[1] : 0f;
        buffer[offset + 2] = value.Length > 2 ? value[2] : 0f;
    }

    private static void WriteVector3Array(Span<float> buffer, int offset, Vector3 value)
    {
        buffer[offset] = value.X;
        buffer[offset + 1] = value.Y;
        buffer[offset + 2] = value.Z;
    }

    private static void WriteVector4(Span<float> buffer, int offset, Vector4 value)
    {
        buffer[offset] = value.X;
        buffer[offset + 1] = value.Y;
        buffer[offset + 2] = value.Z;
        buffer[offset + 3] = value.W;
    }

    private static void WriteColor(Span<float> buffer, int offset, ColorRgba value)
    {
        buffer[offset] = value.R;
        buffer[offset + 1] = value.G;
        buffer[offset + 2] = value.B;
        buffer[offset + 3] = value.A;
    }

    private static byte[] CopyFloatsToFrameBuffer(ReadOnlySpan<float> values, byte[] destination)
    {
        var byteCount = values.Length * sizeof(float);
        if (destination.Length != byteCount)
        {
            throw new ArgumentException("Frame state buffer size does not match payload size.", nameof(destination));
        }

        for (var i = 0; i < values.Length; i++)
        {
            WriteFloat(destination, i * sizeof(float), values[i]);
        }

        return destination;
    }

    private static void WriteFloat(byte[] destination, int byteOffset, float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        destination[byteOffset + 0] = (byte)bits;
        destination[byteOffset + 1] = (byte)(bits >> 8);
        destination[byteOffset + 2] = (byte)(bits >> 16);
        destination[byteOffset + 3] = (byte)(bits >> 24);
    }

    public static void ApplyJsMetrics(int hostId, RenderStats stats)
    {
        static int MetricInt(int hostId, int index) => unchecked((int)Math.Round(WebGlInterop.GetLastHighScaleMetric(hostId, index)));
        static double MetricDouble(int hostId, int index) => WebGlInterop.GetLastHighScaleMetric(hostId, index);

        var visibleChunks = MetricInt(hostId, 0);
        var totalChunks = MetricInt(hostId, 1);
        var culled = MetricInt(hostId, 2);
        var lodD = MetricInt(hostId, 3);
        var lodS = MetricInt(hostId, 4);
        var lodP = MetricInt(hostId, 5);
        var lodB = MetricInt(hostId, 6);
        var lodC = MetricInt(hostId, 7);
        var drawCalls = MetricInt(hostId, 8);
        var batches = MetricInt(hostId, 9);
        var triangles = MetricInt(hostId, 10);
        var partInstances = MetricInt(hostId, 11);

        stats.VisibleChunkCount += visibleChunks;
        stats.TotalChunkCount += totalChunks;
        stats.CulledObjectCount += culled;
        stats.LodDetailedCount += lodD;
        stats.LodSimplifiedCount += lodS;
        stats.LodProxyCount += lodP;
        stats.LodBillboardCount += lodB;
        stats.LodCulledCount += lodC;
        stats.DrawCallCount += drawCalls;
        stats.EstimatedDrawCallCount += drawCalls;
        stats.InstancedBatchCount += batches;
        stats.JsDrawBatchCount = batches;
        stats.TriangleCount += triangles;
        stats.HighScaleVisiblePartInstanceCount += partInstances;
        stats.VisibleMeshCount += partInstances;
        stats.JsCullMilliseconds = MetricDouble(hostId, 12);
        stats.JsDrawMilliseconds = MetricDouble(hostId, 13);
        stats.JsFrameMilliseconds = MetricDouble(hostId, 14);
        stats.WebGlVersion = MetricInt(hostId, 15);
        stats.JsAnimationUploadBatches = MetricInt(hostId, 16);
        stats.JsAnimationUploadBytes = MetricInt(hostId, 17);
        stats.JsTexturePayloadErrors = MetricInt(hostId, 18);
        stats.JsPalettePayloadErrors = MetricInt(hostId, 19);
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

    private static string BuildBatchId(HighScaleInstanceLayer3D layer, HighScaleChunkKey3D chunkKey, HighScaleLodLevel3D lod, int partIndex)
        => RenderId3D.BuildHighScaleBatchId(layer.Id, chunkKey.X, chunkKey.Y, chunkKey.Z, (int)lod, partIndex);

    private static float ToLightingUniform(LightingMode mode)
        => mode == LightingMode.Unlit ? 0f : mode == LightingMode.Lambert ? 1f : mode == LightingMode.Phong ? 2f : 3f;


    private sealed class HighScaleSnapshotChunk
    {
        public HighScaleSnapshotChunk(string id, Vector3 center, Vector3 extents, int instanceCount, IReadOnlyList<string>[] batchIdsByLod)
        {
            Id = id;
            Center = center;
            Extents = extents;
            InstanceCount = instanceCount;
            BatchIdsByLod = batchIdsByLod;
        }

        public string Id { get; }
        public Vector3 Center { get; }
        public Vector3 Extents { get; }
        public int InstanceCount { get; }
        public IReadOnlyList<string>[] BatchIdsByLod { get; }
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
            var capacity = global::System.Math.Max(1, instanceCount);
            _transformVersionsByInstance = new int[capacity];
            _firstBatchRefByInstance = new int[capacity];
            Array.Fill(_firstBatchRefByInstance, -1);
        }

        public string LayerId { get; }
        public int StructuralVersion { get; set; }
        public int TemplateId { get; }
        public int InstanceCount { get; }
        public bool PaletteTextureEnabled { get; }
        public int StateVersion { get; set; } = -1;
        public int MaterialResolverVersion { get; set; } = -1;
        public int LodPolicyVersion { get; set; } = -1;
        private int[] _transformVersionsByInstance;
        private int[] _firstBatchRefByInstance;
        private BatchInstanceRef[] _batchRefs = Array.Empty<BatchInstanceRef>();
        private int _batchRefCount;
        public byte[] SnapshotBytes { get; set; } = Array.Empty<byte>();
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

        public void AddBatchRef(int instanceIndex, BatchRuntime batch, int offset)
        {
            if ((uint)instanceIndex >= (uint)_firstBatchRefByInstance.Length)
            {
                EnsureBatchRefInstanceCapacity(instanceIndex + 1);
            }

            EnsureBatchRefCapacity(_batchRefCount + 1);
            _batchRefs[_batchRefCount] = new BatchInstanceRef(batch, offset, _firstBatchRefByInstance[instanceIndex]);
            _firstBatchRefByInstance[instanceIndex] = _batchRefCount;
            _batchRefCount++;
        }

        public int GetFirstBatchRef(int instanceIndex)
            => (uint)instanceIndex < (uint)_firstBatchRefByInstance.Length ? _firstBatchRefByInstance[instanceIndex] : -1;

        public int GetNextBatchRef(int refIndex) => _batchRefs[refIndex].Next;

        public BatchInstanceRef GetBatchRef(int refIndex) => _batchRefs[refIndex];

        public bool RequiresStateVersionSync(HighScaleInstanceLayer3D layer)
            => StateVersion < 0 ||
               MaterialResolverVersion != layer.MaterialResolverVersion ||
               LodPolicyVersion != layer.LodPolicy.Version;

        private void EnsureBatchRefInstanceCapacity(int count)
        {
            if (_firstBatchRefByInstance.Length >= count) return;
            var oldLength = _firstBatchRefByInstance.Length;
            var next = oldLength;
            while (next < count) next *= 2;
            Array.Resize(ref _firstBatchRefByInstance, next);
            Array.Fill(_firstBatchRefByInstance, -1, oldLength, next - oldLength);
        }

        private void EnsureBatchRefCapacity(int count)
        {
            if (_batchRefs.Length >= count) return;
            var next = _batchRefs.Length == 0 ? 256 : _batchRefs.Length * 2;
            while (next < count) next *= 2;
            Array.Resize(ref _batchRefs, next);
        }
    }

    private readonly struct BatchInstanceRef
    {
        public BatchInstanceRef(BatchRuntime batch, int offset, int next)
        {
            Batch = batch;
            Offset = offset;
            Next = next;
        }

        public BatchRuntime Batch { get; }
        public int Offset { get; }
        public int Next { get; }
    }

    private sealed class BatchRuntime
    {
        private int[] _stateDirtyOffsets = Array.Empty<int>();
        private int _stateDirtyOffsetCount;
        private int _statePatchMarker;
        private int[] _transformDirtyOffsets = Array.Empty<int>();
        private int _transformDirtyOffsetCount;
        private int _transformPatchMarker;

        public BatchRuntime(string batchId, CompositePartTemplate3D part, bool usePalette, float lightingEnabled, int instanceCount, Vector3 chunkCenter)
        {
            BatchId = batchId;
            Part = part;
            UsePalette = usePalette;
            LightingEnabled = lightingEnabled;
            InstanceCount = instanceCount;
            ChunkCenter = chunkCenter;
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
        public Vector3 ChunkCenter { get; }
        public int[] InstanceIndices { get; }
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

        private byte[] _transformBytes = Array.Empty<byte>();
        private byte[] _stateBytes = Array.Empty<byte>();
        private byte[] _transformRangeBytes = Array.Empty<byte>();
        private byte[] _stateRangeBytes = Array.Empty<byte>();

        public byte[] CopyTransformBytes() => CopyFloatsToExactBuffer(TransformData, 0, TransformData.Length, ref _transformBytes);
        public byte[] CopyStateBytes() => CopyFloatsToExactBuffer(StateData, 0, StateData.Length, ref _stateBytes);
        public byte[] CopyTransformRangeBytes(int floatOffset, int floatCount) => CopyFloatsToExactBuffer(TransformData, floatOffset, floatCount, ref _transformRangeBytes);
        public byte[] CopyStateRangeBytes(int floatOffset, int floatCount) => CopyFloatsToExactBuffer(StateData, floatOffset, floatCount, ref _stateRangeBytes);

        private static byte[] CopyFloatsToExactBuffer(float[] source, int floatOffset, int floatCount, ref byte[] buffer)
        {
            if (floatCount <= 0) return Array.Empty<byte>();
            var byteCount = floatCount * sizeof(float);
            if (buffer.Length != byteCount) buffer = new byte[byteCount];
            Buffer.BlockCopy(source, floatOffset * sizeof(float), buffer, 0, byteCount);
            return buffer;
        }

        public bool BeginStatePatch(int marker)
        {
            if (_statePatchMarker == marker)
            {
                return false;
            }

            _statePatchMarker = marker;
            _stateDirtyOffsetCount = 0;
            return true;
        }

        public bool BeginTransformPatch(int marker)
        {
            if (_transformPatchMarker == marker)
            {
                return false;
            }

            _transformPatchMarker = marker;
            _transformDirtyOffsetCount = 0;
            return true;
        }

        public void ResetStateDirtyOffsets() => _stateDirtyOffsetCount = 0;
        public void ResetTransformDirtyOffsets() => _transformDirtyOffsetCount = 0;

        public void AddStateDirtyOffset(int offset)
        {
            if (_stateDirtyOffsets.Length <= _stateDirtyOffsetCount)
            {
                Array.Resize(ref _stateDirtyOffsets, global::System.Math.Max(16, _stateDirtyOffsets.Length * 2));
            }

            _stateDirtyOffsets[_stateDirtyOffsetCount++] = offset;
        }

        public void AddTransformDirtyOffset(int offset)
        {
            if (_transformDirtyOffsets.Length <= _transformDirtyOffsetCount)
            {
                Array.Resize(ref _transformDirtyOffsets, global::System.Math.Max(16, _transformDirtyOffsets.Length * 2));
            }

            _transformDirtyOffsets[_transformDirtyOffsetCount++] = offset;
        }

        public void SortStateDirtyOffsets() => Array.Sort(_stateDirtyOffsets, 0, _stateDirtyOffsetCount);
        public void SortTransformDirtyOffsets() => Array.Sort(_transformDirtyOffsets, 0, _transformDirtyOffsetCount);
        public int GetStateDirtyOffsetAt(int index) => _stateDirtyOffsets[index];
        public int GetTransformDirtyOffsetAt(int index) => _transformDirtyOffsets[index];
    }
}
