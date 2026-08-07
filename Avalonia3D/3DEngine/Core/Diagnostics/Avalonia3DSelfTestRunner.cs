using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using System.Threading;
using ThreeDEngine.Core.Serialization;
using ThreeDEngine.Core.Rendering.Extensions;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Importers.Gltf;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Rhi;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Resources;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Navigation;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Physics.Kinematic;
using Ray3D = ThreeDEngine.Core.Math.Ray;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Spatial;

namespace ThreeDEngine.Core.Diagnostics;

public static class Avalonia3DSelfTestRunner
{
    private static readonly object Gate = new();
    private static bool _isRunning;

    public static Avalonia3DSelfTestResult? LastResult { get; private set; }

    public static Avalonia3DSelfTestResult RunAll()
    {
        lock (Gate)
        {
            if (_isRunning)
            {
                return LastResult ?? new Avalonia3DSelfTestResult(Array.Empty<Avalonia3DSelfTestCaseResult>(), TimeSpan.Zero);
            }

            _isRunning = true;
        }

        var total = Stopwatch.StartNew();
        var cases = new List<Avalonia3DSelfTestCaseResult>();
        try
        {
            RunCase(cases, "Scene rejects duplicate root objects and lights", TestSceneOwnership);
            RunCase(cases, "Scene emits specific change kinds", TestSceneChangeKinds);
            RunCase(cases, "Scene graph applies exact incremental leaf changes", TestIncrementalSceneGraph);
            RunCase(cases, "Scene fixed update is deterministic and pausable", TestDeterministicSceneUpdateLoop);
            RunCase(cases, "RenderStats.Empty is a fresh instance", TestRenderStatsEmptyIsFresh);
            RunCase(cases, "Mesh validates and copies source arrays", TestMeshValidationAndDefensiveCopy);
            RunCase(cases, "Geometry API exposes no mutable array properties", TestGeometryApiIsReadOnly);
            RunCase(cases, "Geometry derives streams lazily and packs compact layouts", TestLazyPackedGeometry);
            RunCase(cases, "Bulk mesh builder transfers exact-size immutable storage", TestMeshGeometryBuilder);
            RunCase(cases, "Meshlets obey limits and cover every triangle", TestMeshlets);
            RunCase(cases, "Mesh optimization preserves source triangle identity", TestMeshSourceTriangleIdentity);
            RunCase(cases, "Geometry owns one lazy BVH per immutable resource", TestGeometryOwnedBvh);
            RunCase(cases, "Generated LODs preserve real mesh streams", TestMeshLodGeneration);
            RunCase(cases, "Normal transforms use the inverse-transpose matrix", TestNormalTransform);
            RunCase(cases, "RHI rejects incomplete GPU capabilities", TestRhiRejectsIncompleteCapabilities);
            RunCase(cases, "RHI resource handles are generation checked", TestRhiResourceLifetime);
            RunCase(cases, "RHI resource owners and budgets are explicit", TestRhiResourceOwnershipAndBudgets);
            RunCase(cases, "Deferred GPU releases honor completed frame fences", TestDeferredGpuReleaseQueue);
            RunCase(cases, "RHI preserves retained categories across partial plans", TestRhiRetainedSubmission);
            RunCase(cases, "Render pipeline rejects unimplemented GPU passes", TestRenderPipelineRejectsUnimplementedPasses);
            RunCase(cases, "Spatial grid clamps pathological bounds", TestSpatialGridPathologicalBounds);
            RunCase(cases, "Animation sampler sorts keys and evaluates quaternions", TestAnimationSamplerRules);
            RunCase(cases, "Cubic-spline animation preserves and evaluates tangents", TestCubicSplineAnimation);
            RunCase(cases, "Material texture updates are atomic and immutable", TestMaterialTextureContracts);
            RunCase(cases, "Immutable texture identity and engine ownership are content based", TestEngineResourceIdentityAndOwnership);
            RunCase(cases, "Partial retained plans replace scene resources transactionally", TestPartialRetainedResourceOwnership);
            RunCase(cases, "Scene disposal releases independent owners after subsystem failure", TestSceneDisposalReleasesResourcesAfterFailure);
            RunCase(cases, "Skybox resource state is complete and immutable", TestSkyboxResourceContracts);
            RunCase(cases, "Particle settings validate and invalidate retained state", TestParticleSettingsContracts);
            RunCase(cases, "High-scale mutations preserve dirty tracking", TestHighScaleMutationContracts);
            RunCase(cases, "Collider mutations notify scene physics", TestColliderMutationContracts);
            RunCase(cases, "Model materials preserve embedded base-color textures", TestEmbeddedTextureMaterialBinding);
            RunCase(cases, "Camera arc flight moves along a curved path", TestCameraArcFlight);
            RunCase(cases, "Camera pose updates are atomic", TestCameraPoseIsAtomic);
            RunCase(cases, "Render-plan scratch reuses transparent commands", TestTransparentPlanScratchReuse);
            RunCase(cases, "GLB importer rejects malformed container length", TestMalformedGlbIsRejected);
            RunCase(cases, "Kinematic character blocks walls and steps over low obstacles", TestKinematicCharacterController);
            RunCase(cases, "Particle billboards follow camera basis", TestParticleBillboardBasis);
            RunCase(cases, "Content-addressed asset cache deduplicates immutable bytes", TestContentAddressedAssetCache);
            RunCase(cases, "Content cache rejects forged hashes and preserves immutable identity", TestContentCacheHardeningContracts);
            RunCase(cases, "Texture mip streaming loads coarse-to-fine and pins residency", TestTextureStreamingContracts);
            RunCase(cases, "Texture residency rolls back a mip that cannot satisfy its budget", TestTextureResidencyRollback);
            RunCase(cases, "Spatial queries fail instead of returning incomplete candidates", TestSpatialQueryCompletenessContracts);
            RunCase(cases, "Spatial cell size changes require an empty index", TestSpatialCellSizeMutationContract);
            RunCase(cases, "Scene builder releases an abandoned scene", TestSceneBuilderLifetime);
            RunCase(cases, "Scene documents round-trip supported primitives", TestSceneSerializationRoundTrip);
            RunCase(cases, "Scene documents preserve complete material strengths", TestSceneSerializationMaterialCompleteness);
            RunCase(cases, "Scene documents reject ambiguous texture slots", TestSceneSerializationTextureSlotValidation);
            RunCase(cases, "Profiler computes deterministic acceptance percentiles", TestProductionProfilerContracts);
            RunCase(cases, "Production acceptance rejects invalid telemetry", TestProductionAcceptanceRejectsInvalidMetrics);
            RunCase(cases, "Unavailable GPU timing is represented explicitly", TestUnavailableGpuTimingContract);
            RunCase(cases, "Engine asynchronous shutdown is observable", TestEngineAsyncShutdownContract);
            RunCase(cases, "GPU picking requires an explicit GPU backend and preserves request order", TestGpuPickingContracts);
            RunCase(cases, "Render and material extensions validate versioned contracts", TestExtensionContracts);
            RunCase(cases, "Extension registry snapshots are immutable and monotonic", TestExtensionSnapshotHardening);
            RunCase(cases, "Material extension identity is deterministic and content-sensitive", TestMaterialExtensionIdentity);
            total.Stop();
            var result = new Avalonia3DSelfTestResult(cases, total.Elapsed);
            if (result.Passed)
            {
                EngineLog3D.Information("SelfTest", $"{result.PassedCount} self-tests passed in {result.Elapsed.TotalMilliseconds:0.##} ms.");
            }
            else
            {
                EngineLog3D.Error("SelfTest", $"{result.FailedCount} of {result.Cases.Count} self-tests failed.\n{result.ToReport()}");
            }

            LastResult = result;
            return result;
        }
        finally
        {
            lock (Gate)
            {
                _isRunning = false;
            }
        }
    }

