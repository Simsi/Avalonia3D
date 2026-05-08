using System;
using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelAnimationPose3D
{
    public ModelAnimationPose3D(Matrix4x4[] nodeWorldTransforms, Matrix4x4[][] skinMatrices)
    {
        NodeWorldTransforms = nodeWorldTransforms ?? Array.Empty<Matrix4x4>();
        SkinMatrices = skinMatrices ?? Array.Empty<Matrix4x4[]>();
    }

    public Matrix4x4[] NodeWorldTransforms { get; }
    public Matrix4x4[][] SkinMatrices { get; }

    public static ModelAnimationPose3D Empty { get; } = new(Array.Empty<Matrix4x4>(), Array.Empty<Matrix4x4[]>());

    public Matrix4x4 GetNodeWorldTransform(int nodeIndex, Matrix4x4 fallback)
        => nodeIndex >= 0 && nodeIndex < NodeWorldTransforms.Length ? NodeWorldTransforms[nodeIndex] : fallback;

    public Matrix4x4[] GetSkinMatrices(int skinIndex)
        => skinIndex >= 0 && skinIndex < SkinMatrices.Length ? SkinMatrices[skinIndex] : Array.Empty<Matrix4x4>();
}
