using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.WebGL.Controls;

namespace ThreeDEngine.Avalonia.WebGL;

public sealed class WebGlScenePresenterFactory : IScenePresenterFactory
{
    public IScenePresenter CreatePresenter() => new WebGlScenePresenter();
}
