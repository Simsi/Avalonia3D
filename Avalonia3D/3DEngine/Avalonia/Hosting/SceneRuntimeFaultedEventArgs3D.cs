using System;

namespace ThreeDEngine.Avalonia.Hosting;

public sealed class SceneRuntimeFaultedEventArgs3D : EventArgs
{
    public SceneRuntimeFaultedEventArgs3D(
        string controlId,
        string subsystem,
        Exception exception,
        string? diagnosticReportPath,
        string? logFilePath)
    {
        ControlId = controlId;
        Subsystem = subsystem;
        Exception = exception;
        DiagnosticReportPath = diagnosticReportPath;
        LogFilePath = logFilePath;
    }

    public string ControlId { get; }
    public string Subsystem { get; }
    public Exception Exception { get; }
    public string? DiagnosticReportPath { get; }
    public string? LogFilePath { get; }
}
