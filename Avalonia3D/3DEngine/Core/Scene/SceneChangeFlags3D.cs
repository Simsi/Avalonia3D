using System;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Aggregated scene-change categories committed by one scene transaction.
/// Unlike <see cref="SceneChangeKind"/>, flags preserve every category when many
/// objects are mutated inside <see cref="Scene3D.BeginUpdate"/> or one fixed tick.
/// </summary>
[Flags]
public enum SceneChangeFlags3D
{
    None = 0,
    Unknown = 1 << 0,
    Structure = 1 << 1,
    Transform = 1 << 2,
    Material = 1 << 3,
    Geometry = 1 << 4,
    Visibility = 1 << 5,
    Physics = 1 << 6,
    Control = 1 << 7,
    Camera = 1 << 8,
    Lighting = 1 << 9,
    Debug = 1 << 10,
    HighScaleState = 1 << 11,
    AnimationPose = 1 << 12,
    Metadata = 1 << 13,
    All = (1 << 14) - 1
}

internal static class SceneChangeFlagsExtensions3D
{
    public static SceneChangeFlags3D ToFlag(this SceneChangeKind kind)
        => kind switch
        {
            SceneChangeKind.Structure => SceneChangeFlags3D.Structure,
            SceneChangeKind.Transform => SceneChangeFlags3D.Transform,
            SceneChangeKind.Material => SceneChangeFlags3D.Material,
            SceneChangeKind.Geometry => SceneChangeFlags3D.Geometry,
            SceneChangeKind.Visibility => SceneChangeFlags3D.Visibility,
            SceneChangeKind.Physics => SceneChangeFlags3D.Physics,
            SceneChangeKind.Control => SceneChangeFlags3D.Control,
            SceneChangeKind.Camera => SceneChangeFlags3D.Camera,
            SceneChangeKind.Lighting => SceneChangeFlags3D.Lighting,
            SceneChangeKind.Debug => SceneChangeFlags3D.Debug,
            SceneChangeKind.HighScaleState => SceneChangeFlags3D.HighScaleState,
            SceneChangeKind.AnimationPose => SceneChangeFlags3D.AnimationPose,
            SceneChangeKind.Metadata => SceneChangeFlags3D.Metadata,
            _ => SceneChangeFlags3D.Unknown
        };
}
