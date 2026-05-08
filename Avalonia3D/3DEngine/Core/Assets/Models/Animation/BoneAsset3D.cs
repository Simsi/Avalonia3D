using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class BoneAsset3D
{
    public BoneAsset3D(int index, int nodeIndex, string name, int? parentBoneIndex, Matrix4x4 inverseBindMatrix)
    {
        Index = index;
        NodeIndex = nodeIndex;
        Name = string.IsNullOrWhiteSpace(name) ? $"Bone_{index}" : name;
        ParentBoneIndex = parentBoneIndex;
        InverseBindMatrix = inverseBindMatrix;
    }

    public int Index { get; }
    public int NodeIndex { get; }
    public string Name { get; }
    public int? ParentBoneIndex { get; }
    public Matrix4x4 InverseBindMatrix { get; }
}
