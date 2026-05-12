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
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Controls;

public sealed class WebGlScenePresenter : Control, IScenePresenter, IPerformanceMetricsOverlayPresenter, ICenterCursorOverlayPresenter, IPointerLockPresenter
{
    private readonly Dictionary<string, int> _textureVersions = new();
    private readonly Dictionary<string, int> _meshGeometryVersions = new();
    private readonly Dictionary<string, bool> _meshWireframeUploaded = new(StringComparer.Ordinal);
    private Scene3D _scene = new();
    private int _hostId = -1;
    private bool _moduleReady;
    private bool _initializing;
    private bool _renderPending;
    private bool _invalidateScheduled;
    private bool _attached;
    private bool _disposed;
    private int _lastSweptUploadRegistryVersion = -1;
    private int _lastSweptUploadBatchContentVersion = -1;
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
    private readonly Dictionary<string, byte[]> _controlTexturePixelBuffers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveTextureSweepScratch = new(StringComparer.Ordinal);
    private readonly List<string> _sweepRemovalScratch = new(64);
    private readonly List<ControlPlaneRenderItem3D> _controlPlaneItems = new(16);
    private readonly List<ControlPlaneUploadRecord> _controlPlaneRecords = new(16);
    private byte[] _controlPlanePlaneBytes = Array.Empty<byte>();
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
    private bool _clientHighScaleRuntimeFailed;
    private int _renderFailureCount;

    public WebGlScenePresenter()
    {
        Focusable = false;
        ClipToBounds = true;
        //Background = Brushes.Transparent;
        LayoutUpdated += (_, _) =>
        {
            if (_moduleReady && _hostId >= 0)
            {
                UpdateHostRect();
            }
        };
    }

    public event EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;

    public BackendKind Kind => BackendKind.WebGlBrowser;
    public Control View => this;

