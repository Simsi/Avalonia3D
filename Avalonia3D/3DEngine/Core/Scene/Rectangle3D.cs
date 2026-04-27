using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;

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
            value = System.MathF.Max(value, 0.01f);
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
            value = System.MathF.Max(value, 0.01f);
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
            value = System.MathF.Max(value, 0.001f);
            if (System.MathF.Abs(_depth - value) < float.Epsilon) return;
            _depth = value;
            UpdateColliderSize();
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh() => MeshCache3D.Shared.GetOrCreate(
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
