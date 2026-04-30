using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Rendering;

/// <summary>
/// Browser/WebGL retained high-scale path.
///
/// v56 data-flow rule:
/// - structural GPU data is rebuilt only when chunk membership/order or mesh identity changes;
/// - transform telemetry inside the same chunk is uploaded as partial gl.bufferSubData ranges;
/// - state/material telemetry is uploaded as partial state-buffer ranges;
/// - layer-wide state/version changes must not force full transform rebuilds.
/// </summary>
internal sealed class WebGlRetainedHighScaleRenderer
{
    private const int TransformFloatStride = 16;
    private const int StateFloatStride = 4;
    private const int RetainedBatchInvisibilityGraceFrames = 600;
    private readonly Dictionary<string, RetainedBatchState> _batches = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveBatchIds = new(StringComparer.Ordinal);
    private int _frameId;

    public List<WebGlRetainedBatchPacket> BuildAndUpload(int hostId, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        _frameId++;
        _liveBatchIds.Clear();
        var drawBatches = new List<WebGlRetainedBatchPacket>(64);

        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            if (!layer.IsVisible || layer.Instances.Count == 0)
            {
                continue;
            }

            if (layer.Chunks.RebuildRequested)
            {
                layer.Chunks.Rebuild(layer.Instances, layer.Template.LocalBounds);
            }

            var visibleChunks = layer.Chunks.QueryVisible(viewProjection);
            stats.TotalChunkCount += layer.Chunks.Chunks.Count;
            var visibleChunkLimit = scene.Performance.MaxVisibleHighScaleChunks > 0
                ? System.Math.Min(scene.Performance.MaxVisibleHighScaleChunks, visibleChunks.Count)
                : visibleChunks.Count;
            stats.VisibleChunkCount += visibleChunkLimit;

            for (var c = 0; c < visibleChunkLimit; c++)
            {
                var chunk = visibleChunks[c];
                if (chunk.InstanceIndices.Count == 0)
                {
                    continue;
                }

                var lod = ResolveChunkLod(scene, layer, chunk);
                if (lod == HighScaleLodLevel3D.Culled)
                {
                    stats.CulledObjectCount += chunk.InstanceIndices.Count;
                    stats.LodCulledCount += chunk.InstanceIndices.Count;
                    continue;
                }

                var renderLod = lod == HighScaleLodLevel3D.Billboard ? HighScaleLodLevel3D.Proxy : lod;
                var parts = layer.Template.ResolveParts(renderLod);
                if (parts.Count == 0)
                {
                    continue;
                }

                if (lod == HighScaleLodLevel3D.Detailed) stats.LodDetailedCount += chunk.InstanceIndices.Count;
                else if (lod == HighScaleLodLevel3D.Simplified) stats.LodSimplifiedCount += chunk.InstanceIndices.Count;
                else if (lod == HighScaleLodLevel3D.Proxy) stats.LodProxyCount += chunk.InstanceIndices.Count;
                else if (lod == HighScaleLodLevel3D.Billboard) stats.LodBillboardCount += chunk.InstanceIndices.Count;

                stats.HighScaleInstanceCount += chunk.InstanceIndices.Count;

                for (var p = 0; p < parts.Count; p++)
                {
                    var part = parts[p];
                    var batchId = BuildBatchId(layer, chunk.Key, renderLod, p);
                    _liveBatchIds.Add(batchId);
                    var usePalette = scene.Performance.EnableHighScalePaletteTexture && part.UsesVertexMaterialSlots && layer.ColorResolver is null;
                    var lighting = part.LightingMode == LightingMode.Lambert ? 1f : 0f;
                    var batchState = GetOrCreateBatch(batchId);
                    batchState.LastLiveFrame = _frameId;
                    var instanceCount = chunk.InstanceIndices.Count;
                    var structuralSignature = HashCode.Combine(part.Mesh.ResourceKey, p, (int)renderLod);
                    var structuralChanged = batchState.StructuralVersion != structuralSignature ||
                                            batchState.InstanceCount != instanceCount ||
                                            !batchState.MatchesIndices(chunk.InstanceIndices) ||
                                            batchState.TransformData.Length < instanceCount * TransformFloatStride;

                    if (structuralChanged)
                    {
                        var transforms = BuildTransformBuffer(layer, chunk.InstanceIndices, part);
                        batchState.ResetInstanceMap(chunk.InstanceIndices);
                        batchState.SetTransformData(transforms, layer, chunk.InstanceIndices);
                        WebGlInterop.UploadRetainedBatchTransformsBytes(
                            hostId,
                            batchId,
                            part.Mesh.ResourceKey,
                            lighting,
                            usePalette,
                            instanceCount,
                            ConvertFloatsToBytes(transforms));
                        stats.InstanceUploadBytes += transforms.Length * sizeof(float);
                        stats.TransformUploadBytes += transforms.Length * sizeof(float);
                        stats.InstanceBufferUploads++;
                        batchState.StructuralVersion = structuralSignature;
                        batchState.InstanceCount = instanceCount;
                        batchState.StateVersion = -1;
                        batchState.MaterialResolverVersion = -1;
                        batchState.LodPolicyVersion = -1;
                        batchState.FadeAlpha = float.NaN;
                        batchState.UsePalette = usePalette;
                    }
                    else
                    {
                        var dirtyTransformCount = UpdateDirtyTransformBuffer(layer, chunk.InstanceIndices, part, batchState);
                        if (dirtyTransformCount > 0)
                        {
                            if (dirtyTransformCount > System.Math.Max(32, instanceCount / 3))
                            {
                                WebGlInterop.UploadRetainedBatchTransformsBytes(
                                    hostId,
                                    batchId,
                                    part.Mesh.ResourceKey,
                                    lighting,
                                    usePalette,
                                    instanceCount,
                                    ConvertFloatsToBytes(batchState.TransformData));
                                stats.InstanceBufferUploads++;
                                stats.InstanceUploadBytes += batchState.TransformData.Length * sizeof(float);
                                stats.TransformUploadBytes += batchState.TransformData.Length * sizeof(float);
                            }
                            else
                            {
                                batchState.SortTransformDirtyOffsets();
                                UploadDirtyTransformRanges(hostId, batchId, batchState, scene.Performance, stats);
                            }
                        }
                    }

                    var alpha = ResolveChunkFadeAlpha(scene, layer, chunk.Bounds.Center);
                    var resolverChanged = batchState.MaterialResolverVersion != layer.MaterialResolverVersion;
                    var lodPolicyChanged = batchState.LodPolicyVersion != layer.LodPolicy.Version;
                    var fadeChanged = !NearlyEqual(batchState.FadeAlpha, alpha);
                    var stateVersionMissedWhileNotVisible = batchState.StateVersion != layer.StateBuffer.Version && !layer.StateBuffer.HasDirtyState;
                    var forceFullState = structuralChanged ||
                                         batchState.StateVersion < 0 ||
                                         batchState.UsePalette != usePalette ||
                                         resolverChanged ||
                                         lodPolicyChanged ||
                                         fadeChanged ||
                                         stateVersionMissedWhileNotVisible ||
                                         batchState.StateData.Length < instanceCount * StateFloatStride;

                    if (forceFullState)
                    {
                        var state = usePalette
                            ? BuildPaletteStateBuffer(layer, chunk.InstanceIndices, alpha)
                            : BuildColorStateBuffer(layer, chunk.InstanceIndices, part, alpha);
                        batchState.StateData = state;
                        var paletteWidth = batchState.PaletteWidth;
                        var paletteHeight = batchState.PaletteHeight;
                        var paletteBytes = Array.Empty<byte>();
                        if (usePalette && (batchState.PaletteVersion != layer.MaterialResolverVersion || batchState.UsePalette != usePalette))
                        {
                            paletteBytes = BuildPaletteBytes(layer.Template, part, out paletteWidth, out paletteHeight);
                            batchState.PaletteVersion = layer.MaterialResolverVersion;
                            batchState.PaletteWidth = paletteWidth;
                            batchState.PaletteHeight = paletteHeight;
                        }
                        else if (!usePalette)
                        {
                            paletteWidth = 1;
                            paletteHeight = 1;
                        }

                        WebGlInterop.UploadRetainedBatchStateBytes(
                            hostId,
                            batchId,
                            usePalette,
                            paletteWidth,
                            paletteHeight,
                            ConvertFloatsToBytes(state),
                            paletteBytes);
                        stats.StateBufferUploads++;
                        stats.StateUploadBytes += state.Length * sizeof(float);
                        batchState.StateVersion = layer.StateBuffer.Version;
                        batchState.MaterialResolverVersion = layer.MaterialResolverVersion;
                        batchState.LodPolicyVersion = layer.LodPolicy.Version;
                        batchState.FadeAlpha = alpha;
                        batchState.UsePalette = usePalette;
                    }
                    else if (layer.StateBuffer.HasDirtyState)
                    {
                        var dirtyCount = usePalette
                            ? UpdateDirtyPaletteStateBuffer(layer, batchState, alpha)
                            : UpdateDirtyColorStateBuffer(layer, batchState, part, alpha);
                        if (dirtyCount > 0)
                        {
                            if (dirtyCount > System.Math.Max(32, instanceCount / 3))
                            {
                                WebGlInterop.UploadRetainedBatchStateBytes(
                                    hostId,
                                    batchId,
                                    usePalette,
                                    batchState.PaletteWidth,
                                    batchState.PaletteHeight,
                                    ConvertFloatsToBytes(batchState.StateData),
                                    Array.Empty<byte>());
                                stats.StateBufferUploads++;
                                stats.StateUploadBytes += batchState.StateData.Length * sizeof(float);
                            }
                            else
                            {
                                batchState.SortStateDirtyOffsets();
                                UploadDirtyStateRanges(hostId, batchId, batchState, scene.Performance, stats);
                            }
                        }

                        batchState.StateVersion = layer.StateBuffer.Version;
                    }

                    drawBatches.Add(new WebGlRetainedBatchPacket
                    {
                        Id = batchId
                    });

                    stats.VisibleMeshCount += instanceCount;
                    stats.TriangleCount += (part.Mesh.Indices.Length / 3) * instanceCount;
                    if (part.UsesVertexMaterialSlots) stats.BakedHighScalePartDraws++;
                }

                chunk.MarkClean();
            }

            layer.StateBuffer.ClearDirty();
        }

