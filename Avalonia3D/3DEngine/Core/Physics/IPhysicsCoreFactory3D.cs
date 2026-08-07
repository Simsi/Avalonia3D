using ThreeDEngine.Core.Hosting;

namespace ThreeDEngine.Core.Physics;

/// <summary>Creates one exclusively scene-owned physics world.</summary>
public interface IPhysicsCoreFactory3D
{
    IPhysicsCore CreatePhysicsCore(IEngineServiceProvider3D services);
}
