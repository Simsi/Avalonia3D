using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Classic render-frame interpolation between simulation/telemetry ticks.
/// This is deterministic smoothing, not AI/synthetic frame generation.
/// </summary>
public sealed class FrameInterpolator3D
{
    private readonly Dictionary<string, Matrix4x4> _previous = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Matrix4x4> _current = new(StringComparer.Ordinal);
    private readonly List<string> _staleKeys = new();
    private long _currentTickTimestamp;

    public bool Enabled { get; set; }
    public double SimulationTickFps { get; set; } = 20d;
    public double Alpha { get; private set; } = 1d;

    public void BeginTick(Scene3D scene)
    {
        if (!Enabled) return;
        _previous.Clear();
        foreach (var pair in _current)
        {
            _previous[pair.Key] = pair.Value;
        }
    }

    public void EndTick(Scene3D scene)
    {
        if (!Enabled) return;
        _current.Clear();
        foreach (var obj in scene.Registry.Renderables)
        {
            _current[obj.Id] = obj.GetModelMatrix();
        }
        _currentTickTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        Alpha = 0d;
        RemoveStalePreviousKeys();
    }

    public void UpdateAlpha()
    {
        if (!Enabled || _currentTickTimestamp == 0)
        {
            Alpha = 1d;
            return;
        }
        var tickMs = 1000d / System.Math.Clamp(SimulationTickFps, 1d, 240d);
        var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _currentTickTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        Alpha = System.Math.Clamp(elapsedMs / tickMs, 0d, 1d);
    }

    public bool TryGetInterpolatedModel(string objectId, out Matrix4x4 model)
    {
        model = Matrix4x4.Identity;
        if (!Enabled || !_current.TryGetValue(objectId, out var current))
        {
            return false;
        }
        if (!_previous.TryGetValue(objectId, out var previous) || Alpha >= 0.999d)
        {
            model = current;
            return true;
        }
        model = Interpolate(previous, current, (float)Alpha);
        return true;
    }

    public void Reset()
    {
        _previous.Clear();
        _current.Clear();
        _staleKeys.Clear();
        _currentTickTimestamp = 0;
        Alpha = 1d;
    }

    private void RemoveStalePreviousKeys()
    {
        _staleKeys.Clear();
        foreach (var key in _previous.Keys)
        {
            if (!_current.ContainsKey(key))
            {
                _staleKeys.Add(key);
            }
        }
        foreach (var key in _staleKeys)
        {
            _previous.Remove(key);
        }
    }

    private static Matrix4x4 Interpolate(Matrix4x4 from, Matrix4x4 to, float t)
    {
        if (Matrix4x4.Decompose(from, out var scaleFrom, out var rotFrom, out var posFrom) &&
            Matrix4x4.Decompose(to, out var scaleTo, out var rotTo, out var posTo))
        {
            var scale = Vector3.Lerp(scaleFrom, scaleTo, t);
            var rotation = Quaternion.Slerp(rotFrom, rotTo, t);
            var position = Vector3.Lerp(posFrom, posTo, t);
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }
        return Matrix4x4.Lerp(from, to, t);
    }
}
