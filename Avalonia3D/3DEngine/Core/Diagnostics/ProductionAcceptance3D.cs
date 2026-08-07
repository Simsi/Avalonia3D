using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Diagnostics;

public sealed class ProductionAcceptanceProfile3D
{
    public int MinimumFrameCount { get; set; } = 600;
    public double MinimumAverageFramesPerSecond { get; set; } = 55d;
    public double MaximumP95FrameMilliseconds { get; set; } = 20d;
    public double MaximumP99FrameMilliseconds { get; set; } = 35d;
    public double MaximumWorstFrameMilliseconds { get; set; } = 250d;
    public double MaximumAverageBackendMilliseconds { get; set; } = 12d;
    public double MaximumAverageSimulationMilliseconds { get; set; } = 8d;
    public long MaximumAllocatedBytesPerFrame { get; set; } = 64L * 1024L;
    public bool RequireGpuDriven { get; set; }

    internal void Validate()
    {
        if (MinimumFrameCount <= 0) throw new ArgumentOutOfRangeException(nameof(MinimumFrameCount));
        if (!double.IsFinite(MinimumAverageFramesPerSecond) || MinimumAverageFramesPerSecond < 0d) throw new ArgumentOutOfRangeException(nameof(MinimumAverageFramesPerSecond));
        if (!double.IsFinite(MaximumP95FrameMilliseconds) || MaximumP95FrameMilliseconds <= 0d) throw new ArgumentOutOfRangeException(nameof(MaximumP95FrameMilliseconds));
        if (!double.IsFinite(MaximumP99FrameMilliseconds) || MaximumP99FrameMilliseconds <= 0d) throw new ArgumentOutOfRangeException(nameof(MaximumP99FrameMilliseconds));
        if (!double.IsFinite(MaximumWorstFrameMilliseconds) || MaximumWorstFrameMilliseconds <= 0d) throw new ArgumentOutOfRangeException(nameof(MaximumWorstFrameMilliseconds));
        if (!double.IsFinite(MaximumAverageBackendMilliseconds) || MaximumAverageBackendMilliseconds < 0d) throw new ArgumentOutOfRangeException(nameof(MaximumAverageBackendMilliseconds));
        if (!double.IsFinite(MaximumAverageSimulationMilliseconds) || MaximumAverageSimulationMilliseconds < 0d) throw new ArgumentOutOfRangeException(nameof(MaximumAverageSimulationMilliseconds));
        if (MaximumAllocatedBytesPerFrame < 0) throw new ArgumentOutOfRangeException(nameof(MaximumAllocatedBytesPerFrame));
        if (MaximumP95FrameMilliseconds > MaximumP99FrameMilliseconds)
            throw new ArgumentException("The p95 frame budget cannot exceed the p99 frame budget.");
        if (MaximumP99FrameMilliseconds > MaximumWorstFrameMilliseconds)
            throw new ArgumentException("The p99 frame budget cannot exceed the worst-frame budget.");
    }
}

public readonly record struct ProductionAcceptanceFailure3D(string Metric, string Actual, string Required, string Explanation);

public sealed class ProductionAcceptanceResult3D
{
    internal ProductionAcceptanceResult3D(
        DateTimeOffset evaluatedUtc,
        EngineProfileSnapshot3D snapshot,
        IReadOnlyList<ProductionAcceptanceFailure3D> failures)
    {
        EvaluatedUtc = evaluatedUtc;
        Snapshot = snapshot;
        Failures = Array.AsReadOnly(failures is ProductionAcceptanceFailure3D[] array ? array : new List<ProductionAcceptanceFailure3D>(failures).ToArray());
    }

    public DateTimeOffset EvaluatedUtc { get; }
    public EngineProfileSnapshot3D Snapshot { get; }
    public IReadOnlyList<ProductionAcceptanceFailure3D> Failures { get; }
    public bool Passed => Failures.Count == 0;
}

