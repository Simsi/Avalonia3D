using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Assets.Models;

public readonly record struct ModelAnimationSequenceItem3D
{
    public ModelAnimationSequenceItem3D(string clipName, float speed = 1f, bool loopClip = false)
    {
        ClipName = Guard3D.RequiredText(clipName, nameof(clipName));
        Speed = Guard3D.Finite(speed, nameof(speed));
        if (Speed == 0f) throw new ArgumentOutOfRangeException(nameof(speed), speed, "Animation speed cannot be zero.");
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
    private readonly ReadOnlyCollection<ModelAnimationSequenceItem3D> _itemsView;
    private int _index;

    public ModelAnimationSequence3D(ImportedModel3D model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _itemsView = _items.AsReadOnly();
        _model.Animation.PlaybackCompleted += OnPlaybackCompleted;
    }

    public IReadOnlyList<ModelAnimationSequenceItem3D> Items => _itemsView;
    public int CurrentIndex => _index;
    public ModelAnimationSequenceItem3D? CurrentItem => _items.Count == 0 || _index < 0 || _index >= _items.Count ? null : _items[_index];
    public bool IsPlaying { get; private set; }
    public bool LoopSequence { get; set; } = true;

    public ModelAnimationSequence3D Add(string clipName, float speed = 1f, bool loopClip = false)
    {
        _items.Add(new ModelAnimationSequenceItem3D(clipName, speed, loopClip));
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

        // A scene-owned fixed update is the sole animation-clock owner. Calling this method
        // from a UI/demo timer must not advance the same clip a second time or mutate the
        // model from the UI thread. Manual/detached models retain explicit Advance support.
        if (_model.OwnerScene is { UpdateLoop.AdvanceAnimations: true }) return;
        _model.Animation.Advance(deltaSeconds);
    }

    private void OnPlaybackCompleted(object? sender, EventArgs e)
    {
        if (!IsPlaying || _items.Count == 0) return;
        var current = CurrentItem;
        if (current.HasValue && current.Value.LoopClip) return;
        PlayNext();
    }

    private bool PlayCurrent()
    {
        var item = CurrentItem;
        if (!item.HasValue || string.IsNullOrWhiteSpace(item.Value.ClipName)) return false;
        var clip = _model.Asset.FindAnimation(item.Value.ClipName);
        if (clip is null || !clip.IsValid) return false;
        _model.Animation.Speed = item.Value.Speed;
        _model.Animation.Play(clip, loop: item.Value.LoopClip);
        EngineLog3D.Information("Animation.Sequence", $"Model '{_model.Name}' sequence selected index={_index}/{_items.Count - 1}; clip='{clip.Name}'; loopClip={item.Value.LoopClip}; loopSequence={LoopSequence}; speed={item.Value.Speed:0.###}.");
        return true;
    }
}
