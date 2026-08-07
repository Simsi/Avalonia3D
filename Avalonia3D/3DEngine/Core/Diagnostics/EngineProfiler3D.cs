using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Core.Diagnostics;

/// <summary>
/// Engine-scoped bounded flight recorder for presented-frame telemetry. Recording performs no
/// allocations after the ring has been created; snapshots allocate only on explicit user request.
/// </summary>
public sealed class EngineProfiler3D
{
    private readonly object _gate = new();
    private FrameProfile3D[] _frames;
    private int _next;
    private int _count;
    private long _sequence;

    internal EngineProfiler3D(int capacity = 2048)
    {
        if (capacity < 64 || capacity > 65_536) throw new ArgumentOutOfRangeException(nameof(capacity));
        _frames = new FrameProfile3D[capacity];
    }

    public int Capacity { get { lock (_gate) return _frames.Length; } }
    public int Count { get { lock (_gate) return _count; } }
    public long LastSequence { get { lock (_gate) return _sequence; } }

    public void Resize(int capacity)
    {
        if (capacity < 64 || capacity > 65_536) throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (_gate)
        {
            if (capacity == _frames.Length) return;
            var replacement = new FrameProfile3D[capacity];
            var copyCount = global::System.Math.Min(_count, capacity);
            var first = (_next - copyCount + _frames.Length) % _frames.Length;
            for (var i = 0; i < copyCount; i++) replacement[i] = _frames[(first + i) % _frames.Length];
            _frames = replacement;
            _count = copyCount;
            _next = copyCount % capacity;
        }
    }

    public EngineProfileSnapshot3D Capture(int maximumFrames = 600)
    {
        if (maximumFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFrames));
        lock (_gate)
        {
            var count = global::System.Math.Min(_count, maximumFrames);
            var frames = new FrameProfile3D[count];
            var first = (_next - count + _frames.Length) % _frames.Length;
            for (var i = 0; i < count; i++) frames[i] = _frames[(first + i) % _frames.Length];
            return EngineProfileSnapshot3D.Create(frames, _sequence);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_frames);
            _next = 0;
            _count = 0;
        }
    }

    internal void RecordFrame(string sourceId, BackendKind backend, RenderStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        sourceId = string.IsNullOrWhiteSpace(sourceId) ? "unknown" : sourceId.Trim();
        if (!Enum.IsDefined(backend)) throw new ArgumentOutOfRangeException(nameof(backend));
        lock (_gate)
        {
            var sequence = _sequence = checked(_sequence + 1);
            _frames[_next] = FrameProfile3D.From(sequence, sourceId, backend, stats);
            _next = (_next + 1) % _frames.Length;
            if (_count < _frames.Length) _count++;
        }
    }
}

