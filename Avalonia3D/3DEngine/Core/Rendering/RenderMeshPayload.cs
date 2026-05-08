namespace ThreeDEngine.Core.Rendering;

public sealed class RenderMeshPayload
{
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public float[] TexCoords0 { get; init; } = System.Array.Empty<float>();
    public float[] Tangents { get; init; } = System.Array.Empty<float>();
    public float[] BoneIndices0 { get; init; } = System.Array.Empty<float>();
    public float[] BoneWeights0 { get; init; } = System.Array.Empty<float>();
    public float[] MaterialSlots { get; init; } = System.Array.Empty<float>();
    public required int[] Indices { get; init; }
    public int[] WireframeIndices { get; init; } = System.Array.Empty<int>();
    public string VertexLayout { get; init; } = string.Empty;
    public long EstimatedUploadBytes { get; init; }
}
