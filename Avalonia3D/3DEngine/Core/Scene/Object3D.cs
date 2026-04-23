using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Scene;

public abstract class Object3D : INotifyPropertyChanged
{
    private string _name = "Object3D";
    private Vector3 _position;
    private Vector3 _rotationDegrees;
    private Vector3 _scale = Vector3.One;
    private ColorRgba _fill = ColorRgba.White;
    private bool _isVisible = true;
    private bool _isHovered;
    private bool _isSelected;
    private Mesh3D? _mesh;
    private bool _meshDirty = true;
    private int _geometryVersion;

    protected Object3D()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    public string Id { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    public event EventHandler<ScenePointerEventArgs>? Clicked;
    public event EventHandler<ScenePointerEventArgs>? PointerEntered;
    public event EventHandler<ScenePointerEventArgs>? PointerExited;
    public event EventHandler<ScenePointerEventArgs>? PointerMoved;
    public event EventHandler<ScenePointerEventArgs>? PointerPressed;
    public event EventHandler<ScenePointerEventArgs>? PointerReleased;

    public virtual bool UseMeshRendering => true;
    public virtual bool UseScenePicking => true;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public Vector3 Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }

    public Vector3 RotationDegrees
    {
        get => _rotationDegrees;
        set => SetField(ref _rotationDegrees, value);
    }

    public Vector3 Rotation
    {
        get => RotationDegrees;
        set => RotationDegrees = value;
    }

    public Vector3 Scale
    {
        get => _scale;
        set => SetField(ref _scale, value);
    }

    public ColorRgba Fill
    {
        get => _fill;
        set => SetField(ref _fill, value);
    }

    public ColorRgba Color
    {
        get => Fill;
        set => Fill = value;
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public bool IsHovered
    {
        get => _isHovered;
        set => SetField(ref _isHovered, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public int GeometryVersion => _geometryVersion;

    public Mesh3D GetMesh()
    {
        if (_meshDirty || _mesh is null)
        {
            _mesh = BuildMesh();
            _meshDirty = false;
            _geometryVersion++;
        }

        return _mesh;
    }

    public virtual Matrix4x4 GetModelMatrix()
    {
        var radians = RotationDegrees * (System.MathF.PI / 180f);
        return Matrix4x4.CreateScale(Scale)
             * Matrix4x4.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z)
             * Matrix4x4.CreateTranslation(Position);
    }

    protected abstract Mesh3D BuildMesh();

    protected void MarkGeometryDirty([CallerMemberName] string? propertyName = null)
    {
        _meshDirty = true;
        OnPropertyChanged(propertyName);
        RaiseChanged();
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        RaiseChanged();
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseClicked(ScenePointerEventArgs e) => Clicked?.Invoke(this, e);
    public void RaisePointerEntered(ScenePointerEventArgs e) => PointerEntered?.Invoke(this, e);
    public void RaisePointerExited(ScenePointerEventArgs e) => PointerExited?.Invoke(this, e);
    public void RaisePointerMoved(ScenePointerEventArgs e) => PointerMoved?.Invoke(this, e);
    public void RaisePointerPressed(ScenePointerEventArgs e) => PointerPressed?.Invoke(this, e);
    public void RaisePointerReleased(ScenePointerEventArgs e) => PointerReleased?.Invoke(this, e);
}
