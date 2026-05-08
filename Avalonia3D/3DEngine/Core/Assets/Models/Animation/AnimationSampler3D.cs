using System;
using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class AnimationSampler3D
{
    public AnimationSampler3D(float[] times, Vector4[] values, AnimationInterpolation3D interpolation)
    {
        var sourceTimes = times ?? Array.Empty<float>();
        var sourceValues = values ?? Array.Empty<Vector4>();
        var count = global::System.Math.Min(sourceTimes.Length, sourceValues.Length);
        if (count == 0)
        {
            Times = Array.Empty<float>();
            Values = Array.Empty<Vector4>();
        }
        else
        {
            var pairs = new (float Time, Vector4 Value)[count];
            for (var i = 0; i < count; i++) pairs[i] = (sourceTimes[i], sourceValues[i]);
            Array.Sort(pairs, static (a, b) => a.Time.CompareTo(b.Time));
            Times = new float[count];
            Values = new Vector4[count];
            for (var i = 0; i < count; i++)
            {
                Times[i] = pairs[i].Time;
                Values[i] = pairs[i].Value;
            }
        }

        Interpolation = interpolation;
        Duration = Times.Length == 0 ? 0f : Times[^1];
    }

    public float[] Times { get; }
    public Vector4[] Values { get; }
    public AnimationInterpolation3D Interpolation { get; }
    public float Duration { get; }
    public int KeyCount => global::System.Math.Min(Times.Length, Values.Length);

    public Vector4 Evaluate(float time, Vector4 fallback)
    {
        var count = KeyCount;
        if (count == 0) return fallback;
        if (time <= Times[0]) return Values[0];
        if (time >= Times[count - 1]) return Values[count - 1];

        var upper = FindUpperKey(time, count);
        var lower = global::System.Math.Max(0, upper - 1);
        if (Interpolation == AnimationInterpolation3D.Step || upper >= count) return Values[lower];

        var span = Times[upper] - Times[lower];
        var t = span <= 0.000001f ? 0f : (time - Times[lower]) / span;
        return Vector4.Lerp(Values[lower], Values[upper], t);
    }

    public Quaternion EvaluateQuaternion(float time, Quaternion fallback)
    {
        var count = KeyCount;
        if (count == 0) return fallback;
        if (time <= Times[0]) return ToQuaternion(Values[0], fallback);
        if (time >= Times[count - 1]) return ToQuaternion(Values[count - 1], fallback);

        var upper = FindUpperKey(time, count);
        var lower = global::System.Math.Max(0, upper - 1);
        var lowerQ = ToQuaternion(Values[lower], fallback);
        if (Interpolation == AnimationInterpolation3D.Step || upper >= count) return lowerQ;

        var upperQ = ToQuaternion(Values[upper], fallback);
        var span = Times[upper] - Times[lower];
        var t = span <= 0.000001f ? 0f : (time - Times[lower]) / span;
        return Quaternion.Normalize(Quaternion.Slerp(lowerQ, upperQ, t));
    }

    private int FindUpperKey(float time, int count)
    {
        var low = 1;
        var high = count - 1;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (Times[mid] < time) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    private static Quaternion ToQuaternion(Vector4 value, Quaternion fallback)
    {
        var q = new Quaternion(value.X, value.Y, value.Z, value.W);
        return q.LengthSquared() > 0.000001f ? Quaternion.Normalize(q) : fallback;
    }
}
