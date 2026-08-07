namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Immutable, allocation-free context supplied to fixed-update callbacks.
/// </summary>
public readonly record struct SceneFixedUpdateContext3D(
    long Tick,
    float DeltaSeconds,
    double SimulationTimeSeconds);

/// <summary>
/// Allocation-free scene fixed-update callback.
/// </summary>
public delegate void SceneFixedUpdateHandler3D(
    Scene3D scene,
    in SceneFixedUpdateContext3D context);
