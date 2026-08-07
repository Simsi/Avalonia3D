using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Rhi;

/// <summary>Fence-gated logical/native release queue. Releases never occur before queue completion.</summary>
internal sealed class RhiDeferredLifetime3D
{
    private readonly Queue<PendingRelease> _pending = new();
    private long _releasedCount;

    public int PendingCount => _pending.Count;
    public long ReleasedCount => _releasedCount;

    public void Enqueue(RhiFence3D fence, Action release)
    {
        if (!fence.IsValid) throw new ArgumentException("A valid fence is required for deferred destruction.", nameof(fence));
        _pending.Enqueue(new PendingRelease(fence, release ?? throw new ArgumentNullException(nameof(release))));
    }

    public int Collect(RhiQueue3D queue)
    {
        if (queue is null) throw new ArgumentNullException(nameof(queue));
        var count = 0;
        while (_pending.Count != 0 && queue.IsComplete(_pending.Peek().Fence))
        {
            var pending = _pending.Dequeue();
            pending.Release();
            _releasedCount++;
            count++;
        }
        return count;
    }

    public void DrainAll()
    {
        while (_pending.Count != 0)
        {
            _pending.Dequeue().Release();
            _releasedCount++;
        }
    }

    public void ClearWithoutRelease() => _pending.Clear();

    private readonly struct PendingRelease
    {
        public PendingRelease(RhiFence3D fence, Action release) { Fence = fence; Release = release; }
        public RhiFence3D Fence { get; }
        public Action Release { get; }
    }
}
