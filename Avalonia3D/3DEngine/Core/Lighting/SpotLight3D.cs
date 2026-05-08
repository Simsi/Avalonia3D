using System;
using System.Numerics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

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
        set
        {
            if (_position == value) return;
            _position = value;
            RaiseChanged();
        }
    }

    public Vector3 Direction
    {
        get => _direction;
        set
        {
            if (value.LengthSquared() < 0.000001f) return;
            var normalized = Vector3.Normalize(value);
            if (_direction == normalized) return;
            _direction = normalized;
            RaiseChanged();
        }
    }

    public ColorRgba Color
    {
        get => _color;
        set
        {
            if (_color.Equals(value)) return;
            _color = value;
            RaiseChanged();
        }
    }

    public float Intensity
    {
        get => _intensity;
        set
        {
            var clamped = MathF.Max(0f, value);
            if (MathF.Abs(_intensity - clamped) < 0.0001f) return;
            _intensity = clamped;
            RaiseChanged();
        }
    }

    public float Range
    {
        get => _range;
        set
        {
            var clamped = MathF.Max(0.01f, value);
            if (MathF.Abs(_range - clamped) < 0.0001f) return;
            _range = clamped;
            RaiseChanged();
        }
    }

    public float InnerConeDegrees
    {
        get => _innerConeDegrees;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 89f);
            if (MathF.Abs(_innerConeDegrees - clamped) < 0.0001f) return;
            _innerConeDegrees = clamped;
            if (_outerConeDegrees < _innerConeDegrees) _outerConeDegrees = _innerConeDegrees;
            RaiseChanged();
        }
    }

    public float OuterConeDegrees
    {
        get => _outerConeDegrees;
        set
        {
            var clamped = global::System.Math.Clamp(value, _innerConeDegrees, 89f);
            if (MathF.Abs(_outerConeDegrees - clamped) < 0.0001f) return;
            _outerConeDegrees = clamped;
            RaiseChanged();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            RaiseChanged();
        }
    }

    public float InnerCosine => MathF.Cos(InnerConeDegrees * MathF.PI / 180f);

    public float OuterCosine => MathF.Cos(OuterConeDegrees * MathF.PI / 180f);

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
