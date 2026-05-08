using System;

namespace ThreeDEngine.Core.Diagnostics;

public static class Avalonia3DGlobalOptions
{
    static Avalonia3DGlobalOptions()
    {
        RunSelfTestsOnStartup = ReadBoolean("AVALONIA3D_RUN_SELF_TESTS") || AppContext.TryGetSwitch("Avalonia3D.RunSelfTestsOnStartup", out var switchEnabled) && switchEnabled;
        ThrowOnSelfTestFailure = !ReadBoolean("AVALONIA3D_SELF_TESTS_NO_THROW");
        WriteSelfTestReportToConsole = true;
    }

    public static bool RunSelfTestsOnStartup { get; set; }
    public static bool ThrowOnSelfTestFailure { get; set; }
    public static bool WriteSelfTestReportToConsole { get; set; }

    private static bool ReadBoolean(string variable)
    {
        var value = global::System.Environment.GetEnvironmentVariable(variable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
