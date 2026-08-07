using System;
using System.Collections.Generic;
using System.Text;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Resources;

public enum ShaderStage3D
{
    Vertex = 0,
    Fragment = 1,
    Compute = 2
}

/// <summary>
/// Immutable shader source blob. Equal source/stage/entry-point combinations share one weakly
/// interned UTF-8 payload and are ready for the unified shader system introduced in a later stage.
/// </summary>
public sealed class ShaderResource3D : IEquatable<ShaderResource3D>
{
    private static readonly object ContentGate = new();
    private static readonly Dictionary<string, WeakReference<ContentBlob>> ContentPool = new(StringComparer.Ordinal);
    private static int _poolOperationCount;
    private readonly ContentBlob _content;

    private ShaderResource3D(string logicalKey, ShaderStage3D stage, string source, string entryPoint)
    {
        LogicalKey = Guard3D.RequiredText(logicalKey, nameof(logicalKey)).Trim();
        Stage = Guard3D.Defined(stage, nameof(stage));
        EntryPoint = Guard3D.RequiredText(entryPoint, nameof(entryPoint)).Trim();
        source = Guard3D.RequiredText(source, nameof(source));
        var utf8 = Encoding.UTF8.GetBytes(source);
        ContentHash = ResourceContentHash3D.Compute("shader-source-v1", $"{(int)Stage}:{EntryPoint}", utf8);
        ResourceKey = "shader:" + ContentHash.Hex;
        _content = InternContent(ResourceKey, ContentHash, Stage, EntryPoint, utf8);
    }

    public string LogicalKey { get; }
    public ShaderStage3D Stage { get; }
    public string EntryPoint { get; }
    public ResourceContentHash3D ContentHash { get; }
    public string ResourceKey { get; }
    public long ContentVersion => ContentHash.ContentVersion;
    public int ByteLength => _content.Data.Length;
    public ShaderResourceDescriptor3D Descriptor => new(LogicalKey, ResourceKey, Stage, EntryPoint, ContentHash, ByteLength);

    public static ShaderResource3D Create(string logicalKey, ShaderStage3D stage, string source, string entryPoint = "main")
        => new(logicalKey, stage, source, entryPoint);

    public string GetSource() => Encoding.UTF8.GetString(_content.Data);
    internal ReadOnlyMemory<byte> Utf8SourceInternal => _content.Data;
    internal int ByteLengthInternal => _content.Data.Length;

    internal bool ContentEquals(ShaderResource3D other)
        => other is not null &&
           ContentHash == other.ContentHash &&
           Stage == other.Stage &&
           string.Equals(EntryPoint, other.EntryPoint, StringComparison.Ordinal) &&
           (_content == other._content || _content.Data.AsSpan().SequenceEqual(other._content.Data));

    public bool Equals(ShaderResource3D? other)
        => other is not null && string.Equals(ResourceKey, other.ResourceKey, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ShaderResource3D other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ResourceKey);

    private static ContentBlob InternContent(string resourceKey, ResourceContentHash3D hash, ShaderStage3D stage, string entryPoint, byte[] source)
    {
        lock (ContentGate)
        {
            if (ContentPool.TryGetValue(resourceKey, out var weak) && weak.TryGetTarget(out var existing))
            {
                if (existing.Hash != hash || existing.Stage != stage ||
                    !string.Equals(existing.EntryPoint, entryPoint, StringComparison.Ordinal) ||
                    !existing.Data.AsSpan().SequenceEqual(source))
                    throw new InvalidOperationException($"Shader content-hash collision for '{resourceKey}'.");
                CleanupPoolIfNeeded();
                return existing;
            }

            var created = new ContentBlob(hash, stage, entryPoint, source);
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

    private sealed class ContentBlob
    {
        public ContentBlob(ResourceContentHash3D hash, ShaderStage3D stage, string entryPoint, byte[] data)
        {
            Hash = hash;
            Stage = stage;
            EntryPoint = entryPoint;
            Data = data;
        }
        public ResourceContentHash3D Hash { get; }
        public ShaderStage3D Stage { get; }
        public string EntryPoint { get; }
        public byte[] Data { get; }
    }
}
