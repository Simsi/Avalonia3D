using System.Numerics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Collision;

public readonly struct RaycastHit3D
{
    public RaycastHit3D(Object3D obj, Vector3 point, Vector3 normal, float distance)
    {
        Object = obj;
        Point = point;
        Normal = normal;
        Distance = distance;
    }

    public Object3D Object { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public float Distance { get; }
}
