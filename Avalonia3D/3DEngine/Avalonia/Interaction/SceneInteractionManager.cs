using System;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Interaction;

public sealed class SceneInteractionManager
{
    private readonly Action _requestRender;
    private readonly Func<Vector2>? _getViewportSize;
    private bool _leftPressed;
    private bool _middlePressed;
    private bool _rightPressed;
    private bool _objectDragStarted;
    private Vector2 _lastPosition;
    private Vector2 _pressPosition;
    private PickingResult? _pressedPick;

    public SceneInteractionManager(Scene3D scene, Action requestRender, Func<Vector2>? getViewportSize = null)
    {
        Scene = scene;
        _requestRender = requestRender;
        _getViewportSize = getViewportSize;
    }

    public event EventHandler<ScenePointerEventArgs>? ObjectClicked;
    public event EventHandler<SceneSelectionChangedEventArgs>? SelectionChanged;

    public Scene3D Scene { get; private set; }
    public Object3D? HoveredObject { get; private set; }
    public Object3D? SelectedObject { get; private set; }

    public void SetScene(Scene3D scene)
    {
        Scene = scene;
        HoveredObject = null;
        SelectedObject = null;
        _pressedPick = null;
        _objectDragStarted = false;
    }

    public void ClearHover()
    {
        if (HoveredObject is null)
        {
            return;
        }

        HoveredObject.IsHovered = false;
        HoveredObject = null;
        _requestRender();
    }

    public void CancelManipulation()
    {
        _leftPressed = false;
        _middlePressed = false;
        _rightPressed = false;
        _objectDragStarted = false;
        _pressedPick = null;
    }

    public void HandlePointerPressed(Control owner, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(owner);
        HandlePointerPressed(owner, e, new Vector2((float)point.X, (float)point.Y));
    }

    public void HandlePointerPressed(Control owner, PointerPressedEventArgs e, Vector2 position)
    {
        owner.Focus();

        _lastPosition = position;
        _pressPosition = _lastPosition;

        if (e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed)
        {
            _leftPressed = true;
            _pressedPick = Pick(owner, _lastPosition);
            UpdateSelection(_pressedPick?.Object);

            if (_pressedPick is not null)
            {
                var args = CreatePointerArgs(_pressedPick, _lastPosition, SceneMouseButton.Left);
                _pressedPick.Object.RaisePointerPressed(args);
            }
        }

        if (e.GetCurrentPoint(owner).Properties.IsMiddleButtonPressed)
        {
            _middlePressed = true;
        }

        if (e.GetCurrentPoint(owner).Properties.IsRightButtonPressed)
        {
            _rightPressed = true;
        }

        e.Pointer.Capture(owner);
        _requestRender();
    }

    public void HandlePointerReleased(Control owner, PointerReleasedEventArgs e)
    {
        var point = e.GetPosition(owner);
        HandlePointerReleased(owner, e, new Vector2((float)point.X, (float)point.Y));
    }

    public void HandlePointerReleased(Control owner, PointerReleasedEventArgs e, Vector2 position)
    {
        if (_leftPressed && _pressedPick is not null)
        {
            var releasePick = Pick(owner, position);

            var pointerArgs = CreatePointerArgs(_pressedPick, position, SceneMouseButton.Left);
            _pressedPick.Object.RaisePointerReleased(pointerArgs);

            var dragDistance = Vector2.Distance(position, _pressPosition);
            if (!_objectDragStarted &&
                releasePick?.Object == _pressedPick.Object &&
                dragDistance < 6f)
            {
                var clickArgs = CreatePointerArgs(releasePick, position, SceneMouseButton.Left);
                releasePick.Object.RaiseClicked(clickArgs);
                ObjectClicked?.Invoke(this, clickArgs);
            }
        }

        _leftPressed = false;
        _middlePressed = false;
        _rightPressed = false;
        _objectDragStarted = false;
        _pressedPick = null;
        _lastPosition = position;

        e.Pointer.Capture(null);
        UpdateHover(owner, position, SceneMouseButton.Unknown);
        _requestRender();
    }

    public void HandlePointerMoved(Control owner, PointerEventArgs e)
    {
        var point = e.GetPosition(owner);
        HandlePointerMoved(owner, e, new Vector2((float)point.X, (float)point.Y));
    }

