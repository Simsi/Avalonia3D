using System;
using System.Numerics;
using ThreeDEngine.Core.Assets.Models;

namespace ThreeDEngine.Core.Interaction;

public sealed class ModelPointerEventArgs : EventArgs
{
    public ModelPointerEventArgs(
        ModelPointerEventKind eventKind,
        ModelHitResult3D hit,
        Vector2 viewportPosition,
        SceneMouseButton button)
    {
        EventKind = eventKind;
        Hit = hit ?? throw new ArgumentNullException(nameof(hit));
        ViewportPosition = viewportPosition;
        Button = button;
    }

    public ModelPointerEventKind EventKind { get; }
    public ModelHitResult3D Hit { get; }
    public ImportedModel3D Model => Hit.Model;
    public ModelPart3D Part => Hit.Part;
    public ModelElementId3D ElementId => Hit.ElementId;
    public string NodePath => Hit.NodePath;
    public string NodeName => Hit.NodeName;
    public int NodeIndex => Hit.NodeIndex;
    public int MeshIndex => Hit.MeshIndex;
    public int PrimitiveIndex => Hit.PrimitiveIndex;
    public int TriangleIndex => Hit.TriangleIndex;
    public Vector3 WorldPosition => Hit.WorldPosition;
    public Vector3 WorldNormal => Hit.WorldNormal;
    public Vector2 ViewportPosition { get; }
    public SceneMouseButton Button { get; }
}
