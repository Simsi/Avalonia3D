using System;
using System.Numerics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Lighting;

public sealed class DirectionalLight3D
{
    private Vector3 _direction = Vector3.Normalize(new Vector3(-0.35f, -0.75f, -0.55f));
    private ColorRgba _color = ColorRgba.White;
    private float _intensity = 1f;
    private bool _isEnabled = true;

    internal Scene3D? OwnerScene { get; set; }
    public event EventHandler? Changed;

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
        set
        {
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            value = Guard3D.Color(value, nameof(Color));
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
            using var access = OwnerScene?.EnterMutationScope() ?? default;
            value = Guard3D.NonNegative(value, nameof(Intensity));
            if (MathF.Abs(_intensity - value) < 0.0001f) return;
            _intensity = value;
            RaiseChanged();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { using var access = OwnerScene?.EnterMutationScope() ?? default; if (_isEnabled == value) return; _isEnabled = value; RaiseChanged(); }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
