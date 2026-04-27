using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Scene;

public sealed class Cone3D : Object3D
{
    private float _radius = 0.5f;
    private float _height = 1f;
    private int _segments = 32;

    public Cone3D()
    {
        Name = "Cone";
        Collider = new BoxCollider3D { Size = new Vector3(_radius * 2f, _height, _radius * 2f) };
    }

    public float Radius
    {
        get => _radius;
        set
        {
            var clamped = System.MathF.Max(value, 0.001f);
            if (System.MathF.Abs(_radius - clamped) < 0.0001f) return;
            _radius = clamped;
            UpdateCollider();
            MarkGeometryDirty();
        }
    }

    public float Height
    {
        get => _height;
        set
        {
            var clamped = System.MathF.Max(value, 0.001f);
            if (System.MathF.Abs(_height - clamped) < 0.0001f) return;
            _height = clamped;
            UpdateCollider();
            MarkGeometryDirty();
        }
    }

    public int Segments
    {
        get => _segments;
        set
        {
            var clamped = System.Math.Max(12, value);
            if (_segments == clamped) return;
            _segments = clamped;
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh() => MeshCache3D.Shared.GetOrCreate(
        MeshResourceKey.Cone(Radius, Height, Segments),
        () => MeshFactory.CreateCone(Radius, Height, Segments));

    private void UpdateCollider()
    {
        if (Collider is BoxCollider3D box)
        {
            box.Size = new Vector3(_radius * 2f, _height, _radius * 2f);
        }
    }
}
