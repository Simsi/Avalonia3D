using System;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral immutable texture upload descriptor. The physical key is content-derived;
/// the logical key is retained only for diagnostics and collision reporting.
/// </summary>
internal readonly struct RenderTextureResource3D
{
    public RenderTextureResource3D(TextureResource3D resource)
        => Resource = resource ?? throw new ArgumentNullException(nameof(resource));

    public TextureResource3D Resource { get; }
    public string Key => Resource.ResourceKey;
    public string LogicalKey => Resource.LogicalKey;
    public string? MimeType => Resource.MimeType;
    public ResourceContentHash3D ContentHash => Resource.ContentHash;
    public long Version => Resource.ContentVersion;
    public int ByteLength => Resource.ByteLength;
    public byte[] Data => Resource.CopyEncodedData();
    internal byte[] DataInternal => Resource.EncodedDataInternal;
    public bool IsValid => Resource is not null && Resource.ContentHash.IsValid;
}
