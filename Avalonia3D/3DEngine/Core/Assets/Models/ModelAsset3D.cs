using System;
using System.Collections.Generic;
using System.Linq;
using ThreeDEngine.Core.Validation;
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
        AssetId = Guard3D.RequiredText(assetId, nameof(assetId));
        SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        Nodes = Array.AsReadOnly((nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray());
        Meshes = Array.AsReadOnly((meshes ?? throw new ArgumentNullException(nameof(meshes))).ToArray());
        Materials = Array.AsReadOnly((materials ?? throw new ArgumentNullException(nameof(materials))).ToArray());
        Textures = Array.AsReadOnly((textures ?? throw new ArgumentNullException(nameof(textures))).ToArray());
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        Skins = Array.AsReadOnly((skins ?? Array.Empty<SkinAsset3D>()).ToArray());
        Animations = Array.AsReadOnly((animations ?? Array.Empty<AnimationClip3D>()).ToArray());
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
        if (!skinIndex.HasValue || skinIndex.Value == -1) return null;
        if ((uint)skinIndex.Value >= (uint)Skins.Count)
            throw new ArgumentOutOfRangeException(nameof(skinIndex), skinIndex, "Skin index is outside the asset skin catalog.");
        return Skins[skinIndex.Value];
    }

    public ModelMaterialAsset3D ResolveMaterial(int materialIndex)
    {
        if (materialIndex == -1) return ModelMaterialAsset3D.Default;
        if ((uint)materialIndex >= (uint)Materials.Count)
            throw new ArgumentOutOfRangeException(nameof(materialIndex), materialIndex, "Material index is outside the asset material catalog.");
        return Materials[materialIndex];
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
