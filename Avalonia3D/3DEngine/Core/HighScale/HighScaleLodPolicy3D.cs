using System;
using System.Numerics;

namespace ThreeDEngine.Core.HighScale;

public sealed class HighScaleLodPolicy3D
{
    public float DetailedDistance { get; set; } = 24f;
    public float SimplifiedDistance { get; set; } = 96f;
    public float ProxyDistance { get; set; } = 320f;
    public bool EnableBillboardFallback { get; set; } = true;

    public HighScaleLodLevel3D Resolve(Vector3 cameraPosition, Matrix4x4 instanceTransform)
    {
        var pos = new Vector3(instanceTransform.M41, instanceTransform.M42, instanceTransform.M43);
        var d2 = Vector3.DistanceSquared(cameraPosition, pos);
        if (d2 <= DetailedDistance * DetailedDistance)
        {
            return HighScaleLodLevel3D.Detailed;
        }

        if (d2 <= SimplifiedDistance * SimplifiedDistance)
        {
            return HighScaleLodLevel3D.Simplified;
        }

        if (d2 <= ProxyDistance * ProxyDistance)
        {
            return HighScaleLodLevel3D.Proxy;
        }

        return EnableBillboardFallback ? HighScaleLodLevel3D.Billboard : HighScaleLodLevel3D.Proxy;
    }
}
