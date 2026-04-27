using System;
using System.Collections.Generic;
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
    private bool _dirty = true;
    private int _version;

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

    internal void Invalidate() => _dirty = true;

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
