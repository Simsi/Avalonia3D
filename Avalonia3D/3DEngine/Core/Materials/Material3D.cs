using System;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Materials;

public enum LightingMode
{
    Unlit,
    Lambert
}

public enum SurfaceMode
{
    Opaque,
    Transparent
}

public enum CullMode
{
    None,
    Back,
    Front
}

public sealed class Material3D
{
    private ColorRgba _baseColor = ColorRgba.White;
    private float _opacity = 1f;
    private LightingMode _lighting = LightingMode.Unlit;
    private SurfaceMode _surface = SurfaceMode.Opaque;
    private CullMode _cullMode = CullMode.None;
    private string? _baseColorTextureKey;

    public event EventHandler? Changed;

    public static Material3D Default { get; } = new Material3D();

    public static Material3D CreateUnlit(ColorRgba color) => new Material3D { BaseColor = color, Lighting = LightingMode.Unlit };

    public ColorRgba BaseColor
    {
        get => _baseColor;
        set
        {
            if (_baseColor.Equals(value)) return;
            _baseColor = value;
            RaiseChanged();
        }
    }

    public float Opacity
    {
        get => _opacity;
        set
        {
            var clamped = System.Math.Clamp(value, 0f, 1f);
            if (System.Math.Abs(_opacity - clamped) < 0.0001f) return;
            _opacity = clamped;
            RaiseChanged();
        }
    }

    public LightingMode Lighting
    {
        get => _lighting;
        set
        {
            if (_lighting == value) return;
            _lighting = value;
            RaiseChanged();
        }
    }

    public SurfaceMode Surface
    {
        get => _surface;
        set
        {
            if (_surface == value) return;
            _surface = value;
            RaiseChanged();
        }
    }

    public CullMode CullMode
    {
        get => _cullMode;
        set
        {
            if (_cullMode == value) return;
            _cullMode = value;
            RaiseChanged();
        }
    }

    public string? BaseColorTextureKey
    {
        get => _baseColorTextureKey;
        set
        {
            if (StringComparer.Ordinal.Equals(_baseColorTextureKey, value)) return;
            _baseColorTextureKey = value;
            RaiseChanged();
        }
    }

    public bool IsTransparent => Surface == SurfaceMode.Transparent || Opacity < 0.999f || BaseColor.A < 0.999f;

    public ColorRgba EffectiveColor => new ColorRgba(BaseColor.R, BaseColor.G, BaseColor.B, BaseColor.A * Opacity);

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
