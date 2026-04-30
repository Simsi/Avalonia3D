namespace ThreeDEngine.Avalonia.Preview;

/// <summary>
/// Request passed from the debugger UI to the host-specific source exporter.
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
        string generatedEventMembersSource,
        bool previewOnly = false)
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
        PreviewOnly = previewOnly;
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
    public bool PreviewOnly { get; }
}


/// <summary>
/// Draft event handler body assigned to a debugger object.
/// It is intentionally data-only so the preview core does not depend on Roslyn
/// or runtime script compilation. The PreviewerApp/VSIX exporter turns these
/// drafts into source methods and subscriptions.
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
    public DebuggerSourceExportResult(bool success, string message, string filePath, string? backupPath = null, string? mode = null, string? diffPreview = null)
    {
        Success = success;
        Message = message;
        FilePath = filePath;
        BackupPath = backupPath;
        Mode = mode ?? string.Empty;
        DiffPreview = diffPreview ?? string.Empty;
    }

    public bool Success { get; }
    public string Message { get; }
    public string FilePath { get; }
    public string? BackupPath { get; }
    public string Mode { get; }
    public string DiffPreview { get; }

    public static DebuggerSourceExportResult Failed(string message, string filePath = "") => new(false, message, filePath);

    public static DebuggerSourceExportResult Completed(string message, string filePath, string backupPath, string mode, string? diffPreview = null) => new(true, message, filePath, backupPath, mode, diffPreview);

    public static DebuggerSourceExportResult Preview(string message, string filePath, string mode, string diffPreview) => new(true, message, filePath, null, mode, diffPreview);
}
