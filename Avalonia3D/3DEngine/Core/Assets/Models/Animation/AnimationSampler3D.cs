using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class AnimationSampler3D
{
    private readonly float[] _times;
    private readonly Vector4[] _values;
    private readonly Vector4[]? _inTangents;
    private readonly Vector4[]? _outTangents;
    private readonly ReadOnlyCollection<float> _timesView;
    private readonly ReadOnlyCollection<Vector4> _valuesView;
    private readonly ReadOnlyCollection<Vector4>? _inTangentsView;
    private readonly ReadOnlyCollection<Vector4>? _outTangentsView;

    public AnimationSampler3D(float[] times, Vector4[] values, AnimationInterpolation3D interpolation)
        : this(times, values, interpolation, null, null)
    {
    }

    public AnimationSampler3D(
        float[] times,
        Vector4[] values,
        AnimationInterpolation3D interpolation,
        Vector4[]? inTangents,
        Vector4[]? outTangents)
    {
        interpolation = Guard3D.Defined(interpolation, nameof(interpolation));
        var sourceTimes = times ?? throw new ArgumentNullException(nameof(times));
        var sourceValues = values ?? throw new ArgumentNullException(nameof(values));
        if (sourceTimes.Length != sourceValues.Length)
            throw new ArgumentException("Animation time and value arrays must have identical lengths.", nameof(values));
        if (interpolation == AnimationInterpolation3D.CubicSpline && sourceTimes.Length > 0)
        {
            if (inTangents is null || outTangents is null)
                throw new ArgumentException("Cubic-spline samplers require both input and output tangent arrays.", nameof(inTangents));
            if (inTangents.Length != sourceTimes.Length || outTangents.Length != sourceTimes.Length)
                throw new ArgumentException("Cubic-spline tangent arrays must match the key count.", nameof(inTangents));
        }
        else if (interpolation != AnimationInterpolation3D.CubicSpline && (inTangents is not null || outTangents is not null))
        {
            throw new ArgumentException("Tangents are valid only for cubic-spline interpolation.", nameof(inTangents));
        }

        var count = sourceTimes.Length;
        var keys = new Key[count];
        for (var i = 0; i < count; i++)
        {
            var time = Guard3D.Finite(sourceTimes[i], nameof(times));
            var value = Guard3D.Finite(sourceValues[i], nameof(values));
            var inTangent = inTangents is null ? default : Guard3D.Finite(inTangents[i], nameof(inTangents));
            var outTangent = outTangents is null ? default : Guard3D.Finite(outTangents[i], nameof(outTangents));
            keys[i] = new Key(time, value, inTangent, outTangent);
        }
        Array.Sort(keys, static (a, b) => a.Time.CompareTo(b.Time));
        for (var i = 1; i < keys.Length; i++)
        {
            if (keys[i].Time <= keys[i - 1].Time)
                throw new ArgumentException("Animation key times must be unique after sorting.", nameof(times));
        }

        _times = new float[count];
        _values = new Vector4[count];
        if (interpolation == AnimationInterpolation3D.CubicSpline && count > 0)
        {
            _inTangents = new Vector4[count];
            _outTangents = new Vector4[count];
        }
        for (var i = 0; i < count; i++)
        {
            _times[i] = keys[i].Time;
            _values[i] = keys[i].Value;
            if (_inTangents is not null)
            {
                _inTangents[i] = keys[i].InTangent;
                _outTangents![i] = keys[i].OutTangent;
            }
        }

        _timesView = Array.AsReadOnly(_times);
        _valuesView = Array.AsReadOnly(_values);
        _inTangentsView = _inTangents is null ? null : Array.AsReadOnly(_inTangents);
        _outTangentsView = _outTangents is null ? null : Array.AsReadOnly(_outTangents);
        Interpolation = interpolation;
        Duration = count == 0 ? 0f : _times[^1];
    }

    public IReadOnlyList<float> Times => _timesView;
    public IReadOnlyList<Vector4> Values => _valuesView;
    public IReadOnlyList<Vector4>? InTangents => _inTangentsView;
    public IReadOnlyList<Vector4>? OutTangents => _outTangentsView;
    public AnimationInterpolation3D Interpolation { get; }
    public float Duration { get; }
    public int KeyCount => _times.Length;

    public Vector4 Evaluate(float time, Vector4 fallback)
    {
        Guard3D.Finite(time, nameof(time));
        var count = KeyCount;
        if (count == 0) return fallback;
        if (time <= _times[0]) return _values[0];
        if (time >= _times[count - 1]) return _values[count - 1];
        var upper = FindUpperKey(time, count);
        var lower = upper - 1;
        if (Interpolation == AnimationInterpolation3D.Step) return _values[lower];
        var span = _times[upper] - _times[lower];
        var t = (time - _times[lower]) / span;
        return Interpolation == AnimationInterpolation3D.CubicSpline
            ? EvaluateCubic(lower, upper, t, span)
            : Vector4.Lerp(_values[lower], _values[upper], t);
    }

    public Quaternion EvaluateQuaternion(float time, Quaternion fallback)
    {
        Guard3D.Finite(time, nameof(time));
        var count = KeyCount;
        if (count == 0) return fallback;
        if (time <= _times[0]) return ToQuaternion(_values[0]);
        if (time >= _times[count - 1]) return ToQuaternion(_values[count - 1]);
        var upper = FindUpperKey(time, count);
        var lower = upper - 1;
        var lowerQ = ToQuaternion(_values[lower]);
        if (Interpolation == AnimationInterpolation3D.Step) return lowerQ;
        var span = _times[upper] - _times[lower];
        var t = (time - _times[lower]) / span;
        if (Interpolation == AnimationInterpolation3D.CubicSpline)
        {
            var value = EvaluateCubic(lower, upper, t, span);
            return ToQuaternion(value);
        }
        return Quaternion.Normalize(Quaternion.Slerp(lowerQ, ToQuaternion(_values[upper]), t));
    }

    private Vector4 EvaluateCubic(int lower, int upper, float t, float span)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = 2f * t3 - 3f * t2 + 1f;
        var h10 = t3 - 2f * t2 + t;
        var h01 = -2f * t3 + 3f * t2;
        var h11 = t3 - t2;
        return h00 * _values[lower]
            + h10 * span * _outTangents![lower]
            + h01 * _values[upper]
            + h11 * span * _inTangents![upper];
    }

    private int FindUpperKey(float time, int count)
    {
        var low = 1;
        var high = count - 1;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (_times[mid] < time) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    private static Quaternion ToQuaternion(Vector4 value)
    {
        var q = new Quaternion(value.X, value.Y, value.Z, value.W);
        var lengthSquared = q.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
            throw new InvalidOperationException("Animation sampler produced a degenerate quaternion.");
        return Quaternion.Normalize(q);
    }

    private readonly record struct Key(float Time, Vector4 Value, Vector4 InTangent, Vector4 OutTangent);
}
