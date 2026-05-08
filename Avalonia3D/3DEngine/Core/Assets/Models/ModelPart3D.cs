using System;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelPart3D : Object3D
{
    private Matrix4x4 _nodeTransform;
    private Matrix4x4 _inverseNodeTransform = Matrix4x4.Identity;
    private readonly Mesh3D _baseMesh;
    private Mesh3D? _deformedMesh;
    private readonly ModelMaterialAsset3D _assetMaterial;
    private int _deformedMeshFrame;

    internal ModelPart3D(ImportedModel3D model, ModelNode3D node, MeshAsset3D meshAsset, MeshPrimitiveAsset3D primitive, ModelMaterialAsset3D material, int primitiveIndex)
    {
        Model = model;
        Node = node;
        MeshAsset = meshAsset;
        Primitive = primitive;
        PrimitiveIndex = primitiveIndex;
        _assetMaterial = material;
        _nodeTransform = node.WorldTransform;
        UpdateInverseNodeTransform();
        Skin = model.Asset.ResolveSkin(node.SkinIndex);
        _baseMesh = primitive.ToMesh($"{model.Asset.AssetId}:node:{node.Index}:mesh:{meshAsset.Index}:primitive:{primitiveIndex}:{primitive.Id}");
        Name = ModelElementPath;
        Material = material.ToMaterial3D(model.Asset.Textures);
        IsPickable = true;
    }

    public ImportedModel3D Model { get; }
    public ModelNode3D Node { get; }
    public MeshAsset3D MeshAsset { get; }
    public MeshPrimitiveAsset3D Primitive { get; }
    public int PrimitiveIndex { get; }
    public SkinAsset3D? Skin { get; }
    public Matrix4x4[] CurrentSkinMatrices { get; private set; } = Array.Empty<Matrix4x4>();
    public bool IsSkinned => Skin is not null && Primitive.HasSkinWeights;
    public ModelSkinningDiagnostics3D SkinningDiagnostics { get; private set; } = ModelSkinningDiagnostics3D.None;
    public string ModelElementPath => $"{Node.Path}/mesh[{MeshAsset.Index}]/primitive[{PrimitiveIndex}]";

    internal void ApplyAnimationPose(ModelAnimationPose3D pose)
    {
        _nodeTransform = pose.GetNodeWorldTransform(Node.Index, Node.WorldTransform);
        UpdateInverseNodeTransform();
        CurrentSkinMatrices = Skin is not null ? pose.GetSkinMatrices(Skin.Index) : Array.Empty<Matrix4x4>();

        if (IsSkinned && CurrentSkinMatrices.Length > 0)
        {
            _deformedMeshFrame++;
            _deformedMesh = BuildCpuSkinnedMesh();
            MarkGeometryDirty(nameof(CurrentSkinMatrices));
        }
        else
        {
            _deformedMesh = null;
            InvalidateWorldCacheRecursive();
        }
    }

    public void ApplyAssetMaterial() => Material = _assetMaterial.ToMaterial3D(Model.Asset.Textures);

    public void ApplyMaterial(Material3D material) => Material = (material ?? Material3D.Default).Clone();

    public override Matrix4x4 GetLocalMatrix() => _nodeTransform * Transform.LocalMatrix;

    protected override Mesh3D BuildMesh() => _deformedMesh ?? _baseMesh;

    private void UpdateInverseNodeTransform()
    {
        _inverseNodeTransform = Matrix4x4.Invert(_nodeTransform, out var inverse) ? inverse : Matrix4x4.Identity;
    }

    private Mesh3D BuildCpuSkinnedMesh()
    {
        var positions = Primitive.Positions;
        var normals = Primitive.Normals;
        var safeNormals = normals.Length == positions.Length ? normals : CreateFallbackNormals(positions.Length);
        var weights = Primitive.SkinWeights0;
        if (positions.Length == 0 || weights.Length != positions.Length || CurrentSkinMatrices.Length == 0)
        {
            return _baseMesh;
        }

        var deformedPositions = new Vector3[positions.Length];
        var deformedNormals = new Vector3[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            var skin = weights[i];
            var p = Vector3.Zero;
            var n = Vector3.Zero;
            var total = 0f;
            var normal = safeNormals[i];
            Accumulate(skin.BoneIndices.X, skin.Weights.X, positions[i], normal, ref p, ref n, ref total);
            Accumulate(skin.BoneIndices.Y, skin.Weights.Y, positions[i], normal, ref p, ref n, ref total);
            Accumulate(skin.BoneIndices.Z, skin.Weights.Z, positions[i], normal, ref p, ref n, ref total);
            Accumulate(skin.BoneIndices.W, skin.Weights.W, positions[i], normal, ref p, ref n, ref total);

            if (total <= 0.000001f)
            {
                deformedPositions[i] = positions[i];
                deformedNormals[i] = normal;
            }
            else
            {
                deformedPositions[i] = p / total;
                deformedNormals[i] = n.LengthSquared() > 0.000001f ? Vector3.Normalize(n) : normal;
            }
        }

        var plausibility = EvaluateDeformationPlausibility(positions, deformedPositions);
        if (!plausibility.Plausible)
        {
            SkinningDiagnostics = new ModelSkinningDiagnostics3D(
                true,
                plausibility.Reason,
                plausibility.SourceSpan,
                plausibility.DeformedSpan);
            return _baseMesh;
        }

        SkinningDiagnostics = ModelSkinningDiagnostics3D.None;
        // Keep the resource key stable across animation frames. Render backends can then
        // update existing dynamic buffers instead of allocating a new GPU resource per frame.
        var dynamicResourceKey = $"{_baseMesh.ResourceKey}:cpu-skin:{Model.Id}:{Node.Index}:{PrimitiveIndex}";
        return new Mesh3D(
            deformedPositions,
            deformedNormals,
            Primitive.Indices,
            dynamicResourceKey,
            texCoords0: Primitive.TexCoords0);
    }

    private static Vector3[] CreateFallbackNormals(int count)
    {
        if (count <= 0) return Array.Empty<Vector3>();
        var normals = new Vector3[count];
        for (var i = 0; i < normals.Length; i++) normals[i] = Vector3.UnitY;
        return normals;
    }
    private static SkinningPlausibility EvaluateDeformationPlausibility(Vector3[] source, Vector3[] deformed)
    {
        if (source.Length == 0 || source.Length != deformed.Length)
        {
            return new SkinningPlausibility(false, "source/deformed vertex counts do not match", 0f, 0f);
        }

        var sourceMin = new Vector3(float.PositiveInfinity);
        var sourceMax = new Vector3(float.NegativeInfinity);
        var deformedMin = new Vector3(float.PositiveInfinity);
        var deformedMax = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < source.Length; i++)
        {
            var p = source[i];
            var d = deformed[i];
            if (!IsFinite(d)) return new SkinningPlausibility(false, $"deformed vertex {i} is not finite", 0f, 0f);
            sourceMin = Vector3.Min(sourceMin, p);
            sourceMax = Vector3.Max(sourceMax, p);
            deformedMin = Vector3.Min(deformedMin, d);
            deformedMax = Vector3.Max(deformedMax, d);
        }

        var sourceSize = sourceMax - sourceMin;
        var deformedSize = deformedMax - deformedMin;
        var sourceSpan = MathF.Max(sourceSize.Length(), 0.0001f);
        var deformedSpan = deformedSize.Length();
        if (deformedSpan > sourceSpan * 6f)
        {
            return new SkinningPlausibility(false, "deformed bounds are implausibly larger than the bind-pose bounds", sourceSpan, deformedSpan);
        }

        return new SkinningPlausibility(true, string.Empty, sourceSpan, deformedSpan);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private readonly record struct SkinningPlausibility(bool Plausible, string Reason, float SourceSpan, float DeformedSpan);

    private void Accumulate(float boneIndexValue, float weight, Vector3 position, Vector3 normal, ref Vector3 positionAccumulator, ref Vector3 normalAccumulator, ref float totalWeight)
    {
        if (weight <= 0.000001f) return;
        var boneIndex = (int)MathF.Round(boneIndexValue);
        if ((uint)boneIndex >= (uint)CurrentSkinMatrices.Length) return;
        // CurrentSkinMatrices already contains inverseBind * jointGlobal in the engine's
        // row-vector convention. Apply the inverse mesh-node transform last so skinned
        // vertices stay in this ModelPart's local space; GetLocalMatrix() then places the
        // part in the scene. The previous order inverted this chain and could explode
        // rigged meshes into large spikes.
        var matrix = CurrentSkinMatrices[boneIndex] * _inverseNodeTransform;
        positionAccumulator += Vector3.Transform(position, matrix) * weight;
        normalAccumulator += Vector3.TransformNormal(normal, matrix) * weight;
        totalWeight += weight;
    }
}
