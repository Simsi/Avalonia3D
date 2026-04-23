using System;
using System.Numerics;

namespace ThreeDEngine.Core.Scene;

public sealed class Camera3D
{
    private float _fieldOfViewDegrees = 55f;
    private float _nearPlane = 0.1f;
    private float _farPlane = 100f;
    private Vector3 _position = new Vector3(0f, 0f, 6f);
    private Vector3 _target = Vector3.Zero;
    private Vector3 _up = Vector3.UnitY;

    public event EventHandler? Changed;

    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_position == value)
            {
                return;
            }

            _position = value;
            RaiseChanged();
        }
    }

    public Vector3 Target
    {
        get => _target;
        set
        {
            if (_target == value)
            {
                return;
            }

            _target = value;
            RaiseChanged();
        }
    }

    public Vector3 Up
    {
        get => _up;
        set
        {
            if (_up == value)
            {
                return;
            }

            _up = value;
            RaiseChanged();
        }
    }

    public float FieldOfViewDegrees
    {
        get => _fieldOfViewDegrees;
        set
        {
            var clamped = System.Math.Clamp(value, 10f, 120f);
            if (System.MathF.Abs(_fieldOfViewDegrees - clamped) < float.Epsilon)
            {
                return;
            }

            _fieldOfViewDegrees = clamped;
            RaiseChanged();
        }
    }

    public float NearPlane
    {
        get => _nearPlane;
        set
        {
            var clamped = System.Math.Clamp(value, 0.001f, 10f);
            if (System.MathF.Abs(_nearPlane - clamped) < float.Epsilon)
            {
                return;
            }

            _nearPlane = clamped;
            RaiseChanged();
        }
    }

    public float FarPlane
    {
        get => _farPlane;
        set
        {
            var clamped = System.Math.Max(value, NearPlane + 1f);
            if (System.MathF.Abs(_farPlane - clamped) < float.Epsilon)
            {
                return;
            }

            _farPlane = clamped;
            RaiseChanged();
        }
    }

    public Matrix4x4 GetViewMatrix()
        => Matrix4x4.CreateLookAt(Position, Target, Up);

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        aspectRatio = aspectRatio <= 0f ? 1f : aspectRatio;
        return Matrix4x4.CreatePerspectiveFieldOfView(
            FieldOfViewDegrees * (System.MathF.PI / 180f),
            aspectRatio,
            NearPlane,
            FarPlane);
    }

    public Vector3 Forward => Vector3.Normalize(Target - Position);

    public Vector3 Right
    {
        get
        {
            var right = Vector3.Cross(Up, Forward);
            if (right.LengthSquared() < 0.0001f)
            {
                return Vector3.UnitX;
            }

            return Vector3.Normalize(right);
        }
    }

    public void Orbit(float deltaYawDegrees, float deltaPitchDegrees)
    {
        var offset = Position - Target;
        var yaw = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, DegreesToRadians(deltaYawDegrees));
        var pitch = Matrix4x4.CreateFromAxisAngle(Right, DegreesToRadians(deltaPitchDegrees));

        offset = Vector3.Transform(offset, pitch * yaw);
        if (offset.LengthSquared() < 0.001f)
        {
            offset = Vector3.Normalize(offset) * 0.001f;
        }

        _position = Target + offset;
        RaiseChanged();
    }

    public void Pan(float deltaX, float deltaY, float viewportHeight)
    {
        viewportHeight = System.Math.Max(viewportHeight, 1f);

        var distance = System.Math.Max((Position - Target).Length(), 0.1f);
        var worldUnitsPerPixel = (2f * System.MathF.Tan(DegreesToRadians(FieldOfViewDegrees) / 2f) * distance) / viewportHeight;

        var translation =
            (-Right * deltaX * worldUnitsPerPixel) +
            (Up * deltaY * worldUnitsPerPixel);

        _position += translation;
        _target += translation;
        RaiseChanged();
    }

    public void Dolly(float amount)
    {
        var forward = Forward;
        var currentDistance = (Target - Position).Length();
        var desiredDistance = System.Math.Clamp(currentDistance - amount, 0.5f, 50f);
        _position = Target - forward * desiredDistance;
        RaiseChanged();
    }

    private static float DegreesToRadians(float degrees) => degrees * (System.MathF.PI / 180f);

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
