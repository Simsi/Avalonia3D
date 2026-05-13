using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Reusable render-planning workspace. Browser/WASM renderers keep one instance per presenter
/// so ordinary planning, draw-command construction and resource discovery stop allocating fresh
/// Lists/Dictionaries on every dirty frame.
/// </summary>
public sealed class SceneRenderPlanScratch3D
{
    internal readonly List<OrdinaryRenderItem3D> OrdinaryItemScratch = new(256);
    internal readonly Dictionary<string, OrdinaryRenderBatch3D> OrdinaryBatchScratch = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, TransparentOrdinaryBatch3D> TransparentBatchScratch = new(StringComparer.Ordinal);

    internal readonly List<OrdinaryRenderBatch3D> OrdinaryBatches = new(128);
    internal readonly List<TransparentOrdinaryRenderItem3D> TransparentOrdinaryItems = new(64);
    internal readonly List<TransparentOrdinaryBatch3D> TransparentOrdinaryBatches = new(32);
    internal readonly List<ParticleRenderItem3D> ParticleItems = new(32);
    internal readonly List<ThreeDEngine.Core.HighScale.HighScaleInstanceLayer3D> HighScaleLayers = new(8);
    internal readonly List<SceneRenderCommand3D> DrawCommands = new(256);
    internal readonly List<SceneRenderCommand3D> ShadowCommands = new(128);

    private readonly List<OrdinaryRenderBatch3D> _ordinaryBatchPool = new(128);
    private readonly List<TransparentOrdinaryBatch3D> _transparentBatchPool = new(32);
    private readonly Dictionary<OrdinaryRetainedBatchKey, string> _ordinaryRetainedBatchIds = new();
    private readonly Dictionary<LogicalMeshBatchKey, string> _logicalMeshBatchKeys = new();

    private int _ordinaryBatchPoolCursor;
    private int _transparentBatchPoolCursor;

    internal RenderResourcePlan3D Resources { get; } = new();
    internal SceneRenderPlan3D Plan { get; } = new();

    internal void BeginFrame()
    {
        OrdinaryItemScratch.Clear();
        OrdinaryBatchScratch.Clear();
        TransparentBatchScratch.Clear();
        OrdinaryBatches.Clear();
        TransparentOrdinaryItems.Clear();
        TransparentOrdinaryBatches.Clear();
        ParticleItems.Clear();
        HighScaleLayers.Clear();
        DrawCommands.Clear();
        ShadowCommands.Clear();
        Resources.Reset(includesOrdinary: false, includesParticles: false, includesHighScale: false);
        _ordinaryBatchPoolCursor = 0;
        _transparentBatchPoolCursor = 0;
    }

    internal OrdinaryRenderBatch3D RentOrdinaryBatch()
    {
        var index = _ordinaryBatchPoolCursor++;
        while (_ordinaryBatchPool.Count <= index)
        {
            _ordinaryBatchPool.Add(new OrdinaryRenderBatch3D());
        }

        return _ordinaryBatchPool[index];
    }

    internal TransparentOrdinaryBatch3D RentTransparentBatch()
    {
        var index = _transparentBatchPoolCursor++;
        while (_transparentBatchPool.Count <= index)
        {
            _transparentBatchPool.Add(new TransparentOrdinaryBatch3D());
        }

        return _transparentBatchPool[index];
    }

    internal string GetLogicalMeshBatchKey(string meshResourceKey, string? gpuSkinOwnerId)
    {
        if (string.IsNullOrEmpty(gpuSkinOwnerId)) return meshResourceKey;
        var key = new LogicalMeshBatchKey(meshResourceKey, gpuSkinOwnerId);
        if (_logicalMeshBatchKeys.TryGetValue(key, out var value)) return value;
        value = RenderId3D.BuildLogicalMeshBatchKey(meshResourceKey, gpuSkinOwnerId);
        _logicalMeshBatchKeys.Add(key, value);
        return value;
    }

    internal string GetOrdinaryRetainedBatchId(string meshResourceKey, ulong materialBatchHash, string? gpuSkinOwnerId)
    {
        var key = new OrdinaryRetainedBatchKey(meshResourceKey, materialBatchHash, gpuSkinOwnerId);
        if (_ordinaryRetainedBatchIds.TryGetValue(key, out var value)) return value;
        value = RenderId3D.BuildOrdinaryRetainedBatchId(meshResourceKey, materialBatchHash, gpuSkinOwnerId);
        _ordinaryRetainedBatchIds.Add(key, value);
        return value;
    }

    private readonly struct LogicalMeshBatchKey : IEquatable<LogicalMeshBatchKey>
    {
        private readonly string _mesh;
        private readonly string _skin;

        public LogicalMeshBatchKey(string mesh, string skin)
        {
            _mesh = mesh ?? string.Empty;
            _skin = skin ?? string.Empty;
        }

        public bool Equals(LogicalMeshBatchKey other)
            => string.Equals(_mesh, other._mesh, StringComparison.Ordinal) &&
               string.Equals(_skin, other._skin, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is LogicalMeshBatchKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(_mesh), StringComparer.Ordinal.GetHashCode(_skin));
    }

    private readonly struct OrdinaryRetainedBatchKey : IEquatable<OrdinaryRetainedBatchKey>
    {
        private readonly string _mesh;
        private readonly ulong _material;
        private readonly string _skin;

        public OrdinaryRetainedBatchKey(string mesh, ulong material, string? skin)
        {
            _mesh = mesh ?? string.Empty;
            _material = material;
            _skin = skin ?? string.Empty;
        }

        public bool Equals(OrdinaryRetainedBatchKey other)
            => _material == other._material &&
               string.Equals(_mesh, other._mesh, StringComparison.Ordinal) &&
               string.Equals(_skin, other._skin, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is OrdinaryRetainedBatchKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(_mesh), _material, StringComparer.Ordinal.GetHashCode(_skin));
    }
}
