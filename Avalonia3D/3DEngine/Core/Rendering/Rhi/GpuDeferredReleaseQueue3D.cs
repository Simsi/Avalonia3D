using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Rhi;

/// <summary>
/// Frame-indexed deferred destruction queue. Backends retain the native object for a bounded
/// number of submitted frames before deleting it, preventing immediate reuse while prior GPU
/// work may still reference the allocation.
/// </summary>
internal sealed class GpuDeferredReleaseQueue3D<T>
{
    private readonly Queue<Entry> _entries = new();

    public int Count => _entries.Count;

    public void Enqueue(T resource, long submittedFrame, int delayFrames)
    {
        if (delayFrames < 0) throw new ArgumentOutOfRangeException(nameof(delayFrames));
        _entries.Enqueue(new Entry(resource, checked(submittedFrame + delayFrames)));
    }


    public bool TryCancel(Predicate<T> match, out T resource)
    {
        ArgumentNullException.ThrowIfNull(match);
        resource = default!;
        if (_entries.Count == 0) return false;

        var found = false;
        var count = _entries.Count;
        for (var i = 0; i < count; i++)
        {
            var entry = _entries.Dequeue();
            if (!found && match(entry.Resource))
            {
                resource = entry.Resource;
                found = true;
                continue;
            }
            _entries.Enqueue(entry);
        }
        return found;
    }

    public void DrainReady(long completedFrame, Action<T> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        while (_entries.Count > 0 && _entries.Peek().ReleaseFrame <= completedFrame)
        {
            release(_entries.Dequeue().Resource);
        }
    }

    public void DrainAll(Action<T> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        while (_entries.Count > 0) release(_entries.Dequeue().Resource);
    }

    public void ClearWithoutRelease() => _entries.Clear();

    private readonly record struct Entry(T Resource, long ReleaseFrame);
}
