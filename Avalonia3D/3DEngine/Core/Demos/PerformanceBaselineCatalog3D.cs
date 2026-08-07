using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Instancing;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Demos;

public sealed record PerformanceBaselineOptions3D
{
    public int OrdinaryGridSide { get; init; } = 32;
    public int HighScaleInstanceCount { get; init; } = 25_000;
    public int UpdatesPerFrame { get; init; } = 512;
    public float ObjectSpacing { get; init; } = 1.35f;

    public static PerformanceBaselineOptions3D FastValidation { get; } = new()
    {
        OrdinaryGridSide = 8,
        HighScaleInstanceCount = 512,
        UpdatesPerFrame = 32
    };

    internal PerformanceBaselineOptions3D Validate() => this with
    {
        OrdinaryGridSide = global::System.Math.Clamp(OrdinaryGridSide, 2, 256),
        HighScaleInstanceCount = global::System.Math.Clamp(HighScaleInstanceCount, 1, 2_000_000),
        UpdatesPerFrame = global::System.Math.Clamp(UpdatesPerFrame, 1, 65_536),
        ObjectSpacing = global::System.Math.Clamp(ObjectSpacing, 0.1f, 100f)
    };
}

/// <summary>
/// A deterministic demo workload. Build is called once for a fresh scene; Update applies
/// a repeatable per-frame mutation pattern without reading wall-clock time.
/// </summary>
public interface IPerformanceBaselineScene3D : IDemoScene3D
{
    int ExpectedLogicalItemCount { get; }
    void Update(float elapsedSeconds);
}

public static class PerformanceBaselineCatalog3D
{
    public static DemoSceneCatalog3D Create(PerformanceBaselineOptions3D? options = null)
    {
        var validated = (options ?? new PerformanceBaselineOptions3D()).Validate();
        var catalog = new DemoSceneCatalog3D();
        catalog.Add(new OrdinaryTransformBaselineScene3D(validated));
        catalog.Add(new HighScaleMutationBaselineScene3D(validated));
        return catalog;
    }
}

public sealed class OrdinaryTransformBaselineScene3D : IPerformanceBaselineScene3D
{
    private readonly PerformanceBaselineOptions3D _options;
    private readonly List<Object3D> _dynamicObjects = new();

    public OrdinaryTransformBaselineScene3D(PerformanceBaselineOptions3D? options = null)
    {
        _options = (options ?? new PerformanceBaselineOptions3D()).Validate();
    }

    public DemoSceneDescriptor3D Descriptor { get; } = new(
        "baseline-ordinary-transform",
        "Ordinary transform baseline",
        "A single instancing-compatible batch of ordinary objects with deterministic transform updates.",
        new[] { "baseline", "ordinary", "transform", "cpu-plan" });

    public int ExpectedLogicalItemCount => _options.OrdinaryGridSide * _options.OrdinaryGridSide;

    public void Build(Scene3D scene, DemoSceneContext3D context)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        _dynamicObjects.Clear();
        scene.Clear();
        ConfigureScene(scene, _options.OrdinaryGridSide * _options.ObjectSpacing);

        var material = Material3D.CreateLambert(new ColorRgba(0.18f, 0.55f, 0.92f, 1f));
        var half = (_options.OrdinaryGridSide - 1) * 0.5f;
        using (scene.BeginUpdate())
        {
            for (var z = 0; z < _options.OrdinaryGridSide; z++)
            {
                for (var x = 0; x < _options.OrdinaryGridSide; x++)
                {
                    var index = z * _options.OrdinaryGridSide + x;
                    var box = new Box3D
                    {
                        Name = "OrdinaryBaseline_" + index,
                        Width = 0.82f,
                        Height = 0.82f,
                        Depth = 0.82f,
                        Position = new Vector3((x - half) * _options.ObjectSpacing, 0f, (z - half) * _options.ObjectSpacing),
                        Material = material,
                        Collider = null,
                        IsPickable = false
                    };
                    scene.Add(box);
                    _dynamicObjects.Add(box);
                }
            }
        }

