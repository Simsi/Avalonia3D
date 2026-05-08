using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;

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
        Index = index;
        Name = string.IsNullOrWhiteSpace(name) ? $"Material_{index}" : name;
        BaseColor = baseColor;
        MetallicFactor = global::System.Math.Clamp(metallicFactor, 0f, 1f);
        RoughnessFactor = global::System.Math.Clamp(roughnessFactor, 0f, 1f);
        AlphaMode = string.IsNullOrWhiteSpace(alphaMode) ? "OPAQUE" : alphaMode;
        AlphaCutoff = global::System.Math.Clamp(alphaCutoff, 0f, 1f);
        BaseColorTextureIndex = baseColorTextureIndex;
        NormalTextureIndex = normalTextureIndex;
        NormalTextureScale = global::System.Math.Clamp(normalTextureScale, 0f, 4f);
        MetallicRoughnessTextureIndex = metallicRoughnessTextureIndex;
        EmissiveTextureIndex = emissiveTextureIndex;
        EmissiveColor = emissiveColor ?? ColorRgba.Transparent;
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
        var key = $"model-texture:{textureIndex.Value}:{role}";
        if (textures is null)
        {
            if (role == "base") material.BaseColorTextureKey = key;
            else if (role == "metallicRoughness") material.MetallicRoughnessTextureKey = key;
            else if (role == "emissive") material.EmissiveTextureKey = key;
            else if (role == "normal") material.NormalMapTextureKey = key;
            return;
        }

        for (var i = 0; i < textures.Count; i++)
        {
            var texture = textures[i];
            if (texture.Index == textureIndex.Value && texture.Data is { Length: > 0 })
            {
                var resolvedKey = BuildTextureKey(texture.Index, role, texture.Data);
                setter(material, resolvedKey, texture.Data, texture.MimeType);
                return;
            }
        }

        if (role == "base") material.BaseColorTextureKey = key;
        else if (role == "metallicRoughness") material.MetallicRoughnessTextureKey = key;
        else if (role == "emissive") material.EmissiveTextureKey = key;
        else if (role == "normal") material.NormalMapTextureKey = key;
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
