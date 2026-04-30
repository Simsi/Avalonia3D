using System;

namespace ThreeDEngine.Core.Scene;

public sealed class Object3DChangedEventArgs : EventArgs
{
    public Object3DChangedEventArgs(Object3D source, SceneChangeKind kind, string? propertyName = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Kind = kind;
        PropertyName = propertyName;
    }

    public Object3D Source { get; }
    public SceneChangeKind Kind { get; }
    public string? PropertyName { get; }
}
