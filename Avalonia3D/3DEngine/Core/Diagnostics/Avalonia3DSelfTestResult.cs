using System;
using System.Collections.Generic;
using System.Linq;

namespace ThreeDEngine.Core.Diagnostics;

public sealed class Avalonia3DSelfTestResult
{
    internal Avalonia3DSelfTestResult(IReadOnlyList<Avalonia3DSelfTestCaseResult> cases, TimeSpan elapsed)
    {
        Cases = cases;
        Elapsed = elapsed;
    }

    public IReadOnlyList<Avalonia3DSelfTestCaseResult> Cases { get; }
    public TimeSpan Elapsed { get; }
    public int PassedCount => Cases.Count(c => c.Passed);
    public int FailedCount => Cases.Count(c => !c.Passed);
    public bool Passed => FailedCount == 0;

    public string ToReport()
    {
        var lines = new List<string>
        {
            $"Avalonia3D self-tests: {PassedCount} passed, {FailedCount} failed, {Elapsed.TotalMilliseconds:0.##} ms"
        };

        foreach (var test in Cases)
        {
            lines.Add(test.Passed
                ? $"  PASS {test.Name} ({test.Elapsed.TotalMilliseconds:0.##} ms)"
                : $"  FAIL {test.Name}: {test.Error}");
        }

        return string.Join(global::System.Environment.NewLine, lines);
    }
}

public sealed class Avalonia3DSelfTestCaseResult
{
    internal Avalonia3DSelfTestCaseResult(string name, bool passed, TimeSpan elapsed, string? error)
    {
        Name = name;
        Passed = passed;
        Elapsed = elapsed;
        Error = error;
    }

    public string Name { get; }
    public bool Passed { get; }
    public TimeSpan Elapsed { get; }
    public string? Error { get; }
}

public sealed class Avalonia3DSelfTestException : Exception
{
    public Avalonia3DSelfTestException(Avalonia3DSelfTestResult result)
        : base(result.ToReport())
    {
        Result = result;
    }

    public Avalonia3DSelfTestResult Result { get; }
}
