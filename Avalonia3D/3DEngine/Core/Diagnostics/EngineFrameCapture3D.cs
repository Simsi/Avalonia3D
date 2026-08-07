using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Rendering.Extensions;

namespace ThreeDEngine.Core.Diagnostics;

/// <summary>Portable, versioned frame/engine capture for regression analysis and support.</summary>
public sealed class EngineFrameCapture3D
{
    public const string FormatName = "Avalonia3D.FrameCapture";
    public const int CurrentVersion = 1;

    public string Format { get; init; } = FormatName;
    public int Version { get; init; } = CurrentVersion;
    public DateTimeOffset CreatedUtc { get; init; }
    public string EngineId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public int ActiveSceneCount { get; init; }
    public AssetStreamingStatistics3D Assets { get; init; }
    public TextureStreamingStatistics3D Textures { get; init; }
    public int RenderExtensionCount { get; init; }
    public int RenderExtensionPassCount { get; init; }
    public long RenderExtensionVersion { get; init; }
    public string RenderExtensionBackend { get; init; } = "unavailable";
    public RenderExtensionCompilationResult3D? RenderExtensionCompilation { get; init; }
    public int MaterialExtensionCount { get; init; }
    public long MaterialExtensionVersion { get; init; }
    public GpuPickingStatistics3D GpuPicking { get; init; }
    public EngineProfileSnapshot3D Profile { get; init; } = null!;
    public string RecentLog { get; init; } = string.Empty;

    public static EngineFrameCapture3D Capture(Engine3D engine, int maximumFrames = 600, int maximumLogEntries = 512)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ObjectDisposedException.ThrowIf(engine.IsDisposed, engine);
        if (maximumFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFrames));
        if (maximumLogEntries < 0) throw new ArgumentOutOfRangeException(nameof(maximumLogEntries));
        var extensions = engine.RenderExtensions.CaptureSnapshot();
        return new EngineFrameCapture3D
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            EngineId = engine.Id,
            SessionId = EngineLog3D.SessionId,
            ActiveSceneCount = engine.ActiveSceneCount,
            Assets = engine.Assets.Statistics,
            Textures = engine.Textures.Statistics,
            RenderExtensionCount = extensions.Extensions.Count,
            RenderExtensionPassCount = extensions.PassCount,
            RenderExtensionVersion = extensions.Version,
            RenderExtensionBackend = engine.RenderExtensionRuntime.BackendName,
            RenderExtensionCompilation = engine.RenderExtensionRuntime.LastCompilation,
            MaterialExtensionCount = engine.MaterialExtensions.Count,
            MaterialExtensionVersion = engine.MaterialExtensions.Version,
            GpuPicking = engine.GpuPicking.Statistics,
            Profile = engine.Profiler.Capture(maximumFrames),
            RecentLog = maximumLogEntries == 0 ? string.Empty : EngineLog3D.FormatSnapshot(maximumLogEntries, includeStackTraces: true)
        };
    }

    public string ToJson(bool indented = true)
        => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = indented });

    public async ValueTask WriteAsync(Stream output, bool indented = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await JsonSerializer.SerializeAsync(output, this, new JsonSerializerOptions { WriteIndented = indented }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> SaveAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException("Browser captures must be exported through a user-initiated download stream.");
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Capture directory cannot be empty.", nameof(directory));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"Avalonia3D-{SessionId}-{CreatedUtc:yyyyMMdd-HHmmssfff}.framecapture.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                await WriteAsync(stream, indented: true, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) { EngineLog3D.Warning("FrameCapture", $"Failed to remove temporary capture '{temporary}': {exception.Message}"); }
        }
    }
}
