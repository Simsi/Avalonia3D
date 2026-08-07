#if AVALONIA3D_TEST_HOST
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Demos;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Instancing;
using ThreeDEngine.Core.Importers.Gltf;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Physics.Jitter2;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Rhi;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.World;

namespace Avalonia3D.Engine.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        var failures = new List<string>();
        Run("Core self-tests", failures, () =>
        {
            var result = Avalonia3DSelfTestRunner.RunAll();
            Console.WriteLine(result.ToReport());
            Expect(result.Passed, result.ToReport());
        });

        Run("Performance baseline catalog", failures, () =>
        {
            var catalog = PerformanceBaselineCatalog3D.Create(PerformanceBaselineOptions3D.FastValidation);
            Expect(catalog.Demos.Count == 2, "Exactly two baseline workloads must be registered.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var demo in catalog.Demos)
            {
                Expect(ids.Add(demo.Descriptor.Id), "Baseline IDs must be unique.");
                using var scene = new Scene3D();
                demo.Build(scene, new DemoSceneContext3D());
                Expect(scene.Objects.Count > 0, $"Baseline '{demo.Descriptor.Id}' produced an empty scene.");
                if (demo is IPerformanceBaselineScene3D workload)
                {
                    workload.Update(1.25f);
                    var representedItems = CountRepresentedItems(scene);
                    Expect(representedItems == workload.ExpectedLogicalItemCount,
                        $"Baseline '{demo.Descriptor.Id}' represents {representedItems} items instead of {workload.ExpectedLogicalItemCount}.");
                }
            }
        });

        Run("Executable RHI command queue and frame resources", failures, () =>
        {
            var capabilities = new RhiDeviceCapabilities3D(
                RhiBackendApi3D.Validation,
                "Validation adapter",
                "stage3",
                RhiDeviceCapabilities3D.RequiredRasterFeatures,
                new RhiDeviceLimits3D(4096, 16, 4, 16, 4096, 4));
            using var device = new RhiDevice3D(capabilities);
            var encoder = device.CreateCommandEncoder();
            encoder.Reset("test-frame");
            encoder.PushDebugGroup("test");
            using (var pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D("main", RhiPassKind3D.ForwardOpaque)))
            {
                pass.ExecuteBackendStage(RhiBackendStage3D.ForwardScene, 0, 3);
            }
            encoder.PopDebugGroup();
            using var commands = encoder.Finish();
            var submission = new RhiFrameSubmission3D();
            device.BeginFrame(submission);
            var upload = device.CurrentUploadRing ?? throw new InvalidOperationException("Frame upload ring was not acquired.");
            var first = upload.Allocate(17, 16);
            var second = upload.Allocate(32, 32);
            Expect(first.Offset == 0 && second.Offset == 32, "RHI upload-ring alignment is incorrect.");
            var executor = new RecordingRhiExecutor();
            var fence = device.Submit(commands, executor);
            Expect(fence.IsValid && device.Queue.IsComplete(fence), "RHI submission fence did not complete.");
            Expect(executor.ForwardStageCount == 1 && executor.CompletedSubmissionId == fence.SubmissionId,
                "RHI queue did not execute the encoded backend stage in order.");
            ExpectThrows<InvalidOperationException>(() => device.Submit(commands, executor),
                "A single-submit RHI command buffer was accepted twice.");
            device.EndFrame(fence);
            Expect(device.Queue.SubmissionCount == 1 && device.Queue.ExecutedCommandCount >= 5,
                "RHI queue counters were not updated.");
            ExpectThrows<RhiCapabilityException3D>(
                () => device.CreateSampler("unsupported", new RhiSamplerDescriptor3D(), 1),
                "Legacy raster profile silently accepted unsupported sampler-object creation.");
        });

        Run("RHI shader reflection, bind groups and pipeline cache", failures, () =>
        {
            var features = RhiDeviceCapabilities3D.RequiredRasterFeatures |
                RhiFeature3D.PipelineLayouts | RhiFeature3D.BindGroups | RhiFeature3D.CopyCommands |
                RhiFeature3D.SamplerObjects | RhiFeature3D.ShaderReflection;
            var capabilities = new RhiDeviceCapabilities3D(
                RhiBackendApi3D.Validation,
                "Validation adapter",
                "stage3-modern",
                features,
                new RhiDeviceLimits3D(4096, 16, 4, 16, 4096, 4,
                    maxUniformBlockSize: 65536,
                    maxBindGroups: 4,
                    maxBindingsPerGroup: 16,
                    maxBufferSize: 16 * 1024 * 1024));
            using var device = new RhiDevice3D(capabilities, profile: RhiCapabilityProfile3D.ModernRaster);
            var uniform = device.CreateBuffer("camera", new RhiBufferDescriptor3D(256, RhiBufferUsage3D.Uniform | RhiBufferUsage3D.CopyDestination), 1);
            var groupLayout = device.CreateBindGroupLayout(
                "frame-layout",
                new RhiBindGroupLayoutDescriptor3D("frame-layout", new[]
                {
                    new RhiBindGroupLayoutEntry3D(0, RhiBindingType3D.UniformBuffer, RhiShaderStage3D.Vertex, 64)
                }),
                1);
            var pipelineLayout = device.CreatePipelineLayout(
                "pipeline-layout",
                new RhiPipelineLayoutDescriptor3D("pipeline-layout", new[] { groupLayout }),
                1);
            var reflection = new RhiShaderReflection3D(new[]
            {
                new RhiShaderBindingReflection3D(0, 0, "Camera", RhiBindingType3D.UniformBuffer, RhiShaderStage3D.Vertex, 64)
            });
            var vertex = device.CreateShaderModule("vertex", new RhiShaderModuleDescriptor3D("vertex", RhiShaderLanguage3D.Wgsl, "vertex-hash", reflection), 1);
            var fragment = device.CreateShaderModule("fragment", new RhiShaderModuleDescriptor3D("fragment", RhiShaderLanguage3D.Wgsl, "fragment-hash", new RhiShaderReflection3D()), 1);
            var pipelineDescriptor = new RhiRenderPipelineDescriptor3D("pipeline", pipelineLayout, vertex, fragment);
            var pipeline = device.GetOrCreateRenderPipeline(pipelineDescriptor);
            Expect(device.GetOrCreateRenderPipeline(pipelineDescriptor).Equals(pipeline) && device.PipelineCache.Hits == 1,
                "RHI pipeline cache did not retain descriptor identity.");
            var bindGroup = device.CreateBindGroup(
                "frame-bindings",
                new RhiBindGroupDescriptor3D("frame-bindings", groupLayout, new[] { new RhiBindGroupEntry3D(0, uniform, 0, 256) }),
                1);

            var encoder = device.CreateCommandEncoder();
            encoder.Reset("modern-frame");
            using (var pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D("main", RhiPassKind3D.ForwardOpaque)))
            {
                pass.SetPipeline(pipeline);
                pass.SetBindGroup(0, bindGroup);
                pass.Draw(3);
            }
            using var commands = encoder.Finish();
            device.BeginFrame(new RhiFrameSubmission3D());
            var executor = new RecordingRhiExecutor();
            var fence = device.Submit(commands, executor);
            device.EndFrame(fence);
            Expect(executor.DrawCount == 1 && executor.RenderPipelineBindCount == 1 && executor.BindGroupBindCount == 1,
                "RHI generic render commands were not executed.");
        });

        Run("GPU-driven RHI commands and render-graph aliasing", failures, () =>
        {
            var features = RhiDeviceCapabilities3D.RequiredGpuDrivenFeatures |
                RhiFeature3D.PipelineLayouts | RhiFeature3D.BindGroups | RhiFeature3D.CopyCommands |
                RhiFeature3D.SamplerObjects | RhiFeature3D.ShaderReflection | RhiFeature3D.FloatTextures;
            var capabilities = new RhiDeviceCapabilities3D(
                RhiBackendApi3D.Validation,
                "GPU-driven validation adapter",
                "stage4",
                features,
                new RhiDeviceLimits3D(8192, 32, 16, 16, 8192, 4,
                    maxUniformBlockSize: 65536,
                    maxStorageBufferBindings: 16,
                    maxBindGroups: 4,
                    maxBindingsPerGroup: 16,
                    maxComputeWorkgroupSizeX: 256,
                    maxComputeWorkgroupSizeY: 256,
                    maxComputeWorkgroupSizeZ: 64,
                    maxComputeInvocationsPerWorkgroup: 256,
                    maxBufferSize: 64 * 1024 * 1024));
            using var device = new RhiDevice3D(capabilities, profile: RhiCapabilityProfile3D.GpuDriven);
            var indirect = device.CreateBuffer("indirect", new RhiBufferDescriptor3D(400, RhiBufferUsage3D.Indirect | RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination), 1);
            var output = device.CreateTexture("output", new RhiTextureDescriptor3D(64, 64, RhiTextureFormat3D.Rgba8Unorm, RhiTextureUsage3D.RenderTarget | RhiTextureUsage3D.Sampled), 1);
            var depth = device.CreateTexture("depth", new RhiTextureDescriptor3D(64, 64, RhiTextureFormat3D.Depth32Float, RhiTextureUsage3D.DepthStencil), 1);
            var encoder = device.CreateCommandEncoder();
            encoder.Reset("gpu-driven-command-contract");
            encoder.ClearBuffer(indirect, 0, 400);
            using (var compute = encoder.BeginComputePass(new RhiComputePassDescriptor3D("dispatch")))
                compute.Dispatch(4096, 1, 1);
            using (var render = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D(
                       "indirect", RhiPassKind3D.ForwardOpaque, colorTarget: output, depthTarget: depth)))
                render.MultiDrawIndexedIndirect(indirect, 0, 20, 20);
            using var commands = encoder.Finish();
            device.BeginFrame(new RhiFrameSubmission3D());
            var executor = new RecordingRhiExecutor();
            var fence = device.Submit(commands, executor);
            device.EndFrame(fence);
            Expect(executor.DrawCount == 20, "Multi-draw indirect command was not executed.");
            Expect(global::System.Runtime.InteropServices.Marshal.SizeOf<ThreeDEngine.Core.Rendering.GpuDriven.GpuDrivenVertex3D>() == 100,
                "Canonical GPU-driven vertex layout is not 100 bytes.");
            Expect(global::System.Runtime.InteropServices.Marshal.SizeOf<ThreeDEngine.Core.Rendering.GpuDriven.GpuMeshletRecord3D>() == 64,
                "GPU meshlet storage record is not 64 bytes.");

            var vertexLayout = new RhiVertexBufferLayout3D(100, new[]
            {
                new RhiVertexAttribute3D(0, 0, RhiVertexFormat3D.Float32x3),
                new RhiVertexAttribute3D(1, 12, RhiVertexFormat3D.Float32x3),
                new RhiVertexAttribute3D(2, 24, RhiVertexFormat3D.Float32x2),
                new RhiVertexAttribute3D(3, 32, RhiVertexFormat3D.Float32x4),
                new RhiVertexAttribute3D(4, 48, RhiVertexFormat3D.Float32x4),
                new RhiVertexAttribute3D(5, 64, RhiVertexFormat3D.Float32),
                new RhiVertexAttribute3D(6, 68, RhiVertexFormat3D.Float32x4),
                new RhiVertexAttribute3D(7, 84, RhiVertexFormat3D.Float32x4)
            });
            Expect(vertexLayout.Attributes.Length == 8 && vertexLayout.ArrayStride == 100,
                "GPU-driven vertex input descriptor does not match the canonical packed record.");

            using var graph = new ThreeDEngine.Core.Rendering.GpuDriven.RenderGraph3D();
            var a = graph.CreateBuffer("transient-a", new RhiBufferDescriptor3D(256, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination));
            var b = graph.CreateBuffer("transient-b", new RhiBufferDescriptor3D(256, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination));
            graph.AddPass("write-a", (context, commandEncoder) => commandEncoder.ClearBuffer(context.GetResource(a), 0, 256))
                .Write(a, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite);
            graph.AddPass("write-b", (context, commandEncoder) => commandEncoder.ClearBuffer(context.GetResource(b), 0, 256))
                .Write(b, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite);
            var graphStats = graph.Compile(device, "stage4-test");
            Expect(graphStats.LogicalResourceCount == 2 && graphStats.PhysicalResourceCount == 1 && graphStats.AliasedResourceCount == 1,
                "Render graph did not alias non-overlapping transient buffers.");
        });

        Run("Runtime diagnostics", failures, () =>
        {
            EngineLog3D.Information("Test", "Diagnostic capture sentinel.");
            using var scene = new Scene3D();
            scene.Add(new Box3D());
            var report = EngineDiagnosticReport3D.Create(scene, maximumLogEntries: 64);
            Expect(report.Contains("Avalonia3D diagnostic report", StringComparison.Ordinal), "Diagnostic report header is missing.");
            Expect(report.Contains("Diagnostic capture sentinel", StringComparison.Ordinal), "Runtime log was not included in the diagnostic report.");
            Expect(report.Contains("Objects: 1", StringComparison.Ordinal), "Scene summary was not included in the diagnostic report.");
            Expect(report.Contains("Engine scope", StringComparison.Ordinal), "Engine scope identity was not included in the diagnostic report.");
        });

        Run("Engine composition root and service ownership", failures, () =>
        {
            var ownedService = new TrackingDisposableService();
            var physics = new TrackingPhysicsCore();
            var builder = new Engine3DBuilder()
                .UseGltfAssets()
                .UsePhysics(_ => physics)
                .ConfigureServices(services => services.AddSingleton(ownedService, EngineServiceOwnership3D.Engine));
            var engine = builder.Build();
            Expect(ReferenceEquals(engine.Services.GetRequiredService<TrackingDisposableService>(), ownedService), "Engine service resolution did not preserve singleton identity.");
            var modelCache = engine.Services.GetRequiredService<ThreeDEngine.Core.Assets.Models.IModelAssetLoader3D>();
            var scene = engine.CreateScene(options =>
            {
                options.ConfigurePerformance = performance => performance.DrawDistance = 4321f;
                options.ConfigureUpdateLoop = loop => loop.FixedUpdatesPerSecond = 120d;
            });
            Expect(ReferenceEquals(scene.Engine, engine), "Scene did not retain its injected engine scope.");
            Expect(ReferenceEquals(scene.PhysicsCore, physics), "Scene did not resolve physics from its engine scope.");
            Expect(scene.Performance.DrawDistance == 4321f && scene.UpdateLoop.FixedUpdatesPerSecond == 120d,
                "Scene options were not applied atomically during construction.");
            engine.Dispose();
            Expect(scene.IsDisposed, "Disposing an engine did not dispose its child scene before services.");
            Expect(physics.DisposeCount == 1, "Engine/scene disposal did not release physics exactly once.");
            Expect(ownedService.DisposeCount == 1, "Engine-owned singleton service was not disposed exactly once.");
            Expect(modelCache is ThreeDEngine.Core.Assets.Models.ModelAssetCache3D { IsDisposed: true },
                "Engine-scoped model cache remained alive after engine disposal.");
            ExpectThrows<ObjectDisposedException>(() => engine.CreateScene(), "Disposed engine accepted a new scene.");
            ExpectThrows<ObjectDisposedException>(() => engine.Services.GetRequiredService<TrackingDisposableService>(), "Disposed service provider continued resolving services.");
            ExpectThrows<InvalidOperationException>(() => builder.Build(), "Engine3DBuilder built a second mutable scope.");

            var duplicate = new Engine3DBuilder();
            duplicate.Services.AddSingleton(new TrackingDisposableService());
            ExpectThrows<InvalidOperationException>(
                () => duplicate.Services.AddSingleton(new TrackingDisposableService()),
                "Duplicate service registration silently replaced an existing dependency.");

            var cyclic = new Engine3DBuilder().ConfigureServices(services =>
            {
                services.AddSingleton<CyclicServiceA>(provider => new CyclicServiceA(provider.GetRequiredService<CyclicServiceB>()));
                services.AddSingleton<CyclicServiceB>(provider => new CyclicServiceB(provider.GetRequiredService<CyclicServiceA>()));
            });
            ExpectThrows<InvalidOperationException>(() => cyclic.Build(), "Circular service graph was not rejected during Build.");
        });

        Run("Aggregate and source-drop default stack bootstrap", failures, () =>
        {
            using var defaultEngine = Engine3D.CreateDefault();
            Expect(defaultEngine.Services.TryGetService<IScenePresenterFactory>(out var presenterFactory) && presenterFactory is not null,
                "The compatibility default stack did not register an Avalonia presenter factory.");
            Expect(presenterFactory!.Kind == BackendKind.OpenGlDesktop,
                "The desktop test host did not select the OpenGL compatibility presenter.");
            Expect(defaultEngine.Services.TryGetService<IPhysicsCoreFactory3D>(out var physicsFactory) && physicsFactory is not null,
                "The compatibility default stack did not register Jitter2 physics.");
            Expect(defaultEngine.Services.TryGetService<ThreeDEngine.Core.Assets.Models.IModelAssetLoader3D>(out var assetLoader) && assetLoader is not null,
                "The compatibility default stack did not register the glTF asset loader.");
        });

        Run("Modular composition fails explicitly", failures, () =>
        {
            using var coreEngine = new Engine3DBuilder().DisablePhysicsByDefault().Build();
            using var coreScene = coreEngine.CreateScene(Scene3DOptions.WithoutPhysics());
            ExpectThrows<InvalidOperationException>(
                () => coreScene.ImportModel("not-read.gltf"),
                "A Core-only engine silently accepted model loading without an asset package.");

            var missingPhysics = new Engine3DBuilder { PhysicsEnabledByDefault = true };
            ExpectThrows<InvalidOperationException>(
                () => missingPhysics.Build(),
                "PhysicsEnabledByDefault silently built without a registered physics package.");
        });

        Run("Scene and physics ownership", failures, () =>
        {
            var first = new TrackingPhysicsCore();
            var second = new TrackingPhysicsCore();
            var scene = CreateScene(first);
            scene.ReplacePhysicsCore(second);
            Expect(first.DisposeCount == 1, "Replacing a physics backend did not dispose the previous backend exactly once.");
            scene.Dispose();
            scene.Dispose();
            Expect(second.DisposeCount == 1, "Disposing a scene did not dispose its current physics backend exactly once.");
            ExpectThrows<ObjectDisposedException>(() => scene.Add(new Box3D()), "Disposed Scene3D accepted a mutation.");
        });

        Run("Engine-scoped immutable mesh cache", failures, () =>
        {
            using var firstEngine = new Engine3DBuilder().DisablePhysicsByDefault().Build();
            using var firstScene = firstEngine.CreateScene(Scene3DOptions.WithoutPhysics());
            var first = firstScene.Add(new Box3D()).GetMesh();
            var second = firstScene.Add(new Box3D()).GetMesh();
            var firstCache = firstEngine.Services.GetRequiredService<MeshCache3D>();
            Expect(ReferenceEquals(first, second), "Identical primitives did not share immutable geometry inside one engine scope.");
            Expect(firstCache.Count == 1 && firstCache.MissCount == 1 && firstCache.HitCount >= 1,
                "Engine mesh-cache counters do not describe the actual cache path.");

            var preciseA = firstScene.Add(new Box3D { Width = 1.00001f }).GetMesh();
            var preciseB = firstScene.Add(new Box3D { Width = 1.00002f }).GetMesh();
            Expect(!ReferenceEquals(preciseA, preciseB),
                "Primitive cache key quantization aliased two distinct geometry dimensions.");

            using var secondEngine = new Engine3DBuilder().DisablePhysicsByDefault().Build();
            using var secondScene = secondEngine.CreateScene(Scene3DOptions.WithoutPhysics());
            var isolated = secondScene.Add(new Box3D()).GetMesh();
            Expect(!ReferenceEquals(first, isolated), "Primitive geometry leaked across independent engine scopes.");

            var migratedObject = firstScene.Add(new Sphere3D());
            var previousScopeMesh = migratedObject.GetMesh();
            Expect(firstScene.Remove(migratedObject), "Unable to detach a primitive for the cache migration test.");
            secondScene.Add(migratedObject);
            Expect(!ReferenceEquals(previousScopeMesh, migratedObject.GetMesh()),
                "A primitive retained another engine's cached mesh after scene migration.");
        });

        Run("Low-poly sphere geometry contract", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var sphere = scene.Add(new Sphere3D { Radius = 10f, Segments = 8, Rings = 4 });
            var mesh = sphere.GetMesh();
            Expect(mesh.Positions.Length == (4 + 1) * (8 + 1), "Low-poly sphere vertex count is invalid.");
            Expect(mesh.Indices.Length == 4 * 8 * 6, "Low-poly sphere index count is invalid.");
            ExpectThrows<ArgumentOutOfRangeException>(() => sphere.Segments = 2, "Sphere accepted fewer than three segments.");
            ExpectThrows<ArgumentOutOfRangeException>(() => sphere.Rings = 1, "Sphere accepted fewer than two rings.");
            ExpectThrows<ArgumentOutOfRangeException>(() => MeshFactory.CreateSphere(1f, 2, 4), "MeshFactory accepted fewer than three sphere segments.");
            ExpectThrows<ArgumentOutOfRangeException>(() => MeshFactory.CreateSphere(1f, 8, 1), "MeshFactory accepted fewer than two sphere rings.");
        });

        Run("Registry physics membership", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var box = scene.Add(new Box3D
            {
                Collider = new BoxCollider3D(),
                Rigidbody = new Rigidbody3D { IsKinematic = false }
            });

            Expect(scene.Registry.DynamicBodies.Count == 1, "Dynamic body was not registered.");
            Expect(scene.Registry.StaticColliders.Count == 0, "Dynamic body was also registered as static.");
            box.Rigidbody!.IsKinematic = true;
            Expect(scene.Registry.DynamicBodies.Count == 0, "IsKinematic change left stale dynamic membership.");
            Expect(scene.Registry.StaticColliders.Count == 1, "IsKinematic change did not update static membership.");
            box.Collider = null;
            Expect(scene.Registry.Colliders.Count == 0, "Collider removal left stale collider membership.");
        });

        Run("Incremental scene graph preserves exact leaf changes", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var composite = scene.Add(new TestComposite3D(4));
            var child = composite.Children[2];
            var snapshot = scene.Registry.GetFrameSnapshot();
            var registryVersion = scene.Registry.Version;
            var fullRebuilds = scene.Registry.FullRebuildCount;
            var transformVersion = scene.BatchTransformVersion;
            var sequence = scene.ChangeSequence;
            SceneChangedEventArgs? observed = null;
            scene.SceneChangedDetailed += (_, e) => observed = e;

            child.Position = new Vector3(3f, 2f, 1f);

            Expect(observed is not null && ReferenceEquals(observed.Source, child), "Composite bubbling replaced the exact leaf source.");
            Expect(observed!.Contains(SceneChangeKind.Transform), "Leaf transform category was lost.");
            Expect(scene.Registry.Version == registryVersion, "Transform-only mutation changed registry membership version.");
            Expect(scene.Registry.FullRebuildCount == fullRebuilds, "Leaf transform forced a full registry rebuild.");
            Expect(ReferenceEquals(snapshot, scene.Registry.GetFrameSnapshot()), "Transform-only mutation rebuilt an immutable membership snapshot.");

            var records = new List<SceneChangeRecord3D>();
            Expect(scene.TryCopyChangesSince(sequence, records) && records.Count == 1 && ReferenceEquals(records[0].Source, child),
                "Exact scene journal did not return the leaf mutation.");
            var dirty = new List<Object3D>();
            Expect(scene.TryCopyBatchTransformChangesSince(transformVersion, dirty), "Transform cursor unexpectedly overflowed.");
            Expect(dirty.Count == 1 && ReferenceEquals(dirty[0], child), "One leaf transform expanded to unrelated siblings.");

            var stableSnapshot = scene.Registry.GetFrameSnapshot();
            child.Name = "Renamed leaf";
            Expect(scene.Registry.Version == registryVersion && ReferenceEquals(stableSnapshot, scene.Registry.GetFrameSnapshot()),
                "Metadata-only mutation invalidated render membership.");
        });

        Run("Incremental membership and composite replacement", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var composite = scene.Add(new TestComposite3D(3));
            var child = composite.Children[0];
            var fullRebuilds = scene.Registry.FullRebuildCount;
            var renderableCount = scene.Registry.Renderables.Count;
            child.IsVisible = false;
            Expect(scene.Registry.Renderables.Count == renderableCount - 1, "Visibility removal did not patch renderable membership.");
            child.IsVisible = true;
            Expect(scene.Registry.Renderables.Count == renderableCount, "Visibility restore did not patch renderable membership.");

            var oldChild = composite.Children[0];
            var beforeRebuildSequence = scene.ChangeSequence;
            composite.PartCount = 5;
            composite.Rebuild();
            Expect(scene.Registry.AllObjects.Count == 6, "Composite subtree replacement left stale or missing registry objects.");
            Expect(scene.Registry.FullRebuildCount == fullRebuilds, "Composite subtree replacement forced a full-scene rebuild.");
            Expect(scene.ChangeSequence == beforeRebuildSequence + 1, "Composite builder leaked temporary-part changes before atomic commit.");
            var sequence = scene.ChangeSequence;
            oldChild.Position = Vector3.One;
            Expect(scene.ChangeSequence == sequence, "Detached composite child remained subscribed to its former scene.");
        });

        Run("Batched changes preserve all categories", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var box = scene.Add(new Box3D());
            var cursor = scene.ChangeSequence;
            SceneChangedEventArgs? committed = null;
            var eventCount = 0;
            scene.SceneChangedDetailed += (_, e) => { committed = e; eventCount++; };
            using (scene.BeginUpdate())
            {
                box.Position = Vector3.UnitX;
                box.Fill = ColorRgba.Black;
            }

            Expect(eventCount == 1 && committed is not null && committed.IsBatch, "Scene transaction emitted an event storm.");
            Expect(committed!.Contains(SceneChangeKind.Transform) && committed.Contains(SceneChangeKind.Material),
                "Scene transaction discarded a change category.");
            var records = new List<SceneChangeRecord3D>();
            Expect(scene.TryCopyChangesSince(cursor, records) && records.Count == 2, "Exact journal did not preserve both batched mutations.");
        });

        Run("Change journal reports cursor overflow", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var box = scene.Add(new Box3D());
            var cursor = scene.ChangeSequence;
            using (scene.BeginUpdate())
            {
                for (var i = 0; i <= scene.ChangeJournalCapacity; i++) box.IsSelected = !box.IsSelected;
            }

            var records = new List<SceneChangeRecord3D>();
            Expect(!scene.TryCopyChangesSince(cursor, records) && records.Count == 0,
                "A lagging retained consumer was given an incomplete journal as if it were complete.");
        });

        Run("Interpolation tracks only changed objects", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var first = scene.Add(new Box3D());
            scene.Add(new Box3D { Position = Vector3.UnitZ });
            scene.FrameInterpolator.Enabled = true;
            _ = scene.Registry.GetFrameSnapshot();
            var snapshotBuilds = scene.Registry.SnapshotBuildCount;
            scene.UpdateLoop.StepOnce();
            scene.FixedUpdate += MoveFirst;
            scene.UpdateLoop.StepOnce();
            Expect(scene.FrameInterpolator.ActiveObjectCount == 1, "Interpolation scanned/activated unchanged renderables.");
            Expect(scene.Registry.SnapshotBuildCount == snapshotBuilds, "Interpolation allocated a new membership snapshot on fixed tick.");

            void MoveFirst(Scene3D owner, in SceneFixedUpdateContext3D context) => first.Position += Vector3.UnitX;
        });

        Run("Dormant physics wake-up activity", failures, () =>
        {
            using var scene = CreateScene(new TrackingPhysicsCore());
            var body = new Rigidbody3D { UseGravity = false, IsKinematic = false };
            scene.Add(new Box3D
            {
                Collider = new BoxCollider3D(),
                Rigidbody = body
            });
            Expect(!scene.HasActiveUpdateWork(), "Dormant body kept the automatic update pump active.");
            body.AddImpulse(Vector3.UnitX);
            Expect(scene.HasActiveUpdateWork(), "Pending impulse did not wake automatic scene updates.");
        });

        Run("Kinematic transform requests one fixed tick", failures, () =>
        {
            using var scene = CreateScene(new TrackingPhysicsCore());
            var kinematic = scene.Add(new Box3D
            {
                Collider = new BoxCollider3D(),
                Rigidbody = new Rigidbody3D { IsKinematic = true, UseGravity = false }
            });
            Expect(!scene.HasActiveUpdateWork(), "Idle kinematic body kept automatic updates active.");
            kinematic.Position = Vector3.UnitX;
            Expect(scene.HasActiveUpdateWork(), "Kinematic transform did not request a physics synchronization tick.");
            scene.UpdateLoop.StepOnce();
            Expect(!scene.HasActiveUpdateWork(), "One-shot fixed update request was not consumed.");
        });

        Run("Activity query does not wait for simulation writer", failures, () =>
        {
            using var enteredStep = new ManualResetEventSlim(false);
            using var releaseStep = new ManualResetEventSlim(false);
            var physics = new TrackingPhysicsCore
            {
                StepCallback = () =>
                {
                    enteredStep.Set();
                    releaseStep.Wait(TimeSpan.FromSeconds(5));
                }
            };
            using var scene = CreateScene(physics);
            scene.AddParticleSystem(new ParticleSystemSettings3D
            {
                Looping = true,
                EmissionRatePerSecond = 1f
            });

            var simulationThread = new Thread(() => scene.UpdateLoop.StepOnce()) { IsBackground = true };
            simulationThread.Start();
            Expect(enteredStep.Wait(TimeSpan.FromSeconds(5)), "Simulation did not enter the blocking physics stage.");
            try
            {
                var query = Task.Run(scene.HasActiveUpdateWork);
                Expect(query.Wait(TimeSpan.FromSeconds(1)),
                    "HasActiveUpdateWork blocked behind the full simulation write lease.");
                Expect(query.Result, "Cached activity hint lost an active particle system.");
            }
            finally
            {
                releaseStep.Set();
                simulationThread.Join(TimeSpan.FromSeconds(5));
            }
        });

        Run("High-scale exact chunk bounds", failures, () =>
        {
            var layer = new InstancedMesh3D("Bounds", MeshFactory.CreateExtrudedRectangle(1f, 1f, 1f), chunkCellSize: 24f);
            var index = layer.AddInstance(Vector3.Zero);
            var chunk = Single(layer.Chunks.Chunks);
            var initialMaximum = chunk.Bounds.Max.X;
            layer.SetInstanceTransform(index, Matrix4x4.CreateTranslation(20f, 0f, 0f));
            layer.QueryVisibleChunks(Matrix4x4.Identity);
            Expect(chunk.Bounds.Max.X > initialMaximum + 19f, "A same-chunk transform left stale culling bounds.");
        });

        Run("32-bit wireframe indices", failures, () =>
        {
            const int vertexCount = 65_537;
            var geometry = new RenderGeometry3D(
                new Vector3[vertexCount],
                new Vector3[vertexCount],
                new[] { 0, 65_535, 65_536 },
                "test:wireframe-u32");
            var payload = geometry.GetWebGlPayload(includeWireframe: true);
            Expect(payload.WireframeIndexElementSize == sizeof(int), "Wireframe payload truncated a 32-bit vertex index to UInt16.");
            var preserved = false;
            for (var offset = 0; offset < payload.WireframeIndices.Length; offset += sizeof(int))
            {
                preserved |= BitConverter.ToInt32(payload.WireframeIndices.Span.Slice(offset, sizeof(int))) == 65_536;
            }
            Expect(preserved, "Wireframe payload did not preserve vertex index 65536.");
        });

        Run("Material factory isolation", failures, () =>
        {
            var first = Material3D.CreateDefault();
            var second = Material3D.CreateDefault();
            first.BaseColor = ColorRgba.Black;
            Expect(!ReferenceEquals(first, second), "CreateDefault returned a shared mutable material.");
            Expect(second.BaseColor.Equals(ColorRgba.White), "Mutating one default material changed another material.");
        });

        Run("Deterministic fixed update loop", failures, () =>
        {
            var firstPhysics = new TrackingPhysicsCore();
            using var first = CreateScene(firstPhysics);
            ConfigureDeterminismTestLoop(first.UpdateLoop);
            var firstTicks = new List<long>();
            first.FixedUpdate += OnFirstFixedUpdate;
            first.Update(0.35d);
            first.Update(0.05d);

            var secondPhysics = new TrackingPhysicsCore();
            using var second = CreateScene(secondPhysics);
            ConfigureDeterminismTestLoop(second.UpdateLoop);
            for (var i = 0; i < 4; i++) second.Update(0.1d);

            Expect(first.UpdateLoop.SimulationTick == 4, "Chunked host time did not produce four fixed ticks.");
            Expect(second.UpdateLoop.SimulationTick == first.UpdateLoop.SimulationTick, "Equivalent elapsed time produced a different tick count.");
            ExpectNear(first.UpdateLoop.SimulationTimeSeconds, second.UpdateLoop.SimulationTimeSeconds, 1e-12d, "Equivalent elapsed time produced a different simulation time.");
            Expect(firstPhysics.StepCount == secondPhysics.StepCount && firstPhysics.StepCount == 4, "Physics did not execute exactly once per fixed tick.");
            Expect(firstTicks.Count == 4 && firstTicks[0] == 1 && firstTicks[3] == 4, "Fixed update context tick sequence is not monotonic.");

            void OnFirstFixedUpdate(Scene3D scene, in SceneFixedUpdateContext3D context)
                => firstTicks.Add(context.Tick);
        });

        Run("Fixed update order, pause and single-step", failures, () =>
        {
            var order = new List<string>();
            var physics = new TrackingPhysicsCore { StepCallback = () => order.Add("physics") };
            using var scene = CreateScene(physics);
            ConfigureDeterminismTestLoop(scene.UpdateLoop);
            scene.FixedUpdate += OnFixedUpdate;
            scene.FixedUpdateCompleted += OnFixedUpdateCompleted;

            scene.UpdateLoop.IsPaused = true;
            var paused = scene.Update(1d);
            Expect(paused.ExecutedSteps == 0 && scene.UpdateLoop.SimulationTick == 0, "Paused scene consumed host time.");
            scene.UpdateLoop.StepOnce();
            Expect(scene.UpdateLoop.SimulationTick == 1, "Single-step did not run exactly one paused tick.");
            Expect(string.Join(",", order) == "before,physics,after", "Fixed update phase order is incorrect.");

            scene.UpdateLoop.IsPaused = false;
            scene.Update(0.1d);
            Expect(scene.UpdateLoop.SimulationTick == 2, "Resume did not continue from the paused timeline.");

            void OnFixedUpdate(Scene3D owner, in SceneFixedUpdateContext3D context) => order.Add("before");
            void OnFixedUpdateCompleted(Scene3D owner, in SceneFixedUpdateContext3D context) => order.Add("after");
        });

        Run("Simulation command queue and stage metrics", failures, () =>
        {
            var order = new List<string>();
            var physics = new TrackingPhysicsCore { StepCallback = () => order.Add("physics") };
            using var scene = CreateScene(physics);
            ConfigureDeterminismTestLoop(scene.UpdateLoop);
            scene.FixedUpdate += OnFixedUpdate;
            scene.FixedUpdateCompleted += OnFixedUpdateCompleted;

            scene.Commands.Enqueue(owner =>
            {
                order.Add("command-1");
                owner.BackgroundColor = ColorRgba.Black;
            });
            var completion = scene.Commands.EnqueueAsync(_ => order.Add("command-2"));
            scene.UpdateLoop.StepOnce();
            completion.GetAwaiter().GetResult();

            Expect(string.Join(",", order) == "command-1,command-2,before,physics,after",
                "Simulation stages or queued commands executed in the wrong order.");
            Expect(scene.Commands.PendingCount == 0 && scene.Commands.LastCompletedSequence == scene.Commands.LastPostedSequence,
                "Command queue did not publish a fully drained sequence.");
            Expect(scene.SimulationMetrics.Tick == 1 && scene.SimulationMetrics.CommandsExecuted == 2,
                "Simulation metrics did not describe the completed fixed tick.");
            Expect(scene.SimulationMetrics.TotalMilliseconds >= 0d && scene.SimulationMetrics.PhysicsMilliseconds >= 0d,
                "Simulation stage timings contain invalid values.");

            scene.UpdateLoop.IsPaused = true;
            var pausedMutationApplied = false;
            scene.Commands.Enqueue(_ => pausedMutationApplied = true);
            scene.Update(0d);
            Expect(pausedMutationApplied, "Paused simulation did not pump queued commands.");

            void OnFixedUpdate(Scene3D owner, in SceneFixedUpdateContext3D context) => order.Add("before");
            void OnFixedUpdateCompleted(Scene3D owner, in SceneFixedUpdateContext3D context) => order.Add("after");
        });

        Run("World ownership, command buffers, immutable publications and replay", failures, () =>
        {
            using var scene = new Scene3D(new Scene3DOptions
            {
                PhysicsEnabled = false,
                MutationPolicy = WorldMutationPolicy3D.StrictSimulationOwner
            });
            var box = scene.Add(new Box3D
            {
                Name = "OwnedBox",
                Collider = new BoxCollider3D(),
                Rigidbody = new Rigidbody3D()
            });
            scene.World.StepOnce();

            using var initial = scene.World.AcquireReadSnapshot();
            var initialObject = FindWorldObject(initial.Snapshot, box.Id);
            Expect(initialObject.Position == Vector3.Zero && initialObject.IsVisible,
                "Initial immutable world publication did not contain the scene object state.");

            scene.World.Replay.BeginCapture();
            using (var commands = scene.World.CreateCommandBuffer())
            {
                commands
                    .Add(SceneCommands3D.SetTransform(box.Id, new Vector3(4f, 5f, 6f), new Vector3(0f, 45f, 0f), new Vector3(2f)))
                    .Add(SceneCommands3D.SetVisibility(box.Id, false));
                commands.Commit();
            }
            Expect(scene.Commands.PendingCount == 1, "A command-buffer transaction was not queued as one deterministic command.");
            Expect(scene.World.PumpCommands() == 1, "World command pump did not execute the queued transaction.");
            scene.World.StepOnce();
            var replay = scene.World.Replay.EndCapture();
            Expect(replay.Entries.Length == 1, "Replay capture did not preserve the command-buffer transaction as one entry.");
            Expect(box.Position == new Vector3(4f, 5f, 6f) && !box.IsVisible,
                "Command-buffer mutations were not committed on the simulation owner.");

            using var updated = scene.World.AcquireReadSnapshot();
            var updatedObject = FindWorldObject(updated.Snapshot, box.Id);
            Expect(updatedObject.Position == new Vector3(4f, 5f, 6f) && !updatedObject.IsVisible,
                "Published immutable world state did not advance after command execution.");
            Expect(updated.Snapshot.SimulationTick == 2,
                "Immutable world publication did not advance its simulation timeline when object state was unchanged during the second tick.");
            Expect(initialObject.Position == Vector3.Zero && initialObject.IsVisible,
                "A held immutable snapshot was overwritten while a reader lease was active.");

            using (var owner = scene.World.BindPersistentOwner())
            {
                var transformFailure = Task.Run(() => CaptureException(() => box.Position = Vector3.One)).GetAwaiter().GetResult();
                Expect(transformFailure is InvalidOperationException,
                    "Strict world ownership accepted a cross-thread Object3D mutation.");
                var materialFailure = Task.Run(() => CaptureException(() => box.Material.BaseColor = new ColorRgba(1f, 0f, 0f, 1f))).GetAwaiter().GetResult();
                Expect(materialFailure is InvalidOperationException,
                    "Strict world ownership accepted a cross-thread nested Material3D mutation.");
                var colliderFailure = Task.Run(() => CaptureException(() => ((BoxCollider3D)box.Collider!).Size = Vector3.One * 2f)).GetAwaiter().GetResult();
                Expect(colliderFailure is InvalidOperationException,
                    "Strict world ownership accepted a cross-thread nested Collider3D mutation.");
                var rigidbodyFailure = Task.Run(() => CaptureException(() => box.Rigidbody!.Mass = 3f)).GetAwaiter().GetResult();
                Expect(rigidbodyFailure is InvalidOperationException,
                    "Strict world ownership accepted a cross-thread nested Rigidbody3D mutation.");
                var pipelineFailure = Task.Run(() => CaptureException(() => scene.RenderPipeline.EnableHdr = true)).GetAwaiter().GetResult();
                Expect(pipelineFailure is InvalidOperationException,
                    "Strict world ownership accepted a cross-thread render-pipeline mutation.");
                var debugFailure = Task.Run(() => CaptureException(() => scene.Debug.ShowGrid = true)).GetAwaiter().GetResult();
                Expect(debugFailure is InvalidOperationException,
                    "Strict world ownership accepted a cross-thread debug-settings mutation.");

                box.Position = Vector3.Zero;
                box.RotationDegrees = Vector3.Zero;
                box.Scale = Vector3.One;
                box.IsVisible = true;
                scene.World.Replay.ReplayOffline(replay);
                Expect(box.Position == new Vector3(4f, 5f, 6f) && !box.IsVisible,
                    "Offline deterministic replay did not reproduce the captured command state.");
            }
        });

        Run("Dependency-aware world jobs commit deterministically", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var box = scene.Add(new Box3D { Name = "JobTarget" });
            scene.World.Jobs.Register(new VisibilityWorldJob("prepare", Array.Empty<string>(), box.Id, false));
            scene.World.Jobs.Register(new VisibilityWorldJob("restore", new[] { "prepare" }, box.Id, true));
            Expect(scene.HasActiveUpdateWork(), "Registered world jobs did not keep automatic simulation active.");
            scene.World.StepOnce();
            Expect(box.IsVisible, "Dependency-ordered world jobs did not commit in deterministic order.");
            var metrics = scene.World.Jobs.LastMetrics;
            Expect(metrics.JobCount == 2 && metrics.CommandsCommitted == 2,
                "World-job metrics did not report executed jobs and committed commands.");
            Expect(scene.SimulationMetrics.JobsExecuted == 2 && scene.SimulationMetrics.JobCommandsCommitted == 2,
                "Simulation stage metrics did not publish world-job execution.");
        });

        Run("Queued recovery from sticky simulation fault", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            ConfigureDeterminismTestLoop(scene.UpdateLoop);
            var fail = true;
            scene.FixedUpdate += OnFixedUpdate;
            ExpectThrows<InvalidOperationException>(
                () => scene.UpdateLoop.StepOnce(),
                "A failing fixed update did not fault the deterministic loop.");
            Expect(scene.UpdateLoop.IsFaulted, "Simulation fault was not retained after the failing tick.");

            var recoveryApplied = false;
            scene.Commands.Enqueue(owner =>
            {
                fail = false;
                owner.UpdateLoop.ResetFault();
                recoveryApplied = true;
            });
            var result = scene.Update(0d);
            Expect(recoveryApplied && !scene.UpdateLoop.IsFaulted,
                "A queued owner-thread command could not recover a sticky simulation fault.");
            Expect(result.ExecutedSteps == 0, "Fault recovery unexpectedly advanced simulation time.");
            scene.UpdateLoop.StepOnce();
            Expect(scene.UpdateLoop.SimulationTick == 1, "Simulation did not resume after queued fault recovery.");

            void OnFixedUpdate(Scene3D owner, in SceneFixedUpdateContext3D context)
            {
                if (fail) throw new InvalidOperationException("intentional test fault");
            }
        });

        Run("Command cancellation and scene disposal", failures, () =>
        {
            var scene = CreateSceneWithoutPhysics();
            var executed = false;
            using var cancellation = new CancellationTokenSource();
            var completion = scene.Commands.EnqueueAsync(_ => executed = true, cancellation.Token);
            cancellation.Cancel();
            Expect(scene.UpdateLoop.PumpCommands() == 1, "Canceled queued command was not consumed.");
            Expect(!executed && completion.IsCanceled, "Canceled command executed or did not complete as canceled.");
            scene.Dispose();
            ExpectThrows<ObjectDisposedException>(
                () => scene.Commands.Enqueue(_ => { }),
                "Disposed scene command queue accepted a new command.");
        });

        Run("Multi-producer command publication order", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            const int producerCount = 4;
            const int commandsPerProducer = 128;
            var executed = new List<long>(producerCount * commandsPerProducer);
            var posted = new ConcurrentBag<long>();
            using var start = new ManualResetEventSlim();
            var producers = new Task[producerCount];
            for (var producer = 0; producer < producers.Length; producer++)
            {
                producers[producer] = Task.Run(() =>
                {
                    start.Wait();
                    for (var commandIndex = 0; commandIndex < commandsPerProducer; commandIndex++)
                    {
                        long sequence = 0;
                        sequence = scene.Commands.Enqueue(_ => executed.Add(sequence));
                        posted.Add(sequence);
                    }
                });
            }

            start.Set();
            Expect(Task.WaitAll(producers, TimeSpan.FromSeconds(5)),
                "Concurrent command producers did not complete.");
            var drained = scene.UpdateLoop.PumpCommands();
            Expect(drained == producerCount * commandsPerProducer,
                "The simulation command queue did not drain every concurrently published command.");
            Expect(executed.Count == drained && posted.Count == drained,
                "Command publication or execution count was inconsistent.");
            for (var index = 1; index < executed.Count; index++)
            {
                Expect(executed[index] == executed[index - 1] + 1,
                    "Concurrent commands were not consumed in atomic publication sequence order.");
            }
            Expect(executed[0] == 1 && executed[^1] == scene.Commands.LastPostedSequence,
                "Published sequence range is incomplete.");
        });

        Run("Host-thread simulation diagnostics", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            using var host = new SceneSimulationHost3D(scene, SceneSimulationExecutionMode3D.HostThread);
            var snapshot = host.CaptureSnapshot();
            Expect(snapshot.ConfiguredMode == SceneSimulationExecutionMode3D.HostThread &&
                   snapshot.ResolvedMode == SceneSimulationExecutionMode3D.HostThread &&
                   !snapshot.UsesDedicatedThread && !snapshot.WorkerAlive,
                "Host-thread simulation diagnostics reported an inconsistent execution owner.");
        });

        Run("Dedicated simulation worker ownership", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            ConfigureDeterminismTestLoop(scene.UpdateLoop);
            var callerThread = Environment.CurrentManagedThreadId;
            var callbackThread = 0;
            using var completed = new ManualResetEventSlim();
            scene.FixedUpdate += OnFixedUpdate;
            using var host = new SceneSimulationHost3D(scene, SceneSimulationExecutionMode3D.DedicatedThread);
            host.Submit(0.1d);
            Expect(completed.Wait(TimeSpan.FromSeconds(5)), "Dedicated simulation worker did not execute a fixed tick.");
            Expect(callbackThread != 0 && callbackThread != callerThread,
                "Dedicated simulation executed the fixed tick on the host thread.");

            void OnFixedUpdate(Scene3D owner, in SceneFixedUpdateContext3D context)
            {
                callbackThread = Environment.CurrentManagedThreadId;
                completed.Set();
            }
        });

        Run("Dedicated simulation faults are observable", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            ConfigureDeterminismTestLoop(scene.UpdateLoop);
            using var faultObserved = new ManualResetEventSlim();
            Exception? observed = null;
            scene.FixedUpdate += static (Scene3D owner, in SceneFixedUpdateContext3D context) =>
                throw new InvalidOperationException("intentional worker fault");
            using var host = new SceneSimulationHost3D(scene, SceneSimulationExecutionMode3D.DedicatedThread);
            host.Faulted += (_, args) =>
            {
                observed = args.Exception;
                faultObserved.Set();
            };
            host.Submit(0.1d);
            Expect(faultObserved.Wait(TimeSpan.FromSeconds(5)), "Dedicated simulation fault was not published to the host.");
            var snapshot = host.CaptureSnapshot();
            Expect(observed is InvalidOperationException, "Dedicated simulation published the wrong fault type.");
            Expect(snapshot.FaultCount >= 1 && snapshot.LastFaultType?.Contains(nameof(InvalidOperationException), StringComparison.Ordinal) == true,
                "Simulation host diagnostics did not retain the worker fault.");
        });

        Run("Simulation and render snapshot serialization", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            ConfigureDeterminismTestLoop(scene.UpdateLoop);
            using var enteredUpdate = new ManualResetEventSlim();
            using var releaseUpdate = new ManualResetEventSlim();
            scene.FixedUpdate += BlockUpdate;

            var simulation = Task.Run(() => scene.UpdateLoop.StepOnce());
            Expect(enteredUpdate.Wait(TimeSpan.FromSeconds(5)), "Simulation did not enter the fixed update callback.");
            var render = Task.Run(() => SceneRenderFrameContext3D.Build(scene, 640f, 480f, BackendKind.OpenGlDesktop));
            Thread.Sleep(50);
            Expect(!render.IsCompleted, "Render snapshot read crossed an active simulation write boundary.");
            releaseUpdate.Set();
            Expect(Task.WaitAll(new Task[] { simulation, render }, TimeSpan.FromSeconds(5)),
                "Simulation/render serialization did not complete after releasing the fixed update.");

            var frame = render.GetAwaiter().GetResult();
            var capturedPosition = frame.Published.CameraPosition;
            frame.Dispose();
            scene.Camera.Position += Vector3.UnitX;
            Expect(frame.Published.CameraPosition == capturedPosition,
                "Published render snapshot changed after mutable scene state advanced.");

            void BlockUpdate(Scene3D owner, in SceneFixedUpdateContext3D context)
            {
                enteredUpdate.Set();
                releaseUpdate.Wait(TimeSpan.FromSeconds(5));
            }
        });

        Run("Cross-platform Jitter2 defaults", failures, () =>
        {
            using var physics = new Jitter2PhysicsCore();
            ExpectNear(physics.FixedTimeStep, 1d / 120d, 1e-7d, "Jitter2 fixed step is not the unified 120 Hz default.");
            Expect(physics.MaxStepsPerFrame == 8, "Jitter2 maximum step budget differs from the unified default.");
            ExpectNear(physics.MaxFrameDeltaSeconds, 0.25d, 1e-7d, "Jitter2 maximum frame delta differs from the unified default.");
            Expect(physics.SubstepCount == 4, "Jitter2 substep default differs across platforms.");
            Expect(physics.SolverIterations == (12, 4), "Jitter2 solver defaults differ across platforms.");
            Expect(physics.EnableGroundProbe, "Jitter2 ground probing is not enabled by the unified profile.");
        });

        Run("Bounded simulation catch-up", failures, () =>
        {
            using var scene = CreateScene(new TrackingPhysicsCore());
            scene.UpdateLoop.FixedDeltaSeconds = 0.1d;
            scene.UpdateLoop.MaximumCatchUpSteps = 3;
            scene.UpdateLoop.MaximumFrameDeltaSeconds = 2d;
            var result = scene.Update(1d);
            Expect(result.ExecutedSteps == 3, "Catch-up cap was not enforced.");
            Expect(result.DroppedSteps == 7, "Excess whole fixed ticks were not reported.");
            ExpectNear(result.DroppedSeconds, 0.7d, 1e-9d, "Dropped catch-up time is incorrect.");
            Expect(scene.UpdateLoop.SimulationTick == 3, "Dropped ticks were incorrectly added to simulation time.");
        });

        Run("Simulation faults are sticky and explicit", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            ConfigureDeterminismTestLoop(scene.UpdateLoop);
            SceneFixedUpdateHandler3D failing = FailUpdate;
            scene.FixedUpdate += failing;
            ExpectThrows<InvalidOperationException>(() => scene.Update(0.1d), "A fixed-update exception was swallowed.");
            Expect(scene.UpdateLoop.IsFaulted, "Update loop did not retain its fault state.");
            Expect(scene.UpdateLoop.SimulationTick == 0, "A failed fixed tick advanced the timeline.");
            ExpectThrows<InvalidOperationException>(() => scene.Update(0.1d), "Faulted update loop continued automatically.");

            scene.FixedUpdate -= failing;
            scene.UpdateLoop.ResetFault();
            scene.Update(0.1d);
            Expect(scene.UpdateLoop.SimulationTick == 1, "ResetFault did not restore updates after the cause was removed.");

            static void FailUpdate(Scene3D owner, in SceneFixedUpdateContext3D context)
                => throw new InvalidOperationException("intentional test fault");
        });

        Run("Render frame is simulation-read-only", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            scene.Add(new Box3D());
            scene.FrameInterpolator.Enabled = true;
            scene.UpdateLoop.StepOnce();
            var version = scene.FrameInterpolator.RenderVersion;
            var alpha = scene.FrameInterpolator.Alpha;
            using (SceneRenderFrameContext3D.Build(scene, 640f, 480f, BackendKind.OpenGlDesktop)) { }
            using (SceneRenderFrameContext3D.Build(scene, 640f, 480f, BackendKind.OpenGlDesktop)) { }
            Expect(scene.FrameInterpolator.RenderVersion == version, "Building a render frame mutated interpolation state.");
            ExpectNear(scene.FrameInterpolator.Alpha, alpha, 0d, "Building a render frame changed interpolation alpha.");
        });

        Run("Allocation-free scene transactions", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            for (var i = 0; i < 32; i++)
            {
                using var warmup = scene.BeginUpdate();
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                using var update = scene.BeginUpdate();
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Expect(allocated <= 512, $"Empty scene transactions allocated {allocated} bytes after warmup.");

            var outer = scene.BeginUpdate();
            var inner = scene.BeginUpdate();
            var rejectedOutOfOrder = false;
            try
            {
                outer.Dispose();
            }
            catch (InvalidOperationException)
            {
                rejectedOutOfOrder = true;
            }
            Expect(rejectedOutOfOrder, "Out-of-order scene transaction disposal was not rejected.");
            inner.Dispose();
            outer.Dispose();
        });

        Run("Explicit transaction and render serialization", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            scene.Add(new Box3D());
            using var renderStarted = new ManualResetEventSlim();
            using var renderCompleted = new ManualResetEventSlim();
            var update = scene.BeginUpdate();
            Task? render = null;
            try
            {
                scene.Objects[0].Position = Vector3.UnitX;
                render = Task.Run(() =>
                {
                    renderStarted.Set();
                    using var frame = SceneRenderFrameContext3D.Build(scene, 640f, 480f, BackendKind.OpenGlDesktop);
                    renderCompleted.Set();
                });
                Expect(renderStarted.Wait(TimeSpan.FromSeconds(5)), "Render reader did not start while the explicit transaction was active.");
                Thread.Sleep(50);
                Expect(!renderCompleted.IsSet, "Render crossed an active explicit scene transaction and observed partial state.");
                ExpectThrows<InvalidOperationException>(() => scene.Dispose(), "Scene disposal was allowed while an explicit update transaction owned the write lease.");
            }
            finally
            {
                update.Dispose();
            }

            Expect(render is not null && render.Wait(TimeSpan.FromSeconds(5)),
                "Render reader did not resume after the explicit transaction completed.");
        });

        Run("Reusable render publication and plan", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            using (scene.BeginUpdate())
            {
                for (var i = 0; i < 128; i++)
                {
                    scene.Add(new Box3D { Position = new Vector3(i % 16, 0f, i / 16) });
                }
            }

            var frameScratch = new SceneRenderFrameScratch3D();
            var planScratch = new SceneRenderPlanScratch3D();
            SceneRenderFrameContext3D? firstContext;
            SceneRenderSnapshot3D? firstPublication;
            using (var frame = frameScratch.Begin(scene, 1280f, 720f, BackendKind.OpenGlDesktop))
            {
                firstContext = frame;
                firstPublication = frame.Published;
                _ = SceneRenderPlanBuilder3D.Build(frame, planScratch);
            }

            for (var i = 0; i < 16; i++)
            {
                using var frame = frameScratch.Begin(scene, 1280f, 720f, BackendKind.OpenGlDesktop);
                _ = SceneRenderPlanBuilder3D.Build(frame, planScratch);
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 64; i++)
            {
                using var frame = frameScratch.Begin(scene, 1280f, 720f, BackendKind.OpenGlDesktop);
                var plan = SceneRenderPlanBuilder3D.Build(frame, planScratch);
                Expect(plan.DrawCommands.Count != 0, "Warm retained plan unexpectedly became empty.");
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Expect(allocated <= 4096, $"Reusable frame publication/planning allocated {allocated} bytes across 64 steady frames.");

            using (var frame = frameScratch.Begin(scene, 1280f, 720f, BackendKind.OpenGlDesktop))
            {
                Expect(ReferenceEquals(firstContext, frame), "Render frame context was not reused.");
                Expect(ReferenceEquals(firstPublication, frame.Published), "Render scalar publication was not reused.");
            }

            scene.Objects[0].IsVisible = false;
            using (var frame = frameScratch.Begin(scene, 1280f, 720f, BackendKind.OpenGlDesktop))
            {
                Expect(frame.Snapshot.RenderablesInternal.Length == 127, "Reusable registry publication retained a stale renderable count.");
            }
        });

        Run("Stable transparent render identity", failures, () =>
        {
            using var scene = CreateSceneWithoutPhysics();
            var material = new Material3D
            {
                BaseColor = new ColorRgba(0.2f, 0.4f, 0.8f, 0.5f),
                Surface = SurfaceMode.Transparent
            };
            var removed = scene.Add(new Box3D { Name = "removed", Material = material });
            var target = scene.Add(new Box3D { Name = "target", Material = material, Position = new Vector3(0f, 0f, -2f) });
            var frameScratch = new SceneRenderFrameScratch3D();
            var planScratch = new SceneRenderPlanScratch3D();

            string FindTargetId()
            {
                using var frame = frameScratch.Begin(scene, 800f, 600f, BackendKind.WebGlBrowser);
                var plan = SceneRenderPlanBuilder3D.Build(frame, planScratch);
                for (var i = 0; i < plan.TransparentOrdinaryItems.Count; i++)
                {
                    var item = plan.TransparentOrdinaryItems[i];
                    if (ReferenceEquals(item.Item.Owner, target)) return item.DrawId;
                }
                throw new InvalidOperationException("Transparent target was not planned.");
            }

            var beforeRemoval = FindTargetId();
            scene.Remove(removed);
            var afterRemoval = FindTargetId();
            Expect(string.Equals(beforeRemoval, afterRemoval, StringComparison.Ordinal),
                "Transparent retained draw identity changed when packed registry source order changed.");
        });

        Run("Exported API snapshot", failures, () =>
        {
            var snapshot = ApiSurfaceSnapshot3D.Capture();
            Expect(snapshot.Contains("class ThreeDEngine.Core.Scene.Scene3D", StringComparison.Ordinal), "Scene3D is missing from the exported API snapshot.");
            Expect(snapshot.Contains("class ThreeDEngine.Core.Hosting.Engine3DBuilder", StringComparison.Ordinal), "Engine3DBuilder is missing from the exported API snapshot.");
            Expect(snapshot.Contains("interface ThreeDEngine.Core.Hosting.IEngineServiceProvider3D", StringComparison.Ordinal), "The engine service provider contract is missing from the exported API snapshot.");
            Expect(snapshot.Contains("class ThreeDEngine.Core.Scene.SceneCommandQueue3D", StringComparison.Ordinal), "Scene command queue is missing from the exported API snapshot.");
            Expect(snapshot.Contains("interface ThreeDEngine.Core.Scene.IEngineClock3D", StringComparison.Ordinal), "Monotonic engine clock contract is missing from the exported API snapshot.");
            Expect(snapshot.Contains("struct ThreeDEngine.Core.Scene.SceneSimulationMetrics3D", StringComparison.Ordinal), "Simulation stage metrics are missing from the exported API snapshot.");
            Expect(snapshot.Contains("struct ThreeDEngine.Core.Scene.SceneUpdateTransaction3D", StringComparison.Ordinal), "Allocation-free scene transaction token is missing from the exported API snapshot.");
            Expect(snapshot.Contains("class ThreeDEngine.Core.Geometry.GeometryBuffer3D", StringComparison.Ordinal), "Immutable geometry buffer is missing from the exported API snapshot.");
            Expect(snapshot.Contains("class ThreeDEngine.Core.Geometry.GeometryIndexBuffer3D", StringComparison.Ordinal), "Compact index buffer is missing from the exported API snapshot.");
            Expect(snapshot.Contains("interface ThreeDEngine.Core.Rendering.IRenderDeviceDiagnostics3D", StringComparison.Ordinal), "Read-only render-device diagnostics are missing from the exported API snapshot.");
            Expect(snapshot.Contains("class ThreeDEngine.Core.Rendering.Rhi.RhiDeviceCapabilities3D", StringComparison.Ordinal), "RHI capability contract is missing from the exported API snapshot.");
            Expect(snapshot.Contains("class ThreeDEngine.Core.Rendering.Rhi.RhiDeviceLimitException3D", StringComparison.Ordinal), "RHI limit diagnostics are missing from the exported API snapshot.");
            Expect(!snapshot.Contains("class ThreeDEngine.Core.Rendering.Rhi.RhiDevice3D", StringComparison.Ordinal), "Concrete RHI device leaked into the public API.");
            Expect(!snapshot.Contains("struct ThreeDEngine.Core.Rendering.Rhi.RhiResourceHandle3D", StringComparison.Ordinal), "Mutable RHI resource handles leaked into the public API.");
            Expect(!snapshot.Contains("class ThreeDEngine.Core.Rendering.SceneRenderPlan3D", StringComparison.Ordinal), "Retained render-plan implementation leaked into the public API.");
            Expect(!snapshot.Contains("struct ThreeDEngine.Core.Rendering.MaterialBinding3D", StringComparison.Ordinal), "Material binding implementation leaked into the public API.");
            Expect(!snapshot.Contains("struct ThreeDEngine.Core.Rendering.OrdinaryRenderItem3D", StringComparison.Ordinal), "Ordinary retained items leaked into the public API.");
            Expect(!snapshot.Contains("struct ThreeDEngine.Core.Rendering.RendererResourceKey", StringComparison.Ordinal), "Backend resource keys leaked into the public API.");
            Expect(!snapshot.Contains("Scene3DPlatform", StringComparison.Ordinal), "The removed global platform service locator leaked into the exported API.");
            Expect(!snapshot.Contains("property ThreeDEngine.Core.Geometry.Mesh3D Shared", StringComparison.Ordinal), "A process-wide mesh cache leaked into the exported API.");
            Expect(!snapshot.Contains("BeginSimulationTick", StringComparison.Ordinal), "Removed manual simulation API leaked into the exported API.");
            Expect(!snapshot.Contains("FrameInterpolationTickFps", StringComparison.Ordinal), "Removed interpolation compatibility alias leaked into the exported API.");
            Expect(!snapshot.Contains("GetCpuSkinnedFallbackMesh", StringComparison.Ordinal), "CPU render fallback leaked into the exported API.");
            var outputPath = ReadOption(args, "--api-output");
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                var fullPath = Path.GetFullPath(outputPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(fullPath, snapshot);
                Console.WriteLine("API snapshot: " + fullPath);
            }
        });

        if (failures.Count == 0)
        {
            Console.WriteLine("Avalonia3D Engine test host: PASS");
            return 0;
        }

        Console.Error.WriteLine($"Avalonia3D Engine test host: FAIL ({failures.Count})");
        foreach (var failure in failures) Console.Error.WriteLine("  - " + failure);
        return 1;
    }

    private static WorldObjectState3D FindWorldObject(WorldSnapshot3D snapshot, string id)
    {
        var objects = snapshot.Objects.Span;
        for (var i = 0; i < objects.Length; i++)
        {
            if (string.Equals(objects[i].Id, id, StringComparison.Ordinal)) return objects[i];
        }
        throw new InvalidOperationException($"World snapshot did not contain object '{id}'.");
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static int CountRepresentedItems(Scene3D scene)
    {
        var count = 0;
        foreach (var root in scene.Objects)
        {
            count += root is ThreeDEngine.Core.HighScale.HighScaleInstanceLayer3D layer
                ? layer.Instances.Count
                : 1;
        }

        return count;
    }

    private static Scene3D CreateSceneWithoutPhysics()
        => new(Scene3DOptions.WithoutPhysics());

    private static Scene3D CreateScene(IPhysicsCore physicsCore)
        => new(new Scene3DOptions { PhysicsFactory = _ => physicsCore });

    private static void Run(string name, List<string> failures, Action body)
    {
        try
        {
            body();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            failures.Add(name + ": " + exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectNear(double actual, double expected, double tolerance, string message)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{message} Expected {expected:R}, got {actual:R}.");
        }
    }

    private static void ConfigureDeterminismTestLoop(SceneUpdateLoop3D loop)
    {
        loop.FixedDeltaSeconds = 0.1d;
        loop.MaximumCatchUpSteps = 32;
        loop.MaximumFrameDeltaSeconds = 2d;
    }

    private static T Single<T>(IReadOnlyCollection<T> values)
    {
        Expect(values.Count == 1, $"Expected one item, got {values.Count}.");
        foreach (var value in values) return value;
        throw new InvalidOperationException("Collection was empty.");
    }

    private static void ExpectThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
        }

        return null;
    }


    private sealed class RecordingRhiExecutor : IRhiCommandExecutor3D
    {
        public int ForwardStageCount { get; private set; }
        public int DrawCount { get; private set; }
        public int RenderPipelineBindCount { get; private set; }
        public int BindGroupBindCount { get; private set; }
        public ulong CompletedSubmissionId { get; private set; }
        public void PushDebugGroup(string label) { }
        public void PopDebugGroup() { }
        public void BeginRenderPass(in RhiRenderPassDescriptor3D descriptor) { }
        public void EndRenderPass() { }
        public void BeginComputePass(in RhiComputePassDescriptor3D descriptor) { }
        public void EndComputePass() { }
        public void SetRenderPipeline(RhiResourceHandle3D pipeline) => RenderPipelineBindCount++;
        public void SetComputePipeline(RhiResourceHandle3D pipeline) { }
        public void SetBindGroup(int slot, RhiResourceHandle3D bindGroup) => BindGroupBindCount++;
        public void SetVertexBuffer(int slot, RhiResourceHandle3D buffer, long offset) { }
        public void SetIndexBuffer(RhiResourceHandle3D buffer, long offset) { }
        public void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance) => DrawCount++;
        public void DrawIndexed(int indexCount, int instanceCount, int firstIndex, int firstInstance) => DrawCount++;
        public void DrawIndirect(RhiResourceHandle3D indirectBuffer, long offset) => DrawCount++;
        public void DrawIndexedIndirect(RhiResourceHandle3D indirectBuffer, long offset) => DrawCount++;
        public void MultiDrawIndexedIndirect(RhiResourceHandle3D indirectBuffer, long offset, int drawCount, int stride) => DrawCount += drawCount;
        public void Dispatch(int x, int y, int z) { }
        public void DispatchIndirect(RhiResourceHandle3D indirectBuffer, long offset) { }
        public void CopyBuffer(RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long destinationOffset, long byteCount) { }
        public void CopyBufferToTexture(RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long byteCount) { }
        public void WriteBuffer(RhiResourceHandle3D destination, long destinationOffset, ReadOnlyMemory<byte> data) { }
        public void ClearBuffer(RhiResourceHandle3D destination, long destinationOffset, long byteCount) { }
        public void Barrier(in RhiResourceBarrier3D barrier) { }
        public void ExecuteBackendStage(RhiBackendStage3D stage, int firstCommand, int commandCount)
        {
            if (stage == RhiBackendStage3D.ForwardScene) ForwardStageCount++;
        }
        public void CompleteSubmission(ulong submissionId) => CompletedSubmissionId = submissionId;
    }

    private sealed class VisibilityWorldJob : IWorldJob3D
    {
        private readonly string _objectId;
        private readonly bool _visible;

        public VisibilityWorldJob(string name, IReadOnlyList<string> dependencies, string objectId, bool visible)
        {
            Name = name;
            Dependencies = dependencies;
            _objectId = objectId;
            _visible = visible;
        }

        public string Name { get; }
        public WorldJobAccess3D Access => WorldJobAccess3D.ReadOnly;
        public IReadOnlyList<string> Dependencies { get; }

        public void Execute(WorldJobContext3D context)
        {
            _ = FindWorldObject(context.Snapshot, _objectId);
            context.Commands.Add(SceneCommands3D.SetVisibility(_objectId, _visible));
        }
    }

    private sealed class TrackingPhysicsCore : IPhysicsCore
    {
        public int DisposeCount { get; private set; }
        public int StepCount { get; private set; }
        public Action? StepCallback { get; init; }

        public void Step(Scene3D scene, float deltaSeconds)
        {
            StepCount++;
            StepCallback?.Invoke();
        }

        public bool Raycast(Scene3D scene, Ray ray, out RaycastHit3D hit)
        {
            hit = default;
            return false;
        }

        public IReadOnlyList<RaycastHit3D> RaycastAll(Scene3D scene, Ray ray)
            => Array.Empty<RaycastHit3D>();

        public void Dispose() => DisposeCount++;
    }

    private sealed class TrackingDisposableService : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class CyclicServiceA
    {
        public CyclicServiceA(CyclicServiceB dependency) => Dependency = dependency;
        public CyclicServiceB Dependency { get; }
    }

    private sealed class CyclicServiceB
    {
        public CyclicServiceB(CyclicServiceA dependency) => Dependency = dependency;
        public CyclicServiceA Dependency { get; }
    }

    private sealed class TestComposite3D : CompositeObject3D
    {
        public TestComposite3D(int partCount) => PartCount = partCount;
        public int PartCount { get; set; }

        protected override void Build(CompositeBuilder3D builder)
        {
            for (var i = 0; i < PartCount; i++) builder.Box("Part" + i, 1f, 1f, 1f).At(i, 0f, 0f);
        }
    }
}
#endif
