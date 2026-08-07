using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.WebGL.Controls;

namespace ThreeDEngine.Avalonia.WebGL;

internal sealed class WebGlScenePresenterFactory : IScenePresenterFactory
{
    public ThreeDEngine.Core.Rendering.BackendKind Kind => ThreeDEngine.Core.Rendering.BackendKind.WebGlBrowser;
    public IScenePresenter CreatePresenter() => new WebGlScenePresenter();
}
