using System;
using System.Runtime.CompilerServices;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Rendering;

public readonly struct MaterialBinding3D : IEquatable<MaterialBinding3D>
{
    private static readonly ConditionalWeakTable<Material3D, CacheEntry> Cache = new();

    private MaterialBinding3D(Material3D material)
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

        // Assign the identity fields before passing this readonly struct to helpers. The values
        // are immediately replaced, but the explicit initialization avoids definite-assignment
        // ambiguity on stricter compilers.
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
    public ulong KeyHash { get; }
    /// <summary>
    /// GPU retained batching key. Base color and opacity are intentionally excluded because
    /// WebGL/OpenGL retained paths stream color as per-instance state; color-only changes should
    /// not destroy and recreate retained batches.
    /// </summary>
    public string BatchKey { get; }
    public ulong BatchKeyHash { get; }
    public RendererResourceKey ResourceKey => RendererResourceKey.Material(Key);

    public static MaterialBinding3D FromMaterial(Material3D material)
    {
        material ??= Material3D.Default;
        var entry = Cache.GetValue(material, static _ => new CacheEntry());
        var version = material.Version;
        if (entry.TryGet(version, out var cached))
        {
            return cached;
        }

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
            if (includeBaseColor)
            {
                hash = HashColor(hash, m.BaseColor);
            }

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
            hash = HashTexture(hash, m.BaseColorTextureKey, m.BaseColorTextureVersion);
            hash = HashTexture(hash, m.NormalMapTextureKey, m.NormalMapTextureVersion);
            hash = HashFloat4(hash, m.NormalMapStrength);
            hash = HashTexture(hash, m.MetallicRoughnessTextureKey, m.MetallicRoughnessTextureVersion);
            hash = HashTexture(hash, m.EmissiveTextureKey, m.EmissiveTextureVersion);
            return hash == 0UL ? 1UL : hash;
        }
    }

    private static ulong HashTexture(ulong hash, string? key, int version)
    {
        hash = HashString(hash, key ?? string.Empty);
        return HashInt(hash, version);
    }

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
            hash ^= (byte)value;
            hash *= RenderId3D.FnvPrime;
            hash ^= (byte)(value >> 8);
            hash *= RenderId3D.FnvPrime;
            hash ^= (byte)(value >> 16);
            hash *= RenderId3D.FnvPrime;
            hash ^= (byte)(value >> 24);
            hash *= RenderId3D.FnvPrime;
            return hash;
        }
    }

    private sealed class CacheEntry
    {
        private int _version = int.MinValue;
        private MaterialBinding3D _binding;

        public bool TryGet(int version, out MaterialBinding3D binding)
        {
            if (_version == version)
            {
                binding = _binding;
                return true;
            }

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
