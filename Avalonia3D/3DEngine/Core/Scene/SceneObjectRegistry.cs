using System;
using System.Collections.Generic;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Spatial;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Cached flat scene view for hot paths. It avoids repeated recursive traversal of
/// CompositeObject3D trees during rendering, picking and physics.
/// </summary>
public sealed class SceneObjectRegistry
{
    private readonly Scene3D _scene;
    private readonly List<Object3D> _allObjects = new();
    private readonly List<Object3D> _renderables = new();
    private readonly List<Object3D> _pickables = new();
    private readonly List<Object3D> _colliders = new();
    private readonly List<Object3D> _dynamicBodies = new();
    private readonly List<Object3D> _staticColliders = new();
    private readonly List<HighScaleInstanceLayer3D> _highScaleLayers = new();
    private bool _dirty = true;
    private int _version;
    private SceneFrameSnapshot3D? _cachedFrameSnapshot;

    internal SceneObjectRegistry(Scene3D scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public SpatialHashGrid3D PickableIndex { get; } = new(8f);
    public SpatialHashGrid3D ColliderIndex { get; } = new(8f);

    public int Version
    {
        get { EnsureCurrent(); return _version; }
    }

    public IReadOnlyList<Object3D> AllObjects { get { EnsureCurrent(); return _allObjects; } }
    public IReadOnlyList<Object3D> Renderables { get { EnsureCurrent(); return _renderables; } }
    public IReadOnlyList<Object3D> Pickables { get { EnsureCurrent(); return _pickables; } }
    public IReadOnlyList<Object3D> Colliders { get { EnsureCurrent(); return _colliders; } }
    public IReadOnlyList<Object3D> DynamicBodies { get { EnsureCurrent(); return _dynamicBodies; } }
    public IReadOnlyList<Object3D> StaticColliders { get { EnsureCurrent(); return _staticColliders; } }


    public SceneFrameSnapshot3D GetFrameSnapshot()
    {
        EnsureCurrent();
        if (_cachedFrameSnapshot is { RegistryVersion: var version } && version == _version)
        {
            return _cachedFrameSnapshot;
        }

        _cachedFrameSnapshot = new SceneFrameSnapshot3D
        {
            RegistryVersion = _version,
            AllObjects = CopyToArray(_allObjects),
            Renderables = CopyToArray(_renderables),
            Pickables = CopyToArray(_pickables),
            Colliders = CopyToArray(_colliders),
            DynamicBodies = CopyToArray(_dynamicBodies),
            StaticColliders = CopyToArray(_staticColliders),
            HighScaleLayers = CopyHighScaleLayersToArray(_highScaleLayers)
        };
        return _cachedFrameSnapshot;
    }

    public Object3D[] SnapshotAllObjects() => GetFrameSnapshot().AllObjects;
    public Object3D[] SnapshotRenderables() => GetFrameSnapshot().Renderables;
    public Object3D[] SnapshotPickables() => GetFrameSnapshot().Pickables;
    public Object3D[] SnapshotColliders() => GetFrameSnapshot().Colliders;
    public Object3D[] SnapshotDynamicBodies() => GetFrameSnapshot().DynamicBodies;
    public Object3D[] SnapshotStaticColliders() => GetFrameSnapshot().StaticColliders;
    public HighScaleInstanceLayer3D[] SnapshotHighScaleLayers() => GetFrameSnapshot().HighScaleLayers;

    private static Object3D[] CopyToArray(IReadOnlyList<Object3D> source)
    {
        var snapshot = new Object3D[source.Count];
        for (var i = 0; i < snapshot.Length; i++) snapshot[i] = source[i];
        return snapshot;
    }

    private static HighScaleInstanceLayer3D[] CopyHighScaleLayersToArray(IReadOnlyList<HighScaleInstanceLayer3D> source)
    {
        var snapshot = new HighScaleInstanceLayer3D[source.Count];
        for (var i = 0; i < snapshot.Length; i++) snapshot[i] = source[i];
        return snapshot;
    }

    internal void Invalidate()
    {
        _dirty = true;
        _cachedFrameSnapshot = null;
    }

    internal void Invalidate(SceneChangeKind kind, Object3D? source)
    {
        if (kind == SceneChangeKind.Transform && !_dirty && source is not null)
        {
            RefreshSpatialIndexes(source);
            return;
        }

        Invalidate();
    }

    private void RefreshSpatialIndexes(Object3D source)
    {
        RefreshSpatialIndexesForObject(source);
        if (source is CompositeObject3D composite)
        {
            foreach (var child in composite.EnumerateDescendants())
            {
                RefreshSpatialIndexesForObject(child);
            }
        }
    }

    private void RefreshSpatialIndexesForObject(Object3D obj)
    {
        if (obj.IsVisible && obj.UseScenePicking)
        {
            var bounds = obj.Collider?.GetWorldBounds(obj) ?? obj.GetWorldBounds();
            PickableIndex.Update(obj, bounds);
        }
        else
        {
            PickableIndex.Remove(obj);
        }

        if (obj.IsVisible && obj.Collider is not null)
        {
            ColliderIndex.Update(obj, obj.Collider.GetWorldBounds(obj));
        }
        else
        {
            ColliderIndex.Remove(obj);
        }
    }

    private void EnsureCurrent()
    {
        if (!_dirty) return;
        Rebuild();
    }

    private void Rebuild()
    {
        _allObjects.Clear();
        _renderables.Clear();
        _pickables.Clear();
        _colliders.Clear();
        _dynamicBodies.Clear();
        _staticColliders.Clear();
        _highScaleLayers.Clear();
        PickableIndex.Clear();
        ColliderIndex.Clear();

        foreach (var root in _scene.Objects)
        {
            AddRecursive(root, includeCompositeRoot: true);
        }

        _version++;
        _dirty = false;
    }

    private void AddRecursive(Object3D obj, bool includeCompositeRoot)
    {
        if (includeCompositeRoot || obj is not CompositeObject3D)
        {
            _allObjects.Add(obj);

            if (obj is HighScaleInstanceLayer3D highScaleLayer)
            {
                _highScaleLayers.Add(highScaleLayer);
            }

            if (obj.IsVisible && obj.UseMeshRendering)
            {
                _renderables.Add(obj);
            }

            if (obj.IsVisible && obj.UseScenePicking)
            {
                _pickables.Add(obj);
                var bounds = obj.Collider?.GetWorldBounds(obj) ?? obj.GetWorldBounds();
                PickableIndex.Add(obj, bounds);
            }

            if (obj.IsVisible && obj.Collider is not null)
            {
                _colliders.Add(obj);
                ColliderIndex.Add(obj, obj.Collider.GetWorldBounds(obj));
                if (obj.Rigidbody is { IsKinematic: false })
                {
                    _dynamicBodies.Add(obj);
                }
                else
                {
                    _staticColliders.Add(obj);
                }
            }
        }

        if (obj is not CompositeObject3D composite)
        {
            return;
        }

        foreach (var child in composite.Children)
        {
            AddRecursive(child, includeCompositeRoot: true);
        }
    }
}
