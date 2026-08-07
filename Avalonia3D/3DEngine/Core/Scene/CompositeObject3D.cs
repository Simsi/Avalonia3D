using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Scene;

public abstract class CompositeObject3D : Object3D
{
    protected CompositeObject3D()
    {
        _childrenView = _children.AsReadOnly();
        _partsView = new ReadOnlyDictionary<string, Object3D>(_parts);
    }

    private readonly List<Object3D> _children = new();
    private readonly ReadOnlyCollection<Object3D> _childrenView;
    private readonly Dictionary<string, Object3D> _parts = new(StringComparer.Ordinal);
    private readonly ReadOnlyDictionary<string, Object3D> _partsView;
    private bool _built;
    private bool _building;
    private Bounds3D _cachedWorldBounds = Bounds3D.Empty;
    private bool _worldBoundsDirty = true;

    public override bool UseMeshRendering => false;
    public override bool UseScenePicking => false;

    // Composite hover/selection is inherited by child visuals through Object3D.IsEffectivelyHovered/Selected.
    // Do not mutate every child on root hover/selection; that creates large event storms in high-part composites.

    public IReadOnlyList<Object3D> Children
    {
        get
        {
            EnsureBuilt();
            return _childrenView;
        }
    }

    public IReadOnlyDictionary<string, Object3D> Parts
    {
        get
        {
            EnsureBuilt();
            return _partsView;
        }
    }

    public void Rebuild()
    {
        if (_building)
        {
            return;
        }

        BuildIntoTemporaryState(out var newChildren, out var newParts);
        ReplaceChildren(newChildren, newParts);
        _built = true;
        RaiseChanged(SceneChangeKind.Structure);
    }

    public Object3D? FindPart(string name)
    {
        EnsureBuilt();
        return _parts.TryGetValue(name, out var part) ? part : null;
    }

    protected void MarkPartsDirty()
    {
        if (_building)
        {
            return;
        }

        _built = false;
        RaiseChanged(SceneChangeKind.Structure);
    }

    public override Bounds3D GetWorldBounds()
    {
        EnsureBuilt();
        if (!_worldBoundsDirty)
        {
            return _cachedWorldBounds;
        }

        var hasBounds = false;
        var min = System.Numerics.Vector3.Zero;
        var max = System.Numerics.Vector3.Zero;

        foreach (var child in _children)
        {
            var bounds = child.GetWorldBounds();
            if (!bounds.IsValid)
            {
                continue;
            }

            if (!hasBounds)
            {
                min = bounds.Min;
                max = bounds.Max;
                hasBounds = true;
            }
            else
            {
                min = System.Numerics.Vector3.Min(min, bounds.Min);
                max = System.Numerics.Vector3.Max(max, bounds.Max);
            }
        }

        _cachedWorldBounds = hasBounds ? new Bounds3D(min, max) : Bounds3D.Empty;
        _worldBoundsDirty = false;
        return _cachedWorldBounds;
    }

    public IEnumerable<Object3D> EnumerateDescendants(bool includeSelf = false)
    {
        EnsureBuilt();
        if (includeSelf)
        {
            yield return this;
        }

        foreach (var child in _children)
        {
            yield return child;

            if (child is CompositeObject3D composite)
            {
                foreach (var nested in composite.EnumerateDescendants())
                {
                    yield return nested;
                }
            }
        }
    }

    protected abstract void Build(CompositeBuilder3D builder);

    protected override Mesh3D BuildMesh() => Mesh3D.Empty;

    protected override void OnWorldCacheInvalidated()
    {
        _worldBoundsDirty = true;
        foreach (var child in _children)
        {
            child.InvalidateWorldCacheRecursive();
        }
    }

