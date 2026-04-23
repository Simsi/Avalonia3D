using System;
using System.Collections.Generic;
using System.Linq;
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
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Controls;

public sealed class Scene3DControl : Border
{
    private readonly Grid _root;
    private readonly Canvas _hiddenHost;
    private readonly DispatcherTimer _snapshotFallbackTimer;
    private static readonly TimeSpan SnapshotMinInterval = TimeSpan.FromMilliseconds(33);
    private readonly Dictionary<ControlPlane3D, ControlPlaneRuntimeAdapter> _controlAdapters;
    private readonly HashSet<ControlPlane3D> _creatingControlAdapters;
    private Scene3D _scene;
    private IScenePresenter? _presenter;
    private ControlPlaneRuntimeAdapter? _activeControlAdapter;
    private ControlPlaneRuntimeAdapter? _focusedControlAdapter;
    private int _forwardedControlInputDepth;

    public Scene3DControl()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;

        _root = new Grid();
        _hiddenHost = new Canvas
        {
            Width = 1d,
            Height = 1d,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true
        };

        Child = _root;

        _scene = new Scene3D();
        InteractionManager = new SceneInteractionManager(_scene, RequestRender);
        InteractionManager.ObjectClicked += OnObjectClicked;
        InteractionManager.SelectionChanged += OnSelectionChanged;
        Adapters = new Avalonia3DAdapterRegistry(_scene);
        _controlAdapters = new Dictionary<ControlPlane3D, ControlPlaneRuntimeAdapter>();
        _creatingControlAdapters = new HashSet<ControlPlane3D>();

        EnsurePresenter();
        _root.Children.Add(_hiddenHost);

        _snapshotFallbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _snapshotFallbackTimer.Tick += OnSnapshotFallbackTimerTick;

