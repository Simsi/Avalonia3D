using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

using JWorld = global::Jitter2.World;
using JVector = global::Jitter2.LinearMath.JVector;
using JQuaternion = global::Jitter2.LinearMath.JQuaternion;
using JMatrix = global::Jitter2.LinearMath.JMatrix;
using JRigidBody = global::Jitter2.Dynamics.RigidBody;
using JMotionType = global::Jitter2.Dynamics.MotionType;
using JMassMode = global::Jitter2.Dynamics.MassInertiaUpdateMode;
using JRigidBodyShape = global::Jitter2.Collision.Shapes.RigidBodyShape;
using JBoxShape = global::Jitter2.Collision.Shapes.BoxShape;
using JSphereShape = global::Jitter2.Collision.Shapes.SphereShape;
using JCapsuleShape = global::Jitter2.Collision.Shapes.CapsuleShape;
using JTransformedShape = global::Jitter2.Collision.Shapes.TransformedShape;

namespace ThreeDEngine.Core.Physics.Jitter2;

/// <summary>
/// Avalonia3D production physics backend backed by Jitter Physics 2.
/// Rendering stays owned by Avalonia3D; this class owns the physics world and synchronizes transforms.
/// </summary>
public sealed class Jitter2PhysicsCore : IPhysicsCore
{
    private readonly JWorld _world = new();
    private readonly Dictionary<Object3D, BodyEntry> _entries = new();
    private readonly HashSet<Object3D> _seen = new();
    private readonly List<RaycastHit3D> _raycastBuffer = new();
    private readonly List<Object3D> _removeScratch = new();

    private float _accumulator;
    private float _fixedTimeStep = 1f / 120f;
    private int _maxStepsPerFrame = 8;
    private float _maxFrameDeltaSeconds = 0.25f;
    private long _lastRegistryVersion = -1;
    private bool _disposed;

    public Jitter2PhysicsCore()
    {
        _world.AllowDeactivation = true;
        _world.SubstepCount = 4;
        _world.SolverIterations = (solver: 12, relaxation: 4);
    }

    public Vector3 Gravity { get; set; } = new(0f, -9.81f, 0f);

    /// <summary>Fixed physics integration step shared by desktop and browser.</summary>
    public float FixedTimeStep
    {
        get => _fixedTimeStep;
        set
        {
            if (!float.IsFinite(value) || value < 1f / 500f || value > 1f / 30f)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Fixed physics step must be finite and between 1/500 and 1/30 second.");
            _fixedTimeStep = value;
        }
    }

    public int MaxStepsPerFrame
    {
        get => _maxStepsPerFrame;
        set
        {
            if (value < 1 || value > 128) throw new ArgumentOutOfRangeException(nameof(value));
            _maxStepsPerFrame = value;
        }
    }

    public float MaxFrameDeltaSeconds
    {
        get => _maxFrameDeltaSeconds;
        set
        {
            if (!float.IsFinite(value) || value < 1f / 240f || value > 0.25f)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum physics frame delta must be finite and between 1/240 and 0.25 second.");
            _maxFrameDeltaSeconds = value;
        }
    }

    public bool UseMultithreading { get; set; }

    /// <summary>Enables support-surface probing after physics integration.</summary>
    public bool EnableGroundProbe { get; set; } = true;

    public int SubstepCount
    {
        get => _world.SubstepCount;
        set
        {
            if (value < 1 || value > 32) throw new ArgumentOutOfRangeException(nameof(value));
            _world.SubstepCount = value;
        }
    }

    public (int solver, int relaxation) SolverIterations
    {
        get => _world.SolverIterations;
        set
        {
            if (value.solver < 1 || value.solver > 128) throw new ArgumentOutOfRangeException(nameof(value));
            if (value.relaxation < 0 || value.relaxation > 128) throw new ArgumentOutOfRangeException(nameof(value));
            _world.SolverIterations = value;
        }
    }

