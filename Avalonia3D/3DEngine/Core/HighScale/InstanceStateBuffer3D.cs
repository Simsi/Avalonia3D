using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.HighScale;

/// <summary>
/// Dense per-instance runtime state for high-scale layers.
///
/// This buffer is intentionally separated from transforms/chunks. Telemetry/status changes
/// update only this state and must not force high-scale chunk rebuilds or full transform
/// instance-buffer uploads.
/// </summary>
public sealed class InstanceStateBuffer3D
{
    private int[] _materialVariants;
    private byte[] _flags;
    private bool[] _dirtyMarks;
    private readonly List<int> _dirtyIndices = new(1024);
    private int _version;

    public InstanceStateBuffer3D(int capacity = 1024)
    {
        var initial = System.Math.Max(1, capacity);
        _materialVariants = new int[initial];
        _flags = new byte[initial];
        _dirtyMarks = new bool[initial];
    }

    public int Version => _version;
    public bool HasDirtyState => _dirtyIndices.Count != 0;
    public IReadOnlyList<int> DirtyIndices => _dirtyIndices;
    public ReadOnlySpan<int> MaterialVariants => _materialVariants;
    public ReadOnlySpan<byte> Flags => _flags;

    public void EnsureCapacity(int count)
    {
        if (_materialVariants.Length >= count) return;
        var next = _materialVariants.Length;
        while (next < count) next *= 2;
        Array.Resize(ref _materialVariants, next);
        Array.Resize(ref _flags, next);
        Array.Resize(ref _dirtyMarks, next);
    }

    public int GetMaterialVariant(int index) => (uint)index < (uint)_materialVariants.Length ? _materialVariants[index] : 0;

    public byte GetFlags(int index) => (uint)index < (uint)_flags.Length ? _flags[index] : (byte)0;

    public bool SetMaterialVariant(int index, int variant)
    {
        EnsureCapacity(index + 1);
        if (_materialVariants[index] == variant) return false;
        _materialVariants[index] = variant;
        MarkDirty(index);
        return true;
    }

    public bool SetFlags(int index, byte flags)
    {
        EnsureCapacity(index + 1);
        if (_flags[index] == flags) return false;
        _flags[index] = flags;
        MarkDirty(index);
        return true;
    }

    public void MarkAllDirty(int count)
    {
        EnsureCapacity(count);
        for (var i = 0; i < count; i++)
        {
            MarkDirty(i);
        }
    }

    public void ClearDirty()
    {
        if (_dirtyIndices.Count == 0) return;
        for (var i = 0; i < _dirtyIndices.Count; i++)
        {
            var index = _dirtyIndices[i];
            if ((uint)index < (uint)_dirtyMarks.Length)
            {
                _dirtyMarks[index] = false;
            }
        }

        _dirtyIndices.Clear();
    }

    private void MarkDirty(int index)
    {
        EnsureCapacity(index + 1);
        if (!_dirtyMarks[index])
        {
            _dirtyMarks[index] = true;
            _dirtyIndices.Add(index);
        }

        _version++;
    }
}
