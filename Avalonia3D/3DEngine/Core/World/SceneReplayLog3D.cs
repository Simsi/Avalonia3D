using System;

namespace ThreeDEngine.Core.World;

public readonly record struct SceneReplayEntry3D(
    long ExecutionTick,
    long CommandSequence,
    string CommandName,
    IReplayableSceneCommand3D Command);

/// <summary>Immutable deterministic command log.</summary>
public sealed class SceneReplayLog3D
{
    internal SceneReplayLog3D(SceneReplayEntry3D[] entries, long finalTick)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        FinalTick = finalTick;
    }

    public ReadOnlyMemory<SceneReplayEntry3D> Entries { get; }
    public long FinalTick { get; }
}
