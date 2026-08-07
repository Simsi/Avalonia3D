using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Avalonia.OpenGL.Rendering;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Rhi;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.OpenGL.Controls;

internal sealed class OpenGlScenePresenter : OpenGlControlBase, IScenePresenter, IScenePresenterDiagnostics3D
{
    private readonly OpenGlSceneRenderer _renderer = new();
    private Scene3D? _scene;
    private bool _disposed;
    private bool _initialized;
    private bool _rendering;
    private long _renderRequestCount;
    private long _renderedFrameCount;
    private long _faultCount;
    private long _lastRequestTimestamp;
    private long _lastFrameTimestamp;
    private long _lastFaultTimestamp;
    private Exception? _lastFault;
    private bool _fatalFaultPublished;

    public OpenGlScenePresenter()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    public event EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;
    public event EventHandler<ScenePresenterFaultedEventArgs3D>? Faulted;

    public BackendKind Kind => BackendKind.OpenGlDesktop;
    public IRenderDeviceDiagnostics3D? RenderDevice => _renderer.Device;
    public Control View => this;

    public Scene3D Scene
    {
        get => _scene ?? throw new InvalidOperationException("A scene must be assigned before rendering.");
        set
        {
            ThrowIfDisposed();
            if (value is null) throw new ArgumentNullException(nameof(value));
            _renderer.ConfigureResources(value.Engine.Configuration.Resources);
            _scene = value;
            RequestNextFrameRendering();
        }
    }

    public void RequestRender()
    {
        if (_disposed) return;
        System.Threading.Interlocked.Increment(ref _renderRequestCount);
        System.Threading.Volatile.Write(ref _lastRequestTimestamp, Stopwatch.GetTimestamp());
        RequestNextFrameRendering();
    }

    public ScenePresenterSnapshot3D CapturePresenterSnapshot()
        => new(Kind, TopLevel.GetTopLevel(this) is not null, _initialized, _disposed, _rendering, false,
            System.Threading.Interlocked.Read(ref _renderRequestCount),
            System.Threading.Interlocked.Read(ref _renderedFrameCount),
            System.Threading.Interlocked.Read(ref _faultCount),
            System.Threading.Volatile.Read(ref _lastRequestTimestamp),
            System.Threading.Volatile.Read(ref _lastFrameTimestamp),
            System.Threading.Volatile.Read(ref _lastFaultTimestamp),
            $"contextInitialized={_initialized}; visualRoot={TopLevel.GetTopLevel(this) is not null}; bounds={Bounds.Width:0.##}x{Bounds.Height:0.##}",
            _lastFault?.GetType().FullName, _lastFault?.Message);

    public void ResetFaultState()
    {
        _fatalFaultPublished = false;
        _lastFault = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene = null;
        FrameRendered = null;
        Faulted = null;
        EngineLog3D.Information("OpenGL", "Presenter disposed; scene reference released.");
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        ThrowIfDisposed();
        try
        {
            _renderer.Initialize(gl);
            _initialized = true;
            ResetFaultState();
            EngineLog3D.Information("OpenGL", "Renderer initialized.");
        }
        catch (Exception exception)
        {
            PublishFault(exception, "Renderer initialization failed.");
            throw;
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        ThrowIfDisposed();
        _rendering = true;
        try
        {
            var start = Stopwatch.GetTimestamp();
            var stats = _renderer.Render(gl, fb, Scene, Bounds);
            stats.BackendMilliseconds = GetElapsedMilliseconds(start);
            System.Threading.Interlocked.Increment(ref _renderedFrameCount);
            System.Threading.Volatile.Write(ref _lastFrameTimestamp, Stopwatch.GetTimestamp());
            FrameRendered?.Invoke(this, new SceneFrameRenderedEventArgs(Kind, stats.BackendMilliseconds, stats));
        }
        catch (Exception exception)
        {
            PublishFault(exception, "Frame rendering failed.");
            throw;
        }
        finally
        {
            _rendering = false;
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        try
        {
            _renderer.Deinitialize(gl);
            _initialized = false;
            EngineLog3D.Information("OpenGL", "Renderer deinitialized.");
        }
        catch (Exception exception)
        {
            EngineLog3D.Error("OpenGL", "Renderer deinitialization failed.", exception);
            throw;
        }
        finally
        {
            base.OnOpenGlDeinit(gl);
        }
    }

    protected override void OnOpenGlLost()
    {
        EngineLog3D.Warning("OpenGL", "Graphics context was lost; GPU resource state was invalidated.");
        _initialized = false;
        _renderer.Reset();
        base.OnOpenGlLost();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            RequestRender();
        }
    }

    private void PublishFault(Exception exception, string message)
    {
        if (_fatalFaultPublished) return;
        _fatalFaultPublished = true;
        _lastFault = exception;
        System.Threading.Interlocked.Increment(ref _faultCount);
        System.Threading.Volatile.Write(ref _lastFaultTimestamp, Stopwatch.GetTimestamp());
        EngineLog3D.Critical("OpenGL", message, exception);
        try { Faulted?.Invoke(this, new ScenePresenterFaultedEventArgs3D(exception, CapturePresenterSnapshot())); }
        catch (Exception subscriberException) { EngineLog3D.Error("OpenGL", "Presenter Faulted subscriber failed.", subscriberException); }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
    }
}
