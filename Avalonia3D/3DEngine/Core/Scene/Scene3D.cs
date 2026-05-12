using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Debugging;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Instancing;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering.Pipeline;

namespace ThreeDEngine.Core.Scene;

public sealed class Scene3D
{
    private readonly List<Object3D> _objects = new List<Object3D>();
    private readonly Camera3D _camera;
    private readonly List<DirectionalLight3D> _lights = new();
    private readonly List<PointLight3D> _pointLights = new();
    private readonly List<SpotLight3D> _spotLights = new();
    private ColorRgba _backgroundColor = ColorRgba.White;
    private ColorRgba _ambientLightColor = ColorRgba.White;
    private float _ambientLightIntensity = 0.28f;
    private int _updateDepth;
    private SceneChangedEventArgs? _pendingChange;
    private int _changeVersion;
    private int _batchContentVersion;
    private int _batchTransformVersion;
    private int _particleContentVersion;
    private int _cameraVersion;
    private int _structureVersion;
    private const int MaxBatchTransformChangeLogEntries = 8192;
    private readonly List<BatchTransformChangeRecord> _batchTransformChangeLog = new();
    private int _batchTransformChangeLogFirstVersion = 1;

    public Scene3D()
    {
        _camera = new Camera3D();
        _camera.Changed += OnCameraChanged;
        Debug = new SceneDebugOptions();
        Debug.Changed += OnDebugOptionsChanged;
        Collisions = new CollisionWorld3D();
        Registry = new SceneObjectRegistry(this);
        Performance = ScenePerformanceOptions.CreateDefault();
        FrameInterpolator = new FrameInterpolator3D();
        AdaptivePerformance = new AdaptivePerformanceController3D();
        Environment = new SceneEnvironment3D();
        Environment.Changed += OnEnvironmentChanged;
        RenderPipeline = new RenderPipelineSettings3D();
        RenderPipeline.Changed += OnRenderPipelineChanged;
        PhysicsCore = PhysicsCoreFactory.CreateDefault();
        Avalonia3DSelfTestRunner.RunAtStartupIfEnabled();
    }

    public event EventHandler? SceneChanged;
    public event EventHandler<SceneChangedEventArgs>? SceneChangedDetailed;

    public Camera3D Camera => _camera;

    public SceneDebugOptions Debug { get; }

    public CollisionWorld3D Collisions { get; }

    public SceneObjectRegistry Registry { get; }

    public ScenePerformanceOptions Performance { get; }

    public FrameInterpolator3D FrameInterpolator { get; }

    public AdaptivePerformanceController3D AdaptivePerformance { get; }

    public SceneEnvironment3D Environment { get; }

    public RenderPipelineSettings3D RenderPipeline { get; }

    public IPhysicsCore? PhysicsCore { get; set; }

    public IReadOnlyList<Object3D> Objects => _objects;

    public IReadOnlyList<DirectionalLight3D> Lights => _lights;

    public IReadOnlyList<PointLight3D> PointLights => _pointLights;

    public IReadOnlyList<SpotLight3D> SpotLights => _spotLights;

    public int ChangeVersion => _changeVersion;

    /// <summary>
    /// Version for renderer-side retained object/particle batch data. Camera, lighting,
    /// environment and debug-only changes update frame uniforms but do not require a CPU
    /// batch rebuild.
    /// </summary>
    public int BatchContentVersion => _batchContentVersion;

    /// <summary>
    /// Version for renderer-side retained per-object transform/state slots. Unlike
    /// <see cref="BatchContentVersion"/>, this changes for transform/animation updates
    /// without implying that mesh/material batch membership has to be rebuilt.
    /// </summary>
    public int BatchTransformVersion => _batchTransformVersion;

    /// <summary>
    /// Version for retained particle instance payloads. Particle simulation advances are
    /// transform-like Object3D changes, but renderers need to know when particle payloads,
    /// not ordinary mesh batch membership, changed.
    /// </summary>
    public int ParticleContentVersion => _particleContentVersion;

    /// <summary>
    /// Version for camera-only dependencies such as transparent draw order. Ordinary retained
    /// batch content must not depend on it unless the batch contains transparent items.
    /// </summary>
    public int CameraVersion => _cameraVersion;

    public int StructureVersion => _structureVersion;

    public bool TryCopyBatchTransformChangesSince(int lastObservedBatchTransformVersion, List<Object3D> output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        output.Clear();
        if (lastObservedBatchTransformVersion == _batchTransformVersion)
        {
            return true;
        }

        if (lastObservedBatchTransformVersion < 0 || lastObservedBatchTransformVersion < _batchTransformChangeLogFirstVersion - 1)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _batchTransformChangeLog.Count; i++)
        {
            var record = _batchTransformChangeLog[i];
            if (record.Version <= lastObservedBatchTransformVersion)
            {
                continue;
            }

            if (record.Source is null)
            {
                output.Clear();
                return false;
            }

            AddTransformChangedObject(record.Source, output, seen);
        }

