namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelTextureAsset3D
{
    public ModelTextureAsset3D(int index, string? name, string? mimeType, string? uri, byte[]? data)
    {
        Index = index;
        Name = string.IsNullOrWhiteSpace(name) ? $"Texture_{index}" : name;
        MimeType = mimeType;
        Uri = uri;
        Data = data;
    }

    public int Index { get; }
    public string Name { get; }
    public string? MimeType { get; }
    public string? Uri { get; }
    public byte[]? Data { get; }
    public bool HasEmbeddedData => Data is { Length: > 0 };
}
