using System;
using System.Numerics;
using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Physics;

/// <summary>
/// Physics body descriptor used by the production Jitter2 backend.
/// Public setters mark user-authored state; simulation writes use internal setters so
/// the backend does not feed its own velocities back as user overrides on the next step.
/// </summary>
public sealed class Rigidbody3D
{
    internal Object3D? Owner { get; set; }
    private float _mass = 1f;
    private Vector3 _velocity;
    private Vector3 _angularVelocity;
    private bool _isKinematic;
    private bool _useGravity = true;
    private bool _freezeRotation;
    private Vector3 _centerOfMassLocal;
    private Vector3 _inertiaTensor = Vector3.One;
    private bool _autoComputeInertiaTensor = true;
    private float _restitution = 0.15f;
    private float _friction = 0.65f;
    private float _rollingFriction;
    private float _collisionTorqueScale = 1f;
    private float _rollingRadius;
    private float _linearDamping = 0.002f;
    private float _angularDamping = 0.005f;
    private float _maxAngularSpeed = 96f;
    private float _maxLinearSpeed = 128f;
    private bool _generateContactRotation = true;
    private bool _deriveKinematicVelocityFromTransform = true;
    private bool _integrateKinematicVelocity;
    private bool _allowSleep = true;
    private float _sleepDelay = 1.0f;
    private float _sleepSpeedThreshold = 0.05f;
    private float _sleepAngularSpeedThreshold = 0.05f;

    private Vector3 _pendingForce;
    private Vector3 _pendingTorque;
    private Vector3 _pendingForceAtPosition;
    private Vector3 _pendingForceWorldPosition;
    private bool _hasPendingForceAtPosition;
    private Vector3 _pendingImpulse;
    private Vector3 _pendingImpulseAtPosition;
    private Vector3 _pendingImpulseWorldPosition;
    private bool _hasPendingImpulseAtPosition;
    private Vector3 _pendingTorqueImpulse;

    internal int ConfigurationVersion { get; private set; }
    internal int VelocityVersion { get; private set; }
    internal int ForceVersion { get; private set; }
    internal bool HasPendingDynamics =>
        _pendingForce != Vector3.Zero ||
        _pendingTorque != Vector3.Zero ||
        _pendingImpulse != Vector3.Zero ||
        _pendingTorqueImpulse != Vector3.Zero ||
        _hasPendingForceAtPosition ||
        _hasPendingImpulseAtPosition;

    /// <summary>
    /// Raised only when registry membership can change. Runtime velocity/configuration writes
    /// stay version-based and do not allocate scene change events every simulation step.
    /// </summary>
    public event EventHandler? MembershipChanged;
    internal event EventHandler? ActivityChanged;