        context.ReportStatus($"Built ordinary baseline with {_dynamicObjects.Count} objects.");
    }

    public void Update(float elapsedSeconds)
    {
        if (_dynamicObjects.Count == 0) return;
        var updateCount = global::System.Math.Min(_options.UpdatesPerFrame, _dynamicObjects.Count);
        for (var i = 0; i < updateCount; i++)
        {
            var obj = _dynamicObjects[i];
            var phase = elapsedSeconds * 1.7f + i * 0.071f;
            var position = obj.Position;
            position.Y = MathF.Sin(phase) * 0.35f;
            obj.Position = position;
            obj.RotationDegrees = new Vector3(0f, phase * 24f, 0f);
        }
    }

    private static void ConfigureScene(Scene3D scene, float extent)
    {
        scene.BackgroundColor = new ColorRgba(0.015f, 0.02f, 0.035f, 1f);
        scene.AmbientLightColor = new ColorRgba(0.45f, 0.52f, 0.65f, 1f);
        scene.AmbientLightIntensity = 0.38f;
        scene.Camera.Position = new Vector3(0f, extent * 0.7f, -extent * 0.9f);
        scene.Camera.Target = Vector3.Zero;
        scene.Camera.FarPlane = global::System.Math.Max(200f, extent * 4f);
        scene.AddLight(new DirectionalLight3D
        {
            Direction = Vector3.Normalize(new Vector3(-0.45f, -1f, -0.35f)),
            Intensity = 1.2f
        });
    }
}

public sealed class HighScaleMutationBaselineScene3D : IPerformanceBaselineScene3D
{
    private readonly PerformanceBaselineOptions3D _options;
    private Vector3[] _basePositions = Array.Empty<Vector3>();
    private InstancedMesh3D? _layer;
    private int _updateCursor;

    public HighScaleMutationBaselineScene3D(PerformanceBaselineOptions3D? options = null)
    {
        _options = (options ?? new PerformanceBaselineOptions3D()).Validate();
    }

    public DemoSceneDescriptor3D Descriptor { get; } = new(
        "baseline-high-scale-mutation",
        "High-scale mutation baseline",
        "A dense instanced layer with deterministic transform and state patches.",
        new[] { "baseline", "high-scale", "instancing", "gpu-buffer" });

    public int ExpectedLogicalItemCount => _options.HighScaleInstanceCount;

    public void Build(Scene3D scene, DemoSceneContext3D context)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        scene.Clear();
        _updateCursor = 0;
        var gridSide = (int)MathF.Ceiling(MathF.Sqrt(_options.HighScaleInstanceCount));
        var extent = gridSide * _options.ObjectSpacing;
        scene.BackgroundColor = new ColorRgba(0.01f, 0.014f, 0.025f, 1f);
        scene.AmbientLightIntensity = 0.42f;
        scene.Camera.Position = new Vector3(0f, extent * 0.72f, -extent * 0.88f);
        scene.Camera.Target = Vector3.Zero;
        scene.Camera.FarPlane = global::System.Math.Max(500f, extent * 4f);
        scene.AddLight(new DirectionalLight3D { Intensity = 1.25f });

        var mesh = MeshFactory.CreateExtrudedRectangle(0.78f, 0.78f, 0.78f);
        _layer = new InstancedMesh3D(
            "HighScaleBaseline",
            mesh,
            Material3D.CreateLambert(new ColorRgba(0.16f, 0.78f, 0.48f, 1f)),
            initialCapacity: _options.HighScaleInstanceCount,
            chunkCellSize: 24f);
        _layer.Template.AddMaterialVariant(1, "Hot")
            .SetPartColor(0, new ColorRgba(1f, 0.32f, 0.12f, 1f));

        _basePositions = new Vector3[_options.HighScaleInstanceCount];
        var half = (gridSide - 1) * 0.5f;
        for (var i = 0; i < _options.HighScaleInstanceCount; i++)
        {
            var x = i % gridSide;
            var z = i / gridSide;
            var position = new Vector3((x - half) * _options.ObjectSpacing, 0f, (z - half) * _options.ObjectSpacing);
            _basePositions[i] = position;
            _layer.AddInstance(position, materialVariantId: 0);
        }

        scene.Add(_layer);
        context.ReportStatus($"Built high-scale baseline with {_layer.Instances.Count} instances in {_layer.Chunks.Chunks.Count} chunks.");
    }

    public void Update(float elapsedSeconds)
    {
        var layer = _layer;
        if (layer is null || layer.Instances.Count == 0) return;
        var updateCount = global::System.Math.Min(_options.UpdatesPerFrame, layer.Instances.Count);
        using var batch = layer.BeginTelemetryBatch();
        for (var offset = 0; offset < updateCount; offset++)
        {
            var index = (_updateCursor + offset) % layer.Instances.Count;
            var basePosition = _basePositions[index];
            var phase = elapsedSeconds * 1.9f + index * 0.017f;
            var position = basePosition + Vector3.UnitY * (MathF.Sin(phase) * 0.3f);
            var transform = Matrix4x4.CreateRotationY(phase * 0.35f) * Matrix4x4.CreateTranslation(position);
            batch.SetTransform(index, transform);
            batch.SetMaterialVariant(index, MathF.Sin(phase) > 0.72f ? 1 : 0);
        }

        _updateCursor = (_updateCursor + updateCount) % layer.Instances.Count;
    }
}

