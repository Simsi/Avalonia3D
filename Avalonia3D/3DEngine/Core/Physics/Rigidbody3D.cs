using System.Numerics;

namespace ThreeDEngine.Core.Physics;

/// <summary>
/// Physics body descriptor used by the production Jitter2 backend.
/// Public setters mark user-authored state; simulation writes use internal setters so
/// the backend does not feed its own velocities back as user overrides on the next step.
/// </summary>
public sealed class Rigidbody3D
{
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
    private float _rollingFriction = 0.015f;
    private float _linearDamping = 0.002f;
    private float _angularDamping = 0.005f;
    private float _maxAngularSpeed = 96f;
    private float _maxLinearSpeed = 128f;
    private float _collisionTorqueScale = 1f;
    private float _rollingRadius;
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

    public float Mass
    {
        get => _mass;
        set { var v = SanitizePositive(value, 1f); if (NearlyEqual(_mass, v)) return; _mass = v; ConfigurationVersion++; }
    }

    public Vector3 Velocity
    {
        get => _velocity;
        set { var v = Sanitize(value); if (_velocity == v) return; _velocity = v; VelocityVersion++; }
    }

    public Vector3 AngularVelocity
    {
        get => _angularVelocity;
        set { var v = Sanitize(value); if (_angularVelocity == v) return; _angularVelocity = v; VelocityVersion++; }
    }

    public bool IsKinematic
    {
        get => _isKinematic;
        set { if (_isKinematic == value) return; _isKinematic = value; ConfigurationVersion++; VelocityVersion++; }
    }

    public bool UseGravity
    {
        get => _useGravity;
        set { if (_useGravity == value) return; _useGravity = value; ConfigurationVersion++; }
    }

    /// <summary>When true, the body keeps infinite angular inertia in the Jitter solver.</summary>
    public bool FreezeRotation
    {
        get => _freezeRotation;
        set { if (_freezeRotation == value) return; _freezeRotation = value; ConfigurationVersion++; VelocityVersion++; }
    }

    public Vector3 CenterOfMassLocal
    {
        get => _centerOfMassLocal;
        set { var v = Sanitize(value); if (_centerOfMassLocal == v) return; _centerOfMassLocal = v; ConfigurationVersion++; }
    }

    public Vector3 InertiaTensor
    {
        get => _inertiaTensor;
        set { var v = Vector3.Max(Sanitize(value), new Vector3(0.0001f)); if (_inertiaTensor == v) return; _inertiaTensor = v; ConfigurationVersion++; }
    }

    public bool AutoComputeInertiaTensor
    {
        get => _autoComputeInertiaTensor;
        set { if (_autoComputeInertiaTensor == value) return; _autoComputeInertiaTensor = value; ConfigurationVersion++; }
    }

    public float Restitution
    {
        get => _restitution;
        set { var v = Clamp(value, 0f, 1f); if (NearlyEqual(_restitution, v)) return; _restitution = v; ConfigurationVersion++; }
    }

    public float Friction
    {
        get => _friction;
        set { var v = Clamp(value, 0f, 8f); if (NearlyEqual(_friction, v)) return; _friction = v; ConfigurationVersion++; }
    }

    /// <summary>Kept for API compatibility. Jitter2 handles rolling behavior through contact friction/inertia.</summary>
    public float RollingFriction
    {
        get => _rollingFriction;
        set { var v = Clamp(value, 0f, 1f); if (NearlyEqual(_rollingFriction, v)) return; _rollingFriction = v; ConfigurationVersion++; }
    }

    public float LinearDamping
    {
        get => _linearDamping;
        set { var v = Clamp(value, 0f, 1f); if (NearlyEqual(_linearDamping, v)) return; _linearDamping = v; ConfigurationVersion++; }
    }

    public float AngularDamping
    {
        get => _angularDamping;
        set { var v = Clamp(value, 0f, 1f); if (NearlyEqual(_angularDamping, v)) return; _angularDamping = v; ConfigurationVersion++; }
    }

    public float MaxAngularSpeed
    {
        get => _maxAngularSpeed;
        set { var v = SanitizeNonNegative(value, 0f); if (NearlyEqual(_maxAngularSpeed, v)) return; _maxAngularSpeed = v; VelocityVersion++; }
    }

    public float MaxLinearSpeed
    {
        get => _maxLinearSpeed;
        set { var v = SanitizeNonNegative(value, 0f); if (NearlyEqual(_maxLinearSpeed, v)) return; _maxLinearSpeed = v; VelocityVersion++; }
    }

    /// <summary>Kept for source compatibility. Jitter2 computes contact torques from shape inertia/contact points.</summary>
    public float CollisionTorqueScale
    {
        get => _collisionTorqueScale;
        set { var v = SanitizeNonNegative(value, 1f); if (NearlyEqual(_collisionTorqueScale, v)) return; _collisionTorqueScale = v; ConfigurationVersion++; }
    }

    public float RollingRadius
    {
        get => _rollingRadius;
        set { var v = SanitizeNonNegative(value, 0f); if (NearlyEqual(_rollingRadius, v)) return; _rollingRadius = v; ConfigurationVersion++; }
    }

    public bool GenerateContactRotation
    {
        get => _generateContactRotation;
        set { if (_generateContactRotation == value) return; _generateContactRotation = value; ConfigurationVersion++; }
    }

    public bool DeriveKinematicVelocityFromTransform
    {
        get => _deriveKinematicVelocityFromTransform;
        set { if (_deriveKinematicVelocityFromTransform == value) return; _deriveKinematicVelocityFromTransform = value; ConfigurationVersion++; VelocityVersion++; }
    }

