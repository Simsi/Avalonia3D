using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Debugging;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Instancing;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Resources;

using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.World;
namespace ThreeDEngine.Core.Scene;

public sealed class Scene3D : IDisposable
{
    private readonly List<Object3D> _objects = new List<Object3D>();
    private readonly ReadOnlyCollection<Object3D> _objectsView;
    private readonly Camera3D _camera;
    private readonly List<DirectionalLight3D> _lights = new();
    private readonly ReadOnlyCollection<DirectionalLight3D> _lightsView;
    private readonly List<PointLight3D> _pointLights = new();
    private readonly ReadOnlyCollection<PointLight3D> _pointLightsView;
    private readonly List<SpotLight3D> _spotLights = new();
    private readonly ReadOnlyCollection<SpotLight3D> _spotLightsView;
    private ColorRgba _backgroundColor = ColorRgba.White;
    private ColorRgba _ambientLightColor = ColorRgba.White;
    private float _ambientLightIntensity = 0.28f;
    private int _updateDepth;
    private long _nextUpdateTransactionToken;
    private long[] _updateTransactionTokens = new long[8];
    private int _updateTransactionOwnerThreadId;
    private SceneChangeFlags3D _pendingChangeKinds;
    private SceneChangeKind _pendingPrimaryChangeKind;
    private Object3D? _pendingChangeSource;
    private bool _pendingChangeSourceInitialized;
    private long _pendingFirstChangeSequence;
    private long _pendingLastChangeSequence;
    private long _changeVersion;
    private long _batchContentVersion;
    private long _batchTransformVersion;
    private long _particleContentVersion;
    private long _cameraVersion;
    private long _structureVersion;
    private readonly SceneChangeJournal3D _changeJournal = new();
    private readonly HashSet<Object3D> _batchTransformCopySeen = new(ObjectReferenceComparer3D<Object3D>.Instance);
    private readonly Engine3D _engine;
    private readonly EngineResourceOwner3D _resourceOwner;
    private readonly EngineResourceOwner3D _environmentResourceOwner;
    private readonly List<TextureResource3D> _resourceTextureScratch = new(32);
    private readonly List<TextureResource3D> _environmentTextureScratch = new(7);
    private long _resourceOwnershipBatchContentVersion = long.MinValue;
    private int _resourceOwnershipEnvironmentTextureVersion = int.MinValue;
    private readonly Scene3DOptions _constructionOptions;
    private readonly SceneAccessGate3D _accessGate = new();
    private readonly SceneSimulationScheduler3D _simulationScheduler;
    private readonly bool _ownsEngine;
    private IPhysicsCore? _physicsCore;
    private volatile bool _disposed;
    private volatile bool _executingFixedUpdate;
    private int _fixedUpdateRequested;
    private int _activeUpdateWorkHint;
    private SceneFixedUpdateHandler3D? _fixedUpdate;
    private SceneFixedUpdateHandler3D? _fixedUpdateCompleted;
    private SceneFixedUpdateHandler3D? _internalFixedUpdateCompleted;

    [Obsolete("Use an explicit Engine3DBuilder and Engine3D.CreateScene(). This compatibility constructor requires Avalonia3D.Engine or the complete 3DEngine source-drop.")]
    public Scene3D()
        : this(Engine3D.CreateDefault(), new Scene3DOptions(), ownsEngine: true)
    {
    }

    /// <summary>
    /// Creates an isolated engine scope and a scene configured with <paramref name="options"/>.
    /// Disposing the scene also disposes that private engine scope.
    /// </summary>
    [Obsolete("Use an explicit Engine3DBuilder and Engine3D.CreateScene(options). This compatibility constructor requires Avalonia3D.Engine or the complete 3DEngine source-drop.")]
    public Scene3D(Scene3DOptions options)
        : this(Engine3D.CreateDefault(), options, ownsEngine: true)
    {
    }

    /// <summary>Creates a scene in an injected engine scope. The caller retains engine ownership.</summary>
    public Scene3D(Engine3D engine, Scene3DOptions? options = null)
        : this(engine, options ?? new Scene3DOptions(), ownsEngine: false)
    {
    }

