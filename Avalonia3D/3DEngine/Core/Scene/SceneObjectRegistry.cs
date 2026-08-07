using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Spatial;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Incremental flat scene graph used by rendering, picking and physics hot paths.
/// Membership and spatial bounds are patched from exact Object3D change sources;
/// a full traversal is reserved for explicit recovery through <see cref="Invalidate"/>.
/// </summary>
public sealed class SceneObjectRegistry
{
    [Flags]
    private enum Membership
    {
        None = 0,
        All = 1 << 0,
        Renderable = 1 << 1,
        Pickable = 1 << 2,
        Collider = 1 << 3,
        DynamicBody = 1 << 4,
        StaticCollider = 1 << 5,
        HighScale = 1 << 6,
        AnimatedModel = 1 << 7,
        ParticleSystem = 1 << 8
    }

    private readonly Scene3D _scene;
    private readonly PackedReferenceList<Object3D> _allObjects = new();
    private readonly PackedReferenceList<Object3D> _renderables = new();
    private readonly PackedReferenceList<Object3D> _pickables = new();
    private readonly PackedReferenceList<Object3D> _colliders = new();
    private readonly PackedReferenceList<Object3D> _dynamicBodies = new();
    private readonly PackedReferenceList<Object3D> _staticColliders = new();
    private readonly PackedReferenceList<HighScaleInstanceLayer3D> _highScaleLayers = new();
    private readonly PackedReferenceList<ImportedModel3D> _animatedModels = new();
    private readonly PackedReferenceList<ParticleSystem3D> _particleSystems = new();
    private readonly Dictionary<Object3D, Membership> _membership = new(ObjectReferenceComparer3D<Object3D>.Instance);
    private readonly Dictionary<CompositeObject3D, Object3D[]> _registeredChildren = new(ObjectReferenceComparer3D<CompositeObject3D>.Instance);
    private long _version;
    private SceneFrameSnapshot3D? _cachedFrameSnapshot;

    internal SceneObjectRegistry(Scene3D scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public SpatialHashGrid3D PickableIndex { get; } = new(8f);
    public SpatialHashGrid3D ColliderIndex { get; } = new(8f);

    public long Version => _version;
    internal long PublishedVersion => _version;
    public int FullRebuildCount { get; private set; }
    public int IncrementalChangeCount { get; private set; }
    public int SpatialRefreshCount { get; private set; }
    public int SnapshotBuildCount { get; private set; }

    public IReadOnlyList<Object3D> AllObjects => _allObjects;
    public IReadOnlyList<Object3D> Renderables => _renderables;
    public IReadOnlyList<Object3D> Pickables => _pickables;
    public IReadOnlyList<Object3D> Colliders => _colliders;
    public IReadOnlyList<Object3D> DynamicBodies => _dynamicBodies;
    public IReadOnlyList<Object3D> StaticColliders => _staticColliders;
    public IReadOnlyList<ImportedModel3D> AnimatedModels => _animatedModels;
    public IReadOnlyList<ParticleSystem3D> ParticleSystems => _particleSystems;

    public SceneFrameSnapshot3D GetFrameSnapshot()
    {
        if (_cachedFrameSnapshot is { RegistryVersion: var version } && version == _version)
        {
            return _cachedFrameSnapshot;
        }

        _cachedFrameSnapshot = new SceneFrameSnapshot3D(
            _version,
            _allObjects.CopyToArray(),
            _renderables.CopyToArray(),
            _pickables.CopyToArray(),
            _colliders.CopyToArray(),
            _dynamicBodies.CopyToArray(),
            _staticColliders.CopyToArray(),
            _highScaleLayers.CopyToArray(),
            _animatedModels.CopyToArray(),
            _particleSystems.CopyToArray());
        SnapshotBuildCount++;
        return _cachedFrameSnapshot;
    }

    /// <summary>
    /// Copies membership into a renderer-owned reusable publication. Capacity grows only
    /// when a category reaches a new high-water mark; ordinary registry changes no longer
    /// allocate nine exact arrays on the render hot path. The caller must hold the scene
    /// render-read lease for the entire lifetime of the returned frame context.
    /// </summary>
    internal void CopyFrameSnapshotInto(SceneFrameSnapshot3D target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.MatchesReusableOwner(this, _version)) return;
        target.ResetReusable(
            this,
            _version,
            _allObjects,
            _renderables,
            _pickables,
            _colliders,
            _dynamicBodies,
            _staticColliders,
            _highScaleLayers,
            _animatedModels,
            _particleSystems);
        SnapshotBuildCount++;
    }

