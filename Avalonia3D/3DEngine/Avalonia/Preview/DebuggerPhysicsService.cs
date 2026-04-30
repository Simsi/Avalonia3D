using System;
using System.Numerics;
using Avalonia.Threading;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Preview;

internal sealed class DebuggerPhysicsService
{
    private readonly Func<Scene3D> _sceneProvider;
    private readonly Func<bool> _isPhysicsEnabled;
    private readonly Func<bool> _isAttached;
    private readonly Func<Object3D?> _selectionProvider;
    private readonly Action<Object3D?> _selectionRefreshed;
    private readonly DispatcherTimer _timer;
    private DateTime _lastTickUtc;

    public DebuggerPhysicsService(
        Func<Scene3D> sceneProvider,
        Func<bool> isPhysicsEnabled,
        Func<bool> isAttached,
        Func<Object3D?> selectionProvider,
        Action<Object3D?> selectionRefreshed)
    {
        _sceneProvider = sceneProvider ?? throw new ArgumentNullException(nameof(sceneProvider));
        _isPhysicsEnabled = isPhysicsEnabled ?? throw new ArgumentNullException(nameof(isPhysicsEnabled));
        _isAttached = isAttached ?? throw new ArgumentNullException(nameof(isAttached));
        _selectionProvider = selectionProvider ?? throw new ArgumentNullException(nameof(selectionProvider));
        _selectionRefreshed = selectionRefreshed ?? throw new ArgumentNullException(nameof(selectionRefreshed));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16d) };
        _timer.Tick += OnTick;
    }

    public void EnsureObjectReady(Object3D obj)
    {
        var scene = _sceneProvider();
        scene.PhysicsCore ??= new BasicPhysicsCore();
        scene.PhysicsSettings.Mode = PhysicsSimulationMode.FixedStep;
        EnsurePhysicsCollider(obj);
        scene.Invalidate();
        UpdatePumpState();
    }

    public void StepImmediate(Object3D? selectionToRefresh)
    {
        var scene = _sceneProvider();
        if (scene.PhysicsCore is null)
        {
            UpdatePumpState();
            return;
        }

        scene.AdvancePhysics(1f / 60f);
        scene.Invalidate();
        RefreshSelection(selectionToRefresh);
        UpdatePumpState();
    }

    public void UpdatePumpState()
    {
        var scene = _sceneProvider();
        var shouldRun =
            _isPhysicsEnabled() &&
            scene.PhysicsCore is not null &&
            _isAttached() &&
            HasDynamicPhysicsBodies(scene);

        if (shouldRun)
        {
            if (!_timer.IsEnabled)
            {
                _lastTickUtc = default;
                _timer.Start();
            }
        }
        else if (_timer.IsEnabled)
        {
            _timer.Stop();
            _lastTickUtc = default;
        }
    }

    public void Stop()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }

        _lastTickUtc = default;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var scene = _sceneProvider();
        if (!_isPhysicsEnabled() || scene.PhysicsCore is null || !_isAttached())
        {
            UpdatePumpState();
            return;
        }

        var now = DateTime.UtcNow;
        var dt = _lastTickUtc == default ? 1f / 60f : (float)(now - _lastTickUtc).TotalSeconds;
        _lastTickUtc = now;
        dt = Math.Clamp(dt, 0.001f, 1f / 15f);

        scene.AdvancePhysics(dt);
        scene.Invalidate();
        RefreshSelection(_selectionProvider());
        UpdatePumpState();
    }

    private void RefreshSelection(Object3D? selection)
    {
        _selectionRefreshed(selection);
    }

    private static bool HasDynamicPhysicsBodies(Scene3D scene)
    {
        foreach (var obj in scene.Registry.DynamicBodies)
        {
            var body = obj.Rigidbody;
            if (body is null || body.IsKinematic)
            {
                continue;
            }

            if (body.UseGravity || body.Velocity.LengthSquared() > 0.000001f)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsurePhysicsCollider(Object3D obj)
    {
        if (obj.Collider is not null)
        {
            return;
        }

        var bounds = obj.GetWorldBounds();
        var size = bounds.IsValid ? bounds.Size : Vector3.One;
        var scale = obj.Scale;
        size = new Vector3(
            MathF.Max(0.01f, size.X / MathF.Max(0.001f, MathF.Abs(scale.X))),
            MathF.Max(0.01f, size.Y / MathF.Max(0.001f, MathF.Abs(scale.Y))),
            MathF.Max(0.01f, size.Z / MathF.Max(0.001f, MathF.Abs(scale.Z))));
        obj.Collider = new BoxCollider3D { Size = size };
    }
}
