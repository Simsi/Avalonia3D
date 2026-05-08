using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Collision;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelAsset3D
{
    public ModelAsset3D(
        string assetId,
        string sourcePath,
        IReadOnlyList<ModelNode3D> nodes,
        IReadOnlyList<MeshAsset3D> meshes,
        IReadOnlyList<ModelMaterialAsset3D> materials,
        IReadOnlyList<ModelTextureAsset3D> textures,
        ModelImportDiagnostics diagnostics,
        IReadOnlyList<SkinAsset3D>? skins = null,
        IReadOnlyList<AnimationClip3D>? animations = null)
    {
        AssetId = string.IsNullOrWhiteSpace(assetId) ? "model:" + Guid.NewGuid().ToString("N") : assetId;
        SourcePath = sourcePath ?? string.Empty;
        Nodes = nodes ?? Array.Empty<ModelNode3D>();
        Meshes = meshes ?? Array.Empty<MeshAsset3D>();
        Materials = materials is { Count: > 0 } ? materials : new[] { ModelMaterialAsset3D.Default };
        Textures = textures ?? Array.Empty<ModelTextureAsset3D>();
        Diagnostics = diagnostics ?? new ModelImportDiagnostics();
        Skins = skins ?? Array.Empty<SkinAsset3D>();
        Animations = animations ?? Array.Empty<AnimationClip3D>();
        Bounds = ComputeBounds(Nodes);
    }

    public string AssetId { get; }
    public string SourcePath { get; }
    public IReadOnlyList<ModelNode3D> Nodes { get; }
    public IReadOnlyList<MeshAsset3D> Meshes { get; }
    public IReadOnlyList<ModelMaterialAsset3D> Materials { get; }
    public IReadOnlyList<ModelTextureAsset3D> Textures { get; }
    public ModelImportDiagnostics Diagnostics { get; }
    public IReadOnlyList<SkinAsset3D> Skins { get; }
    public IReadOnlyList<AnimationClip3D> Animations { get; }
    public Bounds3D Bounds { get; }
    public int PrimitiveCount
    {
        get
        {
            var count = 0;
            foreach (var mesh in Meshes) count += mesh.PrimitiveCount;
            return count;
        }
    }

    public ModelNode3D? FindNode(string pathOrName)
    {
        foreach (var node in Nodes)
        {
            if (StringComparer.Ordinal.Equals(node.Path, pathOrName) || StringComparer.Ordinal.Equals(node.Name, pathOrName)) return node;
        }

        return null;
    }

    public AnimationClip3D? FindAnimation(string name)
    {
        foreach (var animation in Animations)
        {
            if (StringComparer.Ordinal.Equals(animation.Name, name)) return animation;
        }
        return null;
    }

    public SkinAsset3D? ResolveSkin(int? skinIndex)
    {
        if (skinIndex.HasValue && skinIndex.Value >= 0 && skinIndex.Value < Skins.Count) return Skins[skinIndex.Value];
        return null;
    }

    public ModelMaterialAsset3D ResolveMaterial(int materialIndex)
    {
        if (materialIndex >= 0 && materialIndex < Materials.Count) return Materials[materialIndex];
        return Materials.Count > 0 ? Materials[0] : ModelMaterialAsset3D.Default;
    }

    private static Bounds3D ComputeBounds(IReadOnlyList<ModelNode3D> nodes)
    {
        var bounds = Bounds3D.Empty;
        foreach (var node in nodes)
        {
            bounds = bounds.Encapsulate(node.Bounds);
        }

        return bounds;
    }
}
