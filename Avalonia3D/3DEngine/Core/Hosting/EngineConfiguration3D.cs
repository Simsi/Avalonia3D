using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Hosting;

public sealed record EngineConfiguration3D(
    bool PhysicsEnabledByDefault,
    EngineDiagnosticsConfiguration3D Diagnostics,
    EngineResourceConfiguration3D Resources,
    AssetStreamingConfiguration3D Assets);
