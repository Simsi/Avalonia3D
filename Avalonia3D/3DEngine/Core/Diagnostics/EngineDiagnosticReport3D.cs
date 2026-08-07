using System;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Diagnostics;

public static class EngineDiagnosticReport3D
{
    public static string Create(
        Scene3D? scene = null,
        BackendKind backend = BackendKind.Unknown,
        RenderStats? lastRenderStats = null,
        int maximumLogEntries = 4096)
    {
        var builder = new StringBuilder(16 * 1024);
        var assembly = typeof(EngineDiagnosticReport3D).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        builder.AppendLine("Avalonia3D diagnostic report");
        builder.AppendLine("============================");

        var currentManagedThreadId = global::System.Environment.CurrentManagedThreadId;
        var currentThreadName = global::System.Threading.Thread.CurrentThread.Name ?? "unnamed";
        var pendingThreadPoolItems = global::System.Threading.ThreadPool.PendingWorkItemCount;
        var completedThreadPoolItems = global::System.Threading.ThreadPool.CompletedWorkItemCount;
        var threadPoolThreadCount = global::System.Threading.ThreadPool.ThreadCount;

        Append(builder, "Created UTC", DateTimeOffset.UtcNow.ToString("O"));
        Append(builder, "Session", EngineLog3D.SessionId);
        Append(builder, "Persistent log file", EngineLog3D.CurrentLogFilePath ?? "memory-only");
        Append(builder, "Log directory", EngineLog3D.LogDirectory ?? "unavailable");
        Append(builder, "Process ID", global::System.Environment.ProcessId.ToString());
        Append(builder, "Managed thread", $"{currentManagedThreadId}:{currentThreadName}");
        Append(builder, "Processor count", global::System.Environment.ProcessorCount.ToString());
        Append(builder, "Process uptime ms", global::System.Environment.TickCount64.ToString());
        Append(builder, "Command line", OperatingSystem.IsBrowser() ? "browser" : global::System.Environment.CommandLine);
        Append(builder, "Engine assembly", assembly.GetName().Name ?? "unknown");
        Append(builder, "Engine version", informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown");
        Append(builder, "Backend", backend.ToString());
        Append(builder, "Runtime", RuntimeInformation.FrameworkDescription);
        Append(builder, "OS", RuntimeInformation.OSDescription);
        Append(builder, "Process architecture", RuntimeInformation.ProcessArchitecture.ToString());
        Append(builder, "OS architecture", RuntimeInformation.OSArchitecture.ToString());
        Append(builder, "Browser", OperatingSystem.IsBrowser().ToString());
        Append(builder, "Server GC", GCSettings.IsServerGC.ToString());
        Append(builder, "Latency mode", GCSettings.LatencyMode.ToString());
        Append(builder, "Managed heap bytes", GC.GetTotalMemory(false).ToString());
        Append(builder, "Total allocated bytes", GC.GetTotalAllocatedBytes(false).ToString());
        Append(builder, "GC collections 0/1/2", $"{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
        global::System.Threading.ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIo);
        global::System.Threading.ThreadPool.GetMaxThreads(out var maximumWorkers, out var maximumIo);
        Append(builder, "Thread pool", $"workerAvailable={availableWorkers}/{maximumWorkers}; completionAvailable={availableIo}/{maximumIo}; pending={pendingThreadPoolItems}; completed={completedThreadPoolItems}; threads={threadPoolThreadCount}");
        builder.AppendLine();

        AppendScene(builder, scene, backend);
        AppendRenderStats(builder, lastRenderStats);

        builder.AppendLine("Recent runtime log");
        builder.AppendLine("------------------");
        var logs = EngineLog3D.FormatSnapshot(maximumLogEntries, includeStackTraces: true);
        builder.AppendLine(string.IsNullOrWhiteSpace(logs) ? "(empty)" : logs);
        return builder.ToString().TrimEnd();
    }

    public static bool TryWriteToFile(
        string path,
        Scene3D? scene,
        BackendKind backend,
        RenderStats? lastRenderStats,
        out string? error,
        int maximumLogEntries = 4096)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "A non-empty diagnostic report path is required.";
            return false;
        }

