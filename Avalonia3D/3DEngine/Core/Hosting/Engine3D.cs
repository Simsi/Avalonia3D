using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Rendering.Extensions;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Hosting;

/// <summary>
/// Immutable engine scope and composition root. Services are isolated per engine; scenes are
/// child resources and are disposed before their services.
/// </summary>
public sealed class Engine3D : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly EngineServiceProvider3D _services;
    private readonly HashSet<Scene3D> _scenes = new(ReferenceEqualityComparer.Instance);
    private readonly TaskCompletionSource<bool> _shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    internal Engine3D(EngineServiceProvider3D services, EngineConfiguration3D configuration)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Id = Guid.NewGuid().ToString("N");
        Resources = new EngineResourceManager3D(configuration.Resources);
        var contentCache = _services.GetRequiredService<ContentAddressedAssetCache3D>();
        Assets = new AssetManager3D(_services, configuration.Assets, contentCache);
        Textures = new TextureStreamingManager3D(_services, configuration.Assets);
        RenderExtensions = new RenderExtensionRegistry3D();
        RenderExtensionRuntime = new RenderExtensionRuntime3D();
        MaterialExtensions = new MaterialShaderExtensionRegistry3D();
        Profiler = new EngineProfiler3D();
        GpuPicking = new GpuPickingService3D();
        EngineLog3D.Information("Engine", $"Engine scope {Id} built with {_services.GetType().Name}; physicsDefault={Configuration.PhysicsEnabledByDefault}.");
    }

    public string Id { get; }
    public EngineConfiguration3D Configuration { get; }
    public IEngineServiceProvider3D Services => _services;
    public EngineResourceManager3D Resources { get; }
    public AssetManager3D Assets { get; }
    public TextureStreamingManager3D Textures { get; }
    public RenderExtensionRegistry3D RenderExtensions { get; }
    public RenderExtensionRuntime3D RenderExtensionRuntime { get; }
    public MaterialShaderExtensionRegistry3D MaterialExtensions { get; }
    public EngineProfiler3D Profiler { get; }
    public GpuPickingService3D GpuPicking { get; }

    public EngineFrameCapture3D CaptureFrame(int maximumFrames = 600, int maximumLogEntries = 512)
        => EngineFrameCapture3D.Capture(this, maximumFrames, maximumLogEntries);
    public bool IsDisposed => Volatile.Read(ref _disposed);
    /// <summary>Completes after asynchronous loaders, GPU picking, and the engine service provider are fully released.</summary>
    public Task ShutdownCompletion => _shutdownCompletion.Task;
    public int ActiveSceneCount
    {
        get { lock (_gate) return _scenes.Count; }
    }

    [Obsolete("Use an explicit Engine3DBuilder. This compatibility method requires Avalonia3D.Engine or the complete 3DEngine source-drop.")]
    public static Engine3D CreateDefault() => Engine3DDefaultStack3D.Create();

    public Scene3D CreateScene(Scene3DOptions? options = null)
    {
        ThrowIfDisposed();
        return new Scene3D(this, options);
    }

    public Scene3D CreateScene(Action<Scene3DOptions> configure)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configure);
        var options = new Scene3DOptions();
        configure(options);
        return new Scene3D(this, options);
    }


    public SceneBuilder3D CreateSceneBuilder(Scene3DOptions? options = null)
    {
        ThrowIfDisposed();
        return new SceneBuilder3D(this, options);
    }

    public Scene3D BuildScene(Action<SceneBuilder3D> configure, Scene3DOptions? options = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configure);
        using var builder = new SceneBuilder3D(this, options);
        configure(builder);
        return builder.Build();
    }

    public void Dispose()
    {
        Scene3D[] scenes;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            scenes = new Scene3D[_scenes.Count];
            _scenes.CopyTo(scenes);
        }

        List<Exception>? failures = null;
        void Release(string component, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(new InvalidOperationException($"Engine3D failed to release {component}.", exception));
                EngineLog3D.Error("Engine", $"Engine scope {Id} failed while releasing {component}; remaining ownership scopes will still be released.", exception);
            }
        }

        for (var i = scenes.Length - 1; i >= 0; i--)
        {
            var sceneIndex = i;
            Release($"child scene {sceneIndex}", scenes[i].Dispose);
        }

        lock (_gate) _scenes.Clear();
        Release("the GPU picking service", GpuPicking.Dispose);
        Release("the texture streaming manager", Textures.Dispose);
        Release("the asset streaming manager", Assets.Dispose);

        var dependentShutdown = Task.WhenAll(
            GpuPicking.ShutdownCompletion,
            Textures.ShutdownCompletion,
            Assets.ShutdownCompletion);
        if (dependentShutdown.IsCompleted)
        {
            ObserveDependencyFailure(dependentShutdown, ref failures);
            Release("the immutable resource manager", Resources.Dispose);
            Release("the service provider", _services.Dispose);
            CompleteShutdown(failures);
        }
        else
        {
            var priorFailures = failures?.ToArray();
            EngineLog3D.Information("Engine", $"Engine scope {Id} disposal is waiting asynchronously for loader/picking cancellation before releasing service dependencies.");
            _ = dependentShutdown.ContinueWith(
                task => CompleteDeferredServiceDisposal(task, priorFailures),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        if (failures is { Count: > 0 })
        {
            EngineLog3D.Warning("Engine", $"Engine scope {Id} disposal initiated with {failures.Count} failure(s); independent ownership scopes were still processed.");
            throw new AggregateException("Engine3D disposal completed with one or more resource-release failures.", failures);
        }

        if (dependentShutdown.IsCompleted)
            EngineLog3D.Information("Engine", $"Engine scope {Id} disposed; {scenes.Length} child scene(s), immutable resources and services released.");
        else
            EngineLog3D.Information("Engine", $"Engine scope {Id} accepted disposal; {scenes.Length} child scene(s) released and asynchronous dependency shutdown remains observable through ShutdownCompletion.");
    }


    public async ValueTask DisposeAsync()
    {
        Exception? initiationFailure = null;
        try
        {
            Dispose();
        }
        catch (Exception exception)
        {
            initiationFailure = exception;
        }

        Exception? completionFailure = null;
        try
        {
            await ShutdownCompletion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            completionFailure = exception;
        }

        if (initiationFailure is not null && completionFailure is not null && !ReferenceEquals(initiationFailure, completionFailure))
            throw new AggregateException("Engine3D asynchronous disposal reported failures while initiating and completing shutdown.", initiationFailure, completionFailure);
        if (completionFailure is not null) throw completionFailure;
        if (initiationFailure is not null) throw initiationFailure;
    }

    private void CompleteDeferredServiceDisposal(Task dependencyShutdown, Exception[]? priorFailures)
    {
        List<Exception>? failures = priorFailures is { Length: > 0 } ? new List<Exception>(priorFailures) : null;
        ObserveDependencyFailure(dependencyShutdown, ref failures);
        try
        {
            Resources.Dispose();
        }
        catch (Exception exception)
        {
            failures ??= new List<Exception>();
            failures.Add(new InvalidOperationException("Engine3D failed to release immutable resources after asynchronous dependency shutdown.", exception));
            EngineLog3D.Error("Engine", $"Engine scope {Id} failed while releasing deferred immutable resources.", exception);
        }
        try
        {
            _services.Dispose();
        }
        catch (Exception exception)
        {
            failures ??= new List<Exception>();
            failures.Add(new InvalidOperationException("Engine3D failed to release the service provider after asynchronous dependency shutdown.", exception));
            EngineLog3D.Error("Engine", $"Engine scope {Id} failed while releasing deferred service dependencies.", exception);
        }
        CompleteShutdown(failures);
    }

    private static void ObserveDependencyFailure(Task dependencyShutdown, ref List<Exception>? failures)
    {
        if (!dependencyShutdown.IsFaulted || dependencyShutdown.Exception is null) return;
        failures ??= new List<Exception>();
        foreach (var exception in dependencyShutdown.Exception.Flatten().InnerExceptions)
            failures.Add(new InvalidOperationException("An asynchronous engine dependency failed during shutdown.", exception));
    }

    private void CompleteShutdown(List<Exception>? failures)
    {
        if (failures is { Count: > 0 })
        {
            var aggregate = new AggregateException("Engine3D shutdown completed with one or more resource-release failures.", failures);
            _shutdownCompletion.TrySetException(aggregate);
            return;
        }
        _shutdownCompletion.TrySetResult(true);
    }

    internal IPhysicsCore? CreatePhysicsCore(Scene3DOptions options)
    {
        ThrowIfDisposed();
        var enabled = options.PhysicsEnabled ?? Configuration.PhysicsEnabledByDefault;
        if (!enabled)
        {
            if (options.PhysicsFactory is not null)
            {
                throw new InvalidOperationException("Scene3DOptions cannot disable physics and provide a physics factory at the same time.");
            }
            return null;
        }

        return options.PhysicsFactory is not null
            ? options.PhysicsFactory(_services) ?? throw new InvalidOperationException("The scene physics factory returned null.")
            : _services.GetRequiredService<IPhysicsCoreFactory3D>().CreatePhysicsCore(_services)
              ?? throw new InvalidOperationException("The registered physics factory returned null.");
    }

    internal void AttachScene(Scene3D scene)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_scenes.Add(scene)) throw new InvalidOperationException("Scene is already attached to this engine scope.");
        }
    }

    internal void DetachScene(Scene3D scene)
    {
        lock (_gate) _scenes.Remove(scene);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
}
