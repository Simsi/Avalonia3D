namespace ThreeDEngine.Core.World;

/// <summary>Immutable ownership and publication diagnostics for a world.</summary>
public readonly record struct WorldOwnershipSnapshot3D(
    WorldMutationPolicy3D MutationPolicy,
    int OwnerThreadId,
    string? OwnerThreadName,
    long OwnerEpoch,
    bool OwnerBound,
    bool CurrentThreadIsOwner,
    long DirectCompatibilityMutationCount,
    long StrictMutationRejectionCount,
    long PublishedSnapshotVersion,
    long PublishedSnapshotTick,
    long DroppedSnapshotPublicationCount,
    int RegisteredJobCount,
    bool ReplayCaptureEnabled,
    int ReplayEntryCount);
