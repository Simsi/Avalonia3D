namespace ThreeDEngine.Core.Rendering;

public sealed class RenderObjectPacket
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string GeometryKey { get; init; }
    public required float[] Model { get; init; }
    public required float[] Mvp { get; init; }
    public required float[] Color { get; init; }
    public int LightingMode { get; init; }
    public float[] SpecularColor { get; init; } = System.Array.Empty<float>();
    public float[] SpecularParams { get; init; } = System.Array.Empty<float>();
    public float[] MaterialStrengths { get; init; } = System.Array.Empty<float>();
    public RenderMeshPayload? Mesh { get; init; }
}
