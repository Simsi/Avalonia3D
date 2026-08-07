using System;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Hosting;

/// <summary>
/// Mutable, backend-neutral composition root. Optional renderer, importer and physics packages
/// register their services through explicit extension methods before <see cref="Build"/>.
/// </summary>
public sealed class Engine3DBuilder
{
    private bool _built;

    public Engine3DBuilder()
    {
        Diagnostics = EngineDiagnosticsOptions3D.FromEnvironment();
        Resources = new EngineResourceOptions3D();
        Assets = new AssetStreamingOptions3D();
        Services = new EngineServiceCollection3D();
        Services.AddSingleton<MeshCache3D>(_ => new MeshCache3D());
    }

    public EngineServiceCollection3D Services { get; }
    public EngineDiagnosticsOptions3D Diagnostics { get; }
    public EngineResourceOptions3D Resources { get; }
    public AssetStreamingOptions3D Assets { get; }

    /// <summary>
    /// Physics is disabled unless a physics package or application explicitly registers a factory.
    /// </summary>
    public bool PhysicsEnabledByDefault { get; set; }

    public Engine3DBuilder ConfigureServices(Action<EngineServiceCollection3D> configure)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(configure);
        configure(Services);
        return this;
    }

    public Engine3DBuilder ConfigureDiagnostics(Action<EngineDiagnosticsOptions3D> configure)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(configure);
        configure(Diagnostics);
        return this;
    }

    public Engine3DBuilder ConfigureResources(Action<EngineResourceOptions3D> configure)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(configure);
        configure(Resources);
        return this;
    }

    public Engine3DBuilder ConfigureAssets(Action<AssetStreamingOptions3D> configure)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(configure);
        configure(Assets);
        return this;
    }

    /// <summary>Registers the application model-loading service.</summary>
    public Engine3DBuilder UseModelAssets(
        IModelAssetLoader3D loader,
        EngineServiceOwnership3D ownership = EngineServiceOwnership3D.Engine)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(loader);
        Services.ReplaceSingleton<IModelAssetLoader3D>(loader, ownership);
        return this;
    }

    /// <summary>Registers a model loader created and owned by the engine scope.</summary>
    public Engine3DBuilder UseModelAssets(Func<IEngineServiceProvider3D, IModelAssetLoader3D> factory)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(factory);
        Services.ReplaceSingleton<IModelAssetLoader3D>(factory);
        return this;
    }


    public Engine3DBuilder UseTextureMipSource(
        ITextureMipSource3D source,
        EngineServiceOwnership3D ownership = EngineServiceOwnership3D.Engine)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(source);
        Services.ReplaceSingleton<ITextureMipSource3D>(source, ownership);
        return this;
    }

    /// <summary>
    /// Configures the per-scene physics factory. It is invoked once for every physics-enabled
    /// scene and must return a new, exclusively scene-owned world each time.
    /// </summary>
    public Engine3DBuilder UsePhysics(Func<IEngineServiceProvider3D, IPhysicsCore> factory)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(factory);
        Services.ReplaceSingleton<IPhysicsCoreFactory3D>(new DelegatePhysicsCoreFactory3D(factory));
        PhysicsEnabledByDefault = true;
        return this;
    }

    public Engine3DBuilder DisablePhysicsByDefault()
    {
        ThrowIfBuilt();
        PhysicsEnabledByDefault = false;
        return this;
    }

    public Engine3D Build()
    {
        ThrowIfBuilt();
        _built = true;

        EngineServiceProvider3D? provider = null;
        try
        {
            if (PhysicsEnabledByDefault && !Services.Contains<IPhysicsCoreFactory3D>())
            {
                throw new InvalidOperationException(
                    "PhysicsEnabledByDefault requires an IPhysicsCoreFactory3D registration. " +
                    "Install a physics package such as Avalonia3D.Physics.Jitter2 and call UseJitter2Physics(), " +
                    "or register a custom factory with UsePhysics().");
            }

            var assetConfiguration = Assets.Freeze();
            if (!Services.Contains<ContentAddressedAssetCache3D>())
            {
                Services.AddSingleton<ContentAddressedAssetCache3D>(_ => new ContentAddressedAssetCache3D(assetConfiguration));
            }

            provider = Services.BuildProvider();
            provider.ValidateAll();
            _ = provider.GetRequiredService<MeshCache3D>();

            var diagnostics = Diagnostics.Freeze();
            EngineLog3D.Configure(
                diagnostics.MinimumLogLevel,
                diagnostics.WriteLogToConsole,
                diagnostics.LogCapacity,
                diagnostics.WriteLogToFile,
                diagnostics.LogDirectory,
                diagnostics.LogFileMaxBytes,
                diagnostics.RetainedLogFileCount);

            var resourceConfiguration = Resources.Freeze();
            var configuration = new EngineConfiguration3D(PhysicsEnabledByDefault, diagnostics, resourceConfiguration, assetConfiguration);
            var engine = new Engine3D(provider, configuration);
            provider = null;
            return engine;
        }
        catch
        {
            provider?.Dispose();
            throw;
        }
    }

    private void ThrowIfBuilt()
    {
        if (_built) throw new InvalidOperationException("An Engine3DBuilder can build exactly one immutable engine scope.");
    }
}
