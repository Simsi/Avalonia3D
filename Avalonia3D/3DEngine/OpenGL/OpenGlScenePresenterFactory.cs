using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.OpenGL.Controls;

namespace ThreeDEngine.Avalonia.OpenGL;

internal sealed class OpenGlScenePresenterFactory : IScenePresenterFactory
{
    public ThreeDEngine.Core.Rendering.BackendKind Kind => ThreeDEngine.Core.Rendering.BackendKind.OpenGlDesktop;
    public IScenePresenter CreatePresenter() => new OpenGlScenePresenter();
}
