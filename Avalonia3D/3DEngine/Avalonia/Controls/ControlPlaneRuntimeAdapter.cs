using System;
using System.Numerics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Controls;

internal sealed class ControlPlaneRuntimeAdapter : IDisposable
{
    private readonly ControlPlane3D _plane;
    private readonly Control _sourceControl;
    private readonly Canvas _host;
    private RenderTargetBitmap? _bitmap;
    private Interactive? _hoveredElement;
    private Interactive? _pressedElement;
    private Button? _pressedButton;
    private Interactive? _focusedElement;
    private bool _isPointerCaptured;
    private Point _lastLocalPoint;
    private bool _isDisposed;
    private bool _isUpdatingSnapshot;
    private DateTime _lastSnapshotUtc = DateTime.MinValue;
    private Size _lastRenderedSize;

    public ControlPlaneRuntimeAdapter(ControlPlane3D plane, Canvas host)
    {
        _plane = plane ?? throw new ArgumentNullException(nameof(plane));
        _sourceControl = plane.Content ?? throw new ArgumentNullException(nameof(plane.Content));
        _host = host ?? throw new ArgumentNullException(nameof(host));

        Canvas.SetLeft(_sourceControl, 0d);
        Canvas.SetTop(_sourceControl, 0d);
        _sourceControl.IsHitTestVisible = true;
        _host.Children.Add(_sourceControl);

        _sourceControl.PropertyChanged += OnControlPropertyChanged;
        _sourceControl.LayoutUpdated += OnControlLayoutUpdated;
    }

    public ControlPlane3D Plane => _plane;
    public Control SourceControl => _sourceControl;
    public bool IsPointerCaptured => _isPointerCaptured;
    public bool IsDirty => _plane.SnapshotDirty;
    public DateTime LastSnapshotUtc => _lastSnapshotUtc;
    public Interactive? FocusedElement => _focusedElement;
    public bool HasFocus => _focusedElement is not null;
    public bool ShouldCaptureKeyboardInput => IsTextInputElement(_focusedElement);
    public bool IsInteractionReady => _plane.Snapshot is not null && IsActuallyAttached(_sourceControl);

    public bool ShouldRefresh(DateTime nowUtc, TimeSpan minInterval)
    {
        if (!_plane.SnapshotDirty)
        {
            return false;
        }

        return _plane.Snapshot is null || (nowUtc - _lastSnapshotUtc) >= minInterval;
    }

    public void MarkDirty()
    {
        _plane.MarkSnapshotDirty();
    }