    private Scene3D(Engine3D engine, Scene3DOptions options, bool ownsEngine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _constructionOptions = (options ?? throw new ArgumentNullException(nameof(options))).Clone();
        World = new World3D(this, _constructionOptions.MutationPolicy);
        _resourceOwner = _engine.Resources.CreateOwner("scene-materials");
        _environmentResourceOwner = _engine.Resources.CreateOwner("scene-environment");
        _objectsView = _objects.AsReadOnly();
        _lightsView = _lights.AsReadOnly();
        _pointLightsView = _pointLights.AsReadOnly();
        _spotLightsView = _spotLights.AsReadOnly();
        _ownsEngine = ownsEngine;
        _camera = new Camera3D { OwnerScene = this };
        _camera.Changed += OnCameraChanged;
        Debug = new SceneDebugOptions
        {
            MutationScopeRequested = () => EnterMutationScope(nameof(SceneDebugOptions))
        };
        Debug.Changed += OnDebugOptionsChanged;
        Collisions = new CollisionWorld3D();
        Registry = new SceneObjectRegistry(this);
        Performance = ScenePerformanceOptions.CreateDefault();
        FrameInterpolator = new FrameInterpolator3D(this);
        Commands = new SceneCommandQueue3D(NotifyUpdateActivityChanged);
        _simulationScheduler = new SceneSimulationScheduler3D(this);
        UpdateLoop = new SceneUpdateLoop3D(this);
        AdaptivePerformance = new AdaptivePerformanceController3D();
        Environment = new SceneEnvironment3D
        {
            MutationScopeRequested = () => EnterMutationScope(nameof(SceneEnvironment3D))
        };
        Environment.Changed += OnEnvironmentChanged;
        RenderPipeline = new RenderPipelineSettings3D
        {
            MutationScopeRequested = () => EnterMutationScope(nameof(RenderPipelineSettings3D))
        };
        RenderPipeline.Changed += OnRenderPipelineChanged;
        try
        {
            _constructionOptions.ConfigurePerformance?.Invoke(Performance);
            _constructionOptions.ConfigureUpdateLoop?.Invoke(UpdateLoop);
            _physicsCore = _engine.CreatePhysicsCore(_constructionOptions);
            _engine.AttachScene(this);
        }
        catch
        {
            _physicsCore?.Dispose();
            _physicsCore = null;
            _environmentResourceOwner.Dispose();
            _resourceOwner.Dispose();
            if (_ownsEngine) _engine.Dispose();
            throw;
        }
        EngineLog3D.Information("Scene", $"Scene created in engine scope {_engine.Id}; physics={_physicsCore?.GetType().FullName ?? "disabled"}.");
    }

    public event EventHandler? SceneChanged;
    public event EventHandler<SceneChangedEventArgs>? SceneChangedDetailed;
    internal event EventHandler? UpdateActivityChanged;

    /// <summary>
    /// Runs once per deterministic fixed tick before animation, physics and particles.
    /// Use this for gameplay and kinematic state changes that physics must observe.
    /// </summary>
    public event SceneFixedUpdateHandler3D? FixedUpdate
    {
        add
        {
            ThrowIfDisposed();
            using var access = EnterMutationScope();
            _fixedUpdate += value;
            NotifyUpdateActivityChanged();
            UpdateLoop.NotifyActivityChanged();
        }
        remove
        {
            if (_disposed) return;
            using var access = EnterMutationScope();
            _fixedUpdate -= value;
            RefreshActiveUpdateWorkHintCore();
            UpdateLoop.NotifyActivityChanged();
        }
    }

    /// <summary>Runs after animation, physics and particles for the same fixed tick.</summary>
    public event SceneFixedUpdateHandler3D? FixedUpdateCompleted
    {
        add
        {
            ThrowIfDisposed();
            using var access = EnterMutationScope();
            _fixedUpdateCompleted += value;
            NotifyUpdateActivityChanged();
            UpdateLoop.NotifyActivityChanged();
        }
        remove
        {
            if (_disposed) return;
            using var access = EnterMutationScope();
            _fixedUpdateCompleted -= value;
            RefreshActiveUpdateWorkHintCore();
            UpdateLoop.NotifyActivityChanged();
        }
    }

    internal event SceneFixedUpdateHandler3D? InternalFixedUpdateCompleted
    {
        add
        {
            ThrowIfDisposed();
            using var access = EnterMutationScope();
            _internalFixedUpdateCompleted += value;
        }
        remove
        {
            if (_disposed) return;
            using var access = EnterMutationScope();
            _internalFixedUpdateCompleted -= value;
        }
    }

    public Camera3D Camera => _camera;

    public Engine3D Engine => _engine;
    internal EngineResourceOwner3D ResourceOwner => _resourceOwner;

    internal void SynchronizeResourceOwnership(SceneFrameSnapshot3D snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ThrowIfDisposed();

        if (_resourceOwnershipBatchContentVersion != _batchContentVersion)
        {
            _resourceTextureScratch.Clear();
            var objects = snapshot.AllObjectsInternal;
            for (var i = 0; i < objects.Length; i++)
            {
                var obj = objects[i];
                if (!obj.UseMeshRendering) continue;
                AddMaterialTextureResources(_resourceTextureScratch, obj.Material);
            }

            _resourceOwner.SetTextures(_resourceTextureScratch);
            _resourceOwnershipBatchContentVersion = _batchContentVersion;
        }

        var environmentVersion = Environment.Skybox.EnvironmentTextureVersion;
        if (_resourceOwnershipEnvironmentTextureVersion != environmentVersion)
        {
            _environmentTextureScratch.Clear();
            var skybox = Environment.Skybox;
            if (skybox.EquirectangularTextureInternal is { } equirectangular)
            {
                _environmentTextureScratch.Add(equirectangular);
            }
            var cubemap = skybox.CubemapTexturesInternal;
            for (var i = 0; i < cubemap.Count; i++)
            {
                if (cubemap[i] is { } face) _environmentTextureScratch.Add(face);
            }

            _environmentResourceOwner.SetTextures(_environmentTextureScratch);
            _resourceOwnershipEnvironmentTextureVersion = environmentVersion;
        }
    }

