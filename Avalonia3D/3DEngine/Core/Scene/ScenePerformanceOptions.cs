namespace ThreeDEngine.Core.Scene;

public enum ScenePerformanceProfile
{
    Balanced = 0,
    DesktopLarge = 1,
    WebBalanced = 2,
    PreviewQuality = 3,
    ExtremeScale = 4
}

public sealed class ScenePerformanceOptions
{
    public ScenePerformanceProfile Profile { get; set; } = ScenePerformanceProfile.Balanced;
    public int MaxLiveControlSnapshotsPerFrame { get; set; } = 2;
    public int MaxOverlayLabels { get; set; } = 500;
    public bool PreferColliderPicking { get; set; } = true;
    public bool EnableRegistryHotPath { get; set; } = true;
    public bool EnableSpatialBroadphase { get; set; } = true;
    public bool EnableHighScaleChunks { get; set; } = true;
    public bool EnableHighScaleLod { get; set; } = true;
    public bool EnableRetainedInstanceBuffers { get; set; } = true;
    public int TargetMaxVisibleLabels { get; set; } = 500;
    public int TargetMaxLiveControls { get; set; } = 32;

    public static ScenePerformanceOptions CreateDefault() => new();

    public static ScenePerformanceOptions CreateExtremeScale() => new()
    {
        Profile = ScenePerformanceProfile.ExtremeScale,
        MaxLiveControlSnapshotsPerFrame = 1,
        MaxOverlayLabels = 250,
        TargetMaxVisibleLabels = 250,
        TargetMaxLiveControls = 8,
        PreferColliderPicking = true,
        EnableRegistryHotPath = true,
        EnableSpatialBroadphase = true,
        EnableHighScaleChunks = true,
        EnableHighScaleLod = true,
        EnableRetainedInstanceBuffers = true
    };
}