    public bool IntegrateKinematicVelocity
    {
        get => _integrateKinematicVelocity;
        set { if (_integrateKinematicVelocity == value) return; _integrateKinematicVelocity = value; ConfigurationVersion++; VelocityVersion++; }
    }

    public bool AllowSleep
    {
        get => _allowSleep;
        set { if (_allowSleep == value) return; _allowSleep = value; ConfigurationVersion++; }
    }

    public float SleepDelay
    {
        get => _sleepDelay;
        set { var v = SanitizeNonNegative(value, 1f); if (NearlyEqual(_sleepDelay, v)) return; _sleepDelay = v; ConfigurationVersion++; }
    }

    public float SleepSpeedThreshold
    {
        get => _sleepSpeedThreshold;
        set { var v = SanitizeNonNegative(value, 0.05f); if (NearlyEqual(_sleepSpeedThreshold, v)) return; _sleepSpeedThreshold = v; ConfigurationVersion++; }
    }

    public float SleepAngularSpeedThreshold
    {
        get => _sleepAngularSpeedThreshold;
        set { var v = SanitizeNonNegative(value, 0.05f); if (NearlyEqual(_sleepAngularSpeedThreshold, v)) return; _sleepAngularSpeedThreshold = v; ConfigurationVersion++; }
    }

    public bool IsSleeping { get; internal set; }
    public bool IsGrounded { get; internal set; }
    public Vector3 GroundNormal { get; internal set; } = Vector3.UnitY;

    // Compatibility-only diagnostic properties. Jitter2 owns actual deactivation/contact stability.
    public float SleepContactTorqueThreshold { get; set; } = 0.01f;
    public int SleepMinStableContactCount { get; set; } = 1;
    internal float SleepTimer { get; set; }
    internal Vector3 PreviousPosition { get; set; }
    internal Quaternion PreviousRotation { get; set; } = Quaternion.Identity;
    internal bool HasKinematicPreviousPose { get; set; }
    internal Vector3 DerivedKinematicVelocity { get; set; }
    internal Vector3 DerivedKinematicAngularVelocity { get; set; }
    internal Vector3 AccumulatedContactTorque { get; set; }
    internal float AccumulatedContactNormalImpulse { get; set; }
    internal int ContactCount { get; set; }
    internal bool HadUnstableContact { get; set; }

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
        IsSleeping = false;
        SleepTimer = 0f;
        ForceVersion++;
    }

    public void Sleep()
    {
        IsSleeping = true;
        SleepTimer = SleepDelay;
        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
    }

    public void AddForce(Vector3 force)
    {
        force = Sanitize(force);
        if (force == Vector3.Zero) return;
        _pendingForce += force;
        ForceVersion++;
        WakeUp();
    }

    public void AddTorque(Vector3 torque)
    {
        torque = Sanitize(torque);
        if (torque == Vector3.Zero) return;
        _pendingTorque += torque;
        ForceVersion++;
        WakeUp();
    }

    public void AddForce(Vector3 force, Vector3 worldPosition)
    {
        force = Sanitize(force);
        worldPosition = Sanitize(worldPosition);
        if (force == Vector3.Zero) return;
        _pendingForceAtPosition += force;
        _pendingForceWorldPosition = worldPosition;
        _hasPendingForceAtPosition = true;
        ForceVersion++;
        WakeUp();
    }

    public void AddImpulse(Vector3 impulse)
    {
        impulse = Sanitize(impulse);
        if (impulse == Vector3.Zero) return;
        _pendingImpulse += impulse;
        ForceVersion++;
        WakeUp();
    }

    public void AddImpulse(Vector3 impulse, Vector3 worldPosition)
    {
        impulse = Sanitize(impulse);
        worldPosition = Sanitize(worldPosition);
        if (impulse == Vector3.Zero) return;
        _pendingImpulseAtPosition += impulse;
        _pendingImpulseWorldPosition = worldPosition;
        _hasPendingImpulseAtPosition = true;
        ForceVersion++;
        WakeUp();
    }

    public void AddTorqueImpulse(Vector3 angularImpulse)
    {
        angularImpulse = Sanitize(angularImpulse);
        if (angularImpulse == Vector3.Zero) return;
        _pendingTorqueImpulse += angularImpulse;
        ForceVersion++;
        WakeUp();
    }

    public void ClearForces()
    {
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
    }

    public void ClampAngularVelocity()
    {
        AngularVelocity = ClampLength(AngularVelocity, MaxAngularSpeed);
    }

    public void ApplySleepThresholds()
    {
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
        _velocity = Sanitize(velocity);
        _angularVelocity = Sanitize(angularVelocity);
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

    private static Vector3 Sanitize(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) ? value : Vector3.Zero;

    private static float SanitizePositive(float value, float fallback)
        => float.IsFinite(value) && value > 0f ? value : fallback;

    private static float SanitizeNonNegative(float value, float fallback)
        => float.IsFinite(value) && value >= 0f ? value : fallback;

    private static float Clamp(float value, float min, float max)
        => float.IsFinite(value) ? global::System.Math.Clamp(value, min, max) : min;

    private static bool NearlyEqual(float a, float b) => global::System.Math.Abs(a - b) <= 0.000001f;

    private static Vector3 ClampLength(Vector3 value, float maxLength)
    {
        value = Sanitize(value);
        if (maxLength <= 0f) return Vector3.Zero;
        var maxSq = maxLength * maxLength;
        return value.LengthSquared() > maxSq ? Vector3.Normalize(value) * maxLength : value;
    }
}