    public void Step(Scene3D scene, float deltaSeconds)
    {
        ThrowIfDisposed();
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        if (deltaSeconds <= 0f || !float.IsFinite(deltaSeconds)) return;

        EnsureBodies(scene);

        var fixedDt = global::System.Math.Clamp(FixedTimeStep, 1f / 500f, 1f / 30f);
        var frameDt = global::System.Math.Min(deltaSeconds, MaxFrameDeltaSeconds);
        _accumulator = global::System.Math.Min(_accumulator + frameDt, fixedDt * global::System.Math.Max(1, MaxStepsPerFrame));
        _world.Gravity = ToJ(Gravity);

        IntegrateVelocityDrivenKinematics(frameDt);
        PushApplicationStateToJitter(frameDt);

        var steps = 0;
        while (_accumulator >= fixedDt && steps < MaxStepsPerFrame)
        {
            ApplyPendingForcesAndImpulses();
            CaptureAngularVelocitiesBeforeStep();
            _world.Step(fixedDt, UseMultithreading);
            ApplyAngularResponseControls(fixedDt);
            ClampJitterVelocities();
            _accumulator -= fixedDt;
            steps++;
        }

        PullJitterStateToScene();
    }

    public bool Raycast(Scene3D scene, Ray ray, out RaycastHit3D hit)
    {
        hit = default;
        var all = RaycastAll(scene, ray);
        if (all.Count == 0) return false;
        hit = all[0];
        return true;
    }

