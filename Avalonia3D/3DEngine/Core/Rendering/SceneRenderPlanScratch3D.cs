using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Reusable render-planning workspace. Browser/WASM renderers keep one instance per presenter
/// so ordinary planning, draw-command construction and resource discovery stop allocating fresh
/// Lists/Dictionaries on every dirty frame.
/// </summary>
internal sealed class SceneRenderPlanScratch3D
{
    private const int StableIdCacheLimit = 131_072;
    internal readonly List<OrdinaryRenderItem3D> OrdinaryItemScratch = new(256);
    internal readonly Dictionary<string, OrdinaryRenderBatch3D> OrdinaryBatchScratch = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, TransparentOrdinaryBatch3D> TransparentBatchScratch = new(StringComparer.Ordinal);

    internal readonly List<OrdinaryRenderBatch3D> OrdinaryBatches = new(128);
    internal readonly List<TransparentOrdinaryRenderItem3D> TransparentOrdinaryItems = new(64);
    internal readonly List<TransparentOrdinaryBatch3D> TransparentOrdinaryBatches = new(32);
    internal readonly List<ParticleRenderItem3D> ParticleItems = new(32);
    internal readonly List<ThreeDEngine.Core.HighScale.HighScaleInstanceLayer3D> HighScaleLayers = new(8);
    internal readonly List<SceneRenderCommand3D> DrawCommands = new(256);

    private readonly List<OrdinaryRenderBatch3D> _ordinaryBatchPool = new(128);
    private readonly List<TransparentOrdinaryBatch3D> _transparentBatchPool = new(32);
    private readonly Dictionary<OrdinaryRetainedBatchKey, string> _ordinaryRetainedBatchIds = new();
    private readonly Dictionary<LogicalMeshBatchKey, string> _logicalMeshBatchKeys = new();
    private readonly Dictionary<TransparentDrawKey, string> _transparentDrawIds = new();
    private readonly Dictionary<TransparentDepthBatchKey, string> _transparentDepthBatchIds = new();
    private readonly Dictionary<ParticleBatchKey, string> _particleBatchIds = new();
    private readonly List<SceneRenderCommand3D> _commandPool = new(256);

    private int _ordinaryBatchPoolCursor;
    private int _transparentBatchPoolCursor;
    private int _commandPoolCursor;

    internal RenderResourcePlan3D Resources { get; } = new();
    internal SceneRenderPlan3D Plan { get; } = new();

    internal void BeginFrame(ThreeDEngine.Core.Scene.SceneFrameSnapshot3D snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        TrimStableIdCachesIfNeeded();
        EnsureCapacity(snapshot);
        OrdinaryItemScratch.Clear();
        OrdinaryBatchScratch.Clear();
        TransparentBatchScratch.Clear();
        OrdinaryBatches.Clear();
        TransparentOrdinaryItems.Clear();
        TransparentOrdinaryBatches.Clear();
        ParticleItems.Clear();
        HighScaleLayers.Clear();
        DrawCommands.Clear();
        Resources.Reset(includesOrdinary: false, includesParticles: false, includesHighScale: false);
        _ordinaryBatchPoolCursor = 0;
        _transparentBatchPoolCursor = 0;
        _commandPoolCursor = 0;
    }


    private void EnsureCapacity(ThreeDEngine.Core.Scene.SceneFrameSnapshot3D snapshot)
    {
        var renderables = snapshot.RenderablesInternal.Length;
        var particles = snapshot.ParticleSystemsInternal.Length;
        var highScale = snapshot.HighScaleLayersInternal.Length;
        OrdinaryItemScratch.EnsureCapacity(renderables);
        OrdinaryBatches.EnsureCapacity(renderables);
        TransparentOrdinaryItems.EnsureCapacity(renderables);
        TransparentOrdinaryBatches.EnsureCapacity(renderables);
        ParticleItems.EnsureCapacity(particles);
        HighScaleLayers.EnsureCapacity(highScale);
        DrawCommands.EnsureCapacity(checked(renderables + particles + highScale));
    }

