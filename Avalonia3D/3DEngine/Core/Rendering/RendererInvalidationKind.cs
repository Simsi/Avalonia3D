using System;

namespace ThreeDEngine.Core.Rendering;

[Flags]
public enum RendererInvalidationKind
{
    None = 0,
    DrawOnly = 1 << 0,
    ResourceUpload = 1 << 1,
    MaterialUpload = 1 << 2,
    TransformUpload = 1 << 3,
    BatchRebuild = 1 << 4,
    RegistryRebuild = 1 << 5,
    DebugOverlay = 1 << 6,
    HighScaleState = 1 << 7,
    HighScaleStructure = 1 << 8,
    FullFrame = DrawOnly | ResourceUpload | MaterialUpload | TransformUpload | BatchRebuild | RegistryRebuild
}
