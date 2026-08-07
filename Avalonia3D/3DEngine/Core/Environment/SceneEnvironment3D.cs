using System;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Environment;

public sealed class SceneEnvironment3D
{
    public SceneEnvironment3D()
    {
        Skybox = new Skybox3D();
        Skybox.Changed += (_, _) => RaiseChanged();
    }

    public event EventHandler? Changed;

    public Skybox3D Skybox { get; }

    internal Func<SceneAccessLease3D>? MutationScopeRequested
    {
        get => Skybox.MutationScopeRequested;
        set => Skybox.MutationScopeRequested = value;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
