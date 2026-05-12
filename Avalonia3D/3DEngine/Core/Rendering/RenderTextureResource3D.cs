namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral texture upload descriptor. The Core render-resource plan owns which
/// textures are live for the current render plan; backends only decode/upload bytes.
/// </summary>
public readonly struct RenderTextureResource3D
{
    public RenderTextureResource3D(string key, byte[] data, int version)
    {
        Key = key ?? string.Empty;
        Data = data ?? System.Array.Empty<byte>();
        Version = version;
    }

    public string Key { get; }
    public byte[] Data { get; }
    public int Version { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Key) && Data.Length > 0;
}
