namespace ThreeDEngine.Core.Rendering;

public sealed class RenderMeshPayload
{
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required int[] Indices { get; init; }
}
