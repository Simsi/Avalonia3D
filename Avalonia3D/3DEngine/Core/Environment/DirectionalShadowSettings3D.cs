using System;

namespace ThreeDEngine.Core.Environment;

public sealed class DirectionalShadowSettings3D
{
    private bool _isEnabled;
    private int _resolution = 1024;
    private float _distance = 30f;
    private float _strength = 0.55f;
    private float _bias = 0.0035f;
    private float _normalBias = 0.015f;
    private float _orthographicSize = 18f;
    private bool _debugShowShadowMap;

    public event EventHandler? Changed;

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

    public int Resolution
    {
        get => _resolution;
        set
        {
            var clamped = global::System.Math.Clamp(value, 128, 4096);
            if (_resolution == clamped) return;
            _resolution = clamped;
            RaiseChanged();
        }
    }

    public float Distance
    {
        get => _distance;
        set
        {
            var clamped = MathF.Max(1f, value);
            if (MathF.Abs(_distance - clamped) < 0.0001f) return;
            _distance = clamped;
            RaiseChanged();
        }
    }

    public float Strength
    {
        get => _strength;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 1f);
            if (MathF.Abs(_strength - clamped) < 0.0001f) return;
            _strength = clamped;
            RaiseChanged();
        }
    }

    public float Bias
    {
        get => _bias;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 0.1f);
            if (MathF.Abs(_bias - clamped) < 0.00001f) return;
            _bias = clamped;
            RaiseChanged();
        }
    }

    public float NormalBias
    {
        get => _normalBias;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 1f);
            if (MathF.Abs(_normalBias - clamped) < 0.0001f) return;
            _normalBias = clamped;
            RaiseChanged();
        }
    }

    public float OrthographicSize
    {
        get => _orthographicSize;
        set
        {
            var clamped = MathF.Max(1f, value);
            if (MathF.Abs(_orthographicSize - clamped) < 0.0001f) return;
            _orthographicSize = clamped;
            RaiseChanged();
        }
    }

    public bool DebugShowShadowMap
    {
        get => _debugShowShadowMap;
        set
        {
            if (_debugShowShadowMap == value) return;
            _debugShowShadowMap = value;
            RaiseChanged();
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
