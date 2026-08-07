using System;
using System.Collections.Generic;
using System.Linq;
using ThreeDEngine.Core.Validation;
using System.Numerics;
using ThreeDEngine.Core.Collision;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelNode3D
{
    public ModelNode3D(
        int index,
        string name,
        int? parentIndex,
        int? meshIndex,
        Matrix4x4 localTransform,
        Matrix4x4 worldTransform,
        IReadOnlyList<int> childIndices,
        string path,
        int? skinIndex = null)
    {
        Index = Guard3D.NonNegative(index, nameof(index));
        Name = string.IsNullOrWhiteSpace(name) ? $"Node_{index}" : name;
        ParentIndex = ValidateOptionalIndex(parentIndex, nameof(parentIndex));
        MeshIndex = ValidateOptionalIndex(meshIndex, nameof(meshIndex));
        LocalTransform = Guard3D.FiniteMatrix(localTransform, nameof(localTransform), requireInvertible: true);
        WorldTransform = Guard3D.FiniteMatrix(worldTransform, nameof(worldTransform), requireInvertible: true);
        var children = (childIndices ?? throw new ArgumentNullException(nameof(childIndices))).ToArray();
        for (var i = 0; i < children.Length; i++) Guard3D.NonNegative(children[i], nameof(childIndices));
        ChildIndices = Array.AsReadOnly(children);
        Path = string.IsNullOrWhiteSpace(path) ? Name : path;
        SkinIndex = ValidateOptionalIndex(skinIndex, nameof(skinIndex));
    }

    private static int? ValidateOptionalIndex(int? value, string name)
    {
        if (!value.HasValue || value.Value == -1) return null;
        return Guard3D.NonNegative(value.Value, name);
    }

    public int Index { get; }
    public string Name { get; }
    public int? ParentIndex { get; }
    public int? MeshIndex { get; }
    public Matrix4x4 LocalTransform { get; }
    public Matrix4x4 WorldTransform { get; }
    public IReadOnlyList<int> ChildIndices { get; }
    public string Path { get; }
    public int? SkinIndex { get; }
    public bool HasSkin => SkinIndex.HasValue;
    public Bounds3D Bounds { get; internal set; } = Bounds3D.Empty;
}
