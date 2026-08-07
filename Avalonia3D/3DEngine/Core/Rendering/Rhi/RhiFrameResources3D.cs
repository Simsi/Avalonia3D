using System;

namespace ThreeDEngine.Core.Rendering.Rhi;

internal readonly struct RhiFrameResourceLease3D
{
    internal RhiFrameResourceLease3D(int slot, long frameIndex, RhiUploadRing3D uploadRing)
    {
        Slot = slot;
        FrameIndex = frameIndex;
        UploadRing = uploadRing;
    }

    public int Slot { get; }
    public long FrameIndex { get; }
    public RhiUploadRing3D UploadRing { get; }
}

/// <summary>Triple-buffered frame-local resources guarded by submission fences.</summary>
internal sealed class RhiFrameResources3D
{
    private readonly FrameSlot[] _slots;
    private int _activeSlot = -1;
    private long _frameIndex;

    public RhiFrameResources3D(int bufferedFrameCount = 3, int uploadCapacityPerFrame = 4 * 1024 * 1024, int maximumUploadCapacityPerFrame = 64 * 1024 * 1024)
    {
        if (bufferedFrameCount < 2 || bufferedFrameCount > 8) throw new ArgumentOutOfRangeException(nameof(bufferedFrameCount));
        _slots = new FrameSlot[bufferedFrameCount];
        for (var i = 0; i < _slots.Length; i++)
            _slots[i] = new FrameSlot(new RhiUploadRing3D(uploadCapacityPerFrame, maximumUploadCapacityPerFrame));
    }

    public int BufferedFrameCount => _slots.Length;
    public int ActiveSlot => _activeSlot;
    public long FrameIndex => _frameIndex;
    public RhiUploadRing3D? ActiveUploadRing => _activeSlot >= 0 ? _slots[_activeSlot].UploadRing : null;

    public RhiFrameResourceLease3D BeginFrame(RhiQueue3D queue)
    {
        if (queue is null) throw new ArgumentNullException(nameof(queue));
        if (_activeSlot >= 0) throw new InvalidOperationException("An RHI frame-resource slot is already active.");
        var nextFrame = checked(_frameIndex + 1);
        var slotIndex = (int)((nextFrame - 1) % _slots.Length);
        var slot = _slots[slotIndex];
        if (slot.Fence.IsValid && !queue.IsComplete(slot.Fence))
            throw new InvalidOperationException($"RHI frame-resource slot {slotIndex} is still in flight ({slot.Fence}). No implicit blocking or transient fallback is permitted.");
        _frameIndex = nextFrame;
        _activeSlot = slotIndex;
        slot.UploadRing.BeginFrame(_frameIndex);
        return new RhiFrameResourceLease3D(slotIndex, _frameIndex, slot.UploadRing);
    }

    public void EndFrame(RhiFence3D fence)
    {
        if (_activeSlot < 0) throw new InvalidOperationException("No RHI frame-resource slot is active.");
        if (!fence.IsValid) throw new ArgumentException("A valid submission fence is required.", nameof(fence));
        _slots[_activeSlot].Fence = fence;
        _activeSlot = -1;
    }

    public void AbortFrame()
    {
        if (_activeSlot < 0) return;
        _slots[_activeSlot].UploadRing.Reset();
        _activeSlot = -1;
    }

    public void InvalidateContext()
    {
        _activeSlot = -1;
        _frameIndex = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i].Fence = default;
            _slots[i].UploadRing.Reset();
        }
    }

    private sealed class FrameSlot
    {
        public FrameSlot(RhiUploadRing3D uploadRing) => UploadRing = uploadRing;
        public RhiUploadRing3D UploadRing { get; }
        public RhiFence3D Fence { get; set; }
    }
}
