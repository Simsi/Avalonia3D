using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeDEngine.Core.HighScale;

/// <summary>
/// Dense instance storage for large scenes. It is intentionally not object-oriented:
/// renderers read contiguous records and dirty queues instead of traversing Object3D trees.
/// </summary>
public sealed class InstanceStore3D
{
    private InstanceRecord3D[] _records;
    private readonly List<int> _dirtyTransforms = new();
    private readonly List<int> _dirtyMaterials = new();
    private readonly List<int> _dirtyVisibility = new();
    private readonly HashSet<int> _dirtyTransformSet = new();
    private readonly HashSet<int> _dirtyMaterialSet = new();
    private readonly HashSet<int> _dirtyVisibilitySet = new();
    private int _version;

    public InstanceStore3D(int initialCapacity = 1024)
    {
        _records = new InstanceRecord3D[System.Math.Max(1, initialCapacity)];
    }

    public int Count { get; private set; }
    public int Version => _version;
    public int TransformVersion { get; private set; }
    public int MaterialVersion { get; private set; }
    public int VisibilityVersion { get; private set; }

    public ReadOnlySpan<InstanceRecord3D> Records => _records.AsSpan(0, Count);

    public int Add(int templateId, Matrix4x4 transform, int materialVariantId = 0, int dataId = -1, InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        EnsureCapacity(Count + 1);
        var index = Count++;
        TransformVersion++;
        MaterialVersion++;
        VisibilityVersion++;
        _version++;
        _records[index] = new InstanceRecord3D
        {
            TemplateId = templateId,
            Transform = transform,
            MaterialVariantId = materialVariantId,
            DataId = dataId,
            Flags = flags | InstanceFlags3D.DirtyTransform | InstanceFlags3D.DirtyMaterial | InstanceFlags3D.DirtyVisibility,
            TransformVersion = TransformVersion,
            MaterialVersion = MaterialVersion
        };
        MarkTransformDirty(index);
        MarkMaterialDirty(index);
        MarkVisibilityDirty(index);
        return index;
    }

    public ref InstanceRecord3D this[int index] => ref _records[index];

    public void SetTransform(int index, Matrix4x4 transform)
    {
        ref var record = ref _records[index];
        record.Transform = transform;
        TransformVersion++;
        _version++;
        record.TransformVersion = TransformVersion;
        record.Flags |= InstanceFlags3D.DirtyTransform;
        MarkTransformDirty(index);
    }

    public void SetMaterialVariant(int index, int materialVariantId)
    {
        ref var record = ref _records[index];
        if (record.MaterialVariantId == materialVariantId)
        {
            return;
        }

        record.MaterialVariantId = materialVariantId;
        MaterialVersion++;
        _version++;
        record.MaterialVersion = MaterialVersion;
        record.Flags |= InstanceFlags3D.DirtyMaterial;
        MarkMaterialDirty(index);
    }

    public void SetVisible(int index, bool visible)
    {
        ref var record = ref _records[index];
        var isVisible = (record.Flags & InstanceFlags3D.Visible) != 0;
        if (isVisible == visible)
        {
            return;
        }

        if (visible)
        {
            record.Flags |= InstanceFlags3D.Visible;
        }
        else
        {
            record.Flags &= ~InstanceFlags3D.Visible;
        }

        VisibilityVersion++;
        _version++;
        record.Flags |= InstanceFlags3D.DirtyVisibility;
        MarkVisibilityDirty(index);
    }

    public void MarkAllMaterialsDirty()
    {
        MaterialVersion++;
        _version++;
        for (var i = 0; i < Count; i++)
        {
            _records[i].Flags |= InstanceFlags3D.DirtyMaterial;
            _records[i].MaterialVersion = MaterialVersion;
            MarkMaterialDirty(i);
        }
    }

    public int DrainDirtyTransforms(Span<int> destination) => Drain(_dirtyTransforms, _dirtyTransformSet, destination, InstanceFlags3D.DirtyTransform);
    public int DrainDirtyMaterials(Span<int> destination) => Drain(_dirtyMaterials, _dirtyMaterialSet, destination, InstanceFlags3D.DirtyMaterial);
    public int DrainDirtyVisibility(Span<int> destination) => Drain(_dirtyVisibility, _dirtyVisibilitySet, destination, InstanceFlags3D.DirtyVisibility);

    public void Clear()
    {
        Count = 0;
        _version++;
        TransformVersion++;
        MaterialVersion++;
        VisibilityVersion++;
        _dirtyTransforms.Clear();
        _dirtyMaterials.Clear();
        _dirtyVisibility.Clear();
        _dirtyTransformSet.Clear();
        _dirtyMaterialSet.Clear();
        _dirtyVisibilitySet.Clear();
    }

    private void MarkTransformDirty(int index)
    {
        if (_dirtyTransformSet.Add(index)) _dirtyTransforms.Add(index);
    }

    private void MarkMaterialDirty(int index)
    {
        if (_dirtyMaterialSet.Add(index)) _dirtyMaterials.Add(index);
    }

    private void MarkVisibilityDirty(int index)
    {
        if (_dirtyVisibilitySet.Add(index)) _dirtyVisibility.Add(index);
    }

    private void EnsureCapacity(int required)
    {
        if (_records.Length >= required)
        {
            return;
        }

        var newCapacity = System.Math.Max(required, _records.Length * 2);
        Array.Resize(ref _records, newCapacity);
    }

    private int Drain(List<int> source, HashSet<int> set, Span<int> destination, InstanceFlags3D clearFlag)
    {
        var count = System.Math.Min(source.Count, destination.Length);
        for (var i = 0; i < count; i++)
        {
            var index = source[i];
            destination[i] = index;
            if ((uint)index < (uint)Count)
            {
                _records[index].Flags &= ~clearFlag;
            }
            set.Remove(index);
        }

        if (count == source.Count)
        {
            source.Clear();
        }
        else
        {
            source.RemoveRange(0, count);
        }

        return count;
    }
}
