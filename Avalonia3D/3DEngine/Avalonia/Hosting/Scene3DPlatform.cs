using System;

namespace ThreeDEngine.Avalonia.Hosting;

public static class Scene3DPlatform
{
    private static IScenePresenterFactory? _factory;

    public static void RegisterFactory(IScenePresenterFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public static void ResetFactory() => _factory = null;

    public static IScenePresenterFactory GetFactory()
    {
        if (_factory is not null)
        {
            return _factory;
        }

        _factory = OperatingSystem.IsBrowser()
            ? new ThreeDEngine.Avalonia.WebGL.WebGlScenePresenterFactory()
            : new ThreeDEngine.Avalonia.OpenGL.OpenGlScenePresenterFactory();

        return _factory;
    }
}
