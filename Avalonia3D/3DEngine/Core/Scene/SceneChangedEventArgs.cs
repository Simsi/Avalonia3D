using System;

namespace ThreeDEngine.Core.Scene;

public sealed class SceneChangedEventArgs : EventArgs
{
    public SceneChangedEventArgs(
        SceneChangeKind kind,
        Object3D? source = null,
        SceneChangeFlags3D kinds = SceneChangeFlags3D.None,
        long firstSequence = 0,
        long lastSequence = 0)
    {
        Kind = kind;
        Source = source;
        Kinds = kinds == SceneChangeFlags3D.None ? kind.ToFlag() : kinds;
        FirstSequence = firstSequence;
        LastSequence = lastSequence < firstSequence ? firstSequence : lastSequence;
    }

    public SceneChangeKind Kind { get; }
    public Object3D? Source { get; }
    public SceneChangeFlags3D Kinds { get; }
    public long FirstSequence { get; }
    public long LastSequence { get; }
    public int ChangeCount => FirstSequence == 0 ? 1 : checked((int)(LastSequence - FirstSequence + 1));
    public bool IsBatch => ChangeCount > 1;

    public bool Contains(SceneChangeKind kind) => (Kinds & kind.ToFlag()) != 0;
}
