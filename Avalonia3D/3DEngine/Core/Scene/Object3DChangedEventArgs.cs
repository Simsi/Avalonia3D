using System;

namespace ThreeDEngine.Core.Scene;

public sealed class Object3DChangedEventArgs : EventArgs
{
    public Object3DChangedEventArgs(SceneChangeKind kind)
    {
        Kind = kind;
    }

    public SceneChangeKind Kind { get; }
}
