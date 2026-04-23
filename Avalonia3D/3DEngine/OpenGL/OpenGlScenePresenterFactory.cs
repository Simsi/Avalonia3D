using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.OpenGL.Controls;

namespace ThreeDEngine.Avalonia.OpenGL;

public sealed class OpenGlScenePresenterFactory : IScenePresenterFactory
{
    public IScenePresenter CreatePresenter() => new OpenGlScenePresenter();
}
