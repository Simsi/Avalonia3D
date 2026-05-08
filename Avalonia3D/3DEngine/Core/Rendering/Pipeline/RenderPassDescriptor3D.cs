using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Pipeline;

public sealed class RenderPassDescriptor3D
{
    public RenderPassKind3D Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<RenderTargetDescriptor3D> Inputs { get; init; } = new List<RenderTargetDescriptor3D>();
    public IReadOnlyList<RenderTargetDescriptor3D> Outputs { get; init; } = new List<RenderTargetDescriptor3D>();
}