        SubscribeToScene(_scene);
    }

    public event EventHandler<ScenePointerEventArgs>? ObjectClicked;
    public event EventHandler<SceneSelectionChangedEventArgs>? SelectionChanged;

    public SceneInteractionManager InteractionManager { get; }
    public Avalonia3DAdapterRegistry Adapters { get; }

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
            SubscribeToScene(_scene);
            InteractionManager.SetScene(_scene);
            Adapters.SetScene(_scene);

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
            Height = ToWorldUnits(control.Height, 180d)
        };

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
            RequestRender();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _snapshotFallbackTimer.Stop();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnPointerPressed(e);

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed || props.IsMiddleButtonPressed)
        {
            ClearControlHover(e);
            InteractionManager.HandlePointerPressed(this, e);
            return;
        }

        if (TryHandleControlPointerPressed(e))
        {
            e.Handled = true;
            return;
        }

        ClearActiveControlState(e);
        InteractionManager.HandlePointerPressed(this, e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnPointerMoved(e);

        var props = e.GetCurrentPoint(this).Properties;
        if (_activeControlAdapter is null && (props.IsRightButtonPressed || props.IsMiddleButtonPressed))
        {
            ClearControlHover(e);
            InteractionManager.HandlePointerMoved(this, e);
            return;
        }

        if (TryHandleControlPointerMoved(e))
        {
            e.Handled = true;
            return;
        }

        ClearControlHover(e);
        InteractionManager.HandlePointerMoved(this, e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnPointerReleased(e);

        if (_activeControlAdapter is not null && ExecuteForwardedControlInput(() => TryHandleControlPointerReleased(e)))
        {
            e.Handled = true;
            return;
        }

        InteractionManager.HandlePointerReleased(this, e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnPointerWheelChanged(e);

        // By default, wheel controls the camera so scene navigation remains usable
        // even when a control plane is under the pointer.
        InteractionManager.HandlePointerWheel(this, e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnKeyDown(e);
        if (_focusedControlAdapter is not null && ExecuteForwardedControlInput(() => _focusedControlAdapter.HandleKeyDown(e)))
        {
            e.Handled = true;
            RequestRender();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnKeyUp(e);
        if (_focusedControlAdapter is not null && ExecuteForwardedControlInput(() => _focusedControlAdapter.HandleKeyUp(e)))
        {
            e.Handled = true;
            RequestRender();
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (IsForwardingControlInput)
        {
            return;
        }

        base.OnTextInput(e);
        if (_focusedControlAdapter is not null && ExecuteForwardedControlInput(() => _focusedControlAdapter.HandleTextInput(e)))
        {
            e.Handled = true;
            RequestRender();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            RequestRender();
        }
    }

    private void EnsurePresenter()
    {
        if (_presenter is not null)
        {
            return;
        }

        _presenter = Scene3DPlatform.GetFactory().CreatePresenter();
        _presenter.Scene = Scene;
        _presenter.View.IsHitTestVisible = false;
        _root.Children.Add(_presenter.View);
    }

    private void SubscribeToScene(Scene3D scene)
    {
        scene.SceneChanged += OnSceneChanged;
    }

    private void UnsubscribeFromScene(Scene3D scene)
    {
        scene.SceneChanged -= OnSceneChanged;
    }

    private void OnSceneChanged(object? sender, EventArgs e)
    {
        SyncControlAdapters();
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

        _presenter?.RequestRender();
    }

    private void EnsureControlAdapter(ControlPlane3D plane)
    {
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
            _controlAdapters[plane] = adapter;
            adapter.MarkDirty();
        }
        finally
        {
            _creatingControlAdapters.Remove(plane);
        }

        UpdateSnapshotTimerState();
    }

    private void RemoveControlAdapter(ControlPlane3D plane)
    {
        if (!_controlAdapters.TryGetValue(plane, out var adapter))
        {
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

        adapter.Dispose();
        _controlAdapters.Remove(plane);
        UpdateSnapshotTimerState();
    }

    private void ClearControlAdapters()
    {
        foreach (var adapter in _controlAdapters.Values)
        {
            adapter.Dispose();
        }

        _controlAdapters.Clear();
        _activeControlAdapter = null;
        _focusedControlAdapter = null;
        UpdateSnapshotTimerState();
    }

    private void SyncControlAdapters()
    {
        var planes = Scene.Objects.OfType<ControlPlane3D>().ToHashSet();
        foreach (var plane in planes)
        {
            EnsureControlAdapter(plane);
        }

        foreach (var stale in _controlAdapters.Keys.Where(p => !planes.Contains(p)).ToList())
        {
            RemoveControlAdapter(stale);
        }
    }

    private void RefreshDirtyControlSnapshots()
    {
        var now = DateTime.UtcNow;
        foreach (var adapter in _controlAdapters.Values)
        {
            if (adapter.ShouldRefresh(now, SnapshotMinInterval))
            {
                adapter.UpdateSnapshot();
            }
        }
    }

    private void UpdateSnapshotTimerState()
    {
        if (_controlAdapters.Count > 0 && TopLevel.GetTopLevel(this) is not null)
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
    {
        var hit = PickSceneObject(e.GetPosition(this));
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
        e.Pointer.Capture(this);
        RequestRender();
        return true;
    }

    private bool TryHandleControlPointerMoved(PointerEventArgs e)
    {
        var hit = PickSceneObject(e.GetPosition(this));
        var plane = hit?.Object as ControlPlane3D;

        if (_activeControlAdapter is not null && _activeControlAdapter.IsPointerCaptured)
        {
            var worldPoint = ReferenceEquals(plane, _activeControlAdapter.Plane) ? hit?.WorldPosition : null;
            InteractionManager.ClearHover();
            if (ExecuteForwardedControlInput(() => _activeControlAdapter.HandlePointerMoved(e, Scene.Camera, GetRootVisual(), worldPoint)))
            {
                RequestRender();
                return true;
            }
        }

        if (plane is null || !_controlAdapters.TryGetValue(plane, out var adapter))
        {
            return false;
        }

        if (!adapter.IsInteractionReady)
        {
            return false;
        }

        InteractionManager.ClearHover();
        if (!ExecuteForwardedControlInput(() => adapter.HandlePointerMoved(e, Scene.Camera, GetRootVisual(), hit!.WorldPosition)))
        {
            return false;
        }

        RequestRender();
        return true;
    }

    private bool TryHandleControlPointerReleased(PointerReleasedEventArgs e)
    {
        var hit = PickSceneObject(e.GetPosition(this));
        var plane = hit?.Object as ControlPlane3D;
        var adapter = _activeControlAdapter;

        if (adapter is null && plane is not null)
        {
            _controlAdapters.TryGetValue(plane, out adapter);
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
        RequestRender();
        return true;
    }

    private bool TryHandleControlPointerWheel(PointerWheelEventArgs e)
    {
        var hit = PickSceneObject(e.GetPosition(this));
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

        RequestRender();
        return true;
    }

    private void ClearControlHover(PointerEventArgs e)
    {
        foreach (var adapter in _controlAdapters.Values)
        {
            if (!ReferenceEquals(adapter, _activeControlAdapter) || !adapter.IsPointerCaptured)
            {
                adapter.ClearHover(e, GetRootVisual());
            }
        }
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

    private PickingResult? PickSceneObject(Point point)
    {
        if (Bounds.Width <= 0d || Bounds.Height <= 0d)
        {
            return null;
        }

        var viewportPosition = new Vector2((float)point.X, (float)point.Y);
        var viewportSize = new Vector2((float)System.Math.Max(Bounds.Width, 1d), (float)System.Math.Max(Bounds.Height, 1d));
        var ray = ProjectionHelper.CreateRay(Scene.Camera, viewportPosition, viewportSize);

        var meshHit = Raycaster.Pick(Scene, viewportPosition, viewportSize, obj => obj is not ControlPlane3D);
        PickingResult? best = meshHit;

        foreach (var plane in Scene.Objects.OfType<ControlPlane3D>())
        {
            if (!plane.IsVisible)
            {
                continue;
            }

            var corners = ControlPlaneGeometry.GetWorldCorners(plane, Scene.Camera);
            if (Raycaster.IntersectTriangle(ray, corners[0], corners[1], corners[2], out var distanceA, out var pointA))
            {
                if (best is null || distanceA < best.Distance)
                {
                    best = new PickingResult(plane, pointA, distanceA);
                }

                continue;
            }

            if (Raycaster.IntersectTriangle(ray, corners[0], corners[2], corners[3], out var distanceB, out var pointB))
            {
                if (best is null || distanceB < best.Distance)
                {
                    best = new PickingResult(plane, pointB, distanceB);
                }
            }
        }

        return best;
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
