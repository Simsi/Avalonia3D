namespace ThreeDEngine.Avalonia.Preview;

/// <summary>
/// Request passed from the 3DModelEditor UI to the host-specific source exporter.
/// The editor updates only Build(...); event handler code and other class members
/// are intentionally owned by the IDE/user source, not by the visual editor.
/// The core preview control intentionally does not depend on Roslyn packages;
/// PreviewerApp/VSIX supplies a Roslyn-backed handler.
/// </summary>
public sealed class DebuggerSourceExportRequest
{
    public DebuggerSourceExportRequest(
        string filePath,
        string className,
        string? typeFullName,
        int line,
        int classStart,
        bool hasBuildMethod,
        string generatedBuildMethodSource,
        string generatedClassSource,
        string generatedEventMembersSource)
    {
        FilePath = filePath;
        ClassName = className;
        TypeFullName = typeFullName;
        Line = line;
        ClassStart = classStart;
        HasBuildMethod = hasBuildMethod;
        GeneratedBuildMethodSource = generatedBuildMethodSource;
        GeneratedClassSource = generatedClassSource;
        GeneratedEventMembersSource = generatedEventMembersSource;
    }

    public string FilePath { get; }
    public string ClassName { get; }
    public string? TypeFullName { get; }
    public int Line { get; }
    public int ClassStart { get; }
    public bool HasBuildMethod { get; }
    public string GeneratedBuildMethodSource { get; }
    public string GeneratedClassSource { get; }
    public string GeneratedEventMembersSource { get; }
}


/// <summary>
/// Legacy event draft container retained for source compatibility.
/// 3DModelEditor no longer edits handler bodies; existing source subscriptions
/// are detected read-only and preserved when Build(...) is updated.
/// </summary>
public sealed class DebugEventBinding
{
    public DebugEventBinding(string objectId, string eventName, string body)
    {
        ObjectId = objectId;
        EventName = eventName;
        Body = body;
    }

    public string ObjectId { get; }
    public string EventName { get; }
    public string Body { get; }
}

public sealed class DebuggerSourceExportResult
{
    public DebuggerSourceExportResult(bool success, string message, string filePath, string? backupPath = null, string? mode = null)
    {
        Success = success;
        Message = message;
        FilePath = filePath;
        BackupPath = backupPath;
        Mode = mode ?? string.Empty;
    }

    public bool Success { get; }
    public string Message { get; }
    public string FilePath { get; }
    public string? BackupPath { get; }
    public string Mode { get; }

    public static DebuggerSourceExportResult Failed(string message, string filePath = "") => new(false, message, filePath);

    public static DebuggerSourceExportResult Completed(string message, string filePath, string backupPath, string mode) => new(true, message, filePath, backupPath, mode);
}
