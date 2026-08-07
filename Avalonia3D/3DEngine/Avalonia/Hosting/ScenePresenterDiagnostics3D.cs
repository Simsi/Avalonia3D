using System;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Avalonia.Hosting;

public sealed class ScenePresenterFaultedEventArgs3D : EventArgs
{
    public ScenePresenterFaultedEventArgs3D(Exception exception, ScenePresenterSnapshot3D snapshot)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        Snapshot = snapshot;
    }

    public Exception Exception { get; }
    public ScenePresenterSnapshot3D Snapshot { get; }
}

public readonly record struct ScenePresenterSnapshot3D(
    BackendKind Backend,
    bool Attached,
    bool Initialized,
    bool Disposed,
    bool Rendering,
    bool RenderPending,
    long RenderRequestCount,
    long RenderedFrameCount,
    long FaultCount,
    long LastRequestTimestamp,
    long LastFrameTimestamp,
    long LastFaultTimestamp,
    string State,
    string? LastFaultType,
    string? LastFaultMessage);

public interface IScenePresenterDiagnostics3D
{
    event EventHandler<ScenePresenterFaultedEventArgs3D>? Faulted;
    ScenePresenterSnapshot3D CapturePresenterSnapshot();
    void ResetFaultState();
}

public interface IBrowserDiagnosticExportPresenter3D
{
    void ExportTextFile(string fileName, string text);
}
