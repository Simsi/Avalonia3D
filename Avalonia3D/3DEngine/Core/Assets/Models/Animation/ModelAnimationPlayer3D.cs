using System;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelAnimationPlayer3D
{
    public AnimationClip3D? Clip { get; private set; }
    public bool Loop { get; set; } = true;
    public float Speed { get; set; } = 1f;
    public float TimeSeconds { get; private set; }
    public bool IsPlaying { get; private set; }

    public event EventHandler? Advanced;
    public event EventHandler? Completed;

    public void Play(AnimationClip3D clip, bool restart = true)
    {
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));
        if (restart) TimeSeconds = 0f;
        IsPlaying = Clip.IsValid;
        Advanced?.Invoke(this, EventArgs.Empty);
    }

    public void Pause() => IsPlaying = false;

    public void Stop()
    {
        IsPlaying = false;
        TimeSeconds = 0f;
        Advanced?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(float timeSeconds)
    {
        if (Clip is { Duration: > 0f }) TimeSeconds = global::System.Math.Clamp(timeSeconds, 0f, Clip.Duration);
        else TimeSeconds = MathF.Max(0f, timeSeconds);
        Advanced?.Invoke(this, EventArgs.Empty);
    }

    public float Advance(float deltaSeconds)
    {
        if (!IsPlaying || Clip is null || Clip.Duration <= 0f) return TimeSeconds;
        TimeSeconds += deltaSeconds * Speed;
        if (Loop)
        {
            TimeSeconds %= Clip.Duration;
            if (TimeSeconds < 0f) TimeSeconds += Clip.Duration;
        }
        else
        {
            if (TimeSeconds >= Clip.Duration)
            {
                TimeSeconds = Clip.Duration;
                IsPlaying = false;
                Completed?.Invoke(this, EventArgs.Empty);
            }
            else if (TimeSeconds <= 0f)
            {
                TimeSeconds = 0f;
                IsPlaying = false;
                Completed?.Invoke(this, EventArgs.Empty);
            }
        }
        Advanced?.Invoke(this, EventArgs.Empty);
        return TimeSeconds;
    }
}
