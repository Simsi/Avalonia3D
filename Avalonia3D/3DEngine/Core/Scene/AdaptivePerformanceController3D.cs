using System;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Core.Scene;

public sealed class AdaptivePerformanceController3D
{
    private double _qualityScale = 1d;
    private float _baseDrawDistance = -1f;
    private int _stableFrames;
    private int _pressureFrames;

    public bool Enabled { get; set; }
    public double QualityScale => _qualityScale;
    public double MinimumQualityScale { get; set; } = 0.35d;
    public double RecoveryStep { get; set; } = 0.015d;
    public double DegradeStep { get; set; } = 0.075d;
    public int StableFramesToRecover { get; set; } = 90;
    public int PressureFramesToDegrade { get; set; } = 4;

    public void Reset(ScenePerformanceOptions options)
    {
        _qualityScale = 1d;
        _stableFrames = 0;
        _pressureFrames = 0;
        _baseDrawDistance = options.DrawDistance;
        Apply(options);
    }

    public void RecordFrame(RenderStats stats, ScenePerformanceOptions options, double targetFps)
    {
        if (!Enabled)
        {
            if (_qualityScale != 1d)
            {
                _qualityScale = 1d;
                Apply(options);
            }
            return;
        }

        if (_baseDrawDistance <= 0f)
        {
            _baseDrawDistance = options.DrawDistance;
        }

        var targetMs = targetFps > 0d ? 1000d / targetFps : options.TargetFrameMilliseconds;
        options.TargetFrameMilliseconds = targetMs;
        var overBudget = stats.FrameTotalMilliseconds > targetMs * 1.18d || stats.BackendMilliseconds > targetMs * 0.75d;
        var allocationPressure = stats.AllocatedMegabytesPerSecond > options.AllocationBudgetMegabytesPerSecond * 2d || stats.Gen2Collections > 0;
        var highScalePressure = stats.HighScaleBufferBuildMilliseconds + stats.HighScalePlanMilliseconds > targetMs * 0.35d;

        if (overBudget || allocationPressure || highScalePressure)
        {
            _pressureFrames++;
            _stableFrames = 0;
            if (_pressureFrames >= PressureFramesToDegrade)
            {
                _pressureFrames = 0;
                _qualityScale = System.Math.Max(MinimumQualityScale, _qualityScale - DegradeStep);
                Apply(options);
            }
        }
        else
        {
            _stableFrames++;
            _pressureFrames = 0;
            if (_stableFrames >= StableFramesToRecover && _qualityScale < 1d)
            {
                _stableFrames = 0;
                _qualityScale = System.Math.Min(1d, _qualityScale + RecoveryStep);
                Apply(options);
            }
        }
    }

    private void Apply(ScenePerformanceOptions options)
    {
        options.QualityScale = _qualityScale;
        if (_baseDrawDistance > 0f)
        {
            options.DrawDistance = System.Math.Max(options.MinimumAdaptiveDrawDistance, (float)(_baseDrawDistance * _qualityScale));
        }

        if (_qualityScale >= 0.98d)
        {
            options.MaxVisibleHighScaleChunks = 0;
            options.MaxHighScaleVisibleInstances = 0;
        }
        else
        {
            options.MaxVisibleHighScaleChunks = System.Math.Max(16, (int)(220 * _qualityScale));
            options.MaxHighScaleVisibleInstances = System.Math.Max(4_000, (int)(120_000 * _qualityScale));
            options.MaxLiveControlSnapshotsPerFrame = System.Math.Max(0, System.Math.Min(options.MaxLiveControlSnapshotsPerFrame, (int)System.Math.Ceiling(2d * _qualityScale)));
            options.MaxOverlayLabels = System.Math.Max(50, (int)(500 * _qualityScale));
        }
    }
}