    public float Mass
    {
        get => _mass;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Positive(value, nameof(value)); if (NearlyEqual(_mass, v)) return; _mass = v; ConfigurationVersion++; }
    }

    public Vector3 Velocity
    {
        get => _velocity;
        set
        {
            using var mutation = EnterMutationScope();
            var v = Guard3D.Finite(value, nameof(value));
            if (_velocity == v) return;
            _velocity = v;
            if (v != Vector3.Zero) IsSleeping = false;
            VelocityVersion++;
            NotifyActivityChanged();
        }
    }

    public Vector3 AngularVelocity
    {
        get => _angularVelocity;
        set
        {
            using var mutation = EnterMutationScope();
            var v = Guard3D.Finite(value, nameof(value));
            if (_angularVelocity == v) return;
            _angularVelocity = v;
            if (v != Vector3.Zero) IsSleeping = false;
            VelocityVersion++;
            NotifyActivityChanged();
        }
    }

    public bool IsKinematic
    {
        get => _isKinematic;
        set
        {
            using var mutation = EnterMutationScope();
            if (_isKinematic == value) return;
            _isKinematic = value;
            ConfigurationVersion++;
            VelocityVersion++;
            MembershipChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool UseGravity
    {
        get => _useGravity;
        set
        {
            using var mutation = EnterMutationScope();
            if (_useGravity == value) return;
            _useGravity = value;
            if (value) IsSleeping = false;
            ConfigurationVersion++;
            NotifyActivityChanged();
        }
    }

    /// <summary>When true, the body keeps infinite angular inertia in the Jitter solver.</summary>
    public bool FreezeRotation
    {
        get => _freezeRotation;
        set { using var mutation = EnterMutationScope(); if (_freezeRotation == value) return; _freezeRotation = value; ConfigurationVersion++; VelocityVersion++; }
    }

    public Vector3 CenterOfMassLocal
    {
        get => _centerOfMassLocal;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Finite(value, nameof(value)); if (_centerOfMassLocal == v) return; _centerOfMassLocal = v; ConfigurationVersion++; }
    }

    public Vector3 InertiaTensor
    {
        get => _inertiaTensor;
        set
        {
            using var mutation = EnterMutationScope();
            var v = Guard3D.Finite(value, nameof(value));
            if (v.X <= 0f || v.Y <= 0f || v.Z <= 0f) throw new ArgumentOutOfRangeException(nameof(value), value, "Inertia tensor components must be positive.");
            if (_inertiaTensor == v) return;
            _inertiaTensor = v;
            ConfigurationVersion++;
        }
    }

    public bool AutoComputeInertiaTensor
    {
        get => _autoComputeInertiaTensor;
        set { using var mutation = EnterMutationScope(); if (_autoComputeInertiaTensor == value) return; _autoComputeInertiaTensor = value; ConfigurationVersion++; }
    }

    public float Restitution
    {
        get => _restitution;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 1f, nameof(value)); if (NearlyEqual(_restitution, v)) return; _restitution = v; ConfigurationVersion++; }
    }

    public float Friction
    {
        get => _friction;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 8f, nameof(value)); if (NearlyEqual(_friction, v)) return; _friction = v; ConfigurationVersion++; }
    }

    /// <summary>
    /// Tangential rolling-resistance coefficient applied while the body is grounded.
    /// The Jitter2 adapter removes linear and angular rolling energy deterministically
    /// after each solver step instead of storing this value as a compatibility no-op.
    /// </summary>
    public float RollingFriction
    {
        get => _rollingFriction;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 1f, nameof(value)); if (NearlyEqual(_rollingFriction, v)) return; _rollingFriction = v; ConfigurationVersion++; }
    }

    /// <summary>
    /// Scales the angular-velocity delta produced by one physics solver step.
    /// Zero suppresses solver-generated angular response; one preserves the native response.
    /// </summary>
    public float CollisionTorqueScale
    {
        get => _collisionTorqueScale;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 4f, nameof(value)); if (NearlyEqual(_collisionTorqueScale, v)) return; _collisionTorqueScale = v; ConfigurationVersion++; }
    }

    /// <summary>
    /// Effective rolling radius in world units. A value of zero selects a radius derived
    /// from the collider bounds. The radius converts linear rolling resistance to angular deceleration.
    /// </summary>
    public float RollingRadius
    {
        get => _rollingRadius;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_rollingRadius, v)) return; _rollingRadius = v; ConfigurationVersion++; }
    }

    public float LinearDamping
    {
        get => _linearDamping;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 1f, nameof(value)); if (NearlyEqual(_linearDamping, v)) return; _linearDamping = v; ConfigurationVersion++; }
    }

    public float AngularDamping
    {
        get => _angularDamping;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 1f, nameof(value)); if (NearlyEqual(_angularDamping, v)) return; _angularDamping = v; ConfigurationVersion++; }
    }

    public float MaxAngularSpeed
    {
        get => _maxAngularSpeed;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_maxAngularSpeed, v)) return; _maxAngularSpeed = v; VelocityVersion++; }
    }

    public float MaxLinearSpeed
    {
        get => _maxLinearSpeed;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_maxLinearSpeed, v)) return; _maxLinearSpeed = v; VelocityVersion++; }
    }



    public bool GenerateContactRotation
    {
        get => _generateContactRotation;
        set { using var mutation = EnterMutationScope(); if (_generateContactRotation == value) return; _generateContactRotation = value; ConfigurationVersion++; }
    }

    public bool DeriveKinematicVelocityFromTransform
    {
        get => _deriveKinematicVelocityFromTransform;
        set { using var mutation = EnterMutationScope(); if (_deriveKinematicVelocityFromTransform == value) return; _deriveKinematicVelocityFromTransform = value; ConfigurationVersion++; VelocityVersion++; }
    }

    public bool IntegrateKinematicVelocity
    {
        get => _integrateKinematicVelocity;
        set { using var mutation = EnterMutationScope(); if (_integrateKinematicVelocity == value) return; _integrateKinematicVelocity = value; ConfigurationVersion++; VelocityVersion++; }
    }

    public bool AllowSleep
    {
        get => _allowSleep;
        set
        {
            using var mutation = EnterMutationScope();
            if (_allowSleep == value) return;
            _allowSleep = value;
            if (!value) IsSleeping = false;
            ConfigurationVersion++;
            NotifyActivityChanged();
        }
    }

    public float SleepDelay
    {
        get => _sleepDelay;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_sleepDelay, v)) return; _sleepDelay = v; ConfigurationVersion++; }
    }

    public float SleepSpeedThreshold
    {
        get => _sleepSpeedThreshold;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_sleepSpeedThreshold, v)) return; _sleepSpeedThreshold = v; ConfigurationVersion++; }
    }

    public float SleepAngularSpeedThreshold
    {
        get => _sleepAngularSpeedThreshold;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_sleepAngularSpeedThreshold, v)) return; _sleepAngularSpeedThreshold = v; ConfigurationVersion++; }
    }

    public bool IsSleeping { get; internal set; }
    public bool IsGrounded { get; internal set; }
    public Vector3 GroundNormal { get; internal set; } = Vector3.UnitY;
    internal float SleepTimer { get; set; }
    internal Vector3 DerivedKinematicVelocity { get; set; }
    internal Vector3 DerivedKinematicAngularVelocity { get; set; }

    public float InverseMass => IsKinematic || Mass <= 0f ? 0f : 1f / Mass;

    internal Vector3 EffectiveVelocity => IsKinematic && DeriveKinematicVelocityFromTransform
        ? (Velocity.LengthSquared() > 0.000001f ? Velocity : DerivedKinematicVelocity)
        : Velocity;

    internal Vector3 EffectiveAngularVelocity => IsKinematic && DeriveKinematicVelocityFromTransform
        ? (AngularVelocity.LengthSquared() > 0.000001f ? AngularVelocity : DerivedKinematicAngularVelocity)
        : AngularVelocity;

    public Vector3 InverseInertiaTensor
    {
        get
        {
            static float Inv(float v) => v <= 0.0001f ? 0f : 1f / v;
            return FreezeRotation || IsKinematic ? Vector3.Zero : new Vector3(Inv(InertiaTensor.X), Inv(InertiaTensor.Y), Inv(InertiaTensor.Z));
        }
    }

    public void WakeUp()
    {
        using var mutation = EnterMutationScope();
        IsSleeping = false;
        SleepTimer = 0f;
        ForceVersion++;
        NotifyActivityChanged();
    }

    public void Sleep()
    {
        using var mutation = EnterMutationScope();
        IsSleeping = true;
        SleepTimer = SleepDelay;
        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
    }

    public void AddForce(Vector3 force)
    {
        using var mutation = EnterMutationScope();
        force = Guard3D.Finite(force, nameof(force));
        if (force == Vector3.Zero) return;
        _pendingForce += force;
        ForceVersion++;
        WakeUp();
    }

    public void AddTorque(Vector3 torque)
    {
        using var mutation = EnterMutationScope();
        torque = Guard3D.Finite(torque, nameof(torque));
        if (torque == Vector3.Zero) return;
        _pendingTorque += torque;
        ForceVersion++;
        WakeUp();
    }

    public void AddForce(Vector3 force, Vector3 worldPosition)
    {
        using var mutation = EnterMutationScope();
        force = Guard3D.Finite(force, nameof(force));
        worldPosition = Guard3D.Finite(worldPosition, nameof(worldPosition));
        if (force == Vector3.Zero) return;
        _pendingForceAtPosition += force;
        _pendingForceWorldPosition = worldPosition;
        _hasPendingForceAtPosition = true;
        ForceVersion++;
        WakeUp();
    }

    public void AddImpulse(Vector3 impulse)
    {
        using var mutation = EnterMutationScope();
        impulse = Guard3D.Finite(impulse, nameof(impulse));
        if (impulse == Vector3.Zero) return;
        _pendingImpulse += impulse;
        ForceVersion++;
        WakeUp();
    }

    public void AddImpulse(Vector3 impulse, Vector3 worldPosition)
    {
        using var mutation = EnterMutationScope();
        impulse = Guard3D.Finite(impulse, nameof(impulse));
        worldPosition = Guard3D.Finite(worldPosition, nameof(worldPosition));
        if (impulse == Vector3.Zero) return;
        _pendingImpulseAtPosition += impulse;
        _pendingImpulseWorldPosition = worldPosition;
        _hasPendingImpulseAtPosition = true;
        ForceVersion++;
        WakeUp();
    }

    public void AddTorqueImpulse(Vector3 angularImpulse)
    {
        using var mutation = EnterMutationScope();
        angularImpulse = Guard3D.Finite(angularImpulse, nameof(angularImpulse));
        if (angularImpulse == Vector3.Zero) return;
        _pendingTorqueImpulse += angularImpulse;
        ForceVersion++;
        WakeUp();
    }

    public void ClearForces()
    {
        using var mutation = EnterMutationScope();
        _pendingForce = Vector3.Zero;
        _pendingTorque = Vector3.Zero;
        _pendingForceAtPosition = Vector3.Zero;
        _pendingForceWorldPosition = Vector3.Zero;
        _hasPendingForceAtPosition = false;
        _pendingImpulse = Vector3.Zero;
        _pendingImpulseAtPosition = Vector3.Zero;
        _pendingTorqueImpulse = Vector3.Zero;
        _pendingImpulseWorldPosition = Vector3.Zero;
        _hasPendingImpulseAtPosition = false;
        ForceVersion++;
        NotifyActivityChanged();
    }

    public void ClampAngularVelocity()
    {
        using var mutation = EnterMutationScope();
        AngularVelocity = ClampLength(AngularVelocity, MaxAngularSpeed);
    }

    public void ApplySleepThresholds()
    {
        using var mutation = EnterMutationScope();
        if (Velocity.LengthSquared() < SleepSpeedThreshold * SleepSpeedThreshold)
        {
            Velocity = Vector3.Zero;
        }

        if (AngularVelocity.LengthSquared() < SleepAngularSpeedThreshold * SleepAngularSpeedThreshold)
        {
            AngularVelocity = Vector3.Zero;
        }
    }

    internal void SetSimulationVelocity(Vector3 velocity, Vector3 angularVelocity)
    {
        _velocity = Guard3D.Finite(velocity, nameof(velocity));
        _angularVelocity = Guard3D.Finite(angularVelocity, nameof(angularVelocity));
    }

    internal void ConsumePendingDynamics(
        out Vector3 force,
        out Vector3 torque,
        out Vector3 forceAtPosition,
        out Vector3 forceWorldPosition,
        out bool hasForceAtPosition,
        out Vector3 impulse,
        out Vector3 impulseAtPosition,
        out Vector3 impulseWorldPosition,
        out bool hasImpulseAtPosition,
        out Vector3 torqueImpulse)
    {
        force = _pendingForce;
        torque = _pendingTorque;
        forceAtPosition = _pendingForceAtPosition;
        forceWorldPosition = _pendingForceWorldPosition;
        hasForceAtPosition = _hasPendingForceAtPosition;
        impulse = _pendingImpulse;
        impulseAtPosition = _pendingImpulseAtPosition;
        impulseWorldPosition = _pendingImpulseWorldPosition;
        hasImpulseAtPosition = _hasPendingImpulseAtPosition;
        torqueImpulse = _pendingTorqueImpulse;
        _pendingForce = Vector3.Zero;
        _pendingTorque = Vector3.Zero;
        _pendingForceAtPosition = Vector3.Zero;
        _pendingForceWorldPosition = Vector3.Zero;
        _hasPendingForceAtPosition = false;
        _pendingImpulse = Vector3.Zero;
        _pendingImpulseAtPosition = Vector3.Zero;
        _pendingTorqueImpulse = Vector3.Zero;
        _pendingImpulseWorldPosition = Vector3.Zero;
        _hasPendingImpulseAtPosition = false;
    }

    private SceneAccessLease3D EnterMutationScope()
        => Owner?.OwnerScene?.EnterMutationScope(nameof(Rigidbody3D)) ?? default;

    private static bool NearlyEqual(float a, float b) => global::System.Math.Abs(a - b) <= 0.000001f;

    private static Vector3 ClampLength(Vector3 value, float maxLength)
    {
        value = Guard3D.Finite(value, nameof(value));
        if (maxLength <= 0f) return Vector3.Zero;
        var maxSq = maxLength * maxLength;
        return value.LengthSquared() > maxSq ? Vector3.Normalize(value) * maxLength : value;
    }

    private void NotifyActivityChanged() => ActivityChanged?.Invoke(this, EventArgs.Empty);
}