        return true;
    }


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

    public ColorRgba AmbientLightColor
    {
        get => _ambientLightColor;
        set
        {
            if (_ambientLightColor.Equals(value)) return;
            _ambientLightColor = value;
            RaiseChanged(SceneChangeKind.Lighting);
        }
    }

    public float AmbientLightIntensity
    {
        get => _ambientLightIntensity;
        set
        {
            var clamped = MathF.Max(0f, value);
            if (MathF.Abs(_ambientLightIntensity - clamped) < 0.0001f) return;
            _ambientLightIntensity = clamped;
            RaiseChanged(SceneChangeKind.Lighting);
        }
    }

    public IDisposable BeginUpdate()
    {
        _updateDepth++;
        return new SceneUpdateScope(this);
    }

    public T Add<T>(T obj) where T : Object3D
    {
        if (obj is null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (obj.Parent is not null)
        {
            throw new InvalidOperationException("Only root 3D objects can be added to a scene. Add child objects through CompositeObject3D.");
        }

        if (_objects.Contains(obj))
        {
            throw new InvalidOperationException($"Object '{obj.Name}' ({obj.Id}) is already added to this scene.");
        }

        if (obj.OwnerScene is not null && !ReferenceEquals(obj.OwnerScene, this))
        {
            throw new InvalidOperationException($"Object '{obj.Name}' ({obj.Id}) is already attached to another scene.");
        }

        AttachOwnerSceneRecursive(obj);
        _objects.Add(obj);
        obj.Changed += OnObjectChanged;
        if (obj is HighScaleInstanceLayer3D highScaleLayer) highScaleLayer.StateChanged += OnHighScaleStateChanged;
        RaiseChanged(SceneChangeKind.Structure, obj);
        return obj;
    }

    public IReadOnlyList<Object3D> GetObjectsSnapshot(bool includeCompositeRoots = true)
    {
        if (includeCompositeRoots)
        {
            return Registry.SnapshotAllObjects();
        }

        var result = new List<Object3D>();
        foreach (var obj in Registry.SnapshotAllObjects())
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
            return Registry.SnapshotAllObjects();
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
        foreach (var obj in Registry.SnapshotAllObjects())
        {
            if (obj is not CompositeObject3D)
            {
                count++;
            }
        }

        return count;
    }


    public ImportedModel3D ImportModel(string path, Action<ModelImportOptions>? configure = null)
    {
        var options = new ModelImportOptions();
        configure?.Invoke(options);
        var asset = ModelAssetCache3D.Shared.Load(path, options);
        var model = new ImportedModel3D(asset);
        if (!string.IsNullOrWhiteSpace(options.Name)) model.Name = options.Name!;
        model.Position = options.Position;
        model.RotationDegrees = options.RotationDegrees;
        model.Scale = options.Scale;
        return Add(model);
    }

    public ImportedModel3D ImportModel(ModelAsset3D asset, Action<ModelImportOptions>? configure = null)
    {
        var options = new ModelImportOptions();
        configure?.Invoke(options);
        var model = new ImportedModel3D(asset);
        if (!string.IsNullOrWhiteSpace(options.Name)) model.Name = options.Name!;
        model.Position = options.Position;
        model.RotationDegrees = options.RotationDegrees;
        model.Scale = options.Scale;
        return Add(model);
    }

    public DirectionalLight3D AddLight(DirectionalLight3D light)
    {
        if (light is null) throw new ArgumentNullException(nameof(light));
        if (_lights.Contains(light)) throw new InvalidOperationException("Directional light is already added to this scene.");
        if (light.OwnerScene is not null && !ReferenceEquals(light.OwnerScene, this)) throw new InvalidOperationException("Directional light is already attached to another scene.");
        light.OwnerScene = this;
        _lights.Add(light);
        light.Changed += OnLightChanged;
        RaiseChanged(SceneChangeKind.Lighting);
        return light;
    }

    public PointLight3D AddLight(PointLight3D light)
    {
        if (light is null) throw new ArgumentNullException(nameof(light));
        if (_pointLights.Contains(light)) throw new InvalidOperationException("Point light is already added to this scene.");
        if (light.OwnerScene is not null && !ReferenceEquals(light.OwnerScene, this)) throw new InvalidOperationException("Point light is already attached to another scene.");
        light.OwnerScene = this;
        _pointLights.Add(light);
        light.Changed += OnLightChanged;
        RaiseChanged(SceneChangeKind.Lighting);
        return light;
    }

    public SpotLight3D AddLight(SpotLight3D light)
    {
        if (light is null) throw new ArgumentNullException(nameof(light));
        if (_spotLights.Contains(light)) throw new InvalidOperationException("Spot light is already added to this scene.");
        if (light.OwnerScene is not null && !ReferenceEquals(light.OwnerScene, this)) throw new InvalidOperationException("Spot light is already attached to another scene.");
        light.OwnerScene = this;
        _spotLights.Add(light);
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
        light.OwnerScene = null;
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
        light.OwnerScene = null;
        RaiseChanged(SceneChangeKind.Lighting);
        return true;
    }

    public bool RemoveLight(SpotLight3D light)
    {
        var removed = _spotLights.Remove(light);
        if (!removed)
        {
            return false;
        }

        light.Changed -= OnLightChanged;
        light.OwnerScene = null;
        RaiseChanged(SceneChangeKind.Lighting);
        return true;
    }

    public void StepPhysics(float deltaSeconds)
    {
        PhysicsCore?.Step(this, deltaSeconds);
    }

    public void AdvanceParticles(float deltaSeconds)
    {
        // Particle systems can rebuild their dynamic mesh while advancing. Use a
        // frame snapshot so a SceneChanged/registry rebuild cannot invalidate an
        // active List<T> enumerator or shift indexes mid-frame.
        var objects = Registry.SnapshotAllObjects();
        for (var i = 0; i < objects.Length; i++)
        {
            if (objects[i] is ParticleSystem3D particles)
            {
                particles.Advance(deltaSeconds);
            }
        }
    }

    public void AdvanceAnimations(float deltaSeconds)
    {
        // Imported/skinned models can mark geometry dirty and raise SceneChanged
        // from inside AdvanceAnimation. Keep this pass isolated from registry
        // mutations caused by those callbacks.
        var objects = Registry.SnapshotAllObjects();
        for (var i = 0; i < objects.Length; i++)
        {
            if (objects[i] is ImportedModel3D model)
            {
                model.AdvanceAnimation(deltaSeconds);
            }
        }
    }

    public ParticleSystem3D AddParticleSystem(ParticleSystemSettings3D? settings = null, ParticleEmitter3D? emitter = null)
    {
        return Add(new ParticleSystem3D(settings, emitter));
    }

    public InstancedMesh3D AddInstancedMesh(string name, Mesh3D mesh, Material3D? material = null, int initialCapacity = 1024, float chunkCellSize = 24f)
    {
        return Add(new InstancedMesh3D(name, mesh, material, initialCapacity, chunkCellSize));
    }

    public void BeginSimulationTick() => FrameInterpolator.BeginTick(this);

    public void EndSimulationTick() => FrameInterpolator.EndTick(this);

    public bool Remove(Object3D obj)
    {
        if (obj is null) return false;
        var removed = _objects.Remove(obj);
        if (!removed)
        {
            return false;
        }

        obj.Changed -= OnObjectChanged;
        DetachOwnerSceneRecursive(obj);
        if (obj is HighScaleInstanceLayer3D highScaleLayer) highScaleLayer.StateChanged -= OnHighScaleStateChanged;
        RaiseChanged(SceneChangeKind.Structure, obj);
        return true;
    }

    public void Clear()
    {
        foreach (var obj in _objects)
        {
            obj.Changed -= OnObjectChanged;
            DetachOwnerSceneRecursive(obj);
            if (obj is HighScaleInstanceLayer3D highScaleLayer) highScaleLayer.StateChanged -= OnHighScaleStateChanged;
        }

        _objects.Clear();
        foreach (var light in _lights)
        {
            light.Changed -= OnLightChanged;
            light.OwnerScene = null;
        }
        _lights.Clear();
        foreach (var light in _pointLights)
        {
            light.Changed -= OnLightChanged;
            light.OwnerScene = null;
        }
        _pointLights.Clear();
        foreach (var light in _spotLights)
        {
            light.Changed -= OnLightChanged;
            light.OwnerScene = null;
        }
        _spotLights.Clear();
        RaiseChanged(SceneChangeKind.Structure);
    }

    public void Invalidate()
    {
        Registry.Invalidate();
        RaiseChanged(SceneChangeKind.Unknown);
    }


    private void AttachOwnerSceneRecursive(Object3D obj)
    {
        obj.OwnerScene = this;
        if (obj is not CompositeObject3D composite) return;
        foreach (var child in composite.EnumerateDescendants())
        {
            child.OwnerScene = this;
        }
    }

    private static void DetachOwnerSceneRecursive(Object3D obj)
    {
        obj.OwnerScene = null;
        if (obj is not CompositeObject3D composite) return;
        foreach (var child in composite.EnumerateDescendants())
        {
            child.OwnerScene = null;
        }
    }

    private IEnumerable<Object3D> EnumerateWithoutCompositeRoots()
    {
        foreach (var obj in Registry.SnapshotAllObjects())
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
        var kind = e is Object3DChangedEventArgs objectChanged ? objectChanged.Kind : SceneChangeKind.Unknown;
        if (source is CompositeObject3D && kind == SceneChangeKind.Unknown)
        {
            kind = SceneChangeKind.Structure;
        }

        RaiseChanged(kind, source);
    }

    private void OnHighScaleStateChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.HighScaleState, sender as Object3D);
    private void OnCameraChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Camera);
    private void OnLightChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Lighting);
    private void OnDebugOptionsChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Debug);
    private void OnEnvironmentChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Lighting);
    private void OnRenderPipelineChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Debug);

    private void RaiseChanged(SceneChangeKind kind, Object3D? source = null)
    {
        _changeVersion++;
        if (RequiresBatchContentInvalidation(kind))
        {
            _batchContentVersion++;
        }
        if (RequiresBatchTransformInvalidation(kind))
        {
            _batchTransformVersion++;
            AppendBatchTransformChange(_batchTransformVersion, IsIncrementalBatchTransformChange(kind) ? source : null);
        }
        if (RequiresParticleContentInvalidation(kind, source))
        {
            _particleContentVersion++;
        }
        if (kind == SceneChangeKind.Camera)
        {
            _cameraVersion++;
        }
        if (RequiresRegistryInvalidation(kind))
        {
            Registry.Invalidate(kind, source);
        }
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

    private void AppendBatchTransformChange(int version, Object3D? source)
    {
        if (_batchTransformChangeLog.Count == 0)
        {
            _batchTransformChangeLogFirstVersion = version;
        }

        _batchTransformChangeLog.Add(new BatchTransformChangeRecord(version, source));
        if (_batchTransformChangeLog.Count <= MaxBatchTransformChangeLogEntries)
        {
            return;
        }

        var removeCount = _batchTransformChangeLog.Count - MaxBatchTransformChangeLogEntries;
        _batchTransformChangeLog.RemoveRange(0, removeCount);
        _batchTransformChangeLogFirstVersion = _batchTransformChangeLog[0].Version;
    }

    private static void AddTransformChangedObject(Object3D obj, List<Object3D> output, HashSet<string> seen)
    {
        if (seen.Add(obj.Id))
        {
            output.Add(obj);
        }

        if (obj is not CompositeObject3D composite)
        {
            return;
        }

        foreach (var child in composite.EnumerateDescendants())
        {
            if (seen.Add(child.Id))
            {
                output.Add(child);
            }
        }
    }

    private static bool IsIncrementalBatchTransformChange(SceneChangeKind kind)
    {
        return kind == SceneChangeKind.Transform || kind == SceneChangeKind.Physics;
    }

    private static bool RequiresBatchContentInvalidation(SceneChangeKind kind)
    {
        return kind == SceneChangeKind.Structure ||
               kind == SceneChangeKind.Material ||
               kind == SceneChangeKind.Geometry ||
               kind == SceneChangeKind.Visibility ||
               kind == SceneChangeKind.Unknown;
    }

    private static bool RequiresBatchTransformInvalidation(SceneChangeKind kind)
    {
        return kind == SceneChangeKind.Transform ||
               kind == SceneChangeKind.Physics ||
               kind == SceneChangeKind.Unknown;
    }

    private static bool RequiresParticleContentInvalidation(SceneChangeKind kind, Object3D? source)
    {
        return kind == SceneChangeKind.Structure ||
               kind == SceneChangeKind.Unknown ||
               source is ParticleSystem3D &&
               (kind == SceneChangeKind.Transform ||
                kind == SceneChangeKind.Geometry ||
                kind == SceneChangeKind.Material ||
                kind == SceneChangeKind.Visibility);
    }

    private static bool RequiresRegistryInvalidation(SceneChangeKind kind)
    {
        return kind == SceneChangeKind.Structure ||
               kind == SceneChangeKind.Transform ||
               kind == SceneChangeKind.Geometry ||
               kind == SceneChangeKind.Visibility ||
               kind == SceneChangeKind.Physics ||
               kind == SceneChangeKind.Control;
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

    private readonly struct BatchTransformChangeRecord
    {
        public BatchTransformChangeRecord(int version, Object3D? source)
        {
            Version = version;
            Source = source;
        }

        public int Version { get; }
        public Object3D? Source { get; }
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
