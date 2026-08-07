using System;
using System.Collections.Generic;
using System.Linq;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class AnimationClip3D
{
    public AnimationClip3D(int index, string name, IReadOnlyList<AnimationChannel3D> channels)
    {
        Index = Guard3D.NonNegative(index, nameof(index));
        Name = string.IsNullOrWhiteSpace(name) ? $"Animation_{index}" : name;
        Channels = Array.AsReadOnly((channels ?? throw new ArgumentNullException(nameof(channels))).ToArray());
        var duration = 0f;
        foreach (var channel in Channels)
        {
            duration = global::System.Math.Max(duration, channel.Sampler.Duration);
        }
        Duration = duration;
    }

    public int Index { get; }
    public string Name { get; }
    public IReadOnlyList<AnimationChannel3D> Channels { get; }
    public float Duration { get; }
    public bool IsValid => Duration > 0f && Channels.Count > 0;
}
