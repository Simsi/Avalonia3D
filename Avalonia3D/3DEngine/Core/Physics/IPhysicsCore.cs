using System.Collections.Generic;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Physics;

public interface IPhysicsCore
{
    void Step(Scene3D scene, float deltaSeconds);

    bool Raycast(Scene3D scene, Ray ray, out RaycastHit3D hit);

    IReadOnlyList<RaycastHit3D> RaycastAll(Scene3D scene, Ray ray);
}
