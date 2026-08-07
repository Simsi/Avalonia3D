using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Assets.Resolvers;
using ThreeDEngine.Core.Importers.Gltf;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelAssetCache3D : IAsyncModelAssetLoader3D
{
    private static readonly string[] SidecarTextureExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".ktx2" };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(PathComparer);
    private readonly ContentAddressedAssetCache3D? _contentCache;
    private bool _disposed;

    public ModelAssetCache3D()
    {
    }

    public ModelAssetCache3D(ContentAddressedAssetCache3D contentCache)
    {
        _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
    }

    public int Count
    {
        get { lock (_gate) return _cache.Count; }
    }

    public int CachedAssetCount => Count;

    public bool IsDisposed { get { lock (_gate) return _disposed; } }

    public ModelAsset3D Load(string path, ModelImportOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Model path cannot be empty.", nameof(path));
        options = (options ?? new ModelImportOptions()).Clone();
        var resolved = ResolvePath(path, options.BaseDirectory);
        var cacheKey = BuildCacheKey(resolved.FullPath, options);
        var fingerprint = BuildDependencyFingerprint(resolved.FullPath, options);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(cacheKey, out var cached) && StringComparer.Ordinal.Equals(cached.Fingerprint, fingerprint))
                return cached.Asset;
        }

        var asset = GltfModelImporter.Import(resolved.FullPath, options);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(cacheKey, out var raced) && StringComparer.Ordinal.Equals(raced.Fingerprint, fingerprint))
                return raced.Asset;
            _cache[cacheKey] = new CacheEntry(asset, fingerprint);
            return asset;
        }
    }

    public async ValueTask<ModelAsset3D> LoadAsync(
        string path,
        ModelImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Model path cannot be empty.", nameof(path));
        options = (options ?? new ModelImportOptions()).Clone();
        var resolved = ResolvePath(path, options.BaseDirectory);
        var cacheKey = BuildCacheKey(resolved.FullPath, options);
        cancellationToken.ThrowIfCancellationRequested();
        var fileInfo = new FileInfo(resolved.FullPath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("The asynchronous model cache requires a concrete readable file. Configure an asynchronous resolver/loader for virtual or remote assets.", resolved.FullPath);
        if (options.MaxFileBytes > 0 && fileInfo.Length > options.MaxFileBytes)
            throw new InvalidDataException($"Model file '{resolved.FullPath}' contains {fileInfo.Length} bytes, exceeding MaxFileBytes={options.MaxFileBytes}.");

        var fingerprint = OperatingSystem.IsBrowser()
            ? BuildPrimaryFileFingerprint(fileInfo)
            : await Task.Run(() => BuildDependencyFingerprint(resolved.FullPath, options), cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(cacheKey, out var cached) && StringComparer.Ordinal.Equals(cached.Fingerprint, fingerprint))
                return cached.Asset;
        }

        var bytes = await File.ReadAllBytesAsync(resolved.FullPath, cancellationToken).ConfigureAwait(false);
        if (_contentCache is not null)
        {
            var blob = await _contentCache.StoreAsync(bytes, cancellationToken).ConfigureAwait(false);
            bytes = blob.BytesInternal;
        }
        cancellationToken.ThrowIfCancellationRequested();

        ModelAsset3D asset;
        if (OperatingSystem.IsBrowser())
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            asset = ImportLoadedBytes(bytes, resolved.FullPath, options);
        }
        else
        {
            asset = await Task.Run(() => ImportLoadedBytes(bytes, resolved.FullPath, options), cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(cacheKey, out var raced) && StringComparer.Ordinal.Equals(raced.Fingerprint, fingerprint))
                return raced.Asset;
            _cache[cacheKey] = new CacheEntry(asset, fingerprint);
            return asset;
        }
    }

    private static ModelAsset3D ImportLoadedBytes(byte[] bytes, string sourcePath, ModelImportOptions options)
    {
        if (Path.GetExtension(sourcePath).Equals(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return GltfModelImporter.ImportStream(stream, sourcePath, options);
        }
        return GltfModelImporter.ImportBytes(bytes, sourcePath, options);
    }

    public void Clear()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _cache.Clear();
        }
    }

    public bool Remove(string path, string? baseDirectory = null)
    {
        var resolved = ResolvePath(path, baseDirectory);
        lock (_gate)
        {
            ThrowIfDisposed();
            var removed = false;
            foreach (var key in _cache.Keys.Where(k => k.StartsWith(resolved.FullPath + "|", PathComparison)).ToArray())
            {
                removed |= _cache.Remove(key);
            }
            return removed;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cache.Clear();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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
        builder.Append("|warningsAsErrors=").Append(options.TreatWarningsAsErrors);
        builder.Append("|maxFile=").Append(options.MaxFileBytes);
        builder.Append("|maxJson=").Append(options.MaxJsonBytes);
        builder.Append("|maxBin=").Append(options.MaxBinaryChunkBytes);
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
        var typeName = resolver.GetType().AssemblyQualifiedName ?? resolver.GetType().FullName ?? resolver.GetType().Name;
        return typeName + "#" + RuntimeHelpers.GetHashCode(resolver).ToString("X8");
    }

    private static string BuildDependencyFingerprint(string fullPath, ModelImportOptions options)
    {
        var files = new SortedSet<string>(PathComparer) { Path.GetFullPath(fullPath) };
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

    private static string BuildPrimaryFileFingerprint(FileInfo fileInfo)
        => fileInfo.FullName + ":" + fileInfo.Length + "@" + fileInfo.LastWriteTimeUtc.Ticks;

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
