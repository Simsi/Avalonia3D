using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Physics.Kinematic;

public sealed class KinematicCharacterController3D
{
    public float Radius { get; set; } = 0.28f;
    public float Height { get; set; } = 1.72f;
    public float StepHeight { get; set; } = 0.32f;
    public float SlopeLimitDegrees { get; set; } = 52f;
    public float GroundSnapDistance { get; set; } = 0.08f;
    public Vector3 Gravity { get; set; } = new(0f, -9.81f, 0f);
    public Vector3 Velocity { get; set; }
    public bool IsGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.UnitY;

    public void Reset(Vector3? velocity = null, bool grounded = false)
    {
        Velocity = velocity ?? Vector3.Zero;
        IsGrounded = grounded;
        GroundNormal = Vector3.UnitY;
    }

    public Vector3 Move(Scene3D scene, Vector3 footPosition, Vector3 desiredHorizontalMotion, float deltaSeconds)
    {
        if (scene is null || deltaSeconds <= 0f) return footPosition;
        deltaSeconds = MathF.Min(deltaSeconds, 1f / 15f);

        Radius = MathF.Max(0.01f, Radius);
        Height = MathF.Max(Radius * 2f, Height);
        StepHeight = MathF.Max(0f, StepHeight);

        if (!IsGrounded)
        {
            Velocity += Gravity * deltaSeconds;
        }
        else if (Velocity.Y < 0f)
        {
            Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
        }

        var result = footPosition;
        var horizontal = new Vector3(desiredHorizontalMotion.X, 0f, desiredHorizontalMotion.Z);
        result = MoveHorizontal(scene, result, horizontal);
        result = MoveVertical(scene, result, Velocity.Y * deltaSeconds);
        SnapToGround(scene, ref result);
        return result;
    }

    public void Jump(float speed)
    {
        if (!IsGrounded) return;
        Velocity = new Vector3(Velocity.X, MathF.Max(0f, speed), Velocity.Z);
        IsGrounded = false;
    }

    private Vector3 MoveHorizontal(Scene3D scene, Vector3 position, Vector3 motion)
    {
        if (motion.LengthSquared() < 0.0000001f) return position;

        // Axis-separated sweeps are intentionally conservative. They are stable for
        // building walkthroughs and digital-twin navigation even though they are not
        // a full rigidbody collision solver.
        var result = MoveAxis(scene, position, new Vector3(motion.X, 0f, 0f));
        result = MoveAxis(scene, result, new Vector3(0f, 0f, motion.Z));
        return result;
    }

    private Vector3 MoveAxis(Scene3D scene, Vector3 position, Vector3 motion)
    {
        if (motion.LengthSquared() < 0.0000001f) return position;
        var next = position + motion;
        var bounds = GetBounds(next);
        foreach (var obj in GetStaticColliders(scene, bounds))
        {
            if (obj.Collider is null) continue;
            var other = obj.Collider.GetWorldBounds(obj);
            if (!other.IsValid || !bounds.Intersects(other)) continue;

            if (TryStepOver(scene, position, motion, other, out var stepped))
            {
                next = stepped;
                bounds = GetBounds(next);
                continue;
            }

            if (motion.X > 0f) next.X = other.Min.X - Radius;
            else if (motion.X < 0f) next.X = other.Max.X + Radius;
            if (motion.Z > 0f) next.Z = other.Min.Z - Radius;
            else if (motion.Z < 0f) next.Z = other.Max.Z + Radius;

            // Remove the blocked component from velocity so a wall contact cannot
            // accumulate horizontal energy over repeated navigation ticks.
            if (MathF.Abs(motion.X) > 0f) Velocity = new Vector3(0f, Velocity.Y, Velocity.Z);
            if (MathF.Abs(motion.Z) > 0f) Velocity = new Vector3(Velocity.X, Velocity.Y, 0f);
            bounds = GetBounds(next);
        }
        return next;
    }

    private bool TryStepOver(Scene3D scene, Vector3 position, Vector3 motion, Bounds3D blockingBounds, out Vector3 stepped)
    {
        stepped = position;
        if (!IsGrounded || StepHeight <= 0.0001f) return false;

        var currentBounds = GetBounds(position);
        var obstacleHeight = blockingBounds.Max.Y - currentBounds.Min.Y;
        if (obstacleHeight < -0.02f || obstacleHeight > StepHeight + 0.02f) return false;

        var raised = position + new Vector3(0f, StepHeight, 0f);
        var horizontal = raised + motion;
        var testBounds = GetBounds(horizontal);
        foreach (var obj in GetStaticColliders(scene, testBounds))
        {
            if (obj.Collider is null) continue;
            var other = obj.Collider.GetWorldBounds(obj);
            if (!other.IsValid) continue;
            if (testBounds.Intersects(other)) return false;
        }

        stepped = horizontal;
        SnapToGround(scene, ref stepped);
        return true;
    }

