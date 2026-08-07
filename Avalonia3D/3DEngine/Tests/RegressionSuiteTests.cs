#if AVALONIA3D_TEST_HOST
using System;
using Xunit;

namespace Avalonia3D.Engine.Tests;

public sealed class RegressionSuiteTests
{
    [Fact(DisplayName = "Avalonia3D complete regression suite")]
    public void CompleteRegressionSuite()
    {
        var exitCode = Program.Main(Array.Empty<string>());
        Assert.Equal(0, exitCode);
    }
}
#endif
