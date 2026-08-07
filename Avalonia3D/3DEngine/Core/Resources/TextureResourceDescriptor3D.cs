namespace ThreeDEngine.Core.Resources;

/// <summary>
/// Immutable public description of an encoded texture resource. The logical key is an alias for
/// diagnostics; <see cref="ResourceKey"/> and <see cref="ContentHash"/> define physical identity.
/// </summary>
public readonly record struct TextureResourceDescriptor3D(
    string LogicalKey,
    string ResourceKey,
    string? MimeType,
    ResourceContentHash3D ContentHash,
    int EncodedByteLength)
{
    public long ContentVersion => ContentHash.ContentVersion;
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(LogicalKey) &&
        !string.IsNullOrWhiteSpace(ResourceKey) &&
        ContentHash.IsValid &&
        EncodedByteLength > 0;
}
