using System;
using System.Globalization;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Rendering;

public readonly struct MaterialBinding3D : IEquatable<MaterialBinding3D>
{
    public MaterialBinding3D(Material3D material)
    {
        material ??= Material3D.Default;
        BaseColor = material.EffectiveColor;
        SpecularColor = material.SpecularColor;
        Lighting = material.Lighting;
        Surface = material.Surface;
        CullMode = material.CullMode;
        AmbientStrength = material.AmbientStrength;
        DiffuseStrength = material.DiffuseStrength;
        SpecularStrength = material.SpecularStrength;
        Shininess = material.Shininess;
        Metallic = material.Metallic;
        Roughness = material.Roughness;
        AlphaCutoff = material.AlphaCutoff;
        DoubleSided = material.DoubleSided;
        EmissiveColor = material.EmissiveColor;
        BaseColorTextureKey = material.BaseColorTextureKey;
        BaseColorTextureData = material.BaseColorTextureData;
        BaseColorTextureMimeType = material.BaseColorTextureMimeType;
        BaseColorTextureVersion = material.BaseColorTextureVersion;
        NormalMapTextureKey = material.NormalMapTextureKey;
        NormalMapTextureData = material.NormalMapTextureData;
        NormalMapTextureMimeType = material.NormalMapTextureMimeType;
        NormalMapTextureVersion = material.NormalMapTextureVersion;
        NormalMapStrength = material.NormalMapStrength;
        MetallicRoughnessTextureKey = material.MetallicRoughnessTextureKey;
        MetallicRoughnessTextureData = material.MetallicRoughnessTextureData;
        MetallicRoughnessTextureMimeType = material.MetallicRoughnessTextureMimeType;
        MetallicRoughnessTextureVersion = material.MetallicRoughnessTextureVersion;
        EmissiveTextureKey = material.EmissiveTextureKey;
        EmissiveTextureData = material.EmissiveTextureData;
        EmissiveTextureMimeType = material.EmissiveTextureMimeType;
        EmissiveTextureVersion = material.EmissiveTextureVersion;
        Key = BuildKey(this);
    }

    public ColorRgba BaseColor { get; }
    public ColorRgba SpecularColor { get; }
    public LightingMode Lighting { get; }
    public SurfaceMode Surface { get; }
    public CullMode CullMode { get; }
    public float AmbientStrength { get; }
    public float DiffuseStrength { get; }
    public float SpecularStrength { get; }
    public float Shininess { get; }
    public float Metallic { get; }
    public float Roughness { get; }
    public float AlphaCutoff { get; }
    public bool DoubleSided { get; }
    public ColorRgba EmissiveColor { get; }
    public string? BaseColorTextureKey { get; }
    public byte[]? BaseColorTextureData { get; }
    public string? BaseColorTextureMimeType { get; }
    public int BaseColorTextureVersion { get; }
    public bool HasBaseColorTexture => !string.IsNullOrWhiteSpace(BaseColorTextureKey) && BaseColorTextureData is { Length: > 0 };
    public string? NormalMapTextureKey { get; }
    public byte[]? NormalMapTextureData { get; }
    public string? NormalMapTextureMimeType { get; }
    public int NormalMapTextureVersion { get; }
    public float NormalMapStrength { get; }
    public bool HasNormalMap => !string.IsNullOrWhiteSpace(NormalMapTextureKey) && NormalMapTextureData is { Length: > 0 } && NormalMapStrength > 0.0001f;
    public string? MetallicRoughnessTextureKey { get; }
    public byte[]? MetallicRoughnessTextureData { get; }
    public string? MetallicRoughnessTextureMimeType { get; }
    public int MetallicRoughnessTextureVersion { get; }
    public bool HasMetallicRoughnessTexture => !string.IsNullOrWhiteSpace(MetallicRoughnessTextureKey) && MetallicRoughnessTextureData is { Length: > 0 };
    public string? EmissiveTextureKey { get; }
    public byte[]? EmissiveTextureData { get; }
    public string? EmissiveTextureMimeType { get; }
    public int EmissiveTextureVersion { get; }
    public bool HasEmissiveTexture => !string.IsNullOrWhiteSpace(EmissiveTextureKey) && EmissiveTextureData is { Length: > 0 };
    public string Key { get; }
    public RendererResourceKey ResourceKey => RendererResourceKey.Material(Key);

    public static MaterialBinding3D FromMaterial(Material3D material) => new(material);

    public bool Equals(MaterialBinding3D other)
        => Key == other.Key;

    public override bool Equals(object? obj) => obj is MaterialBinding3D other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    private static string BuildKey(MaterialBinding3D m)
        => "rgba(" + F(m.BaseColor.R) + "," + F(m.BaseColor.G) + "," + F(m.BaseColor.B) + "," + F(m.BaseColor.A) + ")|spec=" + F(m.SpecularColor.R) + "," + F(m.SpecularColor.G) + "," + F(m.SpecularColor.B) + "|" + m.Lighting + "|" + m.Surface + "|" + m.CullMode + "|a=" + F(m.AmbientStrength) + "|d=" + F(m.DiffuseStrength) + "|s=" + F(m.SpecularStrength) + "|sh=" + F(m.Shininess) + "|m=" + F(m.Metallic) + "|r=" + F(m.Roughness) + "|cut=" + F(m.AlphaCutoff) + "|ds=" + m.DoubleSided + "|em=" + F(m.EmissiveColor.R) + "," + F(m.EmissiveColor.G) + "," + F(m.EmissiveColor.B) + "," + F(m.EmissiveColor.A) + TextureKey("base", m.BaseColorTextureKey, m.BaseColorTextureVersion) + TextureKey("normal", m.NormalMapTextureKey, m.NormalMapTextureVersion) + "|ns=" + F(m.NormalMapStrength) + TextureKey("mr", m.MetallicRoughnessTextureKey, m.MetallicRoughnessTextureVersion) + TextureKey("emissive", m.EmissiveTextureKey, m.EmissiveTextureVersion);

    private static string TextureKey(string role, string? key, int version)
        => "|" + role + "=" + (key ?? string.Empty) + "@" + version.ToString(CultureInfo.InvariantCulture);

    private static string F(float value) => MathF.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
