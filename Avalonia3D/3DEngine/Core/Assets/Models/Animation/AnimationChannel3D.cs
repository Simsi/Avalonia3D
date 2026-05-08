namespace ThreeDEngine.Core.Assets.Models;

public sealed class AnimationChannel3D
{
    public AnimationChannel3D(int targetNodeIndex, AnimationPath3D path, AnimationSampler3D sampler)
    {
        TargetNodeIndex = targetNodeIndex;
        Path = path;
        Sampler = sampler;
    }

    public int TargetNodeIndex { get; }
    public AnimationPath3D Path { get; }
    public AnimationSampler3D Sampler { get; }
}