    public Scene3D Scene
    {
        get => _scene;
        set
        {
            if (!ReferenceEquals(_scene, null))
            {
                _scene.SceneChangedDetailed -= OnSceneChangedDetailed;
            }

            _scene = value ?? throw new ArgumentNullException(nameof(value));
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
        if (e.Source is ParticleSystem3D &&
            (e.Kind == SceneChangeKind.Transform || e.Kind == SceneChangeKind.Geometry))
        {
            _retainedParticles.MarkDirty(e);
        }
        else
        {
            _retainedOrdinary.MarkDirty(e);
            _retainedParticles.MarkDirty(e);
            _retainedHighScale.MarkDirty(e);
        }

        if (e.Kind == SceneChangeKind.Structure || e.Kind == SceneChangeKind.Visibility || e.Kind == SceneChangeKind.Control)
        {
            _lastControlPlaneUploadVersion = 0UL;
        }
    }

    public void RequestRender()
    {
        if (_disposed)
        {
            return;
        }

        _renderPending = true;
        ScheduleInvalidateVisual();
    }

    private void ScheduleInvalidateVisual()
    {
        if (_disposed || _invalidateScheduled)
        {
            return;
        }

        _invalidateScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _invalidateScheduled = false;
            if (!_disposed)
            {
                InvalidateVisual();
            }
        }, DispatcherPriority.Render);
    }


    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _ = EnsureHostAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        DestroyHost();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!_attached || !_moduleReady || _hostId < 0)
        {
            return;
        }

        UpdateHostRect();

        if (_renderPending)
        {
            _renderPending = false;
            RenderToWebGl();
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
            _moduleReady = true;
            UpdateHostRect(force: true);
            UpdateMetricsIfChanged(force: true);
            UpdateCenterCursorIfChanged(force: true);
            RequestRender();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("3DEngine WebGL backend initialization failed: " + ex);
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
            HandleRenderFailure(ex);
        }
    }

    private void RenderToWebGlCore()
    {
        if (_hostId < 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        if (WebGlInterop.ConsumeContextResetFlag(_hostId))
        {
            ResetUploadCachesAfterContextRestore();
        }

        var start = Stopwatch.GetTimestamp();
        var width = (float)global::System.Math.Max(Bounds.Width, 1d);
        var height = (float)global::System.Math.Max(Bounds.Height, 1d);
        var frame = SceneRenderFrameContext3D.Build(Scene, width, height, BackendKind.WebGlBrowser);
        var snapshot = frame.Snapshot;
        var viewProjection = frame.ViewProjection;
        var pipeline = frame.Pipeline;
        var stats = frame.CreateBaseStats();
        var ordinaryNeedsPlan = _retainedOrdinary.RequiresScenePlan(frame);
        var particleNeedsPlan = _retainedParticles.RequiresScenePlan(frame);
        var plan = SceneRenderPlanBuilder3D.Build(
            frame,
            requiresCpuSkinFallback: null,
            stats: stats,
            includeOrdinary: ordinaryNeedsPlan,
            includeParticles: particleNeedsPlan,
            includeHighScale: true);
        var hasVisibleHighScale = plan.HasVisibleHighScale;
        var useClientHighScale = hasVisibleHighScale &&
            !_clientHighScaleRuntimeFailed &&
            Scene.Performance.EnableRetainedInstanceBuffers &&
            Scene.Performance.EnableWebGlClientHighScaleRuntime;

        if (Scene.Debug.ShowPerformanceMetrics)
        {
            var gpuSkinningSupported = WebGlInterop.IsGpuSkinningSupported(_hostId);
            ApplyAnimationStats(stats, snapshot, gpuSkinningActive: gpuSkinningSupported, fallbackReason: gpuSkinningSupported ? string.Empty : "WebGL GPU skinning unavailable on this context; static bind-pose fallback");
        }

        SweepUnusedUploadState(Scene, plan.Resources, snapshot);
        SceneRenderStats3D.ApplyPipelineStats(stats, Scene, pipeline);

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
                _clientHighScale.SyncFrame(_hostId, Scene, plan.HighScaleLayers, width, height, viewProjection, plan.Shadow, stats);
            }
            catch (Exception ex)
            {
                _clientHighScaleRuntimeFailed = true;
                useClientHighScale = false;
                Debug.WriteLine("3DEngine WebGL client high-scale runtime failed; falling back to retained upload path: " + ex);
                try
                {
                    if (_clientHighScale.HasRuntimeState)
                    {
                        _clientHighScale.Reset(_hostId);
                    }
                }
                catch (Exception resetEx)
                {
                    Debug.WriteLine("3DEngine WebGL client high-scale reset after failure also failed: " + resetEx);
                }
            }
        }
        else if (_clientHighScale.HasRuntimeState)
        {
            _clientHighScale.Reset(_hostId);
        }

        var ordinaryDraws = _retainedOrdinary.BuildAndUpload(_hostId, plan, stats);
        var particleDraws = _retainedParticles.BuildAndUpload(_hostId, plan, stats);
        List<WebGlRetainedBatchPacket>? highScaleDraws = null;

        // Retained high-scale is reserved for the explicit fallback path when the JS-owned runtime is disabled.
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
        RenderRetainedFrameDirect(stats, frame);
        if (Scene.Debug.ShowPerformanceMetrics)
        {
            if (useClientHighScale)
            {
                WebGlClientHighScaleRenderer.ApplyJsMetrics(_hostId, stats);
            }
            ApplyWebGlStateMetrics(_hostId, stats);
        }
        stats.PacketBuildMilliseconds = GetElapsedMilliseconds(packetStart);
        stats.BackendMilliseconds = GetElapsedMilliseconds(start);

        var renderedFrame = new SceneFrameRenderedEventArgs(Kind, stats.BackendMilliseconds, stats);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                FrameRendered?.Invoke(this, renderedFrame);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("3DEngine WebGL FrameRendered subscriber failed: " + ex);
            }
        }, DispatcherPriority.Background);
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
        BuildUnifiedRetainedDrawList(plan, ordinaryDraws, particleDraws, highScaleDraws, useClientHighScale, _retainedBatches);
        _retainedBatches.Sort(CompareRetainedBatchDrawOrder);

        _lastOrdinaryDrawListVersion = _retainedOrdinary.Version;
        _lastParticleDrawListVersion = _retainedParticles.Version;
        _lastHighScaleDrawListVersion = highScaleVersion;
        _lastCombinedHighScaleClientRuntime = useClientHighScale;
        _combinedDrawListVersion++;
    }


    private static void BuildUnifiedRetainedDrawList(
        SceneRenderPlan3D plan,
        List<WebGlRetainedBatchPacket> ordinaryDraws,
        List<WebGlRetainedBatchPacket> particleDraws,
        List<WebGlRetainedBatchPacket>? highScaleDraws,
        bool useClientHighScale,
        List<WebGlRetainedBatchPacket> output)
    {
        var ordinaryById = plan.IncludesOrdinary ? BuildPacketMap(ordinaryDraws) : null;
        var particleById = plan.IncludesParticles ? BuildPacketMap(particleDraws) : null;
        var fallbackHighScaleByOrder = !useClientHighScale && highScaleDraws is not null
            ? BuildPacketsByDrawOrder(highScaleDraws)
            : null;

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
                        output.Add(ClonePacketForCommand(ordinaryPacket, command));
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
                        output.Add(ClonePacketForCommand(particlePacket, command));
                    }
                    break;

                case SceneRenderCommandKind3D.HighScaleLayer:
                    if (useClientHighScale)
                    {
                        if (command.HighScaleLayer is not null)
                        {
                            output.Add(new WebGlRetainedBatchPacket
                            {
                                Id = WebGlClientHighScaleRenderer.BuildLayerDrawCommandId(command.HighScaleLayer.Id),
                                Transparent = false,
                                SortDistanceSquared = command.SortDistanceSquared,
                                DrawOrder = command.SourceOrder
                            });
                        }
                    }
                    else if (fallbackHighScaleByOrder is not null &&
                             fallbackHighScaleByOrder.TryGetValue(command.SourceOrder, out var packets))
                    {
                        for (var p = 0; p < packets.Count; p++)
                        {
                            var packet = packets[p];
                            output.Add(new WebGlRetainedBatchPacket
                            {
                                Id = packet.Id,
                                Transparent = packet.Transparent,
                                SortDistanceSquared = command.SortDistanceSquared,
                                DrawOrder = command.SourceOrder
                            });
                        }
                    }
                    break;
            }
        }
    }

    private static Dictionary<string, WebGlRetainedBatchPacket> BuildPacketMap(List<WebGlRetainedBatchPacket> packets)
    {
        var map = new Dictionary<string, WebGlRetainedBatchPacket>(packets.Count, StringComparer.Ordinal);
        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            map[packet.Id] = packet;
        }

        return map;
    }

    private static Dictionary<int, List<WebGlRetainedBatchPacket>> BuildPacketsByDrawOrder(List<WebGlRetainedBatchPacket> packets)
    {
        var map = new Dictionary<int, List<WebGlRetainedBatchPacket>>();
        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            if (!map.TryGetValue(packet.DrawOrder, out var list))
            {
                list = new List<WebGlRetainedBatchPacket>();
                map[packet.DrawOrder] = list;
            }

            list.Add(packet);
        }

        return map;
    }

    private static void AppendCachedPackets(List<WebGlRetainedBatchPacket> output, List<WebGlRetainedBatchPacket> packets)
    {
        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            output.Add(new WebGlRetainedBatchPacket
            {
                Id = packet.Id,
                Transparent = packet.Transparent,
                SortDistanceSquared = packet.SortDistanceSquared,
                DrawOrder = packet.DrawOrder
            });
        }
    }

    private static WebGlRetainedBatchPacket ClonePacketForCommand(WebGlRetainedBatchPacket packet, SceneRenderCommand3D command)
        => new()
        {
            Id = packet.Id,
            Transparent = command.Transparent,
            SortDistanceSquared = command.SortDistanceSquared,
            DrawOrder = command.SourceOrder
        };

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
            WebGlInterop.SetRetainedDrawOrder(_hostId, string.Empty);
            return;
        }

        var order = string.Create(EstimateDrawOrderLength(retainedBatches), retainedBatches, static (span, batches) =>
        {
            var pos = 0;
            for (var i = 0; i < batches.Count; i++)
            {
                if (i > 0) span[pos++] = '\n';
                var id = batches[i].Id;
                for (var c = 0; c < id.Length; c++)
                {
                    var ch = id[c];
                    if (ch != '\n' && ch != '\r') span[pos++] = ch;
                }
                span[pos++] = '|';
                span[pos++] = batches[i].Transparent ? '1' : '0';
            }
        });
        WebGlInterop.SetRetainedDrawOrder(_hostId, order);
    }

    private static int EstimateDrawOrderLength(List<WebGlRetainedBatchPacket> retainedBatches)
    {
        var length = retainedBatches.Count > 0 ? retainedBatches.Count - 1 : 0;
        for (var i = 0; i < retainedBatches.Count; i++)
        {
            length += retainedBatches[i].Id.Length + 2;
        }
        return length;
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
                WebGlClientHighScaleRenderer.BuildLayerDrawCommandId(command.HighScaleLayer.Id),
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
                packet.Id,
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
        if (_controlPlanePlaneBytes.Length != byteCount)
        {
            _controlPlanePlaneBytes = byteCount == 0 ? Array.Empty<byte>() : new byte[byteCount];
        }
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



    private static void WriteFloat(byte[] destination, int byteOffset, float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        destination[byteOffset + 0] = (byte)bits;
        destination[byteOffset + 1] = (byte)(bits >> 8);
        destination[byteOffset + 2] = (byte)(bits >> 16);
        destination[byteOffset + 3] = (byte)(bits >> 24);
    }

    private void RenderRetainedFrameDirect(RenderStats stats, SceneRenderFrameContext3D frame)
    {
        var pipeline = frame.Pipeline;
        var width = frame.Width;
        var height = frame.Height;
        var viewProjection = frame.ViewProjection;
        var lighting = SceneLightingResolver3D.Resolve(Scene);
        var skybox = Scene.Environment.Skybox;

        Span<float> view = stackalloc float[16];
        WriteMatrix(view, viewProjection);
        Span<float> camera = stackalloc float[12];
        WriteVector3(camera, 0, Scene.Camera.Position);
        WriteVector3(camera, 3, Scene.Camera.Right);
        WriteVector3(camera, 6, Scene.Camera.SafeUp);
        WriteVector3(camera, 9, Scene.Camera.Forward);

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
        WriteColor(style, 0, Scene.BackgroundColor);
        WriteVector3(style, 4, skybox.TopColor.ToVector3());
        WriteVector3(style, 7, skybox.HorizonColor.ToVector3());
        WriteVector3(style, 10, skybox.BottomColor.ToVector3());
        style[13] = skybox.Intensity;
        style[14] = Scene.RenderPipeline.ToneMapping.Exposure;
        style[15] = Scene.RenderPipeline.ToneMapping.Gamma;
        style[16] = Scene.RenderPipeline.Ssao.Strength;
        style[17] = Scene.RenderPipeline.Ssao.Radius;
        style[18] = Scene.RenderPipeline.Ssao.Bias;
        style[19] = Scene.RenderPipeline.Ssao.SampleCount;
        // Reserved slots keep the binary ABI stable for future retained frame state.

        var flags = 0;
        if (skybox.Mode != SkyboxMode3D.None) flags |= 1;
        if (pipeline.SsaoActive) flags |= 2;
        if (pipeline.HdrActive) flags |= 4;
        if (Scene.Debug.ShowWireframeOverlay) flags |= 8;
        if (Scene.Debug.ShowSilhouetteOverlay) flags |= 16;

        WebGlInterop.RenderRetainedSceneFrameDirect(
            _hostId,
            width,
            height,
            flags,
            (int)skybox.Mode,
            (int)pipeline.ToneMappingMode,
            skybox.HasEquirectangularTexture ? skybox.EquirectangularTextureKey ?? string.Empty : string.Empty,
            BuildCubemapCsv(skybox),
            CopyFloatsToFrameBuffer(view, _viewProjectionBytes),
            CopyFloatsToFrameBuffer(camera, _cameraBytes),
            CopyFloatsToFrameBuffer(light, _lightingBytes),
            CopyFloatsToFrameBuffer(style, _styleBytes));
    }

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
        _cachedCubemapCsv = string.Join("\n", skybox.CubemapTextureKeys);
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
    }

    private static void ApplyAnimationStats(RenderStats stats, SceneFrameSnapshot3D snapshot, bool gpuSkinningActive, string fallbackReason)
    {
        var imported = 0;
        var skinned = 0;
        var animated = 0;
        var skinMatrices = 0;
        var skinnedPrimitives = 0;
        long skinPayloadBytes = 0;
        foreach (var obj in snapshot.AllObjects)
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
        stats.SkinningFallbackReason = (gpuSkinningActive && skinned > 0) || skinned == 0 ? string.Empty : fallbackReason;
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
            payload.Positions,
            payload.Normals,
            payload.TexCoords0,
            payload.Tangents,
            payload.Colors0,
            payload.MaterialSlots,
            payload.BoneIndices0,
            payload.BoneWeights0,
            payload.Indices,
            payload.IndexElementSize,
            payload.WireframeIndices,
            payload.WireframeIndexElementSize,
            payload.HasTexCoords0,
            payload.HasTangents,
            payload.HasColors0,
            payload.HasMaterialSlots,
            payload.HasSkinWeights,
            payload.VertexLayout);

        _meshGeometryVersions[meshKey] = mesh.GeometryVersion;
        _meshWireframeUploaded[meshKey] = includeWireframe;
        stats.DirtyMeshUploads++;
        stats.RenderGeometryCount++;
        stats.VertexBufferUploadCount += 2 + (geometry.HasTexCoords0 ? 1 : 0) + (geometry.HasTangents ? 1 : 0) + (geometry.HasColors0 ? 1 : 0) + (geometry.HasMaterialSlots ? 1 : 0) + (geometry.HasSkinWeights ? 2 : 0);
        stats.IndexBufferUploadCount += includeWireframe ? 2 : 1;
        stats.VertexBufferUploadBytes += geometry.EstimatedVertexUploadBytes;
        stats.IndexBufferUploadBytes += geometry.EstimatedIndexUploadBytes;
        stats.MeshUploadBytes += geometry.EstimatedUploadBytes;
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
            var texture = textures[i];
            UploadTextureIfDirty(texture.Key, texture.Data, texture.Version, stats);
        }
    }

    private void UploadTextureIfDirty(string? textureKey, byte[]? textureData, int version, RenderStats stats)
    {
        if (string.IsNullOrWhiteSpace(textureKey) || textureData is not { Length: > 0 })
        {
            return;
        }

        if (_textureVersions.TryGetValue(textureKey, out var knownVersion) && knownVersion == version)
        {
            return;
        }

        if (!TextureDecodeHelper3D.TryDecodeRgba(textureData, out var decoded, out _))
        {
            return;
        }

        WebGlInterop.UploadTextureBytes(_hostId, textureKey, decoded.Width, decoded.Height, decoded.RgbaPixels);
        _textureVersions[textureKey] = version;
        stats.DirtyTextureUploads++;
        stats.TextureUploadBytes += decoded.ByteLength;
    }

    private void UploadDirtyControlTextures(SceneFrameSnapshot3D snapshot, RenderStats stats)
    {
        foreach (var obj in snapshot.AllObjects)
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

            if (_textureVersions.TryGetValue(plane.Id, out var knownVersion) && knownVersion == plane.SnapshotVersion)
            {
                continue;
            }

            var pixelWidth = System.Math.Max(plane.RenderPixelWidth, 1);
            var pixelHeight = System.Math.Max(plane.RenderPixelHeight, 1);
            var stride = pixelWidth * 4;
            var bufferSize = stride * pixelHeight;
            if (!_controlTexturePixelBuffers.TryGetValue(plane.Id, out var pixels) || pixels.Length != bufferSize)
            {
                pixels = new byte[bufferSize];
                _controlTexturePixelBuffers[plane.Id] = pixels;
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

                WebGlInterop.UploadTextureBytes(_hostId, plane.Id, pixelWidth, pixelHeight, pixels);
                _textureVersions[plane.Id] = plane.SnapshotVersion;
                stats.DirtyTextureUploads++;
                stats.TextureUploadBytes += bufferSize;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"3DEngine WebGL skipped control-plane texture '{plane.Id}' after upload failure: {ex}");
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

        foreach (var obj in snapshot.AllObjects)
        {
            if (obj is ControlPlane3D plane && plane.IsVisible && plane.Snapshot is not null)
            {
                liveTextures.Add(plane.Id);
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
            TryDestroyTexture(key);
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
        }
        catch (Exception ex)
        {
            Debug.WriteLine("3DEngine WebGL mesh resource destruction failed: " + ex);
        }
    }

    private void TryDestroyTexture(string textureKey)
    {
        if (_hostId < 0) return;
        try
        {
            WebGlInterop.DestroyTexture(_hostId, textureKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("3DEngine WebGL texture resource destruction failed: " + ex);
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
                Debug.WriteLine("3DEngine WebGL host rectangle update failed: " + ex);
                return;
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
            Debug.WriteLine("3DEngine WebGL metrics overlay update failed: " + ex);
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
            Debug.WriteLine("3DEngine WebGL center-cursor overlay update failed: " + ex);
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
        Debug.WriteLine($"3DEngine WebGL render failed ({_renderFailureCount}): {ex}");

        try
        {
            ResetUploadCachesAfterContextRestore();
        }
        catch (Exception resetEx)
        {
            Debug.WriteLine("3DEngine WebGL cache reset after render failure failed: " + resetEx);
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
                Debug.WriteLine("3DEngine WebGL failed to publish render failure overlay: " + metricsEx);
            }
        }

        if (!_disposed && _attached && _hostId >= 0 && _renderFailureCount <= 2)
        {
            _renderPending = true;
            ScheduleInvalidateVisual();
        }
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

    private void ResetUploadCachesAfterContextRestore()
    {
        _textureVersions.Clear();
        _meshGeometryVersions.Clear();
        _meshWireframeUploaded.Clear();
        _controlTexturePixelBuffers.Clear();
        _lastSweptUploadRegistryVersion = -1;
        _lastSweptUploadBatchContentVersion = -1;
        InvalidateRetainedDrawListCache();
        _lastControlPlaneUploadVersion = 0UL;
        _cachedCubemapCsvVersion = -1;
        _cachedCubemapCsv = string.Empty;
        _clientHighScaleRuntimeFailed = false;
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
            _retainedOrdinary.Reset(_hostId);
            _retainedParticles.Reset(_hostId);
            _retainedHighScale.Reset(_hostId);
            _clientHighScale.Reset(_hostId);
            WebGlInterop.DestroyHost(_hostId);
            _hostId = -1;
        }

        _moduleReady = false;
        _renderPending = false;
        _invalidateScheduled = false;
        _textureVersions.Clear();
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
}
