using System;

namespace ThreeDEngine.Core.Geometry;

[Flags]
public enum GeometryStreamMask3D
{
    None = 0,
    Normals = 1 << 0,
    TexCoords0 = 1 << 1,
    Colors0 = 1 << 2,
    Tangents = 1 << 3,
    MaterialSlots = 1 << 4,
    SkinWeights = 1 << 5
}
