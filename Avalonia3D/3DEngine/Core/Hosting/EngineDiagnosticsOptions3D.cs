using System;
using ThreeDEngine.Core.Diagnostics;

namespace ThreeDEngine.Core.Hosting;

public sealed class EngineDiagnosticsOptions3D
{
    public EngineLogLevel3D MinimumLogLevel { get; set; } = EngineLogLevel3D.Debug;
    public bool WriteLogToConsole { get; set; } = true;
    public int LogCapacity { get; set; } = 16384;
    public bool WriteLogToFile { get; set; } = !OperatingSystem.IsBrowser();
    public string? LogDirectory { get; set; }
    public long LogFileMaxBytes { get; set; } = 16L * 1024L * 1024L;
    public int RetainedLogFileCount { get; set; } = 8;

    public static EngineDiagnosticsOptions3D FromEnvironment()
        => new()
        {
            MinimumLogLevel = ReadLogLevel("AVALONIA3D_LOG_LEVEL", EngineLogLevel3D.Debug),
            WriteLogToConsole = !ReadBoolean("AVALONIA3D_LOG_NO_CONSOLE"),
            LogCapacity = ReadInteger("AVALONIA3D_LOG_CAPACITY", 16384, 256, 65536),
            WriteLogToFile = !OperatingSystem.IsBrowser() && !ReadBoolean("AVALONIA3D_LOG_NO_FILE"),
            LogDirectory = global::System.Environment.GetEnvironmentVariable("AVALONIA3D_LOG_DIRECTORY"),
            LogFileMaxBytes = ReadLong("AVALONIA3D_LOG_FILE_MAX_BYTES", 16L * 1024L * 1024L, 1024L * 1024L, 1024L * 1024L * 1024L),
            RetainedLogFileCount = ReadInteger("AVALONIA3D_LOG_RETAINED_FILES", 8, 1, 64)
        };

    internal EngineDiagnosticsConfiguration3D Freeze()
        => new(
            MinimumLogLevel,
            WriteLogToConsole,
            global::System.Math.Clamp(LogCapacity, 256, 65536),
            WriteLogToFile && !OperatingSystem.IsBrowser(),
            LogDirectory,
            global::System.Math.Clamp(LogFileMaxBytes, 1024L * 1024L, 1024L * 1024L * 1024L),
            global::System.Math.Clamp(RetainedLogFileCount, 1, 64));

    private static bool ReadBoolean(string variable)
    {
        var value = global::System.Environment.GetEnvironmentVariable(variable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInteger(string variable, int fallback, int minimum, int maximum)
    {
        var value = global::System.Environment.GetEnvironmentVariable(variable);
        return int.TryParse(value, out var parsed) ? global::System.Math.Clamp(parsed, minimum, maximum) : fallback;
    }

    private static long ReadLong(string variable, long fallback, long minimum, long maximum)
    {
        var value = global::System.Environment.GetEnvironmentVariable(variable);
        return long.TryParse(value, out var parsed) ? global::System.Math.Clamp(parsed, minimum, maximum) : fallback;
    }

    private static EngineLogLevel3D ReadLogLevel(string variable, EngineLogLevel3D fallback)
    {
        var value = global::System.Environment.GetEnvironmentVariable(variable);
        return Enum.TryParse<EngineLogLevel3D>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }
}

public sealed record EngineDiagnosticsConfiguration3D(
    EngineLogLevel3D MinimumLogLevel,
    bool WriteLogToConsole,
    int LogCapacity,
    bool WriteLogToFile,
    string? LogDirectory,
    long LogFileMaxBytes,
    int RetainedLogFileCount);
