using System;
using System.Threading;

namespace ThreeDEngine.Core.World;

/// <summary>
/// Non-copyable read lease for one immutable world publication slot. Dispose it promptly so the
/// simulation owner can reuse the slot without blocking or allocating another snapshot.
/// </summary>
public sealed class WorldReadSnapshotLease3D : IDisposable
{
    private WorldSnapshotPublisher3D? _publisher;
    private readonly int _slotIndex;

    internal WorldReadSnapshotLease3D(WorldSnapshotPublisher3D publisher, int slotIndex, WorldSnapshot3D snapshot)
    {
        _publisher = publisher;
        _slotIndex = slotIndex;
        Snapshot = snapshot;
    }

    public WorldSnapshot3D Snapshot { get; }
    public bool IsDisposed => Volatile.Read(ref _publisher) is null;

    public void Dispose()
    {
        var publisher = Interlocked.Exchange(ref _publisher, null);
        publisher?.Release(_slotIndex);
    }
}
