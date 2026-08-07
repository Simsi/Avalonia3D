using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelMaterialAsset3D
{
    public ModelMaterialAsset3D(
        int index,
        string name,
        ColorRgba baseColor,
        float metallicFactor,
        float roughnessFactor,
        string alphaMode,
        float alphaCutoff,
        int? baseColorTextureIndex,
        int? normalTextureIndex = null,
        float normalTextureScale = 1f,
        int? metallicRoughnessTextureIndex = null,
        int? emissiveTextureIndex = null,
        ColorRgba? emissiveColor = null,
        bool doubleSided = false)
    {
        Index = Guard3D.NonNegative(index, nameof(index));
        Name = Guard3D.RequiredText(name, nameof(name));
        BaseColor = Guard3D.Color(baseColor, nameof(baseColor));
        MetallicFactor = Guard3D.Range(metallicFactor, 0f, 1f, nameof(metallicFactor));
        RoughnessFactor = Guard3D.Range(roughnessFactor, 0f, 1f, nameof(roughnessFactor));
        AlphaMode = NormalizeAlphaMode(alphaMode);
        AlphaCutoff = Guard3D.Range(alphaCutoff, 0f, 1f, nameof(alphaCutoff));
        BaseColorTextureIndex = ValidateTextureIndex(baseColorTextureIndex, nameof(baseColorTextureIndex));
        NormalTextureIndex = ValidateTextureIndex(normalTextureIndex, nameof(normalTextureIndex));
        NormalTextureScale = Guard3D.Range(normalTextureScale, 0f, 4f, nameof(normalTextureScale));
        MetallicRoughnessTextureIndex = ValidateTextureIndex(metallicRoughnessTextureIndex, nameof(metallicRoughnessTextureIndex));
        EmissiveTextureIndex = ValidateTextureIndex(emissiveTextureIndex, nameof(emissiveTextureIndex));
        EmissiveColor = Guard3D.Color(emissiveColor ?? ColorRgba.Transparent, nameof(emissiveColor));
        DoubleSided = doubleSided;
    }

    public int Index { get; }
    public string Name { get; }
    public ColorRgba BaseColor { get; }
    public float MetallicFactor { get; }
    public float RoughnessFactor { get; }
    public string AlphaMode { get; }
    public float AlphaCutoff { get; }
    public int? BaseColorTextureIndex { get; }
    public int? NormalTextureIndex { get; }
    public float NormalTextureScale { get; }
    public int? MetallicRoughnessTextureIndex { get; }
    public int? EmissiveTextureIndex { get; }
    public ColorRgba EmissiveColor { get; }
    public bool DoubleSided { get; }

    public Material3D ToMaterial3D(IReadOnlyList<ModelTextureAsset3D>? textures = null)
    {
        var material = Material3D.CreatePhong(BaseColor, specularStrength: MathF.Max(0.08f, 1f - RoughnessFactor), shininess: ComputeShininess(RoughnessFactor));
        material.Opacity = BaseColor.A;
        material.Metallic = MetallicFactor;
        material.Roughness = RoughnessFactor;
        material.AlphaCutoff = AlphaCutoff;
        material.DoubleSided = DoubleSided;
        material.CullMode = DoubleSided ? CullMode.None : CullMode.Back;
        material.EmissiveColor = EmissiveColor;
        if (BaseColor.A < 0.999f || AlphaMode == "BLEND")
        {
            material.Surface = SurfaceMode.Transparent;
        }

        ApplyTexture(textures, BaseColorTextureIndex, static (m, key, data, mime) => m.SetBaseColorTexture(key, data, mime), material, "base");
        ApplyTexture(textures, MetallicRoughnessTextureIndex, static (m, key, data, mime) => m.SetMetallicRoughnessTexture(key, data, mime), material, "metallicRoughness");
        ApplyTexture(textures, EmissiveTextureIndex, static (m, key, data, mime) => m.SetEmissiveTexture(key, data, mime), material, "emissive");

        ApplyTexture(textures, NormalTextureIndex, static (m, key, data, mime) => m.SetNormalMapTexture(key, data, mime), material, "normal");
        if (NormalTextureIndex.HasValue) material.NormalMapStrength = NormalTextureScale;

        return material;
    }

    private static void ApplyTexture(
        IReadOnlyList<ModelTextureAsset3D>? textures,
        int? textureIndex,
        Action<Material3D, string, byte[], string?> setter,
        Material3D material,
        string role)
    {
        if (!textureIndex.HasValue) return;
        if (textures is null)
        {
            throw new InvalidOperationException($"Texture index {textureIndex.Value} for material role '{role}' cannot be resolved because no texture catalog was supplied.");
        }

        for (var i = 0; i < textures.Count; i++)
        {
            var texture = textures[i];
            if (texture.Index == textureIndex.Value && texture.DataInternal is { Length: > 0 })
            {
                var resolvedKey = BuildTextureKey(texture.Index, role, texture.DataInternal!);
                setter(material, resolvedKey, texture.DataInternal!, texture.MimeType);
                return;
            }
        }

        throw new InvalidOperationException($"Texture index {textureIndex.Value} for material role '{role}' was not found or has no payload.");
    }

    private static int? ValidateTextureIndex(int? value, string parameterName)
    {
        if (value is < 0) throw new ArgumentOutOfRangeException(parameterName, value, "Texture indices must be non-negative.");
        return value;
    }

    private static string NormalizeAlphaMode(string value)
    {
        value = Guard3D.RequiredText(value, nameof(value)).ToUpperInvariant();
        return value is "OPAQUE" or "MASK" or "BLEND"
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Alpha mode must be OPAQUE, MASK, or BLEND.");
    }

    private static float ComputeShininess(float roughness)
        => global::System.Math.Clamp(2f + (1f - global::System.Math.Clamp(roughness, 0f, 1f)) * 126f, 2f, 128f);

    private static string BuildTextureKey(int index, string role, byte[]? data)
    {
        if (data is null || data.Length == 0) return $"model-texture:{index}:{role}";
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            hash ^= (uint)data.Length;
            hash *= 1099511628211UL;
            var step = global::System.Math.Max(1, data.Length / 64);
            for (var i = 0; i < data.Length; i += step)
            {
                hash ^= data[i];
                hash *= 1099511628211UL;
            }
            return $"model-texture:{index}:{role}:{hash:x16}";
        }
    }

    public static ModelMaterialAsset3D Default { get; } = new(0, "Default", ColorRgba.White, 0f, 1f, "OPAQUE", 0.5f, null);
}
