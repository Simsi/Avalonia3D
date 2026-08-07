using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Particles;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Versioned scene-membership publication.
///
/// Public array accessors always return defensive copies. Render hot paths use bounded
/// <see cref="ReadOnlySpan{T}"/> views. A normal snapshot owns exact immutable arrays;
/// an internal reusable snapshot grows geometrically and is repopulated only while the
/// scene render-read lease is held.
/// </summary>
public sealed class SceneFrameSnapshot3D
{
    private Object3D[] _allObjects;
    private Object3D[] _renderables;
    private Object3D[] _pickables;
    private Object3D[] _colliders;
    private Object3D[] _dynamicBodies;
    private Object3D[] _staticColliders;
    private HighScaleInstanceLayer3D[] _highScaleLayers;
    private ImportedModel3D[] _animatedModels;
    private ParticleSystem3D[] _particleSystems;

    private int _allObjectCount;
    private int _renderableCount;
    private int _pickableCount;
    private int _colliderCount;
    private int _dynamicBodyCount;
    private int _staticColliderCount;
    private int _highScaleLayerCount;
    private int _animatedModelCount;
    private int _particleSystemCount;
    private object? _reusableOwner;

    internal SceneFrameSnapshot3D(
        long registryVersion,
        Object3D[] allObjects,
        Object3D[] renderables,
        Object3D[] pickables,
        Object3D[] colliders,
        Object3D[] dynamicBodies,
        Object3D[] staticColliders,
        HighScaleInstanceLayer3D[] highScaleLayers,
        ImportedModel3D[] animatedModels,
        ParticleSystem3D[] particleSystems)
    {
        RegistryVersion = registryVersion;
        _allObjects = allObjects ?? throw new ArgumentNullException(nameof(allObjects));
        _renderables = renderables ?? throw new ArgumentNullException(nameof(renderables));
        _pickables = pickables ?? throw new ArgumentNullException(nameof(pickables));
        _colliders = colliders ?? throw new ArgumentNullException(nameof(colliders));
        _dynamicBodies = dynamicBodies ?? throw new ArgumentNullException(nameof(dynamicBodies));
        _staticColliders = staticColliders ?? throw new ArgumentNullException(nameof(staticColliders));
        _highScaleLayers = highScaleLayers ?? throw new ArgumentNullException(nameof(highScaleLayers));
        _animatedModels = animatedModels ?? throw new ArgumentNullException(nameof(animatedModels));
        _particleSystems = particleSystems ?? throw new ArgumentNullException(nameof(particleSystems));
        _allObjectCount = _allObjects.Length;
        _renderableCount = _renderables.Length;
        _pickableCount = _pickables.Length;
        _colliderCount = _colliders.Length;
        _dynamicBodyCount = _dynamicBodies.Length;
        _staticColliderCount = _staticColliders.Length;
        _highScaleLayerCount = _highScaleLayers.Length;
        _animatedModelCount = _animatedModels.Length;
        _particleSystemCount = _particleSystems.Length;
    }

    private SceneFrameSnapshot3D()
    {
        _allObjects = Array.Empty<Object3D>();
        _renderables = Array.Empty<Object3D>();
        _pickables = Array.Empty<Object3D>();
        _colliders = Array.Empty<Object3D>();
        _dynamicBodies = Array.Empty<Object3D>();
        _staticColliders = Array.Empty<Object3D>();
        _highScaleLayers = Array.Empty<HighScaleInstanceLayer3D>();
        _animatedModels = Array.Empty<ImportedModel3D>();
        _particleSystems = Array.Empty<ParticleSystem3D>();
    }

    public long RegistryVersion { get; private set; }

    public Object3D[] AllObjects => CopyPrefix(_allObjects, _allObjectCount);
    public Object3D[] Renderables => CopyPrefix(_renderables, _renderableCount);
    public Object3D[] Pickables => CopyPrefix(_pickables, _pickableCount);
    public Object3D[] Colliders => CopyPrefix(_colliders, _colliderCount);
    public Object3D[] DynamicBodies => CopyPrefix(_dynamicBodies, _dynamicBodyCount);
    public Object3D[] StaticColliders => CopyPrefix(_staticColliders, _staticColliderCount);
    public HighScaleInstanceLayer3D[] HighScaleLayers => CopyPrefix(_highScaleLayers, _highScaleLayerCount);
    public ImportedModel3D[] AnimatedModels => CopyPrefix(_animatedModels, _animatedModelCount);
    public ParticleSystem3D[] ParticleSystems => CopyPrefix(_particleSystems, _particleSystemCount);

