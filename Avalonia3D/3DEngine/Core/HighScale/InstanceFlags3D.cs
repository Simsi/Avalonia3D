using System;

namespace ThreeDEngine.Core.HighScale;

[Flags]
public enum InstanceFlags3D
{
    None = 0,
    Visible = 1,
    Pickable = 2,
    Selected = 4,
    Hovered = 8,
    DirtyTransform = 16,
    DirtyMaterial = 32,
    DirtyVisibility = 64
}
