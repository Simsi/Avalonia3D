using System;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Avalonia.Hosting;

public sealed class SceneFrameRenderedEventArgs : EventArgs
{
    public SceneFrameRenderedEventArgs(BackendKind backend, double frameMilliseconds)
        : this(backend, frameMilliseconds, RenderStats.Empty)
    {
    }

    public SceneFrameRenderedEventArgs(BackendKind backend, double frameMilliseconds, RenderStats stats)
    {
        Backend = backend;
        FrameMilliseconds = frameMilliseconds;
        Stats = stats ?? RenderStats.Empty;
    }

    public BackendKind Backend { get; }

    // Backward-compatible alias kept for existing Avalonia3D samples and host apps.
    // New code should prefer Backend, but v90/v91 host code may still read Kind.
    public BackendKind Kind => Backend;

    public double FrameMilliseconds { get; }
    public RenderStats Stats { get; }
}
