using System;
using System.IO;

namespace ThreeDEngine.Core.Assets.Resolvers;

public sealed class FileSystemAssetResolver3D : IAssetResolver3D
{
    public static FileSystemAssetResolver3D Shared { get; } = new();

    /// <summary>
    /// When false, relative glTF asset references are constrained to the directory
    /// that contains the model file. Keep this false for viewers that may open
    /// untrusted assets. Set it to true only for trusted project tooling.
    /// </summary>
    public bool AllowParentDirectoryTraversal { get; init; }

    /// <summary>
    /// Absolute file:// references are disabled by default because untrusted glTF
    /// files can otherwise read arbitrary local files through external buffers/images.
    /// Enable only for trusted offline tooling.
    /// </summary>
    public bool AllowAbsoluteFileUris { get; init; }

    public Stream? Open(string baseUri, string relativeUri)
    {
        var path = ResolvePath(baseUri, relativeUri, AllowParentDirectoryTraversal, AllowAbsoluteFileUris);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return File.OpenRead(path);
    }

    public bool Exists(string baseUri, string relativeUri)
    {
        var path = ResolvePath(baseUri, relativeUri, AllowParentDirectoryTraversal, AllowAbsoluteFileUris);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public static string? ResolvePath(string baseUri, string relativeUri)
        => ResolvePath(baseUri, relativeUri, allowParentDirectoryTraversal: false, allowAbsoluteFileUris: false);

    public static string? ResolvePath(string baseUri, string relativeUri, bool allowParentDirectoryTraversal)
        => ResolvePath(baseUri, relativeUri, allowParentDirectoryTraversal, allowAbsoluteFileUris: false);

    public static string? ResolvePath(string baseUri, string relativeUri, bool allowParentDirectoryTraversal, bool allowAbsoluteFileUris)
    {
        if (string.IsNullOrWhiteSpace(relativeUri)) return null;
        try
        {
            var basePath = baseUri;
            if (Uri.TryCreate(baseUri, UriKind.Absolute, out var baseAbsolute) && baseAbsolute.IsFile)
            {
                basePath = baseAbsolute.LocalPath;
            }

            var baseDirectory = File.Exists(basePath)
                ? Path.GetDirectoryName(Path.GetFullPath(basePath))
                : Directory.Exists(basePath)
                    ? Path.GetFullPath(basePath)
                    : Path.GetDirectoryName(Path.GetFullPath(basePath));

            if (string.IsNullOrWhiteSpace(baseDirectory)) return null;
            var root = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            string combined;
            if (Uri.TryCreate(relativeUri, UriKind.Absolute, out var absolute))
            {
                if (!absolute.IsFile || !allowAbsoluteFileUris) return null;
                combined = Path.GetFullPath(absolute.LocalPath);
            }
            else if (Path.IsPathRooted(relativeUri))
            {
                if (!allowAbsoluteFileUris) return null;
                combined = Path.GetFullPath(relativeUri);
            }
            else
            {
                combined = Path.GetFullPath(Path.Combine(baseDirectory, relativeUri.Replace('/', Path.DirectorySeparatorChar)));
            }

            if (!allowParentDirectoryTraversal && !combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return combined;
        }
        catch
        {
            return null;
        }
    }
}
