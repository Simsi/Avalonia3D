using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Scene;

/// <summary>Bounded allocation-free ring owned by a Scene3D.</summary>
internal sealed class SceneChangeJournal3D
{
    public const int DefaultCapacity = 16_384;

    private readonly int _maximumCapacity;
    private SceneChangeRecord3D[] _records;
    private int _start;
    private int _count;

    public SceneChangeJournal3D(int capacity = DefaultCapacity)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _maximumCapacity = capacity;
        _records = new SceneChangeRecord3D[global::System.Math.Min(128, capacity)];
    }

    public int Capacity => _maximumCapacity;
    public int AllocatedCapacity => _records.Length;
    public int Count => _count;
    public long LatestSequence { get; private set; }
    public long OldestSequence => _count == 0 ? LatestSequence + 1 : this[0].Sequence;

    public void Append(in SceneChangeRecord3D record)
    {
        if (record.Sequence != LatestSequence + 1)
        {
            throw new InvalidOperationException("Scene change sequences must be contiguous and monotonic.");
        }

        if (_count == _records.Length && _records.Length < _maximumCapacity)
        {
            Grow();
        }

        if (_count < _records.Length)
        {
            _records[(_start + _count) % _records.Length] = record;
            _count++;
        }
        else
        {
            _records[_start] = record;
            _start = (_start + 1) % _records.Length;
        }

        LatestSequence = record.Sequence;
    }

    public bool TryCopySince(long lastObservedSequence, List<SceneChangeRecord3D> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        if (lastObservedSequence == LatestSequence) return true;
        if (lastObservedSequence < 0 || lastObservedSequence > LatestSequence) return false;
        if (_count != 0 && lastObservedSequence < OldestSequence - 1) return false;

        // Sequences are contiguous, so jump directly to the first unseen ring slot instead
        // of scanning every retained historic record. A consumer one change behind is now
        // O(1 + changes) even when the 16K journal is full.
        var firstUnseenSequence = lastObservedSequence + 1;
        var offset = _count == 0 ? 0 : checked((int)(firstUnseenSequence - OldestSequence));
        if (offset < 0 || offset > _count) return false;
        for (var i = offset; i < _count; i++) output.Add(this[i]);
        return true;
    }

    public SceneChangeRecord3D this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _records[(_start + index) % _records.Length];
        }
    }

    public void Clear()
    {
        Array.Clear(_records);
        _start = 0;
        _count = 0;
        LatestSequence = 0;
    }

    private void Grow()
    {
        var nextCapacity = global::System.Math.Min(_maximumCapacity, checked(_records.Length * 2));
        var next = new SceneChangeRecord3D[nextCapacity];
        for (var i = 0; i < _count; i++) next[i] = this[i];
        _records = next;
        _start = 0;
    }
}