    internal ReadOnlySpan<Object3D> AllObjectsInternal => _allObjects.AsSpan(0, _allObjectCount);
    internal ReadOnlySpan<Object3D> RenderablesInternal => _renderables.AsSpan(0, _renderableCount);
    internal ReadOnlySpan<Object3D> PickablesInternal => _pickables.AsSpan(0, _pickableCount);
    internal ReadOnlySpan<Object3D> CollidersInternal => _colliders.AsSpan(0, _colliderCount);
    internal ReadOnlySpan<Object3D> DynamicBodiesInternal => _dynamicBodies.AsSpan(0, _dynamicBodyCount);
    internal ReadOnlySpan<Object3D> StaticCollidersInternal => _staticColliders.AsSpan(0, _staticColliderCount);
    internal ReadOnlySpan<HighScaleInstanceLayer3D> HighScaleLayersInternal => _highScaleLayers.AsSpan(0, _highScaleLayerCount);
    internal ReadOnlySpan<ImportedModel3D> AnimatedModelsInternal => _animatedModels.AsSpan(0, _animatedModelCount);
    internal ReadOnlySpan<ParticleSystem3D> ParticleSystemsInternal => _particleSystems.AsSpan(0, _particleSystemCount);

    internal static SceneFrameSnapshot3D CreateReusable() => new();

    internal bool MatchesReusableOwner(object owner, long registryVersion)
        => ReferenceEquals(_reusableOwner, owner) && RegistryVersion == registryVersion;

    internal void ResetReusable(
        object owner,
        long registryVersion,
        IReadOnlyList<Object3D> allObjects,
        IReadOnlyList<Object3D> renderables,
        IReadOnlyList<Object3D> pickables,
        IReadOnlyList<Object3D> colliders,
        IReadOnlyList<Object3D> dynamicBodies,
        IReadOnlyList<Object3D> staticColliders,
        IReadOnlyList<HighScaleInstanceLayer3D> highScaleLayers,
        IReadOnlyList<ImportedModel3D> animatedModels,
        IReadOnlyList<ParticleSystem3D> particleSystems)
    {
        _reusableOwner = owner ?? throw new ArgumentNullException(nameof(owner));
        RegistryVersion = registryVersion;
        CopyInto(allObjects, ref _allObjects, ref _allObjectCount);
        CopyInto(renderables, ref _renderables, ref _renderableCount);
        CopyInto(pickables, ref _pickables, ref _pickableCount);
        CopyInto(colliders, ref _colliders, ref _colliderCount);
        CopyInto(dynamicBodies, ref _dynamicBodies, ref _dynamicBodyCount);
        CopyInto(staticColliders, ref _staticColliders, ref _staticColliderCount);
        CopyInto(highScaleLayers, ref _highScaleLayers, ref _highScaleLayerCount);
        CopyInto(animatedModels, ref _animatedModels, ref _animatedModelCount);
        CopyInto(particleSystems, ref _particleSystems, ref _particleSystemCount);
    }

    private static void CopyInto<T>(IReadOnlyList<T> source, ref T[] buffer, ref int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        var previousCount = count;
        count = source.Count;
        if (buffer.Length < count)
        {
            var capacity = buffer.Length == 0 ? 4 : buffer.Length;
            while (capacity < count) capacity = checked(capacity * 2);
            buffer = new T[capacity];
        }

        for (var i = 0; i < count; i++) buffer[i] = source[i];
        if (RuntimeHelpers3D.ContainsReferences<T>() && previousCount > count)
        {
            Array.Clear(buffer, count, previousCount - count);
        }
    }

    private static T[] CopyPrefix<T>(T[] source, int count)
    {
        if (count == 0) return Array.Empty<T>();
        var copy = new T[count];
        Array.Copy(source, copy, count);
        return copy;
    }

    private static class RuntimeHelpers3D
    {
        public static bool ContainsReferences<T>() => System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>();
    }
}
