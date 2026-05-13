using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Rendering;

/// <summary>
/// Browser retained renderer for ordinary Object3D renderables.
///
/// The old WebGL path serialized full per-frame mesh batches into JSON and then called
/// gl.bufferData for every batch on every camera frame. This runtime makes JavaScript/WebGL
/// own the object batches: C# uploads geometry/materials once, then streams only transform/color
/// dirty ranges. Camera-only frames therefore update only uniforms and do not rebuild JSON
/// instance data or reupload instance buffers.
/// </summary>
internal sealed class WebGlRetainedOrdinaryRenderer
{
    private const int TransformStride = 16;
    private const int StateStride = 4;
    private const int FullRangeUploadThreshold = 128;
    private readonly Dictionary<string, BatchState> _batches = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveBatchIds = new(StringComparer.Ordinal);
    private readonly List<WebGlRetainedBatchPacket> _drawRefs = new(256);
    private readonly List<string> _deadScratch = new(64);
    private readonly Dictionary<string, ObjectSlot> _objectSlots = new(StringComparer.Ordinal);
    private readonly List<Object3D> _transformScratch = new(256);
    private readonly List<string> _dirtyTransformBatchIds = new(64);
    private readonly HashSet<string> _dirtyTransformBatchIdSet = new(StringComparer.Ordinal);
    private readonly List<string> _dirtyStateBatchIds = new(64);
    private readonly HashSet<string> _dirtyStateBatchIdSet = new(StringComparer.Ordinal);
    private bool _sceneDirty = true;
    private bool _hasTransparentBatches;
    private int _lastInterpolationVersion = -1;
    private int _lastTransparentCameraVersion = -1;
    private int _lastBatchTransformVersion = -1;
    private ulong _version;
    private ulong _lastDrawHash;

    public ulong Version => _version;

    public bool RequiresScenePlan(SceneRenderFrameContext3D frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var scene = frame.Scene;
        var interpolationChanged = scene.FrameInterpolator.Enabled &&
                                   _lastInterpolationVersion != scene.FrameInterpolator.RenderVersion;
        return _sceneDirty ||
               interpolationChanged ||
               (_hasTransparentBatches && _lastTransparentCameraVersion != scene.CameraVersion);
    }

    public void MarkDirty(SceneChangedEventArgs change)
    {
        // Camera and lighting changes are frame-global uniforms handled by the retained frame call;
        // they must not force a CPU-side object/batch rebuild. Pure transform/physics changes are
        // patched into retained instance buffers by BatchTransformVersion, avoiding full render-plan
        // rebuilds on browser physics/animation frames.
        if (change.Kind == SceneChangeKind.Camera)
        {
            if (_hasTransparentBatches)
            {
                _sceneDirty = true;
            }

            return;
        }

        if (change.Kind == SceneChangeKind.Lighting ||
            change.Kind == SceneChangeKind.HighScaleState)
        {
            return;
        }

        if (change.Kind == SceneChangeKind.Transform || change.Kind == SceneChangeKind.Physics || change.Kind == SceneChangeKind.AnimationPose)
        {
            if (_objectSlots.Count == 0)
            {
                _sceneDirty = true;
            }

            return;
        }

        if (change.Kind == SceneChangeKind.Material && change.Source is not null)
        {
            if (_objectSlots.Count == 0)
            {
                _sceneDirty = true;
                return;
            }

            if (_objectSlots.TryGetValue(change.Source.Id, out var slot) &&
                slot.Batch.TryQueueMaterialStatePatch(change.Source, slot.Offset, out var requiresRebuild))
            {
                if (requiresRebuild)
                {
                    _sceneDirty = true;
                    return;
                }

                if (_dirtyStateBatchIdSet.Add(slot.BatchId))
                {
                    _dirtyStateBatchIds.Add(slot.BatchId);
                }

                return;
            }
        }

        _sceneDirty = true;
    }

    public List<WebGlRetainedBatchPacket> BuildAndUpload(int hostId, SceneRenderPlan3D plan, RenderStats stats)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (!plan.IncludesOrdinary)
        {
            UploadIncrementalPatches(hostId, plan.Frame, stats);
            stats.DrawCallCount += _drawRefs.Count;
            stats.EstimatedDrawCallCount += _drawRefs.Count;
            stats.InstancedBatchCount += _drawRefs.Count;
            return _drawRefs;
        }

