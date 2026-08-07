using System.Diagnostics;

namespace ThreeDEngine.Core.Scene;

/// <summary>Monotonic host clock used by simulation and render schedulers.</summary>
public interface IEngineClock3D
{
    long Timestamp { get; }
    double GetElapsedSeconds(long earlierTimestamp, long laterTimestamp);
}

public sealed class StopwatchEngineClock3D : IEngineClock3D
{
    public static StopwatchEngineClock3D Shared { get; } = new();
    private StopwatchEngineClock3D() { }
    public long Timestamp => Stopwatch.GetTimestamp();
    public double GetElapsedSeconds(long earlierTimestamp, long laterTimestamp)
        => (laterTimestamp - earlierTimestamp) / (double)Stopwatch.Frequency;
}