    internal void AddBuiltPart(string name, Object3D part)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Part name cannot be empty.", nameof(name));
        }

        if (_parts.ContainsKey(name))
        {
            throw new InvalidOperationException($"A part named '{name}' already exists in '{Name}'.");
        }

        if (part.OwnerScene is not null && !ReferenceEquals(part.OwnerScene, OwnerScene))
        {
            throw new InvalidOperationException($"Part '{name}' is already attached to another scene.");
        }

        part.Name = name;
        if (OwnerScene is { } scene) scene.AttachOwnerSceneRecursive(part);
        else part.OwnerScene = null;
        part.Parent = this;
        part.Changed += OnChildChanged;
        _children.Add(part);
        _parts.Add(name, part);
    }

    private void EnsureBuilt()
    {
        if (_built || _building)
        {
            return;
        }

        BuildIntoTemporaryState(out var newChildren, out var newParts);
        ReplaceChildren(newChildren, newParts);
        _built = true;
    }

    private void BuildIntoTemporaryState(out List<Object3D> newChildren, out Dictionary<string, Object3D> newParts)
    {
        if (_building)
        {
            newChildren = new List<Object3D>();
            newParts = new Dictionary<string, Object3D>(StringComparer.Ordinal);
            return;
        }

        var oldChildren = new List<Object3D>(_children);
        var oldParts = new Dictionary<string, Object3D>(_parts, StringComparer.Ordinal);

        foreach (var child in oldChildren)
        {
            child.Changed -= OnChildChanged;
            Scene3D.DetachOwnerSceneRecursive(child);
            child.Parent = null;
        }

        _children.Clear();
        _parts.Clear();
        _building = true;
        try
        {
            var builder = new CompositeBuilder3D(this);
            Build(builder);

            newChildren = new List<Object3D>(_children);
            newParts = new Dictionary<string, Object3D>(_parts, StringComparer.Ordinal);

            foreach (var child in newChildren)
            {
                child.Changed -= OnChildChanged;
                Scene3D.DetachOwnerSceneRecursive(child);
                child.Parent = null;
            }
        }
        catch
        {
            foreach (var child in _children)
            {
                child.Changed -= OnChildChanged;
                Scene3D.DetachOwnerSceneRecursive(child);
                child.Parent = null;
            }

            _children.Clear();
            _parts.Clear();
            foreach (var child in oldChildren)
            {
                if (OwnerScene is { } scene) scene.AttachOwnerSceneRecursive(child);
                child.Parent = this;
                child.Changed += OnChildChanged;
                _children.Add(child);
            }

            foreach (var pair in oldParts)
            {
                _parts[pair.Key] = pair.Value;
            }

            _built = oldChildren.Count > 0 || oldParts.Count > 0;
            throw;
        }
        finally
        {
            _children.Clear();
            _parts.Clear();
            _building = false;
        }
    }

    private void ReplaceChildren(List<Object3D> newChildren, Dictionary<string, Object3D> newParts)
    {
        ClearChildren();

        foreach (var child in newChildren)
        {
            if (OwnerScene is { } scene) scene.AttachOwnerSceneRecursive(child);
            child.Parent = this;
            child.Changed += OnChildChanged;
            _children.Add(child);
        }

        foreach (var pair in newParts)
        {
            _parts[pair.Key] = pair.Value;
        }

        _worldBoundsDirty = true;
        InvalidateWorldCacheRecursive();
    }

    private void ClearChildren()
    {
        foreach (var child in _children)
        {
            child.Changed -= OnChildChanged;
            Scene3D.DetachOwnerSceneRecursive(child);
            child.Parent = null;
        }

        _children.Clear();
        _parts.Clear();
    }

    private void OnChildChanged(object? sender, EventArgs e)
    {
        _worldBoundsDirty = true;
        if (_building)
        {
            // Builder configuration belongs to the pending subtree transaction. Publishing
            // temporary parts would leak objects that are not registered yet and would also
            // report changes from a build that may still fail and roll back.
            return;
        }

        MarkWorldBoundsDirtyRecursive();
        var kind = e is Object3DChangedEventArgs changed ? changed.Kind : SceneChangeKind.Unknown;
        var source = e is Object3DChangedEventArgs precise ? precise.Source : sender as Object3D;
        RaiseChanged(kind, source);
    }
}
