using System;
using System.Numerics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Interaction;

public sealed class ScenePointerEventArgs : EventArgs
{
    public ScenePointerEventArgs(
        Object3D target,
        Vector2 viewportPosition,
        Vector3 worldPosition,
        SceneMouseButton button)
    {
        Target = target;
        ViewportPosition = viewportPosition;
        WorldPosition = worldPosition;
        Button = button;
    }

    public Object3D Target { get; }
    public Vector2 ViewportPosition { get; }
    public Vector3 WorldPosition { get; }
    public SceneMouseButton Button { get; }
}
