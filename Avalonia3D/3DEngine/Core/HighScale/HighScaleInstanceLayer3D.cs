using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.HighScale;

/// <summary>
/// Runtime layer for very large repeated-object scenes. It keeps repeated logical objects
/// in a dense InstanceStore and a spatial chunk index instead of expanding them into
/// thousands of Object3D parts.
///
/// Data-flow rule: transform/structure changes may dirty chunks; status/material/visibility
/// telemetry changes update StateBuffer only and are surfaced through StateChanged.
/// Renderers should update a small GPU state buffer rather than rebuilding transform batches.
/// </summary>
public sealed class HighScaleInstanceLayer3D : Object3D
{
    private int _materialResolverVersion;
    private bool _suppressStateChanged;
    private bool _pendingStateChanged;
    private bool _pendingStructuralChanged;

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
    }

    public event EventHandler? StateChanged;

    public CompositeTemplate3D Template { get; }
    public InstanceStore3D Instances { get; }
    public HighScaleChunkIndex3D Chunks { get; }
    public HighScaleLodPolicy3D LodPolicy { get; }
    public InstanceStateBuffer3D StateBuffer { get; }
    public int MaterialResolverVersion => _materialResolverVersion;

    /// <summary>
    /// Optional state-to-color hook. The retained renderer evaluates it when updating the
    /// per-instance state-color buffer. It should be deterministic and allocation-free.
    /// Calling MarkMaterialsDirty forces state buffers to be rewritten but does not dirty
    /// transform/chunk buffers.
    /// </summary>
    public Func<CompositePartTemplate3D, InstanceRecord3D, ColorRgba>? ColorResolver { get; set; }

    public override bool UseMeshRendering => false;
    public override bool UseScenePicking => false;

    public int AddInstance(Matrix4x4 transform, int materialVariantId = 0, int dataId = -1, InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        var index = Instances.Add(Template.Id, transform, materialVariantId, dataId, flags);
        StateBuffer.SetMaterialVariant(index, materialVariantId);
        StateBuffer.SetFlags(index, (byte)Instances[index].Flags);
        Chunks.AddInstance(index, transform, Template.LocalBounds);
        RaiseStructuralChanged();
        return index;
    }

    public void AddInstances(IEnumerable<Matrix4x4> transforms, int materialVariantId = 0, InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        using (new DeferredChangeScope(this))
        {
            foreach (var transform in transforms)
            {
                AddInstance(transform, materialVariantId, -1, flags);
            }
        }
    }

    public void SetInstanceTransform(int index, Matrix4x4 transform)
    {
        Instances.SetTransform(index, transform);
        var changedChunkMembership = Chunks.UpdateInstance(index, transform, Template.LocalBounds);
        if (Chunks.RebuildRequested) Chunks.Rebuild(Instances, Template.LocalBounds);

        // A transform update that stays inside the same chunk is high-scale runtime data,
        // not scene structure. Raising the generic Object3D.Changed path invalidates the
        // registry and forces browser-side framegen churn under animation. Only crossing
        // chunk boundaries is structural.
        if (changedChunkMembership) RaiseStructuralChanged();
        else RaiseStateChanged();
    }

    public void SetInstanceMaterialVariant(int index, int materialVariantId)
    {
        Instances.SetMaterialVariant(index, materialVariantId);
        if (StateBuffer.SetMaterialVariant(index, materialVariantId))
        {
            RaiseStateChanged();
        }
    }

    public void SetInstanceVisible(int index, bool visible)
    {
        Instances.SetVisible(index, visible);
        var stateFlags = (byte)Instances[index].Flags;
        if (StateBuffer.SetFlags(index, stateFlags))
        {
            RaiseStateChanged();
        }
    }

    public void MarkMaterialsDirty()
    {
        _materialResolverVersion++;
        Instances.MarkAllMaterialsDirty();
        StateBuffer.MarkAllDirty(Instances.Count);
        RaiseStateChanged();
    }

    public HighScaleTelemetryBatch BeginTelemetryBatch() => new(this);

    public ColorRgba ResolveColor(CompositePartTemplate3D part, InstanceRecord3D record)
    {
        if (ColorResolver is not null)
        {
            return ColorResolver(part, record);
        }

        return Template.ResolveColor(part, record.MaterialVariantId);
    }

    protected override Mesh3D BuildMesh() => Mesh3D.Empty;

    internal bool SuppressChanged { get; set; }

    internal void NotifyChanged() => RaiseStructuralChanged();

    internal void BeginDeferredChanges()
    {
        SuppressChanged = true;
        _suppressStateChanged = true;
    }

    internal void EndDeferredChanges()
    {
        SuppressChanged = false;
        _suppressStateChanged = false;
        FlushDeferredChanges();
    }

    private void RaiseStructuralChanged()
    {
        if (SuppressChanged)
        {
            _pendingStructuralChanged = true;
            return;
        }

        base.RaiseChanged(SceneChangeKind.HighScaleStructure);
    }

    private void RaiseStateChanged()
    {
        if (_suppressStateChanged)
        {
            _pendingStateChanged = true;
            return;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void FlushDeferredChanges()
    {
        var structural = _pendingStructuralChanged;
        var state = _pendingStateChanged;
        _pendingStructuralChanged = false;
        _pendingStateChanged = false;

        if (structural)
        {
            base.RaiseChanged(SceneChangeKind.HighScaleStructure);
        }

        if (state)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class DeferredChangeScope : IDisposable
    {
        private readonly HighScaleInstanceLayer3D _layer;
        private bool _disposed;

        public DeferredChangeScope(HighScaleInstanceLayer3D layer)
        {
            _layer = layer;
            _layer.BeginDeferredChanges();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _layer.EndDeferredChanges();
        }
    }
}

public readonly struct HighScaleTelemetryBatch : IDisposable
{
    private readonly HighScaleInstanceLayer3D _layer;

    internal HighScaleTelemetryBatch(HighScaleInstanceLayer3D layer)
    {
        _layer = layer;
        _layer.BeginDeferredChanges();
    }

    public void SetMaterialVariant(int index, int materialVariantId) => _layer.SetInstanceMaterialVariant(index, materialVariantId);
    public void SetVisible(int index, bool visible) => _layer.SetInstanceVisible(index, visible);
    public void SetTransform(int index, Matrix4x4 transform) => _layer.SetInstanceTransform(index, transform);

    public void Dispose()
    {
        _layer.EndDeferredChanges();
    }
}
