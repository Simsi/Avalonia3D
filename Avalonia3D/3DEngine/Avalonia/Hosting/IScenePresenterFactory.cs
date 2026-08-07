namespace ThreeDEngine.Avalonia.Hosting;

public interface IScenePresenterFactory
{
    ThreeDEngine.Core.Rendering.BackendKind Kind { get; }
    IScenePresenter CreatePresenter();
}
