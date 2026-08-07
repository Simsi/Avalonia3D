using System;
using System.Numerics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Lighting;

public sealed class SpotLight3D
{
    private Vector3 _position = new(0f, 5f, -3f);
    private Vector3 _direction = Vector3.Normalize(new Vector3(0f, -1f, 0.25f));
    private ColorRgba _color = ColorRgba.White;
    private float _intensity = 3f;
    private float _range = 14f;
    private float _innerConeDegrees = 18f;
    private float _outerConeDegrees = 32f;
    private bool _isEnabled = true;

    internal Scene3D? OwnerScene { get; set; }
    public event EventHandler? Changed;

    public Vector3 Position
    {
        get => _position;
        set { using var access = OwnerScene?.EnterMutationScope() ?? default; value = Guard3D.Finite(value, nameof(Position)); if (_position == value) return; _position = value; RaiseChanged(); }
    }

    public Vector3 Direction
    {
        get => _direction;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            var finite = Guard3D.Finite(value, nameof(Direction));
            if (finite.LengthSquared() <= 0.000001f) throw new ArgumentOutOfRangeException(nameof(Direction), value, "Light direction must be non-zero.");
            var normalized = Vector3.Normalize(finite);
            if (_direction == normalized) return;
            _direction = normalized;
            RaiseChanged();
        }
    }

    public ColorRgba Color
    {
        get => _color;
        set { using var access = OwnerScene?.EnterMutationScope() ?? default; value = Guard3D.Color(value, nameof(Color)); if (_color.Equals(value)) return; _color = value; RaiseChanged(); }
    }

    public float Intensity
    {
        get => _intensity;
        set { using var access = OwnerScene?.EnterMutationScope() ?? default; value = Guard3D.NonNegative(value, nameof(Intensity)); if (MathF.Abs(_intensity - value) < 0.0001f) return; _intensity = value; RaiseChanged(); }
    }

    public float Range
    {
        get => _range;
        set { using var access = OwnerScene?.EnterMutationScope() ?? default; value = Guard3D.Positive(value, nameof(Range)); if (MathF.Abs(_range - value) < 0.0001f) return; _range = value; RaiseChanged(); }
    }

    public float InnerConeDegrees
    {
        get => _innerConeDegrees;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            value = Guard3D.Range(value, 0f, 89f, nameof(InnerConeDegrees));
            if (value > _outerConeDegrees) throw new ArgumentOutOfRangeException(nameof(InnerConeDegrees), value, "Inner cone cannot exceed the outer cone.");
            if (MathF.Abs(_innerConeDegrees - value) < 0.0001f) return;
            _innerConeDegrees = value;
            RaiseChanged();
        }
    }

    public float OuterConeDegrees
    {
        get => _outerConeDegrees;
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            value = Guard3D.Range(value, 0f, 89f, nameof(OuterConeDegrees));
            if (value < _innerConeDegrees) throw new ArgumentOutOfRangeException(nameof(OuterConeDegrees), value, "Outer cone cannot be smaller than the inner cone.");
            if (MathF.Abs(_outerConeDegrees - value) < 0.0001f) return;
            _outerConeDegrees = value;
            RaiseChanged();
        }
    }

    public void SetCone(float innerDegrees, float outerDegrees)
    {
        using var access = OwnerScene?.EnterMutationScope() ?? default;
        innerDegrees = Guard3D.Range(innerDegrees, 0f, 89f, nameof(innerDegrees));
        outerDegrees = Guard3D.Range(outerDegrees, 0f, 89f, nameof(outerDegrees));
        if (innerDegrees > outerDegrees) throw new ArgumentException("Inner cone cannot exceed the outer cone.");
        if (MathF.Abs(_innerConeDegrees - innerDegrees) < 0.0001f && MathF.Abs(_outerConeDegrees - outerDegrees) < 0.0001f) return;
        _innerConeDegrees = innerDegrees;
        _outerConeDegrees = outerDegrees;
        RaiseChanged();
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { using var access = OwnerScene?.EnterMutationScope() ?? default; if (_isEnabled == value) return; _isEnabled = value; RaiseChanged(); }
    }

    public float InnerCosine => MathF.Cos(InnerConeDegrees * MathF.PI / 180f);
    public float OuterCosine => MathF.Cos(OuterConeDegrees * MathF.PI / 180f);
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
