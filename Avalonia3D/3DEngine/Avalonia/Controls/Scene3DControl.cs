using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
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
using ThreeDEngine.Core.Navigation;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Physics.Kinematic;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Controls;

public sealed class Scene3DControl : Border
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
    public static readonly StyledProperty<bool> FrameInterpolationEnabledProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(FrameInterpolationEnabled), false);
    public static readonly StyledProperty<double> FrameInterpolationTickFpsProperty = AvaloniaProperty.Register<Scene3DControl, double>(nameof(FrameInterpolationTickFps), 20d);
    public static readonly StyledProperty<bool> AdaptivePerformanceEnabledProperty = AvaloniaProperty.Register<Scene3DControl, bool>(nameof(AdaptivePerformanceEnabled), false);

    private const double PerformanceMetricsUpdateIntervalMilliseconds = 1000d;
    private const double BrowserCameraHoverSuppressionMilliseconds = 180d;
    private const double BrowserVisibilityPollMilliseconds = 250d;

    private readonly Grid _root;
    private readonly Canvas _hiddenHost;
    private readonly Border _performanceMetricsHost;
    private readonly TextBlock _performanceMetricsText;
    private readonly Grid _centerCursorHost;
    private readonly DispatcherTimer _snapshotFallbackTimer;
    private readonly DispatcherTimer _navigationTimer;
    private readonly DispatcherTimer _continuousRenderTimer;
    private readonly HashSet<Key> _pressedKeys;
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
    private float _yawDegrees;
    private float _pitchDegrees;
    private Vector3 _personVelocity;
    private bool _personGrounded;
    private readonly KinematicCharacterController3D _personController = new();
    private DateTime _lastNavigationTickUtc;
    private readonly Dictionary<ControlPlane3D, ControlPlaneRuntimeAdapter> _controlAdapters;
    private readonly HashSet<ControlPlane3D> _creatingControlAdapters;
    private readonly List<ControlPlane3D> _controlPlanes;
    private readonly HashSet<ControlPlane3D> _controlPlaneSet;
    private readonly HashSet<Object3D> _controlPlanePickExclusions;
    private readonly List<ControlPlane3D> _staleControlPlanesScratch;
    private readonly Queue<ControlPlaneRuntimeAdapter> _dirtyControlSnapshotQueue;
    private readonly HashSet<ControlPlaneRuntimeAdapter> _dirtyControlSnapshotSet;
    private Scene3D _scene;
    private IScenePresenter? _presenter;
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

    public Scene3DControl()
    {
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

        Child = _root;

        _scene = new Scene3D();
        ApplyBrowserPerformanceDefaults(_scene);

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
        _navigationTimer.Tick += OnNavigationTimerTick;

        _continuousRenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _continuousRenderTimer.Tick += (_, _) => SafeDispatcherTick(RequestPresenterRenderOnly, "continuous render");

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

        EnsurePresenter();
        _hiddenHost.ZIndex = -1;
        _root.Children.Add(_hiddenHost);
        _root.Children.Add(_centerCursorHost);
        _root.Children.Add(_performanceMetricsHost);
        UpdatePerformanceMetricsVisibility();
        UpdateCenterCursorVisibility();
        UpdateContinuousRenderTimerState();
        UpdateRuntimeOptionsFromControl();

        SubscribeToScene(_scene);
    }

    private static void SafeDispatcherTick(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"3DEngine Scene3DControl dispatcher tick failed ({name}): {ex}");
            Debug.WriteLine($"3DEngine Scene3DControl dispatcher tick failed ({name}): " + ex);
        }
    }

    public event EventHandler<ScenePointerEventArgs>? ObjectClicked;
    public event EventHandler<SceneSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;

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

    public double FrameInterpolationTickFps
    {
        get => GetValue(FrameInterpolationTickFpsProperty);
        set => SetValue(FrameInterpolationTickFpsProperty, value);
    }

    public bool AdaptivePerformanceEnabled
    {
        get => GetValue(AdaptivePerformanceEnabledProperty);
        set => SetValue(AdaptivePerformanceEnabledProperty, value);
    }

    public FreeFlyNavigationSettings FreeFlySettings => _freeFlySettings;

    public PersonNavigationSettings PersonSettings => _personSettings;
    public Scene3D Scene
    {
        get => _scene;
        set
        {
            if (ReferenceEquals(_scene, value))
            {
                return;
            }

            UnsubscribeFromScene(_scene);
            ClearControlAdapters();

            _scene = value ?? throw new ArgumentNullException(nameof(value));
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
        }
    }

    public Object3D? SelectedObject => InteractionManager.SelectedObject;
    public Object3D? HoveredObject => InteractionManager.HoveredObject;

    public T Add<T>(T obj) where T : Object3D
    {
        var added = Scene.Add(obj);
        if (added is ControlPlane3D plane)
        {
            TrackControlPlane(plane);
            EnsureControlAdapter(plane);
        }

        RequestRender();
        return added;
    }

    public Object3D Add(Control avaloniaControl)
    {
        var added = Adapters.Add(avaloniaControl);
        if (added is ControlPlane3D plane)
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
        Scene.Add(plane);
        EnsureControlAdapter(plane);
        RequestRender();
        return plane;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
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
        _snapshotFallbackTimer.Stop();
        _navigationTimer.Stop();
        _continuousRenderTimer.Stop();
        _unlockedRenderPending = false;
        _browserContinuousRenderScheduled = false;
        _pressedKeys.Clear();
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

        if (e.Key == Key.Escape)
        {
            _pressedKeys.Clear();
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
                _pressedKeys.Clear();
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
            _pressedKeys.Add(e.Key);
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
        if (_focusedControlAdapter is not null)
        {
            var capturesKeyboard = _focusedControlAdapter.ShouldCaptureKeyboardInput;
            var handledByControl = ExecuteForwardedControlInput(() => _focusedControlAdapter.HandleKeyUp(e));
            UpdateFocusedControlAdapterState();
            if (handledByControl || capturesKeyboard)
            {
                _pressedKeys.Remove(e.Key);
                UpdateNavigationTimerState();
                e.Handled = true;
                RequestRender();
                return;
            }
        }

        if (IsNavigationKey(e.Key))
        {
            _pressedKeys.Remove(e.Key);
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
        }
        else if (change.Property == FrameInterpolationEnabledProperty || change.Property == FrameInterpolationTickFpsProperty ||
                 change.Property == AdaptivePerformanceEnabledProperty)
        {
            UpdateRuntimeOptionsFromControl();
        }
        else if (change.Property == NavigationModeProperty || change.Property == EnableSceneNavigationProperty || change.Property == MouseLookModeProperty)
        {
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
        if (_presenter is not null)
        {
            return;
        }

        _presenter = Scene3DPlatform.GetFactory().CreatePresenter();
        _presenter.FrameRendered += OnPresenterFrameRendered;
        _presenter.Scene = Scene;
        _presenter.View.IsHitTestVisible = false;
        _presenter.View.ZIndex = 0;
        _root.Children.Add(_presenter.View);
        UpdateCenterCursorVisibility();
    }

    private void SubscribeToScene(Scene3D scene)
    {
        scene.SceneChangedDetailed += OnSceneChanged;
    }

    private void UnsubscribeFromScene(Scene3D scene)
    {
        scene.SceneChangedDetailed -= OnSceneChanged;
    }

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        if (e.Kind == SceneChangeKind.Structure)
        {
            SyncControlAdapters();
        }
        else if (e.Kind == SceneChangeKind.Control)
        {
            EnqueueDirtyControlSnapshot(e.Source as ControlPlane3D);
        }

        if (e.Kind != SceneChangeKind.HighScaleState)
        {
            UpdateNavigationTimerState();
            RequestRender();
            return;
        }

        // In continuous-render high-scale scenes telemetry state changes are consumed by
        // the next scheduled frame. Calling RequestNextFrameRendering for every telemetry
        // batch creates redundant UI render requests and makes frame pacing worse.
        if (!ContinuousRendering)
        {
            RequestRender();
        }
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
        _presenter?.RequestRender();
    }

    private void RequestUnlockedFrameSoon()
    {
        if (_unlockedRenderPending || !ContinuousRendering || FpsLockEnabled || TopLevel.GetTopLevel(this) is null)
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
            _browserContinuousRenderScheduled ||
            !ContinuousRendering ||
            TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        _browserContinuousRenderScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _browserContinuousRenderScheduled = false;
            if (ContinuousRendering && TopLevel.GetTopLevel(this) is not null)
            {
                RequestPresenterRenderOnly();
            }
        }, DispatcherPriority.Render);
    }


    private double EffectiveTargetFps => FpsLockEnabled ? System.Math.Clamp(TargetFps, 1d, 500d) : System.Math.Clamp(UnlockedMaxFps, 1d, 500d);

    private void UpdateRuntimeOptionsFromControl()
    {
        Scene.FrameInterpolator.Enabled = FrameInterpolationEnabled;
        Scene.FrameInterpolator.SimulationTickFps = System.Math.Clamp(FrameInterpolationTickFps, 1d, 240d);
        Scene.AdaptivePerformance.Enabled = AdaptivePerformanceEnabled;
        Scene.Performance.AdaptivePerformanceEnabled = AdaptivePerformanceEnabled;
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private void UpdateRuntimeStats(RenderStats stats)
    {
        var now = Stopwatch.GetTimestamp();
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
        stats.FrameTotalMilliseconds = stats.BackendMilliseconds;
        if (_lastFrameRenderedTicks != 0)
        {
            var realFrameMs = (now - _lastFrameRenderedTicks) * 1000d / Stopwatch.Frequency;
            stats.FrameTotalMilliseconds = realFrameMs;
            var expectedMs = 1000d / EffectiveTargetFps;
            stats.RenderScheduleDelayMilliseconds = System.Math.Max(0d, realFrameMs - expectedMs);
            stats.SchedulerDelayMilliseconds = stats.RenderScheduleDelayMilliseconds;
        }
        _lastFrameRenderedTicks = now;

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


    private void OnPresenterFrameRendered(object? sender, SceneFrameRenderedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnPresenterFrameRendered(sender, e), DispatcherPriority.Background);
            return;
        }

        try
        {
            RefreshBrowserVisibilityState(force: true);

            var statsForRuntime = e.Stats ?? new RenderStats();
            UpdateRuntimeStats(statsForRuntime);
            try
            {
                FrameRendered?.Invoke(this, e);
            }
            catch (Exception subscriberEx)
            {
                Debug.WriteLine("3DEngine Scene3DControl FrameRendered subscriber failed: " + subscriberEx);
            }

            if (OperatingSystem.IsBrowser())
            {
                ScheduleBrowserContinuousFrame();
            }
            else if (ContinuousRendering && !FpsLockEnabled)
            {
                RequestUnlockedFrameSoon();
            }

            if (!ShowPerformanceMetrics)
            {
                return;
            }

            _performanceFrameCount++;
            _performanceFrameMillisecondsLast = e.FrameMilliseconds;
            _performanceFrameMillisecondsTotal += e.FrameMilliseconds;

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

            var fps = _performanceFrameCount * 1000d / elapsedMilliseconds;
            var averageFrameMilliseconds = _performanceFrameMillisecondsTotal / System.Math.Max(_performanceFrameCount, 1);
            var stats = statsForRuntime;

            // FrameRendered may be raised while Avalonia is inside a render pass.
            // Updating TextBlock.Text there invalidates the visual tree during render.
            // Apply the text later through the dispatcher instead.
            _pendingPerformanceMetricsText =
                $"FPS: {fps:0.0}\n" +
                $"Frame: {_performanceFrameMillisecondsLast:0.00} ms | Avg: {averageFrameMilliseconds:0.00} ms\n" +
                $"Backend: {e.Backend}\n" +
                $"Objects: {stats.ObjectCount} | Renderables: {stats.RenderableCount} | Pickables: {stats.PickableCount} | Colliders: {stats.ColliderCount}\n" +
                $"HighScale: {stats.HighScaleInstanceCount} | Chunks: {stats.VisibleChunkCount}/{stats.TotalChunkCount} | Culled: {stats.CulledObjectCount}\n" +
                $"LOD D/S/P/B/C: {stats.LodDetailedCount}/{stats.LodSimplifiedCount}/{stats.LodProxyCount}/{stats.LodBillboardCount}/{stats.LodCulledCount} | PartInst: {stats.HighScaleVisiblePartInstanceCount}\n" +
                $"Draw: {stats.DrawCallCount} | Batches: {stats.InstancedBatchCount} | Tris: {stats.TriangleCount}\n" +
                $"Pipeline: mode={stats.RenderPipelineMode} deferred {OnOff(stats.DeferredActive)}/{OnOff(stats.DeferredRequested)} | GBuffer={OnOff(stats.GBufferActive)} targets={stats.GBufferTargetCount} | HDR={OnOff(stats.HdrActive)} tone={stats.ToneMappingMode} exp={stats.ToneMappingExposure:0.00} gamma={stats.ToneMappingGamma:0.00}\n" +
                $"SSAO: {OnOff(stats.SsaoActive)}/{OnOff(stats.SsaoRequested)} samples={stats.SsaoSampleCount} | Passes={stats.RenderPassCount} | MotionVec={OnOff(stats.MotionVectorsActive)}/{OnOff(stats.MotionVectorsRequested)} | Reason={stats.RenderPipelineReason}\n" +
                $"Particles: {stats.ParticleCount} in {stats.ParticleSystemCount} systems | ParticleVB: {stats.ParticleMeshUploadBytes / 1024d:0.0} KB | InstancedMesh: {stats.InstancedMeshInstanceCount} in {stats.InstancedMeshLayerCount} layers | Throughput retained/fallback: {stats.RetainedThroughputDrawCount}/{stats.ThroughputFallbackDrawCount}\n" +
                $"Models: imported={stats.ImportedModelCount} skinned={stats.SkinnedModelCount} animated={stats.AnimatedModelCount} | Skin matrices={stats.SkinMatrixCount} prim={stats.SkinnedPrimitiveCount} payload={stats.SkinningVertexPayloadBytes / 1024d:0.0} KB | GPUSkin={OnOff(stats.GpuSkinningActive)}/{OnOff(stats.GpuSkinningRequested)} {stats.SkinningFallbackReason}\n" +
                $"Lights D/P/S: {stats.DirectionalLightCount}/{stats.PointLightCount}/{stats.SpotLightCount} | Skybox: {(stats.SkyboxEnabled ? "on" : "off")} mode={stats.SkyboxMode} | Shadows: {(stats.DirectionalShadowEnabled ? "on" : "off")} {stats.ShadowMapResolution}px casters={stats.ShadowCasterCount} maps={stats.ShadowMapCount} {stats.ShadowMapMilliseconds:0.00} ms\n" +
                $"TransformUpload: {stats.InstanceUploadBytes / (1024d * 1024d):0.00} MB | StateUpload: {stats.StateUploadBytes / 1024d:0.0} KB | TexUpload: {stats.TextureUploadBytes / (1024d * 1024d):0.00} MB\n" +
                $"MeshUpload: {stats.MeshUploadBytes / 1024d:0.0} KB | V/I: {stats.VertexBufferUploadBytes / 1024d:0.0}/{stats.IndexBufferUploadBytes / 1024d:0.0} KB | Tangent: {stats.TangentUploadBytes / 1024d:0.0} KB | WireIdx: {stats.WireframeIndexUploadBytes / 1024d:0.0} KB\n" +
                $"Surface: tangentMeshes={stats.TangentSpaceMeshCount} normalMapped={stats.NormalMappedMeshCount} wire/sil={stats.WireframeOverlayDrawCalls}/{stats.SilhouetteOverlayDrawCalls} | Geom: {stats.RenderGeometryCount} | VB/IB: {stats.VertexBufferUploadCount}/{stats.IndexBufferUploadCount} | PacketBytes: {stats.PacketBytes / 1024d:0.0} KB\n" +
                $"Packet: {stats.PacketBuildMilliseconds:0.00} ms | Ser: {stats.SerializationMilliseconds:0.00} ms | Upload: {stats.UploadMilliseconds:0.00} ms | Backend: {stats.BackendMilliseconds:0.00} ms\n" +
                $"WebGLv{stats.WebGlVersion} ClientHS: {(stats.WebGlClientHighScaleRuntime ? "on" : "off")} | GPUAnim: {(stats.WebGlClientGpuTransformAnimation ? "on" : "off")} | JS Cull: {stats.JsCullMilliseconds:0.00} ms | JS Draw: {stats.JsDrawMilliseconds:0.00} ms | JS Frame: {stats.JsFrameMilliseconds:0.00} ms | JS Batches: {stats.JsDrawBatchCount} | Legacy draw/block/str={stats.WebGlLegacyDrawPathCalls}/{stats.WebGlLegacyDrawPathBlockedCalls}/{stats.WebGlLegacyStringProtocolCalls} | bufferData={stats.WebGlBufferDataCalls}/{stats.WebGlDynamicBufferDataCalls}\n" +
                $"JSPatch T/S: {stats.JsTransformPatchRanges}/{stats.JsStatePatchRanges} ranges | {stats.JsTransformPatchBytes / 1024d:0.0}/{stats.JsStatePatchBytes / 1024d:0.0} KB | Route dirty {stats.JsHighScaleDirtyTransformInstances}/{stats.JsHighScaleDirtyStateInstances} refs {stats.JsHighScalePatchRoutedTransformRefs}/{stats.JsHighScalePatchRoutedStateRefs} batches {stats.JsHighScalePatchTouchedTransformBatches}/{stats.JsHighScalePatchTouchedStateBatches} | JSAnim: {stats.JsAnimationUploadBatches} batches/{stats.JsAnimationUploadBytes / 1024d:0.0} KB | TexErr: {stats.JsTexturePayloadErrors}/{stats.JsPalettePayloadErrors} | Patch: {stats.JsPatchMilliseconds:0.00} ms\n" +
                $"Pick: {stats.PickingMilliseconds:0.00} ms ({stats.ControlPointerPickCount}/{stats.ControlPlanePickTestCount}) | Phys: {stats.PhysicsMilliseconds:0.00} ms | Live: {stats.LiveSnapshotMilliseconds:0.00} ms snap={stats.ControlSnapshotRefreshCount} q={stats.ControlSnapshotQueueHighWater}\n" +
                $"Alloc: {stats.AllocatedMegabytesPerSecond:0.00} MB/s | FrameAlloc: {stats.AllocatedBytesPerFrame / 1024d:0.0} KB | GC: {stats.Gen0Collections}/{stats.Gen1Collections}/{stats.Gen2Collections} | Heap: {stats.ManagedHeapBytes / (1024d * 1024d):0.0} MB\n" +
                $"FPSLock: {(stats.FpsLocked ? "on" : "off")} {stats.TargetFps:0} | Interp: {(stats.FrameInterpolationEnabled ? "on" : "off")} a={stats.InterpolationAlpha:0.00} | Adaptive: {(stats.AdaptivePerformanceEnabled ? "on" : "off")} q={stats.AdaptiveQualityScale:0.00} | Delay: {stats.RenderScheduleDelayMilliseconds:0.00} ms\n" +
                $"MeshCache: {stats.MeshCacheCount} | Registry: {stats.RegistryVersion}";
            SchedulePerformanceMetricsTextUpdate();

            _performanceFrameCount = 0;
            _performanceFrameMillisecondsTotal = 0d;
            _performanceWindowStartTicks = Stopwatch.GetTimestamp();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("3DEngine Scene3DControl frame callback failed: " + ex);
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
        Scene.Debug.ShowPerformanceMetrics = ShowPerformanceMetrics;
        _performanceMetricsHost.IsVisible = ShowPerformanceMetrics;
        if (!ShowPerformanceMetrics)
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
        SyncCameraAnglesFromForward();
        _isMouseLooking = true;
        _mouseLookPointer = e.Pointer;
        _mouseLookPointer.Capture(this);
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
        SyncCameraAnglesFromForward();
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

        ApplyMouseLook(delta);
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

        ApplyMouseLook(delta);
    }

    private void ApplyMouseLook(Vector2 delta)
    {
        if (!EnableSceneNavigation || NavigationMode == SceneNavigationMode.None)
        {
            return;
        }

        var sensitivity = NavigationMode == SceneNavigationMode.Person ? PersonSettings.MouseSensitivity : FreeFlySettings.MouseSensitivity;
        var invertX = NavigationMode == SceneNavigationMode.Person ? PersonSettings.InvertMouseX : FreeFlySettings.InvertMouseX;
        var invertY = NavigationMode == SceneNavigationMode.Person ? PersonSettings.InvertMouseY : FreeFlySettings.InvertMouseY;
        _yawDegrees += delta.X * sensitivity * (invertX ? -1f : 1f);
        _pitchDegrees = System.Math.Clamp(_pitchDegrees + (-delta.Y * sensitivity * (invertY ? -1f : 1f)), -88f, 88f);
        ApplyCameraForwardFromAngles();
        SuppressHoverPickingBriefly();
        RequestRender();
    }

    private void OnNavigationTimerTick(object? sender, EventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is null || !NeedsNavigationTimer())
        {
            _navigationTimer.Stop();
            _lastNavigationTickUtc = default;
            return;
        }

        var now = DateTime.UtcNow;
        var dt = _lastNavigationTickUtc == default ? 1f / 60f : (float)(now - _lastNavigationTickUtc).TotalSeconds;
        _lastNavigationTickUtc = now;
        dt = System.Math.Clamp(dt, 0.001f, 1f / 15f);

        TryApplyPresenterPointerLockDelta();

        Scene.StepPhysics(dt);

        if (EnableSceneNavigation && NavigationMode == SceneNavigationMode.Person)
        {
            StepPersonNavigation(dt);
        }
        else if (EnableSceneNavigation && NavigationMode == SceneNavigationMode.FreeFly)
        {
            StepFreeFlyNavigation(dt);
        }

        RequestRender();
        UpdateNavigationTimerState();
    }

    private void UpdateNavigationTimerState()
    {
        if (TopLevel.GetTopLevel(this) is null)
        {
            _navigationTimer.Stop();
            _lastNavigationTickUtc = default;
            return;
        }

        if (NeedsNavigationTimer())
        {
            if (!_navigationTimer.IsEnabled)
            {
                _lastNavigationTickUtc = DateTime.UtcNow;
                _navigationTimer.Start();
            }
        }
        else
        {
            _navigationTimer.Stop();
            _lastNavigationTickUtc = default;
        }
    }

    private bool NeedsNavigationTimer()
    {
        if (HasActiveDynamicPhysicsBodies())
        {
            return true;
        }

        if (!EnableSceneNavigation || NavigationMode == SceneNavigationMode.None)
        {
            return false;
        }

        if (_pressedKeys.Count > 0 || IsButtonDragMouseLookActive || IsCenterLockedMouseLookActive)
        {
            return true;
        }

        return NavigationMode == SceneNavigationMode.Person && (!_personGrounded || System.MathF.Abs(_personVelocity.Y) > 0.001f);
    }

    private bool HasActiveDynamicPhysicsBodies()
    {
        foreach (var obj in Scene.Registry.SnapshotDynamicBodies())
        {
            var body = obj.Rigidbody;
            if (body is null || body.IsKinematic)
            {
                continue;
            }

            if (body.Velocity.LengthSquared() > 0.000001f || (body.UseGravity && !body.IsGrounded))
            {
                return true;
            }
        }

        return false;
    }

    private void StepFreeFlyNavigation(float dt)
    {
        var input = GetMovementInput();
        if (input == Vector3.Zero)
        {
            return;
        }

        var speed = FreeFlySettings.MoveSpeed * (IsPressed(Key.LeftShift) || IsPressed(Key.RightShift) ? FreeFlySettings.FastMoveMultiplier : 1f);
        var forward = Scene.Camera.Forward;
        var right = Scene.Camera.Right;
        var up = Scene.Camera.SafeUp;
        var direction = right * input.X + up * input.Y + forward * input.Z;
        if (direction.LengthSquared() < 0.0001f)
        {
            return;
        }

        direction = Vector3.Normalize(direction);
        var translation = direction * speed * dt;
        Scene.Camera.Position += translation;
        Scene.Camera.Target += translation;
    }

    private void TryStartPersonJump()
    {
        if (!EnableSceneNavigation || NavigationMode != SceneNavigationMode.Person || !_personGrounded)
        {
            return;
        }

        _personController.Jump(System.MathF.Max(PersonSettings.JumpSpeed, 0f));
        _personVelocity = _personController.Velocity;
        _personGrounded = _personController.IsGrounded;
        UpdateNavigationTimerState();
    }

    public void ResetPersonNavigationState(bool grounded = false)
    {
        _personVelocity = Vector3.Zero;
        _personGrounded = grounded;
        _personController.Reset(Vector3.Zero, grounded);
    }

    private void StepPersonNavigation(float dt)
    {
        var input = GetMovementInput();
        var horizontalForward = new Vector3(Scene.Camera.Forward.X, 0f, Scene.Camera.Forward.Z);
        var forward = horizontalForward.LengthSquared() < 0.0001f ? -Vector3.UnitZ : Vector3.Normalize(horizontalForward);
        if (!IsFinite(forward))
        {
            forward = -Vector3.UnitZ;
        }

        var rightVector = Vector3.Cross(forward, Vector3.UnitY);
        var right = rightVector.LengthSquared() < 0.0001f ? Vector3.UnitX : Vector3.Normalize(rightVector);
        var move = right * input.X + forward * input.Z;
        if (move.LengthSquared() > 0.0001f)
        {
            move = Vector3.Normalize(move);
        }

        var speed = PersonSettings.MoveSpeed * (IsPressed(Key.LeftShift) || IsPressed(Key.RightShift) ? PersonSettings.RunMultiplier : 1f);
        _personController.Radius = System.MathF.Max(0.05f, PersonSettings.BodyRadius);
        _personController.Height = System.MathF.Max(_personController.Radius * 2f, PersonSettings.BodyHeight);
        _personController.StepHeight = System.MathF.Max(0f, PersonSettings.StepHeight);
        _personController.Gravity = new Vector3(0f, PersonSettings.Gravity, 0f);

        var eyeToFoot = new Vector3(0f, System.MathF.Max(0.05f, PersonSettings.EyeHeight), 0f);
        var footPosition = Scene.Camera.Position - eyeToFoot;
        var horizontalMotion = move * speed * dt;
        var resolvedFootPosition = _personController.Move(Scene, footPosition, horizontalMotion, dt);
        Scene.Camera.Position = resolvedFootPosition + eyeToFoot;
        _personVelocity = _personController.Velocity;
        _personGrounded = _personController.IsGrounded;
        ApplyCameraForwardFromAngles();
    }

    private Vector3 ResolvePersonCollisions(Vector3 oldPosition, Vector3 desiredPosition, float dt)
    {
        var half = new Vector3(PersonSettings.BodyRadius, PersonSettings.BodyHeight * 0.5f, PersonSettings.BodyRadius);
        var bodyCenter = desiredPosition + new Vector3(0f, PersonSettings.BodyHeight * 0.5f - PersonSettings.EyeHeight, 0f);
        var bodyBounds = new Bounds3D(bodyCenter - half, bodyCenter + half);
        _personGrounded = false;

        for (var i = 0; i < 3; i++)
        {
            var changed = false;
            foreach (var obj in Scene.Registry.SnapshotColliders())
            {
                if (obj.Collider is null)
                {
                    continue;
                }

                var otherBounds = obj.Collider.GetWorldBounds(obj);
                if (!TryGetAabbPenetration(bodyBounds, otherBounds, out var correction, out var normal))
                {
                    continue;
                }

                if (System.MathF.Abs(normal.Y) < 0.25f && _personGrounded && PersonSettings.StepHeight > 0f)
                {
                    var stepUp = new Vector3(0f, PersonSettings.StepHeight, 0f);
                    var steppedCenter = bodyCenter + stepUp;
                    var steppedBounds = new Bounds3D(steppedCenter - half, steppedCenter + half);
                    if (!steppedBounds.Intersects(otherBounds))
                    {
                        bodyCenter = steppedCenter;
                        bodyBounds = steppedBounds;
                        changed = true;
                        continue;
                    }
                }

                bodyCenter += correction;
                bodyBounds = new Bounds3D(bodyCenter - half, bodyCenter + half);
                changed = true;

                if (normal.Y > 0.4f)
                {
                    _personGrounded = true;
                    if (_personVelocity.Y < 0f)
                    {
                        _personVelocity.Y = 0f;
                    }
                }
                else
                {
                    var pushVelocity = new Vector3(_personVelocity.X, 0f, _personVelocity.Z);
                    var normalVelocity = Vector3.Dot(_personVelocity, normal);
                    if (normalVelocity < 0f)
                    {
                        _personVelocity -= normal * normalVelocity;
                    }

                    if (obj.Rigidbody is { IsKinematic: false } body)
                    {
                        var push = pushVelocity * PersonSettings.PushStrength;
                        if (push.LengthSquared() > 0.0001f)
                        {
                            body.AddImpulse(push * dt);
                        }
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return bodyCenter - new Vector3(0f, PersonSettings.BodyHeight * 0.5f - PersonSettings.EyeHeight, 0f);
    }

    private static bool TryGetAabbPenetration(Bounds3D a, Bounds3D b, out Vector3 correction, out Vector3 normal)
    {
        correction = Vector3.Zero;
        normal = Vector3.Zero;
        if (!a.IsValid || !b.IsValid || !a.Intersects(b)) return false;

        var dx1 = b.Max.X - a.Min.X;
        var dx2 = a.Max.X - b.Min.X;
        var dy1 = b.Max.Y - a.Min.Y;
        var dy2 = a.Max.Y - b.Min.Y;
        var dz1 = b.Max.Z - a.Min.Z;
        var dz2 = a.Max.Z - b.Min.Z;

        var px = dx1 < dx2 ? dx1 : -dx2;
        var py = dy1 < dy2 ? dy1 : -dy2;
        var pz = dz1 < dz2 ? dz1 : -dz2;

        var ax = System.MathF.Abs(px);
        var ay = System.MathF.Abs(py);
        var az = System.MathF.Abs(pz);

        if (ax <= ay && ax <= az)
        {
            correction = new Vector3(px, 0f, 0f);
            normal = new Vector3(System.MathF.Sign(px), 0f, 0f);
        }
        else if (ay <= az)
        {
            correction = new Vector3(0f, py, 0f);
            normal = new Vector3(0f, System.MathF.Sign(py), 0f);
        }
        else
        {
            correction = new Vector3(0f, 0f, pz);
            normal = new Vector3(0f, 0f, System.MathF.Sign(pz));
        }

        return true;
    }

    private static bool IsFinite(Vector3 value)
        => !float.IsNaN(value.X) && !float.IsNaN(value.Y) && !float.IsNaN(value.Z) &&
           !float.IsInfinity(value.X) && !float.IsInfinity(value.Y) && !float.IsInfinity(value.Z);

    private Vector3 GetMovementInput()
    {
        var input = Vector3.Zero;
        if (IsPressed(Key.A) || IsPressed(Key.Left)) input.X -= 1f;
        if (IsPressed(Key.D) || IsPressed(Key.Right)) input.X += 1f;
        if (IsPressed(Key.W) || IsPressed(Key.Up)) input.Z += 1f;
        if (IsPressed(Key.S) || IsPressed(Key.Down)) input.Z -= 1f;
        if (NavigationMode == SceneNavigationMode.FreeFly)
        {
            if (IsPressed(Key.Space)) input.Y += 1f;
            if (IsPressed(Key.LeftCtrl) || IsPressed(Key.RightCtrl)) input.Y -= 1f;
        }

        return input;
    }

    private bool IsPressed(Key key) => _pressedKeys.Contains(key);

    private static bool IsNavigationKey(Key key)
        => key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right or Key.Space or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift;

    private void SyncCameraAnglesFromForward()
    {
        var f = Scene.Camera.Forward;
        _yawDegrees = MathF.Atan2(f.X, -f.Z) * 180f / MathF.PI;
        _pitchDegrees = MathF.Asin(System.Math.Clamp(f.Y, -1f, 1f)) * 180f / MathF.PI;
    }

    private void ApplyCameraForwardFromAngles()
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
        Scene.Camera.Target = Scene.Camera.Position + forward;
        Scene.Camera.Up = Vector3.UnitY;
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
            _pressedKeys.Clear();
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
            _pressedKeys.Clear();
            _hasMouseLookPosition = false;
            UpdateNavigationTimerState();
        }
    }

    private PickingResult? PickSceneObject(Point point)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            if (Bounds.Width <= 0d || Bounds.Height <= 0d)
            {
                return null;
            }

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
