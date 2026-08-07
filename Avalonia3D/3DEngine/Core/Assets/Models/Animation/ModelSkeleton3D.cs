using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelSkeleton3D
{
    private readonly Matrix4x4[] _inverseBindMatrices;

    public ModelSkeleton3D(SkinAsset3D skin)
    {
        Skin = skin ?? throw new ArgumentNullException(nameof(skin));
        _inverseBindMatrices = new Matrix4x4[skin.BoneCount];
        for (var i = 0; i < _inverseBindMatrices.Length; i++) _inverseBindMatrices[i] = skin.Bones[i].InverseBindMatrix;
    }

    public SkinAsset3D Skin { get; }
    public IReadOnlyList<BoneAsset3D> Joints => Skin.Bones;
    public Matrix4x4[] InverseBindMatrices => (Matrix4x4[])_inverseBindMatrices.Clone();
    internal Matrix4x4[] InverseBindMatricesInternal => _inverseBindMatrices;
}
