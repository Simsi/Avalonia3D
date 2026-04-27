namespace ThreeDEngine.Core.Navigation;

public sealed class PersonNavigationSettings
{
    public float MoveSpeed { get; set; } = 4.2f;
    public float RunMultiplier { get; set; } = 1.8f;
    public bool InvertMouseX { get; set; }
    public bool InvertMouseY { get; set; }
    public float MouseSensitivity { get; set; } = 0.14f;
    public float EyeHeight { get; set; } = 1.65f;
    public float BodyHeight { get; set; } = 1.8f;
    public float BodyRadius { get; set; } = 0.35f;
    public float PushStrength { get; set; } = 2.5f;
    public float JumpSpeed { get; set; } = 6.2f;
    public float Gravity { get; set; } = -18f;
    public float StepHeight { get; set; } = 0.15f;
}
