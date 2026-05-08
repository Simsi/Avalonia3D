namespace ThreeDEngine.Core.Rendering.Pipeline;

public sealed class RenderTargetDescriptor3D
{
    public string Name { get; init; } = string.Empty;
    public RenderTargetFormat3D Format { get; init; } = RenderTargetFormat3D.Rgba8;
    public float Scale { get; init; } = 1f;
    public bool IsDepth { get; init; }
    public bool IsTransient { get; init; } = true;
}
