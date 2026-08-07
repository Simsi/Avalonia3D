using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Avalonia.Rendering;
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Avalonia.WebGL.Rendering;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Rhi;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Avalonia.WebGL.Controls;

internal sealed partial class WebGlScenePresenter : Control, IRhiCommandExecutor3D, IScenePresenter, IScenePresenterDiagnostics3D, IBrowserDiagnosticExportPresenter3D, IPerformanceMetricsOverlayPresenter, ICenterCursorOverlayPresenter, IPointerLockPresenter, IBrowserPageVisibilityPresenter
{
    private const string ControlTextureKeyPrefix = "control-texture:";
    private readonly Dictionary<string, long> _textureVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RhiResourceHandle3D> _textureHandles = new(StringComparer.Ordinal);
    private readonly GpuDeferredReleaseQueue3D<DeferredTextureRelease> _deferredTextureReleases = new();
    private readonly string _rhiResourceOwnerId = "webgl-presenter:" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, long> _meshGeometryVersions = new();
    private readonly Dictionary<string, bool> _meshWireframeUploaded = new(StringComparer.Ordinal);
    private Scene3D? _scene;
    private int _hostId = -1;
    private bool _moduleReady;
    private bool _initializing;
    private bool _renderPending;
    private bool _invalidateScheduled;
    private bool _rendering;
    private bool _frameRenderedDispatchScheduled;
    private bool _attached;
    private bool _disposed;
    private long _lastSweptUploadRegistryVersion = -1;
    private long _lastSweptUploadBatchContentVersion = -1;
    private string? _performanceMetricsText;
    private bool _performanceMetricsVisible;
    private bool _centerCursorVisible;
    private double _lastHostX = double.NaN;
    private double _lastHostY = double.NaN;
    private double _lastHostWidth = double.NaN;
    private double _lastHostHeight = double.NaN;
    private bool _lastHostVisible;
    private string? _lastMetricsText;
    private bool _lastMetricsVisible;
    private bool _lastCenterCursorVisible;
    private readonly WebGlRetainedOrdinaryRenderer _retainedOrdinary = new();
    private readonly WebGlRetainedParticleRenderer _retainedParticles = new();
    private readonly WebGlRetainedHighScaleRenderer _retainedHighScale = new();
    private readonly WebGlClientHighScaleRenderer _clientHighScale = new();
    private readonly List<WebGlRetainedBatchPacket> _retainedBatches = new(256);
    private readonly List<WebGlRetainedBatchPacket> _retainedBatchPacketPool = new(256);
    private readonly Dictionary<string, WebGlRetainedBatchPacket> _ordinaryPacketMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WebGlRetainedBatchPacket> _particlePacketMap = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<WebGlRetainedBatchPacket>> _highScalePacketsByDrawOrder = new();
    private readonly List<List<WebGlRetainedBatchPacket>> _highScalePacketListPool = new(32);
    private int _retainedBatchPacketPoolCursor;
    private int _highScalePacketListPoolCursor;
    private readonly SceneRenderFrameScratch3D _renderFrameScratch = new();
    private readonly SceneRenderPlanScratch3D _renderPlanScratch = new();
    private readonly Dictionary<string, byte[]> _controlTexturePixelBuffers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveTextureSweepScratch = new(StringComparer.Ordinal);
    private readonly List<string> _sweepRemovalScratch = new(64);
    private readonly List<ControlPlaneRenderItem3D> _controlPlaneItems = new(16);
    private readonly List<ControlPlaneUploadRecord> _controlPlaneRecords = new(16);
    private byte[] _controlPlanePlaneBytes = Array.Empty<byte>();
    private byte[] _retainedDrawOrderBytes = Array.Empty<byte>();
    private readonly byte[] _viewProjectionBytes = new byte[16 * sizeof(float)];
    private readonly byte[] _cameraBytes = new byte[12 * sizeof(float)];
    private readonly byte[] _lightingBytes = new byte[33 * sizeof(float)];
    private readonly byte[] _styleBytes = new byte[30 * sizeof(float)];
    private ulong _lastOrdinaryDrawListVersion = ulong.MaxValue;
    private ulong _lastParticleDrawListVersion = ulong.MaxValue;
    private ulong _lastHighScaleDrawListVersion = ulong.MaxValue;
    private bool _lastCombinedHighScaleClientRuntime;
    private ulong _combinedDrawListVersion;
    private ulong _lastRetainedDrawOrderVersion;
    private ulong _lastControlPlaneUploadVersion;
    private int _cachedCubemapCsvVersion = -1;
    private string _cachedCubemapCsv = string.Empty;
    private int _renderFailureCount;
    private long _renderRequestCount;
    private long _renderedFrameCount;
    private long _faultCount;
    private long _lastRequestTimestamp;
    private long _lastFrameTimestamp;
    private long _lastFaultTimestamp;
    private Exception? _lastFault;
    private bool _fatalFaultPublished;
    private RhiDevice3D? _rhiDevice;
    private EngineResourceConfiguration3D? _resourceConfiguration;
    private SceneFrameRenderedEventArgs? _pendingFrameRendered;
    private readonly Action _applyScheduledInvalidation;
    private readonly Action _dispatchPendingFrameRendered;

    public WebGlScenePresenter()
    {
        _applyScheduledInvalidation = ApplyScheduledInvalidation;
        _dispatchPendingFrameRendered = DispatchPendingFrameRendered;
        Focusable = false;
        ClipToBounds = true;
        //Background = Brushes.Transparent;
        LayoutUpdated += OnLayoutUpdated;
    }

    public event EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;
    public event EventHandler<ScenePresenterFaultedEventArgs3D>? Faulted;

    public BackendKind Kind => BackendKind.WebGlBrowser;
    public IRenderDeviceDiagnostics3D? RenderDevice => _rhiDevice;
    public Control View => this;