public static class ProductionAcceptance3D
{
    public static ProductionAcceptanceResult3D Evaluate(
        EngineProfileSnapshot3D snapshot,
        ProductionAcceptanceProfile3D? profile = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        profile ??= new ProductionAcceptanceProfile3D();
        profile.Validate();
        var failures = new List<ProductionAcceptanceFailure3D>();
        var frames = snapshot.Frames;
        if (snapshot.InvalidMetricCount != 0)
            failures.Add(new("invalidMetrics", snapshot.InvalidMetricCount.ToString(), "0", "The capture contains NaN, infinity, negative timing, or otherwise invalid telemetry and cannot be accepted."));
        RequireFinite(snapshot.AveragePresentedFramesPerSecond, "averageFps", failures);
        RequireFinite(snapshot.P50FrameMilliseconds, "p50FrameMs", failures);
        RequireFinite(snapshot.P95FrameMilliseconds, "p95FrameMs", failures);
        RequireFinite(snapshot.P99FrameMilliseconds, "p99FrameMs", failures);
        RequireFinite(snapshot.WorstFrameMilliseconds, "worstFrameMs", failures);
        RequireFinite(snapshot.AverageBackendMilliseconds, "averageBackendMs", failures);
        RequireFinite(snapshot.AverageSimulationMilliseconds, "averageSimulationMs", failures);
        if (frames.Count < profile.MinimumFrameCount)
            failures.Add(new("frameCount", frames.Count.ToString(), $">={profile.MinimumFrameCount}", "The sample is too short for a production performance decision."));
        if (snapshot.AveragePresentedFramesPerSecond < profile.MinimumAverageFramesPerSecond)
            failures.Add(new("averageFps", snapshot.AveragePresentedFramesPerSecond.ToString("0.###"), $">={profile.MinimumAverageFramesPerSecond:0.###}", "Presented cadence is below the configured product target."));
        if (snapshot.P95FrameMilliseconds > profile.MaximumP95FrameMilliseconds)
            failures.Add(new("p95FrameMs", snapshot.P95FrameMilliseconds.ToString("0.###"), $"<={profile.MaximumP95FrameMilliseconds:0.###}", "Sustained frame pacing exceeds the configured budget."));
        if (snapshot.P99FrameMilliseconds > profile.MaximumP99FrameMilliseconds)
            failures.Add(new("p99FrameMs", snapshot.P99FrameMilliseconds.ToString("0.###"), $"<={profile.MaximumP99FrameMilliseconds:0.###}", "Tail frame latency exceeds the configured budget."));
        if (snapshot.WorstFrameMilliseconds > profile.MaximumWorstFrameMilliseconds)
            failures.Add(new("worstFrameMs", snapshot.WorstFrameMilliseconds.ToString("0.###"), $"<={profile.MaximumWorstFrameMilliseconds:0.###}", "A severe frame stall occurred in the captured window."));
        if (snapshot.AverageBackendMilliseconds > profile.MaximumAverageBackendMilliseconds)
            failures.Add(new("averageBackendMs", snapshot.AverageBackendMilliseconds.ToString("0.###"), $"<={profile.MaximumAverageBackendMilliseconds:0.###}", "Backend execution exceeds the CPU-side render budget."));
        if (snapshot.AverageSimulationMilliseconds > profile.MaximumAverageSimulationMilliseconds)
            failures.Add(new("averageSimulationMs", snapshot.AverageSimulationMilliseconds.ToString("0.###"), $"<={profile.MaximumAverageSimulationMilliseconds:0.###}", "Fixed-update work exceeds the simulation budget."));

        if (frames.Count != 0)
        {
            var averageAllocated = (double)snapshot.TotalAllocatedBytes / frames.Count;
            if (averageAllocated > profile.MaximumAllocatedBytesPerFrame)
                failures.Add(new("allocatedBytesPerFrame", averageAllocated.ToString("0.###"), $"<={profile.MaximumAllocatedBytesPerFrame}", "Managed allocation rate exceeds the production budget."));
            if (profile.RequireGpuDriven)
            {
                for (var i = 0; i < frames.Count; i++)
                {
                    if (frames[i].GpuDriven) continue;
                    failures.Add(new("gpuDriven", "false", "true for every captured frame", $"Frame {frames[i].Sequence} used a non-GPU-driven path."));
                    break;
                }
            }
        }

        return new ProductionAcceptanceResult3D(DateTimeOffset.UtcNow, snapshot, failures);
    }

    private static void RequireFinite(double value, string metric, ICollection<ProductionAcceptanceFailure3D> failures)
    {
        if (double.IsFinite(value) && value >= 0d) return;
        failures.Add(new(metric, value.ToString(), "finite and non-negative", "A non-finite performance metric cannot satisfy a production gate."));
    }
}
