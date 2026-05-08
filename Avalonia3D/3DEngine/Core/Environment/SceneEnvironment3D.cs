using System;

namespace ThreeDEngine.Core.Environment;

public sealed class SceneEnvironment3D
{
    public SceneEnvironment3D()
    {
        Skybox = new Skybox3D();
        DirectionalShadows = new DirectionalShadowSettings3D();
        Skybox.Changed += (_, _) => RaiseChanged();
        DirectionalShadows.Changed += (_, _) => RaiseChanged();
    }

    public event EventHandler? Changed;

    public Skybox3D Skybox { get; }

    public DirectionalShadowSettings3D DirectionalShadows { get; }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