    public Scene3D Scene
    {
        get => _scene ?? throw new InvalidOperationException("A scene must be assigned before rendering.");
        set
        {
            ThrowIfDisposed();
            if (_scene is not null)
            {
                _scene.SceneChangedDetailed -= OnSceneChangedDetailed;
            }

            if (value is null) throw new ArgumentNullException(nameof(value));
            var nextConfiguration = value.Engine.Configuration.Resources;
            if (_hostId >= 0 && _resourceConfiguration is not null && !Equals(_resourceConfiguration, nextConfiguration))
            {
                DestroyHost();
            }
            _resourceConfiguration = nextConfiguration;
            _scene = value;
            _scene.SceneChangedDetailed += OnSceneChangedDetailed;
            InvalidateRetainedDrawListCache();
            _lastControlPlaneUploadVersion = 0UL;
            _cachedCubemapCsvVersion = -1;
            _cachedCubemapCsv = string.Empty;
            if (_hostId >= 0)
            {
                _retainedOrdinary.Reset(_hostId);
                _retainedParticles.Reset(_hostId);
                _retainedHighScale.Reset(_hostId);
                _clientHighScale.Reset(_hostId);
            }
            RequestRender();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LayoutUpdated -= OnLayoutUpdated;
        if (_scene is not null)
        {
            _scene.SceneChangedDetailed -= OnSceneChangedDetailed;
            _scene = null;
        }

        DestroyHost();
        _pendingFrameRendered = null;
        FrameRendered = null;
        Faulted = null;
        EngineLog3D.Information("WebGL", "Presenter disposed; host and scene references released.");
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_disposed && _moduleReady && _hostId >= 0)
        {
            UpdateHostRect();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void SetPerformanceMetricsOverlay(string? text, bool visible)
    {
        _performanceMetricsText = text;
        _performanceMetricsVisible = visible && !string.IsNullOrWhiteSpace(text);
        if (_moduleReady && _hostId >= 0)
        {
            UpdateMetricsIfChanged(force: false);
        }
    }

    public bool SupportsPointerLock => true;

    public bool IsDocumentHidden => _moduleReady && WebGlInterop.IsDocumentHidden();

    public int DocumentVisibilityVersion => _moduleReady ? WebGlInterop.GetDocumentVisibilityVersion() : 0;

    public bool IsPointerLockActive => _moduleReady && _hostId >= 0 && WebGlInterop.IsPointerLockActive(_hostId);

    public void SetCenterCursorOverlay(bool visible)
    {
        _centerCursorVisible = visible;
        if (_moduleReady && _hostId >= 0)
        {
            UpdateCenterCursorIfChanged(force: false);
        }
    }

    public void RequestPointerLock()
    {
        if (_moduleReady && _hostId >= 0)
        {
            WebGlInterop.RequestPointerLock(_hostId);
        }
    }

    public void ExitPointerLock()
    {
        if (_moduleReady && _hostId >= 0)
        {
            WebGlInterop.ExitPointerLock(_hostId);
        }
    }

    public bool TryConsumePointerDelta(out Vector2 delta)
    {
        delta = Vector2.Zero;
        if (!_moduleReady || _hostId < 0)
        {
            return false;
        }

        var x = (float)WebGlInterop.ConsumePointerDeltaX(_hostId);
        var y = (float)WebGlInterop.ConsumePointerDeltaY(_hostId);
        delta = new Vector2(x, y);
        return delta.LengthSquared() > 0.000001f;
    }

    private void OnSceneChangedDetailed(object? sender, SceneChangedEventArgs e)
    {
        // Particle simulations advance through Transform changes because bounds/instance data move.
        // Route those changes only to the particle retained renderer; otherwise a particle-heavy
        // scene invalidates ordinary mesh batches every tick even though particles are excluded
        // from the ordinary renderer.
        var particleOnlyKinds = SceneChangeFlags3D.Transform | SceneChangeFlags3D.Geometry;
        if (e.Source is ParticleSystem3D &&
            (e.Kinds & particleOnlyKinds) != 0 &&
            (e.Kinds & ~particleOnlyKinds) == 0)
        {
            _retainedParticles.MarkDirty(e);
        }
        else
        {
            _retainedOrdinary.MarkDirty(e);
            _retainedParticles.MarkDirty(e);
            _retainedHighScale.MarkDirty(e);
        }

        if (e.Contains(SceneChangeKind.Structure) ||
            e.Contains(SceneChangeKind.Visibility) ||
            e.Contains(SceneChangeKind.Control))
        {
            _lastControlPlaneUploadVersion = 0UL;
        }
    }

    public void RequestRender()
    {
        if (_disposed) return;
        if (_attached && !_moduleReady && !_initializing) _ = EnsureHostAsync();
        System.Threading.Interlocked.Increment(ref _renderRequestCount);
        System.Threading.Volatile.Write(ref _lastRequestTimestamp, Stopwatch.GetTimestamp());

        if (_renderPending) return;
        _renderPending = true;
        if (Dispatcher.UIThread.CheckAccess() && !_rendering)
        {
            InvalidateVisual();
            return;
        }

        ScheduleInvalidateVisual();
    }

    public ScenePresenterSnapshot3D CapturePresenterSnapshot()
        => new(Kind, _attached, _moduleReady && _hostId >= 0, _disposed, _rendering, _renderPending,
            System.Threading.Interlocked.Read(ref _renderRequestCount),
            System.Threading.Interlocked.Read(ref _renderedFrameCount),
            System.Threading.Interlocked.Read(ref _faultCount),
            System.Threading.Volatile.Read(ref _lastRequestTimestamp),
            System.Threading.Volatile.Read(ref _lastFrameTimestamp),
            System.Threading.Volatile.Read(ref _lastFaultTimestamp),
            $"host={_hostId}; moduleReady={_moduleReady}; initializing={_initializing}; invalidateScheduled={_invalidateScheduled}; failures={_renderFailureCount}; bounds={Bounds.Width:0.##}x{Bounds.Height:0.##}; visible={IsVisible}",
            _lastFault?.GetType().FullName, _lastFault?.Message);

    public void ResetFaultState()
    {
        _fatalFaultPublished = false;
        _lastFault = null;
        _renderFailureCount = 0;
        _renderPending = false;
        _pendingFrameRendered = null;
        if (_attached && !_moduleReady && !_initializing) _ = EnsureHostAsync();
    }

    public void ExportTextFile(string fileName, string text)
    {
        if (!_moduleReady) throw new InvalidOperationException("The WebGL module is not initialized.");
        WebGlInterop.DownloadTextFile(fileName, text);
    }

    private void ScheduleInvalidateVisual()
    {
        if (_disposed || _invalidateScheduled)
        {
            return;
        }

        _invalidateScheduled = true;
        Dispatcher.UIThread.Post(_applyScheduledInvalidation, DispatcherPriority.Render);
    }

    private void ApplyScheduledInvalidation()
    {
        _invalidateScheduled = false;
        if (!_disposed)
        {
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_disposed) return;
        _attached = true;
        EngineLog3D.Information("WebGL.Lifecycle", $"Presenter attached; bounds={Bounds.Width:0.##}x{Bounds.Height:0.##}.");
        _ = EnsureHostAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        EngineLog3D.Information("WebGL.Lifecycle", "Presenter detached; destroying browser host.");
        DestroyHost();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!_attached || !_moduleReady || _hostId < 0)
        {
            return;
        }

        _rendering = true;
        try
        {
            UpdateHostRect();

            if (_renderPending)
            {
                _renderPending = false;
                RenderToWebGl();
            }
        }
        catch (Exception exception)
        {
            PublishFault(exception, "WebGL presentation callback failed.");
        }
        finally
        {
            _rendering = false;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == IsVisibleProperty)
        {
            RequestRender();
        }
    }

    private async System.Threading.Tasks.Task EnsureHostAsync()
    {
        if (_disposed || !_attached || _initializing || _moduleReady)
        {
            return;
        }

        _initializing = true;
        try
        {
            await WebGlInterop.EnsureImportedAsync();
            if (_disposed || !_attached)
            {
                return;
            }

            _hostId = WebGlInterop.CreateHost();
            _rhiDevice = CreateRhiDevice(_hostId, _resourceConfiguration ?? Scene.Engine.Configuration.Resources);
            RegisterStaticRhiResources();
            _moduleReady = true;
            ResetFaultState();
            EngineLog3D.Information("WebGL", $"Backend initialized; host={_hostId}.");
            UpdateHostRect(force: true);
            UpdateMetricsIfChanged(force: true);
            UpdateCenterCursorIfChanged(force: true);
            // Requests made while the module was loading are represented by _renderPending,
            // but their early Avalonia invalidation may already have been consumed. Re-arm
            // exactly one invalidation now that a live host exists.
            _renderPending = false;
            RequestRender();
        }
        catch (Exception ex)
        {
            PublishFault(ex, "Backend initialization failed.");
            DestroyHost();
        }
        finally
        {
            _initializing = false;
        }
    }

    private void RenderToWebGl()
    {
        try
        {
            RenderToWebGlCore();
            _renderFailureCount = 0;
        }
        catch (Exception ex)
        {
            _rhiDevice?.AbortFrame();
            HandleRenderFailure(ex);
        }
    }

    private void RenderToWebGlCore()
    {
        var scene = Scene;
        if (_hostId < 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        if (WebGlInterop.ConsumeContextResetFlag(_hostId))
        {
            EngineLog3D.Warning("WebGL", $"Graphics context reset detected for host {_hostId}; GPU resources will be recreated.");
            _rhiDevice?.InvalidateContext("browser reported webglcontextrestored");
            _rhiDevice?.Dispose();
            _rhiDevice = CreateRhiDevice(_hostId, _resourceConfiguration ?? scene.Engine.Configuration.Resources);
            RegisterStaticRhiResources();
            ResetUploadCachesAfterContextRestore();
        }

        DrainDeferredTextureReleases(_rhiDevice?.FrameIndex ?? 0);
        var start = Stopwatch.GetTimestamp();
        var width = (float)global::System.Math.Max(Bounds.Width, 1d);
        var height = (float)global::System.Math.Max(Bounds.Height, 1d);
        using var frame = _renderFrameScratch.Begin(scene, width, height, BackendKind.WebGlBrowser);
        var snapshot = frame.Snapshot;
        var viewProjection = frame.ViewProjection;
        var pipeline = frame.Pipeline;
        var stats = frame.CreateBaseStats();
        var device = _rhiDevice ?? throw new InvalidOperationException("WebGL RHI device is not initialized.");
        var gpuSkinningSupported = device.Capabilities.Supports(RhiFeature3D.VertexTextureFetch | RhiFeature3D.FloatTextures);
        EnsureGpuSkinningAvailable(snapshot, device);
        var ordinaryNeedsPlan = _retainedOrdinary.RequiresScenePlan(frame);
        var particleNeedsPlan = _retainedParticles.RequiresScenePlan(frame);
        var plan = SceneRenderPlanBuilder3D.Build(
            frame,
            _renderPlanScratch,
            stats: stats,
            includeOrdinary: ordinaryNeedsPlan,
            includeParticles: particleNeedsPlan,
            includeHighScale: true);
        device.BeginFrame(plan.RhiSubmission);
        var hasVisibleHighScale = plan.HasVisibleHighScale;
        var useClientHighScale = hasVisibleHighScale &&
            scene.Performance.EnableRetainedInstanceBuffers &&
            scene.Performance.EnableWebGlClientHighScaleRuntime;

        if (scene.Debug.ShowPerformanceMetrics)
        {
            ApplyAnimationStats(stats, snapshot, gpuSkinningActive: gpuSkinningSupported);
        }

        SweepUnusedUploadState(scene, plan.Resources, snapshot);
        SceneRenderStats3D.ApplyPipelineStats(stats, scene, pipeline);

        var uploadStart = Stopwatch.GetTimestamp();
        UploadDirtyMeshGeometry(plan.Resources, stats);
        UploadDirtyControlTextures(snapshot, stats);
        UploadDirtyResourceTextures(plan.Resources, stats);
        stats.UploadMilliseconds = GetElapsedMilliseconds(uploadStart);

        var packetStart = Stopwatch.GetTimestamp();
        if (useClientHighScale)
        {
            try
            {
                _clientHighScale.SyncFrame(_hostId, scene, plan.HighScaleLayers, width, height, viewProjection, stats);
            }
            catch (Exception ex)
            {
                EngineLog3D.Critical("WebGL.HighScale", "Client-owned GPU high-scale runtime failed. The frame is aborted; no runtime fallback is permitted.", ex);
                throw new InvalidOperationException("WebGL client-owned high-scale runtime failed.", ex);
            }
        }
        else if (_clientHighScale.HasRuntimeState)
        {
            _clientHighScale.Reset(_hostId);
        }

        var ordinaryDraws = _retainedOrdinary.BuildAndUpload(_hostId, plan, stats);
        var particleDraws = _retainedParticles.BuildAndUpload(_hostId, plan, stats);
        List<WebGlRetainedBatchPacket>? highScaleDraws = null;

        // Retained high-scale is an explicitly selected configuration path. Runtime failure
        // of the client-owned path is never allowed to switch to it silently.
        if (hasVisibleHighScale && !useClientHighScale)
        {
            highScaleDraws = _retainedHighScale.BuildAndUpload(_hostId, plan, stats);
        }
        else if (_retainedHighScale.HasRuntimeState)
        {
            _retainedHighScale.Reset(_hostId);
        }

        RebuildRetainedDrawListIfNeeded(plan, ordinaryDraws, particleDraws, highScaleDraws, useClientHighScale);
        UploadRetainedDrawOrderIfNeeded(_retainedBatches, stats);
        UploadControlPlanesIfNeeded(snapshot, stats);
        var retainedFrameState = CaptureRetainedFrameState(frame);
        var showPerformanceMetrics = scene.Debug.ShowPerformanceMetrics;
        frame.ReleaseSceneAccess();
        // RHI ForwardScene executes RenderRetainedFrameDirect(stats, in retainedFrameState) only after this release.
        ExecuteRhiFrame(plan, stats, in retainedFrameState, device);
        if (showPerformanceMetrics)
        {
            if (useClientHighScale)
            {
                WebGlClientHighScaleRenderer.ApplyJsMetrics(_hostId, stats);
            }
            ApplyWebGlStateMetrics(_hostId, stats);
        }
        device.ApplyStats(stats);
        stats.PacketBuildMilliseconds = GetElapsedMilliseconds(packetStart);
        stats.BackendMilliseconds = GetElapsedMilliseconds(start);

        _fatalFaultPublished = false;
        _lastFault = null;
        System.Threading.Interlocked.Increment(ref _renderedFrameCount);
        System.Threading.Volatile.Write(ref _lastFrameTimestamp, Stopwatch.GetTimestamp());
        _pendingFrameRendered = new SceneFrameRenderedEventArgs(Kind, stats.BackendMilliseconds, stats);
        if (!_frameRenderedDispatchScheduled)
        {
            _frameRenderedDispatchScheduled = true;
            Dispatcher.UIThread.Post(_dispatchPendingFrameRendered, DispatcherPriority.Render);
        }
    }

    private void DispatchPendingFrameRendered()
    {
        _frameRenderedDispatchScheduled = false;
        var renderedFrame = _pendingFrameRendered;
        _pendingFrameRendered = null;
        if (_disposed || renderedFrame is null)
        {
            return;
        }

        try
        {
            FrameRendered?.Invoke(this, renderedFrame);
        }
        catch (Exception ex)
        {
            EngineLog3D.Error("WebGL", "FrameRendered subscriber failed.", ex);
        }
    }


    private void InvalidateRetainedDrawListCache()
    {
        _lastRetainedDrawOrderVersion = 0UL;
        _lastOrdinaryDrawListVersion = ulong.MaxValue;
        _lastParticleDrawListVersion = ulong.MaxValue;
        _lastHighScaleDrawListVersion = ulong.MaxValue;
        _combinedDrawListVersion++;
    }

    private void RebuildRetainedDrawListIfNeeded(
        SceneRenderPlan3D plan,
        List<WebGlRetainedBatchPacket> ordinaryDraws,
        List<WebGlRetainedBatchPacket> particleDraws,
        List<WebGlRetainedBatchPacket>? highScaleDraws,
        bool useClientHighScale)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var highScaleVersion = useClientHighScale
            ? _clientHighScale.Version ^ ComputeClientHighScaleDrawOrderVersion(plan)
            : highScaleDraws is null ? 0UL : _retainedHighScale.Version ^ ComputeRetainedDrawOrderVersion(highScaleDraws);
        if (_lastOrdinaryDrawListVersion == _retainedOrdinary.Version &&
            _lastParticleDrawListVersion == _retainedParticles.Version &&
            _lastHighScaleDrawListVersion == highScaleVersion &&
            _lastCombinedHighScaleClientRuntime == useClientHighScale)
        {
            return;
        }

        _retainedBatches.Clear();
        _retainedBatchPacketPoolCursor = 0;
        BuildUnifiedRetainedDrawList(plan, ordinaryDraws, particleDraws, highScaleDraws, useClientHighScale, _retainedBatches);
        _retainedBatches.Sort(CompareRetainedBatchDrawOrder);

        _lastOrdinaryDrawListVersion = _retainedOrdinary.Version;
        _lastParticleDrawListVersion = _retainedParticles.Version;
        _lastHighScaleDrawListVersion = highScaleVersion;
        _lastCombinedHighScaleClientRuntime = useClientHighScale;
        _combinedDrawListVersion++;
    }


    private void BuildUnifiedRetainedDrawList(
        SceneRenderPlan3D plan,
        List<WebGlRetainedBatchPacket> ordinaryDraws,
        List<WebGlRetainedBatchPacket> particleDraws,
        List<WebGlRetainedBatchPacket>? highScaleDraws,
        bool useClientHighScale,
        List<WebGlRetainedBatchPacket> output)
    {
        Dictionary<string, WebGlRetainedBatchPacket>? ordinaryById = null;
        Dictionary<string, WebGlRetainedBatchPacket>? particleById = null;
        Dictionary<int, List<WebGlRetainedBatchPacket>>? retainedHighScaleByOrder = null;
        if (plan.IncludesOrdinary)
        {
            ordinaryById = BuildPacketMap(ordinaryDraws, _ordinaryPacketMap);
        }
        if (plan.IncludesParticles)
        {
            particleById = BuildPacketMap(particleDraws, _particlePacketMap);
        }
        if (!useClientHighScale && highScaleDraws is not null)
        {
            retainedHighScaleByOrder = BuildPacketsByDrawOrder(highScaleDraws);
        }

        if (!plan.IncludesOrdinary)
        {
            AppendCachedPackets(output, ordinaryDraws);
        }

        if (!plan.IncludesParticles)
        {
            AppendCachedPackets(output, particleDraws);
        }

        for (var i = 0; i < plan.DrawCommands.Count; i++)
        {
            var command = plan.DrawCommands[i];
            switch (command.Kind)
            {
                case SceneRenderCommandKind3D.OrdinaryBatch:
                case SceneRenderCommandKind3D.TransparentOrdinaryItem:
                case SceneRenderCommandKind3D.TransparentOrdinaryBatch:
                    if (!plan.IncludesOrdinary || ordinaryById is null)
                    {
                        break;
                    }

                    var ordinaryId = command.Kind switch
                    {
                        SceneRenderCommandKind3D.OrdinaryBatch => command.OrdinaryBatch?.BatchId,
                        SceneRenderCommandKind3D.TransparentOrdinaryItem => command.TransparentOrdinary?.DrawId,
                        SceneRenderCommandKind3D.TransparentOrdinaryBatch => command.TransparentOrdinaryBatch?.BatchId,
                        _ => null
                    };
                    if (!string.IsNullOrEmpty(ordinaryId) && ordinaryById.TryGetValue(ordinaryId, out var ordinaryPacket))
                    {
                        output.Add(RentPacket(ordinaryPacket.Id, command.Transparent, ordinaryPacket.IsHighScaleLayer, command.SortDistanceSquared, command.SourceOrder));
                    }
                    break;

                case SceneRenderCommandKind3D.ParticleSystem:
                    if (!plan.IncludesParticles || particleById is null)
                    {
                        break;
                    }

                    var particleId = command.Particle?.RetainedBatchId;
                    if (!string.IsNullOrEmpty(particleId) && particleById.TryGetValue(particleId, out var particlePacket))
                    {
                        output.Add(RentPacket(particlePacket.Id, command.Transparent, particlePacket.IsHighScaleLayer, command.SortDistanceSquared, command.SourceOrder));
                    }
                    break;

                case SceneRenderCommandKind3D.HighScaleLayer:
                    if (useClientHighScale)
                    {
                        if (command.HighScaleLayer is not null)
                        {
                            output.Add(RentPacket(command.HighScaleLayer.Id, transparent: false, isHighScaleLayer: true, command.SortDistanceSquared, command.SourceOrder));
                        }
                    }
                    else if (retainedHighScaleByOrder is not null &&
                             retainedHighScaleByOrder.TryGetValue(command.SourceOrder, out var packets))
                    {
                        for (var p = 0; p < packets.Count; p++)
                        {
                            var packet = packets[p];
                            output.Add(RentPacket(packet.Id, packet.Transparent, packet.IsHighScaleLayer, command.SortDistanceSquared, command.SourceOrder));
                        }
                    }
                    break;
            }
        }
    }

    private static Dictionary<string, WebGlRetainedBatchPacket> BuildPacketMap(
        List<WebGlRetainedBatchPacket> packets,
        Dictionary<string, WebGlRetainedBatchPacket> map)
    {
        map.Clear();
        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            map[packet.Id] = packet;
        }

        return map;
    }

