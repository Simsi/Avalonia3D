using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Scene;

public sealed class Plane3D : Object3D
{
    private float _width = 10f;
    private float _height = 10f;
    private int _segmentsX = 1;
    private int _segmentsY = 1;

    public Plane3D()
    {
        Name = "Plane";
        IsManipulationEnabled = false;
        Collider = new PlaneCollider3D { Size = new Vector2(_width, _height) };
    }

    public float Width
    {
        get => _width;
        set
        {
            var clamped = System.MathF.Max(0.001f, value);
            if (System.MathF.Abs(_width - clamped) < 0.0001f) return;
            _width = clamped;
            UpdateColliderSize();
            MarkGeometryDirty();
        }
    }

    public float Height
    {
        get => _height;
        set
        {
            var clamped = System.MathF.Max(0.001f, value);
            if (System.MathF.Abs(_height - clamped) < 0.0001f) return;
            _height = clamped;
            UpdateColliderSize();
            MarkGeometryDirty();
        }
    }

    public int SegmentsX
    {
        get => _segmentsX;
        set
        {
            var clamped = System.Math.Max(1, value);
            if (_segmentsX == clamped) return;
            _segmentsX = clamped;
            MarkGeometryDirty();
        }
    }

    public int SegmentsY
    {
        get => _segmentsY;
        set
        {
            var clamped = System.Math.Max(1, value);
            if (_segmentsY == clamped) return;
            _segmentsY = clamped;
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh() => MeshCache3D.Shared.GetOrCreate(
        MeshResourceKey.Plane(Width, Height, SegmentsX, SegmentsY),
        () => MeshFactory.CreatePlane(Width, Height, SegmentsX, SegmentsY));

    private void UpdateColliderSize()
    {
        if (Collider is PlaneCollider3D plane)
        {
            plane.Size = new Vector2(_width, _height);
        }
    }
}
