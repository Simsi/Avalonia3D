#if THREE_DENGINE_SMOKE_TESTS
using System;
using System.IO;
using System.Numerics;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Avalonia.Preview;
using PreviewerApp;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            TestSceneAndChangeModel();
            TestPhysicsLifecycle();
            TestCompositeParts();
            TestHighScaleChangeKinds();
            TestRendererInvalidationPolicy();
            TestPhysicsFixedStepLifecycle();
            TestRoslynExportPreviewOnly();
            TestVsctKeyBindingLayout(args);
            Console.WriteLine("3DEngine smoke tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("3DEngine smoke tests failed:");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestSceneAndChangeModel()
    {
        var scene = new Scene3D();
        var box = scene.Add(new Box3D { Name = "Smoke Box" });

        SceneChangeKind lastKind = SceneChangeKind.Unknown;
        scene.SceneChangedDetailed += (_, e) => lastKind = e.Kind;

        _ = scene.Registry.Version;
        var registryBeforeMaterial = scene.Registry.Version;
        box.Material.BaseColor = new ColorRgba(0.2f, 0.3f, 0.4f, 1f);
        _ = scene.Registry.Version;
        Assert(lastKind == SceneChangeKind.Material, "Material update must report Material change.");
        Assert(scene.Registry.Version == registryBeforeMaterial, "Material-only change must not rebuild registry.");

        var registryBeforeTransform = scene.Registry.Version;
        box.Position = new Vector3(1f, 2f, 3f);
        _ = scene.Registry.Version;
        Assert(lastKind == SceneChangeKind.Transform, "Transform update must report Transform change.");
        Assert(scene.Registry.Version > registryBeforeTransform, "Transform change must rebuild registry because pick/collision bounds moved.");

        box.IsPickable = false;
        _ = scene.Registry.Version;
        Assert(lastKind == SceneChangeKind.Picking, "Picking update must report Picking change.");

        box.Rigidbody = new Rigidbody3D();
        _ = scene.Registry.Version;
        Assert(lastKind == SceneChangeKind.Rigidbody, "Rigidbody update must report Rigidbody change.");

        box.Collider = null;
        _ = scene.Registry.Version;
        Assert(lastKind == SceneChangeKind.Collider, "Collider update must report Collider change.");

        box.IsVisible = false;
        _ = scene.Registry.Version;
        Assert(lastKind == SceneChangeKind.Visibility, "Visibility update must report Visibility change.");
    }

    private static void TestPhysicsLifecycle()
    {
        var scene = new Scene3D { PhysicsCore = new BasicPhysicsCore() };
        var ground = scene.Add(new Plane3D { Name = "Ground", Width = 20f, Height = 20f });
        ground.Position = Vector3.Zero;

        var cube = scene.Add(new Box3D { Name = "Dynamic Cube", Width = 1f, Height = 1f, Depth = 1f });
        cube.Position = new Vector3(0f, 4f, 0f);
        cube.Rigidbody = new Rigidbody3D { UseGravity = true, IsKinematic = false, Mass = 1f };

        var startY = cube.Position.Y;
        scene.StepPhysics(1f / 10f);
        Assert(cube.Position.Y < startY, "Dynamic rigidbody with gravity must move during physics step.");
        Assert(scene.Registry.DynamicBodies.Count == 1, "Registry must expose the dynamic rigidbody after physics step.");
    }

    private static void TestCompositeParts()
    {
        var scene = new Scene3D();
        var composite = scene.Add(new SmokeComposite());
        Assert(composite.Children.Count == 2, "Composite must build two children.");

        SceneChangeKind lastKind = SceneChangeKind.Unknown;
        scene.SceneChangedDetailed += (_, e) => lastKind = e.Kind;

        var child = composite.FindPart("Body") ?? throw new InvalidOperationException("Body part not found.");
        child.Position = new Vector3(2f, 0f, 0f);
        Assert(lastKind == SceneChangeKind.Transform, "Composite child transform must propagate detailed change kind.");
    }


    private static void TestHighScaleChangeKinds()
    {
        var scene = new Scene3D();
        var source = scene.Add(new SmokeComposite());
        _ = source.Children.Count;
        var template = HighScaleTemplateCompiler.Compile(1001, source);
        scene.Remove(source);

        var layer = scene.Add(new HighScaleInstanceLayer3D(template, initialCapacity: 4));
        SceneChangeKind lastKind = SceneChangeKind.Unknown;
        scene.SceneChangedDetailed += (_, e) => lastKind = e.Kind;

        layer.AddInstance(Matrix4x4.Identity);
        Assert(lastKind == SceneChangeKind.HighScaleStructure, "High-scale AddInstance must report HighScaleStructure.");
        var structureVersion = scene.StructureVersion;

        layer.SetInstanceMaterialVariant(0, 1);
        Assert(lastKind == SceneChangeKind.HighScaleState, "High-scale state update must report HighScaleState.");
        Assert(scene.StructureVersion == structureVersion, "High-scale material/state update must not change StructureVersion.");
    }


    private static void TestRendererInvalidationPolicy()
    {
        Assert((RendererInvalidationPolicy.FromSceneChange(SceneChangeKind.Material) & RendererInvalidationKind.MaterialUpload) != 0,
            "Material scene changes must request material upload.");
        Assert((RendererInvalidationPolicy.FromSceneChange(SceneChangeKind.Transform) & RendererInvalidationKind.TransformUpload) != 0,
            "Transform scene changes must request transform upload.");
        Assert((RendererInvalidationPolicy.FromSceneChange(SceneChangeKind.HighScaleState) & RendererInvalidationKind.HighScaleState) != 0,
            "HighScaleState scene changes must request high-scale state invalidation.");
        Assert((RendererInvalidationPolicy.FromSceneChange(SceneChangeKind.HighScaleStructure) & RendererInvalidationKind.HighScaleStructure) != 0,
            "HighScaleStructure scene changes must request high-scale structural invalidation.");

        var defaultOptions = ScenePerformanceOptions.CreateDefault();
        Assert(defaultOptions.ForceWebGlJsOwnedHighScaleRuntime == false,
            "JS-owned WebGL high-scale runtime must be opt-in by default until mixed-scene rendering is fully JS-owned.");
    }

    private static void TestRoslynExportPreviewOnly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ThreeDEngineSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "SmokeVisual.cs");
            var original = """
using ThreeDEngine.Core.Scene;

public sealed class SmokeVisual : CompositeObject3D
{
}
""";
            File.WriteAllText(file, original);

            var generatedBuild = """
protected override void Build(CompositeBuilder3D b)
{
    b.Add("Body", new Box3D { Width = 1f, Height = 1f, Depth = 1f });
}
""";
            var request = new DebuggerSourceExportRequest(
                file,
                "SmokeVisual",
                "SmokeVisual",
                3,
                3,
                hasBuildMethod: false,
                generatedBuildMethodSource: generatedBuild,
                generatedClassSource: string.Empty,
                generatedEventMembersSource: string.Empty,
                previewOnly: true);

            var result = RoslynDebuggerSourceExporter.ExportAsync(request).GetAwaiter().GetResult();
            Assert(result.Success, "Roslyn preview-only export must succeed.");
            Assert(string.IsNullOrEmpty(result.BackupPath), "Preview-only export must not create a backup path.");
            Assert(!string.IsNullOrWhiteSpace(result.DiffPreview), "Preview-only export must return a diff preview.");
            Assert(File.ReadAllText(file) == original, "Preview-only export must not write the source file.");
            Assert(result.Mode.Contains("insertion", StringComparison.OrdinalIgnoreCase), "Class without Build must use method insertion mode.");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static void TestPhysicsFixedStepLifecycle()
    {
        var scene = new Scene3D { PhysicsCore = new BasicPhysicsCore() };
        scene.PhysicsSettings.Mode = PhysicsSimulationMode.FixedStep;
        scene.PhysicsSettings.FixedDeltaSeconds = 1f / 60f;

        var cube = scene.Add(new Box3D { Width = 1f, Height = 1f, Depth = 1f });
        cube.Position = new Vector3(0f, 2f, 0f);
        cube.Rigidbody = new Rigidbody3D { UseGravity = true };

        var y = cube.Position.Y;
        var steps = scene.AdvancePhysics(1f / 10f);
        Assert(steps > 0, "Fixed-step physics mode must run at least one step for accumulated delta.");
        Assert(cube.Position.Y < y, "Fixed-step physics must move gravity body.");
    }

    private static void TestVsctKeyBindingLayout(string[] args)
    {
        var root = ResolveRepositoryRoot(args);
        var vsctPath = Path.Combine(root, "VSIXConnector", "Commands", "Open3DPreviewCommand.vsct");
        if (!File.Exists(vsctPath))
        {
            Console.WriteLine("VSCT smoke skipped: file not found at " + vsctPath);
            return;
        }

        var text = File.ReadAllText(vsctPath);
        var commandsStart = text.IndexOf("<Commands", StringComparison.Ordinal);
        var commandsEnd = text.IndexOf("</Commands>", StringComparison.Ordinal);
        var keyStart = text.IndexOf("<KeyBindings>", StringComparison.Ordinal);
        var symbolsStart = text.IndexOf("<Symbols>", StringComparison.Ordinal);

        Assert(commandsStart >= 0 && commandsEnd > commandsStart, "VSCT Commands block not found.");
        Assert(keyStart > commandsEnd, "VSCT KeyBindings must be outside Commands.");
        Assert(symbolsStart > keyStart, "VSCT KeyBindings must be before Symbols.");
        Assert(text.Contains("key1=\"Q\"", StringComparison.Ordinal) && text.Contains("mod1=\"ALT\"", StringComparison.Ordinal), "VSCT must bind Alt+Q.");
    }

    private static string ResolveRepositoryRoot(string[] args)
    {
        if (args.Length > 0 && Directory.Exists(args[0]))
        {
            return Path.GetFullPath(args[0]);
        }

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "3DEngine")) &&
                Directory.Exists(Path.Combine(dir.FullName, "VSIXConnector")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SmokeComposite : CompositeObject3D
    {
        public SmokeComposite()
        {
            Name = "Smoke Composite";
        }

        protected override void Build(CompositeBuilder3D b)
        {
            b.Add("Body", new Box3D { Width = 1f, Height = 1f, Depth = 1f });
            b.Add("Label", new Plane3D { Width = 1f, Height = 0.25f }).At(0f, 0.75f, 0f);
        }
    }
}
#endif