    private static void RunCase(List<Avalonia3DSelfTestCaseResult> cases, string name, Action body)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            body();
            sw.Stop();
            cases.Add(new Avalonia3DSelfTestCaseResult(name, true, sw.Elapsed, null));
        }
        catch (Exception ex)
        {
            sw.Stop();
            cases.Add(new Avalonia3DSelfTestCaseResult(name, false, sw.Elapsed, ex.GetType().Name + ": " + ex.Message));
        }
    }

    private static void TestLazyPackedGeometry()
    {
        var geometry = new RenderGeometry3D(
            new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) },
            Array.Empty<Vector3>(),
            new[] { 0, 1, 2 },
            "selftest:lazy-packed",
            texCoords0: new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY },
            buildOptions: new GeometryBuildOptions3D { PackHalfPrecisionTexCoords = true });
        Expect(!geometry.IsNormalsMaterialized && !geometry.IsTangentsMaterialized,
            "Derived normal/tangent streams materialized during construction.");
        Expect(geometry.Layout.StrideBytes < VertexLayout3D.GpuMesh.StrideBytes,
            "A simple textured mesh retained the legacy 100-byte layout.");
        var packed = geometry.GetInterleavedVertexBuffer();
        Expect(geometry.IsNormalsMaterialized && geometry.IsTangentsMaterialized,
            "Packed upload did not materialize required derived streams exactly once.");
        Expect(packed.ByteCount == (long)geometry.VertexCount * geometry.Layout.StrideBytes,
            "Packed upload byte count does not match the selected layout.");
        Expect(geometry.Layout.Find(VertexAttributeKind3D.Normal)?.Format == VertexAttributeFormat3D.SNorm16x4,
            "Unit normals were not packed to signed normalized 16-bit storage.");
        Expect(geometry.Layout.Find(VertexAttributeKind3D.TexCoord0)?.Format == VertexAttributeFormat3D.Half2,
            "Finite UV coordinates were not packed to half precision.");
    }

    private static void TestMeshGeometryBuilder()
    {
        var builder = new MeshGeometryBuilder3D(3, 3, GeometryStreamMask3D.None);
        builder.Positions[0] = Vector3.Zero;
        builder.Positions[1] = Vector3.UnitX;
        builder.Positions[2] = Vector3.UnitY;
        builder.Indices[0] = 0;
        builder.Indices[1] = 1;
        builder.Indices[2] = 2;
        var mesh = builder.Build("selftest:builder");
        Expect(mesh.Positions.Length == 3 && mesh.Indices.Length == 3, "Bulk builder produced incorrect stream lengths.");
        Expect(mesh.Normals.Length == 3, "Missing normals were not generated lazily for builder geometry.");
        ExpectThrows<InvalidOperationException>(() => builder.Build("selftest:builder:second"));
        var copy = mesh.Positions.ToArray();
        copy[0] = new Vector3(99f);
        Expect(mesh.Positions[0] == Vector3.Zero, "Public geometry copies can mutate builder-owned storage.");
    }

    private static void TestMeshlets()
    {
        var geometry = MeshFactory.CreateSphere(1f, 16, 8).RenderGeometry;
        var set = geometry.GetMeshlets();
        var triangleTotal = 0;
        for (var i = 0; i < set.Meshlets.Length; i++)
        {
            var meshlet = set.Meshlets[i];
            Expect(meshlet.VertexCount <= geometry.BuildOptions.MeshletMaxVertices, "Meshlet exceeds its vertex limit.");
            Expect(meshlet.TriangleCount <= geometry.BuildOptions.MeshletMaxTriangles, "Meshlet exceeds its triangle limit.");
            triangleTotal += meshlet.TriangleCount;
            var localOffset = meshlet.TriangleOffset * 3;
            for (var index = 0; index < meshlet.TriangleCount * 3; index++)
                Expect(set.LocalTriangleIndices[localOffset + index] < meshlet.VertexCount, "Meshlet contains an invalid local vertex index.");
        }
        Expect(triangleTotal == geometry.TriangleCount, "Meshlet preprocessing did not cover every source triangle exactly once.");
    }

    private static void TestMeshSourceTriangleIdentity()
    {
        var mesh = new Mesh3D(
            new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(10f, 0f, 0f), new Vector3(11f, 0f, 0f), new Vector3(10f, 1f, 0f),
                new Vector3(-1f, 1f, 0f)
            },
            Array.Empty<Vector3>(),
            new[] { 0, 1, 2, 3, 4, 5, 0, 2, 6 },
            "selftest:triangle-identity");
        Expect(mesh.RenderGeometry.GetSourceTriangleIndex(0) == 0, "The first optimized triangle lost its source identity.");
        Expect(mesh.RenderGeometry.GetSourceTriangleIndex(1) == 2, "Cache optimization did not preserve the reordered source triangle identity.");
        Expect(mesh.RenderGeometry.GetSourceTriangleIndex(2) == 1, "The disconnected source triangle identity was not retained.");
    }

    private static void TestGeometryOwnedBvh()
    {
        var geometry = MeshFactory.CreateSphere(1f, 16, 8).RenderGeometry;
        Expect(!geometry.IsBvhMaterialized, "BVH materialized before a spatial query requested it.");
        var method = typeof(RenderGeometry3D).GetMethod("GetBvh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Geometry-owned BVH accessor is missing.");
        var first = method.Invoke(geometry, null);
        var second = method.Invoke(geometry, null);
        Expect(first is not null && ReferenceEquals(first, second), "Geometry did not retain one stable lazy BVH instance.");
        Expect(geometry.IsBvhMaterialized, "Geometry did not report its materialized BVH.");
    }

    private static void TestMeshLodGeneration()
    {
        var source = MeshFactory.CreateSphere(1f, 24, 12);
        var lod = MeshLodGenerator3D.Generate(source, 0.35f, "selftest:sphere-lod");
        Expect(lod.RenderGeometry.TriangleCount > 0 && lod.RenderGeometry.TriangleCount < source.RenderGeometry.TriangleCount,
            "LOD generation did not reduce source triangle count.");
        Expect(lod.HasTexCoords0 && lod.HasTangents, "LOD generation discarded texture/tangent streams.");
        Expect(lod.Positions.Length > 8, "LOD generation substituted a bounds box instead of real clustered geometry.");
        Expect(lod.LocalBounds.IsValid, "LOD generation produced invalid bounds.");

        var singleTriangle = new Mesh3D(
            new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
            Array.Empty<Vector3>(),
            new[] { 0, 1, 2 },
            "selftest:single-triangle-lod");
        ExpectThrows<InvalidOperationException>(() => MeshLodGenerator3D.Generate(singleTriangle, 0.5f));
    }

    private static void TestNormalTransform()
    {
        var transform = Matrix4x4.CreateScale(2f, 1f, 0.5f) * Matrix4x4.CreateRotationY(0.35f);
        var normal = Vector3.Normalize(new Vector3(1f, 1f, 1f));
        var transformed = GeometryTransform3D.TransformNormal(normal, GeometryTransform3D.CreateNormalMatrix(transform));
        var tangentStyle = Vector3.Normalize(Vector3.TransformNormal(normal, transform));
        Expect(Vector3.Distance(transformed, tangentStyle) > 0.05f,
            "Normal preprocessing still uses the model matrix under non-uniform scale.");
        Expect(global::System.MathF.Abs(transformed.Length() - 1f) < 0.0001f, "Transformed normal was not normalized.");
        ExpectThrows<ArgumentException>(() => GeometryTransform3D.CreateNormalMatrix(Matrix4x4.CreateScale(1f, 0f, 1f)));
    }

    private static void TestSceneOwnership()
    {
        using var scene = new Scene3D();
        using var secondScene = new Scene3D();
        var box = new Box3D();
        scene.Add(box);
        ExpectThrows<InvalidOperationException>(() => scene.Add(box));
        ExpectThrows<InvalidOperationException>(() => secondScene.Add(box));
        Expect(scene.Remove(box), "Object must be removable after duplicate-add rejection.");
        secondScene.Add(box);
        Expect(secondScene.Remove(box), "Object must be reusable after removal from the previous scene.");
        Expect(!scene.Remove(box), "Removing an object twice must return false.");

        var light = new DirectionalLight3D();
        scene.AddLight(light);
        ExpectThrows<InvalidOperationException>(() => scene.AddLight(light));
        ExpectThrows<InvalidOperationException>(() => secondScene.AddLight(light));
        Expect(scene.RemoveLight(light), "Light must be removable after duplicate-add rejection.");
        secondScene.AddLight(light);
        Expect(secondScene.RemoveLight(light), "Light must be reusable after removal from the previous scene.");
    }

    private static void TestSceneChangeKinds()
    {
        using var scene = new Scene3D();
        var box = scene.Add(new Box3D());
        SceneChangeKind? last = null;
        scene.SceneChangedDetailed += (_, e) => last = e.Kind;
        box.Position = new Vector3(1f, 2f, 3f);
        Expect(last == SceneChangeKind.Transform, "Position changes must report Transform.");
        box.IsVisible = false;
        Expect(last == SceneChangeKind.Visibility, "Visibility changes must report Visibility.");
        box.Fill = ColorRgba.Black;
        Expect(last == SceneChangeKind.Material, "Material/color changes must report Material.");
        box.Transform.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);
        Expect(MathF.Abs(box.RotationDegrees.Y) > 0.01f || MathF.Abs(box.RotationDegrees.X) > 0.01f || MathF.Abs(box.RotationDegrees.Z) > 0.01f, "RotationDegrees must sync after direct Transform.LocalRotation changes.");
    }

    private static void TestIncrementalSceneGraph()
    {
        using var scene = new Scene3D(Scene3DOptions.WithoutPhysics());
        var composite = scene.Add(new SelfTestComposite3D());
        var child = composite.Children[0];
        var registryVersion = scene.Registry.Version;
        var fullRebuilds = scene.Registry.FullRebuildCount;
        var cursor = scene.ChangeSequence;
        SceneChangedEventArgs? observed = null;
        scene.SceneChangedDetailed += (_, e) => observed = e;
        child.Position = Vector3.One;

        Expect(observed is not null && ReferenceEquals(observed.Source, child), "Composite change source must remain the exact leaf.");
        Expect(scene.Registry.Version == registryVersion, "Transform-only update must not rebuild registry membership.");
        Expect(scene.Registry.FullRebuildCount == fullRebuilds, "Transform-only update forced a full registry traversal.");
        var changes = new List<SceneChangeRecord3D>();
        Expect(scene.TryCopyChangesSince(cursor, changes) && changes.Count == 1 && ReferenceEquals(changes[0].Source, child),
            "Scene journal must preserve one exact leaf record.");
    }

    private static void TestRenderStatsEmptyIsFresh()
    {
        var first = RenderStats.Empty;
        first.ObjectCount = 123;
        var second = RenderStats.Empty;
        Expect(!ReferenceEquals(first, second), "RenderStats.Empty must not return a shared mutable singleton.");
        Expect(second.ObjectCount == 0, "RenderStats.Empty must not retain previous mutations.");
    }

    private static void TestDeterministicSceneUpdateLoop()
    {
        using var scene = new Scene3D(Scene3DOptions.WithoutPhysics());
        scene.UpdateLoop.FixedDeltaSeconds = 0.1d;
        scene.UpdateLoop.MaximumCatchUpSteps = 16;
        scene.UpdateLoop.MaximumFrameDeltaSeconds = 2d;
        var observedTicks = 0;
        scene.FixedUpdate += OnFixedUpdate;

        scene.Update(0.35d);
        scene.Update(0.05d);
        Expect(observedTicks == 4 && scene.UpdateLoop.SimulationTick == 4,
            "Equivalent accumulated elapsed time must produce exactly four 100 ms ticks.");

        scene.UpdateLoop.IsPaused = true;
        scene.Update(1d);
        Expect(scene.UpdateLoop.SimulationTick == 4, "Paused update loop must not consume elapsed host time.");
        scene.UpdateLoop.StepOnce();
        Expect(scene.UpdateLoop.SimulationTick == 5, "Single-step must execute exactly one fixed tick while paused.");

        void OnFixedUpdate(Scene3D owner, in SceneFixedUpdateContext3D context)
            => observedTicks++;
    }

    private static void TestMeshValidationAndDefensiveCopy()
    {
        var positions = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f)
        };
        var indices = new[] { 0, 1, 2 };
        var mesh = new Mesh3D(positions, Array.Empty<Vector3>(), indices, "selftest:triangle");
        positions[0] = new Vector3(999f, 999f, 999f);
        indices[0] = 2;
        Expect(mesh.Positions[0] == Vector3.Zero, "Mesh must copy caller-owned position arrays.");
        Expect(mesh.Indices[0] == 0, "Mesh must copy caller-owned index arrays.");
        Expect(mesh.Indices.Format == IndexFormat3D.UInt16 && mesh.Indices.ByteCount == sizeof(ushort) * 3L,
            "Small geometry was not retained in compact UInt16 index storage.");
        Expect(!mesh.RenderGeometry.IsWireframeMaterialized, "Wireframe indices were allocated for a normal mesh construction.");
        var firstUpload = mesh.RenderGeometry.GetInterleavedVertexBuffer();
        var secondUpload = mesh.RenderGeometry.GetInterleavedVertexBuffer();
        Expect(ReferenceEquals(firstUpload, secondUpload), "Interleaved GPU upload data was rebuilt instead of cached.");
        Expect(firstUpload.ByteCount == (long)mesh.Positions.Length * mesh.RenderGeometry.Layout.StrideBytes,
            "Packed GPU upload data has an invalid stride or length.");
        Expect(mesh.RenderGeometry.Layout.StrideBytes < VertexLayout3D.GpuMesh.StrideBytes,
            "A position/normal mesh retained the legacy 100-byte vertex layout.");
        Expect(mesh.RenderGeometry.WireframeIndices.Count == 6 && mesh.RenderGeometry.IsWireframeMaterialized,
            "Debug wireframe geometry was not materialized lazily.");

        var skin = new[]
        {
            new VertexSkinWeights3D(Vector4.Zero, new Vector4(1f, 0f, 0f, 0f)),
            new VertexSkinWeights3D(Vector4.Zero, new Vector4(1f, 0f, 0f, 0f)),
            new VertexSkinWeights3D(Vector4.Zero, new Vector4(1f, 0f, 0f, 0f))
        };
        var primitive = new MeshPrimitiveAsset3D("selftest:primitive", positions, null, null, new[] { 0, 1, 2 }, 0, skinWeights0: skin);
        var primitiveMesh = primitive.ToMesh();
        Expect(ReferenceEquals(primitive.RenderGeometry, primitiveMesh.RenderGeometry),
            "Imported primitive recreated geometry instead of sharing its canonical resource.");
        Expect(ReferenceEquals(primitive.BoneIndices0, primitive.RenderGeometry.BoneIndices0) &&
               ReferenceEquals(primitive.BoneWeights0, primitive.RenderGeometry.BoneWeights0),
            "Imported primitive retained duplicate skinning streams outside canonical geometry.");

        ExpectThrows<ArgumentOutOfRangeException>(() => new Mesh3D(new[] { Vector3.Zero }, Array.Empty<Vector3>(), new[] { 0, 1, 2 }, "selftest:bad-index"));
        ExpectThrows<ArgumentException>(() => new Mesh3D(new[] { new Vector3(float.NaN, 0f, 0f), Vector3.UnitX, Vector3.UnitY }, Array.Empty<Vector3>(), new[] { 0, 1, 2 }, "selftest:nan"));
        ExpectThrows<ArgumentException>(() => new Mesh3D(positions, new[] { Vector3.UnitZ }, new[] { 0, 1, 2 }, "selftest:bad-stream-count"));
        ExpectThrows<ArgumentOutOfRangeException>(() => MeshFactory.CreateSphere(0f));
        ExpectThrows<ArgumentOutOfRangeException>(() => MeshFactory.CreateCylinder(1f, 1f, 3));
    }

    private static void TestSpatialGridPathologicalBounds()
    {
        var grid = new SpatialHashGrid3D(1f);
        var obj = new Box3D();
        grid.Add(obj, new Bounds3D(new Vector3(-1_000_000f), new Vector3(1_000_000f)));
        var result = grid.QueryRay(new Ray3D(Vector3.Zero, Vector3.UnitX), 10f, 32);
        Expect(result.Count == 0, "Pathological bounds must be skipped instead of expanding to millions of cells.");
    }

    private static void TestGeometryApiIsReadOnly()
    {
        foreach (var type in new[]
                 {
                     typeof(Mesh3D),
                     typeof(RenderGeometry3D),
                     typeof(MeshPrimitiveAsset3D),
                     typeof(WebGlGeometryPayload3D)
                 })
        {
            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                Expect(!property.PropertyType.IsArray, $"{type.Name}.{property.Name} exposes a mutable array.");
            }
        }
    }

    private static void TestRhiRejectsIncompleteCapabilities()
    {
        var limits = new RhiDeviceLimits3D(4096, 16, 4, 16, 4096, 4);
        var incomplete = new RhiDeviceCapabilities3D(
            RhiBackendApi3D.WebGl2,
            "self-test",
            "2.0",
            RhiFeature3D.Texture2D,
            limits);
        ExpectThrows<RhiCapabilityException3D>(() => new RhiDevice3D(incomplete));

        var inadequateLimits = new RhiDeviceCapabilities3D(
            RhiBackendApi3D.OpenGl,
            "self-test",
            "3.3",
            RhiDeviceCapabilities3D.RequiredRasterFeatures,
            new RhiDeviceLimits3D(4096, 4, 0, 8, 4096, 0));
        ExpectThrows<RhiDeviceLimitException3D>(() => new RhiDevice3D(inadequateLimits));
    }

    private static void TestRhiResourceLifetime()
    {
        var capabilities = new RhiDeviceCapabilities3D(
            RhiBackendApi3D.OpenGl,
            "self-test",
            "4.5",
            RhiDeviceCapabilities3D.RequiredRasterFeatures | RhiFeature3D.TimerQueries,
            new RhiDeviceLimits3D(8192, 32, 8, 16, 8192, 8));
        using var device = new RhiDevice3D(capabilities);
        var buffer = device.Resources.RegisterBuffer(
            "self-test:vertex",
            new RhiBufferDescriptor3D(120, RhiBufferUsage3D.Vertex, 12),
            contentVersion: 1);
        var sameHandle = device.Resources.RegisterBuffer(
            "self-test:vertex",
            new RhiBufferDescriptor3D(240, RhiBufferUsage3D.Vertex | RhiBufferUsage3D.Dynamic, 12),
            contentVersion: 2);
        Expect(buffer.Equals(sameHandle), "Descriptor updates must preserve a live logical RHI handle.");
        Expect(device.Resources.Contains(buffer), "Fresh RHI resource handle was not live.");
        var beforeReset = device.Resources.CaptureSnapshot();
        Expect(beforeReset.LiveCount == 1 && beforeReset.ResidentBytes == 240 && beforeReset.Creates == 1 && beforeReset.Updates == 1,
            "RHI residency/update telemetry is inconsistent.");

        device.InvalidateContext("self-test");
        Expect(!device.Resources.Contains(buffer), "A handle from the previous context generation remained live.");
        ExpectThrows<InvalidOperationException>(() => device.Resources.RequireLive(buffer, "self-test draw"));
        device.Dispose();
        Expect(device.Resources.IsDisposed, "Disposing an RHI device left its resource registry mutable.");
        ExpectThrows<ObjectDisposedException>(() => device.Resources.RegisterBuffer(
            "self-test:after-dispose",
            new RhiBufferDescriptor3D(16, RhiBufferUsage3D.Vertex, 4),
            1));
    }

    private static void TestRhiResourceOwnershipAndBudgets()
    {
        var capabilities = new RhiDeviceCapabilities3D(
            RhiBackendApi3D.OpenGl,
            "self-test",
            "4.5",
            RhiDeviceCapabilities3D.RequiredRasterFeatures,
            new RhiDeviceLimits3D(8192, 32, 8, 16, 8192, 8));
        var configuration = new EngineResourceConfiguration3D(
            MaxCpuTextureBytes: 1024 * 1024,
            MaxGpuResidentBytes: 1024 * 1024,
            MaxGpuTextureBytes: 1024 * 1024,
            DeferredReleaseFrames: 2);
        ExpectThrows<ArgumentException>(() => new EngineResourceConfiguration3D(
            MaxCpuTextureBytes: 1024 * 1024,
            MaxGpuResidentBytes: 1024 * 1024,
            MaxGpuTextureBytes: 2 * 1024 * 1024,
            DeferredReleaseFrames: 2));
        using var device = new RhiDevice3D(capabilities, configuration);
        var descriptor = new RhiTextureDescriptor3D(256, 256, RhiTextureFormat3D.Rgba8Unorm, RhiTextureUsage3D.Sampled);
        var beforePreflight = device.Resources.CaptureSnapshot();
        device.Resources.ValidateTextureRegistration("texture:self-test", descriptor, 1);
        var afterPreflight = device.Resources.CaptureSnapshot();
        Expect(afterPreflight.LiveCount == beforePreflight.LiveCount && afterPreflight.ResidentBytes == beforePreflight.ResidentBytes,
            "RHI budget preflight mutated the ownership ledger before native upload succeeded.");
        var handle = device.Resources.RegisterTexture("texture:self-test", descriptor, 1, "scene:a");
        device.Resources.AddOwner(handle, "scene:b");
        var shared = device.Resources.CaptureSnapshot();
        Expect(shared.LiveCount == 1 && shared.OwnershipReferences == 2 && shared.TextureBytes == descriptor.EstimatedByteSize,
            "RHI shared ownership or texture accounting is inconsistent.");
        Expect(device.Resources.ReleaseOwner(handle, "scene:a") && device.Resources.Contains(handle),
            "Releasing one RHI owner destroyed a resource still referenced by another owner.");
        Expect(device.Resources.ReleaseOwner(handle, "scene:b") && !device.Resources.Contains(handle),
            "Releasing the final RHI owner did not destroy the logical resource.");

        var oversized = new RhiTextureDescriptor3D(1024, 1024, RhiTextureFormat3D.Rgba8Unorm, RhiTextureUsage3D.Sampled);
        ExpectThrows<InvalidOperationException>(() => device.Resources.RegisterTexture("texture:oversized", oversized, 1, "scene:a"));
    }

    private static void TestDeferredGpuReleaseQueue()
    {
        var queue = new GpuDeferredReleaseQueue3D<int>();
        queue.Enqueue(7, submittedFrame: 10, delayFrames: 2);
        var released = 0;
        queue.DrainReady(11, value => released += value);
        Expect(released == 0 && queue.Count == 1, "Deferred GPU release ran before its safe frame.");
        queue.DrainReady(12, value => released += value);
        Expect(released == 7 && queue.Count == 0, "Deferred GPU release did not run at its safe frame.");
        queue.Enqueue(5, submittedFrame: 20, delayFrames: 4);
        Expect(queue.TryCancel(value => value == 5, out var restored) && restored == 5 && queue.Count == 0,
            "A resurrected GPU resource could not cancel its pending destruction.");
        queue.Enqueue(5, submittedFrame: 20, delayFrames: 4);
        queue.DrainAll(value => released += value);
        Expect(released == 12 && queue.Count == 0, "Explicit teardown did not drain deferred GPU releases.");
    }


    private static void TestRhiRetainedSubmission()
    {
        using var scene = new Scene3D(Scene3DOptions.WithoutPhysics());
        scene.Add(new Box3D());
        var scratch = new SceneRenderPlanScratch3D();
        using var frame = SceneRenderFrameContext3D.Build(scene, 640f, 480f, BackendKind.OpenGlDesktop);
        var full = SceneRenderPlanBuilder3D.Build(frame, scratch, includeOrdinary: true, includeParticles: true, includeHighScale: true);
        var retainedDrawCount = full.RhiSubmission.DrawCommandCount;
        Expect(retainedDrawCount > 0, "Full RHI submission did not contain the visible ordinary category.");

        var partial = SceneRenderPlanBuilder3D.Build(frame, scratch, includeOrdinary: false, includeParticles: false, includeHighScale: false);
        Expect(ReferenceEquals(full, partial), "Scratch render plan did not preserve its allocation-reusable RHI submission.");
        Expect(partial.RhiSubmission.DrawCommandCount == retainedDrawCount,
            "Partial retained plan erased draw contracts for categories that intentionally reused backend state.");

    }

    private static void TestAnimationSamplerRules()
    {
        var sampler = new AnimationSampler3D(
            new[] { 1f, 0f },
            new[] { new Vector4(10f, 0f, 0f, 0f), new Vector4(0f, 0f, 0f, 0f) },
            AnimationInterpolation3D.Linear);
        var mid = sampler.Evaluate(0.5f, Vector4.Zero);
        Expect(MathF.Abs(mid.X - 5f) < 0.001f, "AnimationSampler must sort keyframes before evaluation.");

        var q0 = Quaternion.Identity;
        var q1 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        var qSampler = new AnimationSampler3D(
            new[] { 0f, 1f },
            new[] { new Vector4(q0.X, q0.Y, q0.Z, q0.W), new Vector4(q1.X, q1.Y, q1.Z, q1.W) },
            AnimationInterpolation3D.Linear);
        var qMid = qSampler.EvaluateQuaternion(0.5f, Quaternion.Identity);
        Expect(MathF.Abs(qMid.Length() - 1f) < 0.001f, "Quaternion animation evaluation must return normalized quaternions.");
    }

    private static void TestCubicSplineAnimation()
    {
        var sampler = new AnimationSampler3D(
            new[] { 0f, 1f },
            new[] { Vector4.Zero, new Vector4(1f, 0f, 0f, 0f) },
            AnimationInterpolation3D.CubicSpline,
            new[] { Vector4.Zero, Vector4.Zero },
            new[] { new Vector4(2f, 0f, 0f, 0f), Vector4.Zero });
        var midpoint = sampler.Evaluate(0.5f, Vector4.Zero);
        Expect(MathF.Abs(midpoint.X - 0.75f) < 0.001f, "Cubic-spline sampler did not evaluate Hermite tangents.");
        Expect(sampler.InTangents is { Count: 2 } && sampler.OutTangents is { Count: 2 }, "Cubic-spline tangents were not retained.");
        ExpectThrows<ArgumentException>(() => new AnimationSampler3D(
            new[] { 0f, 1f }, new[] { Vector4.Zero, Vector4.One }, AnimationInterpolation3D.CubicSpline));
    }

    private static void TestMaterialTextureContracts()
    {
        var material = new Material3D();
        var changes = 0;
        material.Changed += (_, _) => changes++;
        var source = new byte[] { 1, 2, 3, 4 };
        material.SetNormalMapTexture("normal", source, "image/png", 0f);
        Expect(changes == 1, "Atomic normal-map assignment emitted more than one change.");
        source[0] = 99;
        var first = material.NormalMapTextureData!;
        first[1] = 88;
        var second = material.NormalMapTextureData!;
        Expect(second[0] == 1 && second[1] == 2, "Material texture bytes are externally mutable.");
        ExpectThrows<ArgumentOutOfRangeException>(() => material.Roughness = float.NaN);
        ExpectThrows<ArgumentOutOfRangeException>(() => material.Lighting = (LightingMode)999);
    }

    private static void TestEngineResourceIdentityAndOwnership()
    {
        var bytes = new byte[] { 10, 20, 30, 40 };
        var first = TextureResource3D.Create("albedo:a", bytes, "image/png");
        var alias = TextureResource3D.Create("albedo:b", bytes, "image/png");
        var different = TextureResource3D.Create("albedo:a", new byte[] { 10, 20, 30, 41 }, "image/png");
        var differentMime = TextureResource3D.Create("albedo:a", bytes, "image/jpeg");
        Expect(first.ResourceKey == alias.ResourceKey && first.ContentVersion == alias.ContentVersion,
            "Identical encoded content did not produce one physical texture identity.");
        Expect(ReferenceEquals(first.EncodedDataInternal, alias.EncodedDataInternal),
            "Identical independently-created textures did not share one immutable CPU payload.");
        Expect(first.ResourceKey != different.ResourceKey && first.ResourceKey != differentMime.ResourceKey,
            "Different texture content or metadata aliased to one physical identity.");
        Expect(first.Descriptor.IsValid && first.Descriptor.EncodedByteLength == bytes.Length,
            "Immutable texture descriptor does not match its resource.");

        using var engine = new Engine3DBuilder()
            .ConfigureResources(options =>
            {
                options.MaxCpuTextureBytes = 1024 * 1024;
                options.MaxCpuShaderBytes = 1024 * 1024;
                options.MaxGpuResidentBytes = 1024 * 1024;
                options.MaxGpuTextureBytes = 1024 * 1024;
                options.DeferredReleaseFrames = 2;
            })
            .Build();
        var canonical = engine.Resources.Intern(first);
        var canonicalAlias = engine.Resources.Intern(alias);
        Expect(ReferenceEquals(canonical, canonicalAlias), "Engine resource catalog did not intern identical texture content.");
        var vertexShader = ShaderResource3D.Create("shader:a", ShaderStage3D.Vertex, "void main(){} ");
        var shaderAlias = ShaderResource3D.Create("shader:b", ShaderStage3D.Vertex, "void main(){} ");
        var fragmentShader = ShaderResource3D.Create("shader:a", ShaderStage3D.Fragment, "void main(){} ");
        Expect(vertexShader.ResourceKey == shaderAlias.ResourceKey && vertexShader.ResourceKey != fragmentShader.ResourceKey,
            "Shader identity did not include immutable source metadata and stage.");
        Expect(vertexShader.Utf8SourceInternal.Equals(shaderAlias.Utf8SourceInternal),
            "Identical independently-created shaders did not share one immutable CPU payload.");
        Expect(ReferenceEquals(engine.Resources.Intern(vertexShader), engine.Resources.Intern(shaderAlias)),
            "Engine resource catalog did not intern identical shader content.");

        using var ownerA = engine.Resources.CreateOwner("self-test:a");
        using var ownerB = engine.Resources.CreateOwner("self-test:b");
        ownerA.SetTextures(new[] { first });
        ownerA.SetShaders(new[] { vertexShader });
        ownerB.SetTextures(new[] { alias });
        ownerB.SetShaders(new[] { shaderAlias });
        var shared = engine.Resources.CaptureSnapshot();
        Expect(shared.TextureCount == 1 && shared.ReferencedTextureCount == 1 && shared.ResidentTextureBytes == bytes.Length,
            "Engine resource catalog failed to deduplicate or reference immutable texture content.");
        Expect(shared.ShaderCount == 1 && shared.ReferencedShaderCount == 1 && shared.ResidentShaderBytes == vertexShader.ByteLength,
            "Engine resource catalog failed to deduplicate or reference immutable shader content.");
        ownerA.Dispose();
        var afterFirstOwner = engine.Resources.CaptureSnapshot();
        Expect(afterFirstOwner.ReferencedTextureCount == 1 && afterFirstOwner.ReferencedShaderCount == 1,
            "Releasing one owner invalidated shared immutable resource content.");
        ownerB.Dispose();
        var afterFinalOwner = engine.Resources.CaptureSnapshot();
        Expect(afterFinalOwner.ReferencedTextureCount == 0 && afterFinalOwner.ReferencedShaderCount == 0,
            "Releasing the final owner left referenced immutable resources.");

        var plan = new RenderResourcePlan3D(true, true, true);
        plan.AddTexture(first);
        ExpectThrows<InvalidOperationException>(() => plan.AddTexture(different));
        var tooLarge = new byte[1024 * 1024 + 1];
        ExpectThrows<InvalidOperationException>(() => engine.Resources.Intern(TextureResource3D.Create("oversized", tooLarge, "application/octet-stream")));
    }


    private static void TestPartialRetainedResourceOwnership()
    {
        const int textureBytes = 700 * 1024;
        using var engine = new Engine3DBuilder()
            .DisablePhysicsByDefault()
            .ConfigureResources(options =>
            {
                options.MaxCpuTextureBytes = 1024 * 1024;
                options.MaxCpuShaderBytes = 1024 * 1024;
                options.MaxGpuResidentBytes = 2 * 1024 * 1024;
                options.MaxGpuTextureBytes = 2 * 1024 * 1024;
            })
            .Build();
        using var scene = engine.CreateScene(Scene3DOptions.WithoutPhysics());
        var firstBytes = new byte[textureBytes];
        var secondBytes = new byte[textureBytes];
        firstBytes[0] = 1;
        secondBytes[0] = 2;
        var first = TextureResource3D.Create("sky:first", firstBytes, "application/x-avalonia3d-self-test");
        var second = TextureResource3D.Create("sky:second", secondBytes, "application/x-avalonia3d-self-test");
        var scratch = new SceneRenderPlanScratch3D();

        scene.Environment.Skybox.SetEquirectangularTexture(first);
        using (var firstFrame = SceneRenderFrameContext3D.Build(scene, 320f, 200f, BackendKind.WebGlBrowser))
        {
            SceneRenderPlanBuilder3D.Build(
            firstFrame,
            scratch,
            includeOrdinary: false,
                includeParticles: false,
                includeHighScale: false);
        }
        var firstSnapshot = engine.Resources.CaptureSnapshot();
        Expect(firstSnapshot.ReferencedTextureCount == 1 && firstSnapshot.ResidentTextureBytes == textureBytes,
            "A partial retained plan did not acquire the active skybox resource.");

        scene.Environment.Skybox.SetEquirectangularTexture(second);
        using (var secondFrame = SceneRenderFrameContext3D.Build(scene, 320f, 200f, BackendKind.WebGlBrowser))
        {
            SceneRenderPlanBuilder3D.Build(
            secondFrame,
            scratch,
            includeOrdinary: false,
                includeParticles: false,
                includeHighScale: false);
        }
        var secondSnapshot = engine.Resources.CaptureSnapshot();
        Expect(secondSnapshot.ReferencedTextureCount == 1 && secondSnapshot.TextureCount == 1,
            "Replacing a scene texture through a partial retained plan left stale ownership or catalog residency.");
        Expect(secondSnapshot.ResidentTextureBytes == textureBytes &&
               secondSnapshot.ResidentTextureBytes <= secondSnapshot.TextureBudgetBytes,
            "Transactional scene-resource replacement exceeded or corrupted the CPU texture budget.");
    }


    private static void TestSkyboxResourceContracts()
    {
        var skybox = new Skybox3D();
        ExpectThrows<InvalidOperationException>(() => skybox.Mode = SkyboxMode3D.Equirectangular);
        var source = new byte[] { 7, 8, 9 };
        skybox.SetEquirectangularTexture("sky", source, "image/png");
        source[0] = 0;
        var publicCopy = skybox.EquirectangularTextureData!;
        publicCopy[1] = 0;
        Expect(skybox.EquirectangularTextureData![0] == 7 && skybox.EquirectangularTextureData![1] == 8,
            "Skybox texture bytes are externally mutable.");
        skybox.ClearEquirectangularTexture();
        Expect(skybox.Mode == SkyboxMode3D.None && !skybox.HasEquirectangularTexture,
            "Clearing the active skybox resource left an invalid active mode.");
        var face = TextureResource3D.Create("cube-face", new byte[] { 1, 2, 3 }, "image/png");
        skybox.SetCubemapFaceTextures(face, face, face, face, face, face);
        Expect(skybox.Mode == SkyboxMode3D.Cubemap && skybox.HasCubemapTextures,
            "Complete cubemap resources did not activate cubemap mode.");
        skybox.ClearCubemapTextures();
        Expect(skybox.Mode == SkyboxMode3D.None && !skybox.HasCubemapTextures,
            "Clearing cubemap resources left an invalid active mode.");
        ExpectThrows<ArgumentNullException>(() => skybox.SetCubemapFaceTextures(face, face, null!, face, face, face));
        ExpectThrows<ArgumentOutOfRangeException>(() => skybox.Intensity = float.PositiveInfinity);
    }

    private static void TestParticleSettingsContracts()
    {
        var particles = new ParticleSystem3D(new ParticleSystemSettings3D
        {
            Capacity = 4,
            EmissionRatePerSecond = 0f,
            ParticleLifetimeSeconds = 2f
        });
        particles.Emit(4);
        var version = particles.ParticleVersion;
        particles.Settings.Capacity = 2;
        Expect(particles.AliveCount == 2 && particles.ParticleVersion > version,
            "Direct particle-setting mutation did not trim and invalidate retained state.");
        var meshBefore = particles.GetMesh();
        particles.Settings.RenderMode = ParticleRenderMode3D.Cube3D;
        var meshAfter = particles.GetMesh();
        Expect(!ReferenceEquals(meshBefore, meshAfter), "RenderMode mutation did not invalidate particle geometry.");
        ExpectThrows<ArgumentOutOfRangeException>(() => particles.Settings.ParticleLifetimeSeconds = float.NaN);
        ExpectThrows<ArgumentOutOfRangeException>(() => particles.Settings.Capacity = 0);
    }

    private static void TestHighScaleMutationContracts()
    {
        var mesh = MeshFactory.CreateExtrudedRectangle(1f, 1f, 1f);
        var part = new CompositePartTemplate3D("part", mesh, new MeshResourceKey(mesh.ResourceKey), 0,
            Matrix4x4.Identity, ColorRgba.White, LightingMode.Lambert);
        var template = new CompositeTemplate3D(1, "template", new[] { part });
        var alert = template.AddMaterialVariant(1, "alert");
        var layer = new HighScaleInstanceLayer3D(template, 2, 4f);
        var index = layer.AddInstance(Matrix4x4.Identity);
        Span<int> dirty = stackalloc int[4];
        Expect(layer.Instances.DrainDirtyTransforms(dirty) == 1, "New high-scale instance was not transform-dirty.");
        Expect(layer.Instances.DrainDirtyMaterials(dirty) == 1, "New high-scale instance was not material-dirty.");
        Expect(layer.Instances.DrainDirtyVisibility(dirty) == 1, "New high-scale instance was not visibility-dirty.");
        layer.SetInstanceMaterialVariant(index, 1);
        Expect(layer.Instances.DrainDirtyMaterials(dirty) == 1, "Material variant mutation was missing from the dirty queue.");
        layer.SetInstanceVisible(index, false);
        Expect(layer.Instances.DrainDirtyVisibility(dirty) == 1, "Visibility mutation was missing from the dirty queue.");
        ExpectThrows<ArgumentOutOfRangeException>(() => _ = layer.Instances[layer.Instances.Count]);
        var stateChanges = 0;
        layer.StateChanged += (_, _) => stateChanges++;
        using (var outer = layer.BeginTelemetryBatch())
        {
            using (var inner = layer.BeginTelemetryBatch())
            {
                inner.SetVisible(index, true);
            }
            Expect(stateChanges == 0, "Nested high-scale batch flushed before the outer scope completed.");
        }
        Expect(stateChanges == 1, "Nested high-scale batch did not coalesce state notifications.");
        var resolverVersion = layer.MaterialResolverVersion;
        alert.DefaultColor = ColorRgba.Black;
        Expect(layer.MaterialResolverVersion > resolverVersion, "Material variant mutation did not invalidate layer material state.");
        ExpectThrows<ArgumentOutOfRangeException>(() => layer.SetInstanceTransform(index, Matrix4x4.CreateScale(0f)));
    }

    private static void TestColliderMutationContracts()
    {
        using var scene = new Scene3D(Scene3DOptions.WithoutPhysics());
        var collider = new BoxCollider3D();
        var box = scene.Add(new Box3D { Collider = collider });
        SceneChangeKind? kind = null;
        scene.SceneChangedDetailed += (_, e) => kind = e.Kind;
        collider.Size = new Vector3(2f, 1f, 1f);
        Expect(kind == SceneChangeKind.Physics, "Collider parameter mutation did not notify the scene physics contract.");
        ExpectThrows<ArgumentOutOfRangeException>(() => collider.Size = new Vector3(0f, 1f, 1f));
        ExpectThrows<ArgumentOutOfRangeException>(() => box.Scale = new Vector3(1f, float.NaN, 1f));
        var body = new Rigidbody3D
        {
            RollingFriction = 0.025f,
            RollingRadius = 0.34f,
            CollisionTorqueScale = 1f
        };
        Expect(global::System.Math.Abs(body.RollingFriction - 0.025f) < 0.000001f, "RollingFriction was not retained.");
        Expect(global::System.Math.Abs(body.RollingRadius - 0.34f) < 0.000001f, "RollingRadius was not retained.");
        Expect(global::System.Math.Abs(body.CollisionTorqueScale - 1f) < 0.000001f, "CollisionTorqueScale was not retained.");
        ExpectThrows<ArgumentOutOfRangeException>(() => body.Mass = float.NaN);
        ExpectThrows<ArgumentOutOfRangeException>(() => body.InertiaTensor = new Vector3(1f, 0f, 1f));
        ExpectThrows<ArgumentOutOfRangeException>(() => body.RollingFriction = float.NaN);
        ExpectThrows<ArgumentOutOfRangeException>(() => body.RollingRadius = -0.01f);
        ExpectThrows<ArgumentOutOfRangeException>(() => body.CollisionTorqueScale = 4.01f);
    }

    private static void TestEmbeddedTextureMaterialBinding()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var texture = new ModelTextureAsset3D(3, "tex", "image/jpeg", null, bytes);
        var assetMaterial = new ModelMaterialAsset3D(0, "mat", ColorRgba.White, 0f, 1f, "OPAQUE", 0.5f, 3);
        ExpectThrows<ArgumentOutOfRangeException>(() => new ModelMaterialAsset3D(0, "bad", ColorRgba.White, 0f, 1f, "UNKNOWN", 0.5f, null));
        ExpectThrows<ArgumentOutOfRangeException>(() => new ModelMaterialAsset3D(0, "bad", ColorRgba.White, 0f, 1f, "OPAQUE", 0.5f, -1));
        var material = assetMaterial.ToMaterial3D(new[] { texture });
        bytes[0] = 99;
        Expect(material.HasBaseColorTexture, "Embedded texture payload must be transferred to runtime Material3D.");
        Expect(material.BaseColorTextureKey is not null && material.BaseColorTextureKey.StartsWith("model-texture:3:", StringComparison.Ordinal), "Model texture key must include a content-derived suffix to avoid cache collisions.");
        Expect(material.BaseColorTextureData is { Length: 8 } && material.BaseColorTextureData[0] == 1, "Material3D must clone embedded texture data.");
    }

    private static void TestCameraArcFlight()
    {
        var camera = new Camera3D
        {
            Position = new Vector3(0f, 2f, -6f),
            Target = Vector3.Zero
        };
        var flight = new CameraArcFlight3D();
        flight.Start(camera, new Vector3(1f, 1f, 1f), distance: 2.5f, elevation: 0.8f, durationSeconds: 1f, arcHeight: 1f);
        var start = camera.Position;
        flight.Update(0.5f);
        var mid = camera.Position;
        Expect(flight.IsActive, "Camera flight must still be active halfway through its duration.");
        Expect(Vector3.Distance(start, mid) > 0.01f, "Camera arc flight must move the camera.");
        flight.Update(0.6f);
        Expect(!flight.IsActive, "Camera flight must finish after elapsed time exceeds duration.");
        Expect(Vector3.Distance(camera.Target, new Vector3(1f, 1f, 1f)) < 0.01f, "Camera flight must end focused on the requested target.");
    }

    private static void TestMalformedGlbIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "avalonia3d_bad_length_" + Guid.NewGuid().ToString("N") + ".glb");
        try
        {
            var bytes = new byte[20];
            BitConverter.GetBytes(0x46546C67u).CopyTo(bytes, 0);
            BitConverter.GetBytes(2u).CopyTo(bytes, 4);
            BitConverter.GetBytes(100u).CopyTo(bytes, 8);
            File.WriteAllBytes(path, bytes);
            var asset = GltfModelImporter.Import(path);
            Expect(asset.Diagnostics.HasErrors, "Malformed GLB declared length must be rejected.");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }


    private static void TestSceneDisposalReleasesResourcesAfterFailure()
    {
        using var engine = new Engine3DBuilder()
            .UsePhysics(static _ => new ThrowingDisposePhysicsCore())
            .Build();
        var scene = engine.CreateScene();
        scene.ResourceOwner.SetTextures(new[]
        {
            TextureResource3D.Create("dispose:self-test", new byte[] { 1, 2, 3, 4 }, "application/x-avalonia3d-self-test")
        });
        Expect(engine.Resources.CaptureSnapshot().ReferencedTextureCount == 1,
            "Scene resource owner was not populated before disposal failure test.");

        ExpectThrows<AggregateException>(scene.Dispose);
        var after = engine.Resources.CaptureSnapshot();
        Expect(scene.IsDisposed && engine.ActiveSceneCount == 0,
            "Scene remained attached after a subsystem failed during disposal.");
        Expect(after.ReferencedTextureCount == 0 && after.OwnerCount == 0,
            "Scene resource owner survived a failure in an independent subsystem disposal.");
    }

    private static void TestRenderPipelineRejectsUnimplementedPasses()
    {
        using var scene = new Scene3D();
        scene.RenderPipeline.Mode = RenderPipelineMode3D.Deferred;
        ExpectThrows<NotSupportedException>(() => RenderPipelinePlanner3D.Plan(scene, BackendKind.OpenGlDesktop));

        scene.RenderPipeline.Mode = RenderPipelineMode3D.Forward;
        scene.RenderPipeline.Ssao.Enabled = true;
        ExpectThrows<NotSupportedException>(() => RenderPipelinePlanner3D.Plan(scene, BackendKind.WebGlBrowser));

        scene.RenderPipeline.Ssao.Enabled = false;
        scene.RenderPipeline.EnableHdr = true;
        ExpectThrows<NotSupportedException>(() => RenderPipelinePlanner3D.Plan(scene, BackendKind.OpenGlDesktop));

        scene.RenderPipeline.EnableHdr = false;
        scene.RenderPipeline.EnableMotionVectorMetadata = true;
        ExpectThrows<NotSupportedException>(() => RenderPipelinePlanner3D.Plan(scene, BackendKind.WebGlBrowser));

        scene.RenderPipeline.EnableMotionVectorMetadata = false;
        scene.RenderPipeline.ToneMapping.Enabled = true;
        scene.RenderPipeline.ToneMapping.Mode = ToneMappingMode3D.None;
        ExpectThrows<InvalidOperationException>(() => RenderPipelinePlanner3D.Plan(scene, BackendKind.OpenGlDesktop));
    }

    private static void TestKinematicCharacterController()
    {
        using var scene = new Scene3D();
        scene.Add(new Box3D
        {
            Position = new Vector3(0f, -0.05f, 0f),
            Width = 8f,
            Height = 0.1f,
            Depth = 8f,
            Collider = new BoxCollider3D { Size = new Vector3(8f, 0.1f, 8f) }
        });
        var wall = scene.Add(new Box3D
        {
            Position = new Vector3(1.0f, 0.5f, 0f),
            Width = 0.2f,
            Height = 1f,
            Depth = 2f,
            Collider = new BoxCollider3D { Size = new Vector3(0.2f, 1f, 2f) }
        });
        var lowStep = scene.Add(new Box3D
        {
            Position = new Vector3(0f, 0.05f, 1.0f),
            Width = 1f,
            Height = 0.1f,
            Depth = 0.35f,
            Collider = new BoxCollider3D { Size = new Vector3(1f, 0.1f, 0.35f) }
        });
        _ = wall;
        _ = lowStep;

        var controller = new KinematicCharacterController3D { Radius = 0.25f, Height = 1.7f, StepHeight = 0.18f };
        var groundedStart = controller.Move(scene, Vector3.Zero, Vector3.Zero, 1f / 60f);
        var blocked = controller.Move(scene, groundedStart, new Vector3(1.5f, 0f, 0f), 1f / 60f);
        Expect(blocked.X < 0.8f, "Kinematic character must not pass through a blocking wall.");
        var stepped = controller.Move(scene, groundedStart, new Vector3(0f, 0f, 1.1f), 1f / 60f);
        Expect(stepped.Y >= 0.08f, "Kinematic character must step onto low obstacles within StepHeight.");
    }


    private static void TestParticleBillboardBasis()
    {
        var particles = new ParticleSystem3D(
            new ParticleSystemSettings3D
            {
                Capacity = 1,
                EmissionRatePerSecond = 0f,
                ParticleLifetimeSeconds = 5f,
                StartSize = 1f,
                EndSize = 1f,
                RenderMode = ParticleRenderMode3D.CameraFacingQuad
            },
            new ParticleEmitter3D(123));
        particles.Emit(1);
        var meshA = particles.GetMesh();
        var versionA = meshA.GeometryVersion;
        var meshB = particles.GetMesh();

        Expect(ReferenceEquals(meshA, meshB), "Particle billboard mesh must stay retained/static across repeated reads.");
        Expect(meshA.Positions.Length == 4 && meshA.Indices.Length == 6, "A camera-facing particle system must expose the shared static quad mesh.");
        Expect(meshB.GeometryVersion == versionA, "Reading the billboard mesh must not dirty CPU particle geometry; billboarding is a renderer/shader responsibility.");
        Expect(particles.AliveCount == 1 && particles.Particles.Count == 1, "Particle instances must remain available as renderer instance data.");
    }

    private static void TestCameraPoseIsAtomic()
    {
        var camera = new Camera3D();
        var changes = 0;
        camera.Changed += (_, _) => changes++;
        camera.SetPose(new Vector3(2f, 3f, 4f), new Vector3(2f, 3f, 3f), Vector3.UnitY);
        Expect(changes == 1, "SetPose published intermediate camera state.");
        camera.Translate(new Vector3(1f, 0f, -2f));
        Expect(changes == 2, "Translate did not publish exactly one camera change.");
        Expect(camera.Position == new Vector3(3f, 3f, 2f) && camera.Target == new Vector3(3f, 3f, 1f),
            "Atomic translation did not preserve the camera direction.");
    }

    private static void TestTransparentPlanScratchReuse()
    {
        using var scene = new Scene3D();
        scene.Add(new Box3D
        {
            Material = new Material3D
            {
                BaseColor = new ColorRgba(0.3f, 0.6f, 0.9f, 0.5f),
                Opacity = 0.5f,
                Surface = SurfaceMode.Transparent
            }
        });

        var scratch = new SceneRenderPlanScratch3D();
        SceneRenderCommand3D firstCommand;
        string firstId;
        using (var firstFrame = SceneRenderFrameContext3D.Build(scene, 800f, 600f, BackendKind.WebGlBrowser))
        {
            var firstPlan = SceneRenderPlanBuilder3D.Build(firstFrame, scratch);
            firstCommand = firstPlan.DrawCommands[0];
            firstId = firstCommand.Id;
        }

        scene.Camera.Translate(new Vector3(0.1f, 0f, 0f));
        using var secondFrame = SceneRenderFrameContext3D.Build(scene, 800f, 600f, BackendKind.WebGlBrowser);
        var secondPlan = SceneRenderPlanBuilder3D.Build(secondFrame, scratch);
        var secondCommand = secondPlan.DrawCommands[0];

        Expect(ReferenceEquals(firstCommand, secondCommand), "Camera-only plan allocated a new draw command instead of reusing scratch storage.");
        Expect(string.Equals(firstId, secondCommand.Id, StringComparison.Ordinal), "Camera-only planning rebuilt an unstable transparent draw id.");
    }


    private static void TestContentAddressedAssetCache()
    {
        var configuration = new AssetStreamingOptions3D { ContentCacheByteBudget = 1024 * 1024 }.Freeze();
        using var cache = new ContentAddressedAssetCache3D(configuration);
        var first = cache.StoreAsync(new byte[] { 1, 2, 3, 4 }).AsTask().GetAwaiter().GetResult();
        var second = cache.StoreAsync(new byte[] { 1, 2, 3, 4 }).AsTask().GetAwaiter().GetResult();
        Expect(first.ContentHash == second.ContentHash && cache.Count == 1, "Content cache did not deduplicate identical immutable bytes.");
        Expect(first.Content.Span.SequenceEqual(second.Content.Span), "Deduplicated content changed.");
    }

    private static void TestContentCacheHardeningContracts()
    {
        var configuration = new AssetStreamingOptions3D { ContentCacheByteBudget = 1024 * 1024 }.Freeze();
        using var cache = new ContentAddressedAssetCache3D(configuration);
        var blob = cache.StoreAsync(new byte[] { 1, 2, 3, 4 }).AsTask().GetAwaiter().GetResult();
        var exposed = blob.CopyBytes();
        exposed[0] = 99;
        var reread = cache.TryGetAsync(blob.ContentHash).AsTask().GetAwaiter().GetResult();
        Expect(reread.HasValue && reread.Value.Content.Span[0] == 1, "Caller mutation changed content-addressed cache identity.");
        ExpectThrows<ArgumentException>(() => cache.TryGetAsync("../forged").AsTask().GetAwaiter().GetResult());
        ExpectThrows<ArgumentException>(() => cache.TryGetAsync(new string('z', 64)).AsTask().GetAwaiter().GetResult());
    }

    private static void TestTextureResidencyRollback()
    {
        var source = new SelfTestOversizedTextureMipSource3D();
        using var engine = new Engine3DBuilder()
            .UseTextureMipSource(source)
            .ConfigureAssets(options =>
            {
                options.MaximumConcurrentTextureLoads = 1;
                options.TextureResidentByteBudget = 32;
            })
            .Build();
        ExpectThrows<InvalidOperationException>(() => engine.Textures.AcquireAsync("selftest:oversized").AsTask().GetAwaiter().GetResult());
        var statistics = engine.Textures.Statistics;
        Expect(statistics.ResidentBytes == 0 && statistics.ResidentMipLevels == 0, "Failed texture residency left bytes committed above budget.");
    }

    private static void TestSpatialQueryCompletenessContracts()
    {
        var grid = new SpatialHashGrid3D(1f);
        var scratch = new SpatialQueryScratch3D();
        ExpectThrows<InvalidOperationException>(() => grid.QueryBounds(new Bounds3D(new Vector3(-100f), new Vector3(100f)), scratch));
        var ray = new Ray3D(Vector3.Zero, Vector3.Normalize(new Vector3(1f, 1f, 1f)));
        ExpectThrows<InvalidOperationException>(() => grid.QueryRay(ray, scratch, maxDistance: 100f, maxSteps: 1));
        ExpectThrows<ArgumentOutOfRangeException>(() => grid.QueryRay(new Ray3D(new Vector3(float.MaxValue), Vector3.UnitX), scratch, 1f, 8));
    }


    private static void TestSpatialCellSizeMutationContract()
    {
        var grid = new SpatialHashGrid3D(1f);
        var box = new Box3D { Width = 1f, Height = 1f, Depth = 1f };
        grid.Add(box, new Bounds3D(new Vector3(-0.5f), new Vector3(0.5f)));
        ExpectThrows<InvalidOperationException>(() => grid.CellSize = 2f);
        grid.Clear();
        grid.CellSize = 2f;
        Expect(grid.CellSize == 2f, "Empty spatial index rejected a valid cell-size reconfiguration.");
    }

    private static void TestSceneBuilderLifetime()
    {
        using var engine = new Engine3DBuilder().Build();
        using (var abandoned = engine.CreateSceneBuilder())
        {
            abandoned.Box("temporary", Vector3.Zero, Vector3.One, ColorRgba.White);
            Expect(engine.ActiveSceneCount == 1, "Scene builder did not attach its construction scene.");
        }
        Expect(engine.ActiveSceneCount == 0, "Disposing an unbuilt SceneBuilder3D leaked its scene.");
        using var scene = engine.BuildScene(builder => builder.Sphere("built", Vector3.Zero, 1f, ColorRgba.White));
        Expect(engine.ActiveSceneCount == 1 && scene.Objects.Count == 1, "BuildScene did not transfer the completed scene to the caller.");
    }

    private static void TestSceneSerializationMaterialCompleteness()
    {
        using var engine = new Engine3DBuilder().Build();
        using var scene = engine.CreateScene();
        scene.Add(new Box3D
        {
            Name = "MaterialRoundTrip",
            Material = new Material3D
            {
                AmbientStrength = 1.5f,
                DiffuseStrength = 0.75f,
                AlphaCutoff = 0.2f
            }
        });
        var document = SceneSerializer3D.Deserialize(SceneSerializer3D.Serialize(scene));
        var material = document.Objects[0].Material;
        Expect(material.AmbientStrength == 1.5f && material.DiffuseStrength == 0.75f && material.AlphaCutoff == 0.2f,
            "Scene material strength fields changed during serialization.");
    }


    private static void TestSceneSerializationTextureSlotValidation()
    {
        using var engine = new Engine3DBuilder().Build();
        using var scene = engine.CreateScene();
        scene.Add(new Box3D { Name = "TextureSlotValidation" });
        var document = SceneSerializer3D.Capture(scene);
        document.Objects[0].Material.Textures.Add(new SceneTextureDocument3D
        {
            Slot = "extension",
            LogicalKey = "ambiguous",
            MimeType = "application/octet-stream",
            Base64Data = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
        });
        var json = global::System.Text.Json.JsonSerializer.Serialize(document);
        ExpectThrows<InvalidDataException>(() => SceneSerializer3D.Deserialize(json));
    }

    private static void TestProductionAcceptanceRejectsInvalidMetrics()
    {
        var profiler = new EngineProfiler3D(64);
        for (var i = 0; i < 64; i++)
        {
            profiler.RecordFrame("invalid", BackendKind.OpenGlDesktop, new RenderStats
            {
                PresentedFramesPerSecond = 60d,
                FrameTotalMilliseconds = i == 63 ? double.NaN : 16.666d,
                BackendMilliseconds = 1d,
                SimulationTotalMilliseconds = 1d
            });
        }
        var snapshot = profiler.Capture(64);
        var result = ProductionAcceptance3D.Evaluate(snapshot, new ProductionAcceptanceProfile3D { MinimumFrameCount = 64 });
        Expect(snapshot.InvalidMetricCount > 0 && !result.Passed, "Invalid telemetry passed the production acceptance gate.");
    }


    private static void TestUnavailableGpuTimingContract()
    {
        var profiler = new EngineProfiler3D(64);
        for (var i = 0; i < 64; i++)
        {
            profiler.RecordFrame("gpu-timing-unavailable", BackendKind.OpenGlDesktop, new RenderStats
            {
                PresentedFramesPerSecond = 60d,
                FrameTotalMilliseconds = 16.666d,
                BackendMilliseconds = 1d,
                SimulationTotalMilliseconds = 1d,
                GpuTimingAvailable = false,
                GpuFrameMilliseconds = double.NaN
            });
        }
        var snapshot = profiler.Capture(64);
        Expect(snapshot.InvalidMetricCount == 0, "Unavailable GPU timer queries were incorrectly classified as invalid telemetry.");
        Expect(!snapshot.Frames[0].GpuTimingAvailable && snapshot.Frames[0].GpuMilliseconds == 0d,
            "Profiler did not normalize unavailable GPU timing while preserving its availability flag.");
    }

    private static void TestEngineAsyncShutdownContract()
    {
        var engine = new Engine3DBuilder().Build();
        engine.Dispose();
        engine.ShutdownCompletion.GetAwaiter().GetResult();
        Expect(engine.IsDisposed && engine.ShutdownCompletion.IsCompletedSuccessfully,
            "Engine shutdown completion did not observe disposal of asynchronous dependencies.");
    }

    private static void TestExtensionSnapshotHardening()
    {
        var sourcePasses = new List<RenderExtensionPass3D>
        {
            new(
                "immutable-pass",
                RenderExtensionStage3D.AfterOpaque,
                RenderExtensionPassKind3D.FullscreenRender,
                ShaderResource3D.Create("selftest:immutable-extension", ShaderStage3D.Fragment, "@fragment fn main() -> @location(0) vec4f { return vec4f(1.0); }"),
                new[] { new RenderExtensionResource3D("hdrColor", RenderExtensionResourceAccess3D.Read) })
        };
        var extension = new MutableSelfTestRenderExtension3D(sourcePasses);
        var registry = new RenderExtensionRegistry3D();
        registry.Register(extension);
        sourcePasses.Clear();
        var snapshot = registry.CaptureSnapshot();
        Expect(snapshot.PassCount == 1 && snapshot.Extensions[0].Passes.Count == 1, "Registry snapshot retained a caller-owned mutable pass collection.");
        ExpectThrows<InvalidOperationException>(() => registry.Replace(new MutableSelfTestRenderExtension3D(new List<RenderExtensionPass3D>(snapshot.Extensions[0].Passes), version: 1)));
    }


    private static void TestMaterialExtensionIdentity()
    {
        var first = new MaterialShaderExtension3D("selftest.identity", 3, new byte[] { 1, 2, 3, 4 });
        var second = new MaterialShaderExtension3D("selftest.identity", 3, new byte[] { 1, 2, 3, 4 });
        var changed = new MaterialShaderExtension3D("selftest.identity", 3, new byte[] { 1, 2, 3, 5 });
        Expect(StringComparer.Ordinal.Equals(first.Identity, second.Identity), "Equal material extension payloads produced different identities.");
        Expect(!StringComparer.Ordinal.Equals(first.Identity, changed.Identity), "Changed material extension payload produced the same identity.");
        var separator = first.Identity.LastIndexOf(':');
        Expect(separator >= 0 && first.Identity.Length - separator - 1 == 64, "Material extension identity does not contain a full SHA-256 digest.");
    }

    private static void TestTextureStreamingContracts()
    {
        var source = new SelfTestTextureMipSource3D();
        using var engine = new Engine3DBuilder()
            .UseTextureMipSource(source)
            .ConfigureAssets(options =>
            {
                options.MaximumConcurrentTextureLoads = 1;
                options.TextureResidentByteBudget = 1024 * 1024;
            })
            .Build();
        var first = engine.Textures.AcquireAsync("selftest:texture", mostDetailedMip: 1).AsTask();
        var second = engine.Textures.AcquireAsync("selftest:texture", mostDetailedMip: 1).AsTask();
        Task.WaitAll(first, second);
        using var firstLease = first.Result;
        using var secondLease = second.Result;
        Expect(firstLease.Snapshot.MostDetailedResidentMip == 1 && firstLease.Snapshot.Mips.Count == 2, "Texture streaming did not load the requested mip tail.");
        Expect(firstLease.Snapshot.Mips[0].MipLevel == 1 && firstLease.Snapshot.Mips[1].MipLevel == 2, "Texture mip snapshot order is not most-detailed to coarsest.");
        Expect(source.DescribeCount == 1 && source.MipLoadCount == 2, "Concurrent texture requests did not coalesce descriptor/mip source work.");
        var stats = engine.Textures.Statistics;
        Expect(stats.PinnedTextures == 1 && stats.ResidentMipLevels == 2, "Texture residency metrics do not reflect coalesced active leases.");
    }

    private static void TestSceneSerializationRoundTrip()
    {
        using var engine = new Engine3DBuilder().Build();
        using var scene = engine.CreateScene();
        scene.BackgroundColor = new ColorRgba(0.1f, 0.2f, 0.3f, 1f);
        scene.Add(new Sphere3D
        {
            Name = "RoundTripSphere",
            Radius = 2f,
            Segments = 16,
            Rings = 12,
            Position = new Vector3(1f, 2f, 3f),
            Material = new Material3D { BaseColor = new ColorRgba(0.8f, 0.2f, 0.1f, 1f) }
        });
        var json = SceneSerializer3D.Serialize(scene);
        var document = SceneSerializer3D.Deserialize(json);
        using var restored = SceneSerializer3D.RestoreAsync(engine, document).AsTask().GetAwaiter().GetResult();
        Expect(restored.Objects.Count == 1 && restored.Objects[0] is Sphere3D, "Scene document did not restore the supported primitive.");
        var sphere = (Sphere3D)restored.Objects[0];
        Expect(sphere.Name == "RoundTripSphere" && sphere.Radius == 2f && sphere.Position == new Vector3(1f, 2f, 3f), "Scene primitive state changed during round-trip.");
    }

    private static void TestProductionProfilerContracts()
    {
        var profiler = new EngineProfiler3D(64);
        for (var i = 0; i < 64; i++)
        {
            profiler.RecordFrame("selftest", BackendKind.OpenGlDesktop, new RenderStats
            {
                PresentedFramesPerSecond = 60d,
                FrameTotalMilliseconds = i == 63 ? 30d : 16d,
                BackendMilliseconds = 2d,
                SimulationTotalMilliseconds = 1d,
                AllocatedBytesPerFrame = 1024,
                GpuDrivenActive = true
            });
        }
        var snapshot = profiler.Capture(64);
        Expect(snapshot.Frames.Count == 64 && snapshot.P99FrameMilliseconds > 16d, "Profiler percentile calculation ignored the tail frame.");
        var result = ProductionAcceptance3D.Evaluate(snapshot, new ProductionAcceptanceProfile3D
        {
            MinimumFrameCount = 64,
            MinimumAverageFramesPerSecond = 59d,
            MaximumP95FrameMilliseconds = 20d,
            MaximumP99FrameMilliseconds = 40d,
            MaximumWorstFrameMilliseconds = 40d,
            MaximumAverageBackendMilliseconds = 4d,
            MaximumAverageSimulationMilliseconds = 2d,
            MaximumAllocatedBytesPerFrame = 2048,
            RequireGpuDriven = true
        });
        Expect(result.Passed, "Production acceptance rejected a profile inside every configured budget.");
    }

    private static void TestGpuPickingContracts()
    {
        using var service = new GpuPickingService3D();
        ExpectThrows<InvalidOperationException>(() => service.PickAsync(0.5f, 0.5f).AsTask().GetAwaiter().GetResult());
        var backend = new SelfTestGpuPickingBackend3D();
        service.AttachBackend(backend);
        var first = service.PickAsync(0.25f, 0.5f).AsTask();
        var second = service.PickAsync(0.75f, 0.5f).AsTask();
        Task.WaitAll(first, second);
        Expect(first.Result.RequestId < second.Result.RequestId && !first.Result.HasHit && !second.Result.HasHit, "GPU picking did not preserve request identity/order.");
        service.DetachBackend(backend);
    }

    private static void TestExtensionContracts()
    {
        var shaderVertex = ShaderResource3D.Create("selftest:custom-vertex", ShaderStage3D.Vertex, "@vertex fn main() -> @builtin(position) vec4f { return vec4f(); }");
        var shaderFragment = ShaderResource3D.Create("selftest:custom-fragment", ShaderStage3D.Fragment, "@fragment fn main() -> @location(0) vec4f { return vec4f(1.0); }");
        var definition = new MaterialShaderExtensionDefinition3D("selftest.material", 1, new[] { 7 }, shaderVertex, shaderFragment, 16);
        var registry = new MaterialShaderExtensionRegistry3D();
        registry.Register(definition);
        registry.Validate(new MaterialShaderExtension3D("selftest.material", 7, new byte[16]));
        Expect(registry.Count == 1 && registry.Version == 1, "Material extension registry did not publish a stable version.");

        var renderRegistry = new RenderExtensionRegistry3D();
        renderRegistry.Register(new SelfTestRenderExtension3D());
        var snapshot = renderRegistry.CaptureSnapshot();
        Expect(snapshot.Extensions.Count == 1 && snapshot.PassCount == 1, "Render extension registry produced an invalid immutable snapshot.");
    }

    private sealed class SelfTestOversizedTextureMipSource3D : ITextureMipSource3D
    {
        public ValueTask<TextureAssetDescriptor3D> DescribeAsync(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new TextureAssetDescriptor3D(key, 4, 4, 1, TexturePixelFormat3D.Rgba8Unorm));

        public ValueTask<TextureMipPayload3D> LoadMipAsync(TextureAssetDescriptor3D descriptor, int mipLevel, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new TextureMipPayload3D(descriptor.Key, mipLevel, 4, 4, 16, new byte[64]));
    }

    private sealed class MutableSelfTestRenderExtension3D : IRenderExtension3D
    {
        public MutableSelfTestRenderExtension3D(IReadOnlyList<RenderExtensionPass3D> passes, int version = 1)
        {
            Passes = passes;
            Version = version;
        }
        public string Id => "selftest.mutable-render";
        public int Version { get; }
        public IReadOnlyList<RenderExtensionPass3D> Passes { get; }
    }

    private sealed class SelfTestTextureMipSource3D : ITextureMipSource3D
    {
        private int _describeCount;
        private int _mipLoadCount;
        public int DescribeCount => Volatile.Read(ref _describeCount);
        public int MipLoadCount => Volatile.Read(ref _mipLoadCount);

        public async ValueTask<TextureAssetDescriptor3D> DescribeAsync(string key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _describeCount);
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            return new TextureAssetDescriptor3D(key, 4, 4, 3, TexturePixelFormat3D.Rgba8Unorm);
        }

        public async ValueTask<TextureMipPayload3D> LoadMipAsync(TextureAssetDescriptor3D descriptor, int mipLevel, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _mipLoadCount);
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            var width = descriptor.GetMipWidth(mipLevel);
            var height = descriptor.GetMipHeight(mipLevel);
            return new TextureMipPayload3D(descriptor.Key, mipLevel, width, height, width * 4, new byte[width * height * 4]);
        }
    }

    private sealed class SelfTestGpuPickingBackend3D : IGpuPickingBackend3D
    {
        public string Name => "selftest-gpu";
        public int MaximumBatchSize => 64;
        public ValueTask<IReadOnlyList<GpuPickResult3D>> ExecuteAsync(IReadOnlyList<GpuPickRequest3D> requests, CancellationToken cancellationToken = default)
        {
            var results = new GpuPickResult3D[requests.Count];
            for (var i = 0; i < results.Length; i++) results[i] = GpuPickResult3D.Miss(requests[i].RequestId);
            return ValueTask.FromResult<IReadOnlyList<GpuPickResult3D>>(results);
        }
    }

    private sealed class SelfTestRenderExtension3D : IRenderExtension3D
    {
        private readonly RenderExtensionPass3D[] _passes =
        {
            new(
                "selftest-pass",
                RenderExtensionStage3D.AfterOpaque,
                RenderExtensionPassKind3D.FullscreenRender,
                ShaderResource3D.Create("selftest:render-extension", ShaderStage3D.Fragment, "@fragment fn main() -> @location(0) vec4f { return vec4f(1.0); }"),
                new[] { new RenderExtensionResource3D("hdrColor", RenderExtensionResourceAccess3D.Read | RenderExtensionResourceAccess3D.Sample) })
        };
        public string Id => "selftest.render";
        public int Version => 1;
        public IReadOnlyList<RenderExtensionPass3D> Passes => _passes;
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException("Expected exception was not thrown: " + typeof(T).Name);
    }

    private sealed class ThrowingDisposePhysicsCore : IPhysicsCore
    {
        public void Step(Scene3D scene, float deltaSeconds)
        {
        }

        public bool Raycast(Scene3D scene, Ray3D ray, out RaycastHit3D hit)
        {
            hit = default;
            return false;
        }

        public IReadOnlyList<RaycastHit3D> RaycastAll(Scene3D scene, Ray3D ray)
            => Array.Empty<RaycastHit3D>();

        public void Dispose()
            => throw new InvalidOperationException("Synthetic physics disposal failure.");
    }

    private sealed class SelfTestComposite3D : CompositeObject3D
    {
        protected override void Build(CompositeBuilder3D builder)
        {
            builder.Box("Left", 1f, 1f, 1f).At(-1f, 0f, 0f);
            builder.Box("Right", 1f, 1f, 1f).At(1f, 0f, 0f);
        }
    }
}
