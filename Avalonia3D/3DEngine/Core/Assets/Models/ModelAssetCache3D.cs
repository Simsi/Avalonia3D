using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ThreeDEngine.Core.Assets.Resolvers;
using ThreeDEngine.Core.Importers.Gltf;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelAssetCache3D
{
    private static readonly string[] SidecarTextureExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".ktx2" };
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static ModelAssetCache3D Shared { get; } = new();

    public ModelAsset3D Load(string path, ModelImportOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Model path cannot be empty.", nameof(path));
        options ??= new ModelImportOptions();
        var resolved = ResolvePath(path, options.BaseDirectory);
        var cacheKey = BuildCacheKey(resolved.FullPath, options);
        var fingerprint = BuildDependencyFingerprint(resolved.FullPath, options);
        if (_cache.TryGetValue(cacheKey, out var cached) && StringComparer.Ordinal.Equals(cached.Fingerprint, fingerprint))
        {
            return cached.Asset;
        }

        var asset = GltfModelImporter.Import(resolved.FullPath, options);
        _cache[cacheKey] = new CacheEntry(asset, fingerprint);
        return asset;
    }

    public void Clear() => _cache.Clear();

    public bool Remove(string path, string? baseDirectory = null)
    {
        var resolved = ResolvePath(path, baseDirectory);
        var removed = false;
        foreach (var key in _cache.Keys.Where(k => k.StartsWith(resolved.FullPath + "|", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            removed |= _cache.Remove(key);
        }
        return removed;
    }

    private static string BuildCacheKey(string fullPath, ModelImportOptions options)
    {
        var builder = new StringBuilder();
        builder.Append(Path.GetFullPath(fullPath));
        builder.Append("|base=").Append(options.BaseDirectory ?? string.Empty);
        builder.Append("|resolver=").Append(GetResolverIdentity(options.AssetResolver));
        builder.Append("|extBuf=").Append(options.ResolveExternalBuffers);
        builder.Append("|extImg=").Append(options.ResolveExternalImages);
        builder.Append("|data=").Append(options.ResolveDataUris);
        builder.Append("|sidecar=").Append(options.ResolveSidecarImages);
        builder.Append("|strict=").Append(options.StrictValidation);
        builder.Append("|strictGlb=").Append(options.StrictGlbValidation);
        builder.Append("|normals=").Append(options.GenerateMissingNormals);
        builder.Append("|unit=").Append(options.NormalizeToUnitSize);
        builder.Append("|maxFile=").Append(options.MaxFileBytes);
        builder.Append("|maxTex=").Append(options.MaxTextureBytes);
        builder.Append("|maxV=").Append(options.MaxVerticesPerPrimitive);
        builder.Append("|maxI=").Append(options.MaxIndicesPerPrimitive);
        return builder.ToString();
    }

    private static string GetResolverIdentity(IAssetResolver3D? resolver)
    {
        if (resolver is null) return "<none>";
        if (resolver is CompositeAssetResolver3D composite)
        {
            return "Composite(" + string.Join(",", composite.Resolvers.Select(GetResolverIdentity)) + ")";
        }
        return resolver.GetType().AssemblyQualifiedName ?? resolver.GetType().FullName ?? resolver.GetType().Name;
    }

    private static string BuildDependencyFingerprint(string fullPath, ModelImportOptions options)
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(fullPath) };
        AddSidecarCandidates(files, fullPath, options);
        AddReferencedFiles(files, fullPath, options);

        var builder = new StringBuilder();
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                builder.Append(info.FullName).Append(':');
                if (info.Exists)
                {
                    builder.Append(info.Length).Append('@').Append(info.LastWriteTimeUtc.Ticks);
                }
                else
                {
                    builder.Append("missing");
                }
                builder.Append('|');
            }
            catch
            {
                builder.Append(file).Append(":unreadable|");
            }
        }
        return builder.ToString();
    }

    private static void AddSidecarCandidates(ISet<string> files, string fullPath, ModelImportOptions options)
    {
        if (!options.ResolveSidecarImages) return;
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        foreach (var extension in SidecarTextureExtensions)
        {
            var candidate = Path.Combine(directory, stem + extension);
            if (File.Exists(candidate)) files.Add(Path.GetFullPath(candidate));
        }
    }

    private static void AddReferencedFiles(ISet<string> files, string fullPath, ModelImportOptions options)
    {
        if (!options.ResolveExternalBuffers && !options.ResolveExternalImages) return;
        string? json = null;
        try
        {
            var extension = Path.GetExtension(fullPath);
            if (extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
            {
                json = File.ReadAllText(fullPath);
            }
            else if (extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
            {
                json = TryReadGlbJson(fullPath);
            }
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanUriArray(doc.RootElement, "buffers", options.ResolveExternalBuffers, files, fullPath);
            ScanUriArray(doc.RootElement, "images", options.ResolveExternalImages, files, fullPath);
        }
        catch
        {
            // Cache fingerprinting must never make model loading fail. Importer diagnostics
            // remain the authority for malformed glTF/GLB content.
        }
    }

    private static void ScanUriArray(JsonElement root, string property, bool enabled, ISet<string> files, string fullPath)
    {
        if (!enabled || !root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) return;
        foreach (var item in array.EnumerateArray())
        {
            if (!item.TryGetProperty("uri", out var uriElement) || uriElement.ValueKind != JsonValueKind.String) continue;
            var uri = uriElement.GetString();
            if (string.IsNullOrWhiteSpace(uri) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute) && !absolute.IsFile) continue;
            var local = FileSystemAssetResolver3D.ResolvePath(fullPath, uri);
            if (!string.IsNullOrWhiteSpace(local)) files.Add(Path.GetFullPath(local));
        }
    }

    private static string? TryReadGlbJson(string fullPath)
    {
        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length < 20) return null;
        if (BitConverter.ToUInt32(bytes, 0) != 0x46546C67u) return null;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkLength = BitConverter.ToInt32(bytes, offset);
            var chunkType = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;
            if (chunkLength < 0 || offset + chunkLength > bytes.Length) return null;
            if (chunkType == 0x4E4F534Au)
            {
                return Encoding.UTF8.GetString(bytes, offset, chunkLength).TrimEnd('\0', ' ', '\r', '\n', '\t');
            }
            offset += chunkLength;
        }
        return null;
    }

    private static ResolvedPath ResolvePath(string path, string? baseDirectory)
    {
        var candidates = new List<string>();
        if (Path.IsPathRooted(path)) candidates.Add(path);
        if (!string.IsNullOrWhiteSpace(baseDirectory)) candidates.Add(Path.Combine(baseDirectory!, path));
        candidates.Add(Path.Combine(global::System.Environment.CurrentDirectory, path));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, path));

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return new ResolvedPath(full);
        }

        return new ResolvedPath(Path.GetFullPath(candidates[0]));
    }

    private readonly record struct ResolvedPath(string FullPath);
    private sealed record CacheEntry(ModelAsset3D Asset, string Fingerprint);
}
