using System;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Materials;

public enum LightingMode
{
    Unlit = 0,
    Lambert = 1,
    Phong = 2,
    BlinnPhong = 3
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
    private ColorRgba _specularColor = ColorRgba.White;
    private float _opacity = 1f;
    private float _ambientStrength = 1f;
    private float _diffuseStrength = 1f;
    private float _specularStrength = 0.35f;
    private float _shininess = 32f;
    private float _metallic = 0f;
    private float _roughness = 1f;
    private LightingMode _lighting = LightingMode.Unlit;
    private SurfaceMode _surface = SurfaceMode.Opaque;
    private CullMode _cullMode = CullMode.None;
    private string? _baseColorTextureKey;
    private string? _normalMapTextureKey;
    private byte[]? _normalMapTextureData;
    private string? _normalMapTextureMimeType;
    private int _normalMapTextureVersion;
    private byte[]? _baseColorTextureData;
    private string? _baseColorTextureMimeType;
    private int _baseColorTextureVersion;
    private string? _metallicRoughnessTextureKey;
    private byte[]? _metallicRoughnessTextureData;
    private string? _metallicRoughnessTextureMimeType;
    private int _metallicRoughnessTextureVersion;
    private string? _emissiveTextureKey;
    private byte[]? _emissiveTextureData;
    private string? _emissiveTextureMimeType;
    private int _emissiveTextureVersion;
    private ColorRgba _emissiveColor = ColorRgba.Transparent;
    private float _alphaCutoff = 0.5f;
    private bool _doubleSided;
    private float _normalMapStrength;

    public event EventHandler? Changed;

    /// <summary>
    /// Monotonic material signature version. Render-side caches use it to avoid rebuilding
    /// material bindings and string keys for unchanged materials on every frame.
    /// </summary>
    public int Version { get; private set; }

    public static Material3D Default { get; } = new Material3D();

    public static Material3D CreateUnlit(ColorRgba color) => new Material3D { BaseColor = color, Lighting = LightingMode.Unlit };

    public static Material3D CreateLambert(ColorRgba color) => new Material3D { BaseColor = color, Lighting = LightingMode.Lambert };

    public static Material3D CreatePhong(ColorRgba color, float specularStrength = 0.35f, float shininess = 32f)
        => new Material3D
        {
            BaseColor = color,
            Lighting = LightingMode.Phong,
            SpecularStrength = specularStrength,
            Shininess = shininess
        };

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

    public ColorRgba SpecularColor
    {
        get => _specularColor;
        set
        {
            if (_specularColor.Equals(value)) return;
            _specularColor = value;
            RaiseChanged();
        }
    }

