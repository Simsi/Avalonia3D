namespace ThreeDEngine.Core.Navigation;

public sealed class FreeFlyNavigationSettings
{
    public float MoveSpeed { get; set; } = 6f;
    public float FastMoveMultiplier { get; set; } = 3f;
    public bool InvertMouseX { get; set; }
    public bool InvertMouseY { get; set; }
    public float MouseSensitivity { get; set; } = 0.16f;
}
