using ThreeDEngine.Core.Hosting;

namespace ThreeDEngine.Core.Physics.Jitter2;

public sealed class Jitter2PhysicsCoreFactory3D : IPhysicsCoreFactory3D
{
    public IPhysicsCore CreatePhysicsCore(IEngineServiceProvider3D services)
        => new Jitter2PhysicsCore();
}
