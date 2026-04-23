using System;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Scene;

public sealed class Rectangle3D : Object3D
{
    private float _width = 1f;
    private float _height = 1f;
    private float _depth = 0.1f;

    public float Width
    {
        get => _width;
        set
        {
            value = System.MathF.Max(value, 0.01f);
            if (System.MathF.Abs(_width - value) < float.Epsilon) return;
            _width = value;
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
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh() => MeshFactory.CreateExtrudedRectangle(Width, Height, Depth);
}
