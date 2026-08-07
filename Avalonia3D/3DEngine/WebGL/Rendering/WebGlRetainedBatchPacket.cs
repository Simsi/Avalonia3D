namespace ThreeDEngine.Avalonia.WebGL.Rendering;

internal sealed class WebGlRetainedBatchPacket
{
    public required string Id { get; set; }
    public bool Transparent { get; set; }
    public bool IsHighScaleLayer { get; set; }
    public float SortDistanceSquared { get; set; }
    public int DrawOrder { get; set; }
}