    private Dictionary<int, List<WebGlRetainedBatchPacket>> BuildPacketsByDrawOrder(List<WebGlRetainedBatchPacket> packets)
    {
        _highScalePacketsByDrawOrder.Clear();
        _highScalePacketListPoolCursor = 0;
        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            if (!_highScalePacketsByDrawOrder.TryGetValue(packet.DrawOrder, out var list))
            {
                list = RentHighScalePacketList();
                _highScalePacketsByDrawOrder[packet.DrawOrder] = list;
            }

            list.Add(packet);
        }

        return _highScalePacketsByDrawOrder;
    }

    private void AppendCachedPackets(List<WebGlRetainedBatchPacket> output, List<WebGlRetainedBatchPacket> packets)
    {
        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            output.Add(RentPacket(packet.Id, packet.Transparent, packet.IsHighScaleLayer, packet.SortDistanceSquared, packet.DrawOrder));
        }
    }

    private WebGlRetainedBatchPacket RentPacket(
        string id,
        bool transparent,
        bool isHighScaleLayer,
        float sortDistanceSquared,
        int drawOrder)
    {
        var index = _retainedBatchPacketPoolCursor++;
        while (_retainedBatchPacketPool.Count <= index)
        {
            _retainedBatchPacketPool.Add(new WebGlRetainedBatchPacket { Id = string.Empty });
        }

        var packet = _retainedBatchPacketPool[index];
        packet.Id = id;
        packet.Transparent = transparent;
        packet.IsHighScaleLayer = isHighScaleLayer;
        packet.SortDistanceSquared = sortDistanceSquared;
        packet.DrawOrder = drawOrder;
        return packet;
    }

    private List<WebGlRetainedBatchPacket> RentHighScalePacketList()
    {
        var index = _highScalePacketListPoolCursor++;
        while (_highScalePacketListPool.Count <= index)
        {
            _highScalePacketListPool.Add(new List<WebGlRetainedBatchPacket>());
        }

        var list = _highScalePacketListPool[index];
        list.Clear();
        return list;
    }

    private static int CompareRetainedBatchDrawOrder(WebGlRetainedBatchPacket? a, WebGlRetainedBatchPacket? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return 1;
        if (b is null) return -1;

        return SceneRenderDrawOrder3D.Compare(
            a.Transparent,
            a.SortDistanceSquared,
            a.DrawOrder,
            a.Id,
            b.Transparent,
            b.SortDistanceSquared,
            b.DrawOrder,
            b.Id);
    }


    private void UploadRetainedDrawOrderIfNeeded(List<WebGlRetainedBatchPacket> retainedBatches, RenderStats stats)
    {
        if (_combinedDrawListVersion == _lastRetainedDrawOrderVersion)
        {
            return;
        }

        _lastRetainedDrawOrderVersion = _combinedDrawListVersion;
        if (retainedBatches.Count == 0)
        {
            WebGlInterop.SetRetainedDrawOrderBytes(_hostId, 0, Array.Empty<byte>());
            return;
        }

        var byteCount = retainedBatches.Count * RetainedDrawOrderRecordByteSize;
        EnsureRetainedDrawOrderBuffer(byteCount);

        for (var i = 0; i < retainedBatches.Count; i++)
        {
            var packet = retainedBatches[i];
            var handle = RenderId3D.StableHash64(packet.Id);
            var offset = i * RetainedDrawOrderRecordByteSize;
            WriteUInt32(_retainedDrawOrderBytes, offset + 0, (uint)handle);
            WriteUInt32(_retainedDrawOrderBytes, offset + 4, (uint)(handle >> 32));
            var flags = (packet.Transparent ? 1u : 0u) | (packet.IsHighScaleLayer ? 2u : 0u);
            WriteUInt32(_retainedDrawOrderBytes, offset + 8, flags);
        }

        WebGlInterop.SetRetainedDrawOrderBytes(_hostId, retainedBatches.Count, _retainedDrawOrderBytes);
        stats.PacketBytes += byteCount;
    }

    private const int RetainedDrawOrderRecordByteSize = 12;

    private void EnsureRetainedDrawOrderBuffer(int byteCount)
    {
        if (byteCount <= 0)
        {
            return;
        }

        if (_retainedDrawOrderBytes.Length >= byteCount)
        {
            return;
        }

        var capacity = _retainedDrawOrderBytes.Length == 0 ? 512 : _retainedDrawOrderBytes.Length;
        while (capacity < byteCount)
        {
            capacity *= 2;
        }

        _retainedDrawOrderBytes = new byte[capacity];
    }

    private static ulong ComputeClientHighScaleDrawOrderVersion(SceneRenderPlan3D plan)
    {
        var hash = SceneRenderDrawOrder3D.CreateHashSeed();
        for (var i = 0; i < plan.DrawCommands.Count; i++)
        {
            var command = plan.DrawCommands[i];
            if (command.Kind != SceneRenderCommandKind3D.HighScaleLayer || command.HighScaleLayer is null)
            {
                continue;
            }

            hash = SceneRenderDrawOrder3D.HashPacket(
                hash,
                command.HighScaleLayer.Id,
                transparent: false,
                sortDistanceSquared: command.SortDistanceSquared,
                sourceOrder: command.SourceOrder,
                includeSourceOrder: true);
        }

        return hash;
    }

    private static ulong ComputeRetainedDrawOrderVersion(List<WebGlRetainedBatchPacket> retainedBatches)
    {
        var hash = SceneRenderDrawOrder3D.CreateHashSeed();
        for (var i = 0; i < retainedBatches.Count; i++)
        {
            var packet = retainedBatches[i];
            hash = SceneRenderDrawOrder3D.HashPacket(
                hash,
                packet.IsHighScaleLayer ? "hs:" + packet.Id : packet.Id,
                packet.Transparent,
                packet.SortDistanceSquared,
                packet.DrawOrder,
                includeSourceOrder: true);
        }

        return hash;
    }

    private void UploadControlPlanesIfNeeded(SceneFrameSnapshot3D snapshot, RenderStats stats)
    {
        _controlPlaneRecords.Clear();
        ControlPlaneRenderPlanner3D.Build(snapshot, Scene.Camera, _controlPlaneItems);
        for (var i = 0; i < _controlPlaneItems.Count; i++)
        {
            _controlPlaneRecords.Add(ControlPlaneUploadRecord.FromItem(_controlPlaneItems[i]));
        }

        var count = _controlPlaneRecords.Count;
        stats.ControlPlaneCount = count;
        var version = ComputeControlPlaneUploadVersion(_controlPlaneRecords);
        if (version == _lastControlPlaneUploadVersion)
        {
            return;
        }

        _lastControlPlaneUploadVersion = version;
        EnsureControlPlaneBuffers(count);

        for (var i = 0; i < count; i++)
        {
            WriteControlPlaneRecord(_controlPlanePlaneBytes, i * ControlPlaneUploadRecord.FloatStride * sizeof(float), _controlPlaneRecords[i]);
        }

        WebGlInterop.SetRetainedControlPlanesDirect(
            _hostId,
            BuildControlPlaneIdList(_controlPlaneRecords),
            count,
            _controlPlanePlaneBytes);

    }

    private void EnsureControlPlaneBuffers(int count)
    {
        var byteCount = Math.Max(0, count) * ControlPlaneUploadRecord.FloatStride * sizeof(float);
        if (byteCount <= 0 || _controlPlanePlaneBytes.Length >= byteCount)
        {
            return;
        }

        var capacity = _controlPlanePlaneBytes.Length == 0 ? 1024 : _controlPlanePlaneBytes.Length;
        while (capacity < byteCount)
        {
            capacity *= 2;
        }

        _controlPlanePlaneBytes = new byte[capacity];
    }

    private static string BuildControlPlaneIdList(List<ControlPlaneUploadRecord> records)
    {
        if (records.Count == 0) return string.Empty;
        var length = Math.Max(0, records.Count - 1);
        for (var i = 0; i < records.Count; i++)
        {
            length += records[i].Id.Length;
        }

        return string.Create(length, records, static (span, state) =>
        {
            var pos = 0;
            for (var i = 0; i < state.Count; i++)
            {
                if (i > 0) span[pos++] = '\n';
                var id = state[i].Id;
                for (var c = 0; c < id.Length; c++)
                {
                    var ch = id[c];
                    span[pos++] = ch == '\n' || ch == '\r' ? '_' : ch;
                }
            }
        });
    }

    private static ulong ComputeControlPlaneUploadVersion(List<ControlPlaneUploadRecord> records)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            for (var i = 0; i < records.Count; i++)
            {
                var r = records[i];
                hash = HashString(hash, r.Id);
                hash = HashFloat(hash, r.AlwaysFaceCamera ? 1f : 0f);
                hash = HashFloat(hash, r.Center.X);
                hash = HashFloat(hash, r.Center.Y);
                hash = HashFloat(hash, r.Center.Z);
                hash = HashFloat(hash, r.ExtentX);
                hash = HashFloat(hash, r.ExtentY);
                hash = HashFloat(hash, r.RollRadians);
                if (!r.AlwaysFaceCamera)
                {
                    hash = HashVector(hash, r.Corner0);
                    hash = HashVector(hash, r.Corner1);
                    hash = HashVector(hash, r.Corner2);
                    hash = HashVector(hash, r.Corner3);
                }
            }

            return hash;
        }
    }

    private static ulong HashString(ulong hash, string value)
    {
        unchecked
        {
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }

            return hash;
        }
    }

    private static ulong HashFloat(ulong hash, float value)
    {
        unchecked
        {
            var bits = (uint)BitConverter.SingleToInt32Bits(value);
            hash ^= bits & 0xFFUL;
            hash *= 1099511628211UL;
            hash ^= (bits >> 8) & 0xFFUL;
            hash *= 1099511628211UL;
            hash ^= (bits >> 16) & 0xFFUL;
            hash *= 1099511628211UL;
            hash ^= (bits >> 24) & 0xFFUL;
            hash *= 1099511628211UL;
            return hash;
        }
    }

    private static ulong HashVector(ulong hash, Vector3 value)
    {
        hash = HashFloat(hash, value.X);
        hash = HashFloat(hash, value.Y);
        hash = HashFloat(hash, value.Z);
        return hash;
    }

    private static void WriteControlPlaneRecord(byte[] destination, int byteOffset, ControlPlaneUploadRecord record)
    {
        WriteFloat(destination, byteOffset + 0 * sizeof(float), record.AlwaysFaceCamera ? 1f : 0f);
        WriteFloat(destination, byteOffset + 1 * sizeof(float), record.Center.X);
        WriteFloat(destination, byteOffset + 2 * sizeof(float), record.Center.Y);
        WriteFloat(destination, byteOffset + 3 * sizeof(float), record.Center.Z);
        WriteFloat(destination, byteOffset + 4 * sizeof(float), record.ExtentX);
        WriteFloat(destination, byteOffset + 5 * sizeof(float), record.ExtentY);
        WriteFloat(destination, byteOffset + 6 * sizeof(float), record.RollRadians);
        WriteFloat(destination, byteOffset + 7 * sizeof(float), 0f);
        WriteVector3(destination, byteOffset + 8 * sizeof(float), record.Corner0);
        WriteVector3(destination, byteOffset + 11 * sizeof(float), record.Corner1);
        WriteVector3(destination, byteOffset + 14 * sizeof(float), record.Corner2);
        WriteVector3(destination, byteOffset + 17 * sizeof(float), record.Corner3);
    }

    private static void WriteVector3(byte[] destination, int byteOffset, Vector3 value)
    {
        WriteFloat(destination, byteOffset + 0, value.X);
        WriteFloat(destination, byteOffset + 4, value.Y);
        WriteFloat(destination, byteOffset + 8, value.Z);
    }



    private static void WriteUInt32(byte[] destination, int byteOffset, uint value)
    {
        destination[byteOffset + 0] = (byte)value;
        destination[byteOffset + 1] = (byte)(value >> 8);
        destination[byteOffset + 2] = (byte)(value >> 16);
        destination[byteOffset + 3] = (byte)(value >> 24);
    }

    private static void WriteFloat(byte[] destination, int byteOffset, float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        WriteUInt32(destination, byteOffset, (uint)bits);
    }

    private RetainedFrameState CaptureRetainedFrameState(SceneRenderFrameContext3D frame)
    {
        var scene = frame.Scene;
        var skybox = scene.Environment.Skybox;
        return new RetainedFrameState(
            frame.Pipeline,
            frame.Width,
            frame.Height,
            frame.ViewProjection,
            SceneLightingResolver3D.Resolve(scene),
            scene.Camera.Position,
            scene.Camera.Right,
            scene.Camera.SafeUp,
            scene.Camera.Forward,
            scene.Camera.FieldOfViewDegrees,
            scene.BackgroundColor,
            skybox.Mode,
            skybox.TopColor,
            skybox.HorizonColor,
            skybox.BottomColor,
            skybox.Intensity,
            skybox.HasEquirectangularTexture ? skybox.EquirectangularTextureResourceKey ?? string.Empty : string.Empty,
            BuildCubemapCsv(skybox),
            scene.RenderPipeline.ToneMapping.Exposure,
            scene.RenderPipeline.ToneMapping.Gamma,
            scene.RenderPipeline.Ssao.Strength,
            scene.RenderPipeline.Ssao.Radius,
            scene.RenderPipeline.Ssao.Bias,
            scene.RenderPipeline.Ssao.SampleCount,
            scene.Debug.ShowWireframeOverlay,
            scene.Debug.ShowSilhouetteOverlay);
    }

    private void RenderRetainedFrameDirect(RenderStats stats, in RetainedFrameState state)
    {
        var pipeline = state.Pipeline;
        var width = state.Width;
        var height = state.Height;
        var viewProjection = state.ViewProjection;
        var lighting = state.Lighting;

        Span<float> view = stackalloc float[16];
        WriteMatrix(view, viewProjection);
        Span<float> camera = stackalloc float[12];
        WriteVector3(camera, 0, state.CameraPosition);
        WriteVector3(camera, 3, state.CameraRight);
        WriteVector3(camera, 6, state.CameraUp);
        WriteVector3(camera, 9, state.CameraForward);

        Span<float> light = stackalloc float[33];
        WriteVector3(light, 0, lighting.Ambient);
        WriteVector3(light, 3, lighting.DirectionalDirection);
        WriteVector3(light, 6, lighting.DirectionalColor);
        WriteVector4(light, 9, lighting.PointPosition);
        WriteVector4(light, 13, lighting.PointColor);
        WriteVector4(light, 17, lighting.SpotPosition);
        WriteVector4(light, 21, lighting.SpotDirection);
        WriteVector4(light, 25, lighting.SpotColor);
        WriteVector4(light, 29, lighting.SpotCone);

        Span<float> style = stackalloc float[30];
        WriteColor(style, 0, state.BackgroundColor);
        WriteVector3(style, 4, state.SkyboxTopColor.ToVector3());
        WriteVector3(style, 7, state.SkyboxHorizonColor.ToVector3());
        WriteVector3(style, 10, state.SkyboxBottomColor.ToVector3());
        style[13] = state.SkyboxIntensity;
        style[14] = state.Exposure;
        style[15] = state.Gamma;
        style[16] = state.SsaoStrength;
        style[17] = state.SsaoRadius;
        style[18] = state.SsaoBias;
        style[19] = state.SsaoSampleCount;
        var verticalProjectionScale = MathF.Tan(state.FieldOfViewDegrees * (MathF.PI / 360f));
        style[22] = verticalProjectionScale * MathF.Max(width, 1f) / MathF.Max(height, 1f);
        style[23] = verticalProjectionScale;

        var flags = 0;
        if (state.SkyboxMode != SkyboxMode3D.None) flags |= 1;
        if (pipeline.SsaoActive) flags |= 2;
        if (pipeline.ToneMappingActive) flags |= 4;
        if (state.ShowWireframeOverlay) flags |= 8;
        if (state.ShowSilhouetteOverlay) flags |= 16;

        WebGlInterop.RenderRetainedSceneFrameDirect(
            _hostId,
            width,
            height,
            flags,
            (int)state.SkyboxMode,
            (int)pipeline.ToneMappingMode,
            state.EquirectangularTextureResourceKey,
            state.CubemapResourceKeys,
            CopyFloatsToFrameBuffer(view, _viewProjectionBytes),
            CopyFloatsToFrameBuffer(camera, _cameraBytes),
            CopyFloatsToFrameBuffer(light, _lightingBytes),
            CopyFloatsToFrameBuffer(style, _styleBytes));
    }

    private readonly record struct RetainedFrameState(
        RenderPipelinePlan3D Pipeline,
        float Width,
        float Height,
        Matrix4x4 ViewProjection,
        SceneLightingSnapshot3D Lighting,
        Vector3 CameraPosition,
        Vector3 CameraRight,
        Vector3 CameraUp,
        Vector3 CameraForward,
        float FieldOfViewDegrees,
        ColorRgba BackgroundColor,
        SkyboxMode3D SkyboxMode,
        ColorRgba SkyboxTopColor,
        ColorRgba SkyboxHorizonColor,
        ColorRgba SkyboxBottomColor,
        float SkyboxIntensity,
        string EquirectangularTextureResourceKey,
        string CubemapResourceKeys,
        float Exposure,
        float Gamma,
        float SsaoStrength,
        float SsaoRadius,
        float SsaoBias,
        int SsaoSampleCount,
        bool ShowWireframeOverlay,
        bool ShowSilhouetteOverlay);

    private string BuildCubemapCsv(Skybox3D skybox)
    {
        if (!skybox.HasCubemapTextures)
        {
            _cachedCubemapCsvVersion = -1;
            _cachedCubemapCsv = string.Empty;
            return string.Empty;
        }

        if (_cachedCubemapCsvVersion == skybox.EnvironmentTextureVersion)
        {
            return _cachedCubemapCsv;
        }

        _cachedCubemapCsvVersion = skybox.EnvironmentTextureVersion;
        _cachedCubemapCsv = string.Join("\n", skybox.CubemapTextureResourceKeys);
        return _cachedCubemapCsv;
    }

    private static void WriteMatrix(Span<float> buffer, Matrix4x4 matrix)
    {
        buffer[0] = matrix.M11; buffer[1] = matrix.M12; buffer[2] = matrix.M13; buffer[3] = matrix.M14;
        buffer[4] = matrix.M21; buffer[5] = matrix.M22; buffer[6] = matrix.M23; buffer[7] = matrix.M24;
        buffer[8] = matrix.M31; buffer[9] = matrix.M32; buffer[10] = matrix.M33; buffer[11] = matrix.M34;
        buffer[12] = matrix.M41; buffer[13] = matrix.M42; buffer[14] = matrix.M43; buffer[15] = matrix.M44;
    }

    private static void WriteVector3(Span<float> buffer, int offset, Vector3 value)
    {
        buffer[offset] = value.X;
        buffer[offset + 1] = value.Y;
        buffer[offset + 2] = value.Z;
    }

    private static void WriteVector4(Span<float> buffer, int offset, Vector4 value)
    {
        buffer[offset] = value.X;
        buffer[offset + 1] = value.Y;
        buffer[offset + 2] = value.Z;
        buffer[offset + 3] = value.W;
    }

    private static void WriteColor(Span<float> buffer, int offset, ColorRgba value)
    {
        buffer[offset] = value.R;
        buffer[offset + 1] = value.G;
        buffer[offset + 2] = value.B;
        buffer[offset + 3] = value.A;
    }

    private static byte[] CopyFloatsToFrameBuffer(ReadOnlySpan<float> values, byte[] destination)
    {
        var byteCount = values.Length * sizeof(float);
        if (destination.Length != byteCount)
        {
            throw new ArgumentException("Frame state buffer size does not match payload size.", nameof(destination));
        }

        for (var i = 0; i < values.Length; i++)
        {
            WriteFloat(destination, i * sizeof(float), values[i]);
        }

        return destination;
    }


    private static void ApplyWebGlStateMetrics(int hostId, RenderStats stats)
    {
        stats.WebGlStateChanges = WebGlInterop.GetWebGlStateMetric(hostId, 0);
        stats.WebGlUniformUpdates = WebGlInterop.GetWebGlStateMetric(hostId, 1);
        stats.WebGlTextureBinds = WebGlInterop.GetWebGlStateMetric(hostId, 2);
        stats.WebGlBufferBinds = WebGlInterop.GetWebGlStateMetric(hostId, 3);
        stats.WebGlVaoBinds = WebGlInterop.GetWebGlStateMetric(hostId, 4);
        stats.WebGlLegacyDrawPathCalls = WebGlInterop.GetWebGlStateMetric(hostId, 5);
        stats.WebGlLegacyDrawPathBlockedCalls = WebGlInterop.GetWebGlStateMetric(hostId, 6);
        stats.WebGlLegacyStringProtocolCalls = WebGlInterop.GetWebGlStateMetric(hostId, 7);
        stats.WebGlBufferDataCalls = WebGlInterop.GetWebGlStateMetric(hostId, 8);
        stats.WebGlDynamicBufferDataCalls = WebGlInterop.GetWebGlStateMetric(hostId, 9);
    }

    private static void EnsureGpuSkinningAvailable(SceneFrameSnapshot3D snapshot, RhiDevice3D device)
    {
        foreach (var obj in snapshot.AllObjectsInternal)
        {
            if (obj is not ModelPart3D { IsSkinned: true } part) continue;
            var boneCount = part.CurrentGpuSkinMatricesInternal.Length;
            if (boneCount == 0)
                throw new InvalidOperationException($"Skinned model part '{part.Name}' has no GPU bone matrices; rendering an undeformed bind pose is forbidden.");
            device.Capabilities.Require(RhiFeature3D.VertexTextureFetch | RhiFeature3D.FloatTextures, $"GPU skinning for '{part.Name}'");
            if (boneCount > device.Capabilities.Limits.MaxTextureSize)
                throw new RhiDeviceLimitException3D(RhiBackendApi3D.WebGl2, $"GPU skinning for '{part.Name}'", $"bone count <= {device.Capabilities.Limits.MaxTextureSize}", device.Capabilities);
        }
    }

    private static void ApplyAnimationStats(RenderStats stats, SceneFrameSnapshot3D snapshot, bool gpuSkinningActive)
    {
        var imported = 0;
        var skinned = 0;
        var animated = 0;
        var skinMatrices = 0;
        var skinnedPrimitives = 0;
        long skinPayloadBytes = 0;
        foreach (var obj in snapshot.AllObjectsInternal)
        {
            if (obj is not ImportedModel3D model) continue;
            imported++;
            if (model.HasSkins)
            {
                skinned++;
                foreach (var skin in model.Asset.Skins) skinMatrices += skin.BoneCount;
            }
            if (model.HasAnimations || model.Animation.CurrentClip is not null) animated++;
            foreach (var part in model.ModelParts)
            {
                if (!part.IsSkinned) continue;
                skinnedPrimitives++;
                skinPayloadBytes += part.Primitive.Positions.LongLength * sizeof(float) * 8L;
            }
        }

        stats.ImportedModelCount = imported;
        stats.SkinnedModelCount = skinned;
        stats.AnimatedModelCount = animated;
        stats.SkinMatrixCount = skinMatrices;
        stats.SkinnedPrimitiveCount = skinnedPrimitives;
        stats.SkinningVertexPayloadBytes = skinPayloadBytes;
        stats.GpuSkinningRequested = skinned > 0;
        stats.GpuSkinningActive = gpuSkinningActive && skinned > 0;
    }

    private void UploadDirtyMeshGeometry(RenderResourcePlan3D resources, RenderStats stats)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));

        var meshes = resources.Meshes;
        for (var i = 0; i < meshes.Count; i++)
        {
            UploadMeshIfNeeded(meshes[i], stats);
        }
    }

    private static RhiDevice3D CreateRhiDevice(int hostId, EngineResourceConfiguration3D resourceConfiguration)
    {
        var features = (RhiFeature3D)(uint)WebGlInterop.GetRhiFeatureMask(hostId);
        features |= RhiFeature3D.CommandBuffers;
        return new RhiDevice3D(new RhiDeviceCapabilities3D(
            RhiBackendApi3D.WebGl2,
            WebGlInterop.GetRhiAdapterName(hostId),
            WebGlInterop.GetRhiApiVersion(hostId),
            features,
            new RhiDeviceLimits3D(
                WebGlInterop.GetRhiLimit(hostId, 0),
                WebGlInterop.GetRhiLimit(hostId, 1),
                WebGlInterop.GetRhiLimit(hostId, 2),
                WebGlInterop.GetRhiLimit(hostId, 3),
                WebGlInterop.GetRhiLimit(hostId, 4),
                WebGlInterop.GetRhiLimit(hostId, 5),
                WebGlInterop.GetRhiLimit(hostId, 6))),
            resourceConfiguration);
    }

    private void RegisterStaticRhiResources()
    {
        var resources = _rhiDevice?.Resources ?? throw new InvalidOperationException("WebGL RHI device is not initialized.");
        resources.RegisterBuffer("utility:skybox:vertices", new RhiBufferDescriptor3D(8L * sizeof(float), RhiBufferUsage3D.Vertex, sizeof(float) * 2), 1);
        resources.RegisterBuffer("utility:quad:indices", new RhiBufferDescriptor3D(6L * sizeof(ushort), RhiBufferUsage3D.Index, sizeof(ushort)), 1);
        resources.RegisterAllocation("utility:skybox:vao", RhiResourceKind3D.VertexArray, 0, 1);
        resources.RegisterAllocation("pipeline:mesh", RhiResourceKind3D.Pipeline, 0, 1);
        resources.RegisterAllocation("pipeline:skybox", RhiResourceKind3D.Pipeline, 0, 1);
        resources.RegisterAllocation("pipeline:textured", RhiResourceKind3D.Pipeline, 0, 1);
    }

    private void RegisterMeshResources(string meshKey, RenderGeometry3D geometry, bool includeWireframe)
    {
        var resources = _rhiDevice?.Resources ?? throw new InvalidOperationException("WebGL RHI device is not initialized.");
        var version = geometry.GeometryVersion;
        RegisterBuffer("position", geometry.Positions.LongLength * sizeof(float) * 3L, RhiBufferUsage3D.Vertex, sizeof(float) * 3);
        RegisterBuffer("normal", geometry.Normals.LongLength * sizeof(float) * 3L, RhiBufferUsage3D.Vertex, sizeof(float) * 3);
        RegisterOptionalBuffer("uv0", geometry.HasTexCoords0, geometry.TexCoords0.LongLength * sizeof(float) * 2L, sizeof(float) * 2);
        RegisterOptionalBuffer("tangent", geometry.HasTangents, geometry.Tangents.LongLength * sizeof(float) * 4L, sizeof(float) * 4);
        RegisterOptionalBuffer("color0", geometry.HasColors0, geometry.Colors0.LongLength * sizeof(float) * 4L, sizeof(float) * 4);
        RegisterOptionalBuffer("material-slot", geometry.HasMaterialSlots, geometry.MaterialSlots.LongLength * sizeof(float), sizeof(float));
        RegisterOptionalBuffer("bone-index", geometry.HasSkinWeights, geometry.BoneIndices0.LongLength * sizeof(float) * 4L, sizeof(float) * 4);
        RegisterOptionalBuffer("bone-weight", geometry.HasSkinWeights, geometry.BoneWeights0.LongLength * sizeof(float) * 4L, sizeof(float) * 4);
        RegisterBuffer("index", geometry.Indices.ByteCount, RhiBufferUsage3D.Index, geometry.Indices.ElementSizeBytes);
        if (includeWireframe)
        {
            RegisterBuffer("wireframe-index", geometry.WireframeIndices.ByteCount, RhiBufferUsage3D.Index, geometry.WireframeIndices.ElementSizeBytes);
        }
        else
        {
            resources.Release($"mesh:{meshKey}:wireframe-index", RhiResourceKind3D.Buffer);
        }
        resources.RegisterAllocation($"mesh:{meshKey}:vao", RhiResourceKind3D.VertexArray, 0, version);

        void RegisterBuffer(string suffix, long byteSize, RhiBufferUsage3D usage, int stride)
            => resources.RegisterBuffer($"mesh:{meshKey}:{suffix}", new RhiBufferDescriptor3D(byteSize, usage, stride), version);

        void RegisterOptionalBuffer(string suffix, bool present, long byteSize, int stride)
        {
            if (present) RegisterBuffer(suffix, byteSize, RhiBufferUsage3D.Vertex, stride);
            else resources.Release($"mesh:{meshKey}:{suffix}", RhiResourceKind3D.Buffer);
        }
    }

    private void ReleaseMeshResources(string meshKey)
    {
        var resources = _rhiDevice?.Resources;
        if (resources is null) return;
        foreach (var suffix in new[] { "position", "normal", "uv0", "tangent", "color0", "material-slot", "bone-index", "bone-weight", "index", "wireframe-index" })
        {
            resources.Release($"mesh:{meshKey}:{suffix}", RhiResourceKind3D.Buffer);
        }
        resources.Release($"mesh:{meshKey}:vao", RhiResourceKind3D.VertexArray);
    }

    private void UploadMeshIfNeeded(Mesh3D mesh, RenderStats stats)
    {
        var meshKey = mesh.ResourceKey;
        var includeWireframe = Scene.Debug.ShowWireframeOverlay || Scene.Debug.ShowSilhouetteOverlay;
        var hasKnownGeometry = _meshGeometryVersions.TryGetValue(meshKey, out var knownGeometryVersion) && knownGeometryVersion == mesh.GeometryVersion;
        var hasRequiredWireframe = !includeWireframe || (_meshWireframeUploaded.TryGetValue(meshKey, out var wireframeUploaded) && wireframeUploaded);
        if (hasKnownGeometry && hasRequiredWireframe)
        {
            return;
        }

        var geometry = mesh.RenderGeometry;
        var payload = geometry.GetWebGlPayload(includeWireframe);

        WebGlInterop.UploadMeshGeometryBytes(
            _hostId,
            meshKey,
            payload.VertexCount,
            payload.IndexCount,
            payload.PositionStorage,
            payload.NormalStorage,
            payload.TexCoordStorage,
            payload.TangentStorage,
            payload.ColorStorage,
            payload.MaterialSlotStorage,
            payload.BoneIndexStorage,
            payload.BoneWeightStorage,
            payload.IndexStorage,
            payload.IndexElementSize,
            payload.WireframeIndexStorage,
            payload.WireframeIndexElementSize,
            payload.HasTexCoords0,
            payload.HasTangents,
            payload.HasColors0,
            payload.HasMaterialSlots,
            payload.HasSkinWeights,
            payload.VertexLayout);

        _meshGeometryVersions[meshKey] = mesh.GeometryVersion;
        _meshWireframeUploaded[meshKey] = includeWireframe;
        RegisterMeshResources(meshKey, geometry, includeWireframe);
        stats.DirtyMeshUploads++;
        stats.RenderGeometryCount++;
        stats.VertexBufferUploadCount += 2 + (geometry.HasTexCoords0 ? 1 : 0) + (geometry.HasTangents ? 1 : 0) + (geometry.HasColors0 ? 1 : 0) + (geometry.HasMaterialSlots ? 1 : 0) + (geometry.HasSkinWeights ? 2 : 0);
        stats.IndexBufferUploadCount += includeWireframe ? 2 : 1;
        stats.VertexBufferUploadBytes += payload.VertexUploadByteCount;
        stats.IndexBufferUploadBytes += geometry.EstimatedIndexUploadBytes;
        stats.MeshUploadBytes += payload.VertexUploadByteCount + geometry.EstimatedIndexUploadBytes;
        stats.TangentUploadBytes += geometry.HasTangents ? geometry.Tangents.LongLength * sizeof(float) * 4L : 0L;
        stats.WireframeIndexUploadBytes += includeWireframe ? geometry.EstimatedWireframeIndexUploadBytes : 0L;
        if (geometry.HasTangentSpace) stats.TangentSpaceMeshCount++;
        stats.PacketBytes += payload.UploadByteCount;
    }

    private void UploadDirtyResourceTextures(RenderResourcePlan3D resources, RenderStats stats)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));

        var textures = resources.Textures;
        for (var i = 0; i < textures.Count; i++)
        {
            UploadTextureIfDirty(textures[i], stats);
        }
    }

    private void UploadTextureIfDirty(RenderTextureResource3D texture, RenderStats stats)
    {
        if (!texture.IsValid) return;
        var textureKey = texture.Key;
        RestoreDeferredTexture(textureKey);
        if (_textureVersions.TryGetValue(textureKey, out var knownVersion) && knownVersion == texture.Version) return;

        if (!TextureDecodeHelper3D.TryDecodeRgba(texture.DataInternal, out var decoded, out var error))
            throw new InvalidOperationException($"Texture '{texture.LogicalKey}' ({texture.ContentHash}) could not be decoded: {error}. Missing GPU texture data is not rendered through a fallback material.");

        var descriptor = new RhiTextureDescriptor3D(decoded.Width, decoded.Height, RhiTextureFormat3D.Rgba8Unorm, RhiTextureUsage3D.Sampled, GetWebGlMipLevelCount(decoded.Width, decoded.Height));
        var device = _rhiDevice ?? throw new InvalidOperationException("WebGL RHI device is not initialized.");
        device.ValidateTexture(descriptor, $"texture '{texture.LogicalKey}'");
        device.Resources.ValidateTextureRegistration(textureKey, descriptor, texture.Version);
        WebGlInterop.UploadTextureBytes(_hostId, textureKey, decoded.Width, decoded.Height, decoded.RgbaPixels);
        var handle = device.Resources.RegisterTexture(textureKey, descriptor, texture.Version, _rhiResourceOwnerId);
        _textureVersions[textureKey] = texture.Version;
        _textureHandles[textureKey] = handle;
        stats.DirtyTextureUploads++;
        stats.TextureUploadBytes += decoded.ByteLength;
    }

    private void UploadDirtyControlTextures(SceneFrameSnapshot3D snapshot, RenderStats stats)
    {
        foreach (var obj in snapshot.AllObjectsInternal)
        {
            if (obj is not ControlPlane3D plane || !plane.IsVisible)
            {
                continue;
            }

            var bitmap = plane.Snapshot;
            if (bitmap is null)
            {
                continue;
            }

            var textureKey = GetControlTextureKey(plane.Id);
            RestoreDeferredTexture(textureKey);
            if (_textureVersions.TryGetValue(textureKey, out var knownVersion) && knownVersion == plane.SnapshotVersion)
            {
                continue;
            }

            var pixelWidth = System.Math.Max(plane.RenderPixelWidth, 1);
            var pixelHeight = System.Math.Max(plane.RenderPixelHeight, 1);
            var stride = pixelWidth * 4;
            var bufferSize = stride * pixelHeight;
            var descriptor = new RhiTextureDescriptor3D(pixelWidth, pixelHeight, RhiTextureFormat3D.Rgba8Unorm, RhiTextureUsage3D.Sampled, GetWebGlMipLevelCount(pixelWidth, pixelHeight));
            var device = _rhiDevice ?? throw new InvalidOperationException("WebGL RHI device is not initialized.");
            device.ValidateTexture(descriptor, $"control-plane texture '{plane.Id}'");
            device.Resources.ValidateTextureRegistration(textureKey, descriptor, plane.SnapshotVersion);
            if (!_controlTexturePixelBuffers.TryGetValue(textureKey, out var pixels) || pixels.Length != bufferSize)
            {
                pixels = new byte[bufferSize];
                _controlTexturePixelBuffers[textureKey] = pixels;
            }

            try
            {
                unsafe
                {
                    fixed (byte* ptr = pixels)
                    {
                        bitmap.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), (IntPtr)ptr, bufferSize, stride);
                    }
                }
            }
            catch (Exception ex)
            {
                EngineLog3D.Critical("WebGL.ControlPlane", $"Snapshot read failed for control plane '{plane.Id}'; the frame is aborted.", ex);
                throw;
            }

            try
            {
                WebGlInterop.UploadTextureBytes(_hostId, textureKey, pixelWidth, pixelHeight, pixels);
                var handle = device.Resources.RegisterTexture(
                    textureKey,
                    descriptor,
                    plane.SnapshotVersion,
                    _rhiResourceOwnerId);
                _textureVersions[textureKey] = plane.SnapshotVersion;
                _textureHandles[textureKey] = handle;
                stats.DirtyTextureUploads++;
                stats.TextureUploadBytes += bufferSize;
            }
            catch (Exception ex)
            {
                EngineLog3D.Critical("WebGL.ControlPlane", $"Texture upload failed for control plane '{plane.Id}'; the frame is aborted because no placeholder-texture fallback is permitted.", ex);
                throw;
            }
        }

    }

    private void SweepUnusedUploadState(Scene3D scene, RenderResourcePlan3D resources, SceneFrameSnapshot3D snapshot)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));

        var registryVersion = snapshot.RegistryVersion;
        var batchContentVersion = scene.BatchContentVersion;
        if (_lastSweptUploadRegistryVersion == registryVersion && _lastSweptUploadBatchContentVersion == batchContentVersion)
        {
            return;
        }

        // Partial retained plans intentionally omit cached ordinary/particle resources. Sweeping from
        // them would delete live GPU objects. Defer the sweep until the next complete resource plan.
        if (!resources.IsCompleteForMeshSweep)
        {
            return;
        }

        var liveTextures = _liveTextureSweepScratch;
        liveTextures.Clear();

        foreach (var obj in snapshot.AllObjectsInternal)
        {
            if (obj is ControlPlane3D plane && plane.IsVisible && plane.Snapshot is not null)
            {
                liveTextures.Add(GetControlTextureKey(plane.Id));
            }
        }

        _sweepRemovalScratch.Clear();
        foreach (var key in _meshGeometryVersions.Keys)
        {
            if (!resources.ContainsMesh(key)) _sweepRemovalScratch.Add(key);
        }
        foreach (var key in _sweepRemovalScratch)
        {
            TryDestroyMeshGeometry(key);
            _meshGeometryVersions.Remove(key);
            _meshWireframeUploaded.Remove(key);
        }

        _sweepRemovalScratch.Clear();
        foreach (var key in _textureVersions.Keys)
        {
            if (!resources.ContainsTexture(key) && !liveTextures.Contains(key)) _sweepRemovalScratch.Add(key);
        }
        foreach (var key in _sweepRemovalScratch)
        {
            QueueDestroyTexture(key);
            _textureVersions.Remove(key);
            _controlTexturePixelBuffers.Remove(key);
        }
        _sweepRemovalScratch.Clear();
        liveTextures.Clear();

        _lastSweptUploadRegistryVersion = registryVersion;
        _lastSweptUploadBatchContentVersion = batchContentVersion;
    }


    private void TryDestroyMeshGeometry(string meshKey)
    {
        if (_hostId < 0) return;
        try
        {
            WebGlInterop.DestroyMeshGeometry(_hostId, meshKey);
            ReleaseMeshResources(meshKey);
        }
        catch (Exception ex)
        {
            EngineLog3D.Error("WebGL.Resources", "Mesh resource destruction failed.", ex);
        }
    }

    private static string GetControlTextureKey(string planeId)
        => ControlTextureKeyPrefix + planeId;

    private void QueueDestroyTexture(string textureKey)
    {
        _textureHandles.Remove(textureKey, out var handle);
        _textureVersions.TryGetValue(textureKey, out var version);
        _deferredTextureReleases.Enqueue(
            new DeferredTextureRelease(textureKey, version, handle),
            _rhiDevice?.FrameIndex ?? 0,
            _resourceConfiguration?.DeferredReleaseFrames ?? 0);
    }

    private void RestoreDeferredTexture(string textureKey)
    {
        if (!_deferredTextureReleases.TryCancel(
                candidate => string.Equals(candidate.TextureKey, textureKey, StringComparison.Ordinal),
                out var release)) return;
        _textureVersions[textureKey] = release.Version;
        if (release.Handle.IsValid) _textureHandles[textureKey] = release.Handle;
    }

    private void DrainDeferredTextureReleases(long completedFrame)
        => _deferredTextureReleases.DrainReady(completedFrame, ReleaseTextureNow);

    private void ReleaseTextureNow(DeferredTextureRelease release)
    {
        if (_hostId < 0) return;
        try
        {
            WebGlInterop.DestroyTexture(_hostId, release.TextureKey);
            if (release.Handle.IsValid) _rhiDevice?.Resources.ReleaseOwner(release.Handle, _rhiResourceOwnerId);
        }
        catch (Exception ex)
        {
            EngineLog3D.Error("WebGL.Resources", "Texture resource destruction failed.", ex);
        }
    }

    private void UpdateHostRect(bool force = false)
    {
        if (_hostId < 0)
        {
            return;
        }

        var root = this.GetVisualRoot() as Visual;
        var origin = root is null ? null : this.TranslatePoint(new Point(0, 0), root);
        var x = origin?.X ?? 0d;
        var y = origin?.Y ?? 0d;
        var width = Bounds.Width;
        var height = Bounds.Height;
        var visible = IsVisible && width > 0 && height > 0;

        if (force || visible != _lastHostVisible || !NearlyEqual(x, _lastHostX) || !NearlyEqual(y, _lastHostY) || !NearlyEqual(width, _lastHostWidth) || !NearlyEqual(height, _lastHostHeight))
        {
            try
            {
                WebGlInterop.UpdateHost(_hostId, x, y, width, height, visible);
                _lastHostX = x;
                _lastHostY = y;
                _lastHostWidth = width;
                _lastHostHeight = height;
                _lastHostVisible = visible;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("WebGL host rectangle update failed.", ex);
            }
        }

        UpdateMetricsIfChanged(force);
        UpdateCenterCursorIfChanged(force);
    }

    private void UpdateMetricsIfChanged(bool force)
    {
        if (_hostId < 0) return;
        var visible = _lastHostVisible && _performanceMetricsVisible;
        var text = visible ? (_performanceMetricsText ?? string.Empty) : string.Empty;
        if (!force && visible == _lastMetricsVisible && string.Equals(text, _lastMetricsText, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            WebGlInterop.UpdateMetrics(_hostId, text, visible);
            _lastMetricsText = text;
            _lastMetricsVisible = visible;
        }
        catch (Exception ex)
        {
            EngineLog3D.Error("WebGL.Overlay", "Metrics overlay update failed.", ex);
        }
    }

    private void UpdateCenterCursorIfChanged(bool force)
    {
        if (_hostId < 0) return;
        var visible = _lastHostVisible && _centerCursorVisible;
        if (!force && visible == _lastCenterCursorVisible)
        {
            return;
        }

        try
        {
            WebGlInterop.UpdateCenterCursor(_hostId, visible);
            _lastCenterCursorVisible = visible;
        }
        catch (Exception ex)
        {
            EngineLog3D.Error("WebGL.Overlay", "Center-cursor overlay update failed.", ex);
        }
    }

    private static bool NearlyEqual(double a, double b)
        => double.IsNaN(a) || double.IsNaN(b) ? false : Math.Abs(a - b) < 0.25d;





    private readonly struct ControlPlaneUploadRecord
    {
        public const int FloatStride = 20;

        private ControlPlaneUploadRecord(
            string id,
            bool alwaysFaceCamera,
            Vector3 center,
            float extentX,
            float extentY,
            float rollRadians,
            Vector3 corner0,
            Vector3 corner1,
            Vector3 corner2,
            Vector3 corner3)
        {
            Id = id;
            AlwaysFaceCamera = alwaysFaceCamera;
            Center = center;
            ExtentX = extentX;
            ExtentY = extentY;
            RollRadians = rollRadians;
            Corner0 = corner0;
            Corner1 = corner1;
            Corner2 = corner2;
            Corner3 = corner3;
        }

        public static ControlPlaneUploadRecord FromItem(ControlPlaneRenderItem3D item)
        {
            return new ControlPlaneUploadRecord(
                item.Id,
                item.AlwaysFaceCamera,
                item.Center,
                item.ExtentX,
                item.ExtentY,
                item.RollRadians,
                item.Corner0,
                item.Corner1,
                item.Corner2,
                item.Corner3);
        }

        public string Id { get; }
        public bool AlwaysFaceCamera { get; }
        public Vector3 Center { get; }
        public float ExtentX { get; }
        public float ExtentY { get; }
        public float RollRadians { get; }
        public Vector3 Corner0 { get; }
        public Vector3 Corner1 { get; }
        public Vector3 Corner2 { get; }
        public Vector3 Corner3 { get; }
    }

    private void HandleRenderFailure(Exception ex)
    {
        _renderFailureCount++;
        _lastFault = ex;
        System.Threading.Interlocked.Increment(ref _faultCount);
        System.Threading.Volatile.Write(ref _lastFaultTimestamp, Stopwatch.GetTimestamp());
        EngineLog3D.Critical("WebGL", $"Frame rendering failed (consecutive failures: {_renderFailureCount}).", ex);

        try
        {
            ResetUploadCachesAfterContextRestore(discardDeferredReleases: false);
        }
        catch (Exception resetEx)
        {
            EngineLog3D.Error("WebGL.Resources", "Cache reset after render failure failed.", resetEx);
        }

        if (_hostId >= 0)
        {
            try
            {
                var message = BuildRenderFailureOverlayText(ex);
                WebGlInterop.UpdateMetrics(_hostId, message, true);
                _lastMetricsText = message;
                _lastMetricsVisible = true;
            }
            catch (Exception metricsEx)
            {
                EngineLog3D.Error("WebGL.Overlay", "Failed to publish render failure overlay.", metricsEx);
            }
        }

        if (!_disposed && _attached && _hostId >= 0 && _renderFailureCount <= 2)
        {
            _renderPending = true;
            ScheduleInvalidateVisual();
            return;
        }

        PublishFault(ex, $"WebGL rendering remained faulted after {_renderFailureCount} consecutive attempts.", countFault: false);
    }

    private void PublishFault(Exception exception, string message, bool countFault = true)
    {
        if (_fatalFaultPublished) return;
        _fatalFaultPublished = true;
        _lastFault = exception;
        if (countFault) System.Threading.Interlocked.Increment(ref _faultCount);
        System.Threading.Volatile.Write(ref _lastFaultTimestamp, Stopwatch.GetTimestamp());
        EngineLog3D.Critical("WebGL", message, exception);
        try { Faulted?.Invoke(this, new ScenePresenterFaultedEventArgs3D(exception, CapturePresenterSnapshot())); }
        catch (Exception subscriberException) { EngineLog3D.Error("WebGL", "Presenter Faulted subscriber failed.", subscriberException); }
    }

    private static string BuildRenderFailureOverlayText(Exception ex)
    {
        var text = "WebGL render failed\n" + ex.GetType().Name + ": " + ex.Message;
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            text += "\n" + ex.StackTrace;
        }

        const int maxLength = 2400;
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }

    private void ResetUploadCachesAfterContextRestore(bool discardDeferredReleases = true)
    {
        _textureVersions.Clear();
        _textureHandles.Clear();
        if (discardDeferredReleases) _deferredTextureReleases.ClearWithoutRelease();
        _meshGeometryVersions.Clear();
        _meshWireframeUploaded.Clear();
        _controlTexturePixelBuffers.Clear();
        _lastSweptUploadRegistryVersion = -1;
        _lastSweptUploadBatchContentVersion = -1;
        InvalidateRetainedDrawListCache();
        _lastControlPlaneUploadVersion = 0UL;
        _cachedCubemapCsvVersion = -1;
        _cachedCubemapCsv = string.Empty;
        if (_hostId >= 0)
        {
            _retainedOrdinary.Reset(_hostId);
            _retainedParticles.Reset(_hostId);
            _retainedHighScale.Reset(_hostId);
            _clientHighScale.Reset(_hostId);
        }
    }

    private void DestroyHost()
    {
        if (_hostId >= 0)
        {
            var destroyedHostId = _hostId;
            var nativeDestroyed = false;
            try
            {
                _retainedOrdinary.Reset(_hostId);
                _retainedParticles.Reset(_hostId);
                _retainedHighScale.Reset(_hostId);
                _clientHighScale.Reset(_hostId);
                WebGlInterop.DestroyHost(_hostId);
                nativeDestroyed = true;
                EngineLog3D.Information("WebGL", $"Backend host {destroyedHostId} destroyed.");
            }
            finally
            {
                if (nativeDestroyed && _rhiDevice is not null && !_rhiDevice.IsDisposed)
                    _rhiDevice.InvalidateContext("WebGL native host destroyed");
                _rhiDevice?.Dispose();
                _rhiDevice = null;
                _hostId = -1;
            }
        }
        else
        {
            _rhiDevice?.Dispose();
            _rhiDevice = null;
        }

        _moduleReady = false;
        _renderPending = false;
        _invalidateScheduled = false;
        _pendingFrameRendered = null;
        _textureVersions.Clear();
        _textureHandles.Clear();
        _deferredTextureReleases.ClearWithoutRelease();
        _meshGeometryVersions.Clear();
        _meshWireframeUploaded.Clear();
        _controlTexturePixelBuffers.Clear();
        _lastHostX = double.NaN;
        _lastHostY = double.NaN;
        _lastHostWidth = double.NaN;
        _lastHostHeight = double.NaN;
        _lastHostVisible = false;
        _lastMetricsText = null;
        _lastMetricsVisible = false;
        _lastCenterCursorVisible = false;
        InvalidateRetainedDrawListCache();
        _lastControlPlaneUploadVersion = 0UL;
        _cachedCubemapCsvVersion = -1;
        _cachedCubemapCsv = string.Empty;
        _lastSweptUploadRegistryVersion = -1;
        _lastSweptUploadBatchContentVersion = -1;
    }

    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
    }

    private static int GetWebGlMipLevelCount(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width & (width - 1)) != 0 || (height & (height - 1)) != 0) return 1;
        var levels = 1;
        for (var size = Math.Max(width, height); size > 1; size >>= 1) levels++;
        return levels;
    }

    private readonly record struct DeferredTextureRelease(string TextureKey, long Version, RhiResourceHandle3D Handle);

}