    public Object3D[] SnapshotAllObjects() => GetFrameSnapshot().AllObjects;
    public Object3D[] SnapshotRenderables() => GetFrameSnapshot().Renderables;
    public Object3D[] SnapshotPickables() => GetFrameSnapshot().Pickables;
    public Object3D[] SnapshotColliders() => GetFrameSnapshot().Colliders;
    public Object3D[] SnapshotDynamicBodies() => GetFrameSnapshot().DynamicBodies;
    public Object3D[] SnapshotStaticColliders() => GetFrameSnapshot().StaticColliders;
    public HighScaleInstanceLayer3D[] SnapshotHighScaleLayers() => GetFrameSnapshot().HighScaleLayers;

    internal bool Contains(Object3D obj) => _membership.ContainsKey(obj);

    internal void Invalidate() => Rebuild();

    internal void Invalidate(SceneChangeKind kind, Object3D? source) => ApplyChange(kind, source);

    internal void ApplyChange(SceneChangeKind kind, Object3D? source)
    {
        switch (kind)
        {
            case SceneChangeKind.Structure:
                ApplyStructureChange(source);
                break;
            case SceneChangeKind.Transform:
            case SceneChangeKind.Geometry:
            case SceneChangeKind.AnimationPose:
                if (source is not null) RefreshSpatialChange(source);
                break;
            case SceneChangeKind.Visibility:
            case SceneChangeKind.Physics:
            case SceneChangeKind.Control:
                if (source is not null)
                {
                    RefreshObjectMembership(source, refreshSpatial: true);
                    RefreshIndexedAncestors(source);
                }
                break;
            case SceneChangeKind.Unknown:
                Rebuild();
                break;
        }
    }

    internal void ClearIncremental()
    {
        var hadObjects = _allObjects.Count != 0;
        ClearStorage();
        if (hadObjects)
        {
            _version++;
            IncrementalChangeCount++;
        }
    }

    internal void AddSubtreeObjects(Object3D source, List<Object3D> output, HashSet<Object3D> seen)
    {
        if (!_membership.ContainsKey(source)) return;
        AddRegisteredSubtree(source, output, seen);
    }

    private void ApplyStructureChange(Object3D? source)
    {
        if (source is null)
        {
            // Scene.Clear has already emptied the roots. A source-less structure event is
            // otherwise ambiguous, so rebuild once instead of guessing a stale membership.
            if (_scene.Objects.Count == 0) ClearIncremental();
            else Invalidate();
            return;
        }

        var changed = false;
        if (!ReferenceEquals(source.OwnerScene, _scene))
        {
            changed = UnregisterSubtree(source);
        }
        else if (!_membership.ContainsKey(source))
        {
            changed = RegisterSubtree(source);
        }
        else if (source is CompositeObject3D composite)
        {
            changed = RefreshCompositeChildren(composite);
            changed |= RefreshObjectMembership(composite, refreshSpatial: true, publishVersion: false);
        }
        else
        {
            changed = RefreshObjectMembership(source, refreshSpatial: true, publishVersion: false);
        }

        RefreshIndexedAncestors(source);
        if (changed) PublishMembershipChange();
    }

    private bool RefreshCompositeChildren(CompositeObject3D composite)
    {
        var changed = false;
        if (_registeredChildren.TryGetValue(composite, out var previous))
        {
            for (var i = 0; i < previous.Length; i++) changed |= UnregisterSubtree(previous[i]);
        }

        var current = CopyChildren(composite.Children);
        _registeredChildren[composite] = current;
        for (var i = 0; i < current.Length; i++) changed |= RegisterSubtree(current[i]);
        return changed;
    }

    private bool RegisterSubtree(Object3D obj)
    {
        if (!ReferenceEquals(obj.OwnerScene, _scene)) return false;

        var changed = false;
        if (!_membership.ContainsKey(obj))
        {
            var flags = EvaluateMembership(obj);
            _membership.Add(obj, flags);
            AddToCollections(obj, flags);
            changed = true;
        }

        if (obj is CompositeObject3D composite)
        {
            var children = CopyChildren(composite.Children);
            _registeredChildren[composite] = children;
            for (var i = 0; i < children.Length; i++) changed |= RegisterSubtree(children[i]);
        }

        return changed;
    }

