using System;

namespace ThreeDEngine.Core.Scene;

public sealed class Object3DChangedEventArgs : EventArgs
{
    public Object3DChangedEventArgs(SceneChangeKind kind, Object3D source)
    {
        Kind = kind;
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public SceneChangeKind Kind { get; }
    public Object3D Source { get; }
}
