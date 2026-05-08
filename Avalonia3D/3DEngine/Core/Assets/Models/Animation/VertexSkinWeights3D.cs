using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

/// <summary>
/// Four-joint skinning payload for one vertex. glTF JOINTS_0/WEIGHTS_0 maps naturally to this shape.
/// </summary>
public readonly record struct VertexSkinWeights3D(Vector4 BoneIndices, Vector4 Weights)
{
    public static VertexSkinWeights3D Empty => new(Vector4.Zero, Vector4.Zero);

    public bool HasWeights => Weights.X > 0f || Weights.Y > 0f || Weights.Z > 0f || Weights.W > 0f;

    public VertexSkinWeights3D Normalize()
    {
        var sum = Weights.X + Weights.Y + Weights.Z + Weights.W;
        if (sum <= 0.000001f) return Empty;
        return new VertexSkinWeights3D(BoneIndices, Weights / sum);
    }
}