    private Vector3 MoveVertical(Scene3D scene, Vector3 position, float deltaY)
    {
        IsGrounded = false;
        GroundNormal = Vector3.UnitY;
        if (MathF.Abs(deltaY) < 0.0000001f) return position;
        var next = position + new Vector3(0f, deltaY, 0f);
        var bounds = GetBounds(next);
        foreach (var obj in GetStaticColliders(scene, bounds))
        {
            if (obj.Collider is null) continue;
            var other = obj.Collider.GetWorldBounds(obj);
            if (!other.IsValid || !bounds.Intersects(other)) continue;
            if (deltaY < 0f)
            {
                next.Y = other.Max.Y;
                Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
                IsGrounded = true;
                GroundNormal = Vector3.UnitY;
            }
            else
            {
                next.Y = other.Min.Y - Height;
                if (Velocity.Y > 0f) Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
            }
            bounds = GetBounds(next);
        }
        return next;
    }

    private void SnapToGround(Scene3D scene, ref Vector3 position)
    {
        var snap = MathF.Max(GroundSnapDistance, MathF.Min(0.2f, StepHeight + 0.02f));
        if (TryProbeGround(scene, position, snap, out var groundY, out var groundNormal))
        {
            position.Y = groundY;
            Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
            IsGrounded = true;
            GroundNormal = groundNormal;
            return;
        }

        var probe = GetBounds(position + new Vector3(0f, -snap, 0f));
        var bestY = float.NegativeInfinity;
        foreach (var obj in GetStaticColliders(scene, probe))
        {
            if (obj.Collider is null) continue;
            var other = obj.Collider.GetWorldBounds(obj);
            if (!other.IsValid) continue;
            var horizontal = !(probe.Max.X < other.Min.X || probe.Min.X > other.Max.X || probe.Max.Z < other.Min.Z || probe.Min.Z > other.Max.Z);
            if (!horizontal) continue;
            if (other.Max.Y <= position.Y + 0.05f && other.Max.Y >= position.Y - snap && other.Max.Y > bestY)
            {
                bestY = other.Max.Y;
            }
        }

        if (!float.IsNegativeInfinity(bestY))
        {
            position.Y = bestY;
            Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
            IsGrounded = true;
            GroundNormal = Vector3.UnitY;
        }
    }

    private bool TryProbeGround(Scene3D scene, Vector3 footPosition, float snap, out float groundY, out Vector3 groundNormal)
    {
        groundY = 0f;
        groundNormal = Vector3.UnitY;
        var origin = footPosition + new Vector3(0f, MathF.Max(0.05f, StepHeight + 0.05f), 0f);
        var maxDistance = MathF.Max(0.05f, StepHeight + snap + 0.08f);
        var ray = new Ray(origin, -Vector3.UnitY);
        var probeBounds = GetBounds(footPosition + new Vector3(0f, -snap, 0f));
        var bestDistance = float.PositiveInfinity;
        var minUp = MathF.Cos(global::System.Math.Clamp(SlopeLimitDegrees, 0f, 89.9f) * MathF.PI / 180f);

        if (scene.PhysicsCore is not null)
        {
            foreach (var hit in scene.PhysicsCore.RaycastAll(scene, ray))
            {
                if (hit.Distance < 0f || hit.Distance > maxDistance || hit.Distance >= bestDistance) continue;
                var n = hit.Normal.LengthSquared() > 0.000001f ? Vector3.Normalize(hit.Normal) : Vector3.UnitY;
                if (Vector3.Dot(n, Vector3.UnitY) < minUp) continue;
                bestDistance = hit.Distance;
                groundY = hit.Point.Y;
                groundNormal = n;
            }

            if (float.IsFinite(bestDistance)) return true;
        }

        foreach (var obj in GetStaticColliders(scene, probeBounds))
        {
            if (obj.Collider is null || !obj.Collider.Raycast(obj, ray, out var hit)) continue;
            if (hit.Distance < 0f || hit.Distance > maxDistance || hit.Distance >= bestDistance) continue;
            var n = hit.Normal.LengthSquared() > 0.000001f ? Vector3.Normalize(hit.Normal) : Vector3.UnitY;
            if (Vector3.Dot(n, Vector3.UnitY) < minUp) continue;
            bestDistance = hit.Distance;
            groundY = hit.Point.Y;
            groundNormal = n;
        }

        return float.IsFinite(bestDistance);
    }

    private Bounds3D GetBounds(Vector3 footPosition)
    {
        return new Bounds3D(
            new Vector3(footPosition.X - Radius, footPosition.Y, footPosition.Z - Radius),
            new Vector3(footPosition.X + Radius, footPosition.Y + Height, footPosition.Z + Radius));
    }

    private static IReadOnlyList<Object3D> GetStaticColliders(Scene3D scene, Bounds3D bounds)
    {
        var candidates = scene.Registry.ColliderIndex.QueryBounds(bounds);
        return candidates.Count == 0 ? scene.Registry.StaticColliders : candidates;
    }
}
