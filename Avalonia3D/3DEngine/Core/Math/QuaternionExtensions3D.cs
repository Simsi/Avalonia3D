using System;
using System.Numerics;

namespace ThreeDEngine.Core.Math;

public static class QuaternionExtensions3D
{
    public static Vector3 ToEulerDegrees(this Quaternion q)
    {
        q = q.LengthSquared() < 0.000001f ? Quaternion.Identity : Quaternion.Normalize(q);

        var sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
        var cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        var roll = MathF.Atan2(sinrCosp, cosrCosp);

        var sinp = 2f * (q.W * q.Y - q.Z * q.X);
        var pitch = MathF.Abs(sinp) >= 1f
            ? (sinp >= 0f ? MathF.PI / 2f : -MathF.PI / 2f)
            : MathF.Asin(sinp);

        var sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
        var cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        var yaw = MathF.Atan2(sinyCosp, cosyCosp);

        const float radiansToDegrees = 180f / MathF.PI;
        return new Vector3(pitch * radiansToDegrees, yaw * radiansToDegrees, roll * radiansToDegrees);
    }
}
