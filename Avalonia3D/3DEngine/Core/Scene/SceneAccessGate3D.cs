using System;
using System.Threading;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Serializes mutable simulation writes against render/read consumers. The gate supports
/// recursion because fixed-update callbacks may call normal scene APIs while a simulation
/// write lease is already active.
/// </summary>
internal sealed class SceneAccessGate3D : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private bool _disposed;

    public SceneAccessLease3D EnterRead()
    {
        ThrowIfDisposed();
        _lock.EnterReadLock();
        return new SceneAccessLease3D(this, write: false);
    }

    public SceneAccessLease3D EnterWrite()
    {
        ThrowIfDisposed();
        _lock.EnterWriteLock();
        return new SceneAccessLease3D(this, write: true);
    }

    internal void Exit(bool write)
    {
        if (write) _lock.ExitWriteLock();
        else _lock.ExitReadLock();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal readonly struct SceneAccessLease3D : IDisposable
{
    private readonly SceneAccessGate3D? _owner;
    private readonly bool _write;

    internal SceneAccessLease3D(SceneAccessGate3D owner, bool write)
    {
        _owner = owner;
        _write = write;
    }

    public void Dispose() => _owner?.Exit(_write);
}
