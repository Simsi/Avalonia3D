using System;
using System.Numerics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Lighting;

public sealed class PointLight3D
{
    private Vector3 _position = new Vector3(0f, 4f, -2f);
    private ColorRgba _color = ColorRgba.White;
    private float _intensity = 2.5f;
    private float _range = 12f;
    private bool _isEnabled = true;

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
