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
/// v43 data-flow rule: structural data is uploaded to JS/WebGL only when chunk/LOD
/// membership changes. Status telemetry is uploaded as small dirty state ranges through
/// gl.bufferSubData; the renderer must not rebuild every visible batch when the layer-level
/// StateBuffer.Version changes.
/// </summary>
internal sealed class WebGlRetainedHighScaleRenderer
{
    private const int TransformFloatStride = 16;
    private const int StateFloatStride = 4;
    private readonly Dictionary<string, RetainedBatchState> _batches = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveBatchIds = new(StringComparer.Ordinal);

    public List<WebGlRetainedBatchPacket> BuildAndUpload(int hostId, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
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
            stats.VisibleChunkCount += visibleChunks.Count;

            for (var c = 0; c < visibleChunks.Count; c++)
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
                    var structuralVersion = HashCode.Combine(chunk.Version, layer.Instances.Version, part.Mesh.ResourceKey, p, (int)renderLod);
                    var instanceCount = chunk.InstanceIndices.Count;

                    var structuralChanged = batchState.StructuralVersion != structuralVersion || batchState.InstanceCount != instanceCount;
                    if (structuralChanged)
                    {
                        var transforms = BuildTransformBuffer(layer, chunk.InstanceIndices, part);
                        batchState.ResetInstanceMap(chunk.InstanceIndices);
                        WebGlInterop.UploadRetainedBatchTransforms(
                            hostId,
                            batchId,
                            part.Mesh.ResourceKey,
                            lighting,
                            usePalette,
                            instanceCount,
                            ConvertFloatsToBase64(transforms));
                        stats.InstanceUploadBytes += transforms.Length * sizeof(float);
                        stats.TransformUploadBytes += transforms.Length * sizeof(float);
                        stats.InstanceBufferUploads++;
                        batchState.StructuralVersion = structuralVersion;
                        batchState.InstanceCount = instanceCount;
                        batchState.StateVersion = -1;
                        batchState.MaterialResolverVersion = -1;
                        batchState.LodPolicyVersion = -1;
                        batchState.FadeAlpha = float.NaN;
                        batchState.UsePalette = usePalette;
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
                        var paletteBytes = string.Empty;
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

                        WebGlInterop.UploadRetainedBatchState(
                            hostId,
                            batchId,
                            usePalette,
                            paletteWidth,
                            paletteHeight,
                            ConvertFloatsToBase64(state),
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
                            batchState.SortDirtyOffsets();
                            UploadDirtyStateRanges(hostId, batchId, batchState, stats);
                        }

                        // Even if this batch did not contain any of the current dirty indices,
                        // it has now consumed the layer state version for the current visible set.
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
            }

            // WebGL must clear dirty state after all visible batches consume it. Without this,
            // the dirty set grows until every frame becomes a full state-buffer rewrite.
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

        var dead = new List<string>();
        foreach (var id in _batches.Keys)
        {
            if (!_liveBatchIds.Contains(id))
            {
                dead.Add(id);
            }
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
            var record = layer.Instances[indices[i]];
            var model = part.LocalTransform * record.Transform;
            var o = i * TransformFloatStride;
            data[o + 0] = model.M11; data[o + 1] = model.M12; data[o + 2] = model.M13; data[o + 3] = model.M14;
            data[o + 4] = model.M21; data[o + 5] = model.M22; data[o + 6] = model.M23; data[o + 7] = model.M24;
            data[o + 8] = model.M31; data[o + 9] = model.M32; data[o + 10] = model.M33; data[o + 11] = model.M34;
            data[o + 12] = model.M41; data[o + 13] = model.M42; data[o + 14] = model.M43; data[o + 15] = model.M44;
        }

        return data;
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
        batchState.ResetDirtyOffsets();
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
            batchState.AddDirtyOffset(offset);
        }

        return batchState.DirtyOffsetCount;
    }

    private static int UpdateDirtyColorStateBuffer(HighScaleInstanceLayer3D layer, RetainedBatchState batchState, CompositePartTemplate3D part, float alpha)
    {
        batchState.ResetDirtyOffsets();
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
            batchState.AddDirtyOffset(offset);
        }

        return batchState.DirtyOffsetCount;
    }

    private static void UploadDirtyStateRanges(int hostId, string batchId, RetainedBatchState batchState, RenderStats stats)
    {
        if (batchState.DirtyOffsetCount <= 0)
        {
            return;
        }

        var rangeStart = batchState.GetDirtyOffsetAt(0);
        var previous = rangeStart;
        for (var i = 1; i <= batchState.DirtyOffsetCount; i++)
        {
            var current = i < batchState.DirtyOffsetCount ? batchState.GetDirtyOffsetAt(i) : -1;
            if (current >= 0 && current == previous + 1)
            {
                previous = current;
                continue;
            }

            var floatOffset = rangeStart * StateFloatStride;
            var floatCount = (previous - rangeStart + 1) * StateFloatStride;
            WebGlInterop.UploadRetainedBatchStateRange(hostId, batchId, rangeStart, ConvertFloatsToBase64(batchState.StateData, floatOffset, floatCount));
            stats.StateBufferSubDataUploads++;
            stats.StateUploadBytes += floatCount * sizeof(float);
            rangeStart = current;
            previous = current;
        }
    }

    private static float ResolveChunkFadeAlpha(Scene3D scene, HighScaleInstanceLayer3D layer, Vector3 chunkCenter)
    {
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

    private static string BuildPaletteBytes(CompositeTemplate3D template, CompositePartTemplate3D part, out int width, out int height)
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

        return Convert.ToBase64String(bytes);
    }

    private static byte ToByte(float value) => (byte)System.Math.Clamp((int)MathF.Round(System.Math.Clamp(value, 0f, 1f) * 255f), 0, 255);

    private static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) <= 0.0001f;

    private static string ConvertFloatsToBase64(float[] values)
        => ConvertFloatsToBase64(values, 0, values.Length);

    private static string ConvertFloatsToBase64(float[] values, int start, int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        var bytes = new byte[count * sizeof(float)];
        Buffer.BlockCopy(values, start * sizeof(float), bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private sealed class RetainedBatchState
    {
        private readonly Dictionary<int, int> _instanceOffsetMap = new();
        private int[] _dirtyOffsets = Array.Empty<int>();
        private int _dirtyOffsetCount;

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
        public float[] StateData = Array.Empty<float>();
        public int DirtyOffsetCount => _dirtyOffsetCount;

        public void ResetInstanceMap(IReadOnlyList<int> indices)
        {
            _instanceOffsetMap.Clear();
            for (var i = 0; i < indices.Count; i++)
            {
                _instanceOffsetMap[indices[i]] = i;
            }
        }

        public bool TryGetOffset(int instanceIndex, out int offset) => _instanceOffsetMap.TryGetValue(instanceIndex, out offset);

        public void ResetDirtyOffsets() => _dirtyOffsetCount = 0;

        public void AddDirtyOffset(int offset)
        {
            if (_dirtyOffsets.Length <= _dirtyOffsetCount)
            {
                Array.Resize(ref _dirtyOffsets, Math.Max(16, _dirtyOffsets.Length * 2));
            }

            _dirtyOffsets[_dirtyOffsetCount++] = offset;
        }

        public void SortDirtyOffsets() => Array.Sort(_dirtyOffsets, 0, _dirtyOffsetCount);

        public int GetDirtyOffsetAt(int index) => _dirtyOffsets[index];
    }
}