        SweepDeadBatches(hostId);
        return drawBatches;
    }

    public void Reset(int hostId)
    {
        if (_batches.Count != 0)
        {
            WebGlInterop.ClearRetainedBatches(hostId);
        }

        _batches.Clear();
        _liveBatchIds.Clear();
        _frameId = 0;
    }

    private RetainedBatchState GetOrCreateBatch(string id)
    {
        if (!_batches.TryGetValue(id, out var state))
        {
            state = new RetainedBatchState();
            _batches[id] = state;
        }

        return state;
    }

    private void SweepDeadBatches(int hostId)
    {
        if (_batches.Count == _liveBatchIds.Count)
        {
            return;
        }

        var cutoffFrame = _frameId - RetainedBatchInvisibilityGraceFrames;
        var dead = new List<string>();
        foreach (var kvp in _batches)
        {
            if (_liveBatchIds.Contains(kvp.Key))
            {
                continue;
            }

            if (kvp.Value.LastLiveFrame > cutoffFrame)
            {
                continue;
            }

            dead.Add(kvp.Key);
        }

        for (var i = 0; i < dead.Count; i++)
        {
            _batches.Remove(dead[i]);
            WebGlInterop.DestroyRetainedBatch(hostId, dead[i]);
        }
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

    private static HighScaleLodLevel3D ResolveChunkLod(Scene3D scene, HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk)
    {
        var center = chunk.Bounds.Center;
        var transform = Matrix4x4.CreateTranslation(center);
        return layer.LodPolicy.Resolve(scene.Camera.Position, transform);
    }

    private static string BuildBatchId(HighScaleInstanceLayer3D layer, HighScaleChunkKey3D chunkKey, HighScaleLodLevel3D lod, int partIndex)
        => $"hs:{layer.Id}:{chunkKey.X}:{chunkKey.Y}:{chunkKey.Z}:{(int)lod}:{partIndex}";

    private static float[] BuildTransformBuffer(HighScaleInstanceLayer3D layer, IReadOnlyList<int> indices, CompositePartTemplate3D part)
    {
        var data = new float[indices.Count * TransformFloatStride];
        for (var i = 0; i < indices.Count; i++)
        {
            WriteTransform(layer, indices[i], part, data, i * TransformFloatStride);
        }

        return data;
    }

    private static int UpdateDirtyTransformBuffer(HighScaleInstanceLayer3D layer, IReadOnlyList<int> indices, CompositePartTemplate3D part, RetainedBatchState batchState)
    {
        batchState.ResetTransformDirtyOffsets();
        for (var offset = 0; offset < indices.Count; offset++)
        {
            var instanceIndex = indices[offset];
            var record = layer.Instances[instanceIndex];
            if (!batchState.IsTransformVersionDirty(offset, record.TransformVersion))
            {
                continue;
            }

            WriteTransform(layer, instanceIndex, part, batchState.TransformData, offset * TransformFloatStride);
            batchState.SetTransformVersion(offset, record.TransformVersion);
            batchState.AddTransformDirtyOffset(offset);
        }

        return batchState.TransformDirtyOffsetCount;
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

    private static float[] BuildPaletteStateBuffer(HighScaleInstanceLayer3D layer, IReadOnlyList<int> indices, float alpha)
    {
        var data = new float[indices.Count * StateFloatStride];
        for (var i = 0; i < indices.Count; i++)
        {
            var record = layer.Instances[indices[i]];
            var visible = (record.Flags & InstanceFlags3D.Visible) != 0 ? 1f : 0f;
            var o = i * StateFloatStride;
            data[o + 0] = record.MaterialVariantId;
            data[o + 1] = visible;
            data[o + 2] = alpha;
            data[o + 3] = 0f;
        }

        return data;
    }

    private static float[] BuildColorStateBuffer(HighScaleInstanceLayer3D layer, IReadOnlyList<int> indices, CompositePartTemplate3D part, float alpha)
    {
        var data = new float[indices.Count * StateFloatStride];
        for (var i = 0; i < indices.Count; i++)
        {
            var record = layer.Instances[indices[i]];
            var visible = (record.Flags & InstanceFlags3D.Visible) != 0 ? 1f : 0f;
            var color = layer.ResolveColor(part, record);
            var a = color.A * alpha * visible;
            var o = i * StateFloatStride;
            data[o + 0] = color.R;
            data[o + 1] = color.G;
            data[o + 2] = color.B;
            data[o + 3] = a;
        }

        return data;
    }

    private static int UpdateDirtyPaletteStateBuffer(HighScaleInstanceLayer3D layer, RetainedBatchState batchState, float alpha)
    {
        batchState.ResetStateDirtyOffsets();
        var dirtyIndices = layer.StateBuffer.DirtyIndices;
        for (var i = 0; i < dirtyIndices.Count; i++)
        {
            var instanceIndex = dirtyIndices[i];
            if (!batchState.TryGetOffset(instanceIndex, out var offset))
            {
                continue;
            }

            var record = layer.Instances[instanceIndex];
            var visible = (record.Flags & InstanceFlags3D.Visible) != 0 ? 1f : 0f;
            var o = offset * StateFloatStride;
            batchState.StateData[o + 0] = record.MaterialVariantId;
            batchState.StateData[o + 1] = visible;
            batchState.StateData[o + 2] = alpha;
            batchState.StateData[o + 3] = 0f;
            batchState.AddStateDirtyOffset(offset);
        }

        return batchState.StateDirtyOffsetCount;
    }

    private static int UpdateDirtyColorStateBuffer(HighScaleInstanceLayer3D layer, RetainedBatchState batchState, CompositePartTemplate3D part, float alpha)
    {
        batchState.ResetStateDirtyOffsets();
        var dirtyIndices = layer.StateBuffer.DirtyIndices;
        for (var i = 0; i < dirtyIndices.Count; i++)
        {
            var instanceIndex = dirtyIndices[i];
            if (!batchState.TryGetOffset(instanceIndex, out var offset))
            {
                continue;
            }

            var record = layer.Instances[instanceIndex];
            var visible = (record.Flags & InstanceFlags3D.Visible) != 0 ? 1f : 0f;
            var color = layer.ResolveColor(part, record);
            var o = offset * StateFloatStride;
            batchState.StateData[o + 0] = color.R;
            batchState.StateData[o + 1] = color.G;
            batchState.StateData[o + 2] = color.B;
            batchState.StateData[o + 3] = color.A * alpha * visible;
            batchState.AddStateDirtyOffset(offset);
        }

        return batchState.StateDirtyOffsetCount;
    }

    private static void UploadDirtyTransformRanges(int hostId, string batchId, RetainedBatchState batchState, ScenePerformanceOptions performance, RenderStats stats)
    {
        if (batchState.TransformDirtyOffsetCount <= 0)
        {
            return;
        }

        var mergeGap = System.Math.Max(0, performance.HighScalePartialStateMergeGap);
        var rangeStart = batchState.GetTransformDirtyOffsetAt(0);
        var previous = rangeStart;
        for (var i = 1; i <= batchState.TransformDirtyOffsetCount; i++)
        {
            var current = i < batchState.TransformDirtyOffsetCount ? batchState.GetTransformDirtyOffsetAt(i) : -1;
            if (current >= 0 && current <= previous + 1 + mergeGap)
            {
                previous = current;
                continue;
            }

            var floatOffset = rangeStart * TransformFloatStride;
            var floatCount = (previous - rangeStart + 1) * TransformFloatStride;
            WebGlInterop.UploadRetainedBatchTransformsRangeBytes(hostId, batchId, rangeStart, ConvertFloatsToBytes(batchState.TransformData, floatOffset, floatCount));
            stats.InstanceBufferSubDataUploads++;
            stats.InstanceUploadBytes += floatCount * sizeof(float);
            stats.TransformUploadBytes += floatCount * sizeof(float);
            rangeStart = current;
            previous = current;
        }
    }

    private static void UploadDirtyStateRanges(int hostId, string batchId, RetainedBatchState batchState, ScenePerformanceOptions performance, RenderStats stats)
    {
        if (batchState.StateDirtyOffsetCount <= 0)
        {
            return;
        }

        var mergeGap = System.Math.Max(0, performance.HighScalePartialStateMergeGap);
        var rangeStart = batchState.GetStateDirtyOffsetAt(0);
        var previous = rangeStart;
        for (var i = 1; i <= batchState.StateDirtyOffsetCount; i++)
        {
            var current = i < batchState.StateDirtyOffsetCount ? batchState.GetStateDirtyOffsetAt(i) : -1;
            if (current >= 0 && current <= previous + 1 + mergeGap)
            {
                previous = current;
                continue;
            }

            var floatOffset = rangeStart * StateFloatStride;
            var floatCount = (previous - rangeStart + 1) * StateFloatStride;
            WebGlInterop.UploadRetainedBatchStateRangeBytes(hostId, batchId, rangeStart, ConvertFloatsToBytes(batchState.StateData, floatOffset, floatCount));
            stats.StateBufferSubDataUploads++;
            stats.StateUploadBytes += floatCount * sizeof(float);
            rangeStart = current;
            previous = current;
        }
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

    private static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) <= 0.0001f;

    private static byte[] ConvertFloatsToBytes(float[] values)
        => ConvertFloatsToBytes(values, 0, values.Length);

    private static byte[] ConvertFloatsToBytes(float[] values, int start, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new byte[count * sizeof(float)];
        Buffer.BlockCopy(values, start * sizeof(float), bytes, 0, bytes.Length);
        return bytes;
    }

    private sealed class RetainedBatchState
    {
        private readonly Dictionary<int, int> _instanceOffsetMap = new();
        private int[] _orderedInstanceIndices = Array.Empty<int>();
        private int[] _stateDirtyOffsets = Array.Empty<int>();
        private int _stateDirtyOffsetCount;
        private int[] _transformDirtyOffsets = Array.Empty<int>();
        private int _transformDirtyOffsetCount;

        public int StructuralVersion = -1;
        public int StateVersion = -1;
        public int MaterialResolverVersion = -1;
        public int LodPolicyVersion = -1;
        public float FadeAlpha = float.NaN;
        public int InstanceCount;
        public bool UsePalette;
        public int PaletteVersion = -1;
        public int PaletteWidth = 1;
        public int PaletteHeight = 1;
        public float[] TransformData = Array.Empty<float>();
        public int[] TransformVersions = Array.Empty<int>();
        public float[] StateData = Array.Empty<float>();
        public int LastLiveFrame;
        public int StateDirtyOffsetCount => _stateDirtyOffsetCount;
        public int TransformDirtyOffsetCount => _transformDirtyOffsetCount;

        public void ResetInstanceMap(IReadOnlyList<int> indices)
        {
            _instanceOffsetMap.Clear();
            if (_orderedInstanceIndices.Length < indices.Count)
            {
                Array.Resize(ref _orderedInstanceIndices, indices.Count);
            }

            for (var i = 0; i < indices.Count; i++)
            {
                var index = indices[i];
                _orderedInstanceIndices[i] = index;
                _instanceOffsetMap[index] = i;
            }
        }

        public bool MatchesIndices(IReadOnlyList<int> indices)
        {
            if (InstanceCount != indices.Count)
            {
                return false;
            }

            if (_orderedInstanceIndices.Length < indices.Count)
            {
                return false;
            }

            for (var i = 0; i < indices.Count; i++)
            {
                if (_orderedInstanceIndices[i] != indices[i])
                {
                    return false;
                }
            }

            return true;
        }

        public void SetTransformData(float[] transforms, HighScaleInstanceLayer3D layer, IReadOnlyList<int> indices)
        {
            TransformData = transforms;
            if (TransformVersions.Length < indices.Count)
            {
                Array.Resize(ref TransformVersions, indices.Count);
            }

            for (var i = 0; i < indices.Count; i++)
            {
                TransformVersions[i] = layer.Instances[indices[i]].TransformVersion;
            }
        }

        public bool IsTransformVersionDirty(int offset, int transformVersion)
            => offset >= TransformVersions.Length || TransformVersions[offset] != transformVersion;

        public void SetTransformVersion(int offset, int transformVersion)
        {
            if (TransformVersions.Length <= offset)
            {
                Array.Resize(ref TransformVersions, Math.Max(offset + 1, Math.Max(16, TransformVersions.Length * 2)));
            }

            TransformVersions[offset] = transformVersion;
        }

        public bool TryGetOffset(int instanceIndex, out int offset) => _instanceOffsetMap.TryGetValue(instanceIndex, out offset);

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
