using System;
using System.Numerics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Lighting;

public sealed class DirectionalLight3D
{
    private Vector3 _direction = Vector3.Normalize(new Vector3(-0.35f, -0.75f, -0.55f));
    private ColorRgba _color = ColorRgba.White;
    private float _intensity = 1f;
    private bool _isEnabled = true;

    public event EventHandler? Changed;

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

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
