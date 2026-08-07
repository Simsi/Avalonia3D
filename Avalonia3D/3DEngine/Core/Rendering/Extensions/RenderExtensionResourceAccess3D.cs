using System;

namespace ThreeDEngine.Core.Rendering.Extensions;

[Flags]
public enum RenderExtensionResourceAccess3D
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Sample = 1 << 2,
    Storage = 1 << 3,
    ColorAttachment = 1 << 4,
    DepthAttachment = 1 << 5,
    Indirect = 1 << 6
}
