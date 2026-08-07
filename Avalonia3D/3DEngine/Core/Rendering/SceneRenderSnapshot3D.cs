using System;
using System.Numerics;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Per-frame scalar publication. The allocating <see cref="Capture"/> entry point remains for
/// diagnostics/tests; GPU presenters use <see cref="CaptureInto"/> through
/// <see cref="SceneRenderFrameScratch3D"/> so camera/light publication reaches zero steady-state
/// allocations after its capacity high-water marks are established.
/// </summary>
internal sealed class SceneRenderSnapshot3D
{
    private DirectionalLightSnapshot3D[] _directionalLights = Array.Empty<DirectionalLightSnapshot3D>();
    private PointLightSnapshot3D[] _pointLights = Array.Empty<PointLightSnapshot3D>();
    private SpotLightSnapshot3D[] _spotLights = Array.Empty<SpotLightSnapshot3D>();
    private int _directionalLightCount;
    private int _pointLightCount;
    private int _spotLightCount;

    internal SceneRenderSnapshot3D()
    {
        Registry = null!;
        Pipeline = null!;
    }

    public SceneFrameSnapshot3D Registry { get; private set; }
    public Matrix4x4 View { get; private set; }
    public Matrix4x4 Projection { get; private set; }
    public Matrix4x4 ViewProjection { get; private set; }
    public Vector3 CameraPosition { get; private set; }
    public ColorRgba BackgroundColor { get; private set; }
    public ColorRgba AmbientLightColor { get; private set; }
    public float AmbientLightIntensity { get; private set; }
    public ReadOnlyMemory<DirectionalLightSnapshot3D> DirectionalLights => _directionalLights.AsMemory(0, _directionalLightCount);
    public ReadOnlyMemory<PointLightSnapshot3D> PointLights => _pointLights.AsMemory(0, _pointLightCount);
    public ReadOnlyMemory<SpotLightSnapshot3D> SpotLights => _spotLights.AsMemory(0, _spotLightCount);
    public RenderPipelinePlan3D Pipeline { get; private set; }
    public long SceneChangeSequence { get; private set; }
    public long BatchContentVersion { get; private set; }
    public long BatchTransformVersion { get; private set; }
    public long ParticleContentVersion { get; private set; }
    public int InterpolationRenderVersion { get; private set; }
    public double InterpolationAlpha { get; private set; }
    public long SimulationTick { get; private set; }
    public double SimulationTimeSeconds { get; private set; }
    public double FixedUpdatesPerSecond { get; private set; }
    public double SimulationAccumulatorSeconds { get; private set; }
    public double DroppedSimulationSeconds { get; private set; }
    public int LastSimulationStepCount { get; private set; }
    public bool SimulationPaused { get; private set; }
    public bool SimulationFaulted { get; private set; }
    public SceneSimulationMetrics3D SimulationMetrics { get; private set; }

    public static SceneRenderSnapshot3D Capture(Scene3D scene, float aspect, BackendKind backendKind)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var target = new SceneRenderSnapshot3D();
        target.Reset(scene, scene.Registry.GetFrameSnapshot(), aspect, backendKind);
        return target;
    }

    internal static void CaptureInto(
        Scene3D scene,
        float aspect,
        BackendKind backendKind,
        SceneFrameSnapshot3D registryTarget,
        SceneRenderSnapshot3D target)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(registryTarget);
        ArgumentNullException.ThrowIfNull(target);
        scene.Registry.CopyFrameSnapshotInto(registryTarget);
        target.Reset(scene, registryTarget, aspect, backendKind);
    }

    private void Reset(Scene3D scene, SceneFrameSnapshot3D registry, float aspect, BackendKind backendKind)
    {
        Registry = registry;
        var camera = scene.Camera;
        View = camera.GetViewMatrix();
        Projection = camera.GetProjectionMatrix(aspect);
        ViewProjection = View * Projection;
        CameraPosition = camera.Position;
        BackgroundColor = scene.BackgroundColor;
        AmbientLightColor = scene.AmbientLightColor;
        AmbientLightIntensity = scene.AmbientLightIntensity;

        EnsureCapacity(ref _directionalLights, scene.Lights.Count);
        _directionalLightCount = scene.Lights.Count;
        for (var i = 0; i < _directionalLightCount; i++)
        {
            _directionalLights[i] = DirectionalLightSnapshot3D.From(scene.Lights[i]);
        }

        EnsureCapacity(ref _pointLights, scene.PointLights.Count);
        _pointLightCount = scene.PointLights.Count;
        for (var i = 0; i < _pointLightCount; i++)
        {
            _pointLights[i] = PointLightSnapshot3D.From(scene.PointLights[i]);
        }

        EnsureCapacity(ref _spotLights, scene.SpotLights.Count);
        _spotLightCount = scene.SpotLights.Count;
        for (var i = 0; i < _spotLightCount; i++)
        {
            _spotLights[i] = SpotLightSnapshot3D.From(scene.SpotLights[i]);
        }

        Pipeline = RenderPipelinePlanner3D.Plan(scene, backendKind);
        SceneChangeSequence = scene.ChangeSequence;
        BatchContentVersion = scene.BatchContentVersion;
        BatchTransformVersion = scene.BatchTransformVersion;
        ParticleContentVersion = scene.ParticleContentVersion;
        InterpolationRenderVersion = scene.FrameInterpolator.RenderVersion;
        InterpolationAlpha = scene.FrameInterpolator.Alpha;
        var loop = scene.UpdateLoop;
        SimulationTick = loop.SimulationTick;
        SimulationTimeSeconds = loop.SimulationTimeSeconds;
        FixedUpdatesPerSecond = loop.FixedUpdatesPerSecond;
        SimulationAccumulatorSeconds = loop.AccumulatorSeconds;
        DroppedSimulationSeconds = loop.TotalDroppedSeconds;
        LastSimulationStepCount = loop.LastResult.ExecutedSteps;
        SimulationPaused = loop.IsPaused;
        SimulationFaulted = loop.IsFaulted;
        SimulationMetrics = scene.SimulationMetrics;
    }

    private static void EnsureCapacity<T>(ref T[] array, int count)
    {
        if (array.Length >= count) return;
        var capacity = array.Length == 0 ? 4 : array.Length;
        while (capacity < count) capacity = checked(capacity * 2);
        array = new T[capacity];
    }
}

internal readonly record struct DirectionalLightSnapshot3D(Vector3 Direction, ColorRgba Color, float Intensity, bool IsEnabled)
{
    public static DirectionalLightSnapshot3D From(DirectionalLight3D light)
        => new(light.Direction, light.Color, light.Intensity, light.IsEnabled);
}

internal readonly record struct PointLightSnapshot3D(Vector3 Position, ColorRgba Color, float Intensity, float Range, bool IsEnabled)
{
    public static PointLightSnapshot3D From(PointLight3D light)
        => new(light.Position, light.Color, light.Intensity, light.Range, light.IsEnabled);
}

internal readonly record struct SpotLightSnapshot3D(
    Vector3 Position,
    Vector3 Direction,
    ColorRgba Color,
    float Intensity,
    float Range,
    float InnerConeDegrees,
    float OuterConeDegrees,
    bool IsEnabled)
{
    public static SpotLightSnapshot3D From(SpotLight3D light)
        => new(light.Position, light.Direction, light.Color, light.Intensity, light.Range, light.InnerConeDegrees, light.OuterConeDegrees, light.IsEnabled);
}
