using System;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Avalonia.Hosting;

public sealed class SceneFrameRenderedEventArgs : EventArgs
{
    public SceneFrameRenderedEventArgs(BackendKind backend, double frameMilliseconds)
        : this(backend, frameMilliseconds, new RenderStats())
    {
    }

    public SceneFrameRenderedEventArgs(BackendKind backend, double frameMilliseconds, RenderStats? stats)
    {
        Backend = backend;
        FrameMilliseconds = frameMilliseconds;
        Stats = stats ?? new RenderStats();
    }

    public BackendKind Backend { get; }

    public BackendKind Kind => Backend;

    public double FrameMilliseconds { get; }
    public RenderStats Stats { get; }
}
