using System.Collections.Generic;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Collision;

public sealed class CollisionWorld3D
{
    public bool Raycast(Scene3D scene, Ray ray, out RaycastHit3D closestHit)
    {
        closestHit = default;
        var hasHit = false;
        var bestDistance = float.MaxValue;
        var candidates = scene.Registry.ColliderIndex.QueryRay(ray);
        var objects = candidates.Count == 0 ? scene.Registry.Colliders : candidates;

        foreach (var obj in objects)
        {
            if (obj.Collider is null || !obj.Collider.Raycast(obj, ray, out var hit) || hit.Distance >= bestDistance)
            {
                continue;
            }

            closestHit = hit;
            bestDistance = hit.Distance;
            hasHit = true;
        }

        return hasHit;
    }

    public IReadOnlyList<RaycastHit3D> RaycastAll(Scene3D scene, Ray ray)
    {
        var hits = new List<RaycastHit3D>();
        var candidates = scene.Registry.ColliderIndex.QueryRay(ray);
        var objects = candidates.Count == 0 ? scene.Registry.Colliders : candidates;
        foreach (var obj in objects)
        {
            if (obj.Collider is not null && obj.Collider.Raycast(obj, ray, out var hit))
            {
                hits.Add(hit);
            }
        }

        hits.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return hits;
    }

    public bool Intersects(Object3D a, Object3D b)
    {
        if (a.Collider is null || b.Collider is null)
        {
            return false;
        }

        return a.Collider.GetWorldBounds(a).Intersects(b.Collider.GetWorldBounds(b));
    }
}
