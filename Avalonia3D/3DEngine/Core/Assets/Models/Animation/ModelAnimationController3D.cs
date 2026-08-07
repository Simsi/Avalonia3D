using System;
using ThreeDEngine.Core.Diagnostics;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelAnimationController3D
{
    private readonly ImportedModel3D _model;
    private AnimationClip3D? _clip;
    private readonly ModelAnimationEvaluatorRuntime3D _runtime;
    private float _timeSeconds;
    private float _speed = 1f;
    private bool _loop = true;

    internal ModelAnimationController3D(ImportedModel3D model)
    {
        _model = model;
        _runtime = new ModelAnimationEvaluatorRuntime3D(model.Asset);
        CurrentPose = _runtime.Evaluate(null, 0f);
    }

    internal event EventHandler? PlaybackCompleted;

    public AnimationClip3D? CurrentClip => _clip;
    public float TimeSeconds => _timeSeconds;
    public float Speed
    {
        get => _speed;
        set
        {
            using var mutation = _model.EnterModelMutationScope();
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Animation speed must be finite.");
            _speed = value;
        }
    }

    public bool Loop
    {
        get => _loop;
        set
        {
            using var mutation = _model.EnterModelMutationScope();
            _loop = value;
        }
    }
    public bool IsPlaying { get; private set; }
    public ModelAnimationPose3D CurrentPose { get; private set; }

    public bool Play(string clipName, bool loop = true)
    {
        using var mutation = _model.EnterModelMutationScope();
        var clip = _model.Asset.FindAnimation(clipName);
        if (clip is null) return false;
        Play(clip, loop);
        return true;
    }

    public void Play(AnimationClip3D clip, bool loop = true)
    {
        using var mutation = _model.EnterModelMutationScope();
        _clip = clip ?? throw new ArgumentNullException(nameof(clip));
        Loop = loop;
        IsPlaying = true;
        _timeSeconds = 0f;
        Reevaluate();
        _model.NotifyAnimationPlaybackChanged();
        EngineLog3D.Information("Animation", $"Model '{_model.Name}' started clip '{_clip.Name}'; duration={_clip.Duration:0.###}s; loop={Loop}; speed={Speed:0.###}.");
    }

    public void Pause()
    {
        using var mutation = _model.EnterModelMutationScope();
        if (!IsPlaying) return;
        IsPlaying = false;
        _model.NotifyAnimationPlaybackChanged();
        EngineLog3D.Debug("Animation", $"Model '{_model.Name}' paused clip '{_clip?.Name ?? "none"}' at {_timeSeconds:0.###}s.");
    }

    public void Resume()
    {
        using var mutation = _model.EnterModelMutationScope();
        if (_clip is null || IsPlaying) return;
        IsPlaying = true;
        _model.NotifyAnimationPlaybackChanged();
        EngineLog3D.Debug("Animation", $"Model '{_model.Name}' resumed clip '{_clip.Name}' at {_timeSeconds:0.###}s.");
    }

    public void Stop()
    {
        using var mutation = _model.EnterModelMutationScope();
        var stoppedClipName = _clip?.Name ?? "none";
        IsPlaying = false;
        _timeSeconds = 0f;
        _clip = null;
        Reevaluate();
        _model.NotifyAnimationPlaybackChanged();
        EngineLog3D.Debug("Animation", $"Model '{_model.Name}' stopped clip '{stoppedClipName}'.");
    }

    public void Seek(float timeSeconds)
    {
        using var mutation = _model.EnterModelMutationScope();
        if (_clip is not null && _clip.Duration > 0f)
        {
            _timeSeconds = global::System.Math.Clamp(timeSeconds, 0f, _clip.Duration);
        }
        else
        {
            _timeSeconds = MathF.Max(0f, timeSeconds);
        }
        Reevaluate();
        if (!IsPlaying) _model.NotifyAnimationPlaybackChanged();
    }

    public void Advance(float deltaSeconds)
    {
        using var mutation = _model.EnterModelMutationScope();
        if (!IsPlaying || _clip is null || _clip.Duration <= 0f) return;
        var activeClip = _clip;
        var completed = false;
        _timeSeconds += MathF.Max(0f, deltaSeconds) * Speed;
        if (Loop)
        {
            _timeSeconds %= activeClip.Duration;
            if (_timeSeconds < 0f) _timeSeconds += activeClip.Duration;
        }
        else if (_timeSeconds >= activeClip.Duration)
        {
            _timeSeconds = activeClip.Duration;
            IsPlaying = false;
            completed = true;
        }
        else if (_timeSeconds <= 0f)
        {
            _timeSeconds = 0f;
            IsPlaying = false;
            completed = true;
        }
        Reevaluate();
        if (!completed) return;

        _model.NotifyAnimationPlaybackChanged();
        EngineLog3D.Information("Animation", $"Model '{_model.Name}' completed clip '{activeClip.Name}' at {_timeSeconds:0.###}s; speed={Speed:0.###}.");
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void Reevaluate()
    {
        CurrentPose = _runtime.Evaluate(_clip, _timeSeconds);
        _model.ApplyAnimationPose(CurrentPose);
    }
}
