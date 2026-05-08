using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Assets.Models;

public readonly record struct ModelAnimationSequenceItem3D
{
    public ModelAnimationSequenceItem3D(string clipName, float speed = 1f, bool loopClip = false)
    {
        ClipName = string.IsNullOrWhiteSpace(clipName) ? string.Empty : clipName;
        Speed = speed == 0f ? 1f : speed;
        LoopClip = loopClip;
    }

    public string ClipName { get; }
    public float Speed { get; }
    public bool LoopClip { get; }
}

public sealed class ModelAnimationSequence3D
{
    private readonly ImportedModel3D _model;
    private readonly List<ModelAnimationSequenceItem3D> _items = new();
    private int _index;

    public ModelAnimationSequence3D(ImportedModel3D model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public IReadOnlyList<ModelAnimationSequenceItem3D> Items => _items;
    public int CurrentIndex => _index;
    public ModelAnimationSequenceItem3D? CurrentItem => _items.Count == 0 || _index < 0 || _index >= _items.Count ? null : _items[_index];
    public bool IsPlaying { get; private set; }
    public bool LoopSequence { get; set; } = true;

    public ModelAnimationSequence3D Add(string clipName, float speed = 1f, bool loopClip = false)
    {
        if (!string.IsNullOrWhiteSpace(clipName))
        {
            _items.Add(new ModelAnimationSequenceItem3D(clipName, speed, loopClip));
        }
        return this;
    }

    public void Clear()
    {
        _items.Clear();
        _index = 0;
        IsPlaying = false;
        _model.Animation.Stop();
    }

    public bool PlayFromStart()
    {
        if (_items.Count == 0) return false;
        _index = 0;
        IsPlaying = PlayCurrent();
        return IsPlaying;
    }

    public bool PlayNext()
    {
        if (_items.Count == 0) return false;
        _index++;
        if (_index >= _items.Count)
        {
            if (!LoopSequence)
            {
                _index = _items.Count - 1;
                IsPlaying = false;
                return false;
            }
            _index = 0;
        }

        IsPlaying = PlayCurrent();
        return IsPlaying;
    }

    public void Pause()
    {
        IsPlaying = false;
        _model.Animation.Pause();
    }

    public void Resume()
    {
        if (_model.Animation.CurrentClip is not null)
        {
            IsPlaying = true;
            _model.Animation.Resume();
        }
    }

    public void Stop()
    {
        IsPlaying = false;
        _model.Animation.Stop();
    }

    public void Advance(float deltaSeconds)
    {
        if (!IsPlaying || _items.Count == 0) return;
        if (_model.Animation.CurrentClip is null && !PlayCurrent())
        {
            IsPlaying = false;
            return;
        }

        _model.Animation.Advance(deltaSeconds);
        var current = CurrentItem;
        if (current.HasValue && current.Value.LoopClip)
        {
            return;
        }

        if (!_model.Animation.IsPlaying)
        {
            PlayNext();
        }
    }

    private bool PlayCurrent()
    {
        var item = CurrentItem;
        if (!item.HasValue || string.IsNullOrWhiteSpace(item.Value.ClipName)) return false;
        var clip = _model.Asset.FindAnimation(item.Value.ClipName);
        if (clip is null || !clip.IsValid) return false;
        _model.Animation.Speed = item.Value.Speed;
        _model.Animation.Play(clip, loop: item.Value.LoopClip);
        return true;
    }
}