        var scene = plan.Frame.Scene;
        var interpolationVersion = scene.FrameInterpolator.RenderVersion;
        if ((scene.FrameInterpolator.Enabled && _lastInterpolationVersion != interpolationVersion) ||
            (_hasTransparentBatches && _lastTransparentCameraVersion != scene.CameraVersion))
        {
            _sceneDirty = true;
        }

        if (!_sceneDirty)
        {
            UploadIncrementalPatches(hostId, plan.Frame, stats);
            stats.DrawCallCount += _drawRefs.Count;
            stats.EstimatedDrawCallCount += _drawRefs.Count;
            stats.InstancedBatchCount += _drawRefs.Count;
            return _drawRefs;
        }

        var previousDrawHash = _lastDrawHash;
        _drawRefs.Clear();
        _liveBatchIds.Clear();
        _deadScratch.Clear();
        _objectSlots.Clear();
        _dirtyTransformBatchIds.Clear();
        _dirtyTransformBatchIdSet.Clear();
        _dirtyStateBatchIds.Clear();
        _dirtyStateBatchIdSet.Clear();
        _hasTransparentBatches = false;

        var ordinaryBatches = plan.OrdinaryBatches;
        for (var batchIndex = 0; batchIndex < ordinaryBatches.Count; batchIndex++)
        {
            var plannedBatch = ordinaryBatches[batchIndex];
            var batchId = plannedBatch.BatchId;
            var batch = GetOrCreateBatch(batchId);
            _liveBatchIds.Add(batchId);
            batch.BeginFrame(plannedBatch.Mesh, plannedBatch.Material);
            for (var itemIndex = 0; itemIndex < plannedBatch.Items.Count; itemIndex++)
            {
                var item = plannedBatch.Items[itemIndex];
                var offset = batch.Add(item);
                _objectSlots[item.Owner.Id] = new ObjectSlot(batchId, batch, offset);
            }
        }

        var transparentItems = plan.TransparentOrdinaryItems;
        for (var itemIndex = 0; itemIndex < transparentItems.Count; itemIndex++)
        {
            var transparent = transparentItems[itemIndex];
            var batchId = transparent.DrawId;
            var batch = GetOrCreateBatch(batchId);
            _liveBatchIds.Add(batchId);
            batch.BeginFrame(transparent.Item.Mesh, transparent.Item.Material);
            var offset = batch.Add(transparent.Item);
            _objectSlots[transparent.Item.Owner.Id] = new ObjectSlot(batchId, batch, offset);
        }

        var transparentBatches = plan.TransparentOrdinaryBatches;
        for (var batchIndex = 0; batchIndex < transparentBatches.Count; batchIndex++)
        {
            var plannedBatch = transparentBatches[batchIndex];
            var batchId = plannedBatch.BatchId;
            var batch = GetOrCreateBatch(batchId);
            _liveBatchIds.Add(batchId);
            batch.BeginFrame(plannedBatch.Mesh, plannedBatch.Material);
            for (var itemIndex = 0; itemIndex < plannedBatch.Items.Count; itemIndex++)
            {
                var item = plannedBatch.Items[itemIndex];
                var offset = batch.Add(item);
                _objectSlots[item.Owner.Id] = new ObjectSlot(batchId, batch, offset);
            }
        }

        for (var commandIndex = 0; commandIndex < plan.DrawCommands.Count; commandIndex++)
        {
            var command = plan.DrawCommands[commandIndex];
            if (command.Kind != SceneRenderCommandKind3D.OrdinaryBatch &&
                command.Kind != SceneRenderCommandKind3D.TransparentOrdinaryItem &&
                command.Kind != SceneRenderCommandKind3D.TransparentOrdinaryBatch)
            {
                continue;
            }

            var batchId = command.Kind switch
            {
                SceneRenderCommandKind3D.OrdinaryBatch => command.OrdinaryBatch?.BatchId,
                SceneRenderCommandKind3D.TransparentOrdinaryItem => command.TransparentOrdinary?.DrawId,
                SceneRenderCommandKind3D.TransparentOrdinaryBatch => command.TransparentOrdinaryBatch?.BatchId,
                _ => null
            };
            if (string.IsNullOrEmpty(batchId) || !_batches.TryGetValue(batchId, out var batch))
            {
                continue;
            }

            batch.EndFrameAndUpload(hostId, batchId, interpolationVersion, stats);
            _hasTransparentBatches |= command.Transparent;
            _drawRefs.Add(new WebGlRetainedBatchPacket
            {
                Id = batchId,
                Transparent = command.Transparent,
                SortDistanceSquared = command.SortDistanceSquared,
                DrawOrder = command.SourceOrder
            });
        }

