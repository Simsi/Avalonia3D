using System;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Interaction;

public sealed class SceneSelectionChangedEventArgs : EventArgs
{
    public SceneSelectionChangedEventArgs(Object3D? oldSelection, Object3D? newSelection)
    {
        OldSelection = oldSelection;
        NewSelection = newSelection;
    }

    public Object3D? OldSelection { get; }
    public Object3D? NewSelection { get; }
}