    public void UpdateSnapshot(bool force = false)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!force && !_plane.SnapshotDirty)
        {
            return;
        }

        if (TopLevel.GetTopLevel(_sourceControl) is null || !IsActuallyAttached(_sourceControl))
        {
            return;
        }

        var size = DetermineRenderSize(_sourceControl);
        var pixelWidth = System.Math.Max((int)System.Math.Ceiling(size.Width), 1);
        var pixelHeight = System.Math.Max((int)System.Math.Ceiling(size.Height), 1);

        _isUpdatingSnapshot = true;
        try
        {
            _sourceControl.ApplyTemplate();
            _sourceControl.Measure(size);
            _sourceControl.Arrange(new Rect(0, 0, size.Width, size.Height));

            if (_bitmap is null || _bitmap.PixelSize.Width != pixelWidth || _bitmap.PixelSize.Height != pixelHeight)
            {
                _bitmap?.Dispose();
                _bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight));
            }

            _bitmap.Render(_sourceControl);
            _plane.UpdateSnapshot(_bitmap, pixelWidth, pixelHeight);
            _lastSnapshotUtc = DateTime.UtcNow;
            _lastRenderedSize = size;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            _isUpdatingSnapshot = false;
        }
    }


    private bool TryRaiseEvent(Interactive target, RoutedEventArgs args)
    {
        try
        {
            target.RaiseEvent(args);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void TryFocusElement()
    {
        try
        {
            if (_focusedElement is InputElement focusableInput && focusableInput.Focusable && focusableInput.GetVisualRoot() is not null)
            {
                focusableInput.Focus();
            }
            else if (_sourceControl.GetVisualRoot() is not null)
            {
                _sourceControl.Focus();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }


    private Visual GetEventRootVisual(Visual fallback)
    {
        return (_sourceControl.GetVisualRoot() as Visual) ?? fallback;
    }

    public void ClearHover(PointerEventArgs sourceEvent, Visual rootVisual)
    {
        if (_hoveredElement is null)
        {
            return;
        }

        var eventRoot = GetEventRootVisual(rootVisual);
        var oldHover = _hoveredElement;
        _hoveredElement = null;
        if (IsTextInputElement(_focusedElement))
        {
            _focusedElement = null;
        }

        var point = TranslateControlPointToRoot(oldHover, _lastLocalPoint, eventRoot);
        var routed = new PointerEventArgs(
            InputElement.PointerExitedEvent,
            oldHover,
            sourceEvent.Pointer,
            eventRoot,
            point,
            sourceEvent.Timestamp,
            sourceEvent.GetCurrentPoint(rootVisual).Properties,
            sourceEvent.KeyModifiers);
        if (TryRaiseEvent(oldHover, routed))
        {
            MarkDirty();
        }
    }

    public void ClearFocus()
    {
        _focusedElement = null;
        _pressedElement = null;
        _pressedButton = null;
        _isPointerCaptured = false;
    }

    public bool HandlePointerPressed(PointerPressedEventArgs sourceEvent, Vector3 worldHit, Camera3D camera, Visual rootVisual)
    {
        if (!IsInteractionReady)
        {
            return false;
        }

        if (!TryMapToLocal(worldHit, camera, out var localPoint, false))
        {
            return false;
        }

        _lastLocalPoint = localPoint;
        var target = ResolveInteractiveTarget(localPoint);
        UpdateHoverTarget(target, sourceEvent, rootVisual, localPoint);

        _pressedElement = target;
        _pressedButton = FindClickableButton(target);
        _focusedElement = FindFocusableTarget(target);
        _isPointerCaptured = true;

        TryFocusElement();
        var eventRoot = GetEventRootVisual(rootVisual);
        var forwarded = new PointerPressedEventArgs(
            target,
            sourceEvent.Pointer,
            eventRoot,
            TranslateControlPointToRoot(target, localPoint, eventRoot),
            sourceEvent.Timestamp,
            sourceEvent.GetCurrentPoint(rootVisual).Properties,
            sourceEvent.KeyModifiers,
            sourceEvent.ClickCount);

        if (!TryRaiseEvent(target, forwarded))
        {
            return false;
        }

        MarkDirty();
        return true;
    }

    public bool HandlePointerMoved(PointerEventArgs sourceEvent, Camera3D camera, Visual rootVisual, Vector3? worldHit)
    {
        if (!IsInteractionReady)
        {
            return false;
        }

        Point localPoint = default;
        var isInside = worldHit.HasValue && TryMapToLocal(worldHit.Value, camera, out localPoint, true);

        if (!isInside && _isPointerCaptured)
        {
            var viewport = GetViewportSize(rootVisual);
            var ray = ProjectionHelper.CreateRay(camera, new System.Numerics.Vector2((float)sourceEvent.GetPosition(rootVisual).X, (float)sourceEvent.GetPosition(rootVisual).Y), viewport);
            if (!ControlPlaneGeometry.TryIntersectInfinitePlane(_plane, camera, ray, out var worldPoint) ||
                !ControlPlaneGeometry.TryMapWorldHitToControlUnclamped(_plane, camera, worldPoint, out localPoint))
            {
                return false;
            }
        }
        else if (!isInside)
        {
            ClearHover(sourceEvent, rootVisual);
            return false;
        }

        _lastLocalPoint = localPoint;

        Interactive target;
        if (_isPointerCaptured && _pressedElement is not null)
        {
            target = _pressedElement;
        }
        else
        {
            target = ResolveInteractiveTarget(localPoint);
            UpdateHoverTarget(target, sourceEvent, rootVisual, localPoint);
        }

        var eventRoot = GetEventRootVisual(rootVisual);
        var forwarded = new PointerEventArgs(
            InputElement.PointerMovedEvent,
            target,
            sourceEvent.Pointer,
            eventRoot,
            TranslateControlPointToRoot(target, localPoint, eventRoot),
            sourceEvent.Timestamp,
            sourceEvent.GetCurrentPoint(rootVisual).Properties,
            sourceEvent.KeyModifiers);

        if (!TryRaiseEvent(target, forwarded))
        {
            return false;
        }

        MarkDirty();
        return true;
    }

    public bool HandlePointerReleased(PointerReleasedEventArgs sourceEvent, Camera3D camera, Visual rootVisual, Vector3? worldHit)
    {
        if (!IsActuallyAttached(_sourceControl))
        {
            _isPointerCaptured = false;
            _pressedElement = null;
            _pressedButton = null;
            return false;
        }

        Point localPoint;
        if (worldHit.HasValue && TryMapToLocal(worldHit.Value, camera, out localPoint, true))
        {
        }
        else if (_isPointerCaptured)
        {
            var viewport = GetViewportSize(rootVisual);
            var ray = ProjectionHelper.CreateRay(camera, new System.Numerics.Vector2((float)sourceEvent.GetPosition(rootVisual).X, (float)sourceEvent.GetPosition(rootVisual).Y), viewport);
            if (!ControlPlaneGeometry.TryIntersectInfinitePlane(_plane, camera, ray, out var worldPoint) ||
                !ControlPlaneGeometry.TryMapWorldHitToControlUnclamped(_plane, camera, worldPoint, out localPoint))
            {
                localPoint = _lastLocalPoint;
            }
        }
        else
        {
            return false;
        }

        _lastLocalPoint = localPoint;
        var target = _pressedElement ?? ResolveInteractiveTarget(localPoint);

        var eventRoot = GetEventRootVisual(rootVisual);
        var forwarded = new PointerReleasedEventArgs(
            target,
            sourceEvent.Pointer,
            eventRoot,
            TranslateControlPointToRoot(target, localPoint, eventRoot),
            sourceEvent.Timestamp,
            sourceEvent.GetCurrentPoint(rootVisual).Properties,
            sourceEvent.KeyModifiers,
            sourceEvent.InitialPressMouseButton);

        if (!TryRaiseEvent(target, forwarded))
        {
            _isPointerCaptured = false;
            _pressedElement = null;
            _pressedButton = null;
            return false;
        }

        _isPointerCaptured = false;

        var releasedButton = FindClickableButton(ResolveInteractiveTarget(localPoint));
        if (_pressedButton is not null && releasedButton is not null && ReferenceEquals(_pressedButton, releasedButton))
        {
            TryRaiseButtonClick(_pressedButton);
        }

        _pressedElement = null;
        _pressedButton = null;

        if (worldHit.HasValue && TryMapToLocal(worldHit.Value, camera, out localPoint, true))
        {
            UpdateHoverTarget(ResolveInteractiveTarget(localPoint), sourceEvent, rootVisual, localPoint);
        }
        else
        {
            ClearHover(sourceEvent, rootVisual);
        }

        MarkDirty();
        return true;
    }

    public bool HandlePointerWheel(PointerWheelEventArgs sourceEvent, Vector3 worldHit, Camera3D camera, Visual rootVisual)
    {
        if (!IsInteractionReady)
        {
            return false;
        }

        if (!TryMapToLocal(worldHit, camera, out var localPoint, false))
        {
            return false;
        }

        _lastLocalPoint = localPoint;
        var target = ResolveInteractiveTarget(localPoint);
        UpdateHoverTarget(target, sourceEvent, rootVisual, localPoint);

        var eventRoot = GetEventRootVisual(rootVisual);
        var forwarded = new PointerWheelEventArgs(
            target,
            sourceEvent.Pointer,
            eventRoot,
            TranslateControlPointToRoot(target, localPoint, eventRoot),
            sourceEvent.Timestamp,
            sourceEvent.GetCurrentPoint(rootVisual).Properties,
            sourceEvent.KeyModifiers,
            sourceEvent.Delta);

        if (!TryRaiseEvent(target, forwarded))
        {
            return false;
        }

        MarkDirty();
        return true;
    }

    public bool HandleKeyDown(KeyEventArgs sourceEvent)
    {
        if (_focusedElement is null || !IsActuallyAttached(_sourceControl))
        {
            return false;
        }

        var forwarded = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = _focusedElement,
            Key = sourceEvent.Key,
            KeyModifiers = sourceEvent.KeyModifiers,
            KeySymbol = sourceEvent.KeySymbol,
            PhysicalKey = sourceEvent.PhysicalKey,
            KeyDeviceType = sourceEvent.KeyDeviceType
        };

        if (!TryRaiseEvent(_focusedElement, forwarded))
        {
            return false;
        }

        MarkDirty();
        return forwarded.Handled;
    }

    public bool HandleKeyUp(KeyEventArgs sourceEvent)
    {
        if (_focusedElement is null || !IsActuallyAttached(_sourceControl))
        {
            return false;
        }

        var forwarded = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Source = _focusedElement,
            Key = sourceEvent.Key,
            KeyModifiers = sourceEvent.KeyModifiers,
            KeySymbol = sourceEvent.KeySymbol,
            PhysicalKey = sourceEvent.PhysicalKey,
            KeyDeviceType = sourceEvent.KeyDeviceType
        };

        if (!TryRaiseEvent(_focusedElement, forwarded))
        {
            return false;
        }

        MarkDirty();
        return forwarded.Handled;
    }

    public bool HandleTextInput(TextInputEventArgs sourceEvent)
    {
        if (_focusedElement is null || !IsActuallyAttached(_sourceControl))
        {
            return false;
        }

        var forwarded = new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = _focusedElement,
            Text = sourceEvent.Text
        };

        if (!TryRaiseEvent(_focusedElement, forwarded))
        {
            return false;
        }

        MarkDirty();
        return forwarded.Handled;
    }

    private void UpdateHoverTarget(Interactive target, PointerEventArgs sourceEvent, Visual rootVisual, Point localPoint)
    {
        if (ReferenceEquals(_hoveredElement, target))
        {
            return;
        }

        var eventRoot = GetEventRootVisual(rootVisual);
        if (_hoveredElement is not null)
        {
            var exitPoint = TranslateControlPointToRoot(_hoveredElement, _lastLocalPoint, eventRoot);
            var exitArgs = new PointerEventArgs(
                InputElement.PointerExitedEvent,
                _hoveredElement,
                sourceEvent.Pointer,
                eventRoot,
                exitPoint,
                sourceEvent.Timestamp,
                sourceEvent.GetCurrentPoint(rootVisual).Properties,
                sourceEvent.KeyModifiers);
            TryRaiseEvent(_hoveredElement, exitArgs);
        }

        _hoveredElement = target;
        var enterArgs = new PointerEventArgs(
            InputElement.PointerEnteredEvent,
            target,
            sourceEvent.Pointer,
            eventRoot,
            TranslateControlPointToRoot(target, localPoint, eventRoot),
            sourceEvent.Timestamp,
            sourceEvent.GetCurrentPoint(rootVisual).Properties,
            sourceEvent.KeyModifiers);
        TryRaiseEvent(target, enterArgs);
        ClearTextFocusIfPointerLeftFocusedElement(target);
    }

    private void ClearTextFocusIfPointerLeftFocusedElement(Interactive target)
    {
        if (!IsTextInputElement(_focusedElement) || IsSameOrVisualDescendant(_focusedElement, target))
        {
            return;
        }

        _focusedElement = null;
    }

    private static bool IsSameOrVisualDescendant(Interactive? ancestor, Interactive? target)
    {
        if (ancestor is null || target is null)
        {
            return false;
        }

        if (ReferenceEquals(ancestor, target))
        {
            return true;
        }

        var current = target as Visual;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private static bool IsTextInputElement(Interactive? target)
    {
        return target is TextBox;
    }

    private Interactive ResolveInteractiveTarget(Point localPoint)
    {
        var visual = FindDeepestVisualAtPoint(_sourceControl, localPoint);
        var current = visual;
        while (current is not null)
        {
            if (current is Interactive interactive)
            {
                return interactive;
            }

            current = current.GetVisualParent();
        }

        try
        {
            var inputElement = _sourceControl.InputHitTest(localPoint);
            if (inputElement is Interactive fallbackInteractive)
            {
                return fallbackInteractive;
            }
        }
        catch (InvalidOperationException)
        {
        }

        return _sourceControl;
    }

    private static Visual? FindDeepestVisualAtPoint(Visual root, Point pointInRoot)
    {
        if (!root.IsVisible || root is InputElement rootInput && !rootInput.IsHitTestVisible)
        {
            return null;
        }

        var rootBounds = root.Bounds;
        if (rootBounds.Width > 0d && rootBounds.Height > 0d)
        {
            if (pointInRoot.X < 0d || pointInRoot.Y < 0d || pointInRoot.X > rootBounds.Width || pointInRoot.Y > rootBounds.Height)
            {
                return null;
            }
        }

        var children = root.GetVisualChildren().ToList();
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            var pointInChild = root.TranslatePoint(pointInRoot, child);
            if (!pointInChild.HasValue)
            {
                continue;
            }

            var hit = FindDeepestVisualAtPoint(child, pointInChild.Value);
            if (hit is not null)
            {
                return hit;
            }
        }

        return root;
    }



    private static Button? FindClickableButton(Interactive target)
    {
        var current = target as Visual;
        while (current is not null)
        {
            if (current is Button button)
            {
                return button;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private bool TryRaiseButtonClick(Button button)
    {
        try
        {
            var args = new RoutedEventArgs(Button.ClickEvent, button);
            button.RaiseEvent(args);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private Interactive FindFocusableTarget(Interactive target)
    {
        var current = target as Visual;
        while (current is not null)
        {
            if (current is InputElement inputElement && inputElement.Focusable)
            {
                return inputElement;
            }

            current = current.GetVisualParent();
        }

        return _sourceControl;
    }

    private Point TranslateControlPointToRoot(Interactive target, Point controlPixelPoint, Visual rootVisual)
    {
        if (target is Visual visual)
        {
            try
            {
                var pointInVisual = controlPixelPoint;
                if (!ReferenceEquals(visual, _sourceControl))
                {
                    var translated = _sourceControl.TranslatePoint(controlPixelPoint, visual);
                    if (translated.HasValue)
                    {
                        pointInVisual = translated.Value;
                    }
                }

                var pointInRoot = visual.TranslatePoint(pointInVisual, rootVisual);
                if (pointInRoot.HasValue)
                {
                    return pointInRoot.Value;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return controlPixelPoint;
    }

    private bool TryMapToLocal(Vector3 worldHit, Camera3D camera, out Point localPoint, bool unclamped)
    {
        if (unclamped)
        {
            return ControlPlaneGeometry.TryMapWorldHitToControlUnclamped(_plane, camera, worldHit, out localPoint);
        }

        return ControlPlaneGeometry.TryMapWorldHitToControl(_plane, camera, worldHit, out localPoint);
    }

    private static bool IsActuallyAttached(Visual visual)
    {
        return visual.GetVisualRoot() is not null && TopLevel.GetTopLevel(visual) is not null;
    }

    private static Size DetermineRenderSize(Control control)
    {
        var width = !double.IsNaN(control.Width) && control.Width > 0d ? control.Width : 0d;
        var height = !double.IsNaN(control.Height) && control.Height > 0d ? control.Height : 0d;

        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = control.DesiredSize;

        if (width <= 0d)
        {
            width = desired.Width;
        }

        if (height <= 0d)
        {
            height = desired.Height;
        }

        if (width <= 0d)
        {
            width = 320d;
        }

        if (height <= 0d)
        {
            height = 180d;
        }

        return new Size(width, height);
    }

    private static System.Numerics.Vector2 GetViewportSize(Visual visual)
    {
        var bounds = visual.Bounds;
        return new System.Numerics.Vector2((float)System.Math.Max(bounds.Width, 1d), (float)System.Math.Max(bounds.Height, 1d));
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_isDisposed || _isUpdatingSnapshot)
        {
            return;
        }

        MarkDirty();
    }

    private void OnControlLayoutUpdated(object? sender, EventArgs e)
    {
        if (_isDisposed || _isUpdatingSnapshot)
        {
            return;
        }

        var bounds = _sourceControl.Bounds;
        var widthChanged = System.Math.Abs(bounds.Width - _lastRenderedSize.Width) > 0.5d;
        var heightChanged = System.Math.Abs(bounds.Height - _lastRenderedSize.Height) > 0.5d;
        if (_plane.Snapshot is null || widthChanged || heightChanged)
        {
            MarkDirty();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _sourceControl.PropertyChanged -= OnControlPropertyChanged;
        _sourceControl.LayoutUpdated -= OnControlLayoutUpdated;
        _host.Children.Remove(_sourceControl);
        _bitmap?.Dispose();
    }
}