    private bool UnregisterSubtree(Object3D obj)
    {
        var changed = false;
        if (obj is CompositeObject3D composite && _registeredChildren.TryGetValue(composite, out var children))
        {
            for (var i = 0; i < children.Length; i++) changed |= UnregisterSubtree(children[i]);
            _registeredChildren.Remove(composite);
        }

        if (_membership.Remove(obj, out var flags))
        {
            RemoveFromCollections(obj, flags);
            changed = true;
        }

        return changed;
    }

    private bool RefreshObjectMembership(Object3D obj, bool refreshSpatial, bool publishVersion = true)
    {
        if (!_membership.TryGetValue(obj, out var previous)) return false;
        var current = EvaluateMembership(obj);
        var changed = previous != current;
        if (changed)
        {
            RemoveFromCollections(obj, previous & ~current);
            AddToCollections(obj, current & ~previous);
            _membership[obj] = current;
        }

        // Add/remove paths already update both spatial indexes. Reinsert only when
        // membership stayed stable and the collider/bounds may have changed.
        if (refreshSpatial && !changed)
        {
            RefreshSpatialObject(obj, current);
        }

        if (changed && publishVersion) PublishMembershipChange();
        return changed;
    }

    private void RefreshSpatialSubtree(Object3D source)
    {
        if (!_membership.TryGetValue(source, out var flags)) return;
        RefreshSpatialObject(source, flags);
        if (source is CompositeObject3D composite && _registeredChildren.TryGetValue(composite, out var children))
        {
            for (var i = 0; i < children.Length; i++) RefreshSpatialSubtree(children[i]);
        }
    }

    private void RefreshSpatialChange(Object3D source)
    {
        RefreshSpatialSubtree(source);
        // Composite bounds aggregate children. Preserve the exact leaf source for
        // render consumers while still refreshing any indexed ancestors.
        RefreshIndexedAncestors(source);
    }

    private void RefreshIndexedAncestors(Object3D source)
    {
        var ancestor = source.Parent;
        while (ancestor is not null)
        {
            if (_membership.TryGetValue(ancestor, out var flags) &&
                (flags & (Membership.Pickable | Membership.Collider)) != 0)
            {
                RefreshSpatialObject(ancestor, flags);
            }
            ancestor = ancestor.Parent;
        }
    }

    private void RefreshSpatialObject(Object3D obj, Membership flags)
    {
        if ((flags & Membership.Pickable) != 0)
        {
            PickableIndex.Update(obj, obj.Collider?.GetWorldBounds(obj) ?? obj.GetWorldBounds());
            SpatialRefreshCount++;
        }
        else
        {
            PickableIndex.Remove(obj);
        }

        if ((flags & Membership.Collider) != 0 && obj.Collider is { } collider)
        {
            ColliderIndex.Update(obj, collider.GetWorldBounds(obj));
            SpatialRefreshCount++;
        }
        else
        {
            ColliderIndex.Remove(obj);
        }
    }

    private void AddToCollections(Object3D obj, Membership flags)
    {
        if ((flags & Membership.All) != 0) _allObjects.Add(obj);
        if ((flags & Membership.Renderable) != 0) _renderables.Add(obj);
        if ((flags & Membership.Pickable) != 0)
        {
            _pickables.Add(obj);
            PickableIndex.Add(obj, obj.Collider?.GetWorldBounds(obj) ?? obj.GetWorldBounds());
        }
        if ((flags & Membership.Collider) != 0)
        {
            _colliders.Add(obj);
            ColliderIndex.Add(obj, obj.Collider!.GetWorldBounds(obj));
        }
        if ((flags & Membership.DynamicBody) != 0) _dynamicBodies.Add(obj);
        if ((flags & Membership.StaticCollider) != 0) _staticColliders.Add(obj);
        if ((flags & Membership.HighScale) != 0) _highScaleLayers.Add((HighScaleInstanceLayer3D)obj);
        if ((flags & Membership.AnimatedModel) != 0) _animatedModels.Add((ImportedModel3D)obj);
        if ((flags & Membership.ParticleSystem) != 0) _particleSystems.Add((ParticleSystem3D)obj);
    }

