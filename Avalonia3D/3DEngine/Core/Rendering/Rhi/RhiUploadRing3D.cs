using System;

namespace ThreeDEngine.Core.Rendering.Rhi;

internal readonly struct RhiUploadSlice3D
{
    internal RhiUploadSlice3D(Memory<byte> memory, int offset, int length, long frameIndex)
    {
        Memory = memory;
        Offset = offset;
        Length = length;
        FrameIndex = frameIndex;
    }

    public Memory<byte> Memory { get; }
    public int Offset { get; }
    public int Length { get; }
    public long FrameIndex { get; }
}

/// <summary>Bounded, frame-local CPU staging ring. Exhaustion is explicit; no heap fallback is used.</summary>
internal sealed class RhiUploadRing3D
{
    private byte[] _storage;
    private readonly int _maximumCapacity;
    private int _used;
    private int _peakUsed;
    private long _frameIndex;

    public RhiUploadRing3D(int initialCapacity = 4 * 1024 * 1024, int maximumCapacity = 64 * 1024 * 1024)
    {
        if (initialCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        if (maximumCapacity < initialCapacity) throw new ArgumentOutOfRangeException(nameof(maximumCapacity));
        _storage = new byte[initialCapacity];
        _maximumCapacity = maximumCapacity;
    }

    public int Capacity => _storage.Length;
    public int MaximumCapacity => _maximumCapacity;
    public int Used => _used;
    public int PeakUsed => _peakUsed;
    public long FrameIndex => _frameIndex;

    public void BeginFrame(long frameIndex)
    {
        if (frameIndex <= 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        _frameIndex = frameIndex;
        _used = 0;
    }

    public RhiUploadSlice3D Allocate(int byteCount, int alignment = 16)
    {
        if (_frameIndex <= 0) throw new InvalidOperationException("BeginFrame must be called before upload allocation.");
        if (byteCount <= 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a power of two.");
        var offset = Align(_used, alignment);
        var required = checked(offset + byteCount);
        if (required > _storage.Length) Grow(required);
        _used = required;
        if (_used > _peakUsed) _peakUsed = _used;
        return new RhiUploadSlice3D(_storage.AsMemory(offset, byteCount), offset, byteCount, _frameIndex);
    }

    internal void Reset()
    {
        _used = 0;
        _frameIndex = 0;
    }

    private void Grow(int required)
    {
        if (required > _maximumCapacity)
            throw new InvalidOperationException($"RHI upload ring exhausted: requested={required}, maximum={_maximumCapacity}. No heap fallback is permitted.");
        var capacity = _storage.Length;
        while (capacity < required) capacity = checked(global::System.Math.Min(_maximumCapacity, capacity * 2));
        Array.Resize(ref _storage, capacity);
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & ~(alignment - 1));
}
