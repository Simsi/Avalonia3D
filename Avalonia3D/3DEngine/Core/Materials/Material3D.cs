using System;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Resources;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

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
    private float _metallic;
    private float _roughness = 1f;
    private LightingMode _lighting = LightingMode.Unlit;
    private SurfaceMode _surface = SurfaceMode.Opaque;
    private CullMode _cullMode = CullMode.None;
    private TextureResource3D? _baseColorTexture;
    private int _baseColorTextureVersion;
    private TextureResource3D? _normalMapTexture;
    private int _normalMapTextureVersion;
    private TextureResource3D? _metallicRoughnessTexture;
    private int _metallicRoughnessTextureVersion;
    private TextureResource3D? _emissiveTexture;
    private int _emissiveTextureVersion;
    private ColorRgba _emissiveColor = ColorRgba.Transparent;
    private float _alphaCutoff = 0.5f;
    private float _normalMapStrength;
    private MaterialShaderExtension3D? _shaderExtension;

    public event EventHandler? Changed;
    internal event Func<SceneAccessLease3D>? MutationScopeRequested;

    /// <summary>Monotonic material signature version.</summary>
    public int Version { get; private set; }

    public static Material3D CreateDefault() => new();
    public static Material3D CreateUnlit(ColorRgba color) => new() { BaseColor = color, Lighting = LightingMode.Unlit };
    public static Material3D CreateLambert(ColorRgba color) => new() { BaseColor = color, Lighting = LightingMode.Lambert };
    public static Material3D CreatePhong(ColorRgba color, float specularStrength = 0.35f, float shininess = 32f)
        => new() { BaseColor = color, Lighting = LightingMode.Phong, SpecularStrength = specularStrength, Shininess = shininess };

    public ColorRgba BaseColor
    {
        get => _baseColor;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Color(value, nameof(value)); if (_baseColor.Equals(value)) return; _baseColor = value; RaiseChanged(); }
    }

    public ColorRgba SpecularColor
    {
        get => _specularColor;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Color(value, nameof(value)); if (_specularColor.Equals(value)) return; _specularColor = value; RaiseChanged(); }
    }

    public float Opacity
    {
        get => _opacity;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 1f, nameof(value)); if (NearlyEqual(_opacity, v)) return; _opacity = v; RaiseChanged(); }
    }

    public float AmbientStrength
    {
        get => _ambientStrength;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 4f, nameof(value)); if (NearlyEqual(_ambientStrength, v)) return; _ambientStrength = v; RaiseChanged(); }
    }

    public float DiffuseStrength
    {
        get => _diffuseStrength;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 4f, nameof(value)); if (NearlyEqual(_diffuseStrength, v)) return; _diffuseStrength = v; RaiseChanged(); }
    }

    public float SpecularStrength
    {
        get => _specularStrength;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 4f, nameof(value)); if (NearlyEqual(_specularStrength, v)) return; _specularStrength = v; RaiseChanged(); }
    }

    public float Shininess
    {
        get => _shininess;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 1f, 512f, nameof(value)); if (NearlyEqual(_shininess, v)) return; _shininess = v; RaiseChanged(); }
    }

    public float Metallic
    {
        get => _metallic;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 1f, nameof(value)); if (NearlyEqual(_metallic, v)) return; _metallic = v; RaiseChanged(); }
    }

    public float Roughness
    {
        get => _roughness;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 1f, nameof(value)); if (NearlyEqual(_roughness, v)) return; _roughness = v; RaiseChanged(); }
    }

    public LightingMode Lighting
    {
        get => _lighting;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Defined(value, nameof(value)); if (_lighting == value) return; _lighting = value; RaiseChanged(); }
    }

    public SurfaceMode Surface
    {
        get => _surface;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Defined(value, nameof(value)); if (_surface == value) return; _surface = value; RaiseChanged(); }
    }

    public CullMode CullMode
    {
        get => _cullMode;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Defined(value, nameof(value)); if (_cullMode == value) return; _cullMode = value; RaiseChanged(); }
    }

    public TextureResource3D? BaseColorTexture => _baseColorTexture;
    public string? BaseColorTextureKey => _baseColorTexture?.LogicalKey;
    public string? BaseColorTextureResourceKey => _baseColorTexture?.ResourceKey;
    public byte[]? BaseColorTextureData => _baseColorTexture?.CopyEncodedData();
    public string? BaseColorTextureMimeType => _baseColorTexture?.MimeType;
    public int BaseColorTextureVersion => _baseColorTextureVersion;
    public bool HasBaseColorTexture => _baseColorTexture is not null;
    internal TextureResource3D? BaseColorTextureInternal => _baseColorTexture;

    public void SetBaseColorTexture(TextureResource3D texture)
        => SetTextureAtomic(ref _baseColorTexture, ref _baseColorTextureVersion, texture);

    public void SetBaseColorTexture(string textureKey, byte[] textureData, string? mimeType = null)
        => SetBaseColorTexture(TextureResource3D.Create(textureKey, textureData, mimeType));

    public void ClearBaseColorTexture()
        => ClearTexture(ref _baseColorTexture, ref _baseColorTextureVersion);

    public TextureResource3D? MetallicRoughnessTexture => _metallicRoughnessTexture;
    public string? MetallicRoughnessTextureKey => _metallicRoughnessTexture?.LogicalKey;
    public string? MetallicRoughnessTextureResourceKey => _metallicRoughnessTexture?.ResourceKey;
    public byte[]? MetallicRoughnessTextureData => _metallicRoughnessTexture?.CopyEncodedData();
    public string? MetallicRoughnessTextureMimeType => _metallicRoughnessTexture?.MimeType;
    public int MetallicRoughnessTextureVersion => _metallicRoughnessTextureVersion;
    public bool HasMetallicRoughnessTexture => _metallicRoughnessTexture is not null;
    internal TextureResource3D? MetallicRoughnessTextureInternal => _metallicRoughnessTexture;

    public void SetMetallicRoughnessTexture(TextureResource3D texture)
        => SetTextureAtomic(ref _metallicRoughnessTexture, ref _metallicRoughnessTextureVersion, texture);

    public void SetMetallicRoughnessTexture(string textureKey, byte[] textureData, string? mimeType = null)
        => SetMetallicRoughnessTexture(TextureResource3D.Create(textureKey, textureData, mimeType));

    public void ClearMetallicRoughnessTexture()
        => ClearTexture(ref _metallicRoughnessTexture, ref _metallicRoughnessTextureVersion);

    public TextureResource3D? EmissiveTexture => _emissiveTexture;
    public string? EmissiveTextureKey => _emissiveTexture?.LogicalKey;
    public string? EmissiveTextureResourceKey => _emissiveTexture?.ResourceKey;
    public byte[]? EmissiveTextureData => _emissiveTexture?.CopyEncodedData();
    public string? EmissiveTextureMimeType => _emissiveTexture?.MimeType;
    public int EmissiveTextureVersion => _emissiveTextureVersion;
    public bool HasEmissiveTexture => _emissiveTexture is not null;
    internal TextureResource3D? EmissiveTextureInternal => _emissiveTexture;

    public void SetEmissiveTexture(TextureResource3D texture)
        => SetTextureAtomic(ref _emissiveTexture, ref _emissiveTextureVersion, texture);

    public void SetEmissiveTexture(string textureKey, byte[] textureData, string? mimeType = null)
        => SetEmissiveTexture(TextureResource3D.Create(textureKey, textureData, mimeType));

    public void ClearEmissiveTexture()
        => ClearTexture(ref _emissiveTexture, ref _emissiveTextureVersion);

    public ColorRgba EmissiveColor
    {
        get => _emissiveColor;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Color(value, nameof(value)); if (_emissiveColor.Equals(value)) return; _emissiveColor = value; RaiseChanged(); }
    }

    public float AlphaCutoff
    {
        get => _alphaCutoff;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 1f, nameof(value)); if (NearlyEqual(_alphaCutoff, v)) return; _alphaCutoff = v; RaiseChanged(); }
    }

    public bool DoubleSided
    {
        get => CullMode == ThreeDEngine.Core.Materials.CullMode.None;
        set => CullMode = value ? ThreeDEngine.Core.Materials.CullMode.None : ThreeDEngine.Core.Materials.CullMode.Back;
    }

    public TextureResource3D? NormalMapTexture => _normalMapTexture;
    public string? NormalMapTextureKey => _normalMapTexture?.LogicalKey;
    public string? NormalMapTextureResourceKey => _normalMapTexture?.ResourceKey;
    public byte[]? NormalMapTextureData => _normalMapTexture?.CopyEncodedData();
    public string? NormalMapTextureMimeType => _normalMapTexture?.MimeType;
    public int NormalMapTextureVersion => _normalMapTextureVersion;
    internal TextureResource3D? NormalMapTextureInternal => _normalMapTexture;

    public void SetNormalMapTexture(TextureResource3D texture, float strength = 1f)
    {
        using var mutation = EnterMutationScope();
        ArgumentNullException.ThrowIfNull(texture);
        var validatedStrength = Guard3D.Range(strength, 0f, 4f, nameof(strength));
        if (TextureSlotEquals(_normalMapTexture, texture) && NearlyEqual(_normalMapStrength, validatedStrength)) return;
        _normalMapTexture = texture;
        _normalMapStrength = validatedStrength;
        unchecked { _normalMapTextureVersion++; }
        RaiseChanged();
    }

    public void SetNormalMapTexture(string textureKey, byte[] textureData, string? mimeType = null, float strength = 1f)
        => SetNormalMapTexture(TextureResource3D.Create(textureKey, textureData, mimeType), strength);

    public void ClearNormalMapTexture()
        => ClearTexture(ref _normalMapTexture, ref _normalMapTextureVersion);

    public float NormalMapStrength
    {
        get => _normalMapStrength;
        set { using var mutation = EnterMutationScope(); var v = Guard3D.Range(value, 0f, 4f, nameof(value)); if (NearlyEqual(_normalMapStrength, v)) return; _normalMapStrength = v; RaiseChanged(); }
    }

    public MaterialShaderExtension3D? ShaderExtension
    {
        get => _shaderExtension;
        set
        {
            using var mutation = EnterMutationScope();
            if (ReferenceEquals(_shaderExtension, value) || (_shaderExtension is not null && _shaderExtension.Equals(value))) return;
            _shaderExtension = value;
            RaiseChanged();
        }
    }

    public bool HasShaderExtension => _shaderExtension is not null;

    public bool HasNormalMap => _normalMapTexture is not null && _normalMapStrength > 0.0001f;
    public bool IsTransparent => Surface == SurfaceMode.Transparent || Opacity < 0.999f || BaseColor.A < 0.999f;
    public bool UsesLighting => Lighting != LightingMode.Unlit;
    public bool UsesSpecular => Lighting == LightingMode.Phong || Lighting == LightingMode.BlinnPhong;
    public ColorRgba EffectiveColor => new(BaseColor.R, BaseColor.G, BaseColor.B, BaseColor.A * Opacity);

    public Material3D Clone()
    {
        return new Material3D
        {
            _baseColor = _baseColor,
            _specularColor = _specularColor,
            _opacity = _opacity,
            _ambientStrength = _ambientStrength,
            _diffuseStrength = _diffuseStrength,
            _specularStrength = _specularStrength,
            _shininess = _shininess,
            _metallic = _metallic,
            _roughness = _roughness,
            _lighting = _lighting,
            _surface = _surface,
            _cullMode = _cullMode,
            _baseColorTexture = _baseColorTexture,
            _baseColorTextureVersion = _baseColorTextureVersion,
            _normalMapTexture = _normalMapTexture,
            _normalMapTextureVersion = _normalMapTextureVersion,
            _metallicRoughnessTexture = _metallicRoughnessTexture,
            _metallicRoughnessTextureVersion = _metallicRoughnessTextureVersion,
            _emissiveTexture = _emissiveTexture,
            _emissiveTextureVersion = _emissiveTextureVersion,
            _emissiveColor = _emissiveColor,
            _alphaCutoff = _alphaCutoff,
            _normalMapStrength = _normalMapStrength,
            _shaderExtension = _shaderExtension,
            Version = Version
        };
    }

    private void SetTextureAtomic(ref TextureResource3D? target, ref int textureVersion, TextureResource3D texture)
    {
        using var mutation = EnterMutationScope();
        ArgumentNullException.ThrowIfNull(texture);
        if (TextureSlotEquals(target, texture)) return;
        target = texture;
        unchecked { textureVersion++; }
        RaiseChanged();
    }

    private void ClearTexture(ref TextureResource3D? target, ref int textureVersion)
    {
        using var mutation = EnterMutationScope();
        if (target is null) return;
        target = null;
        unchecked { textureVersion++; }
        RaiseChanged();
    }

    private static bool TextureSlotEquals(TextureResource3D? left, TextureResource3D right)
        => left is not null
           && left.Equals(right)
           && string.Equals(left.LogicalKey, right.LogicalKey, StringComparison.Ordinal)
           && string.Equals(left.MimeType, right.MimeType, StringComparison.Ordinal);

    private static bool NearlyEqual(float left, float right) => global::System.MathF.Abs(left - right) < 0.0001f;

    private MaterialMutationLease EnterMutationScope()
    {
        var handlers = MutationScopeRequested?.GetInvocationList();
        if (handlers is null || handlers.Length == 0) return default;

        var leases = new SceneAccessLease3D[handlers.Length];
        var acquired = 0;
        try
        {
            for (; acquired < handlers.Length; acquired++)
            {
                leases[acquired] = ((Func<SceneAccessLease3D>)handlers[acquired])();
            }
            return new MaterialMutationLease(leases, acquired);
        }
        catch
        {
            for (var i = acquired - 1; i >= 0; i--) leases[i].Dispose();
            throw;
        }
    }

    private readonly struct MaterialMutationLease : IDisposable
    {
        private readonly SceneAccessLease3D[]? _leases;
        private readonly int _count;

        public MaterialMutationLease(SceneAccessLease3D[] leases, int count)
        {
            _leases = leases;
            _count = count;
        }

        public void Dispose()
        {
            if (_leases is null) return;
            for (var i = _count - 1; i >= 0; i--) _leases[i].Dispose();
        }
    }

    private void RaiseChanged()
    {
        unchecked { Version++; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
