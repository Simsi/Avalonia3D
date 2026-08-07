using System;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Scene;

public sealed class Ellipse3D : Object3D
{
    private float _width = 1f;
    private float _height = 1f;
    private float _depth = 0.1f;
    private int _segments = 48;

    public Ellipse3D()
    {
        Collider = new SphereCollider3D { Radius = 0.5f };
    }

    public float Width
    {
        get => _width;
        set
        {
            value = Guard3D.Positive(value, nameof(Width));
            if (System.MathF.Abs(_width - value) < float.Epsilon) return;
            _width = value;
            UpdateColliderSize();
            MarkGeometryDirty();
        }
    }

    public float Height
    {
        get => _height;
        set
        {
            value = Guard3D.Positive(value, nameof(Height));
            if (System.MathF.Abs(_height - value) < float.Epsilon) return;
            _height = value;
            UpdateColliderSize();
            MarkGeometryDirty();
        }
    }

    public float RadiusX
    {
        get => Width * 0.5f;
        set => Width = Guard3D.Positive(value, nameof(RadiusX)) * 2f;
    }

    public float RadiusY
    {
        get => Height * 0.5f;
        set => Height = Guard3D.Positive(value, nameof(RadiusY)) * 2f;
    }

    public float Depth
    {
        get => _depth;
        set
        {
            value = Guard3D.Positive(value, nameof(Depth));
            if (System.MathF.Abs(_depth - value) < float.Epsilon) return;
            _depth = value;
            UpdateColliderSize();
            MarkGeometryDirty();
        }
    }

    public int Segments
    {
        get => _segments;
        set
        {
            value = value >= 12 ? value : throw new ArgumentOutOfRangeException(nameof(Segments), value, "Ellipse segments must be at least 12.");
            if (_segments == value) return;
            _segments = value;
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh() => GetOrCreateCachedMesh(
        MeshResourceKey.Ellipse(Width, Height, Depth, Segments),
        () => MeshFactory.CreateExtrudedEllipse(Width, Height, Depth, Segments));

    private void UpdateColliderSize()
    {
        if (Collider is SphereCollider3D sphere)
        {
            sphere.Radius = System.MathF.Max(System.MathF.Max(_width, _height), _depth) * 0.5f;
        }
    }
}
