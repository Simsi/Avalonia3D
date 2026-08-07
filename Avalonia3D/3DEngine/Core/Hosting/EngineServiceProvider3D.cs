using System;
using System.Collections.Generic;
using System.Threading;

namespace ThreeDEngine.Core.Hosting;

internal sealed class EngineServiceProvider3D : IEngineServiceProvider3D, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, EngineServiceDescriptor3D> _descriptors;
    private readonly Dictionary<Type, object> _instances = new();
    private readonly HashSet<Type> _resolving = new();
    private readonly List<(object Instance, bool Dispose)> _creationOrder = new();
    private bool _validated;
    private bool _disposed;

    public EngineServiceProvider3D(Dictionary<Type, EngineServiceDescriptor3D> descriptors)
    {
        _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (Volatile.Read(ref _validated))
        {
            ThrowIfDisposed();
            return _instances.TryGetValue(serviceType, out var instance) ? instance : null;
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            return _descriptors.ContainsKey(serviceType) ? Resolve(serviceType) : null;
        }
    }

    public object GetRequiredService(Type serviceType)
        => GetService(serviceType) ?? throw new InvalidOperationException($"Required engine service '{serviceType.FullName}' is not registered.");

    public TService GetRequiredService<TService>() where TService : class
        => (TService)GetRequiredService(typeof(TService));

    public bool TryGetService<TService>(out TService? service) where TService : class
    {
        service = GetService(typeof(TService)) as TService;
        return service is not null;
    }

    public void ValidateAll()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var serviceType in _descriptors.Keys)
            {
                _ = Resolve(serviceType);
            }
            Volatile.Write(ref _validated, true);
        }
    }

    public void Dispose()
    {
        List<(object Instance, bool Dispose)> created;
        lock (_gate)
        {
            if (_disposed) return;
            Volatile.Write(ref _disposed, true);
            created = new List<(object Instance, bool Dispose)>(_creationOrder);
            _creationOrder.Clear();
            _resolving.Clear();
        }

        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        List<Exception>? failures = null;
        for (var i = created.Count - 1; i >= 0; i--)
        {
            var entry = created[i];
            if (entry.Dispose && entry.Instance is IDisposable disposable && disposed.Add(entry.Instance))
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more engine-owned services failed to dispose.", failures);
        }
    }

    private object Resolve(Type serviceType)
    {
        if (_instances.TryGetValue(serviceType, out var existing)) return existing;
        if (!_descriptors.TryGetValue(serviceType, out var descriptor))
        {
            throw new InvalidOperationException($"Required engine service '{serviceType.FullName}' is not registered.");
        }
        if (!_resolving.Add(serviceType))
        {
            throw new InvalidOperationException($"Circular engine service dependency detected while resolving '{serviceType.FullName}'.");
        }

        try
        {
            var instance = descriptor.Factory(this)
                ?? throw new InvalidOperationException($"Factory for engine service '{serviceType.FullName}' returned null.");
            if (!serviceType.IsInstanceOfType(instance))
            {
                throw new InvalidOperationException($"Factory for '{serviceType.FullName}' returned incompatible type '{instance.GetType().FullName}'.");
            }

            _instances.Add(serviceType, instance);
            _creationOrder.Add((instance, descriptor.DisposeWithEngine));
            return instance;
        }
        finally
        {
            _resolving.Remove(serviceType);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
}
