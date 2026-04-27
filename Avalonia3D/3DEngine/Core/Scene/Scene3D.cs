using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Debugging;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Scene;

public sealed class Scene3D
{
    private readonly List<Object3D> _objects = new List<Object3D>();
    private readonly Camera3D _camera;
    private readonly List<DirectionalLight3D> _lights = new();
    private readonly List<PointLight3D> _pointLights = new();
    private ColorRgba _backgroundColor = ColorRgba.White;
    private int _updateDepth;
    private SceneChangedEventArgs? _pendingChange;
    private int _changeVersion;
    private int _structureVersion;

    public Scene3D()
    {
        _camera = new Camera3D();
        _camera.Changed += OnCameraChanged;
        Debug = new SceneDebugOptions();
        Debug.Changed += OnDebugOptionsChanged;
        Collisions = new CollisionWorld3D();
        Registry = new SceneObjectRegistry(this);
        Performance = ScenePerformanceOptions.CreateDefault();
    }

    public event EventHandler? SceneChanged;
    public event EventHandler<SceneChangedEventArgs>? SceneChangedDetailed;

    public Camera3D Camera => _camera;

    public SceneDebugOptions Debug { get; }

    public CollisionWorld3D Collisions { get; }

    public SceneObjectRegistry Registry { get; }

    public ScenePerformanceOptions Performance { get; }

    public IPhysicsCore? PhysicsCore { get; set; } = new BasicPhysicsCore();

    public IReadOnlyList<Object3D> Objects => _objects;

    public IReadOnlyList<DirectionalLight3D> Lights => _lights;

    public IReadOnlyList<PointLight3D> PointLights => _pointLights;

    public int ChangeVersion => _changeVersion;

    public int StructureVersion => _structureVersion;

    public ColorRgba BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (_backgroundColor.Equals(value))
            {
                return;
            }

            _backgroundColor = value;
            RaiseChanged(SceneChangeKind.Material);
        }
    }

    public IDisposable BeginUpdate()
    {
        _updateDepth++;
        return new SceneUpdateScope(this);
    }

    public T Add<T>(T obj) where T : Object3D
    {
        if (obj.Parent is not null)
        {
            throw new InvalidOperationException("Only root 3D objects can be added to a scene. Add child objects through CompositeObject3D.");
        }

        _objects.Add(obj);
        obj.Changed += OnObjectChanged;
        RaiseChanged(SceneChangeKind.Structure, obj);
        return obj;
    }

    public IReadOnlyList<Object3D> GetObjectsSnapshot(bool includeCompositeRoots = true)
    {
        if (includeCompositeRoots)
        {
            return Registry.AllObjects;
        }

        var result = new List<Object3D>();
        foreach (var obj in Registry.AllObjects)
        {
            if (obj is not CompositeObject3D)
            {
                result.Add(obj);
            }
        }

        return result;
    }

    public IEnumerable<Object3D> EnumerateObjects(bool includeCompositeRoots = true)
    {
        if (includeCompositeRoots)
        {
            return Registry.AllObjects;
        }

        return EnumerateWithoutCompositeRoots();
    }

    public int CountObjects(bool includeCompositeRoots = true)
    {
        if (includeCompositeRoots)
        {
            return Registry.AllObjects.Count;
        }

        var count = 0;
        foreach (var obj in Registry.AllObjects)
        {
            if (obj is not CompositeObject3D)
            {
                count++;
            }
        }

        return count;
    }

    public DirectionalLight3D AddLight(DirectionalLight3D light)
    {
        _lights.Add(light);
        light.Changed += OnLightChanged;
        RaiseChanged(SceneChangeKind.Lighting);
        return light;
    }

    public PointLight3D AddLight(PointLight3D light)
    {
        _pointLights.Add(light);
        light.Changed += OnLightChanged;
        RaiseChanged(SceneChangeKind.Lighting);
        return light;
    }

    public bool RemoveLight(DirectionalLight3D light)
    {
        var removed = _lights.Remove(light);
        if (!removed)
        {
            return false;
        }

        light.Changed -= OnLightChanged;
        RaiseChanged(SceneChangeKind.Lighting);
        return true;
    }

    public bool RemoveLight(PointLight3D light)
    {
        var removed = _pointLights.Remove(light);
        if (!removed)
        {
            return false;
        }

        light.Changed -= OnLightChanged;
        RaiseChanged(SceneChangeKind.Lighting);
        return true;
    }

    public void StepPhysics(float deltaSeconds)
    {
        PhysicsCore?.Step(this, deltaSeconds);
    }

    public bool Remove(Object3D obj)
    {
        var removed = _objects.Remove(obj);
        if (!removed)
        {
            return false;
        }

        obj.Changed -= OnObjectChanged;
        RaiseChanged(SceneChangeKind.Structure, obj);
        return true;
    }

    public void Clear()
    {
        foreach (var obj in _objects)
        {
            obj.Changed -= OnObjectChanged;
        }

        _objects.Clear();
        foreach (var light in _lights)
        {
            light.Changed -= OnLightChanged;
        }
        _lights.Clear();
        foreach (var light in _pointLights)
        {
            light.Changed -= OnLightChanged;
        }
        _pointLights.Clear();
        RaiseChanged(SceneChangeKind.Structure);
    }

    public void Invalidate() => RaiseChanged(SceneChangeKind.Unknown);

    private IEnumerable<Object3D> EnumerateWithoutCompositeRoots()
    {
        foreach (var obj in Registry.AllObjects)
        {
            if (obj is not CompositeObject3D)
            {
                yield return obj;
            }
        }
    }

    private void OnObjectChanged(object? sender, EventArgs e)
    {
        var source = sender as Object3D;
        var kind = source is CompositeObject3D ? SceneChangeKind.Structure : SceneChangeKind.Unknown;
        RaiseChanged(kind, source);
    }

    private void OnCameraChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Camera);
    private void OnLightChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Lighting);
    private void OnDebugOptionsChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Debug);

    private void RaiseChanged(SceneChangeKind kind, Object3D? source = null)
    {
        _changeVersion++;
        Registry.Invalidate();
        if (kind == SceneChangeKind.Structure)
        {
            _structureVersion++;
        }

        var args = new SceneChangedEventArgs(kind, source);
        if (_updateDepth > 0)
        {
            _pendingChange = Merge(_pendingChange, args);
            return;
        }

        SceneChangedDetailed?.Invoke(this, args);
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SceneChangedEventArgs Merge(SceneChangedEventArgs? current, SceneChangedEventArgs next)
    {
        if (current is null)
        {
            return next;
        }

        if (current.Kind == SceneChangeKind.Structure || next.Kind == SceneChangeKind.Structure)
        {
            return new SceneChangedEventArgs(SceneChangeKind.Structure, next.Source ?? current.Source);
        }

        return new SceneChangedEventArgs(next.Kind == SceneChangeKind.Unknown ? current.Kind : next.Kind, next.Source ?? current.Source);
    }

    private void EndUpdate()
    {
        if (_updateDepth <= 0)
        {
            return;
        }

        _updateDepth--;
        if (_updateDepth != 0 || _pendingChange is null)
        {
            return;
        }

        var pending = _pendingChange;
        _pendingChange = null;
        SceneChangedDetailed?.Invoke(this, pending);
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SceneUpdateScope : IDisposable
    {
        private Scene3D? _scene;

        public SceneUpdateScope(Scene3D scene)
        {
            _scene = scene;
        }

        public void Dispose()
        {
            var scene = _scene;
            if (scene is null)
            {
                return;
            }

            _scene = null;
            scene.EndUpdate();
        }
    }
}
