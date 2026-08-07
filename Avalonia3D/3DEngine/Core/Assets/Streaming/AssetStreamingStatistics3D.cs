namespace ThreeDEngine.Core.Assets.Streaming;

public readonly record struct AssetStreamingStatistics3D(
    int QueuedRequests,
    int ActiveLoads,
    int ResidentModels,
    int PinnedModels,
    long ResidentBytes,
    long ResidentByteBudget,
    long CacheHits,
    long CacheMisses,
    long CoalescedRequests,
    long Evictions,
    long FailedLoads,
    long CompletedLoads,
    long ContentCacheBytes,
    int ContentCacheEntries);
