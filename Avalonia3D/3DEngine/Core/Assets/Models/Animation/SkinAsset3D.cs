using System;
using System.Collections.Generic;
using System.Linq;
using ThreeDEngine.Core.Validation;
using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class SkinAsset3D
{
    private readonly Dictionary<int, int> _jointNodeToBoneIndex;

    public SkinAsset3D(int index, string name, int? skeletonRootNodeIndex, IReadOnlyList<BoneAsset3D> bones)
    {
        Index = Guard3D.NonNegative(index, nameof(index));
        Name = string.IsNullOrWhiteSpace(name) ? $"Skin_{index}" : name;
        SkeletonRootNodeIndex = skeletonRootNodeIndex is null || skeletonRootNodeIndex == -1 ? null : Guard3D.NonNegative(skeletonRootNodeIndex.Value, nameof(skeletonRootNodeIndex));
        Bones = Array.AsReadOnly((bones ?? throw new ArgumentNullException(nameof(bones))).ToArray());
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
