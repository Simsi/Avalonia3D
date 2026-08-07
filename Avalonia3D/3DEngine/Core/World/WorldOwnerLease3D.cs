using System;
using System.Threading;

namespace ThreeDEngine.Core.World;

/// <summary>Non-copyable owner token; disposal releases exactly one owner scope.</summary>
internal sealed class WorldOwnerLease3D : IDisposable
{
    private World3D? _world;
    private readonly int _threadId;
    private readonly bool _transient;

    internal WorldOwnerLease3D(World3D world, int threadId, bool transient)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _threadId = threadId;
        _transient = transient;
    }

    public void Dispose()
    {
        var world = Interlocked.Exchange(ref _world, null);
        world?.ExitOwnerScope(_threadId, _transient);
    }
}
