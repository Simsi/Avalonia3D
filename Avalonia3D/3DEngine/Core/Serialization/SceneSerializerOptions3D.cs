namespace ThreeDEngine.Core.Serialization;

public sealed class SceneSerializerOptions3D
{
    public int MaximumObjects { get; set; } = 100_000;
    public int MaximumLights { get; set; } = 16_384;
    public int MaximumEmbeddedTextureBytes { get; set; } = 256 * 1024 * 1024;
    public int MaximumExtensionParameterBytes { get; set; } = 16 * 1024 * 1024;
    public int MaximumDocumentBytes { get; set; } = 512 * 1024 * 1024;
    public bool WriteIndented { get; set; } = true;
    public bool IncludeEmbeddedTextures { get; set; } = true;
}
