using System;
using System.Numerics;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Scene;

public sealed class Camera3D
{
    private float _fieldOfViewDegrees = 55f;
    private float _nearPlane = 0.1f;
    private float _farPlane = 100f;
    private Vector3 _position = new(0f, 0f, 6f);
    private Vector3 _target = Vector3.Zero;
    private Vector3 _up = Vector3.UnitY;
    internal Scene3D? OwnerScene { get; set; }

    public event EventHandler? Changed;

    public Vector3 Position
    {
        get => _position;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            value = Guard3D.Finite(value, nameof(value));
            if (value == _target) throw new ArgumentException("Camera position and target must differ.", nameof(value));
            if (_position == value) return;
            _position = value;
            RaiseChanged();
        }
    }

    public Vector3 Target
    {
        get => _target;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            value = Guard3D.Finite(value, nameof(value));
            if (value == _position) throw new ArgumentException("Camera position and target must differ.", nameof(value));
            if (_target == value) return;
            _target = value;
            RaiseChanged();
        }
    }

    public Vector3 Up
    {
        get => _up;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            value = Guard3D.Finite(value, nameof(value));
            if (value.LengthSquared() <= 0.000001f) throw new ArgumentOutOfRangeException(nameof(value), value, "Camera up vector must be non-zero.");
            value = Vector3.Normalize(value);
            if (_up == value) return;
            _up = value;
            RaiseChanged();
        }
    }

    public float FieldOfViewDegrees
    {
        get => _fieldOfViewDegrees;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            var clamped = Guard3D.Range(value, 10f, 120f, nameof(value));
            if (MathF.Abs(_fieldOfViewDegrees - clamped) < 0.0001f) return;
            _fieldOfViewDegrees = clamped;
            RaiseChanged();
        }
    }

    public float NearPlane
    {
        get => _nearPlane;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            var validated = Guard3D.Range(value, 0.001f, 10f, nameof(value));
            if (validated >= _farPlane) throw new ArgumentOutOfRangeException(nameof(value), value, "Near plane must be less than the far plane.");
            if (MathF.Abs(_nearPlane - validated) < 0.0001f) return;
            _nearPlane = validated;
            RaiseChanged();
        }
    }

    public float FarPlane
    {
        get => _farPlane;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            var validated = Guard3D.Finite(value, nameof(value));
            if (validated <= _nearPlane) throw new ArgumentOutOfRangeException(nameof(value), value, "Far plane must be greater than the near plane.");
            if (MathF.Abs(_farPlane - validated) < 0.0001f) return;
            _farPlane = validated;
            RaiseChanged();
        }
    }

    public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(_position, _target, SafeUp);

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        aspectRatio = Guard3D.Positive(aspectRatio, nameof(aspectRatio));
        return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewDegrees * (MathF.PI / 180f), aspectRatio, NearPlane, FarPlane);
    }

    public Vector3 Forward => Vector3.Normalize(_target - _position);

    public Vector3 SafeUp
    {
        get
        {
            var up = _up;
            if (MathF.Abs(Vector3.Dot(up, Forward)) > 0.999f)
            {
                up = Vector3.UnitY;
                if (MathF.Abs(Vector3.Dot(up, Forward)) > 0.999f) up = Vector3.UnitX;
            }
            return up;
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, SafeUp));

    public void SetPose(Vector3 position, Vector3 target, Vector3 up)
    {
        using var access = OwnerScene?.EnterMutationScope() ?? default;
        position = Guard3D.Finite(position, nameof(position));
        target = Guard3D.Finite(target, nameof(target));
        up = Guard3D.Finite(up, nameof(up));
        if (position == target) throw new ArgumentException("Camera position and target must differ.", nameof(target));
        if (up.LengthSquared() <= 0.000001f) throw new ArgumentOutOfRangeException(nameof(up), up, "Camera up vector must be non-zero.");
        up = Vector3.Normalize(up);
        if (_position == position && _target == target && _up == up) return;
        _position = position;
        _target = target;
        _up = up;
        RaiseChanged();
    }

    public void Translate(Vector3 translation)
    {
        using var access = OwnerScene?.EnterMutationScope() ?? default;
        translation = Guard3D.Finite(translation, nameof(translation));
        if (translation == Vector3.Zero) return;
        _position += translation;
        _target += translation;
        RaiseChanged();
    }

    public void Orbit(float deltaYawDegrees, float deltaPitchDegrees)
    {
        using var access = OwnerScene?.EnterMutationScope() ?? default;
        deltaYawDegrees = Guard3D.Finite(deltaYawDegrees, nameof(deltaYawDegrees));
        deltaPitchDegrees = Guard3D.Finite(deltaPitchDegrees, nameof(deltaPitchDegrees));
        var offset = _position - _target;
        var yaw = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, DegreesToRadians(deltaYawDegrees));
        var pitch = Matrix4x4.CreateFromAxisAngle(Right, DegreesToRadians(deltaPitchDegrees));
        offset = Vector3.Transform(offset, pitch * yaw);
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || !float.IsFinite(offset.Z) || offset.LengthSquared() <= 0.000001f)
            throw new InvalidOperationException("Camera orbit produced a degenerate pose.");
        _position = _target + offset;
        RaiseChanged();
    }

    public void Pan(float deltaX, float deltaY, float viewportHeight)
    {
        using var access = OwnerScene?.EnterMutationScope() ?? default;
        deltaX = Guard3D.Finite(deltaX, nameof(deltaX));
        deltaY = Guard3D.Finite(deltaY, nameof(deltaY));
        viewportHeight = Guard3D.Positive(viewportHeight, nameof(viewportHeight));
        var distance = (_position - _target).Length();
        var worldUnitsPerPixel = (2f * MathF.Tan(DegreesToRadians(FieldOfViewDegrees) / 2f) * distance) / viewportHeight;
        var translation = (-Right * deltaX * worldUnitsPerPixel) + (SafeUp * deltaY * worldUnitsPerPixel);
        _position += translation;
        _target += translation;
        RaiseChanged();
    }

    public void Dolly(float amount)
    {
        using var access = OwnerScene?.EnterMutationScope() ?? default;
        amount = Guard3D.Finite(amount, nameof(amount));
        var currentDistance = (_target - _position).Length();
        var desiredDistance = global::System.Math.Clamp(currentDistance - amount, 0.5f, 50f);
        _position = _target - Forward * desiredDistance;
        RaiseChanged();
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
