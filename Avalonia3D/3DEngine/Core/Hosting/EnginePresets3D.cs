using System;

namespace ThreeDEngine.Core.Hosting;

public enum EnginePreset3D
{
    DesktopBalanced = 0,
    DesktopLargeScene = 1,
    BrowserBalanced = 2,
    BrowserMemoryConstrained = 3,
    Diagnostics = 4
}

public static class EnginePresetExtensions3D
{
    /// <summary>
    /// Applies explicit resource/streaming budgets. Presets never select a lower-quality renderer
    /// or enable a CPU fallback; applications may override individual values afterwards.
    /// </summary>
    public static Engine3DBuilder ApplyPreset(this Engine3DBuilder builder, EnginePreset3D preset)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        switch (preset)
        {
            case EnginePreset3D.DesktopBalanced:
                builder.ConfigureResources(options =>
                {
                    options.MaxCpuTextureBytes = 512L * 1024L * 1024L;
                    options.MaxGpuResidentBytes = 1024L * 1024L * 1024L;
                    options.MaxGpuTextureBytes = 768L * 1024L * 1024L;
                });
                builder.ConfigureAssets(options =>
                {
                    options.CpuResidentByteBudget = 1024L * 1024L * 1024L;
                    options.TextureResidentByteBudget = 768L * 1024L * 1024L;
                    options.ContentCacheByteBudget = 512L * 1024L * 1024L;
                });
                break;
            case EnginePreset3D.DesktopLargeScene:
                builder.ConfigureResources(options =>
                {
                    options.MaxCpuTextureBytes = 2L * 1024L * 1024L * 1024L;
                    options.MaxGpuResidentBytes = 4L * 1024L * 1024L * 1024L;
                    options.MaxGpuTextureBytes = 3L * 1024L * 1024L * 1024L;
                });
                builder.ConfigureAssets(options =>
                {
                    options.CpuResidentByteBudget = 4L * 1024L * 1024L * 1024L;
                    options.TextureResidentByteBudget = 3L * 1024L * 1024L * 1024L;
                    options.ContentCacheByteBudget = 1024L * 1024L * 1024L;
                    options.MaximumQueuedRequests = 1024;
                });
                break;
            case EnginePreset3D.BrowserBalanced:
                builder.ConfigureResources(options =>
                {
                    options.MaxCpuTextureBytes = 192L * 1024L * 1024L;
                    options.MaxGpuResidentBytes = 384L * 1024L * 1024L;
                    options.MaxGpuTextureBytes = 256L * 1024L * 1024L;
                });
                builder.ConfigureAssets(options =>
                {
                    options.MaximumConcurrentLoads = 1;
                    options.MaximumConcurrentTextureLoads = 2;
                    options.CpuResidentByteBudget = 256L * 1024L * 1024L;
                    options.TextureResidentByteBudget = 192L * 1024L * 1024L;
                    options.ContentCacheByteBudget = 128L * 1024L * 1024L;
                    options.RejectSynchronousLoaderInBrowser = true;
                });
                break;
            case EnginePreset3D.BrowserMemoryConstrained:
                builder.ConfigureResources(options =>
                {
                    options.MaxCpuTextureBytes = 96L * 1024L * 1024L;
                    options.MaxGpuResidentBytes = 192L * 1024L * 1024L;
                    options.MaxGpuTextureBytes = 128L * 1024L * 1024L;
                });
                builder.ConfigureAssets(options =>
                {
                    options.MaximumConcurrentLoads = 1;
                    options.MaximumConcurrentTextureLoads = 1;
                    options.CpuResidentByteBudget = 128L * 1024L * 1024L;
                    options.TextureResidentByteBudget = 96L * 1024L * 1024L;
                    options.ContentCacheByteBudget = 64L * 1024L * 1024L;
                    options.RejectSynchronousLoaderInBrowser = true;
                });
                break;
            case EnginePreset3D.Diagnostics:
                builder.ConfigureDiagnostics(options =>
                {
                    options.MinimumLogLevel = ThreeDEngine.Core.Diagnostics.EngineLogLevel3D.Trace;
                    options.LogCapacity = 65_536;
                    options.LogFileMaxBytes = 64L * 1024L * 1024L;
                    options.RetainedLogFileCount = 16;
                });
                break;
        }
        return builder;
    }
}
