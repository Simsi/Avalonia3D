using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelSkeleton3D
{
    public ModelSkeleton3D(SkinAsset3D skin)
    {
        Skin = skin ?? throw new ArgumentNullException(nameof(skin));
        var inverseBindMatrices = new Matrix4x4[skin.BoneCount];
        for (var i = 0; i < inverseBindMatrices.Length; i++) inverseBindMatrices[i] = skin.Bones[i].InverseBindMatrix;
        InverseBindMatrices = inverseBindMatrices;
    }

    public SkinAsset3D Skin { get; }
    public IReadOnlyList<BoneAsset3D> Joints => Skin.Bones;
    public Matrix4x4[] InverseBindMatrices { get; }
}
