using System;

namespace ThreeDEngine.Core.Hosting;

/// <summary>Immutable service provider owned by one <see cref="Engine3D"/>.</summary>
public interface IEngineServiceProvider3D : IServiceProvider
{
    object GetRequiredService(Type serviceType);
    TService GetRequiredService<TService>() where TService : class;
    bool TryGetService<TService>(out TService? service) where TService : class;
}
