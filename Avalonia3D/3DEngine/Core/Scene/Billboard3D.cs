using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Camera-facing quad primitive. Rendering backends can treat it as a normal mesh today and
/// later upgrade it to a shader-owned billboard path when capabilities allow it.
/// </summary>
public sealed class Billboard3D : Object3D
{
    private float _width = 1f;
    private float _height = 1f;

    public Billboard3D()
    {
        Name = "Billboard";
    }

    public float Width
    {
        get => _width;
        set
        {
            var clamped = Guard3D.Positive(value, nameof(Width));
            if (System.MathF.Abs(_width - clamped) < 0.0001f) return;
            _width = clamped;
            MarkGeometryDirty();
        }
    }

    public float Height
    {
        get => _height;
        set
        {
            var clamped = Guard3D.Positive(value, nameof(Height));
            if (System.MathF.Abs(_height - clamped) < 0.0001f) return;
            _height = clamped;
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh() => MeshFactory.CreateBillboardQuad(Width, Height);
}