public readonly record struct FrameProfile3D(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string SourceId,
    BackendKind Backend,
    double PresentedFramesPerSecond,
    double FrameIntervalMilliseconds,
    double BackendMilliseconds,
    double CpuPreparationMilliseconds,
    double UploadMilliseconds,
    bool GpuTimingAvailable,
    double GpuMilliseconds,
    double SimulationMilliseconds,
    double PhysicsMilliseconds,
    double PresentationJitterMilliseconds,
    int DrawCalls,
    int Triangles,
    int VisibleMeshes,
    int Objects,
    int Particles,
    long UploadBytes,
    long AllocatedBytes,
    long ManagedHeapBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    bool GpuDriven,
    int RenderPasses,
    int ComputePasses,
    int BarrierCount)
{
    internal static FrameProfile3D From(long sequence, string sourceId, BackendKind backend, RenderStats stats)
        => new(
            sequence,
            DateTimeOffset.UtcNow,
            sourceId,
            backend,
            stats.PresentedFramesPerSecond,
            stats.FrameTotalMilliseconds,
            stats.BackendMilliseconds,
            stats.CpuPreparationMilliseconds,
            stats.UploadMilliseconds,
            stats.GpuTimingAvailable,
            stats.GpuTimingAvailable ? stats.GpuFrameMilliseconds : 0d,
            stats.SimulationTotalMilliseconds,
            stats.PhysicsMilliseconds,
            stats.PresentationJitterMilliseconds,
            stats.DrawCallCount,
            stats.TriangleCount,
            stats.VisibleMeshCount,
            stats.ObjectCount,
            stats.ParticleCount,
            ComputeUploadBytes(stats.VertexBufferUploadBytes, stats.IndexBufferUploadBytes, stats.TextureUploadBytes, stats.InstanceUploadBytes),
            stats.AllocatedBytesPerFrame,
            stats.ManagedHeapBytes,
            stats.Gen0Collections,
            stats.Gen1Collections,
            stats.Gen2Collections,
            stats.GpuDrivenActive,
            stats.GpuDrivenRenderPassCount,
            stats.GpuDrivenComputePassCount,
            stats.GpuDrivenBarrierCount);

    private static long ComputeUploadBytes(long first, long second, long third, long fourth)
    {
        if (first < 0 || second < 0 || third < 0 || fourth < 0) return -1L;
        var total = AddSaturating(0L, first);
        total = AddSaturating(total, second);
        total = AddSaturating(total, third);
        return AddSaturating(total, fourth);
    }

    private static long AddSaturating(long total, long value)
    {
        value = global::System.Math.Max(0L, value);
        return long.MaxValue - total < value ? long.MaxValue : total + value;
    }
}

public sealed class EngineProfileSnapshot3D
{
    private EngineProfileSnapshot3D(
        FrameProfile3D[] frames,
        long lastSequence,
        double averageFps,
        double p50FrameMilliseconds,
        double p95FrameMilliseconds,
        double p99FrameMilliseconds,
        double worstFrameMilliseconds,
        double averageBackendMilliseconds,
        double averageSimulationMilliseconds,
        long totalAllocatedBytes,
        int invalidMetricCount)
    {
        Frames = Array.AsReadOnly(frames);
        LastSequence = lastSequence;
        AveragePresentedFramesPerSecond = averageFps;
        P50FrameMilliseconds = p50FrameMilliseconds;
        P95FrameMilliseconds = p95FrameMilliseconds;
        P99FrameMilliseconds = p99FrameMilliseconds;
        WorstFrameMilliseconds = worstFrameMilliseconds;
        AverageBackendMilliseconds = averageBackendMilliseconds;
        AverageSimulationMilliseconds = averageSimulationMilliseconds;
        TotalAllocatedBytes = totalAllocatedBytes;
        InvalidMetricCount = invalidMetricCount;
    }

    public IReadOnlyList<FrameProfile3D> Frames { get; }
    public long LastSequence { get; }
    public double AveragePresentedFramesPerSecond { get; }
    public double P50FrameMilliseconds { get; }
    public double P95FrameMilliseconds { get; }
    public double P99FrameMilliseconds { get; }
    public double WorstFrameMilliseconds { get; }
    public double AverageBackendMilliseconds { get; }
    public double AverageSimulationMilliseconds { get; }
    public long TotalAllocatedBytes { get; }
    public int InvalidMetricCount { get; }

