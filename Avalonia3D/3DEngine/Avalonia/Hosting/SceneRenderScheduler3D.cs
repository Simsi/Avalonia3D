using System;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Hosting;

/// <summary>
/// Owns the monotonic presentation clock used to translate host callbacks into simulation deltas.
/// Keeping this state outside Scene3DControl prevents timer, presented-frame and unlocked-frame
/// paths from maintaining independent clocks.
/// </summary>
internal sealed class SceneRenderScheduler3D
{
    private readonly IEngineClock3D _clock;
    private long _lastTimestamp;

    public SceneRenderScheduler3D(IEngineClock3D clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool IsStarted => _lastTimestamp != 0;

    public void Start()
        => _lastTimestamp = _clock.Timestamp;

    public void Reset()
        => _lastTimestamp = 0;

    public double ConsumeElapsed(double firstFrameFallbackSeconds)
    {
        if (!double.IsFinite(firstFrameFallbackSeconds) || firstFrameFallbackSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(firstFrameFallbackSeconds));

        var now = _clock.Timestamp;
        var elapsed = _lastTimestamp == 0
            ? firstFrameFallbackSeconds
            : _clock.GetElapsedSeconds(_lastTimestamp, now);
        _lastTimestamp = now;
        return elapsed;
    }
}