    private static void AddMaterialTextureResources(List<TextureResource3D> output, Material3D material)
    {
        if (material.BaseColorTextureInternal is { } baseColor) output.Add(baseColor);
        if (material.NormalMapTextureInternal is { } normalMap) output.Add(normalMap);
        if (material.MetallicRoughnessTextureInternal is { } metallicRoughness) output.Add(metallicRoughness);
        if (material.EmissiveTextureInternal is { } emissive) output.Add(emissive);
    }

    /// <summary>Authoritative world ownership, commands, jobs, replay and immutable snapshots.</summary>
    public World3D World { get; }

    public SceneDebugOptions Debug { get; }

    public CollisionWorld3D Collisions { get; }

    public SceneObjectRegistry Registry { get; }

    public ScenePerformanceOptions Performance { get; }

    public FrameInterpolator3D FrameInterpolator { get; }

    /// <summary>Thread-safe MPSC queue consumed before deterministic fixed ticks.</summary>
    public SceneCommandQueue3D Commands { get; }

    public SceneUpdateLoop3D UpdateLoop { get; }

    /// <summary>CPU timings for the last completed simulation tick.</summary>
    public SceneSimulationMetrics3D SimulationMetrics
    {
        get
        {
            ThrowIfDisposed();
            using var access = EnterRenderReadScope();
            return _simulationScheduler.LastMetrics;
        }
    }

    public AdaptivePerformanceController3D AdaptivePerformance { get; }

    public SceneEnvironment3D Environment { get; }

    public RenderPipelineSettings3D RenderPipeline { get; }

    public IPhysicsCore? PhysicsCore => _physicsCore;

    public bool PhysicsEnabled => _physicsCore is not null;

    public bool IsDisposed => _disposed;

    /// <summary>
    /// Replaces the physics backend. The scene takes exclusive ownership of the new backend
    /// and disposes the previous backend immediately.
    /// </summary>
    public void ReplacePhysicsCore(IPhysicsCore? physicsCore)
    {
        ThrowIfDisposed();
        using var access = EnterMutationScope();
        if (ReferenceEquals(_physicsCore, physicsCore))
        {
            return;
        }

        var previous = _physicsCore;
        _physicsCore = physicsCore;
        previous?.Dispose();
        RaiseChanged(SceneChangeKind.Physics);
        EngineLog3D.Information("Scene", physicsCore is null
            ? "Physics backend disabled."
            : $"Physics backend replaced with {physicsCore.GetType().FullName}.");
    }

    /// <summary>Enables the configured physics backend or releases it deterministically.</summary>
    public void SetPhysicsEnabled(bool enabled)
    {
        ThrowIfDisposed();
        using var access = EnterMutationScope();
        if (enabled == (_physicsCore is not null)) return;
        if (!enabled)
        {
            ReplacePhysicsCore(null);
            return;
        }

        var options = _constructionOptions.Clone();
        options.PhysicsEnabled = true;
        ReplacePhysicsCore(_engine.CreatePhysicsCore(options));
    }

    public IReadOnlyList<Object3D> Objects => _objectsView;

    public IReadOnlyList<DirectionalLight3D> Lights => _lightsView;

    public IReadOnlyList<PointLight3D> PointLights => _pointLightsView;

    public IReadOnlyList<SpotLight3D> SpotLights => _spotLightsView;

    public long ChangeVersion => _changeVersion;

    /// <summary>
    /// Version for renderer-side retained object/particle batch data. Camera, lighting,
    /// environment and debug-only changes update frame uniforms but do not require a CPU
    /// batch rebuild.
    /// </summary>
    public long BatchContentVersion => _batchContentVersion;

    /// <summary>
    /// Version for renderer-side retained per-object transform/state slots. Unlike
    /// <see cref="BatchContentVersion"/>, this changes for transform/animation updates
    /// without implying that mesh/material batch membership has to be rebuilt.
    /// </summary>
    public long BatchTransformVersion => _batchTransformVersion;

    /// <summary>
    /// Version for retained particle instance payloads. Particle simulation advances are
    /// transform-like Object3D changes, but renderers need to know when particle payloads,
    /// not ordinary mesh batch membership, changed.
    /// </summary>
    public long ParticleContentVersion => _particleContentVersion;

    /// <summary>
    /// Version for camera-only dependencies such as transparent draw order. Ordinary retained
    /// batch content must not depend on it unless the batch contains transparent items.
    /// </summary>
    public long CameraVersion => _cameraVersion;

    public long StructureVersion => _structureVersion;

    /// <summary>Latest monotonic exact-change sequence committed by this scene.</summary>
    public long ChangeSequence => _changeJournal.LatestSequence;

    /// <summary>Oldest sequence still available to retained consumers.</summary>
    public long OldestRetainedChangeSequence => _changeJournal.OldestSequence;

    public int ChangeJournalCapacity => _changeJournal.Capacity;
    public int AllocatedChangeJournalCapacity => _changeJournal.AllocatedCapacity;
    public int RetainedChangeCount => _changeJournal.Count;

    /// <summary>
    /// Copies exact changes after <paramref name="lastObservedSequence"/>. Returns false
    /// when the consumer cursor is invalid or fell behind the bounded journal.
    /// </summary>
    public bool TryCopyChangesSince(long lastObservedSequence, List<SceneChangeRecord3D> output)
    {
        ThrowIfDisposed();
        return _changeJournal.TryCopySince(lastObservedSequence, output);
    }

