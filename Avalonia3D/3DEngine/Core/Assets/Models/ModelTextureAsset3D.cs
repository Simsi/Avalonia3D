using System;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelTextureAsset3D
{
    private readonly byte[]? _data;

    public ModelTextureAsset3D(int index, string? name, string? mimeType, string? uri, byte[]? data)
    {
        Index = Guard3D.NonNegative(index, nameof(index));
        Name = string.IsNullOrWhiteSpace(name) ? $"Texture_{index}" : name;
        MimeType = mimeType;
        Uri = uri;
        _data = data is { Length: > 0 } ? (byte[])data.Clone() : null;
    }

    public int Index { get; }
    public string Name { get; }
    public string? MimeType { get; }
    public string? Uri { get; }
    public byte[]? Data => _data is null ? null : (byte[])_data.Clone();
    internal byte[]? DataInternal => _data;
    public bool HasEmbeddedData => _data is { Length: > 0 };
}
