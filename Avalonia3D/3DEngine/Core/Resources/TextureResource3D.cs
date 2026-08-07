using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Resources;

/// <summary>
/// Immutable encoded texture resource. Logical keys are diagnostic aliases; physical GPU
/// identity is derived from MIME type and encoded content. Identical resources share one weakly
/// interned immutable byte blob even when created independently by different materials.
/// </summary>
public sealed class TextureResource3D : IEquatable<TextureResource3D>
{
    private static readonly object ContentGate = new();
    private static readonly Dictionary<string, WeakReference<ContentBlob>> ContentPool = new(StringComparer.Ordinal);
    private static int _poolOperationCount;
    private readonly ContentBlob _content;

    private TextureResource3D(string logicalKey, byte[] encodedData, string? mimeType)
    {
        LogicalKey = Guard3D.RequiredText(logicalKey, nameof(logicalKey)).Trim();
        var snapshot = (byte[])Guard3D.RequiredBytes(encodedData, nameof(encodedData)).Clone();
        MimeType = NormalizeText(mimeType);
        ContentHash = ResourceContentHash3D.Compute("texture-encoded-v1", MimeType ?? string.Empty, snapshot);
        ResourceKey = "texture:" + ContentHash.Hex;
        _content = InternContent(ResourceKey, ContentHash, MimeType, snapshot);
    }

    public string LogicalKey { get; }
    public string? MimeType { get; }
    public ResourceContentHash3D ContentHash { get; }
    public string ResourceKey { get; }
    public long ContentVersion => ContentHash.ContentVersion;
    public int ByteLength => _content.Data.Length;
    public TextureResourceDescriptor3D Descriptor => new(LogicalKey, ResourceKey, MimeType, ContentHash, ByteLength);

    public static TextureResource3D Create(string logicalKey, byte[] encodedData, string? mimeType = null)
        => new(logicalKey, encodedData, mimeType);

    public byte[] CopyEncodedData() => (byte[])_content.Data.Clone();

    internal byte[] EncodedDataInternal => _content.Data;

    internal bool ContentEquals(TextureResource3D other)
        => other is not null &&
           ContentHash == other.ContentHash &&
           string.Equals(MimeType, other.MimeType, StringComparison.Ordinal) &&
           (_content == other._content || _content.Data.AsSpan().SequenceEqual(other._content.Data));

    public bool Equals(TextureResource3D? other)
        => other is not null && string.Equals(ResourceKey, other.ResourceKey, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TextureResource3D other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ResourceKey);
    public override string ToString() => $"{LogicalKey} [{ContentHash.Hex[..12]}] ({ByteLength} bytes)";

    private static ContentBlob InternContent(string resourceKey, ResourceContentHash3D hash, string? mimeType, byte[] snapshot)
    {
        lock (ContentGate)
        {
            if (ContentPool.TryGetValue(resourceKey, out var weak) && weak.TryGetTarget(out var existing))
            {
                if (existing.Hash != hash ||
                    !string.Equals(existing.MimeType, mimeType, StringComparison.Ordinal) ||
                    !existing.Data.AsSpan().SequenceEqual(snapshot))
                    throw new InvalidOperationException($"Texture content-hash collision for '{resourceKey}'.");
                CleanupPoolIfNeeded();
                return existing;
            }

            var created = new ContentBlob(hash, mimeType, snapshot);
            ContentPool[resourceKey] = new WeakReference<ContentBlob>(created);
            CleanupPoolIfNeeded();
            return created;
        }
    }

    private static void CleanupPoolIfNeeded()
    {
        if ((++_poolOperationCount & 0xff) != 0) return;
        var dead = new List<string>();
        foreach (var pair in ContentPool)
        {
            if (!pair.Value.TryGetTarget(out _)) dead.Add(pair.Key);
        }
        for (var i = 0; i < dead.Count; i++) ContentPool.Remove(dead[i]);
    }

    private static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private sealed class ContentBlob
    {
        public ContentBlob(ResourceContentHash3D hash, string? mimeType, byte[] data)
        {
            Hash = hash;
            MimeType = mimeType;
            Data = data;
        }
        public ResourceContentHash3D Hash { get; }
        public string? MimeType { get; }
        public byte[] Data { get; }
    }
}
