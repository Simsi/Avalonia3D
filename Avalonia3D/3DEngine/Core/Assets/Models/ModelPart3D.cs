using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
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
    private Matrix4x4[] _gpuSkinMatricesScratch = Array.Empty<Matrix4x4>();
    private Matrix4x4[] _currentSkinMatrices = Array.Empty<Matrix4x4>();
    private Matrix4x4[] _currentGpuSkinMatrices = Array.Empty<Matrix4x4>();
    private Bounds3D _conservativeSkinnedLocalBounds = Bounds3D.Empty;
    private int _conservativeSkinnedBoundsVersion = -1;

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
        _baseMesh = primitive.ToMesh();
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
    public Matrix4x4[] CurrentSkinMatrices => (Matrix4x4[])_currentSkinMatrices.Clone();
    internal Matrix4x4[] CurrentSkinMatricesInternal => _currentSkinMatrices;

    /// <summary>
    /// Final per-bone matrices for GPU skinning in this ModelPart local space. These are the
    /// same matrices used by the CPU skinning path after applying the inverse node transform;
    /// renderers can therefore skin bind-pose vertices and then use GetLocalMatrix() normally.
    /// </summary>
    public Matrix4x4[] CurrentGpuSkinMatrices => (Matrix4x4[])_currentGpuSkinMatrices.Clone();
    internal Matrix4x4[] CurrentGpuSkinMatricesInternal => _currentGpuSkinMatrices;
    public int SkinningVersion { get; private set; }
    public bool IsSkinned => Skin is not null && Primitive.HasSkinWeights;
    public string ModelElementPath => $"{Node.Path}/mesh[{MeshAsset.Index}]/primitive[{PrimitiveIndex}]";

    internal void ApplyAnimationPose(ModelAnimationPose3D pose)
    {
        _nodeTransform = pose.GetNodeWorldTransform(Node.Index, Node.WorldTransform);
        UpdateInverseNodeTransform();
        _currentSkinMatrices = Skin is not null ? pose.GetSkinMatricesInternal(Skin.Index) : Array.Empty<Matrix4x4>();
        _currentGpuSkinMatrices = IsSkinned && _currentSkinMatrices.Length > 0
            ? BuildGpuSkinMatrices(_currentSkinMatrices)
            : Array.Empty<Matrix4x4>();

        if (IsSkinned && _currentSkinMatrices.Length > 0)
        {
            SkinningVersion++;
            _deformedMesh = null;
            InvalidateWorldCacheRecursive();
            RaiseChanged(SceneChangeKind.AnimationPose);
        }
        else
        {
            _currentGpuSkinMatrices = Array.Empty<Matrix4x4>();
            _deformedMesh = null;
            InvalidateWorldCacheRecursive();
            RaiseChanged(SceneChangeKind.Transform);
        }
    }

    public void ApplyAssetMaterial() => Material = _assetMaterial.ToMaterial3D(Model.Asset.Textures);

    public void ApplyMaterial(Material3D material)
        => Material = (material ?? throw new ArgumentNullException(nameof(material))).Clone();

    public override Matrix4x4 GetLocalMatrix() => _nodeTransform * Transform.LocalMatrix;

    protected override Mesh3D BuildMesh() => _baseMesh;

    internal Mesh3D GetCpuSkinnedPickingMesh()
    {
        if (!IsSkinned || _currentSkinMatrices.Length == 0) return _baseMesh;
        if (_deformedMesh is null || _deformedMeshFrame != SkinningVersion)
        {
            _deformedMesh = BuildCpuSkinnedMesh();
            _deformedMeshFrame = SkinningVersion;
        }

        return _deformedMesh;
    }

    public override Bounds3D GetWorldBounds()
    {
        if (!IsSkinned || _currentSkinMatrices.Length == 0) return base.GetWorldBounds();
        var bounds = _baseMesh.LocalBounds;
        if (!bounds.IsValid) return Bounds3D.Empty;

        return GetConservativeSkinnedLocalBounds(bounds).Transform(GetModelMatrix());
    }

    private Bounds3D GetConservativeSkinnedLocalBounds(Bounds3D bindBounds)
    {
        if (_conservativeSkinnedBoundsVersion == SkinningVersion && _conservativeSkinnedLocalBounds.IsValid)
        {
            return _conservativeSkinnedLocalBounds;
        }

        var result = bindBounds;
        var matrices = _currentGpuSkinMatrices.Length > 0 ? _currentGpuSkinMatrices : _currentSkinMatrices;
        for (var i = 0; i < matrices.Length; i++)
        {
            result = result.Encapsulate(bindBounds.Transform(matrices[i]));
        }

        // Pad by a small fraction so bounds stay conservative under blended bone weights and
        // floating-point differences between CPU picking and GPU skinning paths.
        var size = result.Size;
        var pad = MathF.Max(0.025f, MathF.Max(size.X, MathF.Max(size.Y, size.Z)) * 0.05f);
        _conservativeSkinnedLocalBounds = new Bounds3D(result.Min - new Vector3(pad), result.Max + new Vector3(pad));
        _conservativeSkinnedBoundsVersion = SkinningVersion;
        return _conservativeSkinnedLocalBounds;
    }

    private void UpdateInverseNodeTransform()
    {
        _inverseNodeTransform = Matrix4x4.Invert(_nodeTransform, out var inverse) ? inverse : Matrix4x4.Identity;
    }

    private Mesh3D BuildCpuSkinnedMesh()
    {
        var positions = Primitive.Positions;
        var normals = Primitive.Normals;
        var safeNormals = normals;
        var boneIndices = Primitive.BoneIndices0;
        var boneWeights = Primitive.BoneWeights0;
        if (positions.Length == 0 ||
            boneIndices.Length != positions.Length ||
            boneWeights.Length != positions.Length ||
            _currentSkinMatrices.Length == 0)
        {
            throw new InvalidOperationException(
                $"Skinned picking mesh '{ModelElementPath}' has incomplete skin streams or no skin matrices.");
        }

        var deformedPositions = new Vector3[positions.Length];
        var deformedNormals = new Vector3[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            var joints = boneIndices[i];
            var weights = boneWeights[i];
            var p = Vector3.Zero;
            var n = Vector3.Zero;
            var total = 0f;
            var normal = safeNormals[i];
            Accumulate(joints.X, weights.X, positions[i], normal, ref p, ref n, ref total);
            Accumulate(joints.Y, weights.Y, positions[i], normal, ref p, ref n, ref total);
            Accumulate(joints.Z, weights.Z, positions[i], normal, ref p, ref n, ref total);
            Accumulate(joints.W, weights.W, positions[i], normal, ref p, ref n, ref total);

            if (total <= 0.000001f)
            {
                throw new InvalidOperationException(
                    $"Skinned picking mesh '{ModelElementPath}' vertex {i} has zero total bone weight.");
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
            throw new InvalidOperationException(
                $"Skinned picking deformation for '{ModelElementPath}' is invalid: {plausibility.Reason} " +
                $"(source span {plausibility.SourceSpan:0.######}, deformed span {plausibility.DeformedSpan:0.######}).");
        }

        // This mesh is private to the CPU raycast path and must never enter a renderer resource plan.
        var dynamicResourceKey = $"{_baseMesh.ResourceKey}:cpu-picking:{Model.Id}:{Node.Index}:{PrimitiveIndex}";
        return new Mesh3D(
            deformedPositions,
            deformedNormals,
            Primitive.Indices.ToArray(),
            dynamicResourceKey,
            materialSlots: _baseMesh.MaterialSlots.ToArray(),
            materialSlotBaseColors: _baseMesh.MaterialSlotBaseColors.ToArray(),
            texCoords0: Primitive.TexCoords0.ToArray(),
            vertexColors0: _baseMesh.VertexColors0.ToArray(),
            tangents: _baseMesh.Tangents.ToArray());
    }

    private Matrix4x4[] BuildGpuSkinMatrices(Matrix4x4[] source)
    {
        if (source.Length == 0) return Array.Empty<Matrix4x4>();
        if (_gpuSkinMatricesScratch.Length != source.Length)
        {
            _gpuSkinMatricesScratch = new Matrix4x4[source.Length];
        }

        for (var i = 0; i < source.Length; i++)
        {
            // Match the CPU skinning path: skin vertices into this ModelPart's local space.
            _gpuSkinMatricesScratch[i] = source[i] * _inverseNodeTransform;
        }

        return _gpuSkinMatricesScratch;
    }

    private static SkinningPlausibility EvaluateDeformationPlausibility(GeometryBuffer3D<Vector3> source, Vector3[] deformed)
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
        if ((uint)boneIndex >= (uint)_currentSkinMatrices.Length) return;

        // CurrentSkinMatrices already contains inverseBind * jointGlobal in the engine's
        // row-vector convention. Apply the inverse mesh-node transform last so skinned
        // vertices stay in this ModelPart's local space; GetLocalMatrix() then places the
        // part in the scene.
        var matrix = _currentSkinMatrices[boneIndex] * _inverseNodeTransform;
        positionAccumulator += Vector3.Transform(position, matrix) * weight;
        normalAccumulator += Vector3.TransformNormal(normal, matrix) * weight;
        totalWeight += weight;
    }
}
