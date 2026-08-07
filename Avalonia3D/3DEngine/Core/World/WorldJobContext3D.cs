using System;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

public sealed class WorldJobContext3D
{
    private readonly Scene3D _scene;

    internal WorldJobContext3D(
        Scene3D scene,
        WorldSnapshot3D snapshot,
        SceneCommandBuffer3D commands,
        in SceneFixedUpdateContext3D fixedUpdate,
        WorldJobAccess3D access)
    {
        _scene = scene;
        Snapshot = snapshot;
        Commands = commands;
        FixedUpdate = fixedUpdate;
        Access = access;
    }

    /// <summary>Immutable for this invocation only; do not retain it after Execute returns.</summary>
    public WorldSnapshot3D Snapshot { get; }
    public SceneCommandBuffer3D Commands { get; }
    public SceneFixedUpdateContext3D FixedUpdate { get; }
    public WorldJobAccess3D Access { get; }

    /// <summary>Mutable scene access is available only to exclusive jobs.</summary>
    public Scene3D Scene
        => Access == WorldJobAccess3D.Exclusive
            ? _scene
            : throw new InvalidOperationException("Read-only world jobs cannot access mutable Scene3D. Use Snapshot and Commands.");
}
