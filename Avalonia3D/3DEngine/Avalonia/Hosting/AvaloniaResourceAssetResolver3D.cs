using System;
using System.IO;
using Avalonia.Platform;
using ThreeDEngine.Core.Assets.Resolvers;

namespace ThreeDEngine.Avalonia.Hosting;

public sealed class AvaloniaResourceAssetResolver3D : IAssetResolver3D
{
    public Stream? Open(string baseUri, string relativeUri)
    {
        var uri = ResolveUri(baseUri, relativeUri);
        if (uri is null) return null;
        try
        {
            return AssetLoader.Open(uri);
        }
        catch
        {
            return null;
        }
    }

    public bool Exists(string baseUri, string relativeUri)
    {
        var uri = ResolveUri(baseUri, relativeUri);
        if (uri is null) return false;
        try
        {
            using var stream = AssetLoader.Open(uri);
            return stream is not null;
        }
        catch
        {
            return false;
        }
    }

    private static Uri? ResolveUri(string baseUri, string relativeUri)
    {
        if (string.IsNullOrWhiteSpace(relativeUri)) return null;
        if (Uri.TryCreate(relativeUri, UriKind.Absolute, out var absolute)) return absolute;
        if (Uri.TryCreate(baseUri, UriKind.Absolute, out var baseAbsolute))
        {
            try { return new Uri(baseAbsolute, relativeUri); }
            catch { return null; }
        }
        return null;
    }
}
