using System;

namespace ThreeDEngine.Core.Scene;

public sealed class SceneChangedEventArgs : EventArgs
{
    public SceneChangedEventArgs(SceneChangeKind kind, Object3D? source = null)
    {
        Kind = kind;
        Source = source;
    }

    public SceneChangeKind Kind { get; }
    public Object3D? Source { get; }
}
