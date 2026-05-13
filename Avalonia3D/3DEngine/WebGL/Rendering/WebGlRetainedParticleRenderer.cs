using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Rendering;

/// <summary>
/// WebGL retained particle renderer.
///
/// The portable Object3D particle implementation exposes stable static unit meshes and per-particle
/// instance data. Browser/WebGL must stay on that retained path: every camera move should update only
/// uniforms, and every simulation tick should stream compact particle instance transforms/colors.
/// </summary>
internal sealed class WebGlRetainedParticleRenderer
{
    private const int MaxParticleStride = ParticleInstanceStream3D.MaxFloatStride;
    // Particle meshes are owned by Core so all backends sweep/upload the same resource keys.

    private readonly Dictionary<string, ParticleBatchState> _batches = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveBatchIds = new(StringComparer.Ordinal);
    private readonly List<string> _deadScratch = new(16);
    private readonly List<WebGlRetainedBatchPacket> _drawRefs = new(32);
    private int _lastPlannedParticleVersion = -1;
    private int _lastPlannedRegistryVersion = -1;
    private int _lastPlannedCameraVersion = -1;
    private bool _hasCameraDependentParticleBatches;
    private bool _sceneDirty = true;
    private ulong _version;

    public ulong Version => _version;

    public void MarkDirty(SceneChangedEventArgs change)
    {
        if (change.Kind == SceneChangeKind.Structure ||
            change.Kind == SceneChangeKind.Visibility ||
            change.Kind == SceneChangeKind.Geometry ||
            (change.Source is ParticleSystem3D && change.Kind == SceneChangeKind.Transform))
        {
            _sceneDirty = true;
            _version++;
        }
    }


    public bool RequiresScenePlan(SceneRenderFrameContext3D frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var scene = frame.Scene;
        return _sceneDirty ||
               _lastPlannedParticleVersion != scene.ParticleContentVersion ||
               _lastPlannedRegistryVersion != scene.Registry.Version ||
               (_hasCameraDependentParticleBatches && _lastPlannedCameraVersion != scene.CameraVersion);
    }

    public List<WebGlRetainedBatchPacket> BuildAndUpload(int hostId, SceneRenderPlan3D plan, RenderStats stats)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (!plan.IncludesParticles)
        {
            stats.DrawCallCount += _drawRefs.Count;
            stats.EstimatedDrawCallCount += _drawRefs.Count;
            stats.InstancedBatchCount += _drawRefs.Count;
            return _drawRefs;
        }

        var previousDrawCount = _drawRefs.Count;
        var previousDrawHash = ComputeDrawHash(_drawRefs);
        _drawRefs.Clear();
        _liveBatchIds.Clear();
        _deadScratch.Clear();

        var hasCameraDependentParticleBatches = false;
        for (var commandIndex = 0; commandIndex < plan.DrawCommands.Count; commandIndex++)
        {
            var command = plan.DrawCommands[commandIndex];
            if (command.Kind != SceneRenderCommandKind3D.ParticleSystem || command.Particle is not { } item)
            {
                continue;
            }

            var batchId = item.RetainedBatchId;
            var batch = GetOrCreateBatch(batchId);
            hasCameraDependentParticleBatches |= item.CameraDependentOrder;
            batch.BuildAndUpload(hostId, batchId, item, plan.Frame.Scene.Camera.Position, stats);
            _liveBatchIds.Add(batchId);
            _drawRefs.Add(new WebGlRetainedBatchPacket
            {
                Id = batchId,
                Transparent = command.Transparent,
                SortDistanceSquared = command.SortDistanceSquared,
                DrawOrder = command.SourceOrder
            });
        }

        foreach (var (id, _) in _batches)
        {
            if (!_liveBatchIds.Contains(id))
            {
                _deadScratch.Add(id);
            }
        }

        for (var i = 0; i < _deadScratch.Count; i++)
        {
            WebGlInterop.DestroyRetainedBatch(hostId, _deadScratch[i]);
            _batches.Remove(_deadScratch[i]);
            _version++;
        }

        var newDrawHash = ComputeDrawHash(_drawRefs);
        if (previousDrawCount != _drawRefs.Count || previousDrawHash != newDrawHash)
        {
            _version++;
        }

        _hasCameraDependentParticleBatches = hasCameraDependentParticleBatches;
        _sceneDirty = false;
        _lastPlannedParticleVersion = plan.Frame.Scene.ParticleContentVersion;
        _lastPlannedRegistryVersion = plan.Frame.Scene.Registry.Version;
        _lastPlannedCameraVersion = plan.Frame.Scene.CameraVersion;

