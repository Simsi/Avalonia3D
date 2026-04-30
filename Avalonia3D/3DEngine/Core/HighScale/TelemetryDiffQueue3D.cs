using System;
using System.Diagnostics;
using System.Numerics;

namespace ThreeDEngine.Core.HighScale;

/// <summary>
/// Dense coalescing queue for high-scale telemetry.
/// Latest update wins per instance index; draining is allocation-free and budgeted.
/// </summary>
public sealed class TelemetryDiffQueue3D
{
    private Diff[] _diffs = Array.Empty<Diff>();
    private bool[] _hasDiff = Array.Empty<bool>();
    private int[] _dirtyIndices = Array.Empty<int>();
    private int _dirtyStart;
    private int _dirtyCount;

    public int Count => _dirtyCount - _dirtyStart;

    public void EnqueueMaterial(int index, int materialVariantId)
    {
        if (index < 0) return;
        EnsureCapacity(index + 1);
        MarkDirty(index);
        _diffs[index].HasMaterial = true;
        _diffs[index].MaterialVariantId = materialVariantId;
    }

    public void EnqueueVisibility(int index, bool visible)
    {
        if (index < 0) return;
        EnsureCapacity(index + 1);
        MarkDirty(index);
        _diffs[index].HasVisibility = true;
        _diffs[index].Visible = visible;
    }

    public void EnqueueTransform(int index, Matrix4x4 transform)
    {
        if (index < 0) return;
        EnsureCapacity(index + 1);
        MarkDirty(index);
        _diffs[index].HasTransform = true;
        _diffs[index].Transform = transform;
    }

    public int DrainTo(HighScaleInstanceLayer3D layer, int maxUpdates)
        => DrainTo(layer, maxUpdates, 0d, applyTransforms: true);

    public int DrainTo(HighScaleInstanceLayer3D layer, int maxUpdates, double maxMilliseconds)
        => DrainTo(layer, maxUpdates, maxMilliseconds, applyTransforms: true);

    public int DrainTo(HighScaleInstanceLayer3D layer, int maxUpdates, double maxMilliseconds, bool applyTransforms)
    {
        if (Count == 0 || maxUpdates <= 0) return 0;

        var applied = 0;
        var deadline = maxMilliseconds > 0d
            ? Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * maxMilliseconds / 1000d)
            : long.MaxValue;

        using var batch = layer.BeginTelemetryBatch();
        while (_dirtyStart < _dirtyCount && applied < maxUpdates)
        {
            if ((applied & 7) == 0 && Stopwatch.GetTimestamp() >= deadline)
            {
                break;
            }
            var key = _dirtyIndices[_dirtyStart++];
            _hasDiff[key] = false;

            if ((uint)key >= (uint)layer.Instances.Count)
            {
                _diffs[key] = default;
                continue;
            }

            var diff = _diffs[key];
            _diffs[key] = default;

            if (diff.HasMaterial) batch.SetMaterialVariant(key, diff.MaterialVariantId);
            if (diff.HasVisibility) batch.SetVisible(key, diff.Visible);
            if (applyTransforms && diff.HasTransform) batch.SetTransform(key, diff.Transform);
            applied++;
        }

        CompactDirtyQueueIfNeeded();
        return applied;
    }

    public void Clear()
    {
        for (var i = _dirtyStart; i < _dirtyCount; i++)
        {
            var key = _dirtyIndices[i];
            if ((uint)key < (uint)_hasDiff.Length)
            {
                _hasDiff[key] = false;
                _diffs[key] = default;
            }
        }

        _dirtyStart = 0;
        _dirtyCount = 0;
    }

    private void MarkDirty(int index)
    {
        if (_hasDiff[index]) return;
        _hasDiff[index] = true;
        EnsureDirtyIndexCapacity();
        _dirtyIndices[_dirtyCount++] = index;
    }

    private void EnsureDirtyIndexCapacity()
    {
        if (_dirtyIndices.Length > _dirtyCount) return;
        CompactDirtyQueueIfNeeded(force: true);
        if (_dirtyIndices.Length > _dirtyCount) return;
        Array.Resize(ref _dirtyIndices, System.Math.Max(4, _dirtyIndices.Length * 2));
    }

    private void CompactDirtyQueueIfNeeded(bool force = false)
    {
        if (_dirtyStart == 0) return;
        if (_dirtyStart == _dirtyCount)
        {
            _dirtyStart = 0;
            _dirtyCount = 0;
            return;
        }

        if (!force && _dirtyStart < 4096 && _dirtyStart < _dirtyIndices.Length / 2) return;
        var remaining = _dirtyCount - _dirtyStart;
        Array.Copy(_dirtyIndices, _dirtyStart, _dirtyIndices, 0, remaining);
        _dirtyStart = 0;
        _dirtyCount = remaining;
    }

    private void EnsureCapacity(int required)
    {
        if (_diffs.Length >= required) return;

        var next = System.Math.Max(required, System.Math.Max(4, _diffs.Length * 2));
        Array.Resize(ref _diffs, next);
        Array.Resize(ref _hasDiff, next);
    }

    private struct Diff
    {
        public bool HasMaterial;
        public int MaterialVariantId;
        public bool HasVisibility;
        public bool Visible;
        public bool HasTransform;
        public Matrix4x4 Transform;
    }
}
