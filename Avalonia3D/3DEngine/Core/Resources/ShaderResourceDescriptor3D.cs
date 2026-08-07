namespace ThreeDEngine.Core.Resources;

/// <summary>Immutable public description of shader source content and its physical identity.</summary>
public readonly record struct ShaderResourceDescriptor3D(
    string LogicalKey,
    string ResourceKey,
    ShaderStage3D Stage,
    string EntryPoint,
    ResourceContentHash3D ContentHash,
    int SourceByteLength)
{
    public long ContentVersion => ContentHash.ContentVersion;
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(LogicalKey) &&
        !string.IsNullOrWhiteSpace(ResourceKey) &&
        !string.IsNullOrWhiteSpace(EntryPoint) &&
        ContentHash.IsValid &&
        SourceByteLength > 0;
}
