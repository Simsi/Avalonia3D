using System;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelAnimationController3D
{
    private readonly ImportedModel3D _model;
    private AnimationClip3D? _clip;
    private readonly ModelAnimationEvaluatorRuntime3D _runtime;
    private float _timeSeconds;

    internal ModelAnimationController3D(ImportedModel3D model)
    {
        _model = model;
        _runtime = new ModelAnimationEvaluatorRuntime3D(model.Asset);
        CurrentPose = _runtime.Evaluate(null, 0f);
    }

    public AnimationClip3D? CurrentClip => _clip;
    public float TimeSeconds => _timeSeconds;
    public float Speed { get; set; } = 1f;
    public bool Loop { get; set; } = true;
    public bool IsPlaying { get; private set; }
    public ModelAnimationPose3D CurrentPose { get; private set; }

    public bool Play(string clipName, bool loop = true)
    {
        var clip = _model.Asset.FindAnimation(clipName);
        if (clip is null) return false;
        Play(clip, loop);
        return true;
    }

    public void Play(AnimationClip3D clip, bool loop = true)
    {
        _clip = clip ?? throw new ArgumentNullException(nameof(clip));
        Loop = loop;
        IsPlaying = true;
        _timeSeconds = 0f;
        Reevaluate();
    }

    public void Pause() => IsPlaying = false;
    public void Resume()
    {
        if (_clip is not null) IsPlaying = true;
    }

    public void Stop()
    {
        IsPlaying = false;
        _timeSeconds = 0f;
        _clip = null;
        Reevaluate();
    }

    public void Seek(float timeSeconds)
    {
        if (_clip is not null && _clip.Duration > 0f)
        {
            _timeSeconds = global::System.Math.Clamp(timeSeconds, 0f, _clip.Duration);
        }
        else
        {
            _timeSeconds = MathF.Max(0f, timeSeconds);
        }
        Reevaluate();
    }

    public void Advance(float deltaSeconds)
    {
        if (!IsPlaying || _clip is null || _clip.Duration <= 0f) return;
        _timeSeconds += MathF.Max(0f, deltaSeconds) * Speed;
        if (Loop)
        {
            _timeSeconds %= _clip.Duration;
            if (_timeSeconds < 0f) _timeSeconds += _clip.Duration;
        }
        else if (_timeSeconds >= _clip.Duration)
        {
            _timeSeconds = _clip.Duration;
            IsPlaying = false;
        }
        else if (_timeSeconds <= 0f)
        {
            _timeSeconds = 0f;
            IsPlaying = false;
        }
        Reevaluate();
    }

    private void Reevaluate()
    {
        CurrentPose = _runtime.Evaluate(_clip, _timeSeconds);
        _model.ApplyAnimationPose(CurrentPose);
    }
}
