using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Spatial;

namespace ThreeDEngine.Core.Collision;

public sealed class CollisionWorld3D
{
    [ThreadStatic]
    private static SpatialQueryScratch3D? _queryScratch;

    public bool Raycast(Scene3D scene, Ray ray, out RaycastHit3D closestHit)
    {
        ArgumentNullException.ThrowIfNull(scene);
        closestHit = default;
        var hasHit = false;
        var bestDistance = float.MaxValue;
        var scratch = _queryScratch ??= new SpatialQueryScratch3D();
        var objects = scene.Registry.ColliderIndex.QueryRay(ray, scratch);

        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj.Collider is null || !obj.Collider.Raycast(obj, ray, out var hit) || hit.Distance >= bestDistance) continue;
            closestHit = hit;
            bestDistance = hit.Distance;
            hasHit = true;
        }
        return hasHit;
    }

    public IReadOnlyList<RaycastHit3D> RaycastAll(Scene3D scene, Ray ray)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var hits = new List<RaycastHit3D>();
        var scratch = _queryScratch ??= new SpatialQueryScratch3D();
        var objects = scene.Registry.ColliderIndex.QueryRay(ray, scratch);
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj.Collider is not null && obj.Collider.Raycast(obj, ray, out var hit)) hits.Add(hit);
        }
        hits.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return hits;
    }

    /// <summary>Allocation-free broadphase batch query. Results correspond one-to-one with input rays.</summary>
    public void RaycastBatch(Scene3D scene, ReadOnlySpan<Ray> rays, Span<RaycastHit3D?> results)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (results.Length < rays.Length) throw new ArgumentException("Result span is shorter than the ray span.", nameof(results));
        for (var i = 0; i < rays.Length; i++)
        {
            results[i] = Raycast(scene, rays[i], out var hit) ? hit : null;
        }
    }

    public bool Intersects(Object3D a, Object3D b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Collider is not null && b.Collider is not null &&
               a.Collider.GetWorldBounds(a).Intersects(b.Collider.GetWorldBounds(b));
    }
}
