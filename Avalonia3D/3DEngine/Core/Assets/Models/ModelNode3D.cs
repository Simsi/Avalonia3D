using System;
using System.Collections.Generic;
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
        Index = index;
        Name = string.IsNullOrWhiteSpace(name) ? $"Node_{index}" : name;
        ParentIndex = parentIndex;
        MeshIndex = meshIndex;
        LocalTransform = localTransform;
        WorldTransform = worldTransform;
        ChildIndices = childIndices ?? Array.Empty<int>();
        Path = string.IsNullOrWhiteSpace(path) ? Name : path;
        SkinIndex = skinIndex;
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
