namespace ThreeDEngine.Core.Assets.Streaming;

public enum AssetResidencyState3D
{
    Unloaded = 0,
    Queued = 1,
    Loading = 2,
    Resident = 3,
    Evicted = 4,
    Faulted = 5
}
