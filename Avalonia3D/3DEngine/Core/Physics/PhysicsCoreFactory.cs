using ThreeDEngine.Core.Physics.Jitter2;

namespace ThreeDEngine.Core.Physics;

/// <summary>
/// Centralized factory for Avalonia3D's production physics backend.
/// The handwritten legacy solver has been removed; the built-in backend is Jitter2.
/// </summary>
public static class PhysicsCoreFactory
{
    public static IPhysicsCore CreateDefault() => new Jitter2PhysicsCore();
}
