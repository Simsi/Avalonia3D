using System;
using System.Threading;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

/// <summary>
/// Three-slot publication ring. Simulation never overwrites a slot held by a reader and never
/// waits for rendering. If every non-current slot is busy, the newest publication is dropped
/// and the previous immutable snapshot remains available.
/// </summary>
internal sealed class WorldSnapshotPublisher3D
{
    private sealed class Slot
    {
        public readonly WorldSnapshot3D Snapshot = new();
        public int Readers;
    }

    private readonly Slot[] _slots = { new(), new(), new() };
    private int _currentIndex = -1;
    private long _publicationVersion;
    private long _droppedPublications;

    public long PublicationVersion => Volatile.Read(ref _publicationVersion);
    public long DroppedPublicationCount => Interlocked.Read(ref _droppedPublications);

    public bool Publish(Scene3D scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var current = Volatile.Read(ref _currentIndex);
        var target = FindWritableSlot(current);
        if (target < 0)
        {
            var dropped = Interlocked.Increment(ref _droppedPublications);
            if (dropped == 1 || dropped % 120 == 0)
            {
                EngineLog3D.Warning("WorldSnapshot", $"World snapshot publication skipped because all three slots are held by readers; dropped={dropped}.");
            }
            return false;
        }

        var version = Interlocked.Increment(ref _publicationVersion);
        _slots[target].Snapshot.Capture(scene, version);
        Volatile.Write(ref _currentIndex, target);
        return true;
    }

    public WorldReadSnapshotLease3D Acquire()
    {
        while (true)
        {
            var index = Volatile.Read(ref _currentIndex);
            if (index < 0)
            {
                throw new InvalidOperationException("No world snapshot has been published yet.");
            }

            Interlocked.Increment(ref _slots[index].Readers);
            if (index == Volatile.Read(ref _currentIndex))
            {
                return new WorldReadSnapshotLease3D(this, index, _slots[index].Snapshot);
            }

            Interlocked.Decrement(ref _slots[index].Readers);
        }
    }

    public bool TryAcquire(out WorldReadSnapshotLease3D? lease)
    {
        if (Volatile.Read(ref _currentIndex) < 0)
        {
            lease = default;
            return false;
        }

        lease = Acquire();
        return true;
    }

    internal void Release(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_slots.Length) return;
        Interlocked.Decrement(ref _slots[slotIndex].Readers);
    }

    private int FindWritableSlot(int current)
    {
        for (var offset = 1; offset <= _slots.Length; offset++)
        {
            var candidate = (current + offset + _slots.Length) % _slots.Length;
            if (candidate == current) continue;
            if (Volatile.Read(ref _slots[candidate].Readers) == 0) return candidate;
        }
        return -1;
    }
}
