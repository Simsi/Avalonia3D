using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Collision;

public abstract class Collider3D
{
    public Object3D? Owner { get; internal set; }
    public abstract Bounds3D GetWorldBounds(Object3D owner);
    public abstract bool Raycast(Object3D owner, Ray ray, out RaycastHit3D hit);
}
