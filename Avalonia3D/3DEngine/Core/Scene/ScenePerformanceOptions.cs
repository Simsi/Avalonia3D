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
    public float DrawDistance { get; set; } = 5000f;
    public float DistanceFadeBand { get; set; } = 80f;
    public bool EnableDistanceFade { get; set; } = true;
    public float ChunkCullingMargin { get; set; } = 0f;
    public bool AdaptivePerformanceEnabled { get; set; }
    public double QualityScale { get; set; } = 1d;
    public int MaxVisibleHighScaleChunks { get; set; } = 0;
    public int MaxHighScaleVisibleInstances { get; set; } = 0;
    public double TargetFrameMilliseconds { get; set; } = 16.6667d;
    public double AllocationBudgetMegabytesPerSecond { get; set; } = 1d;

    // High-scale runtime stabilization. These defaults prefer stable frame pacing over immediate
    // perfect LOD updates when the camera moves across many new chunks.
    public bool EnableHighScaleChunkLodPlanning { get; set; } = true;
    public int HighScaleChunkLodPlanningInstanceThreshold { get; set; } = 8000;
    public int HighScaleChunkLodPlanningChunkThreshold { get; set; } = 64;
    public int HighScaleMaxTransformBatchUploadsPerFrame { get; set; } = 12;
    public int HighScalePartialStateMaxIndividualRanges { get; set; } = 64;
    public int HighScalePartialStateMergeGap { get; set; } = 1;
    public double TelemetryApplyBudgetMilliseconds { get; set; } = 0.75d;
    public int TelemetryMaxUpdatesPerFrame { get; set; } = 192;
    public int TelemetryMaxBacklogMultiplier { get; set; } = 4;
    public bool EnableHighScaleDynamicFadeState { get; set; } = false;
    public bool EnableWebGlClientHighScaleRuntime { get; set; } = true;
    public bool EnableWebGlClientGpuTransformAnimation { get; set; }
    public float WebGlClientGpuTransformAnimationAmplitude { get; set; } = 0.18f;

    // Visual high-scale optimization options. Baked template meshes preserve material slots
    // inside one merged mesh, allowing rack-like composites to draw as one detailed part.
    public bool EnableBakedHighScaleDetailedMeshes { get; set; } = true;
    public bool EnableHighScalePaletteTexture { get; set; } = true;

    // Aggregate layer batches are experimental and are disabled by default.
    // The v34 test showed lower GPU usage and worse FPS because this path can lose
    // frustum/chunk locality and rebuild too much state when LOD membership changes.
    public bool EnableHighScaleAggregateLayerBatches { get; set; } = false;
    public int HighScaleAggregateLayerInstanceThreshold { get; set; } = 15000;

    public float MinimumAdaptiveDrawDistance { get; set; } = 300f;

    public static ScenePerformanceOptions CreateDefault() => new();

    public static ScenePerformanceOptions CreateExtremeScale() => new()
    {
        Profile = ScenePerformanceProfile.ExtremeScale,
        MaxLiveControlSnapshotsPerFrame = 1,
        MaxOverlayLabels = 250,
        TargetMaxVisibleLabels = 250,
        TargetMaxLiveControls = 8,
        DrawDistance = 5000f,
        DistanceFadeBand = 80f,
        EnableDistanceFade = true,
        PreferColliderPicking = true,
        EnableRegistryHotPath = true,
        EnableSpatialBroadphase = true,
        EnableHighScaleChunks = true,
        EnableHighScaleLod = true,
        EnableRetainedInstanceBuffers = true,
        AdaptivePerformanceEnabled = true,
        QualityScale = 1d,
        TargetFrameMilliseconds = 16.6667d,
        AllocationBudgetMegabytesPerSecond = 1d,
        EnableHighScaleChunkLodPlanning = true,
        HighScaleChunkLodPlanningInstanceThreshold = 8000,
        HighScaleChunkLodPlanningChunkThreshold = 64,
        HighScaleMaxTransformBatchUploadsPerFrame = 12,
        HighScalePartialStateMaxIndividualRanges = 64,
        HighScalePartialStateMergeGap = 1,
        TelemetryApplyBudgetMilliseconds = 0.75d,
        TelemetryMaxUpdatesPerFrame = 192,
        TelemetryMaxBacklogMultiplier = 4,
        EnableHighScaleDynamicFadeState = false,
        EnableWebGlClientHighScaleRuntime = true,
        EnableWebGlClientGpuTransformAnimation = false,
        WebGlClientGpuTransformAnimationAmplitude = 0.18f,
        EnableBakedHighScaleDetailedMeshes = true,
        EnableHighScalePaletteTexture = true,
        EnableHighScaleAggregateLayerBatches = false,
        HighScaleAggregateLayerInstanceThreshold = 15000
    };
}
