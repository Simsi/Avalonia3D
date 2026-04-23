namespace ThreeDEngine.Core.Rendering;

public sealed class RenderObjectPacket
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string GeometryKey { get; init; }
    public required float[] Model { get; init; }
    public required float[] Mvp { get; init; }
    public required float[] Color { get; init; }
    public RenderMeshPayload? Mesh { get; init; }
}