    private void RemoveFromCollections(Object3D obj, Membership flags)
    {
        if ((flags & Membership.All) != 0) _allObjects.Remove(obj);
        if ((flags & Membership.Renderable) != 0) _renderables.Remove(obj);
        if ((flags & Membership.Pickable) != 0)
        {
            _pickables.Remove(obj);
            PickableIndex.Remove(obj);
        }
        if ((flags & Membership.Collider) != 0)
        {
            _colliders.Remove(obj);
            ColliderIndex.Remove(obj);
        }
        if ((flags & Membership.DynamicBody) != 0) _dynamicBodies.Remove(obj);
        if ((flags & Membership.StaticCollider) != 0) _staticColliders.Remove(obj);
        if ((flags & Membership.HighScale) != 0) _highScaleLayers.Remove((HighScaleInstanceLayer3D)obj);
        if ((flags & Membership.AnimatedModel) != 0) _animatedModels.Remove((ImportedModel3D)obj);
        if ((flags & Membership.ParticleSystem) != 0) _particleSystems.Remove((ParticleSystem3D)obj);
    }

    private static Membership EvaluateMembership(Object3D obj)
    {
        var flags = Membership.All;
        if (obj.IsVisible && obj.UseMeshRendering) flags |= Membership.Renderable;
        if (obj.IsVisible && obj.UseScenePicking) flags |= Membership.Pickable;
        if (obj.IsVisible && obj.Collider is not null)
        {
            flags |= Membership.Collider;
            flags |= obj.Rigidbody is { IsKinematic: false }
                ? Membership.DynamicBody
                : Membership.StaticCollider;
        }
        if (obj is HighScaleInstanceLayer3D) flags |= Membership.HighScale;
        if (obj is ImportedModel3D) flags |= Membership.AnimatedModel;
        if (obj is ParticleSystem3D) flags |= Membership.ParticleSystem;
        return flags;
    }

    private void PublishMembershipChange()
    {
        _version++;
        IncrementalChangeCount++;
        _cachedFrameSnapshot = null;
    }

    private void Rebuild()
    {
        ClearStorage();
        for (var i = 0; i < _scene.Objects.Count; i++) RegisterSubtree(_scene.Objects[i]);
        _version++;
        FullRebuildCount++;
        _cachedFrameSnapshot = null;
    }

    private void ClearStorage()
    {
        _allObjects.Clear();
        _renderables.Clear();
        _pickables.Clear();
        _colliders.Clear();
        _dynamicBodies.Clear();
        _staticColliders.Clear();
        _highScaleLayers.Clear();
        _animatedModels.Clear();
        _particleSystems.Clear();
        _membership.Clear();
        _registeredChildren.Clear();
        PickableIndex.Clear();
        ColliderIndex.Clear();
        _cachedFrameSnapshot = null;
    }

    private void AddRegisteredSubtree(Object3D obj, List<Object3D> output, HashSet<Object3D> seen)
    {
        if (seen.Add(obj)) output.Add(obj);
        if (obj is not CompositeObject3D composite || !_registeredChildren.TryGetValue(composite, out var children)) return;
        for (var i = 0; i < children.Length; i++) AddRegisteredSubtree(children[i], output, seen);
    }

    private static Object3D[] CopyChildren(IReadOnlyList<Object3D> children)
    {
        if (children.Count == 0) return Array.Empty<Object3D>();
        var copy = new Object3D[children.Count];
        for (var i = 0; i < copy.Length; i++) copy[i] = children[i];
        return copy;
    }
}

internal sealed class PackedReferenceList<T> : IReadOnlyList<T> where T : class
{
    private readonly List<T> _items = new();
    private readonly Dictionary<T, int> _indices = new(ObjectReferenceComparer3D<T>.Instance);

    public int Count => _items.Count;
    public T this[int index] => _items[index];

    public bool Add(T item)
    {
        if (_indices.ContainsKey(item)) return false;
        _indices.Add(item, _items.Count);
        _items.Add(item);
        return true;
    }

    public bool Remove(T item)
    {
        if (!_indices.Remove(item, out var index)) return false;
        var lastIndex = _items.Count - 1;
        if (index != lastIndex)
        {
            var moved = _items[lastIndex];
            _items[index] = moved;
            _indices[moved] = index;
        }
        _items.RemoveAt(lastIndex);
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _indices.Clear();
    }

    public T[] CopyToArray()
    {
        var copy = new T[_items.Count];
        _items.CopyTo(copy);
        return copy;
    }

    public List<T>.Enumerator GetEnumerator() => _items.GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal sealed class ObjectReferenceComparer3D<T> : IEqualityComparer<T> where T : class
{
    public static readonly ObjectReferenceComparer3D<T> Instance = new();
    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
