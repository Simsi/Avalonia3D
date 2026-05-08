using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class SkinAsset3D
{
    private readonly Dictionary<int, int> _jointNodeToBoneIndex;

    public SkinAsset3D(int index, string name, int? skeletonRootNodeIndex, IReadOnlyList<BoneAsset3D> bones)
    {
        Index = index;
        Name = string.IsNullOrWhiteSpace(name) ? $"Skin_{index}" : name;
        SkeletonRootNodeIndex = skeletonRootNodeIndex;
        Bones = bones ?? Array.Empty<BoneAsset3D>();
        _jointNodeToBoneIndex = new Dictionary<int, int>();
        foreach (var bone in Bones)
        {
            _jointNodeToBoneIndex[bone.NodeIndex] = bone.Index;
        }
    }

    public int Index { get; }
    public string Name { get; }
    public int? SkeletonRootNodeIndex { get; }
    public IReadOnlyList<BoneAsset3D> Bones { get; }
    public int BoneCount => Bones.Count;

    public bool TryGetBoneIndexForNode(int nodeIndex, out int boneIndex)
        => _jointNodeToBoneIndex.TryGetValue(nodeIndex, out boneIndex);

    public Matrix4x4[] CreateInverseBindMatrixArray()
    {
        var result = new Matrix4x4[Bones.Count];
        for (var i = 0; i < result.Length; i++) result[i] = Bones[i].InverseBindMatrix;
        return result;
    }
}
