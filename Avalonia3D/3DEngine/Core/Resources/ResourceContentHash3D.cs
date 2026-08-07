using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ThreeDEngine.Core.Resources;

/// <summary>
/// Stable SHA-256 content identity used by immutable CPU resources and physical GPU caches.
/// Every text/binary segment is length-prefixed, so metadata and payload boundaries cannot
/// alias even when input contains separator-like bytes.
/// </summary>
public readonly struct ResourceContentHash3D : IEquatable<ResourceContentHash3D>
{
    private readonly string? _hex;

    private ResourceContentHash3D(string hex, long contentVersion)
    {
        _hex = hex;
        ContentVersion = contentVersion;
    }

    public string Hex => _hex ?? string.Empty;
    public long ContentVersion { get; }
    public bool IsValid => _hex is { Length: 64 };

    public static ResourceContentHash3D Compute(string domain, ReadOnlySpan<byte> content)
    {
        if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("A resource hash domain is required.", nameof(domain));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendTextSegment(hash, domain);
        AppendBinarySegment(hash, content);
        return Finish(hash);
    }

    public static ResourceContentHash3D Compute(string domain, string metadata, ReadOnlySpan<byte> content)
    {
        if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("A resource hash domain is required.", nameof(domain));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendTextSegment(hash, domain);
        AppendTextSegment(hash, metadata ?? string.Empty);
        AppendBinarySegment(hash, content);
        return Finish(hash);
    }

    public bool Equals(ResourceContentHash3D other)
        => string.Equals(_hex, other._hex, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ResourceContentHash3D other && Equals(other);
    public override int GetHashCode() => _hex is null ? 0 : StringComparer.Ordinal.GetHashCode(_hex);
    public override string ToString() => Hex;

    public static bool operator ==(ResourceContentHash3D left, ResourceContentHash3D right) => left.Equals(right);
    public static bool operator !=(ResourceContentHash3D left, ResourceContentHash3D right) => !left.Equals(right);

    private static void AppendTextSegment(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void AppendBinarySegment(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static ResourceContentHash3D Finish(IncrementalHash hash)
    {
        var bytes = hash.GetHashAndReset();
        var version = (long)(BinaryPrimitives.ReadUInt64LittleEndian(bytes) & 0x7fff_ffff_ffff_ffffUL);
        if (version == 0) version = 1;
        return new ResourceContentHash3D(Convert.ToHexString(bytes).ToLowerInvariant(), version);
    }
}
