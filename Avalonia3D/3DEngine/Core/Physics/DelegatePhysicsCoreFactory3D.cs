using System;
using ThreeDEngine.Core.Hosting;

namespace ThreeDEngine.Core.Physics;

public sealed class DelegatePhysicsCoreFactory3D : IPhysicsCoreFactory3D
{
    private readonly Func<IEngineServiceProvider3D, IPhysicsCore> _factory;

    public DelegatePhysicsCoreFactory3D(Func<IEngineServiceProvider3D, IPhysicsCore> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public IPhysicsCore CreatePhysicsCore(IEngineServiceProvider3D services)
        => _factory(services) ?? throw new InvalidOperationException("The configured physics factory returned null.");
}
