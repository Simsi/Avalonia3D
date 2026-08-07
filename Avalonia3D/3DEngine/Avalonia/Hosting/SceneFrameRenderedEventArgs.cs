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

    /// <summary>
    /// Time spent executing the backend render submission. This is not the interval
    /// between frames and must not be used to calculate presented FPS.
    /// </summary>
    public double FrameMilliseconds { get; }

    /// <summary>Actual interval between consecutive frames observed by Scene3DControl.</summary>
    public double PresentationIntervalMilliseconds => Stats.FrameTotalMilliseconds;

    /// <summary>Presented frame rate derived from the real frame interval.</summary>
    public double PresentedFramesPerSecond => Stats.PresentedFramesPerSecond > 0d
        ? Stats.PresentedFramesPerSecond
        : PresentationIntervalMilliseconds > 0d
            ? 1000d / PresentationIntervalMilliseconds
            : 0d;

    public RenderStats Stats { get; }
}
