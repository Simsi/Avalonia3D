using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.HighScale;

/// <summary>
/// Runtime layer for very large repeated-object scenes. Mutations are versioned and coalesced;
/// renderers consume dense records and dirty queues rather than Object3D children.
/// </summary>
public class HighScaleInstanceLayer3D : Object3D
{
    private int _materialResolverVersion;
    private int _deferredChangeDepth;
    private bool _pendingStateChanged;
    private bool _pendingStructuralChanged;
    private Func<CompositePartTemplate3D, InstanceRecord3D, ColorRgba>? _colorResolver;

    public HighScaleInstanceLayer3D(CompositeTemplate3D template, int initialCapacity = 1024, float chunkCellSize = 24f)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Instances = new InstanceStore3D(initialCapacity);
        Chunks = new HighScaleChunkIndex3D(chunkCellSize);
        LodPolicy = new HighScaleLodPolicy3D();
        StateBuffer = new InstanceStateBuffer3D(initialCapacity);
        Name = template.Name + " Instances";
        IsPickable = false;
        IsManipulationEnabled = false;
        Template.MaterialVariantsChanged += OnTemplateMaterialVariantsChanged;
    }

    public event EventHandler? StateChanged;

    public CompositeTemplate3D Template { get; }
    public InstanceStore3D Instances { get; }
    public HighScaleChunkIndex3D Chunks { get; }
    public HighScaleLodPolicy3D LodPolicy { get; }
    public InstanceStateBuffer3D StateBuffer { get; }
    public int MaterialResolverVersion => _materialResolverVersion;

    public IReadOnlyList<HighScaleChunk3D> QueryVisibleChunks(Matrix4x4 viewProjection)
        => Chunks.QueryVisible(viewProjection, Instances, Template.LocalBounds);

    /// <summary>
    /// Optional deterministic, allocation-free state-to-color hook. Replacing the resolver
    /// invalidates only material/state buffers.
    /// </summary>
    public Func<CompositePartTemplate3D, InstanceRecord3D, ColorRgba>? ColorResolver
    {
        get => _colorResolver;
        set
        {
            using var mutation = EnterOwnedMutationScope();
            if (ReferenceEquals(_colorResolver, value)) return;
            _colorResolver = value;
            MarkMaterialsDirty();
        }
    }

    public override bool UseMeshRendering => false;
    public override bool UseScenePicking => false;

    public int AddInstance(Matrix4x4 transform, int materialVariantId = 0, int dataId = -1,
        InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        using var mutation = EnterOwnedMutationScope();
        ValidateMaterialVariant(materialVariantId);
        var index = Instances.Add(Template.Id, transform, materialVariantId, dataId, flags);
        StateBuffer.SetMaterialVariant(index, materialVariantId);
        StateBuffer.SetFlags(index, (byte)Instances[index].Flags);
        Chunks.AddInstance(index, transform, Template.LocalBounds);
        RaiseStructuralChanged();
        return index;
    }

    public void AddInstances(IEnumerable<Matrix4x4> transforms, int materialVariantId = 0,
        InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        using var mutation = EnterOwnedMutationScope();
        if (transforms is null) throw new ArgumentNullException(nameof(transforms));
        ValidateMaterialVariant(materialVariantId);
        using var scope = new DeferredChangeScope(this);
        foreach (var transform in transforms) AddInstance(transform, materialVariantId, -1, flags);
    }

    public void SetInstanceTransform(int index, Matrix4x4 transform)
    {
        using var mutation = EnterOwnedMutationScope();
        Instances.SetTransform(index, transform);
        var changedChunkMembership = Chunks.UpdateInstance(index, transform, Template.LocalBounds);
        if (Chunks.RebuildRequested) Chunks.Rebuild(Instances, Template.LocalBounds);
        if (changedChunkMembership) RaiseStructuralChanged();
        else RaiseStateChanged();
    }

    public void SetInstanceMaterialVariant(int index, int materialVariantId)
    {
        using var mutation = EnterOwnedMutationScope();
        ValidateMaterialVariant(materialVariantId);
        var previous = Instances[index].MaterialVariantId;
        if (previous == materialVariantId) return;
        Instances.SetMaterialVariant(index, materialVariantId);
        StateBuffer.SetMaterialVariant(index, materialVariantId);
        RaiseStateChanged();
    }

    public void SetInstanceVisible(int index, bool visible)
    {
        using var mutation = EnterOwnedMutationScope();
        var previous = (Instances[index].Flags & InstanceFlags3D.Visible) != 0;
        if (previous == visible) return;
        Instances.SetVisible(index, visible);
        StateBuffer.SetFlags(index, (byte)Instances[index].Flags);
        RaiseStateChanged();
    }

    public void MarkMaterialsDirty()
    {
        using var mutation = EnterOwnedMutationScope();
        unchecked { _materialResolverVersion++; }
        Instances.MarkAllMaterialsDirty();
        StateBuffer.MarkAllDirty(Instances.Count);
        RaiseStateChanged();
    }

    public HighScaleTelemetryBatch BeginTelemetryBatch() => new(this, EnterOwnedMutationScope());

    public ColorRgba ResolveColor(CompositePartTemplate3D part, InstanceRecord3D record)
        => ColorResolver is not null ? ColorResolver(part, record) : Template.ResolveColor(part, record.MaterialVariantId);

    protected override Mesh3D BuildMesh() => Mesh3D.Empty;

    internal void NotifyChanged() => RaiseStructuralChanged();

    internal void BeginDeferredChanges()
    {
        checked { _deferredChangeDepth++; }
    }

    internal void EndDeferredChanges()
    {
        if (_deferredChangeDepth <= 0) throw new InvalidOperationException("Deferred high-scale change scope is unbalanced.");
        _deferredChangeDepth--;
        if (_deferredChangeDepth == 0) FlushDeferredChanges();
    }

    internal void FlushDeferredChanges()
    {
        if (_deferredChangeDepth != 0) return;
        var structural = _pendingStructuralChanged;
        var state = _pendingStateChanged;
        _pendingStructuralChanged = false;
        _pendingStateChanged = false;
        if (state) StateChanged?.Invoke(this, EventArgs.Empty);
        if (structural || state) base.RaiseChanged(SceneChangeKind.HighScaleState);
    }

    private void ValidateMaterialVariant(int materialVariantId)
    {
        if (!Template.MaterialVariants.ContainsKey(materialVariantId))
            throw new ArgumentOutOfRangeException(nameof(materialVariantId), materialVariantId, "Material variant is not registered by the composite template.");
    }

    private void OnTemplateMaterialVariantsChanged(object? sender, EventArgs e) => MarkMaterialsDirty();

    private void RaiseStructuralChanged()
    {
        if (_deferredChangeDepth > 0)
        {
            _pendingStructuralChanged = true;
            return;
        }
        base.RaiseChanged(SceneChangeKind.HighScaleState);
    }

    private void RaiseStateChanged()
    {
        if (_deferredChangeDepth > 0)
        {
            _pendingStateChanged = true;
            return;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        base.RaiseChanged(SceneChangeKind.HighScaleState);
    }

    private sealed class DeferredChangeScope : IDisposable
    {
        private HighScaleInstanceLayer3D? _layer;
        private SceneAccessLease3D _mutation;

        public DeferredChangeScope(HighScaleInstanceLayer3D layer)
        {
            _layer = layer;
            _mutation = layer.EnterOwnedMutationScope();
            layer.BeginDeferredChanges();
        }

        public void Dispose()
        {
            var layer = _layer;
            if (layer is null) return;
            _layer = null;
            try { layer.EndDeferredChanges(); }
            finally { _mutation.Dispose(); }
        }
    }
}

public sealed class HighScaleTelemetryBatch : IDisposable
{
    private HighScaleInstanceLayer3D? _layer;
    private SceneAccessLease3D _mutation;

    internal HighScaleTelemetryBatch(HighScaleInstanceLayer3D layer, SceneAccessLease3D mutation)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _mutation = mutation;
        layer.BeginDeferredChanges();
    }

    public void SetMaterialVariant(int index, int materialVariantId) => GetLayer().SetInstanceMaterialVariant(index, materialVariantId);
    public void SetVisible(int index, bool visible) => GetLayer().SetInstanceVisible(index, visible);
    public void SetTransform(int index, Matrix4x4 transform) => GetLayer().SetInstanceTransform(index, transform);

    public void Dispose()
    {
        var layer = _layer;
        if (layer is null) return;
        _layer = null;
        try { layer.EndDeferredChanges(); }
        finally { _mutation.Dispose(); }
    }

    private HighScaleInstanceLayer3D GetLayer()
        => _layer ?? throw new ObjectDisposedException(nameof(HighScaleTelemetryBatch));
}
