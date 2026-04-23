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
    private bool _leftPressed;
    private bool _middlePressed;
    private bool _rightPressed;
    private bool _objectDragStarted;
    private Vector2 _lastPosition;
    private Vector2 _pressPosition;
    private PickingResult? _pressedPick;

    public SceneInteractionManager(Scene3D scene, Action requestRender)
    {
        Scene = scene;
        _requestRender = requestRender;
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
        owner.Focus();

        var point = e.GetPosition(owner);
        _lastPosition = new Vector2((float)point.X, (float)point.Y);
        _pressPosition = _lastPosition;

        if (e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed)
        {
            _leftPressed = true;
            _pressedPick = Pick(owner, _lastPosition);
            UpdateSelection(_pressedPick?.Object);

            if (_pressedPick is not null)
            {
                var args = CreatePointerArgs(_pressedPick, SceneMouseButton.Left);
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
        var position = new Vector2((float)point.X, (float)point.Y);

        if (_leftPressed && _pressedPick is not null)
        {
            var releasePick = Pick(owner, position);

            var pointerArgs = CreatePointerArgs(_pressedPick, SceneMouseButton.Left);
            _pressedPick.Object.RaisePointerReleased(pointerArgs);

            var dragDistance = Vector2.Distance(position, _pressPosition);
            if (!_objectDragStarted &&
                releasePick?.Object == _pressedPick.Object &&
                dragDistance < 6f)
            {
                var clickArgs = CreatePointerArgs(releasePick, SceneMouseButton.Left);
                releasePick.Object.RaiseClicked(clickArgs);
                ObjectClicked?.Invoke(this, clickArgs);
            }
        }

        _leftPressed = false;
        _middlePressed = false;
        _rightPressed = false;
        _objectDragStarted = false;
        _pressedPick = null;

        e.Pointer.Capture(null);
        UpdateHover(owner, position, SceneMouseButton.Unknown);
        _requestRender();
    }

    public void HandlePointerMoved(Control owner, PointerEventArgs e)
    {
        var point = e.GetPosition(owner);
        var position = new Vector2((float)point.X, (float)point.Y);
        var delta = position - _lastPosition;

        if (_rightPressed)
        {
            Scene.Camera.Orbit(delta.X * 0.35f, delta.Y * 0.35f);
            _requestRender();
        }
        else if (_middlePressed)
        {
            Scene.Camera.Pan(delta.X, delta.Y, (float)Math.Max(owner.Bounds.Height, 1.0));
            _requestRender();
        }
        else if (_leftPressed && SelectedObject is not null && _pressedPick?.Object == SelectedObject)
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
                HoveredObject.RaisePointerMoved(CreatePointerArgs(hoverPick, SceneMouseButton.Unknown));
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

        var viewportHeight = (float)Math.Max(owner.Bounds.Height, 1.0);
        var distance = Math.Max((Scene.Camera.Position - SelectedObject.Position).Length(), 0.1f);
        var worldUnitsPerPixel =
            (2f * MathF.Tan(Scene.Camera.FieldOfViewDegrees * (MathF.PI / 180f) / 2f) * distance) / viewportHeight;

        var translation =
            (Scene.Camera.Right * delta.X * worldUnitsPerPixel) +
            (-Scene.Camera.Up * delta.Y * worldUnitsPerPixel);

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
            HoveredObject.RaisePointerEntered(CreatePointerArgs(pick, button));
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
        var viewport = new Vector2((float)Math.Max(owner.Bounds.Width, 1.0), (float)Math.Max(owner.Bounds.Height, 1.0));
        return Raycaster.Pick(Scene, position, viewport);
    }

    private ScenePointerEventArgs CreatePointerArgs(PickingResult pick, SceneMouseButton button)
        => new(pick.Object, _lastPosition, pick.WorldPosition, button);
}
