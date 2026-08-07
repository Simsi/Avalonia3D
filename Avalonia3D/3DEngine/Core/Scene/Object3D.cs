using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Transforms;

namespace ThreeDEngine.Core.Scene;

public abstract class Object3D : INotifyPropertyChanged
{
    private string _name = "Object3D";
    private Vector3 _rotationDegrees;
    private bool _suppressTransformChanged;
    private ColorRgba _fill = ColorRgba.White;
    private Material3D _material = Material3D.CreateUnlit(ColorRgba.White);
    private Collider3D? _collider;
    private Rigidbody3D? _rigidbody;
    private bool _isVisible = true;
    private bool _isPickable = true;
    private bool _isHovered;
    private bool _isSelected;
    private bool _isManipulationEnabled = true;
    private object? _dataContext;
    private Mesh3D? _mesh;
    private bool _meshDirty = true;
    private int _geometryVersion;
    private Object3D? _parent;
    private Matrix4x4 _worldMatrix = Matrix4x4.Identity;
    private Bounds3D _worldBounds = Bounds3D.Empty;
    private bool _worldMatrixDirty = true;
    private bool _worldBoundsDirty = true;
    private int _transformVersion;
    private int _materialVersion;
    private Scene3D? _ownerScene;
    private bool _meshBuiltOutsideEngineCache;
    private Engine3D? _meshEngineScope;

    protected Object3D()
    {
        Id = Guid.NewGuid().ToString("N");
        Transform = new Transform3D { EnterMutationScope = EnterMutationScope };
        Transform.Changed += OnTransformChanged;
        _material.Changed += OnMaterialChanged;
        _material.MutationScopeRequested += EnterMaterialMutationScope;
    }

    public string Id { get; }

