using System;
using System.IO;

namespace ThreeDEngine.Core.Assets.Streaming;

public sealed class AssetStreamingOptions3D
{
    public int MaximumConcurrentLoads { get; set; } = OperatingSystem.IsBrowser() ? 1 : global::System.Math.Max(1, global::System.Environment.ProcessorCount / 2);
    public int MaximumQueuedRequests { get; set; } = 256;
    public int MaximumConcurrentTextureLoads { get; set; } = OperatingSystem.IsBrowser() ? 2 : global::System.Math.Max(2, global::System.Environment.ProcessorCount / 2);
    public long CpuResidentByteBudget { get; set; } = OperatingSystem.IsBrowser() ? 256L * 1024L * 1024L : 1024L * 1024L * 1024L;
    public long ContentCacheByteBudget { get; set; } = OperatingSystem.IsBrowser() ? 128L * 1024L * 1024L : 512L * 1024L * 1024L;
    public long TextureResidentByteBudget { get; set; } = OperatingSystem.IsBrowser() ? 192L * 1024L * 1024L : 768L * 1024L * 1024L;
    public string? PersistentContentCacheDirectory { get; set; }
    public bool PersistContentCache { get; set; }
    public bool RejectSynchronousLoaderInBrowser { get; set; } = true;
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    internal AssetStreamingConfiguration3D Freeze()
    {
        if (MaximumConcurrentLoads <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentLoads));
        if (MaximumQueuedRequests <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumQueuedRequests));
        if (MaximumConcurrentTextureLoads <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentTextureLoads));
        if (CpuResidentByteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(CpuResidentByteBudget));
        if (ContentCacheByteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(ContentCacheByteBudget));
        if (TextureResidentByteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(TextureResidentByteBudget));
        if (ShutdownTimeout <= TimeSpan.Zero || ShutdownTimeout > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout));

        string? directory = null;
        if (PersistContentCache)
        {
            if (OperatingSystem.IsBrowser())
                throw new PlatformNotSupportedException("Persistent filesystem asset cache is unavailable in the browser runtime. Configure a browser storage adapter explicitly instead of falling back to memory.");
            directory = string.IsNullOrWhiteSpace(PersistentContentCacheDirectory)
                ? Path.Combine(global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData), "Avalonia3D", "AssetCache")
                : Path.GetFullPath(PersistentContentCacheDirectory);
        }

        return new AssetStreamingConfiguration3D(
            MaximumConcurrentLoads,
            MaximumQueuedRequests,
            MaximumConcurrentTextureLoads,
            CpuResidentByteBudget,
            ContentCacheByteBudget,
            TextureResidentByteBudget,
            directory,
            PersistContentCache,
            RejectSynchronousLoaderInBrowser,
            ShutdownTimeout);
    }
}

public sealed record AssetStreamingConfiguration3D(
    int MaximumConcurrentLoads,
    int MaximumQueuedRequests,
    int MaximumConcurrentTextureLoads,
    long CpuResidentByteBudget,
    long ContentCacheByteBudget,
    long TextureResidentByteBudget,
    string? PersistentContentCacheDirectory,
    bool PersistContentCache,
    bool RejectSynchronousLoaderInBrowser,
    TimeSpan ShutdownTimeout);
