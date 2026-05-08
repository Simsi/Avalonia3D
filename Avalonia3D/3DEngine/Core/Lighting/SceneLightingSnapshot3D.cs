using System.Numerics;

namespace ThreeDEngine.Core.Lighting;

public readonly struct SceneLightingSnapshot3D
{
    public SceneLightingSnapshot3D(
        Vector3 ambient,
        Vector3 directionalDirection,
        Vector3 directionalColor,
        Vector4 pointPosition,
        Vector4 pointColor,
        Vector4 spotPosition,
        Vector4 spotDirection,
        Vector4 spotColor,
        Vector4 spotCone)
    {
        Ambient = ambient;
        DirectionalDirection = directionalDirection;
        DirectionalColor = directionalColor;
        PointPosition = pointPosition;
        PointColor = pointColor;
        SpotPosition = spotPosition;
        SpotDirection = spotDirection;
        SpotColor = spotColor;
        SpotCone = spotCone;
    }

    public Vector3 Ambient { get; }
    public Vector3 DirectionalDirection { get; }
    public Vector3 DirectionalColor { get; }
    public Vector4 PointPosition { get; }
    public Vector4 PointColor { get; }
    public Vector4 SpotPosition { get; }
    public Vector4 SpotDirection { get; }
    public Vector4 SpotColor { get; }
    public Vector4 SpotCone { get; }

    public static SceneLightingSnapshot3D Empty { get; } = new(
        new Vector3(0.28f, 0.28f, 0.28f),
        Vector3.Normalize(new Vector3(-0.35f, -0.75f, -0.55f)),
        Vector3.Zero,
        new Vector4(0f, 0f, 0f, 1f),
        Vector4.Zero,
        new Vector4(0f, 0f, 0f, 1f),
        new Vector4(0f, -1f, 0f, 0f),
        Vector4.Zero,
        new Vector4(0.9f, 0.8f, 1f, 0f));
}
