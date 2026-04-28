using System;
using System.Numerics;

namespace ThreeDEngine.Core.HighScale;

public sealed class HighScaleLodPolicy3D
{
    private float _detailedDistance = 24f;
    private float _simplifiedDistance = 96f;
    private float _proxyDistance = 320f;
    private float _drawDistance = 5000f;
    private float _fadeDistance = 80f;
    private bool _enableBillboardFallback;
    private int _version;

    public int Version => _version;

    public float DetailedDistance
    {
        get => _detailedDistance;
        set => SetDistance(ref _detailedDistance, value, 0.01f);
    }

    public float SimplifiedDistance
    {
        get => _simplifiedDistance;
        set => SetDistance(ref _simplifiedDistance, value, DetailedDistance + 0.01f);
    }

    public float ProxyDistance
    {
        get => _proxyDistance;
        set => SetDistance(ref _proxyDistance, value, SimplifiedDistance + 0.01f);
    }

    /// <summary>
    /// Hard render distance for this high-scale layer. Objects farther than this are not submitted.
    /// Keep this synchronized with Camera.FarPlane or set Camera.FarPlane slightly larger.
    /// </summary>
    public float DrawDistance
    {
        get => _drawDistance;
        set => SetDistance(ref _drawDistance, value, ProxyDistance + 0.01f);
    }

    /// <summary>
    /// Distance band used by renderers for dithered fade near DrawDistance.
    /// </summary>
    public float FadeDistance
    {
        get => _fadeDistance;
        set => SetDistance(ref _fadeDistance, value, 0f);
    }

    /// <summary>
    /// If true, far objects are reported as Billboard. Renderers without a billboard pass draw them through proxy geometry.
    /// </summary>
    public bool EnableBillboardFallback
    {
        get => _enableBillboardFallback;
        set
        {
            if (_enableBillboardFallback == value) return;
            _enableBillboardFallback = value;
            _version++;
        }
    }

    public HighScaleLodLevel3D Resolve(Vector3 cameraPosition, Matrix4x4 instanceTransform)
    {
        var pos = new Vector3(instanceTransform.M41, instanceTransform.M42, instanceTransform.M43);
        var d2 = Vector3.DistanceSquared(cameraPosition, pos);
        if (d2 > DrawDistance * DrawDistance)
        {
            return HighScaleLodLevel3D.Culled;
        }

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

    public float ResolveFadeAlpha(Vector3 cameraPosition, Matrix4x4 instanceTransform)
    {
        if (FadeDistance <= 0.001f)
        {
            return 1f;
        }

        var pos = new Vector3(instanceTransform.M41, instanceTransform.M42, instanceTransform.M43);
        var distance = Vector3.Distance(cameraPosition, pos);
        var fadeStart = MathF.Max(0f, DrawDistance - FadeDistance);
        if (distance <= fadeStart)
        {
            return 1f;
        }

        if (distance >= DrawDistance)
        {
            return 0f;
        }

        var t = (distance - fadeStart) / MathF.Max(FadeDistance, 0.001f);
        return System.Math.Clamp(1f - t, 0f, 1f);
    }

    private void SetDistance(ref float field, float value, float min)
    {
        var clamped = MathF.Max(min, value);
        if (MathF.Abs(field - clamped) < 0.0001f) return;
        field = clamped;
        _version++;
    }
}