    internal static EngineProfileSnapshot3D Create(FrameProfile3D[] frames, long lastSequence)
    {
        if (frames.Length == 0)
            return new EngineProfileSnapshot3D(frames, lastSequence, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0L, 0);

        var intervals = new double[frames.Length];
        var intervalCount = 0;
        var averageInterval = 0d;
        var averageBackend = 0d;
        var backendCount = 0;
        var averageSimulation = 0d;
        var simulationCount = 0;
        var invalidMetrics = 0;
        var allocated = 0L;

        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            invalidMetrics = checked(invalidMetrics + CountInvalidMetrics(frame));

            if (IsFiniteNonNegative(frame.FrameIntervalMilliseconds) && frame.FrameIntervalMilliseconds > 0d)
            {
                intervals[intervalCount] = frame.FrameIntervalMilliseconds;
                intervalCount++;
                averageInterval += (frame.FrameIntervalMilliseconds - averageInterval) / intervalCount;
            }

            if (IsFiniteNonNegative(frame.BackendMilliseconds))
            {
                backendCount++;
                averageBackend += (frame.BackendMilliseconds - averageBackend) / backendCount;
            }

            if (IsFiniteNonNegative(frame.SimulationMilliseconds))
            {
                simulationCount++;
                averageSimulation += (frame.SimulationMilliseconds - averageSimulation) / simulationCount;
            }

            allocated = AddSaturating(allocated, frame.AllocatedBytes);
        }

        if (intervalCount != intervals.Length) Array.Resize(ref intervals, intervalCount);
        Array.Sort(intervals);
        var averageFps = averageInterval > 0d && double.IsFinite(averageInterval) ? 1000d / averageInterval : 0d;

        return new EngineProfileSnapshot3D(
            frames,
            lastSequence,
            averageFps,
            Percentile(intervals, 0.50),
            Percentile(intervals, 0.95),
            Percentile(intervals, 0.99),
            intervalCount == 0 ? 0d : intervals[^1],
            backendCount == 0 ? 0d : averageBackend,
            simulationCount == 0 ? 0d : averageSimulation,
            allocated,
            invalidMetrics);
    }

    private static int CountInvalidMetrics(in FrameProfile3D frame)
    {
        var invalid = 0;
        if (!IsFiniteNonNegative(frame.PresentedFramesPerSecond)) invalid++;
        if (!IsFiniteNonNegative(frame.FrameIntervalMilliseconds) || frame.FrameIntervalMilliseconds <= 0d) invalid++;
        if (!IsFiniteNonNegative(frame.BackendMilliseconds)) invalid++;
        if (!IsFiniteNonNegative(frame.CpuPreparationMilliseconds)) invalid++;
        if (!IsFiniteNonNegative(frame.UploadMilliseconds)) invalid++;
        if (frame.GpuTimingAvailable)
        {
            if (!IsFiniteNonNegative(frame.GpuMilliseconds)) invalid++;
        }
        else if (frame.GpuMilliseconds != 0d)
        {
            invalid++;
        }
        if (!IsFiniteNonNegative(frame.SimulationMilliseconds)) invalid++;
        if (!IsFiniteNonNegative(frame.PhysicsMilliseconds)) invalid++;
        if (!IsFiniteNonNegative(frame.PresentationJitterMilliseconds)) invalid++;
        if (frame.DrawCalls < 0) invalid++;
        if (frame.Triangles < 0) invalid++;
        if (frame.VisibleMeshes < 0) invalid++;
        if (frame.Objects < 0) invalid++;
        if (frame.Particles < 0) invalid++;
        if (frame.UploadBytes < 0) invalid++;
        if (frame.AllocatedBytes < 0) invalid++;
        if (frame.ManagedHeapBytes < 0) invalid++;
        if (frame.Gen0Collections < 0) invalid++;
        if (frame.Gen1Collections < 0) invalid++;
        if (frame.Gen2Collections < 0) invalid++;
        if (frame.RenderPasses < 0) invalid++;
        if (frame.ComputePasses < 0) invalid++;
        if (frame.BarrierCount < 0) invalid++;
        return invalid;
    }

    private static long AddSaturating(long total, long value)
    {
        value = global::System.Math.Max(0L, value);
        return long.MaxValue - total < value ? long.MaxValue : total + value;
    }

    private static bool IsFiniteNonNegative(double value) => double.IsFinite(value) && value >= 0d;

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0d;
        var position = percentile * (sorted.Length - 1);
        var lower = (int)global::System.Math.Floor(position);
        var upper = (int)global::System.Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        var fraction = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }
}
