using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Materials;

/// <summary>
/// Immutable custom material payload. The extension id resolves a registered render extension;
/// parameters are opaque shader bytes and textures retain content-addressed identity.
/// </summary>
public sealed class MaterialShaderExtension3D : IEquatable<MaterialShaderExtension3D>
{
    private readonly byte[] _parameters;
    private readonly TextureResource3D[] _textures;
    private readonly ReadOnlyCollection<TextureResource3D> _texturesView;

    public MaterialShaderExtension3D(
        string extensionId,
        int materialType,
        ReadOnlySpan<byte> parameters,
        IEnumerable<TextureResource3D>? textures = null)
    {
        if (string.IsNullOrWhiteSpace(extensionId)) throw new ArgumentException("Extension id cannot be empty.", nameof(extensionId));
        if (!StringComparer.Ordinal.Equals(extensionId, extensionId.Trim())) throw new ArgumentException("Extension id cannot contain leading or trailing whitespace.", nameof(extensionId));
        ExtensionId = extensionId;
        if (materialType < 0) throw new ArgumentOutOfRangeException(nameof(materialType));
        MaterialType = materialType;
        _parameters = parameters.ToArray();
        _textures = textures?.ToArray() ?? Array.Empty<TextureResource3D>();
        for (var i = 0; i < _textures.Length; i++) ArgumentNullException.ThrowIfNull(_textures[i]);
        _texturesView = Array.AsReadOnly(_textures);
        Identity = BuildIdentity();
    }

    public string ExtensionId { get; }
    public int MaterialType { get; }
    public string Identity { get; }
    public ReadOnlyMemory<byte> Parameters => _parameters.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])_parameters.Clone());
    public IReadOnlyList<TextureResource3D> Textures => _texturesView;
    internal ReadOnlyMemory<byte> ParametersInternal => _parameters;
    internal int ParameterByteLength => _parameters.Length;

    public bool Equals(MaterialShaderExtension3D? other) => other is not null && StringComparer.Ordinal.Equals(Identity, other.Identity);
    public override bool Equals(object? obj) => obj is MaterialShaderExtension3D other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identity);

    private string BuildIdentity()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, ExtensionId);
        Span<byte> integer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(integer, MaterialType);
        hash.AppendData(integer);
        BinaryPrimitives.WriteInt32LittleEndian(integer, _parameters.Length);
        hash.AppendData(integer);
        hash.AppendData(_parameters);
        BinaryPrimitives.WriteInt32LittleEndian(integer, _textures.Length);
        hash.AppendData(integer);
        for (var i = 0; i < _textures.Length; i++) AppendText(hash, _textures[i].ResourceKey);
        return ExtensionId + ":" + MaterialType + ":" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, byteCount);
        hash.AppendData(length);
        if (byteCount == 0) return;
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(bytes);
    }
}
