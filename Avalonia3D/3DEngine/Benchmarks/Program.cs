#if AVALONIA3D_BENCHMARK_HOST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using ThreeDEngine.Core.Demos;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Rhi;
using ThreeDEngine.Core.Resources;
using ThreeDEngine.Core.Scene;

namespace Avalonia3D.Engine.Benchmarks;

internal static class Program
{
    private const int WarmupIterations = 5;
    private const int DefaultMeasuredIterations = 30;
    private static long _consumer;

    public static int Main(string[] args)
    {
        var outputDirectory = ReadOption(args, "--output") ?? Path.Combine("Artifacts", "Baseline");
        var policyPath = ReadOption(args, "--policy") ?? Path.Combine("Baselines", "baseline-policy.json");
        var referencePath = ReadOption(args, "--reference");
        var resultName = ReadOption(args, "--result-name");
        var iterations = global::System.Math.Max(DefaultMeasuredIterations, ReadIntegerOption(args, "--iterations", DefaultMeasuredIterations));
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Avalonia3D CPU baseline: {iterations} measured iterations, {WarmupIterations} warmup iterations.");
        using var ordinary = BuildOrdinaryScene();
        using var incremental = BuildOrdinaryScene();
        using var transparent = BuildTransparentScene();
        using var highScale = BuildHighScaleScene(out var highScaleLayer);
        using var updateScene = new Scene3D(Scene3DOptions.WithoutPhysics());
        using var journalScene = new Scene3D(Scene3DOptions.WithoutPhysics());
        var journalObject = journalScene.Add(new Box3D());
        for (var i = 0; i < SceneChangeJournal3D.DefaultCapacity + 1024; i++)
        {
            journalObject.Position = new Vector3((i & 1) == 0 ? 0.001f : -0.001f, 0f, 0f);
        }
        var journalCursor = journalScene.ChangeSequence;
        var journalRecords = new List<SceneChangeRecord3D>(4);
        var journalMutation = 0;
        using var compositionEngine = new Engine3DBuilder().DisablePhysicsByDefault().Build();
        using var resourceEngine = new Engine3DBuilder().DisablePhysicsByDefault().Build();
        using var resourceOwner = resourceEngine.Resources.CreateOwner("benchmark-resource-owner");
        var resourceTextures = CreateResourceTextures(1000);
        var resourceTexturesHalf = resourceTextures.Take(500).ToArray();
        var resourceOwnerMutation = 0;
        var frameScratch = new SceneRenderFrameScratch3D();
        var ordinaryScratch = new SceneRenderPlanScratch3D();
        var highScaleScratch = new SceneRenderPlanScratch3D();
        var transparentScratch = new SceneRenderPlanScratch3D();
        var transformChanges = new List<Object3D>(4);
        var ordinaryObject = ordinary.Objects[0];
        var incrementalObject = incremental.Objects[0];
        var transparentObject = transparent.Objects[0];
        var transparentMutation = 0;
        var transactionObjects = incremental.Objects.Take(512).ToArray();
        var lastTransformVersion = ordinary.BatchTransformVersion;
        var ordinaryMutation = 0;
        var stateMutation = 0;
        var transformMutation = 0;
        var transformUpdateCount = global::System.Math.Min(512, highScaleLayer.Instances.Count);
        var originalTransforms = new Matrix4x4[transformUpdateCount];
        for (var i = 0; i < transformUpdateCount; i++) originalTransforms[i] = highScaleLayer.Instances[i].Transform;
        var uploadMesh = MeshFactory.CreateSphere(0.5f, 64, 32);
        var uploadView = uploadMesh.RenderGeometry.GetInterleavedVertexBuffer();
        var meshletView = uploadMesh.RenderGeometry.GetMeshlets();
        using var rhiDevice = new RhiDevice3D(new RhiDeviceCapabilities3D(
            RhiBackendApi3D.OpenGl,
            "benchmark",
            "4.5",
            RhiDeviceCapabilities3D.RequiredRasterFeatures,
            new RhiDeviceLimits3D(8192, 32, 8, 16, 8192, 8)));
        var rhiKeys = Enumerable.Range(0, 1000).Select(static i => "benchmark:buffer:" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var rhiDescriptor = new RhiBufferDescriptor3D(4096, RhiBufferUsage3D.Vertex | RhiBufferUsage3D.Dynamic, 16);

        var results = new List<BenchmarkResult3D>
        {
            Measure("engine-service-resolve-1000", iterations, () =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    Consume(compositionEngine.Services.GetRequiredService<ThreeDEngine.Core.Assets.Models.ModelAssetCache3D>().Count);
                }
            }),
            Measure("geometry-upload-view-cached-1000", iterations, () =>
            {
                long bytes = 0;
                for (var i = 0; i < 1000; i++)
                {
                    var view = uploadMesh.RenderGeometry.GetInterleavedVertexBuffer();
                    bytes += view.ByteCount + (ReferenceEquals(view, uploadView) ? 1 : 0);
                }
                Consume(bytes);
            }),
            Measure("geometry-meshlet-view-cached-1000", iterations, () =>
            {
                long count = 0;
                for (var i = 0; i < 1000; i++)
                {
                    var view = uploadMesh.RenderGeometry.GetMeshlets();
                    count += view.Count + (ReferenceEquals(view, meshletView) ? 1 : 0);
                }
                Consume(count);
            }),
            Measure("geometry-bulk-builder-100", iterations, () =>
            {
                long versions = 0;
                for (var i = 0; i < 100; i++)
                {
                    var builder = new MeshGeometryBuilder3D(3, 3, GeometryStreamMask3D.None);
                    builder.Positions[0] = Vector3.Zero;
                    builder.Positions[1] = Vector3.UnitX;
                    builder.Positions[2] = Vector3.UnitY;
                    builder.Indices[0] = 0;
                    builder.Indices[1] = 1;
                    builder.Indices[2] = 2;
                    versions += builder.Build("benchmark:builder:" + i.ToString(CultureInfo.InvariantCulture)).GeometryVersion;
                }
                Consume(versions);
            }),
            Measure("geometry-lod-generate-1", iterations, () =>
            {
                var lod = MeshLodGenerator3D.Generate(uploadMesh, 0.35f, "benchmark:lod");
                Consume(lod.RenderGeometry.TriangleCount + lod.Positions.Length);
            }),
            Measure("resource-content-intern-1000", iterations, () =>
            {
                long bytes = 0;
                for (var i = 0; i < resourceTextures.Length; i++)
                {
                    bytes += resourceEngine.Resources.Intern(resourceTextures[i]).ByteLength;
                }
                Consume(bytes + resourceEngine.Resources.TextureCount);
            }),
            Measure("resource-owner-sync-1000", iterations, () =>
            {
                resourceOwnerMutation++;
                resourceOwner.SetTextures((resourceOwnerMutation & 1) == 0 ? resourceTextures : resourceTexturesHalf);
                Consume(resourceEngine.Resources.ResidentTextureBytes + resourceEngine.Resources.OwnerCount);
            }),
            Measure("rhi-resource-register-release-1000", iterations, () =>
            {
                for (var i = 0; i < rhiKeys.Length; i++) rhiDevice.Resources.RegisterBuffer(rhiKeys[i], rhiDescriptor, i + 1);
                var snapshot = rhiDevice.Resources.CaptureSnapshot();
                for (var i = 0; i < rhiKeys.Length; i++) rhiDevice.Resources.Release(rhiKeys[i], RhiResourceKind3D.Buffer);
                Consume(snapshot.ResidentBytes + snapshot.LiveCount);
            }),
            Measure("fixed-update-empty-1000", iterations, () =>
            {
                for (var i = 0; i < 1000; i++) updateScene.Update(SceneUpdateLoop3D.DefaultFixedDeltaSeconds);
                Consume(updateScene.UpdateLoop.SimulationTick);
            }),
            Measure("simulation-command-drain-1000", iterations, () =>
            {
                for (var i = 0; i < 1000; i++) updateScene.Commands.Enqueue(static scene => Consume(scene.ChangeSequence));
                var drained = updateScene.UpdateLoop.PumpCommands();
                Consume(drained + updateScene.Commands.LastCompletedSequence);
            }),
            Measure("scene-transaction-empty-1000", iterations, () =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    using var update = updateScene.BeginUpdate();
                }
                Consume(updateScene.ChangeSequence);
            }),
            Measure("scene-journal-copy-tail-1", iterations, () =>
            {
                journalMutation++;
                journalObject.Position = new Vector3((journalMutation & 1) == 0 ? 0.002f : -0.002f, 0f, 0f);
                var copied = journalScene.TryCopyChangesSince(journalCursor, journalRecords);
                journalCursor = journalScene.ChangeSequence;
                Consume((copied ? 1 : 0) + journalRecords.Count);
            }),
            Measure("render-frame-publication-1024", iterations, () =>
            {
                using var frame = frameScratch.Begin(ordinary, 1920f, 1080f, BackendKind.OpenGlDesktop);
                Consume(frame.Snapshot.RenderablesInternal.Length + frame.Published.DirectionalLights.Length);
            }),
            Measure("ordinary-plan-1024", iterations, () =>
            {
                using var frame = frameScratch.Begin(ordinary, 1920f, 1080f, BackendKind.OpenGlDesktop);
                var plan = SceneRenderPlanBuilder3D.Build(frame, ordinaryScratch);
                Consume(plan.DrawCommands.Count + plan.Resources.Meshes.Count);
            }),
            Measure("ordinary-single-transform-1024", iterations, () =>
            {
                ordinaryMutation++;
                ordinaryObject.Position = new Vector3((ordinaryMutation & 1) == 0 ? 0.01f : -0.01f, 0f, 0f);
                var copied = ordinary.TryCopyBatchTransformChangesSince(lastTransformVersion, transformChanges);
                lastTransformVersion = ordinary.BatchTransformVersion;
                Consume((copied ? 1 : 0) + transformChanges.Count);
            }),
            Measure("transparent-camera-plan-256", iterations, () =>
            {
                transparent.Camera.Translate(new Vector3(0.002f, 0f, 0f));
                using var frame = frameScratch.Begin(transparent, 1920f, 1080f, BackendKind.WebGlBrowser);
                var plan = SceneRenderPlanBuilder3D.Build(frame, transparentScratch);
                Consume(plan.TransparentOrdinaryItems.Count + plan.DrawCommands.Count);
            }),
            Measure("transparent-camera-transform-plan-256", iterations, () =>
            {
                transparentMutation++;
                transparentObject.Position = new Vector3(0f, (transparentMutation & 1) == 0 ? 0.01f : -0.01f, 0f);
                transparent.Camera.Translate(new Vector3(0.002f, 0f, 0f));
                using var frame = frameScratch.Begin(transparent, 1920f, 1080f, BackendKind.WebGlBrowser);
                var plan = SceneRenderPlanBuilder3D.Build(frame, transparentScratch);
                Consume(plan.TransparentOrdinaryItems.Count + plan.DrawCommands.Count);
            }),
            Measure("registry-visibility-patch-1024", iterations, () =>
            {
                incrementalObject.IsVisible = !incrementalObject.IsVisible;
                Consume(incremental.Registry.Version + incremental.Registry.Renderables.Count);
            }),
            Measure("scene-transaction-transform-512", iterations, () =>
            {
                ordinaryMutation++;
                var y = (ordinaryMutation & 1) == 0 ? 0.02f : -0.02f;
                using var update = incremental.BeginUpdate();
                for (var i = 0; i < transactionObjects.Length; i++)
                {
                    var position = transactionObjects[i].Position;
                    position.Y = y;
                    transactionObjects[i].Position = position;
                }
                Consume(incremental.ChangeSequence);
            }),
            Measure("high-scale-plan-25000", iterations, () =>
            {
                using var frame = frameScratch.Begin(highScale, 1920f, 1080f, BackendKind.OpenGlDesktop);
                var plan = SceneRenderPlanBuilder3D.Build(frame, highScaleScratch);
                Consume(plan.HighScaleLayers.Count + plan.Resources.Meshes.Count);
            }),
            Measure("high-scale-state-patch-512", iterations, () =>
            {
                stateMutation++;
                using var batch = highScaleLayer.BeginTelemetryBatch();
                var variant = stateMutation & 1;
                for (var i = 0; i < transformUpdateCount; i++) batch.SetMaterialVariant(i, variant);
                Consume(highScaleLayer.StateBuffer.Version);
            }),
            Measure("high-scale-transform-patch-512", iterations, () =>
            {
                transformMutation++;
                var y = (transformMutation & 1) == 0 ? 0.1f : -0.1f;
                using var batch = highScaleLayer.BeginTelemetryBatch();
                for (var i = 0; i < transformUpdateCount; i++)
                {
                    var transform = originalTransforms[i];
                    transform.M42 += y;
                    batch.SetTransform(i, transform);
                }
                Consume(highScaleLayer.Instances.Version);
            })
        };

        var report = new BenchmarkReport3D(
            SchemaVersion: 1,
            CreatedUtc: DateTimeOffset.UtcNow,
            EngineVersion: typeof(Scene3D).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            Framework: RuntimeInformation.FrameworkDescription,
            OperatingSystem: RuntimeInformation.OSDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ServerGc: GCSettings.IsServerGC,
            WarmupIterations: WarmupIterations,
            MeasuredIterations: iterations,
            Results: results);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileStem = string.IsNullOrWhiteSpace(resultName) ? $"avalonia3d-baseline_{stamp}" : SanitizeFileStem(resultName);
        var jsonPath = Path.Combine(outputDirectory, fileStem + ".json");
        var markdownPath = Path.Combine(outputDirectory, fileStem + ".md");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, jsonOptions));
        File.WriteAllText(markdownPath, ToMarkdown(report));

        var validation = Validate(report, policyPath, referencePath, jsonOptions);
        var validationPath = Path.Combine(outputDirectory, fileStem + ".validation.txt");
        File.WriteAllText(validationPath, validation.Report);

        Console.WriteLine(ToMarkdown(report));
        Console.WriteLine(validation.Report);
        Console.WriteLine("JSON: " + Path.GetFullPath(jsonPath));
        Console.WriteLine("Markdown: " + Path.GetFullPath(markdownPath));
        Console.WriteLine("Validation: " + Path.GetFullPath(validationPath));
        Consume(_consumer);
        return validation.Passed ? 0 : 2;
    }


    private static BaselineValidationResult3D Validate(
        BenchmarkReport3D report,
        string policyPath,
        string? referencePath,
        JsonSerializerOptions jsonOptions)
    {
        var messages = new List<string>();
        var passed = true;
        var fullPolicyPath = Path.GetFullPath(policyPath);
        if (!File.Exists(fullPolicyPath))
        {
            return new BaselineValidationResult3D(false, $"BASELINE FAIL: policy file was not found: {fullPolicyPath}");
        }

        var policy = JsonSerializer.Deserialize<BaselinePolicy3D>(File.ReadAllText(fullPolicyPath), jsonOptions)
            ?? throw new InvalidDataException("Unable to deserialize baseline policy.");
        if (policy.SchemaVersion != 1)
        {
            passed = false;
            messages.Add($"Unsupported policy schema {policy.SchemaVersion}; expected 1.");
        }
        if (!string.Equals(policy.EngineVersion, report.EngineVersion, StringComparison.Ordinal))
        {
            passed = false;
            messages.Add($"Engine version mismatch: policy={policy.EngineVersion}, report={report.EngineVersion}.");
        }
        if (report.MeasuredIterations < policy.MinimumMeasuredIterations)
        {
            passed = false;
            messages.Add($"Measured iterations {report.MeasuredIterations} are below required {policy.MinimumMeasuredIterations}.");
        }

        var byId = new Dictionary<string, BenchmarkResult3D>(StringComparer.Ordinal);
        foreach (var result in report.Results)
        {
            if (!byId.TryAdd(result.Id, result))
            {
                passed = false;
                messages.Add($"Duplicate workload id '{result.Id}'.");
            }
        }
        foreach (var required in policy.RequiredWorkloads)
        {
            if (!byId.ContainsKey(required))
            {
                passed = false;
                messages.Add($"Required workload '{required}' is missing.");
            }
        }

        if (!string.IsNullOrWhiteSpace(referencePath))
        {
            var fullReferencePath = Path.GetFullPath(referencePath);
            if (!File.Exists(fullReferencePath))
            {
                passed = false;
                messages.Add($"Reference baseline was not found: {fullReferencePath}.");
            }
            else
            {
                var reference = JsonSerializer.Deserialize<BenchmarkReport3D>(File.ReadAllText(fullReferencePath), jsonOptions)
                    ?? throw new InvalidDataException("Unable to deserialize reference baseline.");
                var referenceById = reference.Results.ToDictionary(static item => item.Id, StringComparer.Ordinal);
                foreach (var required in policy.RequiredWorkloads)
                {
                    if (!byId.TryGetValue(required, out var current) || !referenceById.TryGetValue(required, out var previous))
                    {
                        passed = false;
                        messages.Add($"Reference comparison cannot resolve workload '{required}'.");
                        continue;
                    }

                    var p95Ratio = Ratio(current.P95Milliseconds, previous.P95Milliseconds);
                    if (p95Ratio > policy.MaximumAllowedRegressionRatio)
                    {
                        passed = false;
                        messages.Add($"{required}: p95 regression {p95Ratio:0.###}x exceeds {policy.MaximumAllowedRegressionRatio:0.###}x.");
                    }

                    var allocationRatio = Ratio(current.AllocatedBytesPerIteration, previous.AllocatedBytesPerIteration);
                    if (allocationRatio > policy.MaximumAllowedRegressionRatio && current.AllocatedBytesPerIteration > previous.AllocatedBytesPerIteration + 16d)
                    {
                        passed = false;
                        messages.Add($"{required}: allocation regression {allocationRatio:0.###}x exceeds {policy.MaximumAllowedRegressionRatio:0.###}x.");
                    }
                }
            }
        }

        if (messages.Count == 0) messages.Add("All required workloads and baseline constraints passed.");
        var prefix = passed ? "BASELINE PASS" : "BASELINE FAIL";
        return new BaselineValidationResult3D(passed, prefix + Environment.NewLine + string.Join(Environment.NewLine, messages.Select(static message => "- " + message)));
    }

    private static double Ratio(double current, double previous)
    {
        if (previous <= 0d) return current <= 0d ? 1d : double.PositiveInfinity;
        return current / previous;
    }

    private static string SanitizeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }
        return builder.Length == 0 ? "avalonia3d-baseline" : builder.ToString();
    }

    private static TextureResource3D[] CreateResourceTextures(int count)
    {
        var textures = new TextureResource3D[count];
        for (var i = 0; i < textures.Length; i++)
        {
            var payload = new byte[32];
            BitConverter.TryWriteBytes(payload.AsSpan(0, sizeof(int)), i);
            var mixed = unchecked((long)(0x9e3779b97f4a7c15UL * (ulong)(i + 1)));
            BitConverter.TryWriteBytes(payload.AsSpan(sizeof(int), sizeof(long)), mixed);
            textures[i] = TextureResource3D.Create($"benchmark:texture:{i}", payload, "application/x-avalonia3d-benchmark");
        }
        return textures;
    }

    private static Scene3D BuildOrdinaryScene()
    {
        var catalog = PerformanceBaselineCatalog3D.Create(new PerformanceBaselineOptions3D
        {
            OrdinaryGridSide = 32,
            HighScaleInstanceCount = 1,
            UpdatesPerFrame = 1
        });
        var workload = catalog.Demos.OfType<OrdinaryTransformBaselineScene3D>().Single();
        var scene = new Scene3D();
        workload.Build(scene, new DemoSceneContext3D());
        return scene;
    }

    private static Scene3D BuildHighScaleScene(out HighScaleInstanceLayer3D layer)
    {
        var catalog = PerformanceBaselineCatalog3D.Create(new PerformanceBaselineOptions3D
        {
            OrdinaryGridSide = 2,
            HighScaleInstanceCount = 25_000,
            UpdatesPerFrame = 512
        });
        var workload = catalog.Demos.OfType<HighScaleMutationBaselineScene3D>().Single();
        var scene = new Scene3D();
        workload.Build(scene, new DemoSceneContext3D());
        layer = scene.Objects.OfType<HighScaleInstanceLayer3D>().Single();
        return scene;
    }

    private static Scene3D BuildTransparentScene()
    {
        var scene = new Scene3D(Scene3DOptions.WithoutPhysics());
        var material = new Material3D
        {
            BaseColor = new ColorRgba(0.2f, 0.65f, 0.95f, 0.5f),
            Opacity = 0.5f,
            Surface = SurfaceMode.Transparent
        };
        using var update = scene.BeginUpdate();
        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
            {
                scene.Add(new Box3D
                {
                    Position = new Vector3((x - 7.5f) * 1.2f, 0f, (z - 7.5f) * 1.2f),
                    Material = material
                });
            }
        }
        return scene;
    }

    private static BenchmarkResult3D Measure(string id, int iterations, Action action)
    {
        for (var i = 0; i < WarmupIterations; i++) action();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var samples = new double[iterations];
        long allocatedBytes = 0;
        for (var i = 0; i < iterations; i++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            action();
            samples[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }

        Array.Sort(samples);
        var mean = samples.Average();
        var median = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        var result = new BenchmarkResult3D(
            id,
            iterations,
            mean,
            median,
            p95,
            samples[0],
            samples[^1],
            allocatedBytes / (double)iterations);
        Console.WriteLine($"{id}: mean={mean:0.###} ms, p95={p95:0.###} ms, alloc={result.AllocatedBytesPerIteration:0} B/op");
        return result;
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0d;
        var index = (sorted.Length - 1) * percentile;
        var lower = (int)global::System.Math.Floor(index);
        var upper = (int)global::System.Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        var fraction = index - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static string ToMarkdown(BenchmarkReport3D report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Avalonia3D CPU baseline");
        builder.AppendLine();
        builder.AppendLine($"- Created UTC: `{report.CreatedUtc:O}`");
        builder.AppendLine($"- Engine: `{report.EngineVersion}`");
        builder.AppendLine($"- Runtime: `{report.Framework}`");
        builder.AppendLine($"- OS: `{report.OperatingSystem}`");
        builder.AppendLine($"- Architecture: `{report.ProcessArchitecture}`");
        builder.AppendLine($"- Iterations: `{report.MeasuredIterations}` measured / `{report.WarmupIterations}` warmup");
        builder.AppendLine();
        builder.AppendLine("| Workload | Mean ms | Median ms | P95 ms | Min ms | Max ms | B/op |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var result in report.Results)
        {
            builder.Append("|").Append(result.Id)
                .Append('|').Append(result.MeanMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append('|').Append(result.MedianMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append('|').Append(result.P95Milliseconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append('|').Append(result.MinimumMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append('|').Append(result.MaximumMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append('|').Append(result.AllocatedBytesPerIteration.ToString("0", CultureInfo.InvariantCulture))
                .AppendLine("|");
        }
        return builder.ToString();
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
        }
        return null;
    }

    private static int ReadIntegerOption(string[] args, string name, int fallback)
        => int.TryParse(ReadOption(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static void Consume(long value) => Interlocked.Add(ref _consumer, value);
}

internal sealed record BenchmarkResult3D(
    string Id,
    int Iterations,
    double MeanMilliseconds,
    double MedianMilliseconds,
    double P95Milliseconds,
    double MinimumMilliseconds,
    double MaximumMilliseconds,
    double AllocatedBytesPerIteration);

internal sealed record BenchmarkReport3D(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    string EngineVersion,
    string Framework,
    string OperatingSystem,
    string ProcessArchitecture,
    bool ServerGc,
    int WarmupIterations,
    int MeasuredIterations,
    IReadOnlyList<BenchmarkResult3D> Results);

internal sealed record BaselinePolicy3D(
    int SchemaVersion,
    string EngineVersion,
    int MinimumMeasuredIterations,
    double MaximumAllowedRegressionRatio,
    bool FailOnMissingWorkload,
    IReadOnlyList<string> RequiredWorkloads,
    string? Notes);

internal sealed record BaselineValidationResult3D(bool Passed, string Report);
#endif