    public bool TryCopyBatchTransformChangesSince(long lastObservedBatchTransformVersion, List<Object3D> output)
    {
        ThrowIfDisposed();
        if (output is null) throw new ArgumentNullException(nameof(output));
        output.Clear();
        if (lastObservedBatchTransformVersion == _batchTransformVersion)
        {
            return true;
        }

        if (lastObservedBatchTransformVersion < 0 ||
            lastObservedBatchTransformVersion > _batchTransformVersion)
        {
            return false;
        }

        var seen = _batchTransformCopySeen;
        seen.Clear();
        var expectedVersion = _batchTransformVersion;
        try
        {
            // Consume the bounded journal backwards and stop as soon as the retained
            // cursor is reached. The previous forward scan visited up to 16,384 historic
            // records on every animated frame even when only a handful of objects changed,
            // producing periodic desktop frametime spikes as the journal filled/grew.
            // Slot updates do not require chronological order; the reference set below
            // still guarantees that every affected subtree is returned exactly once.
            for (var i = _changeJournal.Count - 1; i >= 0; i--)
            {
                var record = _changeJournal[i];
                if (!RequiresBatchTransformInvalidation(record.Kind) ||
                    record.BatchTransformVersion <= lastObservedBatchTransformVersion)
                {
                    if (RequiresBatchTransformInvalidation(record.Kind) &&
                        record.BatchTransformVersion <= lastObservedBatchTransformVersion)
                    {
                        break;
                    }
                    continue;
                }

                if (record.BatchTransformVersion != expectedVersion ||
                    record.Source is null ||
                    !IsIncrementalBatchTransformChange(record.Kind))
                {
                    output.Clear();
                    return false;
                }

                AddTransformChangedObject(record.Source, output, seen);
                expectedVersion--;
            }

            if (expectedVersion != lastObservedBatchTransformVersion)
            {
                output.Clear();
                return false;
            }

            return true;
        }
        finally
        {
            seen.Clear();
        }
    }


