namespace ThreeDEngine.Core.World;

/// <summary>
/// Controls direct mutable access after a simulation owner has been bound to a world.
/// New applications should use <see cref="StrictSimulationOwner"/> and marshal all runtime
/// mutations through World3D command buffers. The compatibility policy remains available for
/// source-drop demos that are still being migrated.
/// </summary>
public enum WorldMutationPolicy3D
{
    /// <summary>
    /// Direct cross-thread mutations are serialized by the scene access gate. The first such
    /// mutation is logged because it can stall rendering and should be migrated to a command.
    /// </summary>
    SynchronizedCompatibility = 0,

    /// <summary>
    /// Once a simulation owner is bound, direct mutations from any other thread fail fast.
    /// </summary>
    StrictSimulationOwner = 1
}
