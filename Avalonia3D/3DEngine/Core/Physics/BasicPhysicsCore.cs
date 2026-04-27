using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Physics;

public sealed class BasicPhysicsCore : IPhysicsCore
{
    public Vector3 Gravity { get; set; } = new Vector3(0f, -9.81f, 0f);
    public int SolverIterations { get; set; } = 2;

    public void Step(Scene3D scene, float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return;
        }

        deltaSeconds = MathF.Min(deltaSeconds, 1f / 15f);
        var staticObjects = scene.Registry.StaticColliders;
        var dynamicObjects = scene.Registry.DynamicBodies;

        foreach (var obj in dynamicObjects)
        {
            Integrate(obj, deltaSeconds);
        }

        var iterations = System.Math.Max(1, SolverIterations);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            foreach (var obj in dynamicObjects)
            {
                ResolveAgainstWorld(scene, obj, staticObjects);
            }

            for (var i = 0; i < dynamicObjects.Count; i++)
            {
                for (var j = i + 1; j < dynamicObjects.Count; j++)
                {
                    ResolveDynamicPair(dynamicObjects[i], dynamicObjects[j]);
                }
            }
        }
    }

    private void Integrate(Object3D obj, float deltaSeconds)
    {
        var body = obj.Rigidbody;
        if (body is null || body.IsKinematic)
        {
            return;
        }

        body.IsGrounded = false;
        if (body.UseGravity)
        {
            body.Velocity += Gravity * deltaSeconds;
        }

        if (body.LinearDamping > 0f)
        {
            body.Velocity *= MathF.Max(0f, 1f - body.LinearDamping * deltaSeconds);
        }

        obj.Position += body.Velocity * deltaSeconds;
    }

    private static void ResolveAgainstWorld(Scene3D scene, Object3D obj, IReadOnlyList<Object3D> staticObjects)
    {
        if (obj.Collider is null)
        {
            return;
        }

        var bounds = obj.Collider.GetWorldBounds(obj);
        var candidates = scene.Registry.ColliderIndex.QueryBounds(bounds);
        var objects = candidates.Count == 0 ? staticObjects : candidates;
        foreach (var other in objects)
        {
            if (ReferenceEquals(obj, other) || other.Collider is null)
            {
                continue;
            }

            if (!TryGetAabbPenetration(bounds, other.Collider.GetWorldBounds(other), out var correction, out var normal))
            {
                continue;
            }

            obj.Position += correction;
            ApplyCollisionVelocity(obj.Rigidbody, normal);
            bounds = obj.Collider.GetWorldBounds(obj);
        }
    }

    private static void ResolveDynamicPair(Object3D a, Object3D b)
    {
        if (a.Collider is null || b.Collider is null || a.Rigidbody is null || b.Rigidbody is null)
        {
            return;
        }

        if (!TryGetAabbPenetration(a.Collider.GetWorldBounds(a), b.Collider.GetWorldBounds(b), out var correction, out var normal))
        {
            return;
        }

        var invMassA = GetInverseMass(a.Rigidbody);
        var invMassB = GetInverseMass(b.Rigidbody);
        var total = invMassA + invMassB;
        if (total <= 0f)
        {
            return;
        }

        a.Position += correction * (invMassA / total);
        b.Position -= correction * (invMassB / total);
        ApplyCollisionVelocity(a.Rigidbody, normal);
        ApplyCollisionVelocity(b.Rigidbody, -normal);
    }

    public static bool TryGetAabbPenetration(Bounds3D a, Bounds3D b, out Vector3 correction, out Vector3 normal)
    {
        correction = Vector3.Zero;
        normal = Vector3.Zero;
        if (!a.IsValid || !b.IsValid || !a.Intersects(b))
        {
            return false;
        }

        var moveRight = b.Max.X - a.Min.X;
        var moveLeft = b.Min.X - a.Max.X;
        var moveUp = b.Max.Y - a.Min.Y;
        var moveDown = b.Min.Y - a.Max.Y;
        var moveForward = b.Max.Z - a.Min.Z;
        var moveBack = b.Min.Z - a.Max.Z;

        var candidates = new[]
        {
            (Abs: MathF.Abs(moveRight), Correction: new Vector3(moveRight, 0f, 0f), Normal: Vector3.UnitX),
            (Abs: MathF.Abs(moveLeft), Correction: new Vector3(moveLeft, 0f, 0f), Normal: -Vector3.UnitX),
            (Abs: MathF.Abs(moveUp), Correction: new Vector3(0f, moveUp, 0f), Normal: Vector3.UnitY),
            (Abs: MathF.Abs(moveDown), Correction: new Vector3(0f, moveDown, 0f), Normal: -Vector3.UnitY),
            (Abs: MathF.Abs(moveForward), Correction: new Vector3(0f, 0f, moveForward), Normal: Vector3.UnitZ),
            (Abs: MathF.Abs(moveBack), Correction: new Vector3(0f, 0f, moveBack), Normal: -Vector3.UnitZ)
        };

        var best = candidates[0];
        for (var i = 1; i < candidates.Length; i++)
        {
            if (candidates[i].Abs < best.Abs)
            {
                best = candidates[i];
            }
        }

        correction = best.Correction;
        normal = best.Normal;
        return true;
    }

    private static void ApplyCollisionVelocity(Rigidbody3D? body, Vector3 normal)
    {
        if (body is null)
        {
            return;
        }

        if (normal.Y > 0.5f)
        {
            body.IsGrounded = true;
        }

        var normalVelocity = Vector3.Dot(body.Velocity, normal);
        if (normalVelocity >= 0f)
        {
            return;
        }

        var restitution = System.Math.Clamp(body.Restitution, 0f, 1f);
        body.Velocity -= normal * normalVelocity * (1f + restitution);

        if (normal.Y > 0.5f)
        {
            body.Velocity = new Vector3(body.Velocity.X * (1f - System.Math.Clamp(body.Friction, 0f, 1f) * 0.08f), body.Velocity.Y, body.Velocity.Z * (1f - System.Math.Clamp(body.Friction, 0f, 1f) * 0.08f));
        }
    }


    public bool Raycast(Scene3D scene, Ray ray, out RaycastHit3D hit)
        => scene.Collisions.Raycast(scene, ray, out hit);

    public IReadOnlyList<RaycastHit3D> RaycastAll(Scene3D scene, Ray ray)
        => scene.Collisions.RaycastAll(scene, ray);

    private static float GetInverseMass(Rigidbody3D body)
    {
        if (body.IsKinematic || body.Mass <= 0f)
        {
            return 0f;
        }

        return 1f / body.Mass;
    }
}
