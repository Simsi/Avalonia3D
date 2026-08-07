using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Validation;

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
            var clamped = Guard3D.Positive(value, nameof(Radius));
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
            var clamped = value >= 3 ? value : throw new ArgumentOutOfRangeException(nameof(Segments), value, "Sphere segments must be at least 3.");
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
            var clamped = value >= 2 ? value : throw new ArgumentOutOfRangeException(nameof(Rings), value, "Sphere rings must be at least 2.");
            if (_rings == clamped) return;
            _rings = clamped;
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh() => GetOrCreateCachedMesh(
        MeshResourceKey.Sphere(Radius, Segments, Rings),
        () => MeshFactory.CreateSphere(Radius, Segments, Rings));
}
