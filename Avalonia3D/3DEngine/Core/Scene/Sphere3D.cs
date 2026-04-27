using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Scene;

public sealed class Sphere3D : Object3D
{
    private float _radius = 0.5f;
    private int _segments = 32;
    private int _rings = 16;

    public Sphere3D()
    {
        Name = "Sphere";
        Collider = new SphereCollider3D { Radius = _radius };
    }

    public float Radius
    {
        get => _radius;
        set
        {
            var clamped = System.MathF.Max(value, 0.001f);
            if (System.MathF.Abs(_radius - clamped) < 0.0001f) return;
            _radius = clamped;
            if (Collider is SphereCollider3D sphere) sphere.Radius = _radius;
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

    public int Rings
    {
        get => _rings;
        set
        {
            var clamped = System.Math.Max(6, value);
            if (_rings == clamped) return;
            _rings = clamped;
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh() => MeshCache3D.Shared.GetOrCreate(
        MeshResourceKey.Sphere(Radius, Segments, Rings),
        () => MeshFactory.CreateSphere(Radius, Segments, Rings));
}