        stats.DrawCallCount += _drawRefs.Count;
        stats.EstimatedDrawCallCount += _drawRefs.Count;
        stats.InstancedBatchCount += _drawRefs.Count;
        return _drawRefs;
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
        _liveBatchIds.Clear();
        _drawRefs.Clear();
        _deadScratch.Clear();
        _hasCameraDependentParticleBatches = false;
        _sceneDirty = true;
        _lastPlannedParticleVersion = -1;
        _lastPlannedRegistryVersion = -1;
        _lastPlannedCameraVersion = -1;
        _version++;
    }

    private ParticleBatchState GetOrCreateBatch(string id)
    {
        if (!_batches.TryGetValue(id, out var state))
        {
            state = new ParticleBatchState();
            _batches[id] = state;
            _version++;
        }

        return state;
    }

    private sealed class ParticleBatchState
    {
        private float[] _particles = Array.Empty<float>();
        private byte[] _particleBytes = Array.Empty<byte>();
        private int[] _particleSortOrder = Array.Empty<int>();
        private float[] _particleSortKeys = Array.Empty<float>();
        private int _capacity;
        private string? _lastMeshKey;
        private string? _lastMaterialKey;

        public void BuildAndUpload(int hostId, string batchId, ParticleRenderItem3D item, Vector3 cameraPosition, RenderStats stats)
        {
            var count = item.System.AliveCount;
            var cubeMode = !item.Billboard;
            var stride = ParticleInstanceStream3D.GetFloatStride(item.Billboard);
            EnsureCapacity(count, stride);

            int[]? order = null;
            if (ParticleInstanceStream3D.ShouldSortBackToFront(item))
            {
                ParticleInstanceStream3D.EnsureSortScratch(ref _particleSortOrder, ref _particleSortKeys, count);
                ParticleInstanceStream3D.BuildBackToFrontOrder(item, cameraPosition, _particleSortOrder, _particleSortKeys);
                order = _particleSortOrder;
            }

            ParticleInstanceStream3D.WriteInstances(item, cameraPosition, _particles, order);

            var mesh = item.Mesh;
            var material = item.Material;
            WebGlInterop.UploadRetainedParticleBatchBytes(
                hostId,
                batchId,
                mesh.ResourceKey,
                ToLightingUniform(material.Lighting),
                cubeMode,
                count,
                count * stride,
                item.Transparent,
                CopyParticlesToBytes(count, stride));
            stats.InstanceBufferUploads++;
            stats.InstanceUploadBytes += count * stride * sizeof(float);

            if (!string.Equals(_lastMeshKey, mesh.ResourceKey, StringComparison.Ordinal) ||
                !string.Equals(_lastMaterialKey, material.Key, StringComparison.Ordinal))
            {
                WebGlInterop.UploadRetainedBatchMaterial(
                    hostId,
                    batchId,
                    material.NormalMapStrength,
                    material.HasBaseColorTexture ? material.BaseColorTextureKey ?? string.Empty : string.Empty,
                    material.HasNormalMap ? material.NormalMapTextureKey ?? string.Empty : string.Empty,
                    material.HasMetallicRoughnessTexture ? material.MetallicRoughnessTextureKey ?? string.Empty : string.Empty,
                    material.HasEmissiveTexture ? material.EmissiveTextureKey ?? string.Empty : string.Empty,
                    material.Metallic,
                    material.Roughness,
                    0f,
                    true,
                    material.EmissiveColor.R,
                    material.EmissiveColor.G,
                    material.EmissiveColor.B,
                    material.EmissiveColor.A);
                _lastMaterialKey = material.Key;
            }

            _lastMeshKey = mesh.ResourceKey;
        }

        private void EnsureCapacity(int count, int stride)
        {
            if (_capacity >= count && _particles.Length >= count * stride) return;
            var capacity = Math.Max(count, Math.Max(16, _capacity * 2));
            Array.Resize(ref _particles, capacity * MaxParticleStride);
            _capacity = capacity;
        }

        private byte[] CopyParticlesToBytes(int count, int stride)
        {
            var byteCount = Math.Max(0, count) * stride * sizeof(float);
            if (byteCount == 0) return Array.Empty<byte>();
            // Keep the interop payload exactly sized. Passing a capacity-sized byte[] through
            // WASM interop copies the whole array even when JS later uploads only a prefix.
            // Reuse the exact-size payload while the alive count is stable.
            if (_particleBytes.Length != byteCount)
            {
                _particleBytes = new byte[byteCount];
            }

            Buffer.BlockCopy(_particles, 0, _particleBytes, 0, byteCount);
            return _particleBytes;
        }


        private static float ToLightingUniform(LightingMode mode)
            => mode == LightingMode.Unlit ? 0f : mode == LightingMode.Lambert ? 1f : mode == LightingMode.Phong ? 2f : 3f;
    }
}
