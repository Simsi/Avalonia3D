using System;
using System.Numerics;
using ThreeDEngine.Core.Assets.Models;

namespace ThreeDEngine.Core.Interaction;

public sealed class ModelHitResult3D
{
    public ModelHitResult3D(
        ImportedModel3D model,
        ModelPart3D part,
        ModelElementId3D elementId,
        Vector3 worldPosition,
        Vector3 worldNormal,
        float distance)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Part = part ?? throw new ArgumentNullException(nameof(part));
        ElementId = elementId;
        WorldPosition = worldPosition;
        WorldNormal = SafeNormal(worldNormal);
        Distance = distance;
    }

    public ImportedModel3D Model { get; }
    public ModelPart3D Part { get; }
    public ModelElementId3D ElementId { get; }
    public string ModelId => ElementId.ModelId;
    public string NodePath => ElementId.NodePath;
    public string NodeName => Part.Node.Name;
    public int NodeIndex => ElementId.NodeIndex;
    public int MeshIndex => ElementId.MeshIndex;
    public int PrimitiveIndex => ElementId.PrimitiveIndex;
    public int TriangleIndex => ElementId.TriangleIndex ?? -1;
    public string PrimitivePath => ElementId.PrimitivePath;
    public string ElementPath => ElementId.TrianglePath;
    public Vector3 WorldPosition { get; }
    public Vector3 WorldNormal { get; }
    public float Distance { get; }

    public bool IsSameInteractiveElement(ModelHitResult3D? other)
    {
        if (other is null) return false;
        return ReferenceEquals(Model, other.Model) &&
               string.Equals(NodePath, other.NodePath, StringComparison.Ordinal) &&
               MeshIndex == other.MeshIndex &&
               PrimitiveIndex == other.PrimitiveIndex;
    }

    public override string ToString() => $"{Model.Name}:{ElementPath} @ {Distance:0.###}";

    private static Vector3 SafeNormal(Vector3 normal)
    {
        var len = normal.LengthSquared();
        return len > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
    }
}