    public IReadOnlyList<RaycastHit3D> RaycastAll(Scene3D scene, Ray ray)
    {
        ThrowIfDisposed();
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        _raycastBuffer.Clear();
        if (ray.Direction.LengthSquared() <= 0.0000001f) return Array.Empty<RaycastHit3D>();

        EnsureBodies(scene);
        PushApplicationStateToJitter(0f);

        var direction = Vector3.Normalize(ray.Direction);
        var originJ = ToJ(ray.Origin);
        var directionJ = ToJ(direction);

        foreach (var pair in _entries)
        {
            var obj = pair.Key;
            var entry = pair.Value;
            if (!obj.IsVisible || obj.Collider is null) continue;

            if (!entry.Shape.RayCast(originJ, directionJ, out var normalJ, out var lambda)) continue;
            var distance = (float)lambda;
            if (!float.IsFinite(distance) || distance < 0f) continue;

            var point = ray.Origin + direction * distance;
            var normal = ToSystem(normalJ);
            if (normal.LengthSquared() <= 0.000001f) normal = Vector3.UnitY;
            else normal = Vector3.Normalize(normal);

            _raycastBuffer.Add(new RaycastHit3D(obj, point, normal, distance));
        }

        _raycastBuffer.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return _raycastBuffer.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _entries.Clear();
        _world.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureBodies(Scene3D scene)
    {
        var registryVersion = scene.Registry.Version;
        var colliders = scene.Registry.Colliders;
        _seen.Clear();

        foreach (var obj in colliders)
        {
            if (obj.Collider is null || !obj.IsVisible)
            {
                continue;
            }

            _seen.Add(obj);
            var signature = BuildSignature(obj);
            if (!_entries.TryGetValue(obj, out var entry))
            {
                entry = CreateEntry(obj, signature);
                _entries[obj] = entry;
                continue;
            }

            if (entry.Signature != signature || !ReferenceEquals(entry.RigidbodyReference, obj.Rigidbody) || !ReferenceEquals(entry.ColliderReference, obj.Collider))
            {
                RemoveEntry(entry);
                entry = CreateEntry(obj, signature);
                _entries[obj] = entry;
            }
            else
            {
                ConfigureBody(entry, obj);
            }
        }

        if (_lastRegistryVersion != registryVersion || _entries.Count != _seen.Count)
        {
            _removeScratch.Clear();
            foreach (var pair in _entries)
            {
                if (!_seen.Contains(pair.Key)) _removeScratch.Add(pair.Key);
            }

            foreach (var obj in _removeScratch)
            {
                RemoveEntry(_entries[obj]);
                _entries.Remove(obj);
            }
            _removeScratch.Clear();

            _lastRegistryVersion = registryVersion;
        }
    }

    private BodyEntry CreateEntry(Object3D obj, int signature)
    {
        var body = _world.CreateRigidBody();
        body.Tag = obj;
        var shape = CreateShape(obj);
        var entry = new BodyEntry(obj, body, shape, obj.Collider!, obj.Rigidbody, signature);
        body.AddShape(shape, JMassMode.Update);
        ConfigureBody(entry, obj, force: true);
        SyncPoseToBody(entry, force: true);
        entry.LastPhysicsTransformVersion = obj.TransformVersion;
        entry.LastSceneTransformVersion = obj.TransformVersion;
        entry.LastKinematicPosition = obj.Position;
        entry.LastKinematicRotation = Normalize(obj.Transform.LocalRotation);
        return entry;
    }

    private void RemoveEntry(BodyEntry entry)
    {
        if (entry.Body.World == _world)
        {
            _world.Remove(entry.Body);
        }
    }

    private void ConfigureBody(BodyEntry entry, Object3D obj, bool force = false)
    {
        var rb = obj.Rigidbody;
        var body = entry.Body;
        var motionType = rb is null
            ? JMotionType.Static
            : rb.IsKinematic ? JMotionType.Kinematic : JMotionType.Dynamic;

        if (force || body.MotionType != motionType)
        {
            body.MotionType = motionType;
        }

        body.AffectedByGravity = rb?.UseGravity ?? false;
        body.EnableSpeculativeContacts = true;
        body.EnableGyroscopicForces = rb is not null && !rb.FreezeRotation;
        body.Friction = global::System.Math.Clamp(rb?.Friction ?? 0.65f, 0f, 8f);
        body.Restitution = global::System.Math.Clamp(rb?.Restitution ?? 0.05f, 0f, 1f);
        body.Damping = (global::System.Math.Clamp(rb?.LinearDamping ?? 0.002f, 0f, 1f),
                        global::System.Math.Clamp(rb?.AngularDamping ?? 0.005f, 0f, 1f));
        body.DeactivationTime = TimeSpan.FromSeconds(rb?.AllowSleep == false ? 1.0e9 : global::System.Math.Max(0.05f, rb?.SleepDelay ?? 1.0f));
        body.DeactivationThreshold = (global::System.Math.Max(0f, rb?.SleepAngularSpeedThreshold ?? 0.05f),
                                      global::System.Math.Max(0f, rb?.SleepSpeedThreshold ?? 0.05f));

        if (rb is null || motionType != JMotionType.Dynamic)
        {
            entry.LastConfigurationVersion = rb?.ConfigurationVersion ?? -1;
            return;
        }

        if (!force && entry.LastConfigurationVersion == rb.ConfigurationVersion)
        {
            return;
        }

        var mass = global::System.Math.Max(0.0001f, rb.Mass);
        if (rb.FreezeRotation || !rb.GenerateContactRotation)
        {
            body.SetMassInertia(JMatrix.Zero, 1f / mass, setAsInverse: true);
        }
        else if (rb.AutoComputeInertiaTensor)
        {
            body.SetMassInertia(mass);
        }
        else
        {
            var inertia = JMatrix.Identity;
            inertia.M11 = global::System.Math.Max(0.0001f, rb.InertiaTensor.X);
            inertia.M22 = global::System.Math.Max(0.0001f, rb.InertiaTensor.Y);
            inertia.M33 = global::System.Math.Max(0.0001f, rb.InertiaTensor.Z);
            body.SetMassInertia(inertia, mass);
        }

        entry.LastConfigurationVersion = rb.ConfigurationVersion;
    }

    private void IntegrateVelocityDrivenKinematics(float frameDt)
    {
        if (frameDt <= 0f) return;

        foreach (var pair in _entries)
        {
            var obj = pair.Key;
            var rb = obj.Rigidbody;
            if (rb is not { IsKinematic: true, IntegrateKinematicVelocity: true }) continue;

            var v = ClampLength(rb.Velocity, rb.MaxLinearSpeed);
            var w = rb.FreezeRotation ? Vector3.Zero : ClampLength(rb.AngularVelocity, rb.MaxAngularSpeed);
            if (v != Vector3.Zero)
            {
                obj.Position += v * frameDt;
            }

            if (w.LengthSquared() > 0.000001f)
            {
                var angle = w.Length() * frameDt;
                var axis = Vector3.Normalize(w);
                obj.Transform.LocalRotation = Normalize(Quaternion.CreateFromAxisAngle(axis, angle) * obj.Transform.LocalRotation);
            }
        }
    }

    private void PushApplicationStateToJitter(float deltaSeconds)
    {
        foreach (var pair in _entries)
        {
            var obj = pair.Key;
            var entry = pair.Value;
            var rb = obj.Rigidbody;

            if (rb is null)
            {
                SyncPoseToBody(entry, force: true);
                continue;
            }

            if (rb.IsKinematic)
            {
                var derivedLinear = Vector3.Zero;
                var derivedAngular = Vector3.Zero;
                if (rb.DeriveKinematicVelocityFromTransform && deltaSeconds > 0.000001f)
                {
                    derivedLinear = (obj.Position - entry.LastKinematicPosition) / deltaSeconds;
                    derivedAngular = QuaternionToAngularVelocity(Normalize(obj.Transform.LocalRotation), entry.LastKinematicRotation, deltaSeconds);
                    rb.DerivedKinematicVelocity = derivedLinear;
                    rb.DerivedKinematicAngularVelocity = derivedAngular;
                }

                SyncPoseToBody(entry, force: true);
                var linear = rb.VelocityVersion != entry.LastVelocityVersionApplied
                    ? rb.Velocity
                    : (rb.Velocity.LengthSquared() > 0.000001f ? rb.Velocity : derivedLinear);
                var angular = rb.FreezeRotation
                    ? Vector3.Zero
                    : (rb.VelocityVersion != entry.LastVelocityVersionApplied
                        ? rb.AngularVelocity
                        : (rb.AngularVelocity.LengthSquared() > 0.000001f ? rb.AngularVelocity : derivedAngular));
                ApplyVelocityToBody(entry, rb, linear, angular);
                entry.LastVelocityVersionApplied = rb.VelocityVersion;
                entry.LastKinematicPosition = obj.Position;
                entry.LastKinematicRotation = Normalize(obj.Transform.LocalRotation);
                continue;
            }

            if (obj.TransformVersion != entry.LastPhysicsTransformVersion)
            {
                SyncPoseToBody(entry, force: true);
            }

            if (rb.VelocityVersion != entry.LastVelocityVersionApplied)
            {
                ApplyVelocityToBody(entry, rb, rb.Velocity, rb.AngularVelocity);
                entry.LastVelocityVersionApplied = rb.VelocityVersion;
            }
        }
    }

    private void SyncPoseToBody(BodyEntry entry, bool force)
    {
        var obj = entry.Object;
        if (!force && entry.LastSceneTransformVersion == obj.TransformVersion) return;

        entry.Body.Position = ToJ(obj.Position);
        entry.Body.Orientation = ToJ(Normalize(obj.Transform.LocalRotation));
        entry.LastSceneTransformVersion = obj.TransformVersion;
    }

    private static void ApplyVelocityToBody(BodyEntry entry, Rigidbody3D rb, Vector3 linear, Vector3 angular)
    {
        if (entry.Body.MotionType == JMotionType.Static) return;

        entry.Body.Velocity = ToJ(ClampLength(linear, rb.MaxLinearSpeed));
        entry.Body.AngularVelocity = rb.FreezeRotation ? JVector.Zero : ToJ(ClampLength(angular, rb.MaxAngularSpeed));
        if (rb.IsSleeping)
        {
            entry.Body.SetActivationState(false);
        }
        else if (linear.LengthSquared() > 0.000001f || angular.LengthSquared() > 0.000001f)
        {
            entry.Body.SetActivationState(true);
        }
    }

    private void ApplyPendingForcesAndImpulses()
    {
        foreach (var pair in _entries)
        {
            var rb = pair.Key.Rigidbody;
            if (rb is null || rb.IsKinematic) continue;

            rb.ConsumePendingDynamics(out var force, out var torque, out var forceAtPosition, out var forceWorldPosition, out var hasForceAtPosition, out var impulse, out var impulseAtPosition, out var impulseWorldPosition, out var hasImpulseAtPosition, out var torqueImpulse);
            var entry = pair.Value;
            var body = entry.Body;
            if (rb.ForceVersion != entry.LastForceVersionApplied)
            {
                body.SetActivationState(!rb.IsSleeping);
                entry.LastForceVersionApplied = rb.ForceVersion;
            }
            if (force != Vector3.Zero) body.AddForce(ToJ(force));
            if (hasForceAtPosition && forceAtPosition != Vector3.Zero) body.AddForce(ToJ(forceAtPosition), ToJ(forceWorldPosition));
            if (torque != Vector3.Zero) body.Torque += ToJ(torque);
            if (impulse != Vector3.Zero) body.ApplyImpulse(ToJ(impulse));
            if (hasImpulseAtPosition && impulseAtPosition != Vector3.Zero) body.ApplyImpulse(ToJ(impulseAtPosition), ToJ(impulseWorldPosition));
            if (torqueImpulse != Vector3.Zero)
            {
                body.AngularVelocity += ToJ(torqueImpulse);
            }
        }
    }

    private void CaptureAngularVelocitiesBeforeStep()
    {
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            entry.AngularVelocityBeforeStep = ToSystem(entry.Body.AngularVelocity);
        }
    }