        if (OperatingSystem.IsBrowser())
        {
            error = "Direct file output is unavailable in the browser. Use Create() and expose the returned text through the application UI.";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, Create(scene, backend, lastRenderStats, maximumLogEntries), Encoding.UTF8);
            EngineLog3D.Information("Diagnostics", $"Diagnostic report written to '{fullPath}'.");
            EngineLog3D.Flush();
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            EngineLog3D.Error("Diagnostics", "Failed to write diagnostic report.", exception);
            return false;
        }
    }

    private static void AppendScene(StringBuilder builder, Scene3D? scene, BackendKind backend)
    {
        builder.AppendLine("Scene");
        builder.AppendLine("-----");
        if (scene is null)
        {
            builder.AppendLine("(not supplied)");
            builder.AppendLine();
            return;
        }

        try
        {
            using var sceneAccess = scene.EnterRenderReadScope();
            Append(builder, "Disposed", scene.IsDisposed.ToString());
            Append(builder, "Engine scope", scene.Engine.Id);
            Append(builder, "Engine disposed", scene.Engine.IsDisposed.ToString());
            Append(builder, "Engine active scenes", scene.Engine.ActiveSceneCount.ToString());
            Append(builder, "Engine physics default", scene.Engine.Configuration.PhysicsEnabledByDefault.ToString());
            Append(builder, "Engine model asset cache entries", scene.Engine.Services.TryGetService<ThreeDEngine.Core.Assets.Models.IModelAssetLoader3D>(out var modelLoader)
                ? modelLoader!.CachedAssetCount.ToString()
                : "not configured");
            var meshCache = scene.Engine.Services.GetRequiredService<ThreeDEngine.Core.Geometry.MeshCache3D>();
            Append(builder, "Engine mesh cache entries/hits/misses", $"{meshCache.Count}/{meshCache.HitCount}/{meshCache.MissCount}");
            Append(builder, "Physics backend", scene.PhysicsCore?.GetType().FullName ?? "disabled");
            var snapshot = scene.Registry.GetFrameSnapshot();
            Append(builder, "Change version", scene.ChangeVersion.ToString());
            Append(builder, "Change sequence", scene.ChangeSequence.ToString());
            Append(builder, "Change journal retained/allocated/maximum", $"{scene.RetainedChangeCount}/{scene.AllocatedChangeJournalCapacity}/{scene.ChangeJournalCapacity}");
            Append(builder, "Structure version", scene.StructureVersion.ToString());
            Append(builder, "Batch content version", scene.BatchContentVersion.ToString());
            Append(builder, "Batch transform version", scene.BatchTransformVersion.ToString());
            Append(builder, "Simulation tick", scene.UpdateLoop.SimulationTick.ToString());
            Append(builder, "Simulation time seconds", scene.UpdateLoop.SimulationTimeSeconds.ToString("0.######"));
            Append(builder, "Fixed update Hz", scene.UpdateLoop.FixedUpdatesPerSecond.ToString("0.###"));
            Append(builder, "Simulation accumulator seconds", scene.UpdateLoop.AccumulatorSeconds.ToString("0.######"));
            Append(builder, "Dropped simulation seconds", scene.UpdateLoop.TotalDroppedSeconds.ToString("0.######"));
            Append(builder, "Simulation paused", scene.UpdateLoop.IsPaused.ToString());
            Append(builder, "Simulation faulted", scene.UpdateLoop.IsFaulted.ToString());
            Append(builder, "Simulation command queue", $"pending={scene.Commands.PendingCount}; posted={scene.Commands.LastPostedSequence}; completed={scene.Commands.LastCompletedSequence}");
            var ownership = scene.World.CaptureOwnershipSnapshot();
            Append(builder, "World ownership", $"policy={ownership.MutationPolicy}; owner={ownership.OwnerThreadId}:{ownership.OwnerThreadName ?? "unnamed"}; epoch={ownership.OwnerEpoch}; bound={ownership.OwnerBound}; currentIsOwner={ownership.CurrentThreadIsOwner}; compatibilityMutations={ownership.DirectCompatibilityMutationCount}; strictRejections={ownership.StrictMutationRejectionCount}");
            Append(builder, "World publication", $"version={ownership.PublishedSnapshotVersion}; tick={ownership.PublishedSnapshotTick}; dropped={ownership.DroppedSnapshotPublicationCount}; jobs={ownership.RegisteredJobCount}; replayCapture={ownership.ReplayCaptureEnabled}; replayEntries={ownership.ReplayEntryCount}");
            var simulationMetrics = scene.SimulationMetrics;
            Append(builder, "Simulation stage milliseconds", $"commands={simulationMetrics.CommandsMilliseconds:0.###}; jobs={simulationMetrics.JobsTotalMilliseconds:0.###}/{simulationMetrics.JobsExecuted}; jobSnapshot={simulationMetrics.JobsSnapshotMilliseconds:0.###}; jobExecute={simulationMetrics.JobsExecutionMilliseconds:0.###}; jobCommit={simulationMetrics.JobsCommitMilliseconds:0.###}; user={simulationMetrics.UserUpdateMilliseconds:0.###}; animation={simulationMetrics.AnimationMilliseconds:0.###}; physics={simulationMetrics.PhysicsMilliseconds:0.###}; particles={simulationMetrics.ParticleMilliseconds:0.###}; completion={simulationMetrics.CompletionMilliseconds:0.###}; total={simulationMetrics.TotalMilliseconds:0.###}");
            if (scene.UpdateLoop.Fault is { } updateFault)
            {
                Append(builder, "Simulation fault", updateFault.ToString());
            }
            Append(builder, "Objects", snapshot.AllObjectsInternal.Length.ToString());
            Append(builder, "Renderables", snapshot.RenderablesInternal.Length.ToString());
            Append(builder, "Pickables", snapshot.PickablesInternal.Length.ToString());
            Append(builder, "Colliders", snapshot.CollidersInternal.Length.ToString());
            Append(builder, "Dynamic bodies", snapshot.DynamicBodiesInternal.Length.ToString());
            Append(builder, "Registry version", scene.Registry.Version.ToString());
            Append(builder, "Registry incremental changes", scene.Registry.IncrementalChangeCount.ToString());
            Append(builder, "Registry full rebuilds", scene.Registry.FullRebuildCount.ToString());
            Append(builder, "Registry spatial refreshes", scene.Registry.SpatialRefreshCount.ToString());
            Append(builder, "Registry snapshots", scene.Registry.SnapshotBuildCount.ToString());
            var resources = scene.Engine.Resources.CaptureSnapshot();
            Append(builder, "Engine immutable textures", $"count={resources.TextureCount}; referenced={resources.ReferencedTextureCount}; owners={resources.OwnerCount}");
            Append(builder, "Engine CPU texture bytes", $"resident={resources.ResidentTextureBytes}; peak={resources.PeakResidentTextureBytes}; budget={resources.TextureBudgetBytes}");
            Append(builder, "Engine immutable shaders", $"count={resources.ShaderCount}; referenced={resources.ReferencedShaderCount}");
            Append(builder, "Engine CPU shader bytes", $"resident={resources.ResidentShaderBytes}; peak={resources.PeakResidentShaderBytes}; budget={resources.ShaderBudgetBytes}");
            var assets = scene.Engine.Assets.Statistics;
            Append(builder, "Asset streaming", $"queued={assets.QueuedRequests}; active={assets.ActiveLoads}; resident={assets.ResidentModels}; pinned={assets.PinnedModels}; bytes={assets.ResidentBytes}/{assets.ResidentByteBudget}; hits={assets.CacheHits}; misses={assets.CacheMisses}; coalesced={assets.CoalescedRequests}; evictions={assets.Evictions}; failed={assets.FailedLoads}; completed={assets.CompletedLoads}");
            Append(builder, "Content-addressed cache", $"entries={assets.ContentCacheEntries}; bytes={assets.ContentCacheBytes}");
            var textures = scene.Engine.Textures.Statistics;
            Append(builder, "Texture streaming", $"configured={textures.Configured}; textures={textures.ResidentTextures}; mips={textures.ResidentMipLevels}; pinned={textures.PinnedTextures}; active={textures.ActiveLoads}; bytes={textures.ResidentBytes}/{textures.ResidentByteBudget}; requests={textures.Requests}; hits={textures.CacheHits}; loads={textures.MipLoads}; evictions={textures.Evictions}; failures={textures.Failures}");
            var extensions = scene.Engine.RenderExtensions.CaptureSnapshot();
            Append(builder, "Render extensions", $"version={extensions.Version}; extensions={extensions.Extensions.Count}; passes={extensions.PassCount}");
            var gpuPicking = scene.Engine.GpuPicking.Statistics;
            Append(builder, "GPU picking", $"backend={gpuPicking.Backend}; pending={gpuPicking.PendingRequests}; submitted={gpuPicking.SubmittedRequests}; completed={gpuPicking.CompletedRequests}; cancelled={gpuPicking.CancelledRequests}; failed={gpuPicking.FailedRequests}; batches={gpuPicking.BatchCount}; maxBatch={gpuPicking.MaximumObservedBatchSize}; lastMs={gpuPicking.LastBatchMilliseconds:0.###}");
            Append(builder, "Spatial pickable index", $"indexed={scene.Registry.PickableIndex.IndexedObjectCount}; overflow={scene.Registry.PickableIndex.OverflowObjectCount}");
            Append(builder, "Spatial collider index", $"indexed={scene.Registry.ColliderIndex.IndexedObjectCount}; overflow={scene.Registry.ColliderIndex.OverflowObjectCount}");
            var profile = scene.Engine.Profiler.Capture(600);
            Append(builder, "Profiler", $"frames={profile.Frames.Count}; sequence={profile.LastSequence}; avgFps={profile.AveragePresentedFramesPerSecond:0.###}; p50/p95/p99/worstMs={profile.P50FrameMilliseconds:0.###}/{profile.P95FrameMilliseconds:0.###}/{profile.P99FrameMilliseconds:0.###}/{profile.WorstFrameMilliseconds:0.###}; avgBackendMs={profile.AverageBackendMilliseconds:0.###}; avgSimulationMs={profile.AverageSimulationMilliseconds:0.###}; allocated={profile.TotalAllocatedBytes}");

        }
        catch (Exception exception)
        {
            builder.Append("Unable to inspect scene: ").Append(exception.GetType().Name).Append(": ").AppendLine(exception.Message);
        }

        builder.AppendLine();
    }

    private static void AppendRenderStats(StringBuilder builder, RenderStats? stats)
    {
        builder.AppendLine("Last frame");
        builder.AppendLine("----------");
        if (stats is null)
        {
            builder.AppendLine("(not rendered)");
            builder.AppendLine();
            return;
        }

        Append(builder, "Frame interval ms", stats.FrameTotalMilliseconds.ToString("0.###"));
        Append(builder, "Presented FPS", stats.PresentedFramesPerSecond.ToString("0.###"));
        Append(builder, "Instantaneous presented FPS", stats.InstantaneousPresentedFramesPerSecond.ToString("0.###"));
        Append(builder, "Presentation jitter ms", stats.PresentationJitterMilliseconds.ToString("0.###"));
        Append(builder, "Presented frame count", stats.PresentedFrameCount.ToString());
        Append(builder, "CPU backend ms", stats.BackendMilliseconds.ToString("0.###"));
        Append(builder, "GPU frame ms", stats.GpuTimingAvailable ? stats.GpuFrameMilliseconds.ToString("0.###") : "unavailable");
        Append(builder, "RHI backend", stats.RhiBackend);
        Append(builder, "RHI adapter/API", $"{stats.RhiAdapterName}; {stats.RhiApiVersion}");
        Append(builder, "RHI features", stats.RhiFeatures);
        Append(builder, "RHI limits", stats.RhiLimits);
        Append(builder, "RHI resources", $"live={stats.RhiResourceCount}; buffers={stats.RhiBufferCount}; textures={stats.RhiTextureCount}; owners={stats.RhiOwnershipReferences}; generation={stats.RhiContextGeneration}");
        Append(builder, "RHI resident/peak/budget bytes", $"{stats.RhiResidentBytes}/{stats.RhiPeakResidentBytes}/{stats.RhiResidentBudgetBytes}");
        Append(builder, "RHI texture/budget bytes", $"{stats.RhiTextureBytes}/{stats.RhiTextureBudgetBytes}");
        Append(builder, "RHI create/update/release", $"{stats.RhiResourceCreates}/{stats.RhiResourceUpdates}/{stats.RhiResourceReleases}; validations={stats.RhiValidationCount}");
        Append(builder, "RHI execution", $"profile={stats.RhiCapabilityProfile}; submissions={stats.RhiQueueSubmissionCount}; commands={stats.RhiQueueCommandCount}; completed={stats.RhiCompletedSubmissionId}");
        Append(builder, "RHI frame resources", $"slot={stats.RhiFrameResourceSlot}/{stats.RhiBufferedFrameCount}; upload={stats.RhiUploadRingUsed}/{stats.RhiUploadRingCapacity}; peak={stats.RhiUploadRingPeakUsed}");
        Append(builder, "RHI pipeline/lifetime", $"pipelines={stats.RhiPipelineCacheCount}; hits={stats.RhiPipelineCacheHits}; misses={stats.RhiPipelineCacheMisses}; deferred={stats.RhiDeferredReleaseCount}");
        Append(builder, "GPU-driven", $"active={stats.GpuDrivenActive}; objects={stats.GpuDrivenObjectCount}; meshes={stats.GpuDrivenMeshCount}; materials={stats.GpuDrivenMaterialCount}; meshlets={stats.GpuDrivenMeshletCount}; particles={stats.GpuDrivenParticleCapacity}");
        Append(builder, "GPU-driven graph", $"computePasses={stats.GpuDrivenComputePassCount}; renderPasses={stats.GpuDrivenRenderPassCount}; barriers={stats.GpuDrivenBarrierCount}; physical={stats.GpuDrivenPhysicalResourceCount}; aliased={stats.GpuDrivenAliasedResourceCount}");
        Append(builder, "GPU-driven submission", $"indirectCapacity={stats.GpuDrivenIndirectCommandCapacity}; uploadedBytes={stats.GpuDrivenUploadedBytes}; occlusion={stats.GpuDrivenOcclusionCullingActive}; particles={stats.GpuDrivenParticlesActive}; clustered={stats.GpuDrivenClusteredLightingActive}");
        Append(builder, "Packet build ms", stats.PacketBuildMilliseconds.ToString("0.###"));
        Append(builder, "Upload ms", stats.UploadMilliseconds.ToString("0.###"));
        Append(builder, "Draw calls", stats.DrawCallCount.ToString());
        Append(builder, "Triangles", stats.TriangleCount.ToString());
        Append(builder, "Visible meshes", stats.VisibleMeshCount.ToString());
        Append(builder, "High-scale instances", stats.HighScaleInstanceCount.ToString());
        Append(builder, "Instance upload bytes", stats.InstanceUploadBytes.ToString());
        Append(builder, "Texture upload bytes", stats.TextureUploadBytes.ToString());
        Append(builder, "Geometry resources", stats.GeometryResourceCount.ToString());
        Append(builder, "Geometry source/resident bytes", $"{stats.GeometrySourceBytes}/{stats.GeometryResidentBytes}");
        Append(builder, "Compact index bytes saved", stats.GeometryCompactIndexBytesSaved.ToString());
        Append(builder, "Materialized wireframe geometries", stats.MaterializedWireframeGeometryCount.ToString());
        Append(builder, "Allocated bytes/frame", stats.AllocatedBytesPerFrame.ToString());
        Append(builder, "Allocated MB/s", stats.AllocatedMegabytesPerSecond.ToString("0.###"));
        Append(builder, "GC Gen0/1/2", $"{stats.Gen0Collections}/{stats.Gen1Collections}/{stats.Gen2Collections}");
        Append(builder, "WebGL version", stats.WebGlVersion.ToString());
        Append(builder, "JS frame ms", stats.JsFrameMilliseconds.ToString("0.###"));
        Append(builder, "GPU skinning", $"requested={stats.GpuSkinningRequested}; active={stats.GpuSkinningActive}");
        Append(builder, "Retained ordinary", $"rebuilds={stats.RetainedOrdinaryPlanRebuildCount}; cursorRecoveries={stats.RetainedOrdinaryCursorRecoveryCount}; slotUpdates={stats.RetainedTransformSlotUpdateCount}; skinBatchUpdates={stats.RetainedSkinningBatchUpdateCount}; lastFailure={stats.RetainedOrdinaryLastFailureReason}");
        builder.AppendLine();
    }

    private static void Append(StringBuilder builder, string key, string value)
        => builder.Append(key).Append(": ").AppendLine(value);
}