    internal Scene3D? OwnerScene
    {
        get => _ownerScene;
        set
        {
            if (ReferenceEquals(_ownerScene, value)) return;
            _ownerScene = value;
            if (value is not null && (_meshBuiltOutsideEngineCache || (_meshEngineScope is not null && !ReferenceEquals(_meshEngineScope, value.Engine))))
            {
                _mesh = null;
                _meshDirty = true;
                _meshBuiltOutsideEngineCache = false;
                _meshEngineScope = null;
                _worldBoundsDirty = true;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    public event EventHandler<ScenePointerEventArgs>? Clicked;
    public event EventHandler<ScenePointerEventArgs>? PointerEntered;
    public event EventHandler<ScenePointerEventArgs>? PointerExited;
    public event EventHandler<ScenePointerEventArgs>? PointerMoved;
    public event EventHandler<ScenePointerEventArgs>? PointerPressed;
    public event EventHandler<ScenePointerEventArgs>? PointerReleased;

    public Transform3D Transform { get; }

    public Object3D? Parent
    {
        get => _parent;
        internal set
        {
            if (ReferenceEquals(_parent, value))
            {
                return;
            }

            _parent = value;
            InvalidateWorldCacheRecursive();
        }
    }

    public virtual bool UseMeshRendering => true;
    public virtual bool UseScenePicking => IsPickable;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public object? DataContext
    {
        get => _dataContext;
        set => SetField(ref _dataContext, value);
    }

    public Vector3 Position
    {
        get => Transform.LocalPosition;
        set => Transform.LocalPosition = value;
    }

    public Vector3 RotationDegrees
    {
        get => _rotationDegrees;
        set
        {
            using var access = EnterMutationScope();
            if (_rotationDegrees == value)
            {
                return;
            }

            _rotationDegrees = value;
            _suppressTransformChanged = true;
            try
            {
                Transform.SetEulerDegrees(value);
            }
            finally
            {
                _suppressTransformChanged = false;
            }

            InvalidateWorldCacheRecursive();
            OnPropertyChanged(nameof(Transform));
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(Scale));
            OnPropertyChanged(nameof(LocalMatrix));
            OnPropertyChanged(nameof(RotationDegrees));
            OnPropertyChanged(nameof(Rotation));
            RaiseChanged(SceneChangeKind.Transform);
        }
    }

    public Vector3 Rotation
    {
        get => RotationDegrees;
        set => RotationDegrees = value;
    }

    public Vector3 Scale
    {
        get => Transform.LocalScale;
        set => Transform.LocalScale = value;
    }

    public ColorRgba Fill
    {
        get => _fill;
        set
        {
            using var access = EnterMutationScope();
            if (_fill.Equals(value))
            {
                return;
            }

            _fill = value;
            if (!_material.BaseColor.Equals(value))
            {
                _material.BaseColor = value;
                return;
            }

            OnPropertyChanged(nameof(Fill));
            OnPropertyChanged(nameof(Color));
            RaiseChanged(SceneChangeKind.Material);
        }
    }

    public Material3D Material
    {
        get => _material;
        set
        {
            using var access = EnterMutationScope();
            if (ReferenceEquals(_material, value))
            {
                return;
            }

            if (_material is not null)
            {
                _material.Changed -= OnMaterialChanged;
                _material.MutationScopeRequested -= EnterMaterialMutationScope;
            }

            _material = value ?? throw new ArgumentNullException(nameof(value));
            _material.Changed += OnMaterialChanged;
            _material.MutationScopeRequested += EnterMaterialMutationScope;
            _fill = _material.EffectiveColor;
            _materialVersion++;
            OnPropertyChanged(nameof(Material));
            OnPropertyChanged(nameof(Fill));
            OnPropertyChanged(nameof(Color));
            RaiseChanged(SceneChangeKind.Material);
        }
    }

    public Collider3D? Collider
    {
        get => _collider;
        set
        {
            using var access = EnterMutationScope();
            if (ReferenceEquals(_collider, value))
            {
                return;
            }
            if (value?.Owner is not null && !ReferenceEquals(value.Owner, this))
            {
                throw new InvalidOperationException("A Collider3D instance cannot be shared by multiple scene objects.");
            }

            if (_collider is not null)
            {
                _collider.Changed -= OnColliderChanged;
                _collider.Owner = null;
            }

            _collider = value;
            if (_collider is not null)
            {
                _collider.Owner = this;
                _collider.Changed += OnColliderChanged;
            }

            MarkWorldBoundsDirtyRecursive();
            OnPropertyChanged(nameof(Collider));
            RaiseChanged(SceneChangeKind.Physics);
        }
    }

    public Rigidbody3D? Rigidbody
    {
        get => _rigidbody;
        set
        {
            using var access = EnterMutationScope();
            if (ReferenceEquals(_rigidbody, value)) return;
            if (value?.Owner is not null && !ReferenceEquals(value.Owner, this))
            {
                throw new InvalidOperationException("A Rigidbody3D instance cannot be shared by multiple scene objects.");
            }
            if (_rigidbody is not null)
            {
                _rigidbody.MembershipChanged -= OnRigidbodyMembershipChanged;
                _rigidbody.ActivityChanged -= OnRigidbodyActivityChanged;
                _rigidbody.Owner = null;
            }

            _rigidbody = value;
            if (_rigidbody is not null)
            {
                _rigidbody.Owner = this;
                _rigidbody.MembershipChanged += OnRigidbodyMembershipChanged;
                _rigidbody.ActivityChanged += OnRigidbodyActivityChanged;
            }

            OnPropertyChanged(nameof(Rigidbody));
            RaiseChanged(SceneChangeKind.Physics);
        }
    }

    private void OnColliderChanged(object? sender, EventArgs e)
    {
        MarkWorldBoundsDirtyRecursive();
        RaiseChanged(SceneChangeKind.Physics);
    }

    private void OnRigidbodyMembershipChanged(object? sender, EventArgs e)
    {
        RaiseChanged(SceneChangeKind.Physics);
    }

    private void OnRigidbodyActivityChanged(object? sender, EventArgs e)
        => OwnerScene?.NotifyUpdateActivityChanged();

    public ColorRgba Color
    {
        get => Fill;
        set => Fill = value;
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            using var access = EnterMutationScope();
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged(nameof(IsVisible));
            RaiseChanged(SceneChangeKind.Visibility);
        }
    }

    public bool IsPickable
    {
        get => _isPickable;
        set
        {
            using var access = EnterMutationScope();
            if (_isPickable == value) return;
            _isPickable = value;
            OnPropertyChanged(nameof(IsPickable));
            RaiseChanged(SceneChangeKind.Visibility);
        }
    }

    public virtual bool IsHovered
    {
        get => _isHovered;
        set
        {
            using var access = EnterMutationScope();
            if (_isHovered == value) return;
            _isHovered = value;
            _materialVersion++;
            OnPropertyChanged(nameof(IsHovered));
            OnPropertyChanged(nameof(IsEffectivelyHovered));
            RaiseChanged(SceneChangeKind.Material);
        }
    }

    public virtual bool IsSelected
    {
        get => _isSelected;
        set
        {
            using var access = EnterMutationScope();
            if (_isSelected == value) return;
            _isSelected = value;
            _materialVersion++;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(IsEffectivelySelected));
            RaiseChanged(SceneChangeKind.Material);
        }
    }

    public bool IsEffectivelyHovered => IsHovered || (Parent?.IsEffectivelyHovered ?? false);

    public bool IsEffectivelySelected => IsSelected || (Parent?.IsEffectivelySelected ?? false);

    public bool IsManipulationEnabled
    {
        get => _isManipulationEnabled;
        set => SetField(ref _isManipulationEnabled, value);
    }

    public int GeometryVersion => _geometryVersion;
    public int TransformVersion => _transformVersion;
    public int MaterialVersion => _materialVersion;

    public Mesh3D GetMesh()
    {
        if (_meshDirty || _mesh is null)
        {
            _mesh = BuildMesh();
            _meshDirty = false;
            _geometryVersion++;
            MarkWorldBoundsDirtyRecursive();
        }

        return _mesh;
    }

    public Matrix4x4 LocalMatrix => Transform.LocalMatrix;

    public Matrix4x4 WorldMatrix => GetModelMatrix();

