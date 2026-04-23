using System.Linq;
using System.Numerics;

namespace ThreeDEngine.Core.Geometry;

public sealed class Mesh3D
{
    public Mesh3D(Vector3[] positions, Vector3[] normals, int[] indices)
    {
        Positions = positions;
        Normals = normals;
        Indices = indices;
        BoundingRadius = Positions.Length == 0
            ? 0f
            : Positions.Max(static p => p.Length());
    }

    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public int[] Indices { get; }
    public float BoundingRadius { get; }
}
