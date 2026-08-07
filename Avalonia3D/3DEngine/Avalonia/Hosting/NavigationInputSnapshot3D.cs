using System.Numerics;
using ThreeDEngine.Core.Navigation;

namespace ThreeDEngine.Avalonia.Hosting;

/// <summary>
/// Immutable UI-thread capture consumed by the simulation owner. No Avalonia property or
/// mutable navigation setting is read from a dedicated simulation thread.
/// </summary>
internal sealed record NavigationInputSnapshot3D(
    long Sequence,
    bool Enabled,
    SceneNavigationMode Mode,
    Vector3 Movement,
    bool FastMove,
    Vector2 MouseDelta,
    bool JumpRequested,
    bool SynchronizeCameraAngles,
    float FreeFlyMoveSpeed,
    float FreeFlyFastMoveMultiplier,
    float FreeFlyMouseSensitivity,
    bool FreeFlyInvertMouseX,
    bool FreeFlyInvertMouseY,
    float PersonMoveSpeed,
    float PersonRunMultiplier,
    float PersonMouseSensitivity,
    bool PersonInvertMouseX,
    bool PersonInvertMouseY,
    float PersonEyeHeight,
    float PersonBodyHeight,
    float PersonBodyRadius,
    float PersonPushStrength,
    float PersonJumpSpeed,
    float PersonGravity,
    float PersonStepHeight,
    int PressedKeyCount)
{
    public static NavigationInputSnapshot3D Disabled { get; } = new(
        0, false, SceneNavigationMode.None, Vector3.Zero, false, Vector2.Zero, false, false,
        6f, 3f, 0.16f, false, false,
        4.2f, 1.8f, 0.14f, false, false, 1.65f, 1.8f, 0.35f, 2.5f, 6.2f, -18f, 0.15f, 0);
}
