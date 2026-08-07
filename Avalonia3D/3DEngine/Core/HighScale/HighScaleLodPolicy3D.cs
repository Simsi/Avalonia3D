using System;
using System.Numerics;
using ThreeDEngine.Core.Validation;

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
        set => SetDistances(value, _simplifiedDistance, _proxyDistance, _drawDistance, _fadeDistance);
    }

    public float SimplifiedDistance
    {
        get => _simplifiedDistance;
        set => SetDistances(_detailedDistance, value, _proxyDistance, _drawDistance, _fadeDistance);
    }

    public float ProxyDistance
    {
        get => _proxyDistance;
        set => SetDistances(_detailedDistance, _simplifiedDistance, value, _drawDistance, _fadeDistance);
    }

    /// <summary>
    /// Hard render distance for this high-scale layer. Objects farther than this are not submitted.
    /// Keep this synchronized with Camera.FarPlane or set Camera.FarPlane slightly larger.
    /// </summary>
    public float DrawDistance
    {
        get => _drawDistance;
        set => SetDistances(_detailedDistance, _simplifiedDistance, _proxyDistance, value, _fadeDistance);
    }

    /// <summary>Distance band used by renderers for dithered fade near DrawDistance.</summary>
    public float FadeDistance
    {
        get => _fadeDistance;
        set => SetDistances(_detailedDistance, _simplifiedDistance, _proxyDistance, _drawDistance, value);
    }

    /// <summary>
    /// If true, far objects are reported as Billboard. A backend that cannot render billboards
    /// must reject the selected LOD instead of silently drawing proxy geometry.
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

    public void SetDistances(float detailed, float simplified, float proxy, float draw, float fade)
    {
        Guard3D.Positive(detailed, nameof(detailed));
        Guard3D.Positive(simplified, nameof(simplified));
        Guard3D.Positive(proxy, nameof(proxy));
        Guard3D.Positive(draw, nameof(draw));
        Guard3D.NonNegative(fade, nameof(fade));
        if (!(detailed < simplified && simplified < proxy && proxy < draw))
            throw new ArgumentException("LOD distances must satisfy detailed < simplified < proxy < draw.");
        if (fade > draw)
            throw new ArgumentOutOfRangeException(nameof(fade), fade, "Fade distance cannot exceed draw distance.");
        if (_detailedDistance == detailed && _simplifiedDistance == simplified && _proxyDistance == proxy &&
            _drawDistance == draw && _fadeDistance == fade)
        {
            return;
        }

        _detailedDistance = detailed;
        _simplifiedDistance = simplified;
        _proxyDistance = proxy;
        _drawDistance = draw;
        _fadeDistance = fade;
        _version++;
    }

    public HighScaleLodLevel3D Resolve(Vector3 cameraPosition, Matrix4x4 instanceTransform)
    {
        Guard3D.Finite(cameraPosition, nameof(cameraPosition));
        Guard3D.FiniteMatrix(instanceTransform, nameof(instanceTransform), requireInvertible: true);
        var pos = new Vector3(instanceTransform.M41, instanceTransform.M42, instanceTransform.M43);
        var d2 = Vector3.DistanceSquared(cameraPosition, pos);
        if (d2 > DrawDistance * DrawDistance) return HighScaleLodLevel3D.Culled;
        if (d2 <= DetailedDistance * DetailedDistance) return HighScaleLodLevel3D.Detailed;
        if (d2 <= SimplifiedDistance * SimplifiedDistance) return HighScaleLodLevel3D.Simplified;
        if (d2 <= ProxyDistance * ProxyDistance) return HighScaleLodLevel3D.Proxy;
        return EnableBillboardFallback ? HighScaleLodLevel3D.Billboard : HighScaleLodLevel3D.Proxy;
    }

    public float ResolveFadeAlpha(Vector3 cameraPosition, Matrix4x4 instanceTransform)
    {
        Guard3D.Finite(cameraPosition, nameof(cameraPosition));
        Guard3D.FiniteMatrix(instanceTransform, nameof(instanceTransform), requireInvertible: true);
        if (FadeDistance <= 0.001f) return 1f;

        var pos = new Vector3(instanceTransform.M41, instanceTransform.M42, instanceTransform.M43);
        var distance = Vector3.Distance(cameraPosition, pos);
        var fadeStart = MathF.Max(0f, DrawDistance - FadeDistance);
        if (distance <= fadeStart) return 1f;
        if (distance >= DrawDistance) return 0f;
        var t = (distance - fadeStart) / FadeDistance;
        return System.Math.Clamp(1f - t, 0f, 1f);
    }
}
