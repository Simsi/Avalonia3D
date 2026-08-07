using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Extensions;

public interface IRenderExtension3D
{
    string Id { get; }
    int Version { get; }
    IReadOnlyList<RenderExtensionPass3D> Passes { get; }
}
