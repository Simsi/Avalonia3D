using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

/// <summary>Immutable object state published by the simulation owner.</summary>
public readonly record struct WorldObjectState3D(
    string Id,
    string Name,
    string TypeName,
    string? ParentId,
    Vector3 Position,
    Vector3 RotationDegrees,
    Vector3 Scale,
    Matrix4x4 LocalMatrix,
    Matrix4x4 WorldMatrix,
    Bounds3D WorldBounds,
    bool IsVisible,
    bool IsPickable,
    bool UsesMeshRendering,
    bool HasCollider,
    bool HasRigidbody,
    bool IsKinematic,
    int TransformVersion,
    int GeometryVersion,
    int MaterialVersion);

public readonly record struct WorldCameraState3D(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Up,
    float FieldOfViewDegrees,
    float NearPlane,
    float FarPlane);

public readonly record struct WorldDirectionalLightState3D(
    Vector3 Direction,
    ColorRgba Color,
    float Intensity,
    bool IsEnabled);

public readonly record struct WorldPointLightState3D(
    Vector3 Position,
    ColorRgba Color,
    float Intensity,
    float Range,
    bool IsEnabled);

public readonly record struct WorldSpotLightState3D(
    Vector3 Position,
    Vector3 Direction,
    ColorRgba Color,
    float Intensity,
    float Range,
    float InnerConeDegrees,
    float OuterConeDegrees,
    bool IsEnabled);

/// <summary>
/// Triple-buffered read publication. The object and light spans remain stable for the lifetime
/// of the owning <see cref="WorldReadSnapshotLease3D"/>.
/// </summary>
public sealed class WorldSnapshot3D
{
    private WorldObjectState3D[] _objects = Array.Empty<WorldObjectState3D>();
    private WorldDirectionalLightState3D[] _directionalLights = Array.Empty<WorldDirectionalLightState3D>();
    private WorldPointLightState3D[] _pointLights = Array.Empty<WorldPointLightState3D>();
    private WorldSpotLightState3D[] _spotLights = Array.Empty<WorldSpotLightState3D>();
    private int _objectCount;
    private int _directionalLightCount;
    private int _pointLightCount;
    private int _spotLightCount;

    internal WorldSnapshot3D()
    {
    }

    public long PublicationVersion { get; private set; }
    public long SceneChangeSequence { get; private set; }
    public long RegistryVersion { get; private set; }
    public long SimulationTick { get; private set; }
    public double SimulationTimeSeconds { get; private set; }
    public double InterpolationAlpha { get; private set; }
    public bool SimulationPaused { get; private set; }
    public bool SimulationFaulted { get; private set; }
    public ColorRgba BackgroundColor { get; private set; }
    public ColorRgba AmbientLightColor { get; private set; }
    public float AmbientLightIntensity { get; private set; }
    public WorldCameraState3D Camera { get; private set; }
    public ReadOnlyMemory<WorldObjectState3D> Objects => _objects.AsMemory(0, _objectCount);
    public ReadOnlyMemory<WorldDirectionalLightState3D> DirectionalLights => _directionalLights.AsMemory(0, _directionalLightCount);
    public ReadOnlyMemory<WorldPointLightState3D> PointLights => _pointLights.AsMemory(0, _pointLightCount);
    public ReadOnlyMemory<WorldSpotLightState3D> SpotLights => _spotLights.AsMemory(0, _spotLightCount);

    internal void Capture(Scene3D scene, long publicationVersion)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var registry = scene.Registry.GetFrameSnapshot();
        var allObjects = registry.AllObjectsInternal;
        EnsureCapacity(ref _objects, allObjects.Length);
        _objectCount = allObjects.Length;
        for (var i = 0; i < allObjects.Length; i++)
        {
            var obj = allObjects[i];
            var body = obj.Rigidbody;
            _objects[i] = new WorldObjectState3D(
                obj.Id,
                obj.Name,
                obj.GetType().FullName ?? obj.GetType().Name,
                obj.Parent?.Id,
                obj.Position,
                obj.RotationDegrees,
                obj.Scale,
                obj.LocalMatrix,
                obj.GetModelMatrix(),
                obj.GetWorldBounds(),
                obj.IsVisible,
                obj.IsPickable,
                obj.UseMeshRendering,
                obj.Collider is not null,
                body is not null,
                body?.IsKinematic ?? false,
                obj.TransformVersion,
                obj.GeometryVersion,
                obj.MaterialVersion);
        }

        var camera = scene.Camera;
        Camera = new WorldCameraState3D(
            camera.Position,
            camera.Forward,
            camera.Up,
            camera.FieldOfViewDegrees,
            camera.NearPlane,
            camera.FarPlane);

        EnsureCapacity(ref _directionalLights, scene.Lights.Count);
        _directionalLightCount = scene.Lights.Count;
        for (var i = 0; i < _directionalLightCount; i++)
        {
            var light = scene.Lights[i];
            _directionalLights[i] = new WorldDirectionalLightState3D(light.Direction, light.Color, light.Intensity, light.IsEnabled);
        }

        EnsureCapacity(ref _pointLights, scene.PointLights.Count);
        _pointLightCount = scene.PointLights.Count;
        for (var i = 0; i < _pointLightCount; i++)
        {
            var light = scene.PointLights[i];
            _pointLights[i] = new WorldPointLightState3D(light.Position, light.Color, light.Intensity, light.Range, light.IsEnabled);
        }

        EnsureCapacity(ref _spotLights, scene.SpotLights.Count);
        _spotLightCount = scene.SpotLights.Count;
        for (var i = 0; i < _spotLightCount; i++)
        {
            var light = scene.SpotLights[i];
            _spotLights[i] = new WorldSpotLightState3D(
                light.Position,
                light.Direction,
                light.Color,
                light.Intensity,
                light.Range,
                light.InnerConeDegrees,
                light.OuterConeDegrees,
                light.IsEnabled);
        }

        PublicationVersion = publicationVersion;
        SceneChangeSequence = scene.ChangeSequence;
        RegistryVersion = registry.RegistryVersion;
        SimulationTick = scene.UpdateLoop.SimulationTick;
        SimulationTimeSeconds = scene.UpdateLoop.SimulationTimeSeconds;
        InterpolationAlpha = scene.FrameInterpolator.Alpha;
        SimulationPaused = scene.UpdateLoop.IsPaused;
        SimulationFaulted = scene.UpdateLoop.IsFaulted;
        BackgroundColor = scene.BackgroundColor;
        AmbientLightColor = scene.AmbientLightColor;
        AmbientLightIntensity = scene.AmbientLightIntensity;
    }

    private static void EnsureCapacity<T>(ref T[] target, int required)
    {
        if (target.Length >= required) return;
        var capacity = target.Length == 0 ? 4 : target.Length;
        while (capacity < required) capacity = checked(capacity * 2);
        Array.Resize(ref target, capacity);
    }
}
