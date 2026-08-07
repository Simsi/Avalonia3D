using System;
using System.Numerics;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelAnimationPose3D
{
    private Matrix4x4[] _nodeWorldTransforms;
    private Matrix4x4[][] _skinMatrices;

    public ModelAnimationPose3D(Matrix4x4[]? nodeWorldTransforms, Matrix4x4[][]? skinMatrices)
    {
        _nodeWorldTransforms = CopyMatrices(nodeWorldTransforms, nameof(nodeWorldTransforms));
        _skinMatrices = CopyMatrixSets(skinMatrices, nameof(skinMatrices));
    }

    public Matrix4x4[] NodeWorldTransforms => (Matrix4x4[])_nodeWorldTransforms.Clone();
    public Matrix4x4[][] SkinMatrices => CopyMatrixSets(_skinMatrices, nameof(SkinMatrices));

    internal Matrix4x4[] NodeWorldTransformsInternal => _nodeWorldTransforms;
    internal Matrix4x4[][] SkinMatricesInternal => _skinMatrices;

    public static ModelAnimationPose3D Empty { get; } = new(Array.Empty<Matrix4x4>(), Array.Empty<Matrix4x4[]>());

    public Matrix4x4 GetNodeWorldTransform(int nodeIndex, Matrix4x4 fallback)
        => nodeIndex >= 0 && nodeIndex < _nodeWorldTransforms.Length ? _nodeWorldTransforms[nodeIndex] : fallback;

    public Matrix4x4[] GetSkinMatrices(int skinIndex)
        => skinIndex >= 0 && skinIndex < _skinMatrices.Length
            ? (Matrix4x4[])_skinMatrices[skinIndex].Clone()
            : Array.Empty<Matrix4x4>();

    internal Matrix4x4[] GetSkinMatricesInternal(int skinIndex)
        => skinIndex >= 0 && skinIndex < _skinMatrices.Length ? _skinMatrices[skinIndex] : Array.Empty<Matrix4x4>();

    internal void Reset(Matrix4x4[] nodeWorldTransforms, Matrix4x4[][] skinMatrices)
    {
        _nodeWorldTransforms = nodeWorldTransforms ?? throw new ArgumentNullException(nameof(nodeWorldTransforms));
        _skinMatrices = skinMatrices ?? throw new ArgumentNullException(nameof(skinMatrices));
    }

    private static Matrix4x4[] CopyMatrices(Matrix4x4[]? source, string name)
    {
        if (source is null || source.Length == 0) return Array.Empty<Matrix4x4>();
        var copy = (Matrix4x4[])source.Clone();
        for (var i = 0; i < copy.Length; i++) Guard3D.FiniteMatrix(copy[i], name);
        return copy;
    }

    private static Matrix4x4[][] CopyMatrixSets(Matrix4x4[][]? source, string name)
    {
        if (source is null || source.Length == 0) return Array.Empty<Matrix4x4[]>();
        var copy = new Matrix4x4[source.Length][];
        for (var i = 0; i < copy.Length; i++) copy[i] = CopyMatrices(source[i], name);
        return copy;
    }
}
