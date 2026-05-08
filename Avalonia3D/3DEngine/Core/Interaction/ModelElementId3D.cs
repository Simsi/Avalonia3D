using System;

namespace ThreeDEngine.Core.Interaction;

public readonly struct ModelElementId3D : IEquatable<ModelElementId3D>
{
    public ModelElementId3D(
        string modelId,
        string nodePath,
        int nodeIndex,
        int meshIndex,
        int primitiveIndex,
        int? triangleIndex = null)
    {
        ModelId = modelId ?? string.Empty;
        NodePath = nodePath ?? string.Empty;
        NodeIndex = nodeIndex;
        MeshIndex = meshIndex;
        PrimitiveIndex = primitiveIndex;
        TriangleIndex = triangleIndex;
    }

    public string ModelId { get; }
    public string NodePath { get; }
    public int NodeIndex { get; }
    public int MeshIndex { get; }
    public int PrimitiveIndex { get; }
    public int? TriangleIndex { get; }
    public string PrimitivePath => $"{NodePath}/mesh[{MeshIndex}]/primitive[{PrimitiveIndex}]";
    public string TrianglePath => TriangleIndex.HasValue ? $"{PrimitivePath}/triangle[{TriangleIndex.Value}]" : PrimitivePath;

    public bool Equals(ModelElementId3D other)
        => string.Equals(ModelId, other.ModelId, StringComparison.Ordinal) &&
           string.Equals(NodePath, other.NodePath, StringComparison.Ordinal) &&
           NodeIndex == other.NodeIndex &&
           MeshIndex == other.MeshIndex &&
           PrimitiveIndex == other.PrimitiveIndex &&
           TriangleIndex == other.TriangleIndex;

    public override bool Equals(object? obj) => obj is ModelElementId3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ModelId, NodePath, NodeIndex, MeshIndex, PrimitiveIndex, TriangleIndex);
    public override string ToString() => TrianglePath;
    public static bool operator ==(ModelElementId3D left, ModelElementId3D right) => left.Equals(right);
    public static bool operator !=(ModelElementId3D left, ModelElementId3D right) => !left.Equals(right);
}
