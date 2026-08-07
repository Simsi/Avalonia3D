using System;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

internal sealed class CompositeReplayableSceneCommand3D : IReplayableSceneCommand3D
{
    private readonly IReplayableSceneCommand3D[] _commands;

    public CompositeReplayableSceneCommand3D(IReplayableSceneCommand3D[] commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public string Name => $"Batch[{_commands.Length}]";

    public void Execute(Scene3D scene)
    {
        using var update = scene.BeginUpdate();
        for (var i = 0; i < _commands.Length; i++) _commands[i].Execute(scene);
    }

    public IReplayableSceneCommand3D CloneForReplay()
    {
        var clone = new IReplayableSceneCommand3D[_commands.Length];
        for (var i = 0; i < clone.Length; i++) clone[i] = _commands[i].CloneForReplay();
        return new CompositeReplayableSceneCommand3D(clone);
    }
}
