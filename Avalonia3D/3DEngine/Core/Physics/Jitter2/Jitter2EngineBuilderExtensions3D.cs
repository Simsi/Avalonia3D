using System;
using ThreeDEngine.Core.Hosting;

namespace ThreeDEngine.Core.Physics.Jitter2;

/// <summary>Registers a scene-owned Jitter2 physics world factory.</summary>
public static class Jitter2EngineBuilderExtensions3D
{
    public static Engine3DBuilder UseJitter2Physics(this Engine3DBuilder builder, bool enabledByDefault = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.ReplaceSingleton<IPhysicsCoreFactory3D>(new Jitter2PhysicsCoreFactory3D());
        builder.PhysicsEnabledByDefault = enabledByDefault;
        return builder;
    }
}