    private void TrimStableIdCachesIfNeeded()
    {
        var total = _ordinaryRetainedBatchIds.Count +
                    _logicalMeshBatchKeys.Count +
                    _transparentDrawIds.Count +
                    _transparentDepthBatchIds.Count +
                    _particleBatchIds.Count;
        if (total <= StableIdCacheLimit) return;

        // IDs are value-stable strings. Clearing only the interning dictionaries cannot invalidate
        // retained backend keys; it merely bounds memory after extreme scene churn.
        _ordinaryRetainedBatchIds.Clear();
        _logicalMeshBatchKeys.Clear();
        _transparentDrawIds.Clear();
        _transparentDepthBatchIds.Clear();
        _particleBatchIds.Clear();
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

    internal string GetTransparentDrawId(string retainedBatchId, string ownerId)
    {
        var key = new TransparentDrawKey(retainedBatchId, ownerId);
        if (_transparentDrawIds.TryGetValue(key, out var value)) return value;
        value = RenderId3D.BuildTransparentDrawId(retainedBatchId, ownerId);
        _transparentDrawIds.Add(key, value);
        return value;
    }

    internal string GetTransparentDepthBatchId(string retainedBatchId, int depthBin)
    {
        var key = new TransparentDepthBatchKey(retainedBatchId, depthBin);
        if (_transparentDepthBatchIds.TryGetValue(key, out var value)) return value;
        value = RenderId3D.BuildTransparentDepthBatchId(retainedBatchId, depthBin);
        _transparentDepthBatchIds.Add(key, value);
        return value;
    }

    internal string GetParticleRetainedBatchId(string particleSystemId, int renderMode)
    {
        var key = new ParticleBatchKey(particleSystemId, renderMode);
        if (_particleBatchIds.TryGetValue(key, out var value)) return value;
        value = RenderId3D.BuildParticleRetainedBatchId(particleSystemId, renderMode);
        _particleBatchIds.Add(key, value);
        return value;
    }

    internal SceneRenderCommand3D RentOrdinaryCommand(OrdinaryRenderBatch3D batch, int sourceOrder)
    {
        var command = RentCommand();
        command.Reset(SceneRenderCommandKind3D.OrdinaryBatch, batch.BatchId, false, batch.SortDistanceSquared, sourceOrder, ordinaryBatch: batch);
        return command;
    }

    internal SceneRenderCommand3D RentHighScaleCommand(ThreeDEngine.Core.HighScale.HighScaleInstanceLayer3D layer, int sourceOrder)
    {
        var command = RentCommand();
        command.Reset(SceneRenderCommandKind3D.HighScaleLayer, layer.Id, false, 0f, sourceOrder, highScaleLayer: layer);
        return command;
    }

    internal SceneRenderCommand3D RentParticleCommand(ParticleRenderItem3D item, int sourceOrder)
    {
        var command = RentCommand();
        command.Reset(SceneRenderCommandKind3D.ParticleSystem, item.RetainedBatchId, item.Transparent, item.SortDistanceSquared, sourceOrder, particle: item);
        return command;
    }

    internal SceneRenderCommand3D RentTransparentBatchCommand(TransparentOrdinaryBatch3D batch)
    {
        var command = RentCommand();
        command.Reset(SceneRenderCommandKind3D.TransparentOrdinaryBatch, batch.BatchId, true, batch.SortDistanceSquared, batch.SourceOrder, transparentOrdinaryBatch: batch);
        return command;
    }

    internal SceneRenderCommand3D RentTransparentCommand(TransparentOrdinaryRenderItem3D item)
    {
        var command = RentCommand();
        command.Reset(SceneRenderCommandKind3D.TransparentOrdinaryItem, item.DrawId, true, item.SortDistanceSquared, item.SourceOrder, transparentOrdinary: item);
        return command;
    }

    private SceneRenderCommand3D RentCommand()
    {
        var index = _commandPoolCursor++;
        if (_commandPool.Count <= index)
        {
            _commandPool.Add(new SceneRenderCommand3D());
        }
        return _commandPool[index];
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

    private readonly struct TransparentDrawKey : IEquatable<TransparentDrawKey>
    {
        private readonly string _batch;
        private readonly string _owner;

        public TransparentDrawKey(string batch, string owner)
        {
            _batch = batch;
            _owner = owner;
        }

        public bool Equals(TransparentDrawKey other)
            => string.Equals(_batch, other._batch, StringComparison.Ordinal) &&
               string.Equals(_owner, other._owner, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is TransparentDrawKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(StringComparer.Ordinal.GetHashCode(_batch), StringComparer.Ordinal.GetHashCode(_owner));
    }

    private readonly struct TransparentDepthBatchKey : IEquatable<TransparentDepthBatchKey>
    {
        private readonly string _batch;
        private readonly int _depthBin;

        public TransparentDepthBatchKey(string batch, int depthBin)
        {
            _batch = batch;
            _depthBin = depthBin;
        }

        public bool Equals(TransparentDepthBatchKey other)
            => _depthBin == other._depthBin && string.Equals(_batch, other._batch, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is TransparentDepthBatchKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(_batch), _depthBin);
    }

    private readonly struct ParticleBatchKey : IEquatable<ParticleBatchKey>
    {
        private readonly string _system;
        private readonly int _renderMode;

        public ParticleBatchKey(string system, int renderMode)
        {
            _system = system;
            _renderMode = renderMode;
        }

        public bool Equals(ParticleBatchKey other)
            => _renderMode == other._renderMode && string.Equals(_system, other._system, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ParticleBatchKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(_system), _renderMode);
    }
}
