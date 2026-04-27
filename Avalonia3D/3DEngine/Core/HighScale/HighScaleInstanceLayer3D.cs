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
/// </summary>
public sealed class HighScaleInstanceLayer3D : Object3D
{
    private int _materialResolverVersion;

    public HighScaleInstanceLayer3D(CompositeTemplate3D template, int initialCapacity = 1024, float chunkCellSize = 24f)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Instances = new InstanceStore3D(initialCapacity);
        Chunks = new HighScaleChunkIndex3D(chunkCellSize);
        LodPolicy = new HighScaleLodPolicy3D();
        Name = template.Name + " Instances";
        IsPickable = false;
        IsManipulationEnabled = false;
    }

    public CompositeTemplate3D Template { get; }
    public InstanceStore3D Instances { get; }
    public HighScaleChunkIndex3D Chunks { get; }
    public HighScaleLodPolicy3D LodPolicy { get; }
    public int MaterialResolverVersion => _materialResolverVersion;

    /// <summary>
    /// Optional fast state-to-color hook. It is called only while rebuilding dirty GPU chunks;
    /// avoid allocations inside this delegate. Calling MarkMaterialsDirty forces cached chunks
    /// to be rebuilt.
    /// </summary>
    public Func<CompositePartTemplate3D, InstanceRecord3D, ColorRgba>? ColorResolver { get; set; }

    public override bool UseMeshRendering => false;
    public override bool UseScenePicking => false;

    public int AddInstance(Matrix4x4 transform, int materialVariantId = 0, int dataId = -1, InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        var index = Instances.Add(Template.Id, transform, materialVariantId, dataId, flags);
        Chunks.AddInstance(index, transform, Template.LocalBounds);
        RaiseChanged();
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
        Chunks.UpdateInstance(index, transform, Template.LocalBounds);
        if (Chunks.RebuildRequested) Chunks.Rebuild(Instances, Template.LocalBounds);
        RaiseChanged();
    }

    public void SetInstanceMaterialVariant(int index, int materialVariantId)
    {
        Instances.SetMaterialVariant(index, materialVariantId);
        Chunks.MarkInstanceDirty(index);
        RaiseChanged();
    }

    public void SetInstanceVisible(int index, bool visible)
    {
        Instances.SetVisible(index, visible);
        Chunks.MarkInstanceDirty(index);
        RaiseChanged();
    }

    public void MarkMaterialsDirty()
    {
        _materialResolverVersion++;
        Instances.MarkAllMaterialsDirty();
        foreach (var chunk in Chunks.Chunks)
        {
            chunk.MarkDirty();
        }
        RaiseChanged();
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

    private sealed class DeferredChangeScope : IDisposable
    {
        private readonly HighScaleInstanceLayer3D _layer;
        private bool _disposed;

        public DeferredChangeScope(HighScaleInstanceLayer3D layer)
        {
            _layer = layer;
            _layer.SuppressChanged = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _layer.SuppressChanged = false;
            _layer.NotifyChanged();
        }
    }

    internal bool SuppressChanged { get; set; }

    internal void NotifyChanged() => RaiseChanged();

    protected override void RaiseChanged()
    {
        if (!SuppressChanged)
        {
            base.RaiseChanged();
        }
    }
}

public readonly struct HighScaleTelemetryBatch : IDisposable
{
    private readonly HighScaleInstanceLayer3D _layer;

    internal HighScaleTelemetryBatch(HighScaleInstanceLayer3D layer)
    {
        _layer = layer;
        _layer.SuppressChanged = true;
    }

    public void SetMaterialVariant(int index, int materialVariantId) => _layer.SetInstanceMaterialVariant(index, materialVariantId);
    public void SetVisible(int index, bool visible) => _layer.SetInstanceVisible(index, visible);
    public void SetTransform(int index, Matrix4x4 transform) => _layer.SetInstanceTransform(index, transform);

    public void Dispose()
    {
        _layer.SuppressChanged = false;
        _layer.NotifyChanged();
    }
}
