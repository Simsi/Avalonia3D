using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.OpenGL.Rendering;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.OpenGL.Controls;

public sealed class OpenGlScenePresenter : OpenGlControlBase, IScenePresenter
{
    private readonly OpenGlSceneRenderer _renderer = new();
    private RendererInvalidationKind _pendingInvalidation = RendererInvalidationKind.FullFrame;
    private Scene3D _scene = new();

    public OpenGlScenePresenter()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    public event EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;

    public BackendKind Kind => BackendKind.OpenGlDesktop;
    public Control View => this;

    public Scene3D Scene
    {
        get => _scene;
        set
        {
            _scene = value ?? throw new ArgumentNullException(nameof(value));
            _pendingInvalidation = RendererInvalidationKind.FullFrame;
            RequestNextFrameRendering();
        }
    }

    public void NotifySceneChanged(SceneChangedEventArgs change, RendererInvalidationKind invalidation)
    {
        _pendingInvalidation |= invalidation;
        _renderer.NotifySceneChanged(change, _pendingInvalidation);
    }

    public void RequestRender() => RequestNextFrameRendering();

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        _renderer.Initialize(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        var start = Stopwatch.GetTimestamp();
        var invalidation = _pendingInvalidation == RendererInvalidationKind.None ? RendererInvalidationKind.DrawOnly : _pendingInvalidation;
        _pendingInvalidation = RendererInvalidationKind.None;
        var stats = _renderer.Render(gl, fb, Scene, Bounds, invalidation);
        stats.BackendMilliseconds = GetElapsedMilliseconds(start);
        FrameRendered?.Invoke(this, new SceneFrameRenderedEventArgs(Kind, stats.BackendMilliseconds, stats));
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer.Deinitialize(gl);
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlLost()
    {
        _renderer.Reset();
        base.OnOpenGlLost();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            RequestNextFrameRendering();
        }
    }

    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
    }
}