    public ColorRgba BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            ThrowIfDisposed();
            using var access = EnterMutationScope();
            value = Guard3D.Color(value, nameof(BackgroundColor));
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
            ThrowIfDisposed();
            using var access = EnterMutationScope();
            value = Guard3D.Color(value, nameof(AmbientLightColor));
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
            ThrowIfDisposed();
            using var access = EnterMutationScope();
            value = Guard3D.NonNegative(value, nameof(AmbientLightIntensity));
            if (MathF.Abs(_ambientLightIntensity - value) < 0.0001f) return;
            _ambientLightIntensity = value;
            RaiseChanged(SceneChangeKind.Lighting);
        }
    }

    public SceneUpdateTransaction3D BeginUpdate()
    {
        ThrowIfDisposed();
        var access = EnterMutationScope();
        try
        {
            var threadId = global::System.Environment.CurrentManagedThreadId;
            if (_updateDepth == 0) _updateTransactionOwnerThreadId = threadId;
            else if (_updateTransactionOwnerThreadId != threadId)
                throw new InvalidOperationException("Nested scene transactions must remain on their owning thread.");
            if (_updateDepth == _updateTransactionTokens.Length)
            {
                Array.Resize(ref _updateTransactionTokens, checked(_updateTransactionTokens.Length * 2));
            }

            var token = unchecked(++_nextUpdateTransactionToken);
            if (token == 0) token = unchecked(++_nextUpdateTransactionToken);
            _updateTransactionTokens[_updateDepth] = token;
            _updateDepth++;
            return new SceneUpdateTransaction3D(this, token, access);
        }
        catch
        {
            access.Dispose();
            throw;
        }
    }

    public T Add<T>(T obj) where T : Object3D
    {
        ThrowIfDisposed();
        using var access = EnterMutationScope();
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
        RaiseChanged(SceneChangeKind.Structure, obj);
        return obj;
    }

    public IReadOnlyList<Object3D> GetObjectsSnapshot(bool includeCompositeRoots = true)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        if (includeCompositeRoots)
        {
            return Registry.SnapshotAllObjects();
        }

        return EnumerateWithoutCompositeRoots();
    }

    public int CountObjects(bool includeCompositeRoots = true)
    {
        ThrowIfDisposed();
        if (includeCompositeRoots)
        {
            return Registry.AllObjects.Count;
        }

        var count = 0;
        var objects = Registry.AllObjects;
        for (var i = 0; i < objects.Count; i++)
        {
            if (objects[i] is not CompositeObject3D)
            {
                count++;
            }
        }

        return count;
    }


    public ImportedModel3D ImportModel(string path, Action<ModelImportOptions>? configure = null)
    {
        ThrowIfDisposed();
        var options = new ModelImportOptions();
        configure?.Invoke(options);
        if (!_engine.Services.TryGetService<IModelAssetLoader3D>(out var loader) || loader is null)
        {
            throw new InvalidOperationException(
                "No model asset loader is registered for this engine scope. " +
                "Install an asset package such as Avalonia3D.Assets.Gltf and call UseGltfAssets(), " +
                "or register a custom IModelAssetLoader3D with Engine3DBuilder.UseModelAssets().");
        }
        var asset = loader.Load(path, options);
        var model = new ImportedModel3D(asset);
        if (!string.IsNullOrWhiteSpace(options.Name)) model.Name = options.Name!;
        model.Position = options.Position;
        model.RotationDegrees = options.RotationDegrees;
        model.Scale = options.Scale;
        return Add(model);
    }

    public ImportedModel3D ImportModel(ModelAsset3D asset, Action<ModelImportOptions>? configure = null)
    {
        ThrowIfDisposed();
        var options = new ModelImportOptions();
        configure?.Invoke(options);
        var model = new ImportedModel3D(asset);
        if (!string.IsNullOrWhiteSpace(options.Name)) model.Name = options.Name!;
        model.Position = options.Position;
        model.RotationDegrees = options.RotationDegrees;
        model.Scale = options.Scale;
        return Add(model);
    }

    /// <summary>
    /// Streams a model without blocking the caller and commits the new scene object through the
    /// authoritative world command queue. When no persistent simulation owner exists, the method
    /// pumps the queued commit through a transient owner before awaiting it.
    /// </summary>
    public async ValueTask<ImportedModel3D> ImportModelAsync(
        string path,
        Action<ModelImportOptions>? configure = null,
        AssetLoadPriority3D priority = AssetLoadPriority3D.Normal,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var options = new ModelImportOptions();
        configure?.Invoke(options);
        var asset = await _engine.Assets.LoadModelAsync(path, options, priority, cancellationToken).ConfigureAwait(false);
        var model = new ImportedModel3D(asset);
        if (!string.IsNullOrWhiteSpace(options.Name)) model.Name = options.Name!;
        model.Position = options.Position;
        model.RotationDegrees = options.RotationDegrees;
        model.Scale = options.Scale;
        var commit = World.MutateAsync(scene => scene.Add(model), cancellationToken);
        if (!World.HasSimulationOwner) World.PumpCommands();
        await commit.ConfigureAwait(false);
        return model;
    }

    /// <summary>Streams and pins a model asset without attaching it to the scene.</summary>
    public ValueTask<AssetLease3D> AcquireModelAssetAsync(
        string path,
        ModelImportOptions? options = null,
        AssetLoadPriority3D priority = AssetLoadPriority3D.Normal,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _engine.Assets.AcquireModelAsync(path, options, priority, cancellationToken);
    }

    public DirectionalLight3D AddLight(DirectionalLight3D light)
    {
        ThrowIfDisposed();
        using var access = EnterMutationScope();
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
        ThrowIfDisposed();
        using var access = EnterMutationScope();
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
        ThrowIfDisposed();
        using var access = EnterMutationScope();
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
        ThrowIfDisposed();
        using var access = EnterMutationScope();
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
        ThrowIfDisposed();
        using var access = EnterMutationScope();
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
        ThrowIfDisposed();
        using var access = EnterMutationScope();
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

    /// <summary>Feeds host time into the scene's deterministic fixed-update loop.</summary>
    public SceneUpdateResult3D Update(double elapsedSeconds) => World.Advance(elapsedSeconds);

    /// <summary>Feeds host time into the scene's deterministic fixed-update loop.</summary>
    public SceneUpdateResult3D Update(TimeSpan elapsed) => World.Advance(elapsed);

    private void AdvanceParticlesCore(float deltaSeconds)
    {
        var particles = Registry.ParticleSystems;
        for (var i = 0; i < particles.Count; i++) particles[i].Advance(deltaSeconds);
    }

    private void AdvanceAnimationsCore(float deltaSeconds)
    {
        var models = Registry.AnimatedModels;
        for (var i = 0; i < models.Count; i++) models[i].AdvanceAnimation(deltaSeconds);
    }

    public ParticleSystem3D AddParticleSystem(ParticleSystemSettings3D? settings = null, ParticleEmitter3D? emitter = null)
    {
        return Add(new ParticleSystem3D(settings, emitter));
    }

    public InstancedMesh3D AddInstancedMesh(string name, Mesh3D mesh, Material3D? material = null, int initialCapacity = 1024, float chunkCellSize = 24f)
    {
        return Add(new InstancedMesh3D(name, mesh, material, initialCapacity, chunkCellSize));
    }

    /// <summary>
    /// Returns true when built-in simulation or user fixed-update callbacks require a host
    /// to keep feeding the update loop. The result is an atomic activity publication maintained
    /// by the simulation owner; callers never wait for the full scene write lease.
    /// </summary>
    public bool HasActiveUpdateWork()
    {
        ThrowIfDisposed();
        return Volatile.Read(ref _fixedUpdateRequested) != 0 ||
               _fixedUpdate is not null ||
               _fixedUpdateCompleted is not null ||
               Performance.EnableWebGlClientGpuTransformAnimation ||
               World.Jobs.Count > 0 ||
               Volatile.Read(ref _activeUpdateWorkHint) != 0;
    }

    private bool CalculateActiveUpdateWorkCore()
    {
        if (World.Jobs.Count > 0) return true;

        var models = Registry.AnimatedModels;
        if (UpdateLoop.AdvanceAnimations)
        {
            for (var i = 0; i < models.Count; i++)
            {
                if (models[i].Animation.IsPlaying) return true;
            }
        }

        var particles = Registry.ParticleSystems;
        if (UpdateLoop.AdvanceParticles)
        {
            for (var i = 0; i < particles.Count; i++)
            {
                var system = particles[i];
                if (system.AliveCount > 0 ||
                    system.IsPlaying && system.Settings.Looping && system.Settings.EmissionRatePerSecond > 0f)
                {
                    return true;
                }
            }
        }

        if (UpdateLoop.AdvancePhysics && _physicsCore is not null)
        {
            var bodies = Registry.DynamicBodies;
            for (var i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].Rigidbody is { } body &&
                    (body.HasPendingDynamics ||
                     !body.IsSleeping &&
                     (body.Velocity.LengthSquared() > 0.000001f ||
                      body.AngularVelocity.LengthSquared() > 0.000001f ||
                      body.UseGravity)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RefreshActiveUpdateWorkHintCore()
        => Volatile.Write(ref _activeUpdateWorkHint, CalculateActiveUpdateWorkCore() ? 1 : 0);

    private void MarkActiveUpdateWorkDirty()
        => Volatile.Write(ref _activeUpdateWorkHint, 1);

    internal void RefreshActiveUpdateWorkHint()
    {
        if (_disposed) return;
        RefreshActiveUpdateWorkHintCore();
    }

    /// <summary>
    /// Requests at least one automatic fixed tick. Custom subsystems can use this to wake an
    /// otherwise idle scene without keeping a permanent <see cref="FixedUpdate"/> subscriber.
    /// </summary>
    public void RequestFixedUpdate()
    {
        ThrowIfDisposed();
        NotifyUpdateActivityChanged();
    }

    internal void ExecuteFixedUpdate(
        in SceneFixedUpdateContext3D context,
        bool advanceAnimations,
        bool advancePhysics,
        bool advanceParticles)
    {
        ThrowIfDisposed();
        if (_executingFixedUpdate)
        {
            throw new InvalidOperationException("Scene fixed updates cannot be nested.");
        }
        if (_updateDepth != 0)
        {
            throw new InvalidOperationException("A fixed update cannot begin while an explicit scene transaction is active.");
        }

        _executingFixedUpdate = true;
        _updateTransactionOwnerThreadId = global::System.Environment.CurrentManagedThreadId;
        Volatile.Write(ref _fixedUpdateRequested, 0);
        FrameInterpolator.BeginTick(this);
        if (_updateDepth == _updateTransactionTokens.Length)
        {
            Array.Resize(ref _updateTransactionTokens, checked(_updateTransactionTokens.Length * 2));
        }
        _updateTransactionTokens[_updateDepth] = 0;
        _updateDepth++;
        try
        {
            _simulationScheduler.Execute(in context, advanceAnimations, advancePhysics, advanceParticles);
        }
        finally
        {
            try
            {
                FrameInterpolator.EndTick(this);
            }
            finally
            {
                try
                {
                    EndUpdateCore();
                }
                finally
                {
                    RefreshActiveUpdateWorkHintCore();
                    _executingFixedUpdate = false;
                }
            }
        }
    }

    internal void BeginScheduledFixedUpdate(in SceneFixedUpdateContext3D context)
        => _fixedUpdate?.Invoke(this, in context);

    internal void AdvanceScheduledAnimations(float deltaSeconds)
        => AdvanceAnimationsCore(deltaSeconds);

    internal void AdvanceScheduledPhysics(float deltaSeconds)
        => _physicsCore?.Step(this, deltaSeconds);

    internal void AdvanceScheduledParticles(float deltaSeconds)
        => AdvanceParticlesCore(deltaSeconds);

    internal void CompleteScheduledFixedUpdate(in SceneFixedUpdateContext3D context)
    {
        _internalFixedUpdateCompleted?.Invoke(this, in context);
        _fixedUpdateCompleted?.Invoke(this, in context);
    }

    internal int PumpQueuedCommands()
    {
        ThrowIfDisposed();
        using var access = EnterMutationScope();
        return _simulationScheduler.PumpCommands();
    }

    internal SceneAccessLease3D EnterRenderReadScope()
    {
        ThrowIfDisposed();
        return _accessGate.EnterRead();
    }

    internal SceneAccessLease3D EnterMutationScope([global::System.Runtime.CompilerServices.CallerMemberName] string? operation = null)
    {
        ThrowIfDisposed();
        World.ValidateMutationAccess(operation);
        var lease = _accessGate.EnterWrite();
        if (_updateDepth == 0 || _updateTransactionOwnerThreadId == global::System.Environment.CurrentManagedThreadId)
        {
            return lease;
        }

        lease.Dispose();
        throw new InvalidOperationException(
            "Scene mutation attempted from a different thread while an explicit/fixed update transaction is active. " +
            "Enqueue the mutation through Scene3D.Commands instead.");
    }

    internal void NotifyUpdateActivityChanged()
    {
        if (_disposed) return;
        MarkActiveUpdateWorkDirty();
        Volatile.Write(ref _fixedUpdateRequested, 1);
        UpdateActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(Object3D obj)
    {
        ThrowIfDisposed();
        using var access = EnterMutationScope();
        if (obj is null) return false;
        var removed = _objects.Remove(obj);
        if (!removed)
        {
            return false;
        }

        obj.Changed -= OnObjectChanged;
        DetachOwnerSceneRecursive(obj);
        RaiseChanged(SceneChangeKind.Structure, obj);
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();
        using var access = EnterMutationScope();
        ClearCore(notify: true);
    }

    private void ClearCore(bool notify)
    {
        foreach (var obj in _objects)
        {
            obj.Changed -= OnObjectChanged;
            DetachOwnerSceneRecursive(obj);
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
        Registry.ClearIncremental();
        if (notify)
        {
            RaiseChanged(SceneChangeKind.Structure);
        }
    }

    public void Invalidate()
    {
        ThrowIfDisposed();
        using var access = EnterMutationScope();
        RaiseChanged(SceneChangeKind.Unknown);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        if (_executingFixedUpdate)
        {
            throw new InvalidOperationException("Scene3D cannot be disposed from inside its fixed update.");
        }
        if (Volatile.Read(ref _updateDepth) != 0)
        {
            throw new InvalidOperationException("Scene3D cannot be disposed while an explicit update transaction is active. Dispose the transaction first.");
        }

        _disposed = true;
        List<Exception>? failures = null;
        void Release(string component, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(new InvalidOperationException($"Scene3D failed to release {component}.", exception));
                EngineLog3D.Error("Scene", $"Scene disposal failed while releasing {component}; remaining owned resources will still be released.", exception);
            }
        }

        Release("the update loop", UpdateLoop.DisposeFromScene);
        Commands.Dispose();
        World.DisposeFromScene();
        _camera.Changed -= OnCameraChanged;
        Debug.Changed -= OnDebugOptionsChanged;
        Environment.Changed -= OnEnvironmentChanged;
        RenderPipeline.Changed -= OnRenderPipelineChanged;
        Release("scene objects", () => ClearCore(notify: false));

        var physicsCore = _physicsCore;
        _physicsCore = null;
        if (physicsCore is not null) Release("the physics world", physicsCore.Dispose);

        ResetPendingChange();
        _updateDepth = 0;
        _updateTransactionOwnerThreadId = 0;
        Array.Clear(_updateTransactionTokens);
        _fixedUpdate = null;
        _fixedUpdateCompleted = null;
        _internalFixedUpdateCompleted = null;
        Volatile.Write(ref _fixedUpdateRequested, 0);
        Volatile.Write(ref _activeUpdateWorkHint, 0);
        UpdateActivityChanged = null;
        _changeJournal.Clear();
        _batchTransformCopySeen.Clear();
        SceneChanged = null;
        SceneChangedDetailed = null;
        _resourceTextureScratch.Clear();
        _environmentTextureScratch.Clear();
        Release("the environment resource owner", _environmentResourceOwner.Dispose);
        Release("the material resource owner", _resourceOwner.Dispose);
        Release("the engine attachment", () => _engine.DetachScene(this));
        Release("the scene access gate", _accessGate.Dispose);

        if (_ownsEngine)
        {
            Release("the private engine scope", _engine.Dispose);
        }

        if (failures is { Count: > 0 })
        {
            EngineLog3D.Warning("Scene", $"Scene disposal completed with {failures.Count} failure(s); all independent ownership scopes were still released.");
            throw new AggregateException("Scene3D disposal completed with one or more resource-release failures.", failures);
        }

        EngineLog3D.Information("Scene", $"Scene disposed; physics and immutable resources released before detaching from engine scope {_engine.Id}.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }


    internal void AttachOwnerSceneRecursive(Object3D obj)
    {
        ValidateSubtreeOwner(obj);
        SetSubtreeOwner(obj, this);
    }

    internal static void DetachOwnerSceneRecursive(Object3D obj)
    {
        SetSubtreeOwner(obj, null);
    }

    private void ValidateSubtreeOwner(Object3D obj)
    {
        if (obj.OwnerScene is not null && !ReferenceEquals(obj.OwnerScene, this))
        {
            throw new InvalidOperationException($"Object '{obj.Name}' ({obj.Id}) is already attached to another scene.");
        }

        if (obj is not CompositeObject3D composite) return;
        var children = composite.Children;
        for (var i = 0; i < children.Count; i++) ValidateSubtreeOwner(children[i]);
    }

    private static void SetSubtreeOwner(Object3D obj, Scene3D? owner)
    {
        obj.OwnerScene = owner;
        if (obj is not CompositeObject3D composite) return;
        var children = composite.Children;
        for (var i = 0; i < children.Count; i++) SetSubtreeOwner(children[i], owner);
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
        var source = e is Object3DChangedEventArgs precise ? precise.Source : sender as Object3D;
        var kind = e is Object3DChangedEventArgs objectChanged ? objectChanged.Kind : SceneChangeKind.Unknown;
        if (sender is CompositeObject3D && kind == SceneChangeKind.Unknown)
        {
            kind = SceneChangeKind.Structure;
        }

        RaiseChanged(kind, source);
    }

    private void OnCameraChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Camera);
    private void OnLightChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Lighting);
    private void OnDebugOptionsChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Debug);
    private void OnEnvironmentChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Lighting);
    private void OnRenderPipelineChanged(object? sender, EventArgs e) => RaiseChanged(SceneChangeKind.Debug);

    private void RaiseChanged(SceneChangeKind kind, Object3D? source = null)
    {
        if (_disposed)
        {
            return;
        }
        if (kind == SceneChangeKind.Transform && source is { Collider: not null, Rigidbody.IsKinematic: true })
        {
            NotifyUpdateActivityChanged();
        }

        World.MarkSnapshotDirty();
        _changeVersion++;
        if (RequiresBatchContentInvalidation(kind))
        {
            _batchContentVersion++;
        }
        if (RequiresBatchTransformInvalidation(kind))
        {
            _batchTransformVersion++;
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
            Registry.ApplyChange(kind, source);
        }
        if (!_executingFixedUpdate && RequiresActiveWorkRefresh(kind, source))
        {
            RefreshActiveUpdateWorkHintCore();
        }
        if (kind == SceneChangeKind.Structure)
        {
            _structureVersion++;
        }

        var sequence = _changeJournal.LatestSequence + 1;
        var record = new SceneChangeRecord3D(
            sequence,
            kind,
            source,
            Registry.PublishedVersion,
            _batchContentVersion,
            _batchTransformVersion,
            _particleContentVersion,
            _cameraVersion,
            _structureVersion);
        _changeJournal.Append(in record);

        if (_updateDepth > 0)
        {
            AccumulatePendingChange(kind, source, sequence);
            return;
        }

        var args = new SceneChangedEventArgs(kind, source, kind.ToFlag(), sequence, sequence);
        SceneChangedDetailed?.Invoke(this, args);
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AccumulatePendingChange(SceneChangeKind kind, Object3D? source, long sequence)
    {
        if (_pendingChangeKinds == SceneChangeFlags3D.None)
        {
            _pendingPrimaryChangeKind = kind;
            _pendingChangeSource = source;
            _pendingChangeSourceInitialized = true;
            _pendingFirstChangeSequence = sequence;
        }
        else
        {
            if (_pendingPrimaryChangeKind != kind) _pendingPrimaryChangeKind = SceneChangeKind.Unknown;
            if (_pendingChangeSourceInitialized && !ReferenceEquals(_pendingChangeSource, source))
            {
                _pendingChangeSource = null;
            }
        }

        _pendingChangeKinds |= kind.ToFlag();
        _pendingLastChangeSequence = sequence;
    }

    private void AddTransformChangedObject(Object3D obj, List<Object3D> output, HashSet<Object3D> seen)
    {
        Registry.AddSubtreeObjects(obj, output, seen);
    }

    private static bool IsIncrementalBatchTransformChange(SceneChangeKind kind)
    {
        return kind == SceneChangeKind.Transform || kind == SceneChangeKind.Physics || kind == SceneChangeKind.AnimationPose;
    }

    private static bool RequiresActiveWorkRefresh(SceneChangeKind kind, Object3D? source)
    {
        return kind == SceneChangeKind.Structure ||
               kind == SceneChangeKind.Physics ||
               source is ImportedModel3D or ParticleSystem3D ||
               source?.Rigidbody is not null;
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
               kind == SceneChangeKind.AnimationPose ||
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

    private void ResetPendingChange()
    {
        _pendingChangeKinds = SceneChangeFlags3D.None;
        _pendingPrimaryChangeKind = SceneChangeKind.Unknown;
        _pendingChangeSource = null;
        _pendingChangeSourceInitialized = false;
        _pendingFirstChangeSequence = 0;
        _pendingLastChangeSequence = 0;
    }

    internal void EndUpdateTransaction(long token, SceneAccessLease3D transactionLease)
    {
        ThrowIfDisposed();
        var structurallyClosed = false;
        try
        {
            using var access = EnterMutationScope();
            if (_updateDepth <= 0)
            {
                throw new InvalidOperationException("The scene update transaction is no longer active.");
            }

            var index = _updateDepth - 1;
            if (_updateTransactionTokens[index] != token || token == 0)
            {
                throw new InvalidOperationException(
                    "Scene update transactions must be disposed exactly once and in reverse nesting order.");
            }

            _updateTransactionTokens[index] = 0;
            structurallyClosed = true;
            EndUpdateCore();
        }
        finally
        {
            // A valid token owns one recursive write lease for its complete lifetime. Release it
            // even when a coalesced change subscriber throws after the transaction was closed,
            // but never release a copied/out-of-order token's lease.
            if (structurallyClosed) transactionLease.Dispose();
        }
    }

    private void EndUpdateCore()
    {
        if (_updateDepth <= 0)
        {
            throw new InvalidOperationException("Scene update depth underflow.");
        }

        var index = _updateDepth - 1;
        if (_updateTransactionTokens[index] != 0)
        {
            throw new InvalidOperationException("A nested scene transaction was not disposed before its owning update scope completed.");
        }
        _updateDepth--;
        if (_updateDepth == 0) _updateTransactionOwnerThreadId = 0;
        if (_updateDepth != 0 || _pendingChangeKinds == SceneChangeFlags3D.None)
        {
            return;
        }

        var pending = new SceneChangedEventArgs(
            _pendingPrimaryChangeKind,
            _pendingChangeSource,
            _pendingChangeKinds,
            _pendingFirstChangeSequence,
            _pendingLastChangeSequence);
        ResetPendingChange();
        SceneChangedDetailed?.Invoke(this, pending);
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }
}
