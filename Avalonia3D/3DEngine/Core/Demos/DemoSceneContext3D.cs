using System;
using ThreeDEngine.Core.Diagnostics;

namespace ThreeDEngine.Core.Demos;

public sealed class DemoSceneContext3D
{
    public Action<string>? Status { get; init; }
    public Action<string>? Warning { get; init; }
    public Action<string>? Diagnostics { get; init; }

    public void ReportStatus(string message) => Status?.Invoke(message);
    public void ReportWarning(string message) => Warning?.Invoke(message);
    public void ReportDiagnostics(string message) => Diagnostics?.Invoke(message);
}
