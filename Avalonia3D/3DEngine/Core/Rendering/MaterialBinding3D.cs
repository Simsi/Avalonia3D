using System;
using System.Runtime.CompilerServices;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Rendering;

internal readonly struct MaterialBinding3D : IEquatable<MaterialBinding3D>
{
    private static readonly ConditionalWeakTable<Material3D, CacheEntry> Cache = new();

    private MaterialBinding3D(Material3D material)
    {
        ArgumentNullException.ThrowIfNull(material);
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
        BaseColorTextureResource = material.BaseColorTextureInternal;
        BaseColorTextureVersion = material.BaseColorTextureVersion;
        NormalMapTextureResource = material.NormalMapTextureInternal;
        NormalMapTextureVersion = material.NormalMapTextureVersion;
        NormalMapStrength = material.NormalMapStrength;
        MetallicRoughnessTextureResource = material.MetallicRoughnessTextureInternal;
        MetallicRoughnessTextureVersion = material.MetallicRoughnessTextureVersion;
        EmissiveTextureResource = material.EmissiveTextureInternal;
        EmissiveTextureVersion = material.EmissiveTextureVersion;

        KeyHash = 0UL;
        BatchKeyHash = 0UL;
        Key = string.Empty;
        BatchKey = string.Empty;

        var keyHash = ComputeSignatureHash(this, includeBaseColor: true);
        var batchKeyHash = ComputeSignatureHash(this, includeBaseColor: false);
        KeyHash = keyHash;
        BatchKeyHash = batchKeyHash;
        Key = RenderId3D.FormatStableHash(keyHash, "mat:");
        BatchKey = RenderId3D.FormatStableHash(batchKeyHash, "matb:");
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

    public TextureResource3D? BaseColorTextureResource { get; }
    public string? BaseColorTextureKey => BaseColorTextureResource?.LogicalKey;
    public string? BaseColorTextureResourceKey => BaseColorTextureResource?.ResourceKey;
    public byte[]? BaseColorTextureData => BaseColorTextureResource?.CopyEncodedData();
    internal byte[]? BaseColorTextureDataInternal => BaseColorTextureResource?.EncodedDataInternal;
    public string? BaseColorTextureMimeType => BaseColorTextureResource?.MimeType;
    public int BaseColorTextureVersion { get; }
    public bool HasBaseColorTexture => BaseColorTextureResource is not null;

    public TextureResource3D? NormalMapTextureResource { get; }
    public string? NormalMapTextureKey => NormalMapTextureResource?.LogicalKey;
    public string? NormalMapTextureResourceKey => NormalMapTextureResource?.ResourceKey;
    public byte[]? NormalMapTextureData => NormalMapTextureResource?.CopyEncodedData();
    internal byte[]? NormalMapTextureDataInternal => NormalMapTextureResource?.EncodedDataInternal;
    public string? NormalMapTextureMimeType => NormalMapTextureResource?.MimeType;
    public int NormalMapTextureVersion { get; }
    public float NormalMapStrength { get; }
    public bool HasNormalMap => NormalMapTextureResource is not null && NormalMapStrength > 0.0001f;

    public TextureResource3D? MetallicRoughnessTextureResource { get; }
    public string? MetallicRoughnessTextureKey => MetallicRoughnessTextureResource?.LogicalKey;
    public string? MetallicRoughnessTextureResourceKey => MetallicRoughnessTextureResource?.ResourceKey;
    public byte[]? MetallicRoughnessTextureData => MetallicRoughnessTextureResource?.CopyEncodedData();
    internal byte[]? MetallicRoughnessTextureDataInternal => MetallicRoughnessTextureResource?.EncodedDataInternal;
    public string? MetallicRoughnessTextureMimeType => MetallicRoughnessTextureResource?.MimeType;
    public int MetallicRoughnessTextureVersion { get; }
    public bool HasMetallicRoughnessTexture => MetallicRoughnessTextureResource is not null;

    public TextureResource3D? EmissiveTextureResource { get; }
    public string? EmissiveTextureKey => EmissiveTextureResource?.LogicalKey;
    public string? EmissiveTextureResourceKey => EmissiveTextureResource?.ResourceKey;
    public byte[]? EmissiveTextureData => EmissiveTextureResource?.CopyEncodedData();
    internal byte[]? EmissiveTextureDataInternal => EmissiveTextureResource?.EncodedDataInternal;
    public string? EmissiveTextureMimeType => EmissiveTextureResource?.MimeType;
    public int EmissiveTextureVersion { get; }
    public bool HasEmissiveTexture => EmissiveTextureResource is not null;

    public string Key { get; }
    public ulong KeyHash { get; }

    /// <summary>
    /// GPU retained batching key. Base color and opacity are intentionally excluded because
    /// retained paths stream color as per-instance state. Texture identity is content-based.
    /// </summary>
    public string BatchKey { get; }
    public ulong BatchKeyHash { get; }
    public RendererResourceKey ResourceKey => RendererResourceKey.Material(Key);

    public static MaterialBinding3D FromMaterial(Material3D material)
    {
        ArgumentNullException.ThrowIfNull(material);
        var entry = Cache.GetValue(material, static _ => new CacheEntry());
        var version = material.Version;
        if (entry.TryGet(version, out var cached)) return cached;
        var created = new MaterialBinding3D(material);
        entry.Set(version, created);
        return created;
    }

    public bool Equals(MaterialBinding3D other)
        => KeyHash == other.KeyHash && string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is MaterialBinding3D other && Equals(other);
    public override int GetHashCode() => KeyHash.GetHashCode();

    private static ulong ComputeSignatureHash(MaterialBinding3D m, bool includeBaseColor)
    {
        unchecked
        {
            var hash = RenderId3D.FnvOffsetBasis;
            hash = HashInt(hash, includeBaseColor ? 1 : 0);
            if (includeBaseColor) hash = HashColor(hash, m.BaseColor);
            hash = HashColor(hash, m.SpecularColor);
            hash = HashInt(hash, (int)m.Lighting);
            hash = HashInt(hash, (int)m.Surface);
            hash = HashInt(hash, (int)m.CullMode);
            hash = HashFloat4(hash, m.AmbientStrength);
            hash = HashFloat4(hash, m.DiffuseStrength);
            hash = HashFloat4(hash, m.SpecularStrength);
            hash = HashFloat4(hash, m.Shininess);
            hash = HashFloat4(hash, m.Metallic);
            hash = HashFloat4(hash, m.Roughness);
            hash = HashFloat4(hash, m.AlphaCutoff);
            hash = HashInt(hash, m.DoubleSided ? 1 : 0);
            hash = HashColor(hash, m.EmissiveColor);
            hash = HashTexture(hash, m.BaseColorTextureResourceKey);
            hash = HashTexture(hash, m.NormalMapTextureResourceKey);
            hash = HashFloat4(hash, m.NormalMapStrength);
            hash = HashTexture(hash, m.MetallicRoughnessTextureResourceKey);
            hash = HashTexture(hash, m.EmissiveTextureResourceKey);
            return hash == 0UL ? 1UL : hash;
        }
    }

    private static ulong HashTexture(ulong hash, string? resourceKey)
        => HashString(hash, resourceKey ?? string.Empty);

    private static ulong HashColor(ulong hash, ColorRgba color)
    {
        hash = HashFloat4(hash, color.R);
        hash = HashFloat4(hash, color.G);
        hash = HashFloat4(hash, color.B);
        return HashFloat4(hash, color.A);
    }

    private static ulong HashFloat4(ulong hash, float value)
        => HashInt(hash, (int)MathF.Round(value * 10000f));

    private static ulong HashString(ulong hash, string value)
    {
        unchecked
        {
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= RenderId3D.FnvPrime;
            }
            return hash;
        }
    }

    private static ulong HashInt(ulong hash, int value)
    {
        unchecked
        {
            hash ^= (byte)value; hash *= RenderId3D.FnvPrime;
            hash ^= (byte)(value >> 8); hash *= RenderId3D.FnvPrime;
            hash ^= (byte)(value >> 16); hash *= RenderId3D.FnvPrime;
            hash ^= (byte)(value >> 24); hash *= RenderId3D.FnvPrime;
            return hash;
        }
    }

    private sealed class CacheEntry
    {
        private int _version = int.MinValue;
        private MaterialBinding3D _binding;

        public bool TryGet(int version, out MaterialBinding3D binding)
        {
            if (_version == version) { binding = _binding; return true; }
            binding = default;
            return false;
        }

        public void Set(int version, MaterialBinding3D binding)
        {
            _version = version;
            _binding = binding;
        }
    }
}
