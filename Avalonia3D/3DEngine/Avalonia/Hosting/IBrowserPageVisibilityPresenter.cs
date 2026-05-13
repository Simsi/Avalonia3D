namespace ThreeDEngine.Avalonia.Hosting;

public interface IBrowserPageVisibilityPresenter
{
    bool IsDocumentHidden { get; }
    int DocumentVisibilityVersion { get; }
}