    public Bounds3D WorldBounds => GetWorldBounds();

    public virtual Bounds3D GetWorldBounds()
    {
        if (!_worldBoundsDirty)
        {
            return _worldBounds;
        }

        if (Collider is not null)
        {
            _worldBounds = Collider.GetWorldBounds(this);
            _worldBoundsDirty = false;
            return _worldBounds;
        }

        var mesh = GetMesh();
        _worldBounds = mesh.LocalBounds.IsValid ? mesh.LocalBounds.Transform(GetModelMatrix()) : Bounds3D.Empty;
        _worldBoundsDirty = false;
        return _worldBounds;
    }

    public virtual Matrix4x4 GetLocalMatrix() => Transform.LocalMatrix;

    public virtual Matrix4x4 GetModelMatrix()
    {
        if (!_worldMatrixDirty)
        {
            return _worldMatrix;
        }

        var local = GetLocalMatrix();
        _worldMatrix = Parent is null ? local : local * Parent.GetModelMatrix();
        _worldMatrixDirty = false;
        return _worldMatrix;
    }

    protected abstract Mesh3D BuildMesh();

    /// <summary>
    /// Shares immutable generated geometry inside the owning engine scope. Detached objects can
    /// still be inspected and receive a private mesh; once normally rendered through a scene,
    /// identical primitives use that scene's engine-owned cache.
    /// </summary>
    protected Mesh3D GetOrCreateCachedMesh(MeshResourceKey key, Func<Mesh3D> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (OwnerScene is { } scene)
        {
            _meshBuiltOutsideEngineCache = false;
            _meshEngineScope = scene.Engine;
            return scene.Engine.Services.GetRequiredService<MeshCache3D>().GetOrCreate(key, factory);
        }

        _meshBuiltOutsideEngineCache = true;
        _meshEngineScope = null;
        return factory();
    }

    protected virtual void OnWorldCacheInvalidated()
    {
    }

    internal void InvalidateWorldCacheRecursive()
    {
        _worldMatrixDirty = true;
        _worldBoundsDirty = true;
        _transformVersion++;
        OnWorldCacheInvalidated();
        OnPropertyChanged(nameof(WorldMatrix));
        OnPropertyChanged(nameof(WorldBounds));
    }

    protected void MarkWorldBoundsDirtyRecursive()
    {
        _worldBoundsDirty = true;
        OnWorldCacheInvalidated();
        OnPropertyChanged(nameof(WorldBounds));
    }

    private void OnTransformChanged(object? sender, EventArgs e)
    {
        InvalidateWorldCacheRecursive();
        if (_suppressTransformChanged)
        {
            return;
        }

        _rotationDegrees = Transform.LocalRotation.ToEulerDegrees();
        OnPropertyChanged(nameof(RotationDegrees));
        OnPropertyChanged(nameof(Rotation));

        OnPropertyChanged(nameof(Transform));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(LocalMatrix));
        RaiseChanged(SceneChangeKind.Transform);
    }

    private SceneAccessLease3D EnterMaterialMutationScope()
        => _ownerScene?.EnterMutationScope(nameof(Material3D)) ?? default;

    private void OnMaterialChanged(object? sender, EventArgs e)
    {
        _fill = _material.EffectiveColor;
        _materialVersion++;
        OnPropertyChanged(nameof(Material));
        OnPropertyChanged(nameof(Fill));
        OnPropertyChanged(nameof(Color));
        RaiseChanged(SceneChangeKind.Material);
    }

    protected void MarkGeometryDirty([CallerMemberName] string? propertyName = null)
    {
        _meshDirty = true;
        MarkWorldBoundsDirtyRecursive();
        OnPropertyChanged(propertyName);
        RaiseChanged(SceneChangeKind.Geometry);
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        using var access = EnterMutationScope();
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        RaiseChanged(SceneChangeKind.Metadata);
        return true;
    }

    private SceneAccessLease3D EnterMutationScope()
        => _ownerScene?.EnterMutationScope() ?? default;

    /// <summary>Same-assembly derived systems use this to preserve world ownership.</summary>
    private protected SceneAccessLease3D EnterOwnedMutationScope() => EnterMutationScope();

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void RaiseChanged(SceneChangeKind kind = SceneChangeKind.Unknown, Object3D? source = null)
    {
        Changed?.Invoke(this, new Object3DChangedEventArgs(kind, source ?? this));
    }

    public void RaiseClicked(ScenePointerEventArgs e) => Clicked?.Invoke(this, e);
    public void RaisePointerEntered(ScenePointerEventArgs e) => PointerEntered?.Invoke(this, e);
    public void RaisePointerExited(ScenePointerEventArgs e) => PointerExited?.Invoke(this, e);
    public void RaisePointerMoved(ScenePointerEventArgs e) => PointerMoved?.Invoke(this, e);
    public void RaisePointerPressed(ScenePointerEventArgs e) => PointerPressed?.Invoke(this, e);
    public void RaisePointerReleased(ScenePointerEventArgs e) => PointerReleased?.Invoke(this, e);
}
