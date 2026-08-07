using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ThreeDEngine.Core.Diagnostics;

public enum EngineLogLevel3D
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

public readonly record struct EngineLogEntry3D(
    long Sequence,
    DateTimeOffset TimestampUtc,
    long SessionElapsedMilliseconds,
    int ProcessId,
    int ManagedThreadId,
    string? ThreadName,
    EngineLogLevel3D Level,
    string Category,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionStackTrace)
{
    public string ToLogLine(bool includeStackTrace = false)
    {
        var builder = new StringBuilder(320);
        builder.Append(TimestampUtc.ToString("O"));
        builder.Append(" +").Append(SessionElapsedMilliseconds).Append("ms");
        builder.Append(" #").Append(Sequence);
        builder.Append(" [").Append(Level).Append(']');
        builder.Append(" [P").Append(ProcessId).Append(":T").Append(ManagedThreadId);
        if (!string.IsNullOrWhiteSpace(ThreadName)) builder.Append(':').Append(ThreadName);
        builder.Append("] ");
        builder.Append(Category).Append(": ").Append(Message);
        if (!string.IsNullOrWhiteSpace(ExceptionType))
        {
            builder.Append(" | ").Append(ExceptionType);
            if (!string.IsNullOrWhiteSpace(ExceptionMessage)) builder.Append(": ").Append(ExceptionMessage);
        }

        if (includeStackTrace && !string.IsNullOrWhiteSpace(ExceptionStackTrace))
        {
            builder.AppendLine();
            builder.Append(ExceptionStackTrace);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Process-wide diagnostic stream with an in-memory flight recorder and an automatically
/// flushed rotating desktop file. The implementation deliberately has no external logging
/// dependency, so browser failures can still be exported as plain text.
/// </summary>
public static class EngineLog3D
{
    private const int DefaultCapacity = 16384;
    private const long DefaultFileMaxBytes = 16L * 1024L * 1024L;
    private const int DefaultRetainedFileCount = 8;
    private static readonly object Gate = new();
    private static readonly Queue<EngineLogEntry3D> Entries = new(DefaultCapacity);
    private static readonly long SessionStartTicks = Stopwatch.GetTimestamp();
    private static int _capacity = DefaultCapacity;
    private static long _sequence;
    private static StreamWriter? _fileWriter;
    private static string? _currentLogFilePath;
    private static string? _logDirectory;
    private static long _fileMaxBytes = DefaultFileMaxBytes;
    private static int _retainedFileCount = DefaultRetainedFileCount;
    private static bool _fileSinkFailureReported;
    private static bool _processHooksInstalled;

    static EngineLog3D()
    {
        SessionId = Guid.NewGuid().ToString("N");
        MinimumLevel = ReadLogLevel("AVALONIA3D_LOG_LEVEL", EngineLogLevel3D.Debug);
        WriteToConsole = !ReadBoolean("AVALONIA3D_LOG_NO_CONSOLE");
        WriteToFile = !OperatingSystem.IsBrowser() && !ReadBoolean("AVALONIA3D_LOG_NO_FILE");
        Capacity = ReadInteger("AVALONIA3D_LOG_CAPACITY", DefaultCapacity, 256, 65536);
        _fileMaxBytes = ReadLong("AVALONIA3D_LOG_FILE_MAX_BYTES", DefaultFileMaxBytes, 1024L * 1024L, 1024L * 1024L * 1024L);
        _retainedFileCount = ReadInteger("AVALONIA3D_LOG_RETAINED_FILES", DefaultRetainedFileCount, 1, 64);
        _logDirectory = ResolveLogDirectory(global::System.Environment.GetEnvironmentVariable("AVALONIA3D_LOG_DIRECTORY"));
        var explicitFile = global::System.Environment.GetEnvironmentVariable("AVALONIA3D_LOG_FILE");
        InitializeFileSink(explicitFile);
        InstallProcessHooks();
        Information("Runtime", BuildStartupMessage());
    }

    public static event Action<EngineLogEntry3D>? EntryWritten;

    public static string SessionId { get; }
    public static EngineLogLevel3D MinimumLevel { get; set; } = EngineLogLevel3D.Debug;
    public static bool WriteToConsole { get; set; } = true;
    public static bool WriteToFile { get; private set; }
    public static string? CurrentLogFilePath { get { lock (Gate) return _currentLogFilePath; } }
    public static string? LogDirectory { get { lock (Gate) return _logDirectory; } }

    public static int Capacity
    {
        get { lock (Gate) return _capacity; }
        set
        {
            var clamped = global::System.Math.Clamp(value, 256, 65536);
            lock (Gate)
            {
                _capacity = clamped;
                while (Entries.Count > _capacity) Entries.Dequeue();
            }
        }
    }

    public static void Configure(
        EngineLogLevel3D minimumLevel,
        bool writeToConsole,
        int capacity,
        bool writeToFile,
        string? logDirectory,
        long fileMaxBytes,
        int retainedFileCount)
    {
        MinimumLevel = minimumLevel;
        WriteToConsole = writeToConsole;
        Capacity = capacity;
        if (OperatingSystem.IsBrowser()) writeToFile = false;

        lock (Gate)
        {
            _fileMaxBytes = global::System.Math.Clamp(fileMaxBytes, 1024L * 1024L, 1024L * 1024L * 1024L);
            _retainedFileCount = global::System.Math.Clamp(retainedFileCount, 1, 64);
            var resolvedDirectory = ResolveLogDirectory(logDirectory);
            var sinkChanged = WriteToFile != writeToFile || !string.Equals(_logDirectory, resolvedDirectory, StringComparison.OrdinalIgnoreCase);
            WriteToFile = writeToFile;
            _logDirectory = resolvedDirectory;
            if (sinkChanged)
            {
                CloseFileSinkNoThrow();
                _currentLogFilePath = null;
                _fileSinkFailureReported = false;
                InitializeFileSinkLocked(explicitPath: null);
            }
        }

        Information("Diagnostics", $"Logging configured: level={MinimumLevel}; memoryCapacity={Capacity}; console={WriteToConsole}; file={WriteToFile}; path={CurrentLogFilePath ?? "unavailable"}; maxBytes={_fileMaxBytes}; retainedFiles={_retainedFileCount}.");
    }

    public static void Trace(string category, string message) => Write(EngineLogLevel3D.Trace, category, message);
    public static void Debug(string category, string message) => Write(EngineLogLevel3D.Debug, category, message);
    public static void Information(string category, string message) => Write(EngineLogLevel3D.Information, category, message);
    public static void Info(string category, string message) => Information(category, message);
    public static void Warning(string category, string message, Exception? exception = null) => Write(EngineLogLevel3D.Warning, category, message, exception);
    public static void Error(string category, string message, Exception? exception = null) => Write(EngineLogLevel3D.Error, category, message, exception);
    public static void Critical(string category, string message, Exception? exception = null) => Write(EngineLogLevel3D.Critical, category, message, exception);

    public static void Write(EngineLogLevel3D level, string category, string message, Exception? exception = null)
    {
        if (level < MinimumLevel) return;

        category = string.IsNullOrWhiteSpace(category) ? "Engine" : category.Trim();
        message = string.IsNullOrWhiteSpace(message) ? "(no message)" : message.Trim();
        var elapsedMilliseconds = (long)(Stopwatch.GetElapsedTime(SessionStartTicks).TotalMilliseconds);
        var entry = new EngineLogEntry3D(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            elapsedMilliseconds,
            global::System.Environment.ProcessId,
            global::System.Environment.CurrentManagedThreadId,
            Thread.CurrentThread.Name,
            level,
            category,
            message,
            exception?.GetType().FullName,
            exception?.Message,
            exception?.ToString());

        var line = entry.ToLogLine(includeStackTrace: exception is not null);
        lock (Gate)
        {
            Entries.Enqueue(entry);
            while (Entries.Count > _capacity) Entries.Dequeue();
            WriteFileLineLocked(line);
        }

        if (WriteToConsole)
        {
            try
            {
                if (level >= EngineLogLevel3D.Warning) Console.Error.WriteLine(line);
                else Console.WriteLine(line);
            }
            catch
            {
                // Console availability is host-specific; the in-memory recorder remains active.
            }
            Debugger.Log(0, "Avalonia3D", line + global::System.Environment.NewLine);
        }

        try
        {
            EntryWritten?.Invoke(entry);
        }
        catch (Exception sinkException)
        {
            Debugger.Log(0, "Avalonia3D", "Diagnostic event sink failed: " + sinkException + global::System.Environment.NewLine);
        }
    }

    public static void WriteDiagnosticBlock(string category, string title, string text, EngineLogLevel3D level = EngineLogLevel3D.Information)
    {
        var normalized = string.IsNullOrWhiteSpace(text) ? "(empty)" : text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        Write(level, category, $"BEGIN {title}\n{normalized}\nEND {title}");
    }

    public static IReadOnlyList<EngineLogEntry3D> Snapshot(int maximumEntries = int.MaxValue)
    {
        maximumEntries = global::System.Math.Max(0, maximumEntries);
        lock (Gate)
        {
            var source = Entries.ToArray();
            if (source.Length <= maximumEntries) return source;
            var result = new EngineLogEntry3D[maximumEntries];
            Array.Copy(source, source.Length - maximumEntries, result, 0, maximumEntries);
            return result;
        }
    }

    public static string FormatSnapshot(int maximumEntries = 4096, bool includeStackTraces = true)
    {
        var entries = Snapshot(maximumEntries);
        var builder = new StringBuilder(global::System.Math.Max(256, entries.Count * 160));
        foreach (var entry in entries) builder.AppendLine(entry.ToLogLine(includeStackTraces));
        return builder.ToString().TrimEnd();
    }

    public static void Flush()
    {
        lock (Gate)
        {
            try { _fileWriter?.Flush(); }
            catch (Exception exception) { ReportFileSinkFailureNoThrow(exception); }
        }
    }

    public static void Clear()
    {
        lock (Gate) Entries.Clear();
    }

    private static string BuildStartupMessage()
    {
        var processId = global::System.Environment.ProcessId;
        return $"Diagnostics session {SessionId} started; process={processId}; runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}; architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; browser={OperatingSystem.IsBrowser()}; logFile={CurrentLogFilePath ?? "memory-only"}.";
    }

    private static void InstallProcessHooks()
    {
        lock (Gate)
        {
            if (_processHooksInstalled) return;
            _processHooksInstalled = true;
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                Critical("Runtime.UnhandledException", $"Unhandled AppDomain exception; terminating={args.IsTerminating}.", exception);
                Flush();
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Error("Runtime.UnobservedTask", "An unobserved task exception reached the finalizer thread.", args.Exception);
                Flush();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        }
        catch (Exception exception)
        {
            Debugger.Log(0, "Avalonia3D", "Unable to install process diagnostic hooks: " + exception + global::System.Environment.NewLine);
        }
    }

    private static void InitializeFileSink(string? explicitPath)
    {
        lock (Gate) InitializeFileSinkLocked(explicitPath);
    }

    private static void InitializeFileSinkLocked(string? explicitPath)
    {
        if (!WriteToFile || OperatingSystem.IsBrowser() || _fileWriter is not null) return;
        try
        {
            var path = string.IsNullOrWhiteSpace(explicitPath)
                ? Path.Combine(_logDirectory ?? ResolveLogDirectory(null), $"Avalonia3D-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{SessionId}.log")
                : Path.GetFullPath(global::System.Environment.ExpandEnvironmentVariables(explicitPath));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            PruneOldLogFiles(directory ?? _logDirectory ?? string.Empty, Path.GetFileName(path));
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            _fileWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 64 * 1024) { AutoFlush = true };
            _currentLogFilePath = path;
        }
        catch (Exception exception)
        {
            _fileWriter = null;
            _currentLogFilePath = null;
            WriteToFile = false;
            ReportFileSinkFailureNoThrow(exception);
        }
    }

    private static void WriteFileLineLocked(string line)
    {
        if (!WriteToFile || OperatingSystem.IsBrowser()) return;
        if (_fileWriter is null) InitializeFileSinkLocked(explicitPath: null);
        if (_fileWriter is null) return;

        try
        {
            if (_fileWriter.BaseStream.Length + Encoding.UTF8.GetByteCount(line) + 2 > _fileMaxBytes)
            {
                RotateFileLocked();
            }
            _fileWriter.WriteLine(line);
            if (line.Contains('\n')) _fileWriter.WriteLine("--- end multiline entry ---");
        }
        catch (Exception exception)
        {
            ReportFileSinkFailureNoThrow(exception);
            CloseFileSinkNoThrow();
            WriteToFile = false;
        }
    }

    private static void RotateFileLocked()
    {
        var previous = _currentLogFilePath;
        CloseFileSinkNoThrow();
        if (!string.IsNullOrWhiteSpace(previous))
        {
            try
            {
                var archived = Path.Combine(Path.GetDirectoryName(previous) ?? string.Empty,
                    $"{Path.GetFileNameWithoutExtension(previous)}-{DateTime.UtcNow:HHmmssfff}{Path.GetExtension(previous)}");
                File.Move(previous, archived, overwrite: true);
            }
            catch (Exception exception)
            {
                ReportFileSinkFailureNoThrow(exception);
            }
        }
        _currentLogFilePath = null;
        InitializeFileSinkLocked(explicitPath: null);
    }

    private static void PruneOldLogFiles(string directory, string currentFileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("Avalonia3D-*.log");
            Array.Sort(files, static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            var retained = 0;
            foreach (var file in files)
            {
                if (string.Equals(file.Name, currentFileName, StringComparison.OrdinalIgnoreCase)) continue;
                retained++;
                if (retained > _retainedFileCount) file.Delete();
            }
        }
        catch (Exception exception)
        {
            ReportFileSinkFailureNoThrow(exception);
        }
    }

    private static void CloseFileSinkNoThrow()
    {
        try { _fileWriter?.Flush(); } catch { }
        try { _fileWriter?.Dispose(); } catch { }
        _fileWriter = null;
    }

    private static void ReportFileSinkFailureNoThrow(Exception exception)
    {
        if (_fileSinkFailureReported) return;
        _fileSinkFailureReported = true;
        Debugger.Log(0, "Avalonia3D", "File diagnostic sink failed: " + exception + global::System.Environment.NewLine);
        try { Console.Error.WriteLine("Avalonia3D file diagnostic sink failed: " + exception); } catch { }
    }

    private static string ResolveLogDirectory(string? configured)
    {
        if (OperatingSystem.IsBrowser()) return string.Empty;
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(global::System.Environment.ExpandEnvironmentVariables(configured));

        var root = global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
        return Path.Combine(root, "Avalonia3D", "Logs");
    }

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
