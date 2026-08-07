using System;
using System.Numerics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Lighting;

public sealed class PointLight3D
{
    private Vector3 _position = new(0f, 4f, -2f);
    private ColorRgba _color = ColorRgba.White;
    private float _intensity = 2.5f;
    private float _range = 12f;
    private bool _isEnabled = true;

    internal Scene3D? OwnerScene { get; set; }
    public event EventHandler? Changed;

    public Vector3 Position
    {
        get => _position;
        set { using var access = OwnerScene?.EnterMutationScope() ?? default; value = Guard3D.Finite(value, nameof(Position)); if (_position == value) return; _position = value; RaiseChanged(); }
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

    public bool IsEnabled
    {
        get => _isEnabled;
        set { using var access = OwnerScene?.EnterMutationScope() ?? default; if (_isEnabled == value) return; _isEnabled = value; RaiseChanged(); }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
