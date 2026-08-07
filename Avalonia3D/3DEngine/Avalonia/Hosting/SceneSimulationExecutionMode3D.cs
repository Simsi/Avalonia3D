namespace ThreeDEngine.Avalonia.Hosting;

/// <summary>Controls where automatic fixed simulation is executed by Scene3DControl.</summary>
public enum SceneSimulationExecutionMode3D
{
    /// <summary>Dedicated worker on desktop; cooperative host-thread execution in browser.</summary>
    Automatic = 0,
    /// <summary>Execute simulation on the Avalonia host thread.</summary>
    HostThread = 1,
    /// <summary>Execute simulation on one dedicated worker thread. Not available in browser.</summary>
    DedicatedThread = 2
}
