using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Scene;

public class Rectangle3D : Object3D
{
    private float _width = 1f;
    private float _height = 1f;
    private float _depth = 0.1f;

    public Rectangle3D()
    {
        Collider = new BoxCollider3D { Size = new Vector3(_width, _height, _depth) };
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

    protected override Mesh3D BuildMesh() => GetOrCreateCachedMesh(
        MeshResourceKey.Box(Width, Height, Depth),
        () => MeshFactory.CreateExtrudedRectangle(Width, Height, Depth));

    private void UpdateColliderSize()
    {
        if (Collider is BoxCollider3D box)
        {
            box.Size = new Vector3(_width, _height, _depth);
        }
    }
}