    public void HandlePointerMoved(Control owner, PointerEventArgs e, Vector2 position)
    {
        var delta = position - _lastPosition;

        if (_rightPressed)
        {
            Scene.Camera.Orbit(delta.X * 0.35f, delta.Y * 0.35f);
            _requestRender();
        }
        else if (_middlePressed)
        {
            Scene.Camera.Pan(delta.X, delta.Y, (float)System.Math.Max(owner.Bounds.Height, 1.0));
            _requestRender();
        }
        else if (_leftPressed && SelectedObject is { IsManipulationEnabled: true } && _pressedPick?.Object == SelectedObject)
        {
            if (!_objectDragStarted && Vector2.Distance(position, _pressPosition) > 4f)
            {
                _objectDragStarted = true;
            }

            if (_objectDragStarted)
            {
                DragSelectedObject(delta, owner);
                _requestRender();
            }
        }

        UpdateHover(owner, position, SceneMouseButton.Unknown);

        if (HoveredObject is not null)
        {
            var hoverPick = Pick(owner, position);
            if (hoverPick is not null)
            {
                HoveredObject.RaisePointerMoved(CreatePointerArgs(hoverPick, position, SceneMouseButton.Unknown));
            }
        }

        _lastPosition = position;
    }

    public void HandlePointerHover(Control owner, PointerEventArgs e, Vector2 position)
    {
        UpdateHover(owner, position, SceneMouseButton.Unknown);

        if (HoveredObject is not null)
        {
            var hoverPick = Pick(owner, position);
            if (hoverPick is not null)
            {
                HoveredObject.RaisePointerMoved(CreatePointerArgs(hoverPick, position, SceneMouseButton.Unknown));
            }
        }

        _lastPosition = position;
    }

    public void HandlePointerWheel(Control owner, PointerWheelEventArgs e)
    {
        Scene.Camera.Dolly((float)e.Delta.Y * 0.5f);
        _requestRender();
    }

    private void DragSelectedObject(Vector2 delta, Control owner)
    {
        if (SelectedObject is null)
        {
            return;
        }

        var viewportHeight = (float)System.Math.Max(owner.Bounds.Height, 1.0);
        var distance = System.Math.Max((Scene.Camera.Position - SelectedObject.Position).Length(), 0.1f);
        var worldUnitsPerPixel =
            (2f * MathF.Tan(Scene.Camera.FieldOfViewDegrees * (MathF.PI / 180f) / 2f) * distance) / viewportHeight;

        var translation =
            (Scene.Camera.Right * delta.X * worldUnitsPerPixel) +
            (-Scene.Camera.SafeUp * delta.Y * worldUnitsPerPixel);

        SelectedObject.Position += translation;
    }

    private void UpdateHover(Control owner, Vector2 position, SceneMouseButton button)
    {
        var pick = Pick(owner, position);
        var oldHovered = HoveredObject;
        HoveredObject = pick?.Object;

        if (oldHovered == HoveredObject)
        {
            return;
        }

        if (oldHovered is not null)
        {
            oldHovered.IsHovered = false;
            oldHovered.RaisePointerExited(new ScenePointerEventArgs(oldHovered, position, oldHovered.Position, button));
        }

        if (HoveredObject is not null && pick is not null)
        {
            HoveredObject.IsHovered = true;
            HoveredObject.RaisePointerEntered(CreatePointerArgs(pick, position, button));
        }

        _requestRender();
    }

    private void UpdateSelection(Object3D? newSelection)
    {
        if (SelectedObject == newSelection)
        {
            return;
        }

        var oldSelection = SelectedObject;
        if (oldSelection is not null)
        {
            oldSelection.IsSelected = false;
        }

        SelectedObject = newSelection;

        if (SelectedObject is not null)
        {
            SelectedObject.IsSelected = true;
        }

        SelectionChanged?.Invoke(this, new SceneSelectionChangedEventArgs(oldSelection, SelectedObject));
        _requestRender();
    }

    private PickingResult? Pick(Control owner, Vector2 position)
    {
        var viewport = _getViewportSize?.Invoke() ??
                       new Vector2((float)System.Math.Max(owner.Bounds.Width, 1.0), (float)System.Math.Max(owner.Bounds.Height, 1.0));
        var pick = Raycaster.Pick(Scene, position, viewport);
        return NormalizePickTarget(pick);
    }

    private static PickingResult? NormalizePickTarget(PickingResult? pick)
    {
        if (pick is null)
        {
            return null;
        }

        var target = ResolveInteractionTarget(pick.Object);
        return ReferenceEquals(target, pick.Object)
            ? pick
            : new PickingResult(target, pick.WorldPosition, pick.Distance);
    }

    private static Object3D ResolveInteractionTarget(Object3D obj)
    {
        Object3D target = obj;
        var current = obj;
        while (current.Parent is not null)
        {
            if (current.Parent is CompositeObject3D composite && composite.IsManipulationEnabled)
            {
                target = composite;
            }

            current = current.Parent;
        }

        return target;
    }

    private static ScenePointerEventArgs CreatePointerArgs(PickingResult pick, Vector2 position, SceneMouseButton button)
        => new(pick.Object, position, pick.WorldPosition, button);
}
