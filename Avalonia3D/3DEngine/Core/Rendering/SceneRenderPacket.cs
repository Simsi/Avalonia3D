using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering;

public sealed class SceneRenderPacket
{
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required float[] ClearColor { get; init; }
    public required List<RenderObjectPacket> Objects { get; init; }
}
