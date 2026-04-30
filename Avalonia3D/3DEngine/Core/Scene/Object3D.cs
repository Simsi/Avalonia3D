using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Transforms;

namespace ThreeDEngine.Core.Scene;

public abstract class Object3D : INotifyPropertyChanged
{
    private string _name = "Object3D";
    private Vector3 _rotationDegrees;
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

    protected Object3D()
    {
        Id = Guid.NewGuid().ToString("N");
        Transform = new Transform3D();
        Transform.Changed += OnTransformChanged;
        _material.Changed += OnMaterialChanged;
    }

    public string Id { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    public event EventHandler<Object3DChangedEventArgs>? ChangedDetailed;
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
        set => SetField(ref _name, value, SceneChangeKind.Debug);
    }

    public object? DataContext
    {
        get => _dataContext;
        set => SetField(ref _dataContext, value, SceneChangeKind.Debug);
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
            if (_rotationDegrees == value)
            {
                return;
            }

            _rotationDegrees = value;
            Transform.SetEulerDegrees(value);
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
            RaiseChanged(SceneChangeKind.Material, nameof(Fill));
        }
    }

    public Material3D Material
    {
        get => _material;
        set
        {
            if (ReferenceEquals(_material, value))
            {
                return;
            }

            if (_material is not null)
            {
                _material.Changed -= OnMaterialChanged;
            }

            _material = value ?? throw new ArgumentNullException(nameof(value));
            _material.Changed += OnMaterialChanged;
            _fill = _material.EffectiveColor;
            _materialVersion++;
            OnPropertyChanged(nameof(Material));
            OnPropertyChanged(nameof(Fill));
            OnPropertyChanged(nameof(Color));
            RaiseChanged(SceneChangeKind.Material, nameof(Material));
        }
    }

    public Collider3D? Collider
    {
        get => _collider;
        set
        {
            if (ReferenceEquals(_collider, value))
            {
                return;
            }

            if (_collider is not null)
            {
                _collider.Owner = null;
            }

            _collider = value;
            if (_collider is not null)
            {
                _collider.Owner = this;
            }

            MarkWorldBoundsDirtyRecursive();
            OnPropertyChanged(nameof(Collider));
            RaiseChanged(SceneChangeKind.Collider, nameof(Collider));
        }
    }

    public Rigidbody3D? Rigidbody
    {
        get => _rigidbody;
        set => SetField(ref _rigidbody, value, SceneChangeKind.Rigidbody);
    }

    public ColorRgba Color
    {
        get => Fill;
        set => Fill = value;
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value, SceneChangeKind.Visibility);
    }

    public bool IsPickable
    {
        get => _isPickable;
        set => SetField(ref _isPickable, value, SceneChangeKind.Picking);
    }

    public virtual bool IsHovered
    {
        get => _isHovered;
        set => SetField(ref _isHovered, value, SceneChangeKind.DebugVisual);
    }

    public virtual bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value, SceneChangeKind.Selection);
    }

    public bool IsEffectivelyHovered => IsHovered || (Parent?.IsEffectivelyHovered ?? false);

    public bool IsEffectivelySelected => IsSelected || (Parent?.IsEffectivelySelected ?? false);

    public bool IsManipulationEnabled
    {
        get => _isManipulationEnabled;
        set => SetField(ref _isManipulationEnabled, value, SceneChangeKind.Picking);
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
        OnPropertyChanged(nameof(Transform));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(RotationDegrees));
        OnPropertyChanged(nameof(Rotation));
        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(LocalMatrix));
        RaiseChanged(SceneChangeKind.Transform, nameof(Transform));
    }

    private void OnMaterialChanged(object? sender, EventArgs e)
    {
        _fill = _material.EffectiveColor;
        _materialVersion++;
        OnPropertyChanged(nameof(Material));
        OnPropertyChanged(nameof(Fill));
        OnPropertyChanged(nameof(Color));
        RaiseChanged(SceneChangeKind.Material, nameof(Material));
    }

    protected void MarkGeometryDirty([CallerMemberName] string? propertyName = null)
    {
        _meshDirty = true;
        MarkWorldBoundsDirtyRecursive();
        OnPropertyChanged(propertyName);
        RaiseChanged(SceneChangeKind.Geometry, propertyName);
    }

    protected bool SetField<T>(ref T field, T value, SceneChangeKind kind = SceneChangeKind.Unknown, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        RaiseChanged(kind, propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void RaiseChanged(SceneChangeKind kind = SceneChangeKind.Unknown, string? propertyName = null)
    {
        var args = new Object3DChangedEventArgs(this, kind, propertyName);
        ChangedDetailed?.Invoke(this, args);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseClicked(ScenePointerEventArgs e) => Clicked?.Invoke(this, e);
    public void RaisePointerEntered(ScenePointerEventArgs e) => PointerEntered?.Invoke(this, e);
    public void RaisePointerExited(ScenePointerEventArgs e) => PointerExited?.Invoke(this, e);
    public void RaisePointerMoved(ScenePointerEventArgs e) => PointerMoved?.Invoke(this, e);
    public void RaisePointerPressed(ScenePointerEventArgs e) => PointerPressed?.Invoke(this, e);
    public void RaisePointerReleased(ScenePointerEventArgs e) => PointerReleased?.Invoke(this, e);
}
