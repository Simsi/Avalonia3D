using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.HighScale;

/// <summary>
/// Dense instance storage for large scenes. Public mutation is routed through versioned methods;
/// direct mutable references are intentionally not exposed.
/// </summary>
public sealed class InstanceStore3D
{
    private const InstanceFlags3D PublicFlags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable |
        InstanceFlags3D.Selected | InstanceFlags3D.Hovered;

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
        initialCapacity = Guard3D.Positive(initialCapacity, nameof(initialCapacity));
        _records = new InstanceRecord3D[initialCapacity];
    }

    public int Count { get; private set; }
    public int Capacity => _records.Length;
    public int Version => _version;
    public int TransformVersion { get; private set; }
    public int MaterialVersion { get; private set; }
    public int VisibilityVersion { get; private set; }
    public ReadOnlySpan<InstanceRecord3D> Records => _records.AsSpan(0, Count);

    public InstanceRecord3D this[int index]
    {
        get
        {
            ValidateIndex(index);
            return _records[index];
        }
    }

    public int Add(int templateId, Matrix4x4 transform, int materialVariantId = 0, int dataId = -1,
        InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        Guard3D.NonNegative(templateId, nameof(templateId));
        Guard3D.NonNegative(materialVariantId, nameof(materialVariantId));
        Guard3D.FiniteMatrix(transform, nameof(transform), requireInvertible: true);
        ValidateFlags(flags, nameof(flags));

        EnsureCapacity(Count + 1);
        var index = Count++;
        unchecked
        {
            TransformVersion++;
            MaterialVersion++;
            VisibilityVersion++;
            _version++;
        }

        var dirtyFlags = InstanceFlags3D.DirtyTransform | InstanceFlags3D.DirtyMaterial | InstanceFlags3D.DirtyVisibility;
        _records[index] = new InstanceRecord3D
        {
            TemplateId = templateId,
            Transform = transform,
            MaterialVariantId = materialVariantId,
            DataId = dataId,
            Flags = flags | dirtyFlags,
            TransformVersion = TransformVersion,
            MaterialVersion = MaterialVersion
        };
        MarkTransformDirty(index);
        MarkMaterialDirty(index);
        MarkVisibilityDirty(index);
        return index;
    }

    public void SetTransform(int index, Matrix4x4 transform)
    {
        ValidateIndex(index);
        Guard3D.FiniteMatrix(transform, nameof(transform), requireInvertible: true);
        ref var record = ref _records[index];
        if (record.Transform.Equals(transform)) return;
        record.Transform = transform;
        unchecked { TransformVersion++; _version++; }
        record.TransformVersion = TransformVersion;
        record.Flags |= InstanceFlags3D.DirtyTransform;
        MarkTransformDirty(index);
    }

    public void SetMaterialVariant(int index, int materialVariantId)
    {
        ValidateIndex(index);
        Guard3D.NonNegative(materialVariantId, nameof(materialVariantId));
        ref var record = ref _records[index];
        if (record.MaterialVariantId == materialVariantId) return;
        record.MaterialVariantId = materialVariantId;
        unchecked { MaterialVersion++; _version++; }
        record.MaterialVersion = MaterialVersion;
        record.Flags |= InstanceFlags3D.DirtyMaterial;
        MarkMaterialDirty(index);
    }

    public void SetVisible(int index, bool visible)
    {
        ValidateIndex(index);
        ref var record = ref _records[index];
        var isVisible = (record.Flags & InstanceFlags3D.Visible) != 0;
        if (isVisible == visible) return;
        if (visible) record.Flags |= InstanceFlags3D.Visible;
        else record.Flags &= ~InstanceFlags3D.Visible;
        record.Flags |= InstanceFlags3D.DirtyVisibility;
        unchecked { VisibilityVersion++; _version++; }
        MarkVisibilityDirty(index);
    }

    public void SetFlags(int index, InstanceFlags3D flags)
    {
        ValidateIndex(index);
        ValidateFlags(flags, nameof(flags));
        var persistentFlags = flags & ~(InstanceFlags3D.DirtyTransform | InstanceFlags3D.DirtyMaterial | InstanceFlags3D.DirtyVisibility);
        ref var record = ref _records[index];
        var currentPersistent = record.Flags & ~(InstanceFlags3D.DirtyTransform | InstanceFlags3D.DirtyMaterial | InstanceFlags3D.DirtyVisibility);
        if (currentPersistent == persistentFlags) return;
        var visibilityChanged = (currentPersistent & InstanceFlags3D.Visible) != (persistentFlags & InstanceFlags3D.Visible);
        record.Flags = persistentFlags | (record.Flags & (InstanceFlags3D.DirtyTransform | InstanceFlags3D.DirtyMaterial | InstanceFlags3D.DirtyVisibility));
        if (visibilityChanged)
        {
            record.Flags |= InstanceFlags3D.DirtyVisibility;
            unchecked { VisibilityVersion++; }
            MarkVisibilityDirty(index);
        }
        unchecked { _version++; }
    }

    public void MarkAllMaterialsDirty()
    {
        unchecked { MaterialVersion++; _version++; }
        for (var i = 0; i < Count; i++)
        {
            _records[i].MaterialVersion = MaterialVersion;
            _records[i].Flags |= InstanceFlags3D.DirtyMaterial;
            MarkMaterialDirty(i);
        }
    }

    public int DrainDirtyTransforms(Span<int> destination) => Drain(_dirtyTransforms, _dirtyTransformSet, destination, InstanceFlags3D.DirtyTransform);
    public int DrainDirtyMaterials(Span<int> destination) => Drain(_dirtyMaterials, _dirtyMaterialSet, destination, InstanceFlags3D.DirtyMaterial);
    public int DrainDirtyVisibility(Span<int> destination) => Drain(_dirtyVisibility, _dirtyVisibilitySet, destination, InstanceFlags3D.DirtyVisibility);

    public void Clear()
    {
        if (Count == 0) return;
        Array.Clear(_records, 0, Count);
        Count = 0;
        unchecked { _version++; TransformVersion++; MaterialVersion++; VisibilityVersion++; }
        _dirtyTransforms.Clear();
        _dirtyMaterials.Clear();
        _dirtyVisibility.Clear();
        _dirtyTransformSet.Clear();
        _dirtyMaterialSet.Clear();
        _dirtyVisibilitySet.Clear();
    }

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index), index, "Instance index lies outside the live range.");
    }

    private static void ValidateFlags(InstanceFlags3D flags, string name)
    {
        if ((flags & ~PublicFlags) != 0)
            throw new ArgumentOutOfRangeException(name, flags, "Instance flags contain unknown or engine-owned dirty bits.");
    }

    private void MarkTransformDirty(int index) { if (_dirtyTransformSet.Add(index)) _dirtyTransforms.Add(index); }
    private void MarkMaterialDirty(int index) { if (_dirtyMaterialSet.Add(index)) _dirtyMaterials.Add(index); }
    private void MarkVisibilityDirty(int index) { if (_dirtyVisibilitySet.Add(index)) _dirtyVisibility.Add(index); }

    private void EnsureCapacity(int required)
    {
        if (_records.Length >= required) return;
        var newCapacity = global::System.Math.Max(required, checked(_records.Length * 2));
        Array.Resize(ref _records, newCapacity);
    }

    private int Drain(List<int> source, HashSet<int> set, Span<int> destination, InstanceFlags3D clearFlag)
    {
        var count = global::System.Math.Min(source.Count, destination.Length);
        for (var i = 0; i < count; i++)
        {
            var index = source[i];
            destination[i] = index;
            if ((uint)index < (uint)Count) _records[index].Flags &= ~clearFlag;
            set.Remove(index);
        }
        if (count == source.Count) source.Clear();
        else source.RemoveRange(0, count);
        return count;
    }
}