    public float Opacity
    {
        get => _opacity;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 1f);
            if (global::System.Math.Abs(_opacity - clamped) < 0.0001f) return;
            _opacity = clamped;
            RaiseChanged();
        }
    }

    public float AmbientStrength
    {
        get => _ambientStrength;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 4f);
            if (global::System.Math.Abs(_ambientStrength - clamped) < 0.0001f) return;
            _ambientStrength = clamped;
            RaiseChanged();
        }
    }

    public float DiffuseStrength
    {
        get => _diffuseStrength;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 4f);
            if (global::System.Math.Abs(_diffuseStrength - clamped) < 0.0001f) return;
            _diffuseStrength = clamped;
            RaiseChanged();
        }
    }

    public float SpecularStrength
    {
        get => _specularStrength;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 4f);
            if (global::System.Math.Abs(_specularStrength - clamped) < 0.0001f) return;
            _specularStrength = clamped;
            RaiseChanged();
        }
    }

    public float Shininess
    {
        get => _shininess;
        set
        {
            var clamped = global::System.Math.Clamp(value, 1f, 512f);
            if (global::System.Math.Abs(_shininess - clamped) < 0.0001f) return;
            _shininess = clamped;
            RaiseChanged();
        }
    }

    public float Metallic
    {
        get => _metallic;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 1f);
            if (global::System.Math.Abs(_metallic - clamped) < 0.0001f) return;
            _metallic = clamped;
            RaiseChanged();
        }
    }

    public float Roughness
    {
        get => _roughness;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 1f);
            if (global::System.Math.Abs(_roughness - clamped) < 0.0001f) return;
            _roughness = clamped;
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


    public byte[]? BaseColorTextureData
    {
        get => _baseColorTextureData;
        set
        {
            if (ReferenceEquals(_baseColorTextureData, value)) return;
            _baseColorTextureData = value is { Length: > 0 } ? (byte[])value.Clone() : null;
            _baseColorTextureVersion++;
            RaiseChanged();
        }
    }

    public string? BaseColorTextureMimeType
    {
        get => _baseColorTextureMimeType;
        set
        {
            if (StringComparer.Ordinal.Equals(_baseColorTextureMimeType, value)) return;
            _baseColorTextureMimeType = string.IsNullOrWhiteSpace(value) ? null : value;
            _baseColorTextureVersion++;
            RaiseChanged();
        }
    }

    public int BaseColorTextureVersion => _baseColorTextureVersion;

    public bool HasBaseColorTexture => !string.IsNullOrWhiteSpace(BaseColorTextureKey) && BaseColorTextureData is { Length: > 0 };

    public void SetBaseColorTexture(string textureKey, byte[] textureData, string? mimeType = null)
    {
        BaseColorTextureKey = textureKey;
        _baseColorTextureData = textureData is { Length: > 0 } ? (byte[])textureData.Clone() : null;
        _baseColorTextureMimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType;
        _baseColorTextureVersion++;
        RaiseChanged();
    }


    public string? MetallicRoughnessTextureKey
    {
        get => _metallicRoughnessTextureKey;
        set
        {
            if (StringComparer.Ordinal.Equals(_metallicRoughnessTextureKey, value)) return;
            _metallicRoughnessTextureKey = value;
            RaiseChanged();
        }
    }

    public byte[]? MetallicRoughnessTextureData
    {
        get => _metallicRoughnessTextureData;
        set
        {
            if (ReferenceEquals(_metallicRoughnessTextureData, value)) return;
            _metallicRoughnessTextureData = value is { Length: > 0 } ? (byte[])value.Clone() : null;
            _metallicRoughnessTextureVersion++;
            RaiseChanged();
        }
    }

    public string? MetallicRoughnessTextureMimeType
    {
        get => _metallicRoughnessTextureMimeType;
        set
        {
            if (StringComparer.Ordinal.Equals(_metallicRoughnessTextureMimeType, value)) return;
            _metallicRoughnessTextureMimeType = string.IsNullOrWhiteSpace(value) ? null : value;
            _metallicRoughnessTextureVersion++;
            RaiseChanged();
        }
    }

    public int MetallicRoughnessTextureVersion => _metallicRoughnessTextureVersion;
    public bool HasMetallicRoughnessTexture => !string.IsNullOrWhiteSpace(MetallicRoughnessTextureKey) && MetallicRoughnessTextureData is { Length: > 0 };

    public void SetMetallicRoughnessTexture(string textureKey, byte[] textureData, string? mimeType = null)
    {
        MetallicRoughnessTextureKey = textureKey;
        _metallicRoughnessTextureData = textureData is { Length: > 0 } ? (byte[])textureData.Clone() : null;
        _metallicRoughnessTextureMimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType;
        _metallicRoughnessTextureVersion++;
        RaiseChanged();
    }

    public string? EmissiveTextureKey
    {
        get => _emissiveTextureKey;
        set
        {
            if (StringComparer.Ordinal.Equals(_emissiveTextureKey, value)) return;
            _emissiveTextureKey = value;
            RaiseChanged();
        }
    }

    public byte[]? EmissiveTextureData
    {
        get => _emissiveTextureData;
        set
        {
            if (ReferenceEquals(_emissiveTextureData, value)) return;
            _emissiveTextureData = value is { Length: > 0 } ? (byte[])value.Clone() : null;
            _emissiveTextureVersion++;
            RaiseChanged();
        }
    }

    public string? EmissiveTextureMimeType
    {
        get => _emissiveTextureMimeType;
        set
        {
            if (StringComparer.Ordinal.Equals(_emissiveTextureMimeType, value)) return;
            _emissiveTextureMimeType = string.IsNullOrWhiteSpace(value) ? null : value;
            _emissiveTextureVersion++;
            RaiseChanged();
        }
    }

    public int EmissiveTextureVersion => _emissiveTextureVersion;
    public bool HasEmissiveTexture => !string.IsNullOrWhiteSpace(EmissiveTextureKey) && EmissiveTextureData is { Length: > 0 };

    public void SetEmissiveTexture(string textureKey, byte[] textureData, string? mimeType = null)
    {
        EmissiveTextureKey = textureKey;
        _emissiveTextureData = textureData is { Length: > 0 } ? (byte[])textureData.Clone() : null;
        _emissiveTextureMimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType;
        _emissiveTextureVersion++;
        RaiseChanged();
    }

    public ColorRgba EmissiveColor
    {
        get => _emissiveColor;
        set
        {
            if (_emissiveColor.Equals(value)) return;
            _emissiveColor = value;
            RaiseChanged();
        }
    }

    public float AlphaCutoff
    {
        get => _alphaCutoff;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 1f);
            if (global::System.Math.Abs(_alphaCutoff - clamped) < 0.0001f) return;
            _alphaCutoff = clamped;
            RaiseChanged();
        }
    }

    public bool DoubleSided
    {
        get => _doubleSided;
        set
        {
            if (_doubleSided == value) return;
            _doubleSided = value;
            CullMode = value ? ThreeDEngine.Core.Materials.CullMode.None : ThreeDEngine.Core.Materials.CullMode.Back;
            RaiseChanged();
        }
    }

    public string? NormalMapTextureKey
    {
        get => _normalMapTextureKey;
        set
        {
            if (StringComparer.Ordinal.Equals(_normalMapTextureKey, value)) return;
            _normalMapTextureKey = value;
            RaiseChanged();
        }
    }

    public byte[]? NormalMapTextureData
    {
        get => _normalMapTextureData;
        set
        {
            if (ReferenceEquals(_normalMapTextureData, value)) return;
            _normalMapTextureData = value is { Length: > 0 } ? (byte[])value.Clone() : null;
            _normalMapTextureVersion++;
            RaiseChanged();
        }
    }

    public string? NormalMapTextureMimeType
    {
        get => _normalMapTextureMimeType;
        set
        {
            if (StringComparer.Ordinal.Equals(_normalMapTextureMimeType, value)) return;
            _normalMapTextureMimeType = string.IsNullOrWhiteSpace(value) ? null : value;
            _normalMapTextureVersion++;
            RaiseChanged();
        }
    }

    public int NormalMapTextureVersion => _normalMapTextureVersion;

    public void SetNormalMapTexture(string textureKey, byte[] textureData, string? mimeType = null, float strength = 1f)
    {
        NormalMapTextureKey = textureKey;
        _normalMapTextureData = textureData is { Length: > 0 } ? (byte[])textureData.Clone() : null;
        _normalMapTextureMimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType;
        _normalMapTextureVersion++;
        NormalMapStrength = strength;
        RaiseChanged();
    }

    public float NormalMapStrength
    {
        get => _normalMapStrength;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 4f);
            if (global::System.Math.Abs(_normalMapStrength - clamped) < 0.0001f) return;
            _normalMapStrength = clamped;
            RaiseChanged();
        }
    }

    public bool HasNormalMap => !string.IsNullOrWhiteSpace(NormalMapTextureKey) && NormalMapTextureData is { Length: > 0 } && NormalMapStrength > 0.0001f;

    public bool IsTransparent => Surface == SurfaceMode.Transparent || Opacity < 0.999f || BaseColor.A < 0.999f;

    public bool UsesLighting => Lighting != LightingMode.Unlit;

    public bool UsesSpecular => Lighting == LightingMode.Phong || Lighting == LightingMode.BlinnPhong;

    public ColorRgba EffectiveColor => new ColorRgba(BaseColor.R, BaseColor.G, BaseColor.B, BaseColor.A * Opacity);

    public Material3D Clone()
        => new Material3D
        {
            BaseColor = BaseColor,
            SpecularColor = SpecularColor,
            Opacity = Opacity,
            AmbientStrength = AmbientStrength,
            DiffuseStrength = DiffuseStrength,
            SpecularStrength = SpecularStrength,
            Shininess = Shininess,
            Metallic = Metallic,
            Roughness = Roughness,
            Lighting = Lighting,
            Surface = Surface,
            DoubleSided = DoubleSided,
            CullMode = CullMode,
            BaseColorTextureKey = BaseColorTextureKey,
            BaseColorTextureData = BaseColorTextureData,
            BaseColorTextureMimeType = BaseColorTextureMimeType,
            MetallicRoughnessTextureKey = MetallicRoughnessTextureKey,
            MetallicRoughnessTextureData = MetallicRoughnessTextureData,
            MetallicRoughnessTextureMimeType = MetallicRoughnessTextureMimeType,
            EmissiveTextureKey = EmissiveTextureKey,
            EmissiveTextureData = EmissiveTextureData,
            EmissiveTextureMimeType = EmissiveTextureMimeType,
            EmissiveColor = EmissiveColor,
            AlphaCutoff = AlphaCutoff,
            NormalMapTextureKey = NormalMapTextureKey,
            NormalMapTextureData = NormalMapTextureData,
            NormalMapTextureMimeType = NormalMapTextureMimeType,
            NormalMapStrength = NormalMapStrength
        };

    private void RaiseChanged()
    {
        unchecked { Version++; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
