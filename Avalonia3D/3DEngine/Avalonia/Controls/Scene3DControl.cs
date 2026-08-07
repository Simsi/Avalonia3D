using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ThreeDEngine.Avalonia.Adapters;
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.Interaction;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Navigation;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Physics.Kinematic;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Controls;

public sealed class Scene3DControl : Border, IDisposable
{
    public static readonly StyledProperty<bool> ShowPerformanceMetricsProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(ShowPerformanceMetrics), false);
    public static readonly StyledProperty<double> LiveControlSnapshotFpsProperty = AvaloniaProperty.Register<Scene3DControl, double>(nameof(LiveControlSnapshotFps), 30d);
    public static readonly StyledProperty<bool> EnableLiveControlFallbackRefreshProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(EnableLiveControlFallbackRefresh), false);
    public static readonly StyledProperty<SceneNavigationMode> NavigationModeProperty = AvaloniaProperty.Register<Scene3DControl, SceneNavigationMode>(nameof(NavigationMode), SceneNavigationMode.FreeFly);
    public static readonly StyledProperty<bool> EnableSceneNavigationProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(EnableSceneNavigation), true);
    public static readonly StyledProperty<SceneMouseLookMode> MouseLookModeProperty = AvaloniaProperty.Register<Scene3DControl, SceneMouseLookMode>(nameof(MouseLookMode), SceneMouseLookMode.ButtonDrag);
    public static readonly StyledProperty<bool> ShowCenterCursorProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(ShowCenterCursor), true);
    public static readonly StyledProperty<bool> ContinuousRenderingProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(ContinuousRendering), false);
    public static readonly StyledProperty<double> ContinuousRenderingFpsProperty = AvaloniaProperty.Register<Scene3DControl, double>(nameof(ContinuousRenderingFps), 60d);
    public static readonly StyledProperty<bool> FpsLockEnabledProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(FpsLockEnabled), true);
    public static readonly StyledProperty<double> TargetFpsProperty = AvaloniaProperty.Register<Scene3DControl, double>(nameof(TargetFps), 60d);
    public static readonly StyledProperty<double> UnlockedMaxFpsProperty = AvaloniaProperty.Register<Scene3DControl, double>(nameof(UnlockedMaxFps), 240d);
    public static readonly StyledProperty<bool> FrameInterpolationEnabledProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(FrameInterpolationEnabled), true);
    public static readonly StyledProperty<bool> AutomaticSceneUpdatesProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(AutomaticSceneUpdates), true);
    public static readonly StyledProperty<double> FixedUpdateFramesPerSecondProperty = AvaloniaProperty.Register<Scene3DControl, double>(nameof(FixedUpdateFramesPerSecond), 60d);
    public static readonly StyledProperty<int> MaximumCatchUpStepsProperty = AvaloniaProperty.Register<Scene3DControl, int>(nameof(MaximumCatchUpSteps), SceneUpdateLoop3D.DefaultMaximumCatchUpSteps);
    public static readonly StyledProperty<double> SimulationTimeScaleProperty = AvaloniaProperty.Register<Scene3DControl, double>(nameof(SimulationTimeScale), 1d);
    public static readonly StyledProperty<bool> IsSimulationPausedProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(IsSimulationPaused), false);
    public static readonly StyledProperty<bool> AdaptivePerformanceEnabledProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(AdaptivePerformanceEnabled), false);
    public static readonly StyledProperty<SceneSimulationExecutionMode3D> SimulationExecutionModeProperty = AvaloniaProperty.Register<Scene3DControl, SceneSimulationExecutionMode3D>(nameof(SimulationExecutionMode), SceneSimulationExecutionMode3D.Automatic);

    private const double PerformanceMetricsUpdateIntervalMilliseconds = 1000d;
    private const double BrowserCameraHoverSuppressionMilliseconds = 180d;
    private const double BrowserVisibilityPollMilliseconds = 250d;
    private const double LongFrameThresholdMilliseconds = 40d;
    private const double FramePacingLogIntervalMilliseconds = 5000d;
    private const double RuntimeHealthLogIntervalMilliseconds = 2000d;
    private const double WatchdogWarningMilliseconds = 2500d;
    private const double WatchdogFailureMilliseconds = 15000d;

    private readonly Grid _root;
    private readonly Canvas _hiddenHost;
    private readonly Border _performanceMetricsHost;
    private readonly TextBlock _performanceMetricsText;
    private readonly Grid _centerCursorHost;
    private readonly Border _runtimeFaultHost;
    private readonly TextBlock _runtimeFaultText;
    private readonly DispatcherTimer _runtimeDiagnosticsTimer;
    private readonly DispatcherTimer _snapshotFallbackTimer;
    private readonly DispatcherTimer _navigationTimer;
    private readonly DispatcherTimer _continuousRenderTimer;
    private readonly HashSet<Key> _pressedKeys;
    private readonly object _navigationStateSync = new();
    private NavigationInputSnapshot3D _publishedNavigationInput = NavigationInputSnapshot3D.Disabled;
    private long _navigationInputSequence;
    private long _lastConsumedNavigationInputSequence;
    private bool _pendingPersonJump;
    private bool _pendingCameraAngleSynchronization;
    private int _simulationPersonGrounded;
    private float _simulationPersonVerticalVelocity;
    private readonly FreeFlyNavigationSettings _freeFlySettings = new();
    private readonly PersonNavigationSettings _personSettings = new();
    private bool _isMouseLooking;
    private bool _hasMouseLookPosition;
    private bool _isPointerInsideScene;
    private bool _centerLockedCursorApplied;
    private Cursor? _cursorBeforeCenterLockedMouseLook;
    private IPointer? _mouseLookPointer;
    private PointerEventArgs? _lastCenterLockedPointerEvent;
    private Vector2 _lastMouseLookPosition;
    private Vector2 _pendingMouseLookDelta;
    private float _yawDegrees;
    private float _pitchDegrees;
    private Vector3 _personVelocity;
    private bool _personGrounded;
    private readonly KinematicCharacterController3D _personController = new();
    private readonly SceneRenderScheduler3D _renderScheduler = new(StopwatchEngineClock3D.Shared);
    private readonly Dictionary<ControlPlane3D, ControlPlaneRuntimeAdapter> _controlAdapters;
    private readonly HashSet<ControlPlane3D> _creatingControlAdapters;
    private readonly List<ControlPlane3D> _controlPlanes;
    private readonly HashSet<ControlPlane3D> _controlPlaneSet;
    private readonly HashSet<Object3D> _controlPlanePickExclusions;
    private readonly List<ControlPlane3D> _staleControlPlanesScratch;
    private readonly Queue<ControlPlaneRuntimeAdapter> _dirtyControlSnapshotQueue;
    private readonly HashSet<ControlPlaneRuntimeAdapter> _dirtyControlSnapshotSet;
    private readonly Engine3D _engine;
    private readonly bool _ownsEngine;
    private Scene3D _scene;
    private readonly SceneSimulationHost3D _simulationHost;
    private IScenePresenter? _presenter;
    private bool _ownsScene = true;
    private volatile bool _disposed;
    private ControlPlaneRuntimeAdapter? _activeControlAdapter;
    private ControlPlaneRuntimeAdapter? _focusedControlAdapter;
    private ControlPlaneRuntimeAdapter? _hoveredControlAdapter;
    private int _forwardedControlInputDepth;
    private int _performanceFrameCount;
    private double _performanceFrameMillisecondsTotal;
    private double _performanceFrameMillisecondsLast;
    private long _performanceWindowStartTicks;
    private string? _pendingPerformanceMetricsText;
    private bool _performanceMetricsTextUpdateScheduled;
    private bool _unlockedRenderPending;
    private bool _browserContinuousRenderScheduled;
    private long _suppressHoverPickingUntilTicks;
    private long _lastBrowserVisibilityPollTicks;
    private int _lastBrowserDocumentVisibilityVersion = -1;
    private long _lastFrameRenderedTicks;
    private long _presentationWindowStartTicks;
    private int _presentationWindowFrameCount;
    private double _lastPresentedFramesPerSecond;
    private double _lastPresentationJitterMilliseconds;
    private long _lastFramePacingLogTicks;
    private int _longFrameCount;
    private double _worstFrameMilliseconds;
    private long _lastFrameAllocatedBytes;
    private long _lastAllocationWindowTicks;
    private long _lastAllocationWindowBytes;
    private int _lastGen0Count;
    private int _lastGen1Count;
    private int _lastGen2Count;
    private double _lastAllocatedMegabytesPerSecond;
    private int _controlSnapshotRefreshesSinceLastFrame;
    private int _controlSnapshotQueueHighWaterSinceLastFrame;
    private int _controlPickingRequestsSinceLastFrame;
    private int _controlPlanePickTestsSinceLastFrame;
    private double _pickingMillisecondsSinceLastFrame;
    private RenderStats _lastRenderStats = RenderStats.Empty;
    private readonly string _controlId = Guid.NewGuid().ToString("N");
    private Exception? _runtimeFault;
    private string? _runtimeFaultSubsystem;
    private string? _lastAutomaticDiagnosticPath;
    private long _renderRequestCount;
    private long _renderedFrameCount;
    private long _coalescedContinuousSceneInvalidationCount;
    private long _lastRenderRequestTicks;
    private long _firstOutstandingRenderRequestTicks;
    private long _lastHealthLogTicks;
    private long _lastWatchdogWarningTicks;
    private long _lastObservedSimulationTick;
    private long _lastSimulationProgressTicks;
    private int _watchdogRearmCount;

    [Obsolete("Use Scene3DControl(Engine3D) with an explicitly composed engine. This compatibility constructor requires Avalonia3D.Engine or the complete 3DEngine source-drop.")]
    public Scene3DControl()
        : this(Engine3D.CreateDefault(), scene: null, ownsEngine: true)
    {
    }

    public Scene3DControl(Engine3D engine)
        : this(engine, scene: null, ownsEngine: false)
    {
    }

    public Scene3DControl(Scene3D scene)
        : this(scene?.Engine ?? throw new ArgumentNullException(nameof(scene)), scene, ownsEngine: false)
    {
    }

    private Scene3DControl(Engine3D engine, Scene3D? scene, bool ownsEngine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        if (!_engine.Services.TryGetService<IScenePresenterFactory>(out var presenterFactory) || presenterFactory is null)
        {
            throw new ArgumentException(
                "The supplied Engine3D has no Avalonia presenter configured. Call UseOpenGl() on desktop or UseWebGl() in the browser before Build().",
                nameof(engine));
        }
        if (OperatingSystem.IsBrowser() && presenterFactory.Kind == BackendKind.OpenGlDesktop)
        {
            throw new ArgumentException("The OpenGL desktop presenter cannot be used by a browser Scene3DControl. Configure UseWebGl().", nameof(engine));
        }
        if (!OperatingSystem.IsBrowser() && presenterFactory.Kind == BackendKind.WebGlBrowser)
        {
            throw new ArgumentException("The WebGL presenter requires a browser Scene3DControl. Configure UseOpenGl() on desktop.", nameof(engine));
        }
        if (scene?.IsDisposed == true) throw new ObjectDisposedException(nameof(scene));
        _ownsEngine = ownsEngine;
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;

        _root = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true
        };
        _hiddenHost = new Canvas
        {
            Width = 1d,
            Height = 1d,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true
        };

        _performanceMetricsText = new TextBlock
        {
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 12d,
            Foreground = Brushes.White,
            Text = "FPS: --"
        };

        _performanceMetricsHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8d),
            Padding = new Thickness(8d, 5d),
            CornerRadius = new CornerRadius(4d),
            Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            IsHitTestVisible = false,
            IsVisible = false,
            Child = _performanceMetricsText
        };
        _performanceMetricsHost.ZIndex = int.MaxValue;

        _centerCursorHost = CreateCenterCursorHost();
        _centerCursorHost.ZIndex = int.MaxValue - 1;

        _runtimeFaultText = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 13d,
            TextWrapping = TextWrapping.Wrap
        };
        _runtimeFaultHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12d),
            Padding = new Thickness(12d),
            CornerRadius = new CornerRadius(5d),
            Background = new SolidColorBrush(Color.FromArgb(235, 130, 20, 20)),
            IsVisible = false,
            Child = _runtimeFaultText
        };
        _runtimeFaultHost.ZIndex = int.MaxValue;

        Child = _root;

        _scene = scene ?? _engine.CreateScene();
        _ownsScene = scene is null;
        ApplyBrowserPerformanceDefaults(_scene);
        _simulationHost = new SceneSimulationHost3D(_scene, SceneSimulationExecutionMode3D.Automatic);
        _simulationHost.Faulted += OnSimulationHostFaulted;

        _controlAdapters = new Dictionary<ControlPlane3D, ControlPlaneRuntimeAdapter>();
        _creatingControlAdapters = new HashSet<ControlPlane3D>();
        _controlPlanes = new List<ControlPlane3D>();
        _controlPlaneSet = new HashSet<ControlPlane3D>();
        _controlPlanePickExclusions = new HashSet<Object3D>();
        _staleControlPlanesScratch = new List<ControlPlane3D>();
        _dirtyControlSnapshotQueue = new Queue<ControlPlaneRuntimeAdapter>();
        _dirtyControlSnapshotSet = new HashSet<ControlPlaneRuntimeAdapter>();
        _pressedKeys = new HashSet<Key>();

        _navigationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _navigationTimer.Tick += (_, _) => SafeDispatcherTick(OnSceneUpdateTimerTick, "scene update");

        _continuousRenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _continuousRenderTimer.Tick += (_, _) => SafeDispatcherTick(OnContinuousRenderTimerTick, "continuous render");

        _runtimeDiagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1d) };
        _runtimeDiagnosticsTimer.Tick += (_, _) => TryRuntimeDiagnosticsTick();

        _snapshotFallbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _snapshotFallbackTimer.Tick += (sender, args) => SafeDispatcherTick(() => OnSnapshotFallbackTimerTick(sender, args), "snapshot fallback");

        if (OperatingSystem.IsBrowser())
        {
            _scene.Performance.MaxLiveControlSnapshotsPerFrame = 1;
            _scene.Performance.UseConservativeSkinnedPicking = true;
            LiveControlSnapshotFps = 12d;
            TargetFps = 60d;
            UnlockedMaxFps = 60d;
        }

        InteractionManager = new SceneInteractionManager(_scene, RequestRender, GetViewportSize);
        InteractionManager.ObjectClicked += OnObjectClicked;
        InteractionManager.SelectionChanged += OnSelectionChanged;
        Adapters = new Avalonia3DAdapterRegistry(_scene);
        _lastFrameAllocatedBytes = GC.GetTotalAllocatedBytes(false);
        _lastAllocationWindowBytes = _lastFrameAllocatedBytes;
        _lastAllocationWindowTicks = Stopwatch.GetTimestamp();
        _lastGen0Count = GC.CollectionCount(0);
        _lastGen1Count = GC.CollectionCount(1);
        _lastGen2Count = GC.CollectionCount(2);

        try
        {
            EnsurePresenter();
        }
        catch
        {
            _simulationHost.Dispose();
            if (_presenter is not null)
            {
                _presenter.FrameRendered -= OnPresenterFrameRendered;
                if (_presenter is IScenePresenterDiagnostics3D presenterDiagnostics) presenterDiagnostics.Faulted -= OnPresenterFaulted;
                _presenter.Dispose();
                _presenter = null;
            }
            if (_ownsScene) _scene.Dispose();
            if (_ownsEngine && !_engine.IsDisposed) _engine.Dispose();
            throw;
        }
        _hiddenHost.ZIndex = -1;
        _root.Children.Add(_hiddenHost);
        _root.Children.Add(_centerCursorHost);
        _root.Children.Add(_performanceMetricsHost);
        _root.Children.Add(_runtimeFaultHost);
        LostFocus += (_, _) => OnViewportLostFocus();
        UpdatePerformanceMetricsVisibility();
        UpdateCenterCursorVisibility();
        UpdateContinuousRenderTimerState();
        UpdateRuntimeOptionsFromControl();

        SubscribeToScene(_scene);
        _lastSimulationProgressTicks = Stopwatch.GetTimestamp();
        EngineLog3D.Information("Scene3DControl.Lifecycle", $"Control {_controlId} constructed; backend={_presenter?.Kind}; sceneEngine={_scene.Engine.Id}; logFile={EngineLog3D.CurrentLogFilePath ?? "memory-only"}.");
    }

    private void SafeDispatcherTick(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            EnterRuntimeFaultState("Dispatcher." + name, ex);
        }
    }

    public event EventHandler<ScenePointerEventArgs>? ObjectClicked;
    public event EventHandler<SceneSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;
    public event EventHandler<SceneRuntimeFaultedEventArgs3D>? RuntimeFaulted;

    public string ControlId => _controlId;
    public bool IsRuntimeFaulted => _runtimeFault is not null;
    public Exception? RuntimeFault => _runtimeFault;
    public string? RuntimeFaultSubsystem => _runtimeFaultSubsystem;
    public string? CurrentLogFilePath => EngineLog3D.CurrentLogFilePath;
    public string? LastAutomaticDiagnosticPath => _lastAutomaticDiagnosticPath;

    public SceneInteractionManager InteractionManager { get; }
    public Avalonia3DAdapterRegistry Adapters { get; }

    public bool ShowPerformanceMetrics
    {
        get => GetValue(ShowPerformanceMetricsProperty);
        set => SetValue(ShowPerformanceMetricsProperty, value);
    }

    public double LiveControlSnapshotFps
    {
        get => GetValue(LiveControlSnapshotFpsProperty);
        set => SetValue(LiveControlSnapshotFpsProperty, value);
    }

    public bool EnableLiveControlFallbackRefresh
    {
        get => GetValue(EnableLiveControlFallbackRefreshProperty);
        set => SetValue(EnableLiveControlFallbackRefreshProperty, value);
    }

    public SceneNavigationMode NavigationMode
    {
        get => GetValue(NavigationModeProperty);
        set => SetValue(NavigationModeProperty, value);
    }

    public bool EnableSceneNavigation
    {
        get => GetValue(EnableSceneNavigationProperty);
        set => SetValue(EnableSceneNavigationProperty, value);
    }

    public SceneMouseLookMode MouseLookMode
    {
        get => GetValue(MouseLookModeProperty);
        set => SetValue(MouseLookModeProperty, value);
    }

    public bool ShowCenterCursor
    {
        get => GetValue(ShowCenterCursorProperty);
        set => SetValue(ShowCenterCursorProperty, value);
    }

    public bool ContinuousRendering
    {
        get => GetValue(ContinuousRenderingProperty);
        set => SetValue(ContinuousRenderingProperty, value);
    }

    public double ContinuousRenderingFps
    {
        get => GetValue(ContinuousRenderingFpsProperty);
        set
        {
            SetValue(ContinuousRenderingFpsProperty, value);
            SetValue(TargetFpsProperty, value);
        }
    }

    public bool FpsLockEnabled
    {
        get => GetValue(FpsLockEnabledProperty);
        set => SetValue(FpsLockEnabledProperty, value);
    }

    public double TargetFps
    {
        get => GetValue(TargetFpsProperty);
        set => SetValue(TargetFpsProperty, value);
    }

    public double UnlockedMaxFps
    {
        get => GetValue(UnlockedMaxFpsProperty);
        set => SetValue(UnlockedMaxFpsProperty, value);
    }

    public bool FrameInterpolationEnabled
    {
        get => GetValue(FrameInterpolationEnabledProperty);
        set => SetValue(FrameInterpolationEnabledProperty, value);
    }

    public bool AutomaticSceneUpdates
    {
        get => GetValue(AutomaticSceneUpdatesProperty);
        set => SetValue(AutomaticSceneUpdatesProperty, value);
    }

    public double FixedUpdateFramesPerSecond
    {
        get => GetValue(FixedUpdateFramesPerSecondProperty);
        set => SetValue(FixedUpdateFramesPerSecondProperty, value);
    }

    public int MaximumCatchUpSteps
    {
        get => GetValue(MaximumCatchUpStepsProperty);
        set => SetValue(MaximumCatchUpStepsProperty, value);
    }

    public double SimulationTimeScale
    {
        get => GetValue(SimulationTimeScaleProperty);
        set => SetValue(SimulationTimeScaleProperty, value);
    }

    public bool IsSimulationPaused
    {
        get => GetValue(IsSimulationPausedProperty);
        set => SetValue(IsSimulationPausedProperty, value);
    }

    public bool AdaptivePerformanceEnabled
    {
        get => GetValue(AdaptivePerformanceEnabledProperty);
        set => SetValue(AdaptivePerformanceEnabledProperty, value);
    }

    public SceneSimulationExecutionMode3D SimulationExecutionMode
    {
        get => GetValue(SimulationExecutionModeProperty);
        set => SetValue(SimulationExecutionModeProperty, value);
    }

    public FreeFlyNavigationSettings FreeFlySettings => _freeFlySettings;

    public PersonNavigationSettings PersonSettings => _personSettings;
    public Engine3D Engine => _engine;
    public Scene3D Scene
    {
        get => _scene;
        set
        {
            if (!ReferenceEquals(_scene, value)) SetScene(value, takeOwnership: false);
        }
    }

    /// <summary>
    /// Assigns a scene and explicitly defines whether the control must dispose it.
    /// The regular <see cref="Scene"/> setter treats externally supplied scenes as caller-owned.
    /// </summary>
    public void SetScene(Scene3D value, bool takeOwnership)
    {
        ThrowIfDisposed();
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value.IsDisposed) throw new ObjectDisposedException(nameof(value));
        if (ReferenceEquals(_scene, value))
        {
            _ownsScene = takeOwnership;
            return;
        }

        var previous = _scene;
        var disposePrevious = _ownsScene;
        EngineLog3D.Information("Scene3DControl.Lifecycle", $"Control {_controlId} switching scene; previousOwned={disposePrevious}; nextOwned={takeOwnership}; previousDisposed={previous.IsDisposed}; nextEngine={value.Engine.Id}.");

        // Stop/rebind the simulation owner before mutating control-owned scene state. A bounded
        // shutdown failure therefore leaves the previous control/presenter bindings intact.
        _simulationHost.SetScene(value);

        UnsubscribeFromScene(previous);
        ClearControlAdapters();
        _scene = value;
        _renderScheduler.Reset();
        _ownsScene = takeOwnership;
        ApplyBrowserPerformanceDefaults(_scene);
        SubscribeToScene(_scene);
        InteractionManager.SetScene(_scene);
        Adapters.SetScene(_scene);
        UpdateRuntimeOptionsFromControl();

        if (_presenter is not null)
        {
            _presenter.Scene = _scene;
        }

        SyncControlAdapters();
        RequestRender();
        if (disposePrevious)
        {
            previous.Dispose();
        }
    }

    /// <summary>
    /// Queues a mutation for deterministic execution on the scene simulation owner.
    /// Use this API when <see cref="SimulationExecutionMode"/> may use a dedicated thread.
    /// </summary>
    public long EnqueueSceneCommand(Action<Scene3D> command)
    {
        ThrowIfDisposed();
        var sequence = Scene.Commands.Enqueue(command);
        RequestSimulationCommandPump();
        return sequence;
    }

    /// <summary>
    /// Queues a mutation and completes after the simulation owner has executed it.
    /// </summary>
    public Task EnqueueSceneCommandAsync(Action<Scene3D> command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var completion = Scene.Commands.EnqueueAsync(command, cancellationToken);
        RequestSimulationCommandPump();
        return completion;
    }

    public Object3D? SelectedObject => InteractionManager.SelectedObject;
    public Object3D? HoveredObject => InteractionManager.HoveredObject;
    public RenderStats LastRenderStats => _lastRenderStats;

    public string CreateDiagnosticReport(int maximumLogEntries = 4096)
    {
        var report = EngineDiagnosticReport3D.Create(Scene, _presenter?.Kind ?? BackendKind.Unknown, _lastRenderStats, maximumLogEntries);
        return report + Environment.NewLine + Environment.NewLine + "Viewport runtime" + Environment.NewLine + "----------------" + Environment.NewLine + CaptureRuntimeHealthSnapshotSafely("manual-report");
    }

    public bool TryWriteDiagnosticReport(string path, out string? error, int maximumLogEntries = 4096)
    {
        error = null;
        if (OperatingSystem.IsBrowser())
        {
            error = "Direct file output is unavailable in the browser. Call ExportDiagnosticReport() from a user action.";
            return false;
        }
        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            File.WriteAllText(fullPath, CreateDiagnosticReport(maximumLogEntries), new UTF8Encoding(false));
            EngineLog3D.Information("Diagnostics", $"Control {_controlId} diagnostic report written to '{fullPath}'.");
            EngineLog3D.Flush();
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            EngineLog3D.Error("Diagnostics", $"Control {_controlId} failed to write diagnostic report.", exception);
            return false;
        }
    }

    /// <summary>Writes a desktop report or starts a browser text-file download.</summary>
    public string? ExportDiagnosticReport(string? pathOrFileName = null, int maximumLogEntries = 4096)
    {
        ThrowIfDisposed();
        var fileName = string.IsNullOrWhiteSpace(pathOrFileName)
            ? $"Avalonia3D-{EngineLog3D.SessionId}-{_controlId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.diagnostic.txt"
            : pathOrFileName;
        if (OperatingSystem.IsBrowser())
        {
            if (_presenter is not IBrowserDiagnosticExportPresenter3D exporter)
                throw new InvalidOperationException("The active browser presenter cannot export diagnostic text.");
            exporter.ExportTextFile(Path.GetFileName(fileName), CreateDiagnosticReport(maximumLogEntries));
            EngineLog3D.Information("Diagnostics", $"Control {_controlId} requested browser diagnostic download '{Path.GetFileName(fileName)}'.");
            return null;
        }

        var fullPath = Path.IsPathRooted(fileName)
            ? fileName
            : Path.Combine(EngineLog3D.LogDirectory ?? Path.GetTempPath(), fileName);
        if (!TryWriteDiagnosticReport(fullPath, out var error, maximumLogEntries))
            throw new IOException("Unable to export the Avalonia3D diagnostic report: " + error);
        return Path.GetFullPath(fullPath);
    }

    /// <summary>Explicitly clears a fail-fast runtime state after the application corrected its cause.</summary>
    public void ResetRuntimeFault(bool discardAccumulatedSimulationTime = true)
    {
        ThrowIfDisposed();
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("ResetRuntimeFault must be called on the Avalonia UI thread.");
        if (_runtimeFault is null) return;

        var previous = _runtimeFault;
        if (Scene.UpdateLoop.IsFaulted)
        {
            if (_simulationHost.UsesDedicatedThread)
            {
                var faultedScene = Scene;
                faultedScene.Commands.Enqueue(owner => owner.UpdateLoop.ResetFault(discardAccumulatedSimulationTime));
                if (!_simulationHost.TryWakeDedicatedWorker())
                    throw new InvalidOperationException("The dedicated simulation worker is unavailable; the runtime fault cannot be reset safely.");
            }
            else
            {
                Scene.UpdateLoop.ResetFault(discardAccumulatedSimulationTime);
            }
        }
        _runtimeFault = null;
        _runtimeFaultSubsystem = null;
        _runtimeFaultHost.IsVisible = false;
        _runtimeFaultText.Text = string.Empty;
        _renderScheduler.Reset();
        (_presenter as IScenePresenterDiagnostics3D)?.ResetFaultState();
        _lastSimulationProgressTicks = Stopwatch.GetTimestamp();
        EngineLog3D.Warning("RuntimeFault", $"Control {_controlId} runtime fault was explicitly reset by the application. Previous={previous.GetType().Name}: {previous.Message}");
        UpdateNavigationTimerState();
        UpdateContinuousRenderTimerState();
        RequestPresenterRenderOnly();
    }

    public T Add<T>(T obj) where T : Object3D
    {
        ArgumentNullException.ThrowIfNull(obj);
        var queued = Scene.World.HasSimulationOwner && !Scene.World.IsCurrentThreadSimulationOwner;
        if (queued)
        {
            Scene.World.Mutate(scene => scene.Add(obj));
            _simulationHost.PumpCommands();
        }
        else
        {
            Scene.Add(obj);
        }

        if (!queued && obj is ControlPlane3D plane)
        {
            TrackControlPlane(plane);
            EnsureControlAdapter(plane);
        }

        RequestRender();
        return obj;
    }

    public Object3D Add(Control avaloniaControl)
    {
        ArgumentNullException.ThrowIfNull(avaloniaControl);
        var added = Adapters.Add(avaloniaControl);
        var queued = added.OwnerScene is null;
        if (queued)
        {
            _simulationHost.PumpCommands();
        }
        else if (added is ControlPlane3D plane)
        {
            TrackControlPlane(plane);
            EnsureControlAdapter(plane);
        }

        RequestRender();
        return added;
    }

    public ControlPlane3D AddLiveControl(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        var plane = new ControlPlane3D(control)
        {
            Width = ToWorldUnits(control.Width, 320d),
            Height = ToWorldUnits(control.Height, 180d),
            RenderScale = OperatingSystem.IsBrowser() ? 1.25d : 2d
        };

        plane.Collider = new PlaneCollider3D { Size = new Vector2(plane.Width, plane.Height), LocalNormal = Vector3.UnitZ };
        Add(plane);
        return plane;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_disposed) return;
        EngineLog3D.Information("Scene3DControl.Lifecycle", $"Control {_controlId} attached; bounds={Bounds.Width:0.##}x{Bounds.Height:0.##}; visible={IsVisible}.");
        _runtimeDiagnosticsTimer.Start();
        _lastSimulationProgressTicks = Stopwatch.GetTimestamp();
        Volatile.Write(ref _firstOutstandingRenderRequestTicks, 0);
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) is null)
            {
                return;
            }

            SyncControlAdapters();
            UpdateSnapshotTimerState();
            UpdateNavigationTimerState();
            UpdateContinuousRenderTimerState();
            RequestRender();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        EngineLog3D.Information("Scene3DControl.Lifecycle", $"Control {_controlId} detached; requests={_renderRequestCount}; frames={_renderedFrameCount}; simulationTick={Scene.UpdateLoop.SimulationTick}.");
        _runtimeDiagnosticsTimer.Stop();
        _snapshotFallbackTimer.Stop();
        _navigationTimer.Stop();
        _renderScheduler.Reset();
        _continuousRenderTimer.Stop();
        _unlockedRenderPending = false;
        _browserContinuousRenderScheduled = false;
        Volatile.Write(ref _firstOutstandingRenderRequestTicks, 0);
        _lastFramePacingLogTicks = 0;
        _presentationWindowStartTicks = 0;
        _presentationWindowFrameCount = 0;
        _lastPresentedFramesPerSecond = 0d;
        _lastPresentationJitterMilliseconds = 0d;
        _lastFrameRenderedTicks = 0;
        _longFrameCount = 0;
        _worstFrameMilliseconds = 0d;
        ClearPendingMouseLookDelta();
        ClearPressedKeys();
        EndMouseLook();
        _isPointerInsideScene = false;
        RestoreCenterLockedCursor();
        ClearControlAdapters();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _isPointerInsideScene = true;
        if (ShouldUseCenterLockedMouseLook())
        {
            BeginCenterLockedMouseLook(e);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isPointerInsideScene = false;

        // CenterLocked behaves like a game pointer-lock mode: once active, the
        // logical cursor stays at the viewport center until Escape/mode change.
        // Do not stop it just because the OS cursor left the control bounds.
        if (!IsCenterLockedMouseLookActive && !ShouldSuppressHoverPicking(e))
        {
            ClearControlHover(e);
            InteractionManager.ClearHover();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnPointerPressed(e);
        Focus();

        var props = e.GetCurrentPoint(this).Properties;
        if (MouseLookMode == SceneMouseLookMode.ButtonDrag && (props.IsRightButtonPressed || props.IsMiddleButtonPressed))
        {
            BeginMouseLook(e);
            e.Handled = true;
            return;
        }

        if (ShouldUseCenterLockedMouseLook())
        {
            BeginCenterLockedMouseLook(e);
            _lastCenterLockedPointerEvent = e;
            RequestPresenterPointerLock();

            var center = GetCenterViewportPoint();
            if (TryHandleControlPointerPressed(e, center))
            {
                e.Handled = true;
                return;
            }

            ClearActiveControlState(e);
            InteractionManager.HandlePointerPressed(this, e, GetCenterViewportPosition());
            e.Handled = true;
            return;
        }

        if (TryHandleControlPointerPressed(e))
        {
            e.Handled = true;
            return;
        }

        ClearActiveControlState(e);
        InteractionManager.HandlePointerPressed(this, e, GetViewportPosition(e));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnPointerMoved(e);

        if (IsButtonDragMouseLookActive)
        {
            ApplyMouseLookFromPointer(e);
            e.Handled = true;
            return;
        }

        if (ShouldUseCenterLockedMouseLook())
        {
            if (!IsCenterLockedMouseLookActive)
            {
                BeginCenterLockedMouseLook(e);
            }

            _lastCenterLockedPointerEvent = e;
            if (!IsPresenterPointerLockActive())
            {
                ApplyMouseLookFromPointer(e);
            }

            UpdateCenterLockedHover(e);
            e.Handled = true;
            return;
        }

        if (ShouldSuppressHoverPicking(e))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleControlPointerMoved(e))
        {
            e.Handled = true;
            return;
        }

        ClearControlHover(e);
        InteractionManager.HandlePointerMoved(this, e, GetViewportPosition(e));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnPointerReleased(e);

        if (IsButtonDragMouseLookActive)
        {
            EndMouseLook(e);
            e.Handled = true;
            return;
        }

        if (IsCenterLockedMouseLookActive)
        {
            _lastCenterLockedPointerEvent = e;
            var center = GetCenterViewportPoint();
            if (_activeControlAdapter is not null && ExecuteForwardedControlInput(() => TryHandleControlPointerReleased(e, center)))
            {
                CaptureCenterLockedPointer(e.Pointer);
                e.Handled = true;
                return;
            }

            InteractionManager.HandlePointerReleased(this, e, GetCenterViewportPosition());
            CaptureCenterLockedPointer(e.Pointer);
            UpdateCenterLockedHover(e);
            e.Handled = true;
            return;
        }

        if (_activeControlAdapter is not null && ExecuteForwardedControlInput(() => TryHandleControlPointerReleased(e)))
        {
            e.Handled = true;
            return;
        }

        InteractionManager.HandlePointerReleased(this, e, GetViewportPosition(e));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnPointerWheelChanged(e);

        if (IsCenterLockedMouseLookActive)
        {
            if (TryHandleControlPointerWheel(e, GetCenterViewportPoint()))
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            return;
        }

        if (!IsButtonDragMouseLookActive && TryHandleControlPointerWheel(e))
        {
            e.Handled = true;
            return;
        }

        InteractionManager.HandlePointerWheel(this, e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnKeyDown(e);
        EngineLog3D.Debug("Input", $"Control {_controlId} key-down {e.Key}; focused={IsFocused}; navigation={NavigationMode}; forwardedControl={_focusedControlAdapter is not null}.");

        if (e.Key == Key.Escape)
        {
            ClearPressedKeys();
            _focusedControlAdapter?.ClearFocus();
            _focusedControlAdapter = null;
            EndMouseLook();
            e.Handled = true;
            return;
        }

        if (_focusedControlAdapter is not null)
        {
            var capturesKeyboard = _focusedControlAdapter.ShouldCaptureKeyboardInput;
            var handledByControl = ExecuteForwardedControlInput(() => _focusedControlAdapter.HandleKeyDown(e));
            UpdateFocusedControlAdapterState();
            if (handledByControl || capturesKeyboard)
            {
                ClearPressedKeys();
                UpdateNavigationTimerState();
                e.Handled = true;
                RequestRender();
                return;
            }
        }

        if (NavigationMode == SceneNavigationMode.Person && e.Key == Key.Space)
        {
            TryStartPersonJump();
            e.Handled = true;
            return;
        }

        if (IsNavigationKey(e.Key))
        {
            AddPressedKey(e.Key);
            UpdateNavigationTimerState();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnKeyUp(e);
        EngineLog3D.Debug("Input", $"Control {_controlId} key-up {e.Key}; focused={IsFocused}; navigation={NavigationMode}; forwardedControl={_focusedControlAdapter is not null}.");
        if (_focusedControlAdapter is not null)
        {
            var capturesKeyboard = _focusedControlAdapter.ShouldCaptureKeyboardInput;
            var handledByControl = ExecuteForwardedControlInput(() => _focusedControlAdapter.HandleKeyUp(e));
            UpdateFocusedControlAdapterState();
            if (handledByControl || capturesKeyboard)
            {
                RemovePressedKey(e.Key);
                UpdateNavigationTimerState();
                e.Handled = true;
                RequestRender();
                return;
            }
        }

        if (IsNavigationKey(e.Key))
        {
            RemovePressedKey(e.Key);
            UpdateNavigationTimerState();
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnTextInput(e);
        if (_focusedControlAdapter is not null)
        {
            var capturesKeyboard = _focusedControlAdapter.ShouldCaptureKeyboardInput;
            var handledByControl = ExecuteForwardedControlInput(() => _focusedControlAdapter.HandleTextInput(e));
            UpdateFocusedControlAdapterState();
            if (handledByControl || capturesKeyboard)
            {
                e.Handled = true;
                RequestRender();
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            UpdateCenterCursorVisibility();
            RequestRender();
        }
        else if (change.Property == LiveControlSnapshotFpsProperty || change.Property == EnableLiveControlFallbackRefreshProperty)
        {
            UpdateSnapshotTimerState();
        }
        else if (change.Property == ShowPerformanceMetricsProperty)
        {
            UpdatePerformanceMetricsVisibility();
            RequestRender();
        }
        else if (change.Property == ShowCenterCursorProperty)
        {
            UpdateCenterCursorVisibility();
        }
        else if (change.Property == ContinuousRenderingProperty || change.Property == ContinuousRenderingFpsProperty ||
                 change.Property == FpsLockEnabledProperty || change.Property == TargetFpsProperty || change.Property == UnlockedMaxFpsProperty)
        {
            UpdateContinuousRenderTimerState();
            UpdateNavigationTimerState();
        }
        else if (change.Property == FrameInterpolationEnabledProperty || change.Property == FixedUpdateFramesPerSecondProperty ||
                 change.Property == MaximumCatchUpStepsProperty ||
                 change.Property == SimulationTimeScaleProperty || change.Property == IsSimulationPausedProperty ||
                 change.Property == AdaptivePerformanceEnabledProperty || change.Property == SimulationExecutionModeProperty)
        {
            UpdateRuntimeOptionsFromControl();
        }
        else if (change.Property == AutomaticSceneUpdatesProperty)
        {
            UpdateNavigationTimerState();
        }
        else if (change.Property == NavigationModeProperty || change.Property == EnableSceneNavigationProperty || change.Property == MouseLookModeProperty)
        {
            PublishNavigationInputSnapshot(consumeTransientInput: false);
            if (!EnableSceneNavigation || NavigationMode == SceneNavigationMode.None)
            {
                ClearPendingMouseLookDelta();
            }
            if (!ShouldUseCenterLockedMouseLook())
            {
                EndMouseLook();
            }
            else if (_isPointerInsideScene)
            {
                BeginCenterLockedMouseLook();
            }

            UpdateNavigationTimerState();
            UpdateCenterCursorVisibility();
        }
    }

    private static Grid CreateCenterCursorHost()
    {
        static Border Line(double width, double height, HorizontalAlignment horizontal, VerticalAlignment vertical, Thickness margin)
            => new()
            {
                Width = width,
                Height = height,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Margin = margin,
                Background = Brushes.White
            };

        var host = new Grid
        {
            Width = 24d,
            Height = 24d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            IsVisible = false
        };

        host.Children.Add(Line(2d, 7d, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0d, 0d, 0d, 0d)));
        host.Children.Add(Line(2d, 7d, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0d, 0d, 0d, 0d)));
        host.Children.Add(Line(7d, 2d, HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(0d, 0d, 0d, 0d)));
        host.Children.Add(Line(7d, 2d, HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0d, 0d, 0d, 0d)));
        return host;
    }

    private void EnsurePresenter()
    {
        ThrowIfDisposed();
        if (_presenter is not null)
        {
            return;
        }

        _presenter = _engine.Services.GetRequiredService<IScenePresenterFactory>().CreatePresenter();
        _presenter.FrameRendered += OnPresenterFrameRendered;
        if (_presenter is IScenePresenterDiagnostics3D presenterDiagnostics) presenterDiagnostics.Faulted += OnPresenterFaulted;
        _presenter.Scene = Scene;
        _presenter.View.IsHitTestVisible = false;
        _presenter.View.ZIndex = 0;
        _root.Children.Add(_presenter.View);
        EngineLog3D.Information("Scene3DControl", $"Presenter created: {_presenter.GetType().FullName}; backend={_presenter.Kind}.");
        UpdateCenterCursorVisibility();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _runtimeDiagnosticsTimer.Stop();
        _snapshotFallbackTimer.Stop();
        _navigationTimer.Stop();
        _continuousRenderTimer.Stop();
        UnsubscribeFromScene(_scene);
        _simulationHost.Faulted -= OnSimulationHostFaulted;
        _simulationHost.Dispose();
        _renderScheduler.Reset();
        ClearPressedKeys();
        EndMouseLook();
        ClearControlAdapters();
        InteractionManager.ObjectClicked -= OnObjectClicked;
        InteractionManager.SelectionChanged -= OnSelectionChanged;

        if (_presenter is not null)
        {
            _presenter.FrameRendered -= OnPresenterFrameRendered;
            if (_presenter is IScenePresenterDiagnostics3D presenterDiagnostics) presenterDiagnostics.Faulted -= OnPresenterFaulted;
            _root.Children.Remove(_presenter.View);
            _presenter.Dispose();
            _presenter = null;
        }

        if (_ownsScene)
        {
            _scene.Dispose();
        }

        if (_ownsEngine)
        {
            _engine.Dispose();
        }

        ObjectClicked = null;
        SelectionChanged = null;
        FrameRendered = null;
        RuntimeFaulted = null;
        EngineLog3D.Information("Scene3DControl.Lifecycle", $"Control {_controlId} disposed; timers, presenter and owned scene released. requests={_renderRequestCount}; frames={_renderedFrameCount}.");
        EngineLog3D.Flush();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void SubscribeToScene(Scene3D scene)
    {
        scene.SceneChangedDetailed += OnSceneChanged;
        scene.InternalFixedUpdateCompleted += OnSceneFixedUpdate;
        scene.UpdateActivityChanged += OnSceneUpdateActivityChanged;
        scene.UpdateLoop.StateChanged += OnSceneUpdateLoopStateChanged;
    }

    private void UnsubscribeFromScene(Scene3D scene)
    {
        scene.SceneChangedDetailed -= OnSceneChanged;
        scene.InternalFixedUpdateCompleted -= OnSceneFixedUpdate;
        scene.UpdateActivityChanged -= OnSceneUpdateActivityChanged;
        scene.UpdateLoop.StateChanged -= OnSceneUpdateLoopStateChanged;
    }

    private void OnSceneUpdateLoopStateChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSceneUpdateLoopStateChanged(sender, e), DispatcherPriority.Background);
            return;
        }
        if (Scene.UpdateLoop.Fault is { } fault)
        {
            EnterRuntimeFaultState("Simulation.UpdateLoop", fault);
            return;
        }
        UpdateNavigationTimerState();
        RequestRender();
    }

    private void OnSceneUpdateActivityChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        RequestSimulationCommandPump();
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                RequestSimulationCommandPump();
                UpdateNavigationTimerState();
            }, DispatcherPriority.Background);
            return;
        }
        UpdateNavigationTimerState();
    }

    private void RequestSimulationCommandPump()
    {
        if (_disposed) return;
        if (_simulationHost.TryWakeDedicatedWorker()) return;
        if (Dispatcher.UIThread.CheckAccess())
        {
            _simulationHost.PumpCommands();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            // The mode may have changed while this callback was queued. Pump only on the
            // Avalonia owner when there is still no dedicated simulation worker.
            if (!_simulationHost.TryWakeDedicatedWorker()) _simulationHost.PumpCommands();
        }, DispatcherPriority.Background);
    }

    private void OnSceneFixedUpdate(Scene3D scene, in SceneFixedUpdateContext3D context)
    {
        if (!ReferenceEquals(scene, _scene)) return;
        var input = Volatile.Read(ref _publishedNavigationInput);
        if (!input.Enabled || input.Mode == SceneNavigationMode.None) return;

        var consumeTransients = input.Sequence != _lastConsumedNavigationInputSequence;
        if (consumeTransients) _lastConsumedNavigationInputSequence = input.Sequence;
        if (consumeTransients && input.SynchronizeCameraAngles) SyncCameraAnglesFromForward();
        if (consumeTransients && input.MouseDelta.LengthSquared() > 0.000001f) ApplyMouseLookCore(input.MouseDelta, input);

        if (input.Mode == SceneNavigationMode.Person)
        {
            if (consumeTransients && input.JumpRequested && _personController.IsGrounded)
                _personController.Jump(MathF.Max(input.PersonJumpSpeed, 0f));
            StepPersonNavigation(context.DeltaSeconds, input);
        }
        else if (input.Mode == SceneNavigationMode.FreeFly)
        {
            StepFreeFlyNavigation(context.DeltaSeconds, input);
        }

        Volatile.Write(ref _simulationPersonGrounded, _personGrounded ? 1 : 0);
        Volatile.Write(ref _simulationPersonVerticalVelocity, _personVelocity.Y);
    }

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        if (_disposed) return;
        if (!_simulationHost.IsCurrentThreadOwner) _simulationHost.TryWakeDedicatedWorker();
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSceneChanged(sender, e), DispatcherPriority.Background);
            return;
        }
        if (e.Kinds == SceneChangeFlags3D.Metadata)
        {
            return;
        }

        if (e.Contains(SceneChangeKind.Structure))
        {
            SyncControlAdapters();
        }
        else if (e.Contains(SceneChangeKind.Control))
        {
            EnqueueDirtyControlSnapshot(e.Source as ControlPlane3D);
        }

        UpdateNavigationTimerState();
        if (ContinuousRendering)
        {
            // The continuous scheduler already owns the next presentation. Enqueuing another
            // Avalonia render request for every transform/animation tick doubles request traffic
            // and distorts pacing diagnostics. Control snapshots are refreshed here because the
            // scheduled presenter-only request intentionally avoids touching Avalonia controls.
            if (e.Contains(SceneChangeKind.Structure) || e.Contains(SceneChangeKind.Control))
            {
                RefreshDirtyControlSnapshots();
            }

            Interlocked.Increment(ref _coalescedContinuousSceneInvalidationCount);
            return;
        }

        RequestRender();
    }

    private void OnObjectClicked(object? sender, ScenePointerEventArgs e)
    {
        ObjectClicked?.Invoke(this, e);
    }

    private void OnSelectionChanged(object? sender, SceneSelectionChangedEventArgs e)
    {
        SelectionChanged?.Invoke(this, e);
    }


    private bool IsForwardingControlInput => _forwardedControlInputDepth > 0;

    private bool ExecuteForwardedControlInput(Func<bool> action)
    {
        _forwardedControlInputDepth++;
        try
        {
            return action();
        }
        finally
        {
            _forwardedControlInputDepth--;
        }
    }

    private void RequestRender()
    {
        if (TopLevel.GetTopLevel(this) is not null)
        {
            RefreshDirtyControlSnapshots();
        }

        RequestPresenterRenderOnly();
    }

    private void RequestPresenterRenderOnly()
    {
        if (_runtimeFault is not null || _disposed) return;
        Interlocked.Increment(ref _renderRequestCount);
        var now = Stopwatch.GetTimestamp();
        Volatile.Write(ref _lastRenderRequestTicks, now);
        Interlocked.CompareExchange(ref _firstOutstandingRenderRequestTicks, now, 0);
        _presenter?.RequestRender();
    }

    private void RequestUnlockedFrameSoon()
    {
        if (_runtimeFault is not null || _unlockedRenderPending || !ContinuousRendering || FpsLockEnabled || TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        _unlockedRenderPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _unlockedRenderPending = false;
            if (ContinuousRendering && !FpsLockEnabled && TopLevel.GetTopLevel(this) is not null)
            {
                RequestPresenterRenderOnly();
            }
        }, DispatcherPriority.Render);
    }

    private void UpdateContinuousRenderTimerState()
    {
        if (_disposed || _runtimeFault is not null || TopLevel.GetTopLevel(this) is null)
        {
            _continuousRenderTimer.Stop();
            _browserContinuousRenderScheduled = false;
            return;
        }

        var target = FpsLockEnabled ? TargetFps : UnlockedMaxFps;
        target = System.Math.Clamp(target <= 0d ? 60d : target, 1d, 500d);
        _continuousRenderTimer.Interval = TimeSpan.FromMilliseconds(1000d / target);

        if (OperatingSystem.IsBrowser())
        {
            if (_continuousRenderTimer.IsEnabled)
            {
                _continuousRenderTimer.Stop();
            }

            if (ContinuousRendering)
            {
                ScheduleBrowserContinuousFrame();
            }
            return;
        }

        if (ContinuousRendering && TopLevel.GetTopLevel(this) is not null && FpsLockEnabled)
        {
            if (!_continuousRenderTimer.IsEnabled)
            {
                _continuousRenderTimer.Start();
            }
        }
        else if (_continuousRenderTimer.IsEnabled)
        {
            _continuousRenderTimer.Stop();
        }

        if (ContinuousRendering && !FpsLockEnabled)
        {
            RequestUnlockedFrameSoon();
        }
    }

    private void ScheduleBrowserContinuousFrame()
    {
        if (!OperatingSystem.IsBrowser() ||
            _runtimeFault is not null ||
            _browserContinuousRenderScheduled ||
            !ContinuousRendering ||
            TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        _browserContinuousRenderScheduled = true;
        try
        {
            if (ContinuousRendering && TopLevel.GetTopLevel(this) is not null)
            {
                RequestPresenterRenderOnly();
            }
        }
        finally
        {
            _browserContinuousRenderScheduled = false;
        }
    }


    private double EffectiveTargetFps => FpsLockEnabled ? System.Math.Clamp(TargetFps, 1d, 500d) : System.Math.Clamp(UnlockedMaxFps, 1d, 500d);

    private void UpdateRuntimeOptionsFromControl()
    {
        var executionMode = SimulationExecutionMode;
        var interpolationEnabled = FrameInterpolationEnabled;
        var fixedUpdatesPerSecond = System.Math.Clamp(FixedUpdateFramesPerSecond, 1d, 1000d);
        var maximumCatchUpSteps = System.Math.Clamp(MaximumCatchUpSteps, 1, 1024);
        var timeScale = System.Math.Clamp(SimulationTimeScale, 0d, 100d);
        var paused = IsSimulationPaused;
        var adaptive = AdaptivePerformanceEnabled;

        // Establish the requested host first. Configuration is then submitted through the same
        // owner command path as application mutations, including during strict ownership.
        _simulationHost.SetMode(executionMode);
        Scene.World.Mutate(scene =>
        {
            scene.FrameInterpolator.Enabled = interpolationEnabled;
            scene.UpdateLoop.FixedUpdatesPerSecond = fixedUpdatesPerSecond;
            scene.UpdateLoop.MaximumCatchUpSteps = maximumCatchUpSteps;
            scene.UpdateLoop.TimeScale = timeScale;
            scene.UpdateLoop.IsPaused = paused;
            scene.AdaptivePerformance.Enabled = adaptive;
            scene.Performance.AdaptivePerformanceEnabled = adaptive;
        });
        _simulationHost.PumpCommands();

        PublishNavigationInputSnapshot(consumeTransientInput: false);
        EngineLog3D.Information("Scene3DControl.Configuration", $"Control {_controlId}: simulation={executionMode}; automatic={AutomaticSceneUpdates}; fixedHz={fixedUpdatesPerSecond:0.###}; catchUp={maximumCatchUpSteps}; timeScale={timeScale:0.###}; paused={paused}; interpolation={interpolationEnabled}; continuous={ContinuousRendering}; fpsLock={FpsLockEnabled}; targetFps={EffectiveTargetFps:0.###}; navigation={EnableSceneNavigation}/{NavigationMode}/{MouseLookMode}.");
        UpdateNavigationTimerState();
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private void UpdateRuntimeStats(RenderStats stats, long presentedAtTicks)
    {
        var now = presentedAtTicks;
        if (ShowPerformanceMetrics)
        {
            var allocated = GC.GetTotalAllocatedBytes(false);
            var frameAllocated = allocated - _lastFrameAllocatedBytes;
            if (frameAllocated < 0) frameAllocated = 0;
            _lastFrameAllocatedBytes = allocated;

            if (_lastAllocationWindowTicks == 0)
            {
                _lastAllocationWindowTicks = now;
                _lastAllocationWindowBytes = allocated;
            }

            var allocElapsed = (now - _lastAllocationWindowTicks) * 1000d / Stopwatch.Frequency;
            if (allocElapsed >= 250d)
            {
                var allocDelta = allocated - _lastAllocationWindowBytes;
                _lastAllocatedMegabytesPerSecond = allocDelta <= 0 ? 0d : (allocDelta / (1024d * 1024d)) / (allocElapsed / 1000d);
                _lastAllocationWindowBytes = allocated;
                _lastAllocationWindowTicks = now;
            }

            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            stats.Gen0Collections = gen0 - _lastGen0Count;
            stats.Gen1Collections = gen1 - _lastGen1Count;
            stats.Gen2Collections = gen2 - _lastGen2Count;
            _lastGen0Count = gen0;
            _lastGen1Count = gen1;
            _lastGen2Count = gen2;

            stats.AllocatedBytesPerFrame = frameAllocated;
            stats.AllocatedMegabytesPerSecond = _lastAllocatedMegabytesPerSecond;
            stats.ManagedAllocatedBytes = allocated;
            stats.ManagedHeapBytes = GC.GetTotalMemory(false);
        }
        // The first frame has no preceding presentation timestamp. Report it as unknown
        // instead of substituting backend execution time and producing a fictitious FPS.
        var previousPresentationTicks = Volatile.Read(ref _lastFrameRenderedTicks);
        var expectedMs = 1000d / EffectiveTargetFps;
        stats.FrameTotalMilliseconds = 0d;
        stats.InstantaneousPresentedFramesPerSecond = 0d;
        stats.PresentationJitterMilliseconds = _lastPresentationJitterMilliseconds;
        if (previousPresentationTicks != 0 && now > previousPresentationTicks)
        {
            var realFrameMs = (now - previousPresentationTicks) * 1000d / Stopwatch.Frequency;
            stats.FrameTotalMilliseconds = realFrameMs;
            stats.InstantaneousPresentedFramesPerSecond = realFrameMs > 0d ? 1000d / realFrameMs : 0d;
            _lastPresentationJitterMilliseconds = System.Math.Abs(realFrameMs - expectedMs);
            stats.PresentationJitterMilliseconds = _lastPresentationJitterMilliseconds;
            stats.RenderScheduleDelayMilliseconds = System.Math.Max(0d, realFrameMs - expectedMs);
            stats.SchedulerDelayMilliseconds = stats.RenderScheduleDelayMilliseconds;
            RecordFramePacing(realFrameMs, expectedMs, now);
        }

        if (_presentationWindowStartTicks == 0)
        {
            // The first presentation is the time baseline, not a complete frame interval.
            _presentationWindowStartTicks = now;
            _presentationWindowFrameCount = 0;
        }
        else
        {
            _presentationWindowFrameCount++;
            var presentationWindowMilliseconds = (now - _presentationWindowStartTicks) * 1000d / Stopwatch.Frequency;
            if (presentationWindowMilliseconds >= 500d)
            {
                _lastPresentedFramesPerSecond = _presentationWindowFrameCount * 1000d / presentationWindowMilliseconds;
                _presentationWindowStartTicks = now;
                _presentationWindowFrameCount = 0;
            }
        }

        stats.PresentedFramesPerSecond = _lastPresentedFramesPerSecond;
        stats.PresentedFrameCount = Interlocked.Read(ref _renderedFrameCount);
        Volatile.Write(ref _lastFrameRenderedTicks, now);

        stats.FpsLocked = FpsLockEnabled;
        stats.TargetFps = EffectiveTargetFps;
        stats.ContinuousRendering = ContinuousRendering;
        stats.FrameInterpolationEnabled = FrameInterpolationEnabled;
        stats.AdaptivePerformanceEnabled = AdaptivePerformanceEnabled;
        stats.InterpolationAlpha = Scene.FrameInterpolator.Alpha;
        stats.ControlSnapshotRefreshCount = _controlSnapshotRefreshesSinceLastFrame;
        stats.ControlSnapshotQueueHighWater = _controlSnapshotQueueHighWaterSinceLastFrame;
        stats.ControlPointerPickCount = _controlPickingRequestsSinceLastFrame;
        stats.ControlPlanePickTestCount = _controlPlanePickTestsSinceLastFrame;
        stats.PickingMilliseconds += _pickingMillisecondsSinceLastFrame;
        _controlSnapshotRefreshesSinceLastFrame = 0;
        _controlSnapshotQueueHighWaterSinceLastFrame = _dirtyControlSnapshotQueue.Count;
        _controlPickingRequestsSinceLastFrame = 0;
        _controlPlanePickTestsSinceLastFrame = 0;
        _pickingMillisecondsSinceLastFrame = 0d;

        Scene.AdaptivePerformance.Enabled = AdaptivePerformanceEnabled;
        Scene.AdaptivePerformance.RecordFrame(stats, Scene.Performance, EffectiveTargetFps);
        stats.AdaptiveQualityScale = Scene.AdaptivePerformance.QualityScale;
    }

    private void RecordFramePacing(double realFrameMilliseconds, double expectedMilliseconds, long nowTicks)
    {
        var longFrameThreshold = System.Math.Max(LongFrameThresholdMilliseconds, expectedMilliseconds * 2d);
        if (realFrameMilliseconds >= longFrameThreshold)
        {
            _longFrameCount++;
            _worstFrameMilliseconds = System.Math.Max(_worstFrameMilliseconds, realFrameMilliseconds);
        }

        if (_longFrameCount == 0 ||
            _lastFramePacingLogTicks != 0 &&
            (nowTicks - _lastFramePacingLogTicks) * 1000d / Stopwatch.Frequency < FramePacingLogIntervalMilliseconds)
        {
            return;
        }

        EngineLog3D.Warning(
            "FramePacing",
            $"{(_presenter?.Kind.ToString() ?? "Unknown")} presented {_longFrameCount} long frame(s); worst={_worstFrameMilliseconds:0.00} ms, target={expectedMilliseconds:0.00} ms. " +
            "Diagnostic FrameTotal/RenderScheduleDelay values are presentation intervals, not backend execution time.");
        _longFrameCount = 0;
        _worstFrameMilliseconds = 0d;
        _lastFramePacingLogTicks = nowTicks;
    }


    private void OnPresenterFrameRendered(object? sender, SceneFrameRenderedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnPresenterFrameRendered(sender, e), DispatcherPriority.Background);
            return;
        }

        try
        {
            var presentedAtTicks = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref _renderedFrameCount);
            Volatile.Write(ref _firstOutstandingRenderRequestTicks, 0);
            if (_renderedFrameCount == 1) EngineLog3D.Information("Rendering", $"Control {_controlId} presented its first frame; backend={e.Backend}; bounds={Bounds.Width:0.##}x{Bounds.Height:0.##}.");
            if (RefreshBrowserVisibilityState(force: true))
            {
                _renderScheduler.Reset();
                UpdateNavigationTimerState();
            }

            var statsForRuntime = e.Stats ?? new RenderStats();
            UpdateRuntimeStats(statsForRuntime, presentedAtTicks);
            _lastRenderStats = statsForRuntime;
            _scene.Engine.Profiler.RecordFrame(_controlId, e.Backend, statsForRuntime);

            if (OperatingSystem.IsBrowser())
            {
                AdvanceSceneFromPresentedFrame();
                ScheduleBrowserContinuousFrame();
            }
            else if (ContinuousRendering && !FpsLockEnabled)
            {
                AdvanceSceneFromPresentedFrame();
                RequestUnlockedFrameSoon();
            }

            try
            {
                FrameRendered?.Invoke(this, e);
            }
            catch (Exception subscriberEx)
            {
                EngineLog3D.Error("Scene3DControl", "FrameRendered subscriber failed.", subscriberEx);
            }

            if (!ShowPerformanceMetrics)
            {
                return;
            }

            _performanceFrameCount++;
            _performanceFrameMillisecondsLast = statsForRuntime.FrameTotalMilliseconds;
            _performanceFrameMillisecondsTotal += statsForRuntime.FrameTotalMilliseconds;

            if (_performanceWindowStartTicks == 0)
            {
                _performanceWindowStartTicks = Stopwatch.GetTimestamp();
                return;
            }

            var elapsedMilliseconds = (Stopwatch.GetTimestamp() - _performanceWindowStartTicks) * 1000d / Stopwatch.Frequency;
            if (elapsedMilliseconds < PerformanceMetricsUpdateIntervalMilliseconds)
            {
                return;
            }

            var fps = statsForRuntime.PresentedFramesPerSecond > 0d
                ? statsForRuntime.PresentedFramesPerSecond
                : _performanceFrameCount * 1000d / elapsedMilliseconds;
            var averageFrameMilliseconds = _performanceFrameMillisecondsTotal / System.Math.Max(_performanceFrameCount, 1);
            var stats = statsForRuntime;
            var cpuResources = _scene.Engine.Resources.CaptureSnapshot();

            // FrameRendered may be raised while Avalonia is inside a render pass.
            // Updating TextBlock.Text there invalidates the visual tree during render.
            // Apply the text later through the dispatcher instead.
            _pendingPerformanceMetricsText =
                $"FPS presented: {fps:0.0} | instant: {stats.InstantaneousPresentedFramesPerSecond:0.0}\n" +
                $"Frame interval: {_performanceFrameMillisecondsLast:0.00} ms | Avg: {averageFrameMilliseconds:0.00} ms | Jitter: {stats.PresentationJitterMilliseconds:0.00} ms\n" +
                $"Backend: {e.Backend}\n" +
                $"Objects: {stats.ObjectCount} | Renderables: {stats.RenderableCount} | Pickables: {stats.PickableCount} | Colliders: {stats.ColliderCount}\n" +
                $"HighScale: {stats.HighScaleInstanceCount} | Chunks: {stats.VisibleChunkCount}/{stats.TotalChunkCount} | Culled: {stats.CulledObjectCount}\n" +
                $"LOD D/S/P/B/C: {stats.LodDetailedCount}/{stats.LodSimplifiedCount}/{stats.LodProxyCount}/{stats.LodBillboardCount}/{stats.LodCulledCount} | PartInst: {stats.HighScaleVisiblePartInstanceCount}\n" +
                $"Draw: {stats.DrawCallCount} | Batches: {stats.InstancedBatchCount} | Tris: {stats.TriangleCount}\n" +
                $"Pipeline: mode={stats.RenderPipelineMode} deferred {OnOff(stats.DeferredActive)}/{OnOff(stats.DeferredRequested)} | GBuffer={OnOff(stats.GBufferActive)} targets={stats.GBufferTargetCount} | HDR={OnOff(stats.HdrActive)} tone={stats.ToneMappingMode} exp={stats.ToneMappingExposure:0.00} gamma={stats.ToneMappingGamma:0.00}\n" +
                $"SSAO: {OnOff(stats.SsaoActive)}/{OnOff(stats.SsaoRequested)} samples={stats.SsaoSampleCount} | Passes={stats.RenderPassCount} | MotionVec={OnOff(stats.MotionVectorsActive)}/{OnOff(stats.MotionVectorsRequested)} | Reason={stats.RenderPipelineReason}\n" +
                $"Particles: {stats.ParticleCount} in {stats.ParticleSystemCount} systems | ParticleVB: {stats.ParticleMeshUploadBytes / 1024d:0.0} KB | InstancedMesh: {stats.InstancedMeshInstanceCount} in {stats.InstancedMeshLayerCount} layers\n" +
                $"Models: imported={stats.ImportedModelCount} skinned={stats.SkinnedModelCount} animated={stats.AnimatedModelCount} | Skin matrices={stats.SkinMatrixCount} prim={stats.SkinnedPrimitiveCount} payload={stats.SkinningVertexPayloadBytes / 1024d:0.0} KB | GPUSkin={OnOff(stats.GpuSkinningActive)}/{OnOff(stats.GpuSkinningRequested)}\n" +
                $"Lights D/P/S: {stats.DirectionalLightCount}/{stats.PointLightCount}/{stats.SpotLightCount} | Skybox: {(stats.SkyboxEnabled ? "on" : "off")} mode={stats.SkyboxMode}\n" +
                $"TransformUpload: {stats.InstanceUploadBytes / (1024d * 1024d):0.00} MB | StateUpload: {stats.StateUploadBytes / 1024d:0.0} KB | TexUpload: {stats.TextureUploadBytes / (1024d * 1024d):0.00} MB\n" +
                $"MeshUpload: {stats.MeshUploadBytes / 1024d:0.0} KB | V/I: {stats.VertexBufferUploadBytes / 1024d:0.0}/{stats.IndexBufferUploadBytes / 1024d:0.0} KB | Tangent: {stats.TangentUploadBytes / 1024d:0.0} KB | WireIdx: {stats.WireframeIndexUploadBytes / 1024d:0.0} KB\n" +
                $"Surface: tangentMeshes={stats.TangentSpaceMeshCount} normalMapped={stats.NormalMappedMeshCount} wire/sil={stats.WireframeOverlayDrawCalls}/{stats.SilhouetteOverlayDrawCalls} | Geom: {stats.RenderGeometryCount} | VB/IB: {stats.VertexBufferUploadCount}/{stats.IndexBufferUploadCount} | PacketBytes: {stats.PacketBytes / 1024d:0.0} KB\n" +
                $"GeometryMem: src/res={stats.GeometrySourceBytes / 1024d:0.0}/{stats.GeometryResidentBytes / 1024d:0.0} KB | resources={stats.GeometryResourceCount} | index saved={stats.GeometryCompactIndexBytesSaved / 1024d:0.0} KB | wire materialized={stats.MaterializedWireframeGeometryCount}\n" +
                $"CPUResources: tex={cpuResources.TextureCount}/{cpuResources.ReferencedTextureCount} {cpuResources.ResidentTextureBytes / (1024d * 1024d):0.00}/{cpuResources.TextureBudgetBytes / (1024d * 1024d):0} MB | shaders={cpuResources.ShaderCount}/{cpuResources.ReferencedShaderCount} {cpuResources.ResidentShaderBytes / 1024d:0.0}/{cpuResources.ShaderBudgetBytes / 1024d:0} KB | owners={cpuResources.OwnerCount}\n" +
                $"RHI {stats.RhiBackend}: GPU={(stats.GpuTimingAvailable ? stats.GpuFrameMilliseconds.ToString("0.00") + " ms" : "timing unavailable")} | live/buf/tex/owners={stats.RhiResourceCount}/{stats.RhiBufferCount}/{stats.RhiTextureCount}/{stats.RhiOwnershipReferences} | resident/texture={stats.RhiResidentBytes / (1024d * 1024d):0.00}/{stats.RhiTextureBytes / (1024d * 1024d):0.00} MB budget={stats.RhiResidentBudgetBytes / (1024d * 1024d):0}/{stats.RhiTextureBudgetBytes / (1024d * 1024d):0} MB peak={stats.RhiPeakResidentBytes / (1024d * 1024d):0.00} MB | C/U/R={stats.RhiResourceCreates}/{stats.RhiResourceUpdates}/{stats.RhiResourceReleases} gen={stats.RhiContextGeneration}\n" +
                $"GPUDriven: {(stats.GpuDrivenActive ? "on" : "off")} obj/mesh/mat/meshlet={stats.GpuDrivenObjectCount}/{stats.GpuDrivenMeshCount}/{stats.GpuDrivenMaterialCount}/{stats.GpuDrivenMeshletCount} | particles={stats.GpuDrivenParticleCapacity} | pass C/R={stats.GpuDrivenComputePassCount}/{stats.GpuDrivenRenderPassCount} | barriers={stats.GpuDrivenBarrierCount} | indirect={stats.GpuDrivenIndirectCommandCapacity} | graph={stats.GpuDrivenPhysicalResourceCount}/{stats.GpuDrivenAliasedResourceCount}\n" +
                $"Packet: {stats.PacketBuildMilliseconds:0.00} ms | Ser: {stats.SerializationMilliseconds:0.00} ms | Upload: {stats.UploadMilliseconds:0.00} ms | Backend: {stats.BackendMilliseconds:0.00} ms\n" +
                $"WebGLv{stats.WebGlVersion} ClientHS: {(stats.WebGlClientHighScaleRuntime ? "on" : "off")} | GPUAnim: {(stats.WebGlClientGpuTransformAnimation ? "on" : "off")} | JS Cull: {stats.JsCullMilliseconds:0.00} ms | JS Draw: {stats.JsDrawMilliseconds:0.00} ms | JS Frame: {stats.JsFrameMilliseconds:0.00} ms | JS Batches: {stats.JsDrawBatchCount} | Legacy draw/block/str={stats.WebGlLegacyDrawPathCalls}/{stats.WebGlLegacyDrawPathBlockedCalls}/{stats.WebGlLegacyStringProtocolCalls} | bufferData={stats.WebGlBufferDataCalls}/{stats.WebGlDynamicBufferDataCalls}\n" +
                $"JSPatch T/S: {stats.JsTransformPatchRanges}/{stats.JsStatePatchRanges} ranges | {stats.JsTransformPatchBytes / 1024d:0.0}/{stats.JsStatePatchBytes / 1024d:0.0} KB | Route dirty {stats.JsHighScaleDirtyTransformInstances}/{stats.JsHighScaleDirtyStateInstances} refs {stats.JsHighScalePatchRoutedTransformRefs}/{stats.JsHighScalePatchRoutedStateRefs} batches {stats.JsHighScalePatchTouchedTransformBatches}/{stats.JsHighScalePatchTouchedStateBatches} | JSAnim: {stats.JsAnimationUploadBatches} batches/{stats.JsAnimationUploadBytes / 1024d:0.0} KB | TexErr: {stats.JsTexturePayloadErrors}/{stats.JsPalettePayloadErrors} | Patch: {stats.JsPatchMilliseconds:0.00} ms\n" +
                $"Pick: {stats.PickingMilliseconds:0.00} ms ({stats.ControlPointerPickCount}/{stats.ControlPlanePickTestCount}) | Phys: {stats.PhysicsMilliseconds:0.00} ms | Live: {stats.LiveSnapshotMilliseconds:0.00} ms snap={stats.ControlSnapshotRefreshCount} q={stats.ControlSnapshotQueueHighWater}\n" +
                $"Alloc: {stats.AllocatedMegabytesPerSecond:0.00} MB/s | FrameAlloc: {stats.AllocatedBytesPerFrame / 1024d:0.0} KB | GC: {stats.Gen0Collections}/{stats.Gen1Collections}/{stats.Gen2Collections} | Heap: {stats.ManagedHeapBytes / (1024d * 1024d):0.0} MB\n" +
                $"Sim: tick={stats.SimulationTick} time={stats.SimulationTimeSeconds:0.000}s fixed={stats.FixedUpdatesPerSecond:0.##}Hz steps={stats.LastSimulationStepCount} acc={stats.SimulationAccumulatorSeconds * 1000d:0.00}ms dropped={stats.DroppedSimulationSeconds * 1000d:0.00}ms pause/fault={OnOff(stats.SimulationPaused)}/{OnOff(stats.SimulationFaulted)}\n" +
                $"SimCPU: total={stats.SimulationTotalMilliseconds:0.00} ms cmd={stats.SimulationCommandsMilliseconds:0.00}/{stats.SimulationCommandsExecuted} jobs={stats.SimulationJobsTotalMilliseconds:0.00}/{stats.SimulationJobsExecuted} user={stats.SimulationUserUpdateMilliseconds:0.00} anim={stats.SimulationAnimationMilliseconds:0.00} phys={stats.SimulationPhysicsMilliseconds:0.00} part={stats.SimulationParticleMilliseconds:0.00} complete={stats.SimulationCompletionMilliseconds:0.00}\n" +
                $"FPSLock: {(stats.FpsLocked ? "on" : "off")} {stats.TargetFps:0} | Interp: {(stats.FrameInterpolationEnabled ? "on" : "off")} a={stats.InterpolationAlpha:0.00} | Adaptive: {(stats.AdaptivePerformanceEnabled ? "on" : "off")} q={stats.AdaptiveQualityScale:0.00} | Delay: {stats.RenderScheduleDelayMilliseconds:0.00} ms\n" +
                $"SceneGraph: seq={stats.SceneChangeSequence} journal={stats.RetainedSceneChangeCount} | Registry: v{stats.RegistryVersion} inc/full/spatial/snap={stats.RegistryIncrementalChangeCount}/{stats.RegistryFullRebuildCount}/{stats.RegistrySpatialRefreshCount}/{stats.RegistrySnapshotBuildCount}\n" +
                $"MeshCache: {stats.MeshCacheCount} hit/miss={stats.MeshCacheHitCount}/{stats.MeshCacheMissCount}";
            SchedulePerformanceMetricsTextUpdate();

            _performanceFrameCount = 0;
            _performanceFrameMillisecondsTotal = 0d;
            _performanceWindowStartTicks = Stopwatch.GetTimestamp();
        }
        catch (Exception ex)
        {
            EnterRuntimeFaultState("Rendering.FrameCallback", ex);
        }
    }

    private void SchedulePerformanceMetricsTextUpdate()
    {
        if (_performanceMetricsTextUpdateScheduled)
        {
            return;
        }

        _performanceMetricsTextUpdateScheduled = true;
        Dispatcher.UIThread.Post(ApplyPerformanceMetricsTextUpdate, DispatcherPriority.Background);
    }

    private void ApplyPerformanceMetricsTextUpdate()
    {
        _performanceMetricsTextUpdateScheduled = false;

        if (!ShowPerformanceMetrics || _pendingPerformanceMetricsText is null)
        {
            return;
        }

        var text = _pendingPerformanceMetricsText;
        _performanceMetricsText.Text = text;
        (_presenter as IPerformanceMetricsOverlayPresenter)?.SetPerformanceMetricsOverlay(text, true);
        _pendingPerformanceMetricsText = null;
    }

    private void UpdatePerformanceMetricsVisibility()
    {
        var showPerformanceMetrics = ShowPerformanceMetrics;
        Scene.World.Mutate(scene => scene.Debug.ShowPerformanceMetrics = showPerformanceMetrics);
        _simulationHost.PumpCommands();
        _performanceMetricsHost.IsVisible = showPerformanceMetrics;
        if (!showPerformanceMetrics)
        {
            _performanceFrameCount = 0;
            _performanceFrameMillisecondsTotal = 0d;
            _performanceFrameMillisecondsLast = 0d;
            _performanceWindowStartTicks = 0;
            _pendingPerformanceMetricsText = null;
            _performanceMetricsText.Text = "FPS: --";
            (_presenter as IPerformanceMetricsOverlayPresenter)?.SetPerformanceMetricsOverlay(null, false);
        }
        else
        {
            (_presenter as IPerformanceMetricsOverlayPresenter)?.SetPerformanceMetricsOverlay(_performanceMetricsText.Text, true);
        }
    }
    private static void ApplyBrowserPerformanceDefaults(Scene3D scene)
    {
        if (!OperatingSystem.IsBrowser())
        {
            return;
        }

        scene.Performance.MaxLiveControlSnapshotsPerFrame = 1;
        scene.Performance.UseConservativeSkinnedPicking = true;
    }

    private bool IsButtonDragMouseLookActive => _isMouseLooking && MouseLookMode == SceneMouseLookMode.ButtonDrag;

    private bool IsCenterLockedMouseLookActive => _isMouseLooking && MouseLookMode == SceneMouseLookMode.CenterLocked;

    private bool ShouldUseCenterLockedMouseLook()
        => EnableSceneNavigation && NavigationMode != SceneNavigationMode.None && MouseLookMode == SceneMouseLookMode.CenterLocked;

    private void BeginMouseLook(PointerEventArgs e)
    {
        Focus();
        ClearControlHover(e);
        ClearActiveControlState(e);
        InteractionManager.CancelManipulation();
        var point = GetViewportPoint(e);
        _lastMouseLookPosition = new Vector2((float)point.X, (float)point.Y);
        _hasMouseLookPosition = true;
        RequestCameraAngleSynchronization();
        _isMouseLooking = true;
        _mouseLookPointer = e.Pointer;
        _mouseLookPointer.Capture(this);
        EngineLog3D.Debug("Input", $"Control {_controlId} began button-drag mouse look; pointer={e.Pointer.Id}; position={_lastMouseLookPosition}.");
        SuppressHoverPickingBriefly();
        UpdateNavigationTimerState();
    }

    private void BeginCenterLockedMouseLook(PointerEventArgs? e = null)
    {
        if (!ShouldUseCenterLockedMouseLook())
        {
            return;
        }

        Focus();
        RequestCameraAngleSynchronization();
        _isMouseLooking = true;
        if (e is not null)
        {
            _lastCenterLockedPointerEvent = e;
            CaptureCenterLockedPointer(e.Pointer);
            var point = GetViewportPoint(e);
            _lastMouseLookPosition = new Vector2((float)point.X, (float)point.Y);
            _hasMouseLookPosition = true;
        }
        else
        {
            _hasMouseLookPosition = false;
        }

        ApplyCenterLockedCursor();
        SuppressHoverPickingBriefly();
        RequestPresenterPointerLock();
        EngineLog3D.Debug("Input", $"Control {_controlId} began center-locked mouse look; pointerEvent={e is not null}; browserPointerLock={IsPresenterPointerLockActive()}.");
        UpdateCenterCursorVisibility();
        UpdateNavigationTimerState();
    }

    private void EndMouseLook(PointerEventArgs e)
    {
        if (_mouseLookPointer is not null && ReferenceEquals(_mouseLookPointer, e.Pointer))
        {
            _mouseLookPointer.Capture(null);
        }
        else
        {
            e.Pointer.Capture(null);
        }

        _mouseLookPointer = null;
        _lastCenterLockedPointerEvent = null;
        _isMouseLooking = false;
        _hasMouseLookPosition = false;
        RestoreCenterLockedCursor();
        ExitPresenterPointerLock();
        SuppressHoverPickingBriefly();
        EngineLog3D.Debug("Input", $"Control {_controlId} ended pointer-specific mouse look; pointer={e.Pointer.Id}.");
        UpdateCenterCursorVisibility();
        UpdateNavigationTimerState();
    }

    private void EndMouseLook()
    {
        _mouseLookPointer?.Capture(null);
        _mouseLookPointer = null;
        _lastCenterLockedPointerEvent = null;
        _isMouseLooking = false;
        _hasMouseLookPosition = false;
        RestoreCenterLockedCursor();
        ExitPresenterPointerLock();
        SuppressHoverPickingBriefly();
        EngineLog3D.Debug("Input", $"Control {_controlId} ended mouse look and released pointer lock.");
        UpdateCenterCursorVisibility();
        UpdateNavigationTimerState();
    }

    private void ApplyCenterLockedCursor()
    {
        if (_centerLockedCursorApplied)
        {
            return;
        }

        _cursorBeforeCenterLockedMouseLook = Cursor;
        Cursor = new Cursor(StandardCursorType.None);
        _centerLockedCursorApplied = true;
    }

    private void RestoreCenterLockedCursor()
    {
        if (!_centerLockedCursorApplied)
        {
            return;
        }

        Cursor = _cursorBeforeCenterLockedMouseLook;
        _cursorBeforeCenterLockedMouseLook = null;
        _centerLockedCursorApplied = false;
    }


    private void CaptureCenterLockedPointer(IPointer pointer)
    {
        _mouseLookPointer = pointer;
        try
        {
            _mouseLookPointer.Capture(this);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private Point GetCenterViewportPoint()
    {
        var size = GetViewportSize();
        return new Point(size.X * 0.5f, size.Y * 0.5f);
    }

    private Vector2 GetCenterViewportPosition()
    {
        var point = GetCenterViewportPoint();
        return new Vector2((float)point.X, (float)point.Y);
    }

    private Control GetViewportControl()
        => _presenter?.View ?? this;

    private Point GetViewportPoint(PointerEventArgs e)
        => e.GetPosition(GetViewportControl());

    private Vector2 GetViewportPosition(PointerEventArgs e)
    {
        var point = GetViewportPoint(e);
        return new Vector2((float)point.X, (float)point.Y);
    }

    private Vector2 GetViewportSize()
    {
        var control = GetViewportControl();
        var width = control.Bounds.Width > 0d ? control.Bounds.Width : Bounds.Width;
        var height = control.Bounds.Height > 0d ? control.Bounds.Height : Bounds.Height;
        return new Vector2((float)System.Math.Max(width, 1d), (float)System.Math.Max(height, 1d));
    }

    private void RequestPresenterPointerLock()
    {
        if (_presenter is IPointerLockPresenter pointerLock && pointerLock.SupportsPointerLock)
        {
            pointerLock.RequestPointerLock();
        }
    }

    private void ExitPresenterPointerLock()
    {
        if (_presenter is IPointerLockPresenter pointerLock && pointerLock.SupportsPointerLock)
        {
            pointerLock.ExitPointerLock();
        }
    }

    private bool IsPresenterPointerLockActive()
        => _presenter is IPointerLockPresenter { IsPointerLockActive: true };

    private bool TryApplyPresenterPointerLockDelta()
    {
        if (!IsCenterLockedMouseLookActive || _presenter is not IPointerLockPresenter pointerLock)
        {
            return false;
        }

        if (!pointerLock.TryConsumePointerDelta(out var delta))
        {
            return false;
        }

        QueueMouseLookDelta(delta);
        if (_lastCenterLockedPointerEvent is not null)
        {
            UpdateCenterLockedHover(_lastCenterLockedPointerEvent);
        }

        return true;
    }

    private void UpdateCenterCursorVisibility()
    {
        var visible = ShowCenterCursor && IsCenterLockedMouseLookActive && Bounds.Width > 0d && Bounds.Height > 0d;
        _centerCursorHost.IsVisible = visible;
        if (_presenter is ICenterCursorOverlayPresenter centerCursorOverlay)
        {
            centerCursorOverlay.SetCenterCursorOverlay(visible);
        }
    }

    private void UpdateCenterLockedHover(PointerEventArgs e)
    {
        if (ShouldSuppressHoverPicking(e))
        {
            return;
        }

        var center = GetCenterViewportPoint();
        if (TryHandleControlPointerMoved(e, center))
        {
            return;
        }

        ClearControlHover(e);
        InteractionManager.HandlePointerHover(this, e, GetCenterViewportPosition());
    }

    private void SuppressHoverPickingBriefly()
    {
        SuppressHoverPickingBriefly(Stopwatch.GetTimestamp());
    }

    private void SuppressHoverPickingBriefly(long nowTicks)
    {
        if (!OperatingSystem.IsBrowser())
        {
            return;
        }

        _suppressHoverPickingUntilTicks = nowTicks + (long)(Stopwatch.Frequency * BrowserCameraHoverSuppressionMilliseconds / 1000d);
    }

    private bool RefreshBrowserVisibilityState(bool force = false)
    {
        if (!OperatingSystem.IsBrowser() || _presenter is not IBrowserPageVisibilityPresenter browserVisibility)
        {
            return false;
        }

        var now = Stopwatch.GetTimestamp();
        if (!force && _lastBrowserVisibilityPollTicks != 0 &&
            now - _lastBrowserVisibilityPollTicks < (long)(Stopwatch.Frequency * BrowserVisibilityPollMilliseconds / 1000d))
        {
            return false;
        }

        _lastBrowserVisibilityPollTicks = now;
        var visibilityVersion = browserVisibility.DocumentVisibilityVersion;
        if (visibilityVersion == _lastBrowserDocumentVisibilityVersion)
        {
            return false;
        }

        _lastBrowserDocumentVisibilityVersion = visibilityVersion;
        _lastFrameRenderedTicks = now;
        SuppressHoverPickingBriefly(now);
        return true;
    }

    private bool ShouldSuppressHoverPicking(PointerEventArgs e)
    {
        if (!OperatingSystem.IsBrowser())
        {
            return false;
        }

        RefreshBrowserVisibilityState();
        if (_suppressHoverPickingUntilTicks == 0)
        {
            return false;
        }

        var now = Stopwatch.GetTimestamp();
        if (now >= _suppressHoverPickingUntilTicks)
        {
            _suppressHoverPickingUntilTicks = 0;
            return false;
        }

        var props = e.GetCurrentPoint(this).Properties;
        return !props.IsLeftButtonPressed && !props.IsRightButtonPressed && !props.IsMiddleButtonPressed;
    }

    private void ApplyMouseLookFromPointer(PointerEventArgs e)
    {
        var point = GetViewportPoint(e);
        var position = new Vector2((float)point.X, (float)point.Y);
        if (!_hasMouseLookPosition)
        {
            _lastMouseLookPosition = position;
            _hasMouseLookPosition = true;
            return;
        }

        var delta = position - _lastMouseLookPosition;
        _lastMouseLookPosition = position;
        if (delta.LengthSquared() <= 0.000001f)
        {
            return;
        }

        // Pointer events are not synchronized with either the compositor or the simulation
        // clock. Automatic simulation consumes the accumulated delta on its owner thread in the
        // same fixed tick as keyboard navigation. Manual-host mode applies it immediately.
        if (AutomaticSceneUpdates)
        {
            QueueMouseLookDelta(delta);
            SuppressHoverPickingBriefly();
            return;
        }

        var input = CaptureNavigationInputSnapshot(consumeTransientInput: false);
        ExecuteNavigationMutationOnSimulationOwner(() => ApplyMouseLookCore(delta, input));
    }

    private void ExecuteNavigationMutationOnSimulationOwner(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (_simulationHost.UsesDedicatedThread && !_simulationHost.IsCurrentThreadOwner)
        {
            EnqueueSceneCommand(_ => mutation());
            return;
        }

        mutation();
    }

    private void QueueMouseLookDelta(Vector2 delta)
    {
        if (delta.LengthSquared() <= 0.000001f) return;
        lock (_navigationStateSync)
        {
            _pendingMouseLookDelta += delta;
        }
    }

    private void ClearPendingMouseLookDelta()
    {
        lock (_navigationStateSync) _pendingMouseLookDelta = Vector2.Zero;
    }

    private void ApplyMouseLookCore(Vector2 delta, NavigationInputSnapshot3D input)
    {
        if (!input.Enabled || input.Mode == SceneNavigationMode.None) return;
        var sensitivity = input.Mode == SceneNavigationMode.Person ? input.PersonMouseSensitivity : input.FreeFlyMouseSensitivity;
        var invertX = input.Mode == SceneNavigationMode.Person ? input.PersonInvertMouseX : input.FreeFlyInvertMouseX;
        var invertY = input.Mode == SceneNavigationMode.Person ? input.PersonInvertMouseY : input.FreeFlyInvertMouseY;
        _yawDegrees += delta.X * sensitivity * (invertX ? -1f : 1f);
        _pitchDegrees = Math.Clamp(_pitchDegrees + (-delta.Y * sensitivity * (invertY ? -1f : 1f)), -88f, 88f);
        ApplyCameraForwardFromAngles();
    }

    private void OnSceneUpdateTimerTick()
    {
        if (TopLevel.GetTopLevel(this) is null || !NeedsSceneUpdateTimer())
        {
            _navigationTimer.Stop();
            _renderScheduler.Reset();
            return;
        }

        if (_presenter is IBrowserPageVisibilityPresenter { IsDocumentHidden: true })
        {
            _renderScheduler.Reset();
            RefreshBrowserVisibilityState(force: true);
            return;
        }

        AdvanceAutomaticSceneUpdate();
        if (!ContinuousRendering)
        {
            RequestRender();
        }
        UpdateNavigationTimerState();
    }

    private void OnContinuousRenderTimerTick()
    {
        if (_disposed || TopLevel.GetTopLevel(this) is null || !ContinuousRendering || !FpsLockEnabled)
        {
            _continuousRenderTimer.Stop();
            _renderScheduler.Reset();
            return;
        }

        if (NeedsSceneUpdateTimer())
        {
            AdvanceAutomaticSceneUpdate();
        }
        else
        {
            _renderScheduler.Reset();
        }

        RequestPresenterRenderOnly();
    }

    private void UpdateNavigationTimerState()
    {
        if (_disposed || _runtimeFault is not null || TopLevel.GetTopLevel(this) is null)
        {
            _navigationTimer.Stop();
            _renderScheduler.Reset();
            return;
        }

        // Continuous rendering owns the scene-update cadence. Running the navigation timer
        // beside the render timer creates two unsynchronised 60 Hz clocks; when mouse-look and
        // keyboard movement are active together their phase beating is visible as regular
        // Desktop stalls. Locked Desktop frames are advanced by OnContinuousRenderTimerTick;
        // browser and unlocked Desktop frames are advanced from the presented-frame callback.
        if (UsesContinuousFrameUpdates())
        {
            if (_navigationTimer.IsEnabled)
            {
                _navigationTimer.Stop();
                _renderScheduler.Reset();
            }
            return;
        }

        if (_presenter is IBrowserPageVisibilityPresenter { IsDocumentHidden: true })
        {
            _navigationTimer.Interval = TimeSpan.FromMilliseconds(BrowserVisibilityPollMilliseconds);
        }
        else
        {
            var interpolationPumpFps = FrameInterpolationEnabled ? EffectiveTargetFps : 0d;
            var pumpFps = System.Math.Clamp(System.Math.Max(Scene.UpdateLoop.FixedUpdatesPerSecond, interpolationPumpFps), 1d, 500d);
            _navigationTimer.Interval = TimeSpan.FromMilliseconds(1000d / pumpFps);
        }

        if (NeedsSceneUpdateTimer())
        {
            if (!_navigationTimer.IsEnabled)
            {
                _renderScheduler.Start();
                _navigationTimer.Start();
            }
        }
        else
        {
            _navigationTimer.Stop();
            _renderScheduler.Reset();
        }
    }

    private bool UsesContinuousFrameUpdates()
        => ContinuousRendering && TopLevel.GetTopLevel(this) is not null;

    private void AdvanceSceneFromPresentedFrame()
    {
        if (!UsesContinuousFrameUpdates() ||
            _presenter is IBrowserPageVisibilityPresenter { IsDocumentHidden: true })
        {
            _renderScheduler.Reset();
            return;
        }

        if (!NeedsSceneUpdateTimer())
        {
            _renderScheduler.Reset();
            return;
        }

        AdvanceAutomaticSceneUpdate();
    }

    private void AdvanceAutomaticSceneUpdate()
    {
        var elapsedSeconds = _renderScheduler.ConsumeElapsed(Scene.UpdateLoop.FixedDeltaSeconds);
        TryApplyPresenterPointerLockDelta();
        PublishNavigationInputSnapshot(consumeTransientInput: true);
        _simulationHost.Submit(elapsedSeconds);
    }

    private bool NeedsSceneUpdateTimer()
    {
        if (_runtimeFault is not null || !AutomaticSceneUpdates || Scene.UpdateLoop.IsPaused || Scene.UpdateLoop.IsFaulted || Scene.UpdateLoop.TimeScale <= 0d)
        {
            return false;
        }

        if (Scene.HasActiveUpdateWork())
        {
            return true;
        }

        if (!EnableSceneNavigation || NavigationMode == SceneNavigationMode.None)
        {
            return false;
        }

        if (PressedKeyCount > 0 || IsButtonDragMouseLookActive || IsCenterLockedMouseLookActive)
        {
            return true;
        }

        return NavigationMode == SceneNavigationMode.Person &&
               (Volatile.Read(ref _simulationPersonGrounded) == 0 || MathF.Abs(Volatile.Read(ref _simulationPersonVerticalVelocity)) > 0.001f);
    }

    private void StepFreeFlyNavigation(float dt, NavigationInputSnapshot3D input)
    {
        if (input.Movement == Vector3.Zero) return;
        var speed = input.FreeFlyMoveSpeed * (input.FastMove ? input.FreeFlyFastMoveMultiplier : 1f);
        var forward = _scene.Camera.Forward;
        var right = _scene.Camera.Right;
        var up = _scene.Camera.SafeUp;
        var direction = right * input.Movement.X + up * input.Movement.Y + forward * input.Movement.Z;
        if (direction.LengthSquared() < 0.0001f) return;
        _scene.Camera.Translate(Vector3.Normalize(direction) * speed * dt);
    }

    private void TryStartPersonJump()
    {
        if (!EnableSceneNavigation || NavigationMode != SceneNavigationMode.Person || Volatile.Read(ref _simulationPersonGrounded) == 0) return;
        if (AutomaticSceneUpdates)
        {
            lock (_navigationStateSync) _pendingPersonJump = true;
        }
        else
        {
            ExecuteNavigationMutationOnSimulationOwner(() =>
            {
                _personController.Jump(MathF.Max(PersonSettings.JumpSpeed, 0f));
                _personVelocity = _personController.Velocity;
                _personGrounded = _personController.IsGrounded;
                Volatile.Write(ref _simulationPersonVerticalVelocity, _personVelocity.Y);
                Volatile.Write(ref _simulationPersonGrounded, _personGrounded ? 1 : 0);
            });
        }
        UpdateNavigationTimerState();
    }

    public void ResetPersonNavigationState(bool grounded = false)
    {
        ThrowIfDisposed();
        if (_simulationHost.UsesDedicatedThread && !_simulationHost.IsCurrentThreadOwner)
        {
            EnqueueSceneCommand(_ => ResetPersonNavigationStateCore(grounded));
            return;
        }
        ResetPersonNavigationStateCore(grounded);
    }

    private void ResetPersonNavigationStateCore(bool grounded)
    {
        _personVelocity = Vector3.Zero;
        _personGrounded = grounded;
        _personController.Reset(Vector3.Zero, grounded);
        Volatile.Write(ref _simulationPersonGrounded, grounded ? 1 : 0);
        Volatile.Write(ref _simulationPersonVerticalVelocity, 0f);
    }

    private void StepPersonNavigation(float dt, NavigationInputSnapshot3D input)
    {
        var horizontalForward = new Vector3(_scene.Camera.Forward.X, 0f, _scene.Camera.Forward.Z);
        var forward = horizontalForward.LengthSquared() < 0.0001f ? -Vector3.UnitZ : Vector3.Normalize(horizontalForward);
        if (!IsFinite(forward))
        {
            forward = -Vector3.UnitZ;
        }

        var rightVector = Vector3.Cross(forward, Vector3.UnitY);
        var right = rightVector.LengthSquared() < 0.0001f ? Vector3.UnitX : Vector3.Normalize(rightVector);
        var move = right * input.Movement.X + forward * input.Movement.Z;
        if (move.LengthSquared() > 0.0001f)
        {
            move = Vector3.Normalize(move);
        }

        var speed = input.PersonMoveSpeed * (input.FastMove ? input.PersonRunMultiplier : 1f);
        _personController.Radius = MathF.Max(0.05f, input.PersonBodyRadius);
        _personController.Height = MathF.Max(_personController.Radius * 2f, input.PersonBodyHeight);
        _personController.StepHeight = MathF.Max(0f, input.PersonStepHeight);
        _personController.Gravity = new Vector3(0f, input.PersonGravity, 0f);

        var eyeToFoot = new Vector3(0f, MathF.Max(0.05f, input.PersonEyeHeight), 0f);
        var footPosition = _scene.Camera.Position - eyeToFoot;
        var horizontalMotion = move * speed * dt;
        var resolvedFootPosition = _personController.Move(_scene, footPosition, horizontalMotion, dt);
        _personVelocity = _personController.Velocity;
        _personGrounded = _personController.IsGrounded;
        ApplyCameraForwardFromAngles(resolvedFootPosition + eyeToFoot);
    }

    private static bool IsFinite(Vector3 value)
        => !float.IsNaN(value.X) && !float.IsNaN(value.Y) && !float.IsNaN(value.Z) &&
           !float.IsInfinity(value.X) && !float.IsInfinity(value.Y) && !float.IsInfinity(value.Z);

    private void PublishNavigationInputSnapshot(bool consumeTransientInput)
        => Volatile.Write(ref _publishedNavigationInput, CaptureNavigationInputSnapshot(consumeTransientInput));

    private NavigationInputSnapshot3D CaptureNavigationInputSnapshot(bool consumeTransientInput)
    {
        var mode = NavigationMode;
        var enabled = EnableSceneNavigation;
        Vector3 movement;
        bool fastMove;
        Vector2 mouseDelta;
        bool jumpRequested;
        bool synchronizeAngles;
        int pressedKeyCount;
        lock (_navigationStateSync)
        {
            movement = Vector3.Zero;
            if (_pressedKeys.Contains(Key.A) || _pressedKeys.Contains(Key.Left)) movement.X -= 1f;
            if (_pressedKeys.Contains(Key.D) || _pressedKeys.Contains(Key.Right)) movement.X += 1f;
            if (_pressedKeys.Contains(Key.W) || _pressedKeys.Contains(Key.Up)) movement.Z += 1f;
            if (_pressedKeys.Contains(Key.S) || _pressedKeys.Contains(Key.Down)) movement.Z -= 1f;
            if (mode == SceneNavigationMode.FreeFly)
            {
                if (_pressedKeys.Contains(Key.Space)) movement.Y += 1f;
                if (_pressedKeys.Contains(Key.LeftCtrl) || _pressedKeys.Contains(Key.RightCtrl)) movement.Y -= 1f;
            }
            fastMove = _pressedKeys.Contains(Key.LeftShift) || _pressedKeys.Contains(Key.RightShift);
            pressedKeyCount = _pressedKeys.Count;
            mouseDelta = _pendingMouseLookDelta;
            jumpRequested = _pendingPersonJump;
            synchronizeAngles = _pendingCameraAngleSynchronization;
            if (consumeTransientInput)
            {
                _pendingMouseLookDelta = Vector2.Zero;
                _pendingPersonJump = false;
                _pendingCameraAngleSynchronization = false;
            }
        }

        return new NavigationInputSnapshot3D(
            Interlocked.Increment(ref _navigationInputSequence), enabled, mode, movement, fastMove, mouseDelta, jumpRequested, synchronizeAngles,
            _freeFlySettings.MoveSpeed, _freeFlySettings.FastMoveMultiplier, _freeFlySettings.MouseSensitivity, _freeFlySettings.InvertMouseX, _freeFlySettings.InvertMouseY,
            _personSettings.MoveSpeed, _personSettings.RunMultiplier, _personSettings.MouseSensitivity, _personSettings.InvertMouseX, _personSettings.InvertMouseY,
            _personSettings.EyeHeight, _personSettings.BodyHeight, _personSettings.BodyRadius, _personSettings.PushStrength, _personSettings.JumpSpeed,
            _personSettings.Gravity, _personSettings.StepHeight, pressedKeyCount);
    }

    private int PressedKeyCount
    {
        get { lock (_navigationStateSync) return _pressedKeys.Count; }
    }

    private void ClearPressedKeys()
    {
        lock (_navigationStateSync) _pressedKeys.Clear();
    }

    private void AddPressedKey(Key key)
    {
        lock (_navigationStateSync) _pressedKeys.Add(key);
    }

    private void RemovePressedKey(Key key)
    {
        lock (_navigationStateSync) _pressedKeys.Remove(key);
    }

    private static bool IsNavigationKey(Key key)
        => key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right or Key.Space or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift;

    private void RequestCameraAngleSynchronization()
    {
        if (!AutomaticSceneUpdates)
        {
            ExecuteNavigationMutationOnSimulationOwner(SyncCameraAnglesFromForward);
            return;
        }
        lock (_navigationStateSync) _pendingCameraAngleSynchronization = true;
    }

    private void SyncCameraAnglesFromForward()
    {
        var f = Scene.Camera.Forward;
        _yawDegrees = MathF.Atan2(f.X, -f.Z) * 180f / MathF.PI;
        _pitchDegrees = MathF.Asin(System.Math.Clamp(f.Y, -1f, 1f)) * 180f / MathF.PI;
    }

    private void ApplyCameraForwardFromAngles()
        => ApplyCameraForwardFromAngles(Scene.Camera.Position);

    private void ApplyCameraForwardFromAngles(Vector3 position)
    {
        var yaw = _yawDegrees * MathF.PI / 180f;
        var pitch = _pitchDegrees * MathF.PI / 180f;
        var cosPitch = MathF.Cos(pitch);
        var forward = new Vector3(MathF.Sin(yaw) * cosPitch, MathF.Sin(pitch), -MathF.Cos(yaw) * cosPitch);
        if (forward.LengthSquared() < 0.0001f)
        {
            forward = -Vector3.UnitZ;
        }

        forward = Vector3.Normalize(forward);
        Scene.Camera.SetPose(position, position + forward, Vector3.UnitY);
    }

    private void TrackControlPlane(ControlPlane3D plane)
    {
        if (!_controlPlaneSet.Add(plane))
        {
            return;
        }

        _controlPlanes.Add(plane);
        _controlPlanePickExclusions.Add(plane);
    }

    private void UntrackControlPlane(ControlPlane3D plane)
    {
        if (!_controlPlaneSet.Remove(plane))
        {
            return;
        }

        _controlPlanes.Remove(plane);
        _controlPlanePickExclusions.Remove(plane);
    }

    private void EnsureControlAdapter(ControlPlane3D plane)
    {
        TrackControlPlane(plane);
        if (_controlAdapters.ContainsKey(plane) || _creatingControlAdapters.Contains(plane))
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        _creatingControlAdapters.Add(plane);
        try
        {
            var adapter = new ControlPlaneRuntimeAdapter(plane, _hiddenHost);
            adapter.SnapshotDirtyRequested += OnControlAdapterSnapshotDirtyRequested;
            _controlAdapters[plane] = adapter;
            adapter.MarkDirty();
        }
        finally
        {
            _creatingControlAdapters.Remove(plane);
        }

        UpdateSnapshotTimerState();
        UpdateNavigationTimerState();
    }

    private void OnControlAdapterSnapshotDirtyRequested(ControlPlaneRuntimeAdapter adapter)
    {
        EnqueueDirtyControlSnapshot(adapter);
    }

    private void EnqueueDirtyControlSnapshot(ControlPlane3D? plane)
    {
        if (plane is not null && _controlAdapters.TryGetValue(plane, out var adapter))
        {
            EnqueueDirtyControlSnapshot(adapter);
            return;
        }

        if (plane is not null)
        {
            return;
        }

        foreach (var item in _controlAdapters.Values)
        {
            EnqueueDirtyControlSnapshot(item);
        }
    }

    private void EnqueueDirtyControlSnapshot(ControlPlaneRuntimeAdapter adapter)
    {
        if (!adapter.IsDirty || !_controlAdapters.TryGetValue(adapter.Plane, out var current) || !ReferenceEquals(adapter, current))
        {
            return;
        }

        if (!_dirtyControlSnapshotSet.Add(adapter))
        {
            return;
        }

        _dirtyControlSnapshotQueue.Enqueue(adapter);
        if (_dirtyControlSnapshotQueue.Count > _controlSnapshotQueueHighWaterSinceLastFrame)
        {
            _controlSnapshotQueueHighWaterSinceLastFrame = _dirtyControlSnapshotQueue.Count;
        }
    }

    private void RemoveControlAdapter(ControlPlane3D plane)
    {
        if (!_controlAdapters.TryGetValue(plane, out var adapter))
        {
            UntrackControlPlane(plane);
            return;
        }

        if (ReferenceEquals(_activeControlAdapter, adapter))
        {
            _activeControlAdapter = null;
        }

        if (ReferenceEquals(_focusedControlAdapter, adapter))
        {
            _focusedControlAdapter = null;
        }

        if (ReferenceEquals(_hoveredControlAdapter, adapter))
        {
            _hoveredControlAdapter = null;
        }

        adapter.SnapshotDirtyRequested -= OnControlAdapterSnapshotDirtyRequested;
        adapter.Dispose();
        _controlAdapters.Remove(plane);
        _dirtyControlSnapshotSet.Remove(adapter);
        UntrackControlPlane(plane);
        UpdateSnapshotTimerState();
        UpdateNavigationTimerState();
    }

    private void ClearControlAdapters()
    {
        foreach (var adapter in _controlAdapters.Values)
        {
            adapter.SnapshotDirtyRequested -= OnControlAdapterSnapshotDirtyRequested;
            adapter.Dispose();
        }

        _controlAdapters.Clear();
        _controlPlanes.Clear();
        _controlPlaneSet.Clear();
        _controlPlanePickExclusions.Clear();
        _dirtyControlSnapshotQueue.Clear();
        _dirtyControlSnapshotSet.Clear();
        _activeControlAdapter = null;
        _focusedControlAdapter = null;
        _hoveredControlAdapter = null;
        UpdateSnapshotTimerState();
        UpdateNavigationTimerState();
    }

    private void SyncControlAdapters()
    {
        _controlPlanes.Clear();
        _controlPlaneSet.Clear();
        _controlPlanePickExclusions.Clear();

        var objects = Scene.Registry.SnapshotAllObjects();
        for (var i = 0; i < objects.Length; i++)
        {
            if (objects[i] is not ControlPlane3D plane)
            {
                continue;
            }

            TrackControlPlane(plane);
            EnsureControlAdapter(plane);
        }

        _staleControlPlanesScratch.Clear();
        foreach (var plane in _controlAdapters.Keys)
        {
            if (!_controlPlaneSet.Contains(plane))
            {
                _staleControlPlanesScratch.Add(plane);
            }
        }

        for (var i = 0; i < _staleControlPlanesScratch.Count; i++)
        {
            RemoveControlAdapter(_staleControlPlanesScratch[i]);
        }

        _staleControlPlanesScratch.Clear();
    }

    private void RefreshDirtyControlSnapshots()
    {
        var now = DateTime.UtcNow;
        var refreshed = 0;
        var budget = OperatingSystem.IsBrowser()
            ? System.Math.Clamp(Scene.Performance.MaxLiveControlSnapshotsPerFrame, 0, 1)
            : System.Math.Max(1, Scene.Performance.MaxLiveControlSnapshotsPerFrame);
        if (budget <= 0 || _dirtyControlSnapshotQueue.Count == 0)
        {
            return;
        }

        var minInterval = GetSnapshotMinInterval();
        var inspected = _dirtyControlSnapshotQueue.Count;
        while (inspected-- > 0 && refreshed < budget && _dirtyControlSnapshotQueue.Count > 0)
        {
            var adapter = _dirtyControlSnapshotQueue.Dequeue();
            _dirtyControlSnapshotSet.Remove(adapter);

            if (!_controlAdapters.TryGetValue(adapter.Plane, out var current) || !ReferenceEquals(adapter, current) || !adapter.IsDirty)
            {
                continue;
            }

            if (!adapter.ShouldRefresh(now, minInterval))
            {
                EnqueueDirtyControlSnapshot(adapter);
                continue;
            }

            adapter.UpdateSnapshot();
            if (adapter.IsDirty)
            {
                EnqueueDirtyControlSnapshot(adapter);
            }
            else
            {
                refreshed++;
                _controlSnapshotRefreshesSinceLastFrame++;
            }
        }
    }

    private void UpdateSnapshotTimerState()
    {
        if (EnableLiveControlFallbackRefresh && _controlAdapters.Count > 0 && TopLevel.GetTopLevel(this) is not null)
        {
            if (!_snapshotFallbackTimer.IsEnabled)
            {
                _snapshotFallbackTimer.Start();
            }
        }
        else
        {
            _snapshotFallbackTimer.Stop();
        }
    }

    private TimeSpan GetSnapshotMinInterval()
    {
        var fps = System.Math.Clamp(LiveControlSnapshotFps, 1d, OperatingSystem.IsBrowser() ? 15d : 120d);
        return TimeSpan.FromSeconds(1d / fps);
    }

    private void OnSnapshotFallbackTimerTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        foreach (var adapter in _controlAdapters.Values)
        {
            if ((now - adapter.LastSnapshotUtc) > TimeSpan.FromMilliseconds(750))
            {
                adapter.MarkDirty();
            }
        }

        RequestRender();
    }

    private bool TryHandleControlPointerPressed(PointerPressedEventArgs e)
        => TryHandleControlPointerPressed(e, GetViewportPoint(e));

    private bool TryHandleControlPointerPressed(PointerPressedEventArgs e, Point viewportPoint)
    {
        var hit = PickSceneObject(viewportPoint);
        if (hit?.Object is not ControlPlane3D plane)
        {
            return false;
        }

        if (!_controlAdapters.TryGetValue(plane, out var adapter))
        {
            return false;
        }

        InteractionManager.ClearHover();
        InteractionManager.CancelManipulation();

        if (!adapter.IsInteractionReady)
        {
            return false;
        }

        if (!ExecuteForwardedControlInput(() => adapter.HandlePointerPressed(e, hit.WorldPosition, Scene.Camera, GetRootVisual())))
        {
            return false;
        }

        _activeControlAdapter = adapter;
        _focusedControlAdapter = adapter;
        if (adapter.ShouldCaptureKeyboardInput)
        {
            ClearPressedKeys();
            _hasMouseLookPosition = false;
            UpdateNavigationTimerState();
        }

        e.Pointer.Capture(this);
        RequestRender();
        return true;
    }

    private bool TryHandleControlPointerMoved(PointerEventArgs e)
        => TryHandleControlPointerMoved(e, GetViewportPoint(e));

    private bool TryHandleControlPointerMoved(PointerEventArgs e, Point viewportPoint)
    {
        if (_activeControlAdapter is not null && _activeControlAdapter.IsPointerCaptured)
        {
            var worldPoint = TryPickControlPlane(_activeControlAdapter.Plane, viewportPoint, out var capturedHit)
                ? capturedHit.WorldPosition
                : (Vector3?)null;
            InteractionManager.ClearHover();
            if (ExecuteForwardedControlInput(() => _activeControlAdapter.HandlePointerMoved(e, Scene.Camera, GetRootVisual(), worldPoint)))
            {
                _hoveredControlAdapter = _activeControlAdapter;
                UpdateFocusedControlAdapterState();
                RequestRender();
                return true;
            }
        }

        var hit = PickSceneObject(viewportPoint);
        var plane = hit?.Object as ControlPlane3D;
        if (plane is null || !_controlAdapters.TryGetValue(plane, out var adapter))
        {
            return false;
        }

        if (!adapter.IsInteractionReady)
        {
            return false;
        }

        if (_hoveredControlAdapter is not null && !ReferenceEquals(_hoveredControlAdapter, adapter))
        {
            _hoveredControlAdapter.ClearHover(e, GetRootVisual());
            _hoveredControlAdapter = null;
        }

        InteractionManager.ClearHover();
        if (!ExecuteForwardedControlInput(() => adapter.HandlePointerMoved(e, Scene.Camera, GetRootVisual(), hit!.WorldPosition)))
        {
            return false;
        }

        _hoveredControlAdapter = adapter;
        UpdateFocusedControlAdapterState();
        RequestRender();
        return true;
    }

    private bool TryHandleControlPointerReleased(PointerReleasedEventArgs e)
        => TryHandleControlPointerReleased(e, GetViewportPoint(e));

    private bool TryHandleControlPointerReleased(PointerReleasedEventArgs e, Point viewportPoint)
    {
        var adapter = _activeControlAdapter;
        PickingResult? hit = null;
        ControlPlane3D? plane = null;

        if (adapter is not null)
        {
            if (TryPickControlPlane(adapter.Plane, viewportPoint, out var activeHit))
            {
                hit = activeHit;
                plane = adapter.Plane;
            }
        }
        else
        {
            hit = PickSceneObject(viewportPoint);
            plane = hit?.Object as ControlPlane3D;
            if (plane is not null)
            {
                _controlAdapters.TryGetValue(plane, out adapter);
            }
        }

        if (adapter is null)
        {
            return false;
        }

        var worldPoint = plane is not null && ReferenceEquals(adapter.Plane, plane) ? hit?.WorldPosition : null;
        if (!adapter.IsInteractionReady || !adapter.HandlePointerReleased(e, Scene.Camera, GetRootVisual(), worldPoint))
        {
            _activeControlAdapter = null;
            e.Pointer.Capture(null);
            return false;
        }

        _activeControlAdapter = null;
        e.Pointer.Capture(null);
        UpdateFocusedControlAdapterState();
        RequestRender();
        return true;
    }

    private bool TryHandleControlPointerWheel(PointerWheelEventArgs e)
        => TryHandleControlPointerWheel(e, GetViewportPoint(e));

    private bool TryHandleControlPointerWheel(PointerWheelEventArgs e, Point viewportPoint)
    {
        var hit = PickSceneObject(viewportPoint);
        if (hit?.Object is not ControlPlane3D plane)
        {
            return false;
        }

        if (!_controlAdapters.TryGetValue(plane, out var adapter))
        {
            return false;
        }

        InteractionManager.ClearHover();
        if (!ExecuteForwardedControlInput(() => adapter.HandlePointerWheel(e, hit.WorldPosition, Scene.Camera, GetRootVisual())))
        {
            return false;
        }

        UpdateFocusedControlAdapterState();
        RequestRender();
        return true;
    }

    private void ClearControlHover(PointerEventArgs e)
    {
        if (_hoveredControlAdapter is not null)
        {
            if (!ReferenceEquals(_hoveredControlAdapter, _activeControlAdapter) || !_hoveredControlAdapter.IsPointerCaptured)
            {
                _hoveredControlAdapter.ClearHover(e, GetRootVisual());
                _hoveredControlAdapter = null;
            }

            UpdateFocusedControlAdapterState();
            return;
        }

        foreach (var adapter in _controlAdapters.Values)
        {
            if (!ReferenceEquals(adapter, _activeControlAdapter) || !adapter.IsPointerCaptured)
            {
                adapter.ClearHover(e, GetRootVisual());
            }
        }

        UpdateFocusedControlAdapterState();
    }

    private void ClearActiveControlState(PointerEventArgs sourceEvent)
    {
        ClearControlHover(sourceEvent);
        _activeControlAdapter = null;
        if (_focusedControlAdapter is not null)
        {
            _focusedControlAdapter.ClearFocus();
            _focusedControlAdapter = null;
        }
    }

    private void UpdateFocusedControlAdapterState()
    {
        if (_focusedControlAdapter is null)
        {
            return;
        }

        if (!_focusedControlAdapter.HasFocus)
        {
            _focusedControlAdapter = null;
            return;
        }

        if (_focusedControlAdapter.ShouldCaptureKeyboardInput)
        {
            ClearPressedKeys();
            _hasMouseLookPosition = false;
            UpdateNavigationTimerState();
        }
    }

    private void OnViewportLostFocus()
    {
        if (_disposed) return;
        var keyCount = PressedKeyCount;
        ClearPressedKeys();
        lock (_navigationStateSync)
        {
            _pendingMouseLookDelta = Vector2.Zero;
            _pendingPersonJump = false;
            _pendingCameraAngleSynchronization = false;
        }
        if (_isMouseLooking) EndMouseLook();
        PublishNavigationInputSnapshot(consumeTransientInput: false);
        EngineLog3D.Information("Input", $"Control {_controlId} lost keyboard focus; cleared {keyCount} pressed key(s), pointer capture and transient navigation input.");
        UpdateNavigationTimerState();
    }

    private void OnSimulationHostFaulted(object? sender, SceneSimulationFaultedEventArgs3D e)
        => EnterRuntimeFaultState("Simulation.Host", e.Exception);

    private void OnPresenterFaulted(object? sender, ScenePresenterFaultedEventArgs3D e)
        => EnterRuntimeFaultState("Presenter." + e.Snapshot.Backend, e.Exception);

    private void EnterRuntimeFaultState(string subsystem, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        EngineLog3D.Critical("RuntimeFault", $"Control {_controlId} reported a fatal runtime fault in {subsystem}.", exception);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => EnterRuntimeFaultState(subsystem, exception), DispatcherPriority.Background);
            return;
        }
        if (_disposed) return;
        if (_runtimeFault is not null)
        {
            EngineLog3D.Warning("RuntimeFault", $"Control {_controlId} received an additional fault while already failed. first={_runtimeFaultSubsystem}; additional={subsystem}.", exception);
            return;
        }

        _runtimeFault = exception;
        _runtimeFaultSubsystem = subsystem;
        _navigationTimer.Stop();
        _continuousRenderTimer.Stop();
        _snapshotFallbackTimer.Stop();
        _renderScheduler.Reset();
        _unlockedRenderPending = false;
        _browserContinuousRenderScheduled = false;

        var health = CaptureRuntimeHealthSnapshotSafely("fatal-runtime-fault");
        EngineLog3D.WriteDiagnosticBlock("RuntimeHealth", $"fatal-control-{_controlId}", health, EngineLogLevel3D.Critical);
        _lastAutomaticDiagnosticPath = TryWriteAutomaticDiagnosticReport(subsystem);
        EngineLog3D.Flush();

        var pathText = OperatingSystem.IsBrowser()
            ? "Use ExportDiagnosticReport() from a button/click handler to download the complete browser report."
            : $"Log: {EngineLog3D.CurrentLogFilePath ?? "unavailable"}\nReport: {_lastAutomaticDiagnosticPath ?? "write failed"}";
        _runtimeFaultText.Text =
            $"Avalonia3D stopped deliberately after a fatal error.\n" +
            $"Subsystem: {subsystem}\n" +
            $"{exception.GetType().Name}: {exception.Message}\n\n" +
            pathText + "\n\nCorrect the cause, then call ResetRuntimeFault().";
        _runtimeFaultHost.IsVisible = true;

        try
        {
            RuntimeFaulted?.Invoke(this, new SceneRuntimeFaultedEventArgs3D(
                _controlId, subsystem, exception, _lastAutomaticDiagnosticPath, EngineLog3D.CurrentLogFilePath));
        }
        catch (Exception subscriberException)
        {
            EngineLog3D.Error("RuntimeFault", $"Control {_controlId} RuntimeFaulted subscriber failed.", subscriberException);
        }
    }

    private string? TryWriteAutomaticDiagnosticReport(string subsystem)
    {
        if (OperatingSystem.IsBrowser()) return null;
        try
        {
            var safeSubsystem = new string(subsystem.Select(static character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
            var directory = EngineLog3D.LogDirectory ?? Path.GetTempPath();
            var path = Path.Combine(directory,
                $"Avalonia3D-{EngineLog3D.SessionId}-{_controlId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{safeSubsystem}.diagnostic.txt");
            return TryWriteDiagnosticReport(path, out var error, 8192)
                ? Path.GetFullPath(path)
                : LogAutomaticReportFailure(error);
        }
        catch (Exception exception)
        {
            EngineLog3D.Error("Diagnostics", $"Control {_controlId} automatic diagnostic report failed.", exception);
            return null;
        }
    }

    private string? LogAutomaticReportFailure(string? error)
    {
        EngineLog3D.Error("Diagnostics", $"Control {_controlId} automatic diagnostic report could not be written: {error ?? "unknown error"}.");
        return null;
    }

    private void TryRuntimeDiagnosticsTick()
    {
        try
        {
            OnRuntimeDiagnosticsTimerTick();
        }
        catch (Exception exception)
        {
            // Diagnostics must never become the reason the engine stops. Preserve the failure in
            // the persistent journal and allow the next watchdog interval to try again.
            EngineLog3D.Error("Diagnostics", $"Control {_controlId} runtime diagnostic tick failed; engine remains active.", exception);
            EngineLog3D.Flush();
        }
    }

    private string CaptureRuntimeHealthSnapshotSafely(string context)
    {
        try
        {
            return CreateRuntimeHealthSnapshot();
        }
        catch (Exception exception)
        {
            EngineLog3D.Error("Diagnostics", $"Control {_controlId} could not capture runtime health ({context}).", exception);
            return $"ControlId={_controlId}{Environment.NewLine}" +
                   $"SessionId={EngineLog3D.SessionId}{Environment.NewLine}" +
                   $"HealthCaptureContext={context}{Environment.NewLine}" +
                   $"HealthCaptureFailure={exception.GetType().FullName}: {exception.Message}{Environment.NewLine}" +
                   exception;
        }
    }

    private void OnRuntimeDiagnosticsTimerTick()
    {
        if (_disposed) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        var now = Stopwatch.GetTimestamp();
        var presentationSuspended = !IsVisible || Bounds.Width <= 0d || Bounds.Height <= 0d ||
            topLevel is Window { WindowState: WindowState.Minimized } ||
            _presenter is IBrowserPageVisibilityPresenter { IsDocumentHidden: true };
        var tick = Scene.UpdateLoop.SimulationTick;
        if (tick != _lastObservedSimulationTick)
        {
            _lastObservedSimulationTick = tick;
            _lastSimulationProgressTicks = now;
        }

        if (_runtimeFault is not null)
        {
            if (ShouldLogSince(ref _lastHealthLogTicks, now, RuntimeHealthLogIntervalMilliseconds))
                EngineLog3D.WriteDiagnosticBlock("RuntimeHealth", $"failed-control-{_controlId}", CaptureRuntimeHealthSnapshotSafely("watchdog-or-failed-state"), EngineLogLevel3D.Warning);
            return;
        }

        if (presentationSuspended)
        {
            Volatile.Write(ref _firstOutstandingRenderRequestTicks, 0);
            _lastSimulationProgressTicks = now;
            if (ShouldLogSince(ref _lastHealthLogTicks, now, RuntimeHealthLogIntervalMilliseconds))
                EngineLog3D.WriteDiagnosticBlock("RuntimeHealth", $"suspended-control-{_controlId}", CaptureRuntimeHealthSnapshotSafely("runtime-heartbeat"));
            return;
        }

        var simulationExpected = NeedsSceneUpdateTimer();
        var simulationStallMs = GetTimestampAgeMilliseconds(_lastSimulationProgressTicks, now);
        if (simulationExpected && simulationStallMs >= WatchdogWarningMilliseconds &&
            ShouldLogSince(ref _lastWatchdogWarningTicks, now, WatchdogWarningMilliseconds))
        {
            _watchdogRearmCount++;
            EngineLog3D.Warning("Watchdog", $"Control {_controlId} simulation made no fixed-tick progress for {simulationStallMs:0} ms while work was expected; rearming schedulers (attempt {_watchdogRearmCount}).");
            EngineLog3D.WriteDiagnosticBlock("RuntimeHealth", $"simulation-stall-{_controlId}", CaptureRuntimeHealthSnapshotSafely("watchdog-or-failed-state"), EngineLogLevel3D.Warning);
            _renderScheduler.Reset();
            UpdateNavigationTimerState();
            RequestPresenterRenderOnly();
            _simulationHost.TryWakeDedicatedWorker();
            if (simulationStallMs >= WatchdogFailureMilliseconds)
            {
                EnterRuntimeFaultState("Watchdog.SimulationStall",
                    new TimeoutException($"Simulation did not advance for {simulationStallMs:0} ms while active work or input required fixed updates."));
                return;
            }
        }

        var outstandingRequestTicks = Volatile.Read(ref _firstOutstandingRenderRequestTicks);
        var frameTicks = Volatile.Read(ref _lastFrameRenderedTicks);
        var renderStallMs = GetTimestampAgeMilliseconds(outstandingRequestTicks, now);
        var frameIsOlderThanRequest = outstandingRequestTicks != 0 && (frameTicks == 0 || frameTicks < outstandingRequestTicks);
        if (frameIsOlderThanRequest && renderStallMs >= WatchdogWarningMilliseconds)
        {
            if (ShouldLogSince(ref _lastWatchdogWarningTicks, now, WatchdogWarningMilliseconds))
            {
                _watchdogRearmCount++;
                EngineLog3D.Warning("Watchdog", $"Control {_controlId} has an unfulfilled render request for {renderStallMs:0} ms; re-requesting a frame (attempt {_watchdogRearmCount}).");
                EngineLog3D.WriteDiagnosticBlock("RuntimeHealth", $"render-stall-{_controlId}", CaptureRuntimeHealthSnapshotSafely("watchdog-or-failed-state"), EngineLogLevel3D.Warning);
            }
            RequestPresenterRenderOnly();
            if (renderStallMs >= WatchdogFailureMilliseconds)
            {
                EnterRuntimeFaultState("Watchdog.RenderStall",
                    new TimeoutException($"The presenter did not complete a requested frame for {renderStallMs:0} ms."));
                return;
            }
        }

        if (ShouldLogSince(ref _lastHealthLogTicks, now, RuntimeHealthLogIntervalMilliseconds))
            EngineLog3D.WriteDiagnosticBlock("RuntimeHealth", $"heartbeat-control-{_controlId}", CaptureRuntimeHealthSnapshotSafely("runtime-heartbeat"));
    }

    private string CreateRuntimeHealthSnapshot()
    {
        var now = Stopwatch.GetTimestamp();
        var host = _simulationHost.CaptureSnapshot();
        var presenter = (_presenter as IScenePresenterDiagnostics3D)?.CapturePresenterSnapshot();
        var input = Volatile.Read(ref _publishedNavigationInput);
        var stats = _lastRenderStats;
        var builder = new StringBuilder(4096);
        AppendHealth(builder, "ControlId", _controlId);
        AppendHealth(builder, "SessionId", EngineLog3D.SessionId);
        AppendHealth(builder, "LogFile", EngineLog3D.CurrentLogFilePath ?? "memory-only");
        AppendHealth(builder, "UIThreadAccess", Dispatcher.UIThread.CheckAccess());
        AppendHealth(builder, "ManagedThread", $"{Environment.CurrentManagedThreadId}:{Thread.CurrentThread.Name ?? "unnamed"}");
        AppendHealth(builder, "Attached", TopLevel.GetTopLevel(this) is not null);
        AppendHealth(builder, "Focused", IsFocused);
        AppendHealth(builder, "Visible/Bounds", $"{IsVisible}; {Bounds.Width:0.##}x{Bounds.Height:0.##}");
        AppendHealth(builder, "RuntimeFault", _runtimeFault is null ? "none" : $"{_runtimeFaultSubsystem}: {_runtimeFault.GetType().Name}: {_runtimeFault.Message}");
        AppendHealth(builder, "Timers", $"navigation={_navigationTimer.IsEnabled}; continuous={_continuousRenderTimer.IsEnabled}; snapshot={_snapshotFallbackTimer.IsEnabled}; diagnostics={_runtimeDiagnosticsTimer.IsEnabled}");
        AppendHealth(builder, "RenderPolicy", $"continuous={ContinuousRendering}; fpsLock={FpsLockEnabled}; target={EffectiveTargetFps:0.##}; automaticSimulation={AutomaticSceneUpdates}; interpolation={FrameInterpolationEnabled}");
        AppendHealth(builder, "RenderRequests/Frames", $"{Interlocked.Read(ref _renderRequestCount)}/{Interlocked.Read(ref _renderedFrameCount)}; requestAgeMs={FormatAge(_lastRenderRequestTicks, now)}; outstandingAgeMs={FormatAge(_firstOutstandingRenderRequestTicks, now)}; frameAgeMs={FormatAge(_lastFrameRenderedTicks, now)}; watchdogRearms={_watchdogRearmCount}");
        AppendHealth(builder, "RenderCoalescing", $"continuousSceneInvalidations={Interlocked.Read(ref _coalescedContinuousSceneInvalidationCount)}");
        AppendHealth(builder, "FrameRate", $"presentedFps={stats.PresentedFramesPerSecond:0.###}; instantFps={stats.InstantaneousPresentedFramesPerSecond:0.###}; intervalMs={stats.FrameTotalMilliseconds:0.###}; jitterMs={stats.PresentationJitterMilliseconds:0.###}; targetFps={EffectiveTargetFps:0.###}; presentedFrames={stats.PresentedFrameCount}");
        AppendHealth(builder, "AutomaticDiagnostic", _lastAutomaticDiagnosticPath ?? "none");
        ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);
        ThreadPool.GetMaxThreads(out var maximumWorkerThreads, out var maximumCompletionPortThreads);
        AppendHealth(builder, "ThreadPool", $"workerAvailable={availableWorkerThreads}/{maximumWorkerThreads}; completionAvailable={availableCompletionPortThreads}/{maximumCompletionPortThreads}; pending={ThreadPool.PendingWorkItemCount}; completed={ThreadPool.CompletedWorkItemCount}; threads={ThreadPool.ThreadCount}");
        using var sceneAccess = Scene.EnterRenderReadScope();
        AppendHealth(builder, "SimulationLoop", $"tick={Scene.UpdateLoop.SimulationTick}; time={Scene.UpdateLoop.SimulationTimeSeconds:0.######}; fixedHz={Scene.UpdateLoop.FixedUpdatesPerSecond:0.###}; accumulator={Scene.UpdateLoop.AccumulatorSeconds:0.######}; dropped={Scene.UpdateLoop.TotalDroppedSeconds:0.######}; paused={Scene.UpdateLoop.IsPaused}; timeScale={Scene.UpdateLoop.TimeScale:0.###}; faulted={Scene.UpdateLoop.IsFaulted}; progressAgeMs={FormatAge(_lastSimulationProgressTicks, now)}");
        AppendHealth(builder, "SimulationCommands", $"pending={Scene.Commands.PendingCount}; posted={Scene.Commands.LastPostedSequence}; completed={Scene.Commands.LastCompletedSequence}");
        var ownership = Scene.World.CaptureOwnershipSnapshot();
        AppendHealth(builder, "World", $"policy={ownership.MutationPolicy}; owner={ownership.OwnerThreadId}:{ownership.OwnerThreadName ?? "unnamed"}; epoch={ownership.OwnerEpoch}; bound={ownership.OwnerBound}; currentIsOwner={ownership.CurrentThreadIsOwner}; compatibilityMutations={ownership.DirectCompatibilityMutationCount}; strictRejections={ownership.StrictMutationRejectionCount}; snapshot={ownership.PublishedSnapshotVersion}@{ownership.PublishedSnapshotTick}; snapshotDrops={ownership.DroppedSnapshotPublicationCount}; jobs={ownership.RegisteredJobCount}; replay={ownership.ReplayCaptureEnabled}/{ownership.ReplayEntryCount}");
        AppendHealth(builder, "SimulationStagesMs", $"commands={Scene.SimulationMetrics.CommandsMilliseconds:0.###}; jobs={Scene.SimulationMetrics.JobsTotalMilliseconds:0.###}/{Scene.SimulationMetrics.JobsExecuted}; jobSnapshot={Scene.SimulationMetrics.JobsSnapshotMilliseconds:0.###}; jobExecute={Scene.SimulationMetrics.JobsExecutionMilliseconds:0.###}; jobCommit={Scene.SimulationMetrics.JobsCommitMilliseconds:0.###}; jobCommands={Scene.SimulationMetrics.JobCommandsCommitted}; parallelJobBatches={Scene.SimulationMetrics.ParallelJobBatches}; user={Scene.SimulationMetrics.UserUpdateMilliseconds:0.###}; animation={Scene.SimulationMetrics.AnimationMilliseconds:0.###}; physics={Scene.SimulationMetrics.PhysicsMilliseconds:0.###}; particles={Scene.SimulationMetrics.ParticleMilliseconds:0.###}; completion={Scene.SimulationMetrics.CompletionMilliseconds:0.###}; total={Scene.SimulationMetrics.TotalMilliseconds:0.###}");
        AppendHealth(builder, "SimulationHost", $"configured={host.ConfiguredMode}; resolved={host.ResolvedMode}; dedicated={host.UsesDedicatedThread}; alive={host.WorkerAlive}; thread={host.WorkerManagedThreadId}:{host.WorkerName}; pendingSeconds={host.PendingSeconds:0.######}; submits={host.SubmitCount}; wakes={host.WakeCount}; advances={host.AdvanceCount}; pumps={host.CommandPumpCount}; success={host.SuccessfulCycleCount}; faults={host.FaultCount}; stop={host.StopRequested}; shutdownTimeout={host.ShutdownTimedOut}; lastSubmitAgeMs={FormatAge(host.LastSubmitTimestamp, now)}; lastSuccessAgeMs={FormatAge(host.LastSuccessTimestamp, now)}; lastFault={host.LastFaultType}: {host.LastFaultMessage}");
        AppendHealth(builder, "Input", $"snapshot={input.Sequence}; enabled={input.Enabled}; mode={input.Mode}; movement={input.Movement}; fast={input.FastMove}; mouseDelta={input.MouseDelta}; jump={input.JumpRequested}; syncAngles={input.SynchronizeCameraAngles}; keys={input.PressedKeyCount}; mouseLooking={_isMouseLooking}; pointerInside={_isPointerInsideScene}; presenterPointerLock={IsPresenterPointerLockActive()}; personGrounded={Volatile.Read(ref _simulationPersonGrounded) != 0}; verticalVelocity={Volatile.Read(ref _simulationPersonVerticalVelocity):0.###}");
        AppendHealth(builder, "Scene", $"change={Scene.ChangeVersion}/{Scene.ChangeSequence}; structure={Scene.StructureVersion}; batchContent={Scene.BatchContentVersion}; batchTransform={Scene.BatchTransformVersion}; registry={Scene.Registry.Version}; objects={Scene.Registry.AllObjects.Count}; renderables={Scene.Registry.Renderables.Count}; pickables={Scene.Registry.Pickables.Count}; colliders={Scene.Registry.Colliders.Count}; activeWork={Scene.HasActiveUpdateWork()}");
        AppendHealth(builder, "LastFrame", $"backend={_presenter?.Kind}; frameMs={stats.FrameTotalMilliseconds:0.###}; backendMs={stats.BackendMilliseconds:0.###}; gpuMs={(stats.GpuTimingAvailable ? stats.GpuFrameMilliseconds.ToString("0.###") : "unavailable")}; draws={stats.DrawCallCount}; triangles={stats.TriangleCount}; visible={stats.VisibleMeshCount}; culled={stats.CulledObjectCount}; uploads={stats.InstanceUploadBytes + stats.TextureUploadBytes + stats.MeshUploadBytes}; allocations={stats.AllocatedBytesPerFrame}; heap={GC.GetTotalMemory(false)}; retainedRebuilds={stats.RetainedOrdinaryPlanRebuildCount}; retainedRecoveries={stats.RetainedOrdinaryCursorRecoveryCount}; skinBatchUpdates={stats.RetainedSkinningBatchUpdateCount}; retainedFailure={stats.RetainedOrdinaryLastFailureReason}");
        AppendAnimationHealth(builder);
        AppendHealth(builder, "RHI", $"backend={stats.RhiBackend}; profile={stats.RhiCapabilityProfile}; adapter={stats.RhiAdapterName}; api={stats.RhiApiVersion}; features={stats.RhiFeatures}; limits={stats.RhiLimits}; resources={stats.RhiResourceCount}; buffers={stats.RhiBufferCount}; textures={stats.RhiTextureCount}; resident={stats.RhiResidentBytes}; peak={stats.RhiPeakResidentBytes}; budget={stats.RhiResidentBudgetBytes}; generation={stats.RhiContextGeneration}; submissions={stats.RhiQueueSubmissionCount}; commands={stats.RhiQueueCommandCount}; completed={stats.RhiCompletedSubmissionId}; frameSlot={stats.RhiFrameResourceSlot}/{stats.RhiBufferedFrameCount}; upload={stats.RhiUploadRingUsed}/{stats.RhiUploadRingCapacity}; pipelines={stats.RhiPipelineCacheCount}; deferred={stats.RhiDeferredReleaseCount}");
        AppendHealth(builder, "GpuDriven", $"active={stats.GpuDrivenActive}; objects={stats.GpuDrivenObjectCount}; meshes={stats.GpuDrivenMeshCount}; materials={stats.GpuDrivenMaterialCount}; meshlets={stats.GpuDrivenMeshletCount}; particles={stats.GpuDrivenParticleCapacity}; compute/render={stats.GpuDrivenComputePassCount}/{stats.GpuDrivenRenderPassCount}; barriers={stats.GpuDrivenBarrierCount}; indirectCapacity={stats.GpuDrivenIndirectCommandCapacity}; upload={stats.GpuDrivenUploadedBytes}; graphPhysical/aliased={stats.GpuDrivenPhysicalResourceCount}/{stats.GpuDrivenAliasedResourceCount}; occlusion/particles/clustered={stats.GpuDrivenOcclusionCullingActive}/{stats.GpuDrivenParticlesActive}/{stats.GpuDrivenClusteredLightingActive}");
        AppendHealth(builder, "GC", $"gen0={GC.CollectionCount(0)}; gen1={GC.CollectionCount(1)}; gen2={GC.CollectionCount(2)}; allocatedTotal={GC.GetTotalAllocatedBytes(false)}; managedHeap={GC.GetTotalMemory(false)}");
        if (presenter is { } presenterSnapshot)
            AppendHealth(builder, "Presenter", $"backend={presenterSnapshot.Backend}; attached={presenterSnapshot.Attached}; initialized={presenterSnapshot.Initialized}; disposed={presenterSnapshot.Disposed}; rendering={presenterSnapshot.Rendering}; pending={presenterSnapshot.RenderPending}; requests={presenterSnapshot.RenderRequestCount}; frames={presenterSnapshot.RenderedFrameCount}; faults={presenterSnapshot.FaultCount}; requestAgeMs={FormatAge(presenterSnapshot.LastRequestTimestamp, now)}; frameAgeMs={FormatAge(presenterSnapshot.LastFrameTimestamp, now)}; state={presenterSnapshot.State}; lastFault={presenterSnapshot.LastFaultType}: {presenterSnapshot.LastFaultMessage}");
        if (Scene.UpdateLoop.Fault is { } simulationFault)
            AppendHealth(builder, "SimulationFaultDetail", simulationFault);
        if (_runtimeFault is { } runtimeFault)
            AppendHealth(builder, "RuntimeFaultDetail", runtimeFault);
        return builder.ToString().TrimEnd();
    }

    private void AppendAnimationHealth(StringBuilder builder)
    {
        var models = Scene.Registry.AnimatedModels;
        AppendHealth(builder, "Animations", $"advanceEnabled={Scene.UpdateLoop.AdvanceAnimations}; models={models.Count}");
        var count = System.Math.Min(models.Count, 8);
        for (var i = 0; i < count; i++)
        {
            var model = models[i];
            var controller = model.Animation;
            var clip = controller.CurrentClip;
            var skinnedParts = 0;
            var minimumSkinningVersion = int.MaxValue;
            var maximumSkinningVersion = 0;
            var parts = model.ModelParts;
            for (var partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                var part = parts[partIndex];
                if (!part.IsSkinned) continue;
                skinnedParts++;
                minimumSkinningVersion = System.Math.Min(minimumSkinningVersion, part.SkinningVersion);
                maximumSkinningVersion = System.Math.Max(maximumSkinningVersion, part.SkinningVersion);
            }

            var skinVersionRange = skinnedParts == 0
                ? "none"
                : $"{minimumSkinningVersion}-{maximumSkinningVersion}";
            var clipDuration = clip?.Duration ?? 0f;
            AppendHealth(
                builder,
                $"Animation[{i}]",
                $"name={model.Name}; clip={clip?.Name ?? "none"}; playing={controller.IsPlaying}; loop={controller.Loop}; speed={controller.Speed:0.###}; time={controller.TimeSeconds:0.###}/{clipDuration:0.###}; parts={parts.Count}; skinnedParts={skinnedParts}; skinVersionRange={skinVersionRange}");
        }
        if (models.Count > count) AppendHealth(builder, "AnimationsOmitted", models.Count - count);
    }

    private static void AppendHealth(StringBuilder builder, string key, object? value)
        => builder.Append(key).Append("=").AppendLine(value?.ToString() ?? "null");

    private static bool ShouldLogSince(ref long timestamp, long now, double intervalMilliseconds)
    {
        if (timestamp != 0 && (now - timestamp) * 1000d / Stopwatch.Frequency < intervalMilliseconds) return false;
        timestamp = now;
        return true;
    }

    private static double GetTimestampAgeMilliseconds(long timestamp, long now)
        => timestamp == 0 ? double.PositiveInfinity : Math.Max(0d, (now - timestamp) * 1000d / Stopwatch.Frequency);

    private static string FormatAge(long timestamp, long now)
        => timestamp == 0 ? "never" : GetTimestampAgeMilliseconds(timestamp, now).ToString("0.###");

    private PickingResult? PickSceneObject(Point point)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            if (Bounds.Width <= 0d || Bounds.Height <= 0d)
            {
                return null;
            }

            using var sceneAccess = Scene.EnterRenderReadScope();
            _controlPickingRequestsSinceLastFrame++;
            var viewportPosition = new Vector2((float)point.X, (float)point.Y);
            var viewportSize = GetViewportSize();
            var ray = ProjectionHelper.CreateRay(Scene.Camera, viewportPosition, viewportSize);

            var meshHit = Raycaster.PickExcluding(Scene, viewportPosition, viewportSize, _controlPlanePickExclusions);
            PickingResult? best = meshHit;

            for (var i = 0; i < _controlPlanes.Count; i++)
            {
                var plane = _controlPlanes[i];
                if (!plane.IsVisible)
                {
                    continue;
                }

                _controlPlanePickTestsSinceLastFrame++;
                if (TryIntersectControlPlane(plane, ray, out var planeHit) &&
                    (best is null || planeHit.Distance < best.Distance))
                {
                    best = planeHit;
                }
            }

            return best;
        }
        finally
        {
            _pickingMillisecondsSinceLastFrame += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
    }

    private bool TryPickControlPlane(ControlPlane3D plane, Point point, out PickingResult hit)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            hit = default!;
            if (Bounds.Width <= 0d || Bounds.Height <= 0d || !plane.IsVisible)
            {
                return false;
            }

            using var sceneAccess = Scene.EnterRenderReadScope();
            _controlPickingRequestsSinceLastFrame++;
            _controlPlanePickTestsSinceLastFrame++;
            var viewportPosition = new Vector2((float)point.X, (float)point.Y);
            var viewportSize = GetViewportSize();
            var ray = ProjectionHelper.CreateRay(Scene.Camera, viewportPosition, viewportSize);
            return TryIntersectControlPlane(plane, ray, out hit);
        }
        finally
        {
            _pickingMillisecondsSinceLastFrame += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
    }

    private bool TryIntersectControlPlane(ControlPlane3D plane, Ray ray, out PickingResult hit)
    {
        Span<Vector3> corners = stackalloc Vector3[4];
        ControlPlaneGeometry.GetWorldCorners(plane, Scene.Camera, corners);
        if (Raycaster.IntersectTriangle(ray, corners[0], corners[1], corners[2], out var distanceA, out var pointA))
        {
            hit = new PickingResult(plane, pointA, distanceA);
            return true;
        }

        if (Raycaster.IntersectTriangle(ray, corners[0], corners[2], corners[3], out var distanceB, out var pointB))
        {
            hit = new PickingResult(plane, pointB, distanceB);
            return true;
        }

        hit = default!;
        return false;
    }

    private Visual GetRootVisual()
    {
        return (TopLevel.GetTopLevel(this) as Visual) ?? this;
    }

    private static float ToWorldUnits(double value, double fallbackPixels)
    {
        if (double.IsNaN(value) || value <= 0d)
        {
            value = fallbackPixels;
        }

        return (float)(value * 0.01d);
    }
}