        foreach (var batchId in _batches.Keys)
        {
            if (!_liveBatchIds.Contains(batchId))
            {
                _deadScratch.Add(batchId);
            }
        }

        for (var i = 0; i < _deadScratch.Count; i++)
        {
            WebGlInterop.DestroyRetainedBatch(hostId, _deadScratch[i]);
            _batches.Remove(_deadScratch[i]);
            _version++;
        }
        _deadScratch.Clear();

        _lastDrawHash = ComputeDrawHash(_drawRefs);
        if (_lastDrawHash != previousDrawHash)
        {
            _version++;
        }

        stats.DrawCallCount += _drawRefs.Count;
        stats.EstimatedDrawCallCount += _drawRefs.Count;
        stats.InstancedBatchCount += _drawRefs.Count;
        _sceneDirty = false;
        _lastInterpolationVersion = interpolationVersion;
        _lastTransparentCameraVersion = scene.CameraVersion;
        _lastBatchTransformVersion = scene.BatchTransformVersion;
        return _drawRefs;
    }


    private void UploadIncrementalPatches(int hostId, SceneRenderFrameContext3D frame, RenderStats stats)
    {
        UploadIncrementalTransformPatches(hostId, frame, stats);
        UploadQueuedStatePatches(hostId, stats);
    }

    private void UploadQueuedStatePatches(int hostId, RenderStats stats)
    {
        if (_dirtyStateBatchIds.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _dirtyStateBatchIds.Count; i++)
        {
            var batchId = _dirtyStateBatchIds[i];
            if (_batches.TryGetValue(batchId, out var batch))
            {
                batch.UploadQueuedStateRanges(hostId, batchId, stats);
            }
        }

        _dirtyStateBatchIds.Clear();
        _dirtyStateBatchIdSet.Clear();
    }

    private void UploadIncrementalTransformPatches(int hostId, SceneRenderFrameContext3D frame, RenderStats stats)
    {
        var scene = frame.Scene;
        if (_lastBatchTransformVersion == scene.BatchTransformVersion)
        {
            return;
        }

        if (_lastBatchTransformVersion < 0 || _objectSlots.Count == 0)
        {
            _sceneDirty = true;
            return;
        }

        if (!scene.TryCopyBatchTransformChangesSince(_lastBatchTransformVersion, _transformScratch))
        {
            _sceneDirty = true;
            _lastBatchTransformVersion = scene.BatchTransformVersion;
            _transformScratch.Clear();
            return;
        }

        if (_transformScratch.Count == 0)
        {
            _lastBatchTransformVersion = scene.BatchTransformVersion;
            return;
        }

        _dirtyTransformBatchIds.Clear();
        _dirtyTransformBatchIdSet.Clear();
        for (var i = 0; i < _transformScratch.Count; i++)
        {
            var obj = _transformScratch[i];
            if (obj is null)
            {
                continue;
            }

            if (!_objectSlots.TryGetValue(obj.Id, out var slot))
            {
                continue;
            }

            if (!obj.IsVisible || !obj.UseMeshRendering)
            {
                _sceneDirty = true;
                continue;
            }

            var model = scene.FrameInterpolator.TryGetInterpolatedModel(obj.Id, out var interpolated)
                ? interpolated
                : obj.GetModelMatrix();
            slot.Batch.WriteTransformAt(slot.Offset, model, obj.TransformVersion);
            slot.Batch.QueueTransformDirtyOffset(slot.Offset);
            if (_dirtyTransformBatchIdSet.Add(slot.BatchId))
            {
                _dirtyTransformBatchIds.Add(slot.BatchId);
            }
        }

        for (var i = 0; i < _dirtyTransformBatchIds.Count; i++)
        {
            var batchId = _dirtyTransformBatchIds[i];
            if (_batches.TryGetValue(batchId, out var batch))
            {
                batch.UploadQueuedTransformRanges(hostId, batchId, stats);
                batch.UploadSkinningIfNeededForIncrementalPatch(hostId, batchId, stats);
            }
        }

        _lastBatchTransformVersion = scene.BatchTransformVersion;
        _transformScratch.Clear();
        _dirtyTransformBatchIds.Clear();
        _dirtyTransformBatchIdSet.Clear();
    }

    private readonly struct ObjectSlot
    {
        public ObjectSlot(string batchId, BatchState batch, int offset)
        {
            BatchId = batchId;
            Batch = batch;
            Offset = offset;
        }

        public string BatchId { get; }
        public BatchState Batch { get; }
        public int Offset { get; }
    }

    private static ulong ComputeDrawHash(List<WebGlRetainedBatchPacket> refs)
    {
        var hash = SceneRenderDrawOrder3D.CreateHashSeed();
        for (var i = 0; i < refs.Count; i++)
        {
            var packet = refs[i];
            hash = SceneRenderDrawOrder3D.HashPacket(
                hash,
                packet.Id,
                packet.Transparent,
                packet.SortDistanceSquared,
                packet.DrawOrder,
                includeSourceOrder: true);
        }

        return hash;
    }

    public void Reset(int hostId)
    {
        foreach (var id in _batches.Keys)
        {
            WebGlInterop.DestroyRetainedBatch(hostId, id);
        }

        _batches.Clear();
        _drawRefs.Clear();
        _liveBatchIds.Clear();
        _deadScratch.Clear();
        _objectSlots.Clear();
        _transformScratch.Clear();
        _dirtyTransformBatchIds.Clear();
        _dirtyTransformBatchIdSet.Clear();
        _dirtyStateBatchIds.Clear();
        _dirtyStateBatchIdSet.Clear();
        _sceneDirty = true;
        _hasTransparentBatches = false;
        _lastInterpolationVersion = -1;
        _lastTransparentCameraVersion = -1;
        _lastBatchTransformVersion = -1;
        _lastDrawHash = 0UL;
        _version++;
    }

    private BatchState GetOrCreateBatch(string id)
    {
        if (!_batches.TryGetValue(id, out var state))
        {
            state = new BatchState();
            _batches[id] = state;
            _version++;
        }

        return state;
    }

    private sealed class BatchState
    {
        private readonly List<Object3D> _objects = new(64);
        private readonly List<int> _transformDirtyOffsets = new(64);
        private readonly List<int> _stateDirtyOffsets = new(64);
        private string[] _objectIds = Array.Empty<string>();
        private int[] _transformVersions = Array.Empty<int>();
        private int[] _materialVersions = Array.Empty<int>();
        private int[] _stateHashes = Array.Empty<int>();
        private int _skinningVersion = -1;
        private int _interpolationVersion = -1;
        private bool _skinningEnabled;
        private float[] _transforms = Array.Empty<float>();
        private float[] _state = Array.Empty<float>();
        private float[] _skinMatrixScratch = Array.Empty<float>();
        private byte[] _transformUploadBytes = Array.Empty<byte>();
        private byte[] _stateUploadBytes = Array.Empty<byte>();
        private byte[] _rangeUploadBytes = Array.Empty<byte>();
        private byte[] _skinningUploadBytes = Array.Empty<byte>();
        private Mesh3D? _mesh;
        private string? _meshKey;
        private int _meshGeometryVersion;
        private string? _materialKey;
        private MaterialBinding3D _material;
        private int _count;
        private bool _structuralDirty = true;
        private bool _materialDirty = true;

        public bool Transparent { get; private set; }

        public void BeginFrame(Mesh3D mesh, MaterialBinding3D material)
        {
            _objects.Clear();
            _mesh = mesh;
            if (!string.Equals(_meshKey, mesh.ResourceKey, StringComparison.Ordinal) || _meshGeometryVersion != mesh.GeometryVersion)
            {
                _meshKey = mesh.ResourceKey;
                _meshGeometryVersion = mesh.GeometryVersion;
                _structuralDirty = true;
            }

            Transparent = material.Surface == SurfaceMode.Transparent || material.BaseColor.A < 0.999f;
            _material = material;
            if (!string.Equals(_materialKey, material.Key, StringComparison.Ordinal))
            {
                _materialKey = material.Key;
                _materialDirty = true;
            }
        }

        public int Add(OrdinaryRenderItem3D item)
        {
            _objects.Add(item.Owner);
            var offset = _objects.Count - 1;
            EnsureCapacity(_objects.Count);
            WriteMatrix(_transforms, offset * TransformStride, item.Model);
            WriteColor(_state, offset * StateStride, item.Color);
            return offset;
        }

        public bool EndFrameAndUpload(int hostId, string batchId, int interpolationVersion, RenderStats stats)
        {
            if (_mesh is null || _objects.Count == 0)
            {
                return false;
            }

            var changed = false;
            _transformDirtyOffsets.Clear();
            _stateDirtyOffsets.Clear();

            if (_count != _objects.Count)
            {
                _structuralDirty = true;
            }

            var interpolationDirty = _interpolationVersion != interpolationVersion;
            for (var i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (!_structuralDirty && !string.Equals(_objectIds[i], obj.Id, StringComparison.Ordinal))
                {
                    _structuralDirty = true;
                }

                if (_structuralDirty || interpolationDirty || _transformVersions[i] != obj.TransformVersion)
                {
                    _transformDirtyOffsets.Add(i);
                    _transformVersions[i] = obj.TransformVersion;
                }

                var stateHash = ComputeStateHash(_state, i * StateStride);
                if (_structuralDirty || _materialVersions[i] != obj.MaterialVersion || _stateHashes[i] != stateHash)
                {
                    _stateDirtyOffsets.Add(i);
                    _materialVersions[i] = obj.MaterialVersion;
                    _stateHashes[i] = stateHash;
                }

                _objectIds[i] = obj.Id;
            }

            if (_structuralDirty)
            {
                var transformBytes = CopyFloatsToBuffer(_transforms, 0, _objects.Count * TransformStride, ref _transformUploadBytes);
                WebGlInterop.UploadRetainedBatchTransformsBytes(hostId, batchId, _mesh.ResourceKey, ToLightingUniform(_material.Lighting), false, _objects.Count, transformBytes);
                var stateBytes = CopyFloatsToBuffer(_state, 0, _objects.Count * StateStride, ref _stateUploadBytes);
                WebGlInterop.UploadRetainedBatchStateBytes(hostId, batchId, false, 1, 1, stateBytes, Array.Empty<byte>());
                _count = _objects.Count;
                _structuralDirty = false;
                _interpolationVersion = interpolationVersion;
                changed = true;
                stats.InstanceBufferUploads++;
                stats.StateBufferUploads++;
                stats.InstanceUploadBytes += transformBytes.LongLength;
                stats.StateUploadBytes += stateBytes.LongLength;
            }
            else
            {
                changed |= UploadDirtyRanges(hostId, batchId, _transformDirtyOffsets, _transforms, _count, TransformStride, true, stats);
                changed |= UploadDirtyRanges(hostId, batchId, _stateDirtyOffsets, _state, _count, StateStride, false, stats);
                _interpolationVersion = interpolationVersion;
            }

            if (_materialDirty)
            {
                WebGlInterop.UploadRetainedBatchMaterial(
                    hostId,
                    batchId,
                    _material.NormalMapStrength,
                    _material.HasBaseColorTexture ? _material.BaseColorTextureKey ?? string.Empty : string.Empty,
                    _material.HasNormalMap ? _material.NormalMapTextureKey ?? string.Empty : string.Empty,
                    _material.HasMetallicRoughnessTexture ? _material.MetallicRoughnessTextureKey ?? string.Empty : string.Empty,
                    _material.HasEmissiveTexture ? _material.EmissiveTextureKey ?? string.Empty : string.Empty,
                    _material.Metallic,
                    _material.Roughness,
                    (_material.Surface == SurfaceMode.Transparent || _material.BaseColor.A < 0.999f) ? 0f : _material.AlphaCutoff,
                    _material.Surface == SurfaceMode.Transparent || _material.BaseColor.A < 0.999f,
                    _material.EmissiveColor.R,
                    _material.EmissiveColor.G,
                    _material.EmissiveColor.B,
                    _material.EmissiveColor.A);
                _materialDirty = false;
                changed = true;
            }

            changed |= UploadSkinningIfNeeded(hostId, batchId, stats);

            return changed;
        }


        public void WriteTransformAt(int offset, Matrix4x4 model, int transformVersion)
        {
            if ((uint)offset >= (uint)_count)
            {
                return;
            }

            WriteMatrix(_transforms, offset * TransformStride, model);
            _transformVersions[offset] = transformVersion;
        }

        public void QueueTransformDirtyOffset(int offset)
        {
            if ((uint)offset < (uint)_count)
            {
                _transformDirtyOffsets.Add(offset);
            }
        }

        public bool UploadQueuedTransformRanges(int hostId, string batchId, RenderStats stats)
        {
            var changed = UploadDirtyRanges(hostId, batchId, _transformDirtyOffsets, _transforms, _count, TransformStride, true, stats);
            _transformDirtyOffsets.Clear();
            return changed;
        }

        public bool UploadSkinningIfNeededForIncrementalPatch(int hostId, string batchId, RenderStats stats)
            => UploadSkinningIfNeeded(hostId, batchId, stats);

        public bool TryQueueMaterialStatePatch(Object3D obj, int offset, out bool requiresRebuild)
        {
            requiresRebuild = false;
            if ((uint)offset >= (uint)_count || !string.Equals(_objectIds[offset], obj.Id, StringComparison.Ordinal))
            {
                requiresRebuild = true;
                return true;
            }

            var material = MaterialBinding3D.FromMaterial(obj.Material);
            var transparent = material.Surface == SurfaceMode.Transparent || material.BaseColor.A < 0.999f;
            if (!string.Equals(_material.BatchKey, material.BatchKey, StringComparison.Ordinal) || Transparent != transparent)
            {
                requiresRebuild = true;
                return true;
            }

            var stateOffset = offset * StateStride;
            WriteColor(_state, stateOffset, SceneOrdinaryRenderItemBuilder3D.ResolveColor(obj));
            _materialVersions[offset] = obj.MaterialVersion;
            _stateHashes[offset] = ComputeStateHash(_state, stateOffset);
            _stateDirtyOffsets.Add(offset);
            return true;
        }

        public bool UploadQueuedStateRanges(int hostId, string batchId, RenderStats stats)
        {
            var changed = UploadDirtyRanges(hostId, batchId, _stateDirtyOffsets, _state, _count, StateStride, false, stats);
            _stateDirtyOffsets.Clear();
            return changed;
        }


        private bool UploadSkinningIfNeeded(int hostId, string batchId, RenderStats stats)
        {
            if (_objects.Count == 0 || _objects[0] is not ModelPart3D part || !part.IsSkinned || part.CurrentGpuSkinMatrices.Length == 0)
            {
                if (_skinningEnabled)
                {
                    WebGlInterop.UploadRetainedBatchSkinningBytes(hostId, batchId, false, 0, Array.Empty<byte>());
                    _skinningEnabled = false;
                    _skinningVersion = -1;
                    return true;
                }

                return false;
            }

            if (_skinningEnabled && _skinningVersion == part.SkinningVersion)
            {
                return false;
            }

            var matrices = part.CurrentGpuSkinMatrices;
            var floatCount = matrices.Length * 16;
            if (_skinMatrixScratch.Length != floatCount)
            {
                _skinMatrixScratch = new float[floatCount];
            }

            for (var i = 0; i < matrices.Length; i++)
            {
                WriteMatrix(_skinMatrixScratch, i * 16, matrices[i]);
            }

            var bytes = CopyFloatsToBuffer(_skinMatrixScratch, 0, floatCount, ref _skinningUploadBytes);
            WebGlInterop.UploadRetainedBatchSkinningBytes(hostId, batchId, true, matrices.Length, bytes);
            _skinningEnabled = true;
            _skinningVersion = part.SkinningVersion;
            stats.SkinMatrixCount += matrices.Length;
            stats.TransformUploadBytes += bytes.LongLength;
            return true;
        }

        private bool UploadDirtyRanges(int hostId, string batchId, List<int> dirtyOffsets, float[] data, int activeCount, int stride, bool transform, RenderStats stats)
        {
            if (dirtyOffsets.Count == 0) return false;
            if (dirtyOffsets.Count >= FullRangeUploadThreshold)
            {
                var bytes = CopyFloatsToBuffer(data, 0, activeCount * stride, ref _rangeUploadBytes);
                if (transform) WebGlInterop.UploadRetainedBatchTransformsRangeBytes(hostId, batchId, 0, bytes);
                else WebGlInterop.UploadRetainedBatchStateRangeBytes(hostId, batchId, 0, bytes);
                if (transform) { stats.TransformUploadBytes += bytes.LongLength; stats.InstanceUploadBytes += bytes.LongLength; }
                else { stats.StateUploadBytes += bytes.LongLength; }
                return true;
            }

            dirtyOffsets.Sort();
            var changed = false;
            var rangeStart = dirtyOffsets[0];
            var previous = rangeStart;
            for (var i = 1; i <= dirtyOffsets.Count; i++)
            {
                if (i < dirtyOffsets.Count)
                {
                    var current = dirtyOffsets[i];
                    if (current == previous)
                    {
                        continue;
                    }

                    if (current == previous + 1)
                    {
                        previous = current;
                        continue;
                    }
                }

                var floatOffset = rangeStart * stride;
                var floatCount = (previous - rangeStart + 1) * stride;
                var bytes = CopyFloatsToBuffer(data, floatOffset, floatCount, ref _rangeUploadBytes);
                if (transform) WebGlInterop.UploadRetainedBatchTransformsRangeBytes(hostId, batchId, rangeStart, bytes);
                else WebGlInterop.UploadRetainedBatchStateRangeBytes(hostId, batchId, rangeStart, bytes);
                if (transform) { stats.TransformUploadBytes += bytes.LongLength; stats.InstanceUploadBytes += bytes.LongLength; }
                else { stats.StateUploadBytes += bytes.LongLength; }
                changed = true;

                if (i < dirtyOffsets.Count)
                {
                    rangeStart = dirtyOffsets[i];
                    previous = rangeStart;
                }
            }

            return changed;
        }

        private void EnsureCapacity(int count)
        {
            if (_objectIds.Length >= count) return;
            var newSize = Math.Max(count, Math.Max(16, _objectIds.Length * 2));
            Array.Resize(ref _objectIds, newSize);
            Array.Resize(ref _transformVersions, newSize);
            Array.Resize(ref _materialVersions, newSize);
            Array.Resize(ref _stateHashes, newSize);
            Array.Resize(ref _transforms, newSize * TransformStride);
            Array.Resize(ref _state, newSize * StateStride);
        }

        private static void WriteMatrix(float[] buffer, int offset, Matrix4x4 matrix)
        {
            buffer[offset] = matrix.M11; buffer[offset + 1] = matrix.M12; buffer[offset + 2] = matrix.M13; buffer[offset + 3] = matrix.M14;
            buffer[offset + 4] = matrix.M21; buffer[offset + 5] = matrix.M22; buffer[offset + 6] = matrix.M23; buffer[offset + 7] = matrix.M24;
            buffer[offset + 8] = matrix.M31; buffer[offset + 9] = matrix.M32; buffer[offset + 10] = matrix.M33; buffer[offset + 11] = matrix.M34;
            buffer[offset + 12] = matrix.M41; buffer[offset + 13] = matrix.M42; buffer[offset + 14] = matrix.M43; buffer[offset + 15] = matrix.M44;
        }

        private static void WriteColor(float[] buffer, int offset, ColorRgba color)
        {
            buffer[offset] = color.R;
            buffer[offset + 1] = color.G;
            buffer[offset + 2] = color.B;
            buffer[offset + 3] = color.A;
        }

        private static int ComputeStateHash(float[] buffer, int offset)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + buffer[offset].GetHashCode();
                hash = hash * 31 + buffer[offset + 1].GetHashCode();
                hash = hash * 31 + buffer[offset + 2].GetHashCode();
                hash = hash * 31 + buffer[offset + 3].GetHashCode();
                return hash;
            }
        }

        private static byte[] CopyFloatsToBuffer(float[] data, int offset, int count, ref byte[] buffer)
        {
            if (count <= 0) return Array.Empty<byte>();
            var byteCount = count * sizeof(float);
            // JS interop consumes the whole byte[]; keep this exact-sized until the bridge
            // accepts an explicit byteCount/span. Avoiding realloc here requires a protocol change.
            if (buffer.Length != byteCount) buffer = new byte[byteCount];
            Buffer.BlockCopy(data, offset * sizeof(float), buffer, 0, byteCount);
            return buffer;
        }

        private static float ToLightingUniform(LightingMode mode)
            => mode == LightingMode.Unlit ? 0f : mode == LightingMode.Lambert ? 1f : mode == LightingMode.Phong ? 2f : 3f;
    }
}
