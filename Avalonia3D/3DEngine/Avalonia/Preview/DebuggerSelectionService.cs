using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Preview;

internal sealed class DebuggerSelectionService
{
    public Object3D? SelectedObject { get; private set; }

    public event EventHandler<Object3D?>? SelectionChanged;

    public void Select(Object3D selected, IEnumerable<Object3D> visibleObjects)
    {
        SelectedObject = selected ?? throw new ArgumentNullException(nameof(selected));
        foreach (var obj in visibleObjects)
        {
            obj.IsSelected = ReferenceEquals(obj, selected);
        }

        SelectionChanged?.Invoke(this, selected);
    }

    public void Clear(IEnumerable<Object3D> visibleObjects)
    {
        SelectedObject = null;
        foreach (var obj in visibleObjects)
        {
            obj.IsSelected = false;
        }

        SelectionChanged?.Invoke(this, null);
    }
}
