using System.Numerics;

namespace ThreeDEngine.Core.Rendering.Shadows;

public sealed class DirectionalShadowSnapshot3D
{
    public bool IsEnabled { get; init; }
    public int Resolution { get; init; }
    public float Strength { get; init; }
    public float Bias { get; init; }
    public float NormalBias { get; init; }
    public Matrix4x4 LightViewProjection { get; init; }
    public string Reason { get; init; } = string.Empty;
}