    private void ApplyAngularResponseControls(float deltaSeconds)
    {
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            var rb = pair.Key.Rigidbody;
            if (rb is null || rb.IsKinematic || entry.Body.MotionType != JMotionType.Dynamic) continue;

            var angular = ToSystem(entry.Body.AngularVelocity);
            if (!NearlyEqual(rb.CollisionTorqueScale, 1f))
            {
                angular = entry.AngularVelocityBeforeStep +
                    (angular - entry.AngularVelocityBeforeStep) * rb.CollisionTorqueScale;
            }

            if (rb.RollingFriction > 0f && rb.IsGrounded)
            {
                var normal = rb.GroundNormal.LengthSquared() > 0.000001f
                    ? Vector3.Normalize(rb.GroundNormal)
                    : Vector3.UnitY;
                var gravityAlongNormal = global::System.Math.Abs(Vector3.Dot(Gravity, normal));
                if (gravityAlongNormal <= 0.0001f) gravityAlongNormal = Gravity.Length();

                var linear = ToSystem(entry.Body.Velocity);
                var normalVelocity = normal * Vector3.Dot(linear, normal);
                var tangentVelocity = linear - normalVelocity;
                var linearDrop = rb.RollingFriction * gravityAlongNormal * deltaSeconds;
                tangentVelocity = MoveTowardsZero(tangentVelocity, linearDrop);
                entry.Body.Velocity = ToJ(normalVelocity + tangentVelocity);

                var radius = rb.RollingRadius > 0f ? rb.RollingRadius : entry.EstimatedRollingRadius;
                angular = MoveTowardsZero(angular, linearDrop / global::System.Math.Max(radius, 0.0001f));
            }

            entry.Body.AngularVelocity = rb.FreezeRotation ? JVector.Zero : ToJ(angular);
        }
    }

    private static Vector3 MoveTowardsZero(Vector3 value, float maximumDelta)
    {
        if (maximumDelta <= 0f) return value;
        var length = value.Length();
        if (length <= maximumDelta || length <= 0.000001f) return Vector3.Zero;
        return value * ((length - maximumDelta) / length);
    }

    private static bool NearlyEqual(float left, float right)
        => global::System.Math.Abs(left - right) <= 0.000001f;

    private void ClampJitterVelocities()
    {
        foreach (var pair in _entries)
        {
            var rb = pair.Key.Rigidbody;
            if (rb is null || pair.Value.Body.MotionType == JMotionType.Static) continue;
            pair.Value.Body.Velocity = ToJ(ClampLength(ToSystem(pair.Value.Body.Velocity), rb.MaxLinearSpeed));
            pair.Value.Body.AngularVelocity = rb.FreezeRotation ? JVector.Zero : ToJ(ClampLength(ToSystem(pair.Value.Body.AngularVelocity), rb.MaxAngularSpeed));
        }
    }

    private void PullJitterStateToScene()
    {
        foreach (var pair in _entries)
        {
            var obj = pair.Key;
            var rb = obj.Rigidbody;
            var entry = pair.Value;
            var body = entry.Body;

            if (rb is null)
            {
                continue;
            }

            if (!rb.IsKinematic)
            {
                obj.Transform.LocalPosition = ToSystem(body.Position);
                obj.Transform.LocalRotation = ToSystem(body.Orientation);
                entry.LastPhysicsTransformVersion = obj.TransformVersion;
                entry.LastSceneTransformVersion = obj.TransformVersion;
            }

            var linear = ClampLength(ToSystem(body.Velocity), rb.MaxLinearSpeed);
            var angular = rb.FreezeRotation ? Vector3.Zero : ClampLength(ToSystem(body.AngularVelocity), rb.MaxAngularSpeed);
            rb.SetSimulationVelocity(linear, angular);
            rb.IsSleeping = !body.IsActive;
            if (EnableGroundProbe && TryProbeGround(entry, out var groundNormal))
            {
                rb.IsGrounded = true;
                rb.GroundNormal = groundNormal;
            }
            else
            {
                rb.IsGrounded = false;
                rb.GroundNormal = Vector3.UnitY;
            }
        }
    }

    private bool TryProbeGround(BodyEntry source, out Vector3 groundNormal)
    {
        groundNormal = Vector3.UnitY;
        var obj = source.Object;
        var bounds = obj.GetWorldBounds();
        if (!bounds.IsValid) return false;

        var origin = new Vector3(bounds.Center.X, bounds.Min.Y + 0.04f, bounds.Center.Z);
        var direction = -Vector3.UnitY;
        var maxDistance = 0.12f;
        var originJ = ToJ(origin);
        var directionJ = ToJ(direction);
        var bestDistance = float.PositiveInfinity;

        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            if (ReferenceEquals(entry, source)) continue;
            if (!entry.Object.IsVisible || entry.Object.Collider is null) continue;
            if (!entry.Shape.RayCast(originJ, directionJ, out var normalJ, out var lambda)) continue;
            var distance = (float)lambda;
            if (!float.IsFinite(distance) || distance < 0f || distance > maxDistance || distance >= bestDistance) continue;
            var n = ToSystem(normalJ);
            if (n.LengthSquared() <= 0.000001f) continue;
            n = Vector3.Normalize(n);
            if (Vector3.Dot(n, Vector3.UnitY) < 0.35f) continue;
            bestDistance = distance;
            groundNormal = n;
        }

        return float.IsFinite(bestDistance);
    }

    private JRigidBodyShape CreateShape(Object3D obj)
    {
        var scale = SafeScale(obj.Scale);
        return obj.Collider switch
        {
            BoxCollider3D box => OffsetShape(new JBoxShape(ToJ(Vector3.Max(Vector3.Abs(box.Size * scale), new Vector3(0.0001f)))), box.Center * scale),
            SphereCollider3D sphere => OffsetShape(new JSphereShape(global::System.Math.Max(0.0001f, sphere.Radius * MaxAbs(scale))), sphere.Center * scale),
            CapsuleCollider3D capsule => CreateCapsuleShape(capsule, scale),
            PlaneCollider3D plane => CreatePlaneShape(plane, scale),
            _ => new JBoxShape(ToJ(Vector3.Max(Vector3.Abs(obj.GetWorldBounds().Size), new Vector3(0.0001f))))
        };
    }

    private static JRigidBodyShape CreateCapsuleShape(CapsuleCollider3D capsule, Vector3 scale)
    {
        var radius = global::System.Math.Max(0.0001f, capsule.Radius * global::System.Math.Max(global::System.Math.Abs(scale.X), global::System.Math.Abs(scale.Z)));
        var fullHeight = global::System.Math.Max(radius * 2f, capsule.Height * global::System.Math.Abs(scale.Y));
        var cylinderLength = global::System.Math.Max(0f, fullHeight - radius * 2f);
        return OffsetShape(new JCapsuleShape(radius, cylinderLength), capsule.Center * scale);
    }

    private static JRigidBodyShape CreatePlaneShape(PlaneCollider3D plane, Vector3 scale)
    {
        var thickness = global::System.Math.Max(0.001f, plane.Thickness * global::System.Math.Max(0.0001f, global::System.Math.Abs(scale.Y)));
        var sx = global::System.Math.Max(0.0001f, global::System.Math.Abs(plane.Size.X * scale.X));
        var sz = global::System.Math.Max(0.0001f, global::System.Math.Abs(plane.Size.Y * scale.Z));
        var box = new JBoxShape(sx, thickness, sz);
        var normal = plane.LocalNormal.LengthSquared() < 0.000001f ? Vector3.UnitY : Vector3.Normalize(plane.LocalNormal);
        var offset = -normal * plane.Offset;
        var rotation = JQuaternion.CreateFromToRotation(new JVector(0f, 1f, 0f), ToJ(normal));
        var transform = JMatrix.CreateFromQuaternion(rotation);
        return new JTransformedShape(box, ToJ(offset), transform);
    }

    private static JRigidBodyShape OffsetShape(JRigidBodyShape shape, Vector3 offset)
    {
        return offset.LengthSquared() <= 0.0000001f ? shape : new JTransformedShape(shape, ToJ(offset));
    }

    private static int BuildSignature(Object3D obj)
    {
        var h = new HashCode();
        h.Add(obj.Collider?.GetType().FullName);
        h.Add(obj.Scale.X); h.Add(obj.Scale.Y); h.Add(obj.Scale.Z);
        if (obj.Collider is BoxCollider3D box)
        {
            h.Add(box.Center.X); h.Add(box.Center.Y); h.Add(box.Center.Z);
            h.Add(box.Size.X); h.Add(box.Size.Y); h.Add(box.Size.Z);
        }
        else if (obj.Collider is SphereCollider3D sphere)
        {
            h.Add(sphere.Center.X); h.Add(sphere.Center.Y); h.Add(sphere.Center.Z); h.Add(sphere.Radius);
        }
        else if (obj.Collider is CapsuleCollider3D capsule)
        {
            h.Add(capsule.Center.X); h.Add(capsule.Center.Y); h.Add(capsule.Center.Z); h.Add(capsule.Radius); h.Add(capsule.Height);
        }
        else if (obj.Collider is PlaneCollider3D plane)
        {
            h.Add(plane.LocalNormal.X); h.Add(plane.LocalNormal.Y); h.Add(plane.LocalNormal.Z); h.Add(plane.Offset); h.Add(plane.Size.X); h.Add(plane.Size.Y); h.Add(plane.Thickness);
        }
        if (obj.Rigidbody is { } rb)
        {
            h.Add(rb.IsKinematic); h.Add(rb.Mass); h.Add(rb.FreezeRotation); h.Add(rb.AutoComputeInertiaTensor); h.Add(rb.GenerateContactRotation);
        }
        return h.ToHashCode();
    }

    private static Vector3 QuaternionToAngularVelocity(Quaternion current, Quaternion previous, float dt)
    {
        if (dt <= 0.000001f) return Vector3.Zero;
        var delta = Normalize(current * Quaternion.Inverse(previous));
        if (delta.W < 0f) delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        var angle = 2f * global::System.MathF.Acos(global::System.Math.Clamp(delta.W, -1f, 1f));
        var sinHalf = global::System.MathF.Sqrt(global::System.Math.Max(0f, 1f - delta.W * delta.W));
        if (sinHalf < 0.0001f || angle < 0.0001f) return Vector3.Zero;
        var axis = new Vector3(delta.X, delta.Y, delta.Z) / sinHalf;
        return axis * (angle / dt);
    }

    private static Vector3 SafeScale(Vector3 value)
        => new(AbsOrOne(value.X), AbsOrOne(value.Y), AbsOrOne(value.Z));

    private static float AbsOrOne(float value)
    {
        var v = global::System.Math.Abs(value);
        return v < 0.0001f || !float.IsFinite(v) ? 1f : v;
    }

    private static float MaxAbs(Vector3 v)
        => global::System.Math.Max(global::System.Math.Abs(v.X), global::System.Math.Max(global::System.Math.Abs(v.Y), global::System.Math.Abs(v.Z)));

    private static Vector3 ClampLength(Vector3 value, float maxLength)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) return Vector3.Zero;
        if (maxLength <= 0f) return Vector3.Zero;
        var limitSq = maxLength * maxLength;
        return value.LengthSquared() > limitSq ? Vector3.Normalize(value) * maxLength : value;
    }

    private static Quaternion Normalize(Quaternion q)
        => q.LengthSquared() < 0.000001f ? Quaternion.Identity : Quaternion.Normalize(q);

    private static JVector ToJ(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 ToSystem(JVector v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static JQuaternion ToJ(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    private static Quaternion ToSystem(JQuaternion q) => Normalize(new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W));

    private sealed class BodyEntry
    {
        public BodyEntry(Object3D obj, JRigidBody body, JRigidBodyShape shape, Collider3D collider, Rigidbody3D? rigidbody, int signature)
        {
            Object = obj;
            Body = body;
            Shape = shape;
            ColliderReference = collider;
            RigidbodyReference = rigidbody;
            Signature = signature;
            var bounds = obj.GetWorldBounds();
            var size = bounds.IsValid ? Vector3.Abs(bounds.Size) : Vector3.One;
            var horizontalDiameter = global::System.Math.Min(size.X, size.Z);
            EstimatedRollingRadius = global::System.Math.Max(0.0001f, horizontalDiameter * 0.5f);
        }

        public JRigidBody Body { get; }
        public JRigidBodyShape Shape { get; }
        public Object3D Object { get; }
        public Collider3D ColliderReference { get; }
        public Rigidbody3D? RigidbodyReference { get; }
        public int Signature { get; }
        public int LastSceneTransformVersion { get; set; } = -1;
        public int LastPhysicsTransformVersion { get; set; } = -1;
        public int LastConfigurationVersion { get; set; } = -1;
        public int LastVelocityVersionApplied { get; set; } = -1;
        public int LastForceVersionApplied { get; set; } = -1;
        public float EstimatedRollingRadius { get; }
        public Vector3 AngularVelocityBeforeStep { get; set; }
        public Vector3 LastKinematicPosition { get; set; }
        public Quaternion LastKinematicRotation { get; set; } = Quaternion.Identity;
    }
}
