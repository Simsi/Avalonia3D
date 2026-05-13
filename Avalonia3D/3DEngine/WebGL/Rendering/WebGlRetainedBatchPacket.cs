namespace ThreeDEngine.Avalonia.WebGL.Rendering;

internal sealed class WebGlRetainedBatchPacket
{
    public required string Id { get; init; }
    public bool Transparent { get; init; }
    public bool IsHighScaleLayer { get; init; }
    public float SortDistanceSquared { get; init; }
    public int DrawOrder { get; set; }
}
