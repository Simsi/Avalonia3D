using System;
using System.Globalization;

namespace ThreeDEngine.Core.Rendering;

internal readonly struct RendererResourceKey : IEquatable<RendererResourceKey>
{
    public RendererResourceKey(string kind, string id, long version = 0)
    {
        Kind = string.IsNullOrWhiteSpace(kind) ? "resource" : kind;
        Id = id ?? string.Empty;
        Version = version;
    }

    public string Kind { get; }
    public string Id { get; }
    public long Version { get; }
    public string StableId => Kind + ":" + Id;

    public static RendererResourceKey Mesh(string meshKey, long version = 0) => new("mesh", meshKey, version);
    public static RendererResourceKey Material(string materialKey, long version = 0) => new("material", materialKey, version);
    public static RendererResourceKey Shader(string shaderKey, long version = 0) => new("shader", shaderKey, version);
    public static RendererResourceKey Texture(string textureKey, long version = 0) => new("texture", textureKey, version);

    public bool Equals(RendererResourceKey other)
        => Version == other.Version
           && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
           && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is RendererResourceKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Kind), StringComparer.Ordinal.GetHashCode(Id), Version);
    public override string ToString() => StableId + "@" + Version.ToString(CultureInfo.InvariantCulture);

    public static bool operator ==(RendererResourceKey left, RendererResourceKey right) => left.Equals(right);
    public static bool operator !=(RendererResourceKey left, RendererResourceKey right) => !left.Equals(right);
}
