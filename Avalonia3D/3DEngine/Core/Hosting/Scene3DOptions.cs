using System;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.World;

namespace ThreeDEngine.Core.Hosting;

/// <summary>Per-scene construction options. Values are copied when the scene is created.</summary>
public sealed class Scene3DOptions
{
    /// <summary>Null inherits <see cref="EngineConfiguration3D.PhysicsEnabledByDefault"/>.</summary>
    public bool? PhysicsEnabled { get; set; }

    /// <summary>
    /// Optional per-scene physics factory. A produced backend is exclusively owned by the scene.
    /// </summary>
    public Func<IEngineServiceProvider3D, IPhysicsCore>? PhysicsFactory { get; set; }

    public Action<ScenePerformanceOptions>? ConfigurePerformance { get; set; }
    public Action<SceneUpdateLoop3D>? ConfigureUpdateLoop { get; set; }

    /// <summary>Direct mutation policy after a simulation owner is bound.</summary>
    public WorldMutationPolicy3D MutationPolicy { get; set; } = WorldMutationPolicy3D.SynchronizedCompatibility;

    public static Scene3DOptions WithoutPhysics() => new() { PhysicsEnabled = false };

    /// <summary>Creates options that reject every non-owner runtime mutation.</summary>
    public static Scene3DOptions StrictOwnership(bool? physicsEnabled = null) => new()
    {
        PhysicsEnabled = physicsEnabled,
        MutationPolicy = WorldMutationPolicy3D.StrictSimulationOwner
    };

    internal Scene3DOptions Clone() => new()
    {
        PhysicsEnabled = PhysicsEnabled,
        PhysicsFactory = PhysicsFactory,
        ConfigurePerformance = ConfigurePerformance,
        ConfigureUpdateLoop = ConfigureUpdateLoop,
        MutationPolicy = MutationPolicy
    };
}
