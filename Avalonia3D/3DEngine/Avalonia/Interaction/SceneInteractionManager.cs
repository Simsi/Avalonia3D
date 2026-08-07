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
    private ModelHitResult3D? _hoveredModelHit;

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
        _hoveredModelHit = null;
        _objectDragStarted = false;
    }

    public void ClearHover()
    {
        if (HoveredObject is null)
        {
            _hoveredModelHit = null;
            return;
        }

        var oldHovered = HoveredObject;
        HoveredObject = null;
        Scene.World.Mutate(_ => oldHovered.IsHovered = false);
        _hoveredModelHit = null;
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
                DispatchModelPointerEvent(_pressedPick, ModelPointerEventKind.PointerPressed, _lastPosition, SceneMouseButton.Left);
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
            DispatchModelPointerEvent(_pressedPick, ModelPointerEventKind.PointerReleased, position, SceneMouseButton.Left);

            var dragDistance = Vector2.Distance(position, _pressPosition);
            if (!_objectDragStarted &&
                releasePick?.Object == _pressedPick.Object &&
                dragDistance < 6f)
            {
                var clickArgs = CreatePointerArgs(releasePick, position, SceneMouseButton.Left);
                releasePick.Object.RaiseClicked(clickArgs);
                DispatchModelPointerEvent(releasePick, ModelPointerEventKind.Clicked, position, SceneMouseButton.Left);
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
            var yaw = delta.X * 0.35f;
            var pitch = delta.Y * 0.35f;
            Scene.World.Mutate(scene => scene.Camera.Orbit(yaw, pitch));
            _requestRender();
        }
        else if (_middlePressed)
        {
            var panX = delta.X;
            var panY = delta.Y;
            var viewportHeight = (float)System.Math.Max(owner.Bounds.Height, 1.0);
            Scene.World.Mutate(scene => scene.Camera.Pan(panX, panY, viewportHeight));
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

        var hoverPick = UpdateHover(owner, position, SceneMouseButton.Unknown);
        if (HoveredObject is not null && hoverPick is not null)
        {
            HoveredObject.RaisePointerMoved(CreatePointerArgs(hoverPick, position, SceneMouseButton.Unknown));
            DispatchModelPointerEvent(hoverPick, ModelPointerEventKind.PointerMoved, position, SceneMouseButton.Unknown);
        }

        _lastPosition = position;
    }

    public void HandlePointerHover(Control owner, PointerEventArgs e, Vector2 position)
    {
        var hoverPick = UpdateHover(owner, position, SceneMouseButton.Unknown);
        if (HoveredObject is not null && hoverPick is not null)
        {
            HoveredObject.RaisePointerMoved(CreatePointerArgs(hoverPick, position, SceneMouseButton.Unknown));
            DispatchModelPointerEvent(hoverPick, ModelPointerEventKind.PointerMoved, position, SceneMouseButton.Unknown);
        }

        _lastPosition = position;
    }

    public void HandlePointerWheel(Control owner, PointerWheelEventArgs e)
    {
        var distance = (float)e.Delta.Y * 0.5f;
        Scene.World.Mutate(scene => scene.Camera.Dolly(distance));
        _requestRender();
    }

    private void DragSelectedObject(Vector2 delta, Control owner)
    {
        if (SelectedObject is null)
        {
            return;
        }

        var selected = SelectedObject!;
        var viewportHeight = (float)System.Math.Max(owner.Bounds.Height, 1.0);
        var dragDelta = delta;
        Scene.World.Mutate(scene =>
        {
            var distance = System.Math.Max((scene.Camera.Position - selected.Position).Length(), 0.1f);
            var worldUnitsPerPixel =
                (2f * MathF.Tan(scene.Camera.FieldOfViewDegrees * (MathF.PI / 180f) / 2f) * distance) / viewportHeight;
            var translation =
                (scene.Camera.Right * dragDelta.X * worldUnitsPerPixel) +
                (-scene.Camera.SafeUp * dragDelta.Y * worldUnitsPerPixel);
            selected.Position += translation;
        });
    }

    private PickingResult? UpdateHover(Control owner, Vector2 position, SceneMouseButton button)
    {
        var pick = Pick(owner, position);
        var oldHovered = HoveredObject;
        HoveredObject = pick?.Object;

        var hoverTargetChanged = oldHovered != HoveredObject;
        var modelElementChanged = !AreSameModelHover(_hoveredModelHit, pick?.ModelHit);
        if (!hoverTargetChanged && !modelElementChanged)
        {
            return pick;
        }

        if (oldHovered is not null && hoverTargetChanged)
        {
            Scene.World.Mutate(_ => oldHovered.IsHovered = false);
            oldHovered.RaisePointerExited(new ScenePointerEventArgs(oldHovered, position, oldHovered.Position, button, _hoveredModelHit));
        }

        DispatchModelHoverTransition(_hoveredModelHit, pick?.ModelHit, position, button);
        _hoveredModelHit = pick?.ModelHit;

        if (HoveredObject is not null && pick is not null && hoverTargetChanged)
        {
            var newHovered = HoveredObject!;
            Scene.World.Mutate(_ => newHovered.IsHovered = true);
            HoveredObject.RaisePointerEntered(CreatePointerArgs(pick, position, button));
        }

        _requestRender();
        return pick;
    }

    private void UpdateSelection(Object3D? newSelection)
    {
        if (SelectedObject == newSelection)
        {
            return;
        }

        var oldSelection = SelectedObject;
        SelectedObject = newSelection;
        Scene.World.Mutate(_ =>
        {
            if (oldSelection is not null) oldSelection.IsSelected = false;
            if (newSelection is not null) newSelection.IsSelected = true;
        });

        SelectionChanged?.Invoke(this, new SceneSelectionChangedEventArgs(oldSelection, SelectedObject));
        _requestRender();
    }

    private PickingResult? Pick(Control owner, Vector2 position)
    {
        using var sceneAccess = Scene.EnterRenderReadScope();
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
            : new PickingResult(target, pick.WorldPosition, pick.Distance, pick.ModelHit);
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
        => new(pick.Object, position, pick.WorldPosition, button, pick.ModelHit);

    private static bool AreSameModelHover(ModelHitResult3D? oldHit, ModelHitResult3D? newHit)
        => oldHit is null ? newHit is null : oldHit.IsSameInteractiveElement(newHit);

    private static void DispatchModelPointerEvent(PickingResult pick, ModelPointerEventKind eventKind, Vector2 position, SceneMouseButton button)
    {
        var modelHit = pick.ModelHit;
        if (modelHit is null)
        {
            return;
        }

        modelHit.Model.RaiseModelPointerEvent(eventKind, modelHit, position, button);
    }

    private static void DispatchModelHoverTransition(ModelHitResult3D? oldHit, ModelHitResult3D? newHit, Vector2 position, SceneMouseButton button)
    {
        if (oldHit is not null && !oldHit.IsSameInteractiveElement(newHit))
        {
            oldHit.Model.RaiseModelPointerEvent(ModelPointerEventKind.PointerExited, oldHit, position, button);
        }

        if (newHit is not null && !newHit.IsSameInteractiveElement(oldHit))
        {
            newHit.Model.RaiseModelPointerEvent(ModelPointerEventKind.PointerEntered, newHit, position, button);
        }
    }
}
