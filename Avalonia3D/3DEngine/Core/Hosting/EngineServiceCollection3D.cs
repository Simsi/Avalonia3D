using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Hosting;

/// <summary>
/// Exact-type singleton registrations for an engine scope. The collection is mutable only
/// until <see cref="Engine3DBuilder.Build"/> freezes it.
/// </summary>
public sealed class EngineServiceCollection3D
{
    private readonly Dictionary<Type, EngineServiceDescriptor3D> _descriptors = new();
    private bool _frozen;

    public int Count => _descriptors.Count;

    public bool Contains<TService>() where TService : class
        => _descriptors.ContainsKey(typeof(TService));

    public EngineServiceCollection3D AddSingleton<TService>(
        TService instance,
        EngineServiceOwnership3D ownership = EngineServiceOwnership3D.External)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        return Add(typeof(TService), _ => instance, ownership == EngineServiceOwnership3D.Engine, replace: false);
    }

    public EngineServiceCollection3D AddSingleton<TService>(Func<IEngineServiceProvider3D, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Add(typeof(TService), services => factory(services), disposeWithEngine: true, replace: false);
    }

    public EngineServiceCollection3D ReplaceSingleton<TService>(
        TService instance,
        EngineServiceOwnership3D ownership = EngineServiceOwnership3D.External)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        return Add(typeof(TService), _ => instance, ownership == EngineServiceOwnership3D.Engine, replace: true);
    }

    public EngineServiceCollection3D ReplaceSingleton<TService>(Func<IEngineServiceProvider3D, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Add(typeof(TService), services => factory(services), disposeWithEngine: true, replace: true);
    }

    internal EngineServiceProvider3D BuildProvider()
    {
        ThrowIfFrozen();
        _frozen = true;
        return new EngineServiceProvider3D(new Dictionary<Type, EngineServiceDescriptor3D>(_descriptors));
    }

    private EngineServiceCollection3D Add(
        Type serviceType,
        Func<IEngineServiceProvider3D, object> factory,
        bool disposeWithEngine,
        bool replace)
    {
        ThrowIfFrozen();
        if (!replace && _descriptors.ContainsKey(serviceType))
        {
            throw new InvalidOperationException($"Service '{serviceType.FullName}' is already registered. Use ReplaceSingleton for an intentional override.");
        }

        _descriptors[serviceType] = new EngineServiceDescriptor3D(serviceType, factory, disposeWithEngine);
        return this;
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Engine services are frozen after Build and cannot be mutated.");
        }
    }
}

internal sealed record EngineServiceDescriptor3D(
    Type ServiceType,
    Func<IEngineServiceProvider3D, object> Factory,
    bool DisposeWithEngine);
