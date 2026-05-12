using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Importers.Gltf;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Capabilities;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Navigation;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Physics.Kinematic;
using Ray3D = ThreeDEngine.Core.Math.Ray;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Spatial;

namespace ThreeDEngine.Core.Diagnostics;

public static class Avalonia3DSelfTestRunner
{
    private static readonly object Gate = new();
    private static bool _startupRunCompleted;
    private static bool _isRunning;

    public static Avalonia3DSelfTestResult? LastStartupResult { get; private set; }

    public static Avalonia3DSelfTestResult RunAll()
    {
        lock (Gate)
        {
            if (_isRunning)
            {
                return LastStartupResult ?? new Avalonia3DSelfTestResult(Array.Empty<Avalonia3DSelfTestCaseResult>(), TimeSpan.Zero);
            }

            _isRunning = true;
        }

        var total = Stopwatch.StartNew();
        var cases = new List<Avalonia3DSelfTestCaseResult>();
        try
        {
            RunCase(cases, "Scene rejects duplicate root objects and lights", TestSceneOwnership);
            RunCase(cases, "Scene emits specific change kinds", TestSceneChangeKinds);
            RunCase(cases, "RenderStats.Empty is a fresh instance", TestRenderStatsEmptyIsFresh);
            RunCase(cases, "Mesh validates and copies source arrays", TestMeshValidationAndDefensiveCopy);
            RunCase(cases, "Spatial grid clamps pathological bounds", TestSpatialGridPathologicalBounds);
            RunCase(cases, "Animation sampler sorts keys and evaluates quaternions", TestAnimationSamplerRules);
            RunCase(cases, "Model materials preserve embedded base-color textures", TestEmbeddedTextureMaterialBinding);
            RunCase(cases, "Camera arc flight moves along a curved path", TestCameraArcFlight);
            RunCase(cases, "GLB importer rejects malformed container length", TestMalformedGlbIsRejected);
            RunCase(cases, "Renderer capabilities report unsupported material features honestly", TestRendererCapabilitiesDiagnostics);
            RunCase(cases, "Kinematic character blocks walls and steps over low obstacles", TestKinematicCharacterController);
            RunCase(cases, "Particle billboards follow camera basis", TestParticleBillboardBasis);
            total.Stop();
            return new Avalonia3DSelfTestResult(cases, total.Elapsed);
        }
        finally
        {
            lock (Gate)
            {
                _isRunning = false;
            }
        }
    }

    public static Avalonia3DSelfTestResult? RunAtStartupIfEnabled()
    {
        if (!Avalonia3DGlobalOptions.RunSelfTestsOnStartup)
        {
            return null;
        }

        lock (Gate)
        {
            if (_isRunning)
            {
                return LastStartupResult;
            }

            if (_startupRunCompleted)
            {
                return LastStartupResult;
            }

            _startupRunCompleted = true;
            LastStartupResult = RunAll();
            if (Avalonia3DGlobalOptions.WriteSelfTestReportToConsole)
            {
                Console.WriteLine(LastStartupResult.ToReport());
                Debug.WriteLine(LastStartupResult.ToReport());
            }

            if (!LastStartupResult.Passed && Avalonia3DGlobalOptions.ThrowOnSelfTestFailure)
            {
                throw new Avalonia3DSelfTestException(LastStartupResult);
            }

            return LastStartupResult;
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

    private static void TestSceneOwnership()
    {
        var scene = new Scene3D();
        var secondScene = new Scene3D();
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
        var scene = new Scene3D();
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

    private static void TestRenderStatsEmptyIsFresh()
    {
        var first = RenderStats.Empty;
        first.ObjectCount = 123;
        var second = RenderStats.Empty;
        Expect(!ReferenceEquals(first, second), "RenderStats.Empty must not return a shared mutable singleton.");
        Expect(second.ObjectCount == 0, "RenderStats.Empty must not retain previous mutations.");
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
        ExpectThrows<ArgumentOutOfRangeException>(() => new Mesh3D(new[] { Vector3.Zero }, Array.Empty<Vector3>(), new[] { 0, 1, 2 }, "selftest:bad-index"));
        ExpectThrows<ArgumentException>(() => new Mesh3D(new[] { new Vector3(float.NaN, 0f, 0f), Vector3.UnitX, Vector3.UnitY }, Array.Empty<Vector3>(), new[] { 0, 1, 2 }, "selftest:nan"));
    }

    private static void TestSpatialGridPathologicalBounds()
    {
        var grid = new SpatialHashGrid3D(1f);
        var obj = new Box3D();
        grid.Add(obj, new Bounds3D(new Vector3(-1_000_000f), new Vector3(1_000_000f)));
        var result = grid.QueryRay(new Ray3D(Vector3.Zero, Vector3.UnitX), 10f, 32);
        Expect(result.Count == 0, "Pathological bounds must be skipped instead of expanding to millions of cells.");
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

    private static void TestEmbeddedTextureMaterialBinding()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var texture = new ModelTextureAsset3D(3, "tex", "image/jpeg", null, bytes);
        var assetMaterial = new ModelMaterialAsset3D(0, "mat", ColorRgba.White, 0f, 1f, "OPAQUE", 0.5f, 3);
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


    private static void TestRendererCapabilitiesDiagnostics()
    {
        var material = Material3D.CreatePhong(ColorRgba.White);
        material.SetNormalMapTexture("normal", new byte[] { 1, 2, 3, 4 }, "image/png");
        material.SetMetallicRoughnessTexture("mr", new byte[] { 1, 2, 3, 4 }, "image/png");
        material.SetEmissiveTexture("em", new byte[] { 5, 6, 7, 8 }, "image/png");
        var webDiagnostics = MaterialFeatureDiagnostics3D.Validate(material, RendererCapabilities3D.WebGlBrowser);
        Expect(webDiagnostics.Count == 0, "WebGL capabilities must include the normal/metallic/emissive texture features wired in v100.");
        var desktopDiagnostics = MaterialFeatureDiagnostics3D.Validate(material, RendererCapabilities3D.OpenGlDesktop);
        Expect(desktopDiagnostics.Count == 0, "OpenGL desktop capabilities must include the material texture features wired in v99/v100.");
    }

    private static void TestKinematicCharacterController()
    {
        var scene = new Scene3D();
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
        particles.SetBillboardBasis(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        var meshA = particles.GetMesh();
        var versionA = meshA.GeometryVersion;
        particles.SetBillboardBasis(Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitX);
        var meshB = particles.GetMesh();

        Expect(ReferenceEquals(meshA, meshB), "Particle billboard mesh must stay retained/static across camera basis changes.");
        Expect(meshA.Positions.Length == 4 && meshA.Indices.Length == 6, "A camera-facing particle system must expose the shared static quad mesh.");
        Expect(meshB.GeometryVersion == versionA, "Changing the billboard basis must not dirty CPU particle geometry; billboarding is a renderer/shader responsibility.");
        Expect(particles.AliveCount == 1 && particles.Particles.Count == 1, "Particle instances must remain available as renderer instance data.");
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
}
