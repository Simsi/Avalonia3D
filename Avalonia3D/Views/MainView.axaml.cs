using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Importers.Gltf;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Navigation;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Physics.Jitter2;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Scene;

namespace Avalonia3D.Views;

public partial class MainView : UserControl
{
    private enum DemoSceneKind
    {
        PrimitivesAndMaterials,
        LightingAndEnvironment,
        PickingAndInteraction,
        EmbeddedAvaloniaControls,
        HighScaleDigitalTwin,
        Particles,
        Physics,
        ImportedGlbModel,
        CameraArcPlanetFocus,
        ShaderLightingLab,
        BuildingWalkthrough,
        BridgeDigitalTwin,
        RenderPipelineAndDiagnostics,
        CrossPlatformStressLab
    }

    private sealed record DemoDefinition(DemoSceneKind Kind, string Title, string Summary);
    private sealed record StressParameters(
        int Columns,
        int Rows,
        int TransparentObjects,
        int ParticleCapacityA,
        int ParticleEmissionA,
        int ParticleCapacityB,
        int ParticleEmissionB,
        int TelemetryUpdatesPerSecond);

    private static readonly DemoDefinition[] DemoDefinitions =
    {
        new(DemoSceneKind.PrimitivesAndMaterials, "01. Геометрия и материалы", "Процедурные примитивы, прозрачность, разные lighting-модели и базовая анимация transform."),
        new(DemoSceneKind.LightingAndEnvironment, "02. Свет и окружение", "Directional, point и spot lights, skybox и ambient light в общем OpenGL/WebGL2 forward pipeline."),
        new(DemoSceneKind.PickingAndInteraction, "03. Picking и selection", "Объекты сцены кликаются, получают selection state и отдают данные обратно в Avalonia UI."),
        new(DemoSceneKind.EmbeddedAvaloniaControls, "04. Avalonia UI внутри 3D", "Обычные Avalonia controls рендерятся на 3D-плоскость и принимают pointer input."),
        new(DemoSceneKind.HighScaleDigitalTwin, "05. High-scale / digital twin", "Retained instance layer для большого числа похожих объектов, palette variants, telemetry updates и smooth GPU motion."),
        new(DemoSceneKind.Particles, "06. Particle system", "Несколько emitter-типов: мягкий fountain, искры и полноценные 3D cube particles."),
        new(DemoSceneKind.Physics, "07. Default rigidbody physics", "Встроенная default-физика: rigidbody, вращения, friction/restitution, sleep, CCD и многоточечные контакты для устойчивых столкновений."),
        new(DemoSceneKind.ImportedGlbModel, "08. GLB character / Doctor Watson", "Загрузка rigged/skinned GLB с embedded текстурами и authored skeletal animation clip."),
        new(DemoSceneKind.CameraArcPlanetFocus, "09. Камера: дуговой облёт и фокус", "Детализированная планета, координаты точки вводятся вручную, камера облетает тело по безопасной дуге."),
        new(DemoSceneKind.ShaderLightingLab, "10. Шейдеры, свет и цветокор", "Отдельная forward-сцена для настройки света, GPU tone mapping и визуальных пресетов."),
        new(DemoSceneKind.BuildingWalkthrough, "11. Person camera / 4-этажное здание", "Площадка и 4-этажное здание с кабинетами, мебелью и Person-навигацией с физическими коллизиями."),
        new(DemoSceneKind.BridgeDigitalTwin, "12. Цифровой двойник разводного моста", "Большой разводной мост, створки, опоры, трафик и множество интерактивных датчиков телеметрии."),
        new(DemoSceneKind.RenderPipelineAndDiagnostics, "13. RHI и diagnostics", "Actual RHI capabilities/limits, forward tone mapping, wireframe overlay, GPU timing и resource telemetry."),
        new(DemoSceneKind.CrossPlatformStressLab, "14. Cross-platform GPU stress lab", "Параметрический retained/GPU stress lab: размеры поля, прозрачность, particle capacity/emission и state churn задаются точными значениями одинаково для OpenGL/WebGL2.")
    };

    private readonly Scene3DControl _sceneControl;
    private readonly Random _random = new(20260506);
    private readonly List<Object3D> _selectableObjects = new();
    private readonly List<Object3D> _physicsObjects = new();
    private readonly List<Object3D> _characterRigObjects = new();
    private readonly List<Object3D> _bridgeSensors = new();
    private readonly List<(Object3D Obj, Vector3 Normal, float RadiusOffset)> _planetSurfaceObjects = new();
    private readonly List<(Object3D Obj, Vector3 Origin, float Phase)> _stressAnimatedObjects = new();
    private readonly CameraArcFlight3D _cameraFlight = new();

    private ComboBox _demoBox = null!;
    private TextBlock _demoTitleText = null!;
    private TextBlock _demoSummaryText = null!;
    private TextBlock _selectionText = null!;
    private TextBlock _backendText = null!;
    private TextBlock _statusText = null!;
    private CheckBox _animateCheck = null!;
    private CheckBox _metricsCheck = null!;
    private CheckBox _wireframeCheck = null!;
    private Button _primaryActionButton = null!;
    private StackPanel _demoSpecificPanel = null!;
    private TextBox? _planetLatBox;
    private TextBox? _planetLonBox;
    private TextBox? _exposureBox;
    private TextBox? _gammaBox;
    private TextBox? _ambientBox;
    private TextBox? _stressColumnsBox;
    private TextBox? _stressRowsBox;
    private TextBox? _stressTransparentBox;
    private TextBox? _stressParticleCapacityABox;
    private TextBox? _stressParticleEmissionABox;
    private TextBox? _stressParticleCapacityBBox;
    private TextBox? _stressParticleEmissionBBox;
    private TextBox? _stressTelemetryRateBox;

    private DemoSceneKind _activeDemo = DemoSceneKind.PrimitivesAndMaterials;
    private Object3D? _selectedObject;
    private Box3D? _rotatingBox;
    private Sphere3D? _orbitSphere;
    private Cylinder3D? _rotatingCylinder;
    private Cone3D? _animatedCone;
    private PointLight3D? _movingPointLight;
    private SpotLight3D? _movingSpotLight;
    private ControlPlane3D? _controlPlane;
    private HighScaleInstanceLayer3D? _rackLayer;
    private HighScaleInstanceLayer3D? _stressLayer;
    private Box3D? _physicsCube;
    private Sphere3D? _physicsBall;
    private ImportedModel3D? _doctorWatsonModel;
    private ModelAnimationSequence3D? _doctorWatsonSequence;
    private ModelAsset3D? _doctorWatsonAsset;
    private ImportedModel3D? _earthModel;
    private ModelAsset3D? _earthAsset;
    private string _doctorWatsonImportInfo = string.Empty;
    private Sphere3D? _planet;
    private Sphere3D? _planetMarker;
    private ControlPlane3D? _planetLabel;
    private ControlPlane3D? _bridgeSensorPanel;
    private Object3D? _grabbedPhysicsObject;
    private Vector3 _planetFocusPoint = Vector3.UnitZ;
    private DirectionalLight3D? _shaderSun;
    private PointLight3D? _shaderAccent;
    private float _characterSequenceTime;
    private float _doctorWatsonBaseY;
    private SceneFrameRenderedEventArgs? _lastFrame;
    private double _animationTimeSeconds;
    private double _lastFps;
    private int _clickCount;
    private int _telemetryCursor;
    private int _embeddedCounter;
    private double _stressTelemetryAccumulator;
    private int _stressTelemetryUpdateCursor;
    private long _lastStatusTicks;
    private long _lastBackendTextTicks;
    private string _lastStatusText = string.Empty;
    private bool _showingDiagnosticReport;
    private StressParameters _stressParameters = new(480, 320, 768, 24000, 9000, 12000, 4000, 120000);

    public MainView()
    {
        InitializeComponent();

        var isBrowser = OperatingSystem.IsBrowser();
        _sceneControl = new Scene3DControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ShowPerformanceMetrics = !isBrowser,
            ContinuousRendering = true,
            ContinuousRenderingFps = 60d,
            FpsLockEnabled = true,
            TargetFps = 60d,
            UnlockedMaxFps = isBrowser ? 60d : 180d,
            FrameInterpolationEnabled = true,
            AutomaticSceneUpdates = true,
            FixedUpdateFramesPerSecond = 60d,
            MaximumCatchUpSteps = 4,
            AdaptivePerformanceEnabled = false,
            EnableSceneNavigation = true,
            ShowCenterCursor = !isBrowser,
            Width = double.NaN,
            Height = double.NaN
        };
        _sceneControl.ObjectClicked += OnObjectClicked;
        _sceneControl.SelectionChanged += (_, e) => SetSelection(e.NewSelection);
        _sceneControl.FrameRendered += OnFrameRendered;
        _sceneControl.Scene.FixedUpdate += OnSceneFixedUpdate;
        _sceneControl.Scene.FixedUpdateCompleted += OnSceneFixedUpdateCompleted;

        BuildUi();
        LoadDemo(DemoSceneKind.PrimitivesAndMaterials);
    }

    private void BuildUi()
    {
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,410")
        };

        root.Children.Add(_sceneControl);

        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(232, 14, 17, 23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(54, 62, 78)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Child = BuildControlPanel()
        };
        Grid.SetColumn(panel, 1);
        root.Children.Add(panel);

        ContentGrid.Children.Clear();
        ContentGrid.Children.Add(root);
    }

    private Control BuildControlPanel()
    {
        _demoTitleText = new TextBlock
        {
            Text = "Avalonia3D demo scenes",
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 18
        };
        _demoSummaryText = new TextBlock
        {
            Text = "Выберите отдельную сцену для демонстрации конкретной возможности движка.",
            Foreground = new SolidColorBrush(Color.FromRgb(185, 194, 210)),
            TextWrapping = TextWrapping.Wrap
        };

        _demoBox = new ComboBox
        {
            ItemsSource = CreateDemoLabels(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _demoBox.SelectionChanged += (_, _) =>
        {
            if (_demoBox.SelectedIndex >= 0 && _demoBox.SelectedIndex < DemoDefinitions.Length)
            {
                LoadDemo(DemoDefinitions[_demoBox.SelectedIndex].Kind);
            }
        };

        _animateCheck = Check("Анимация", true);
        _metricsCheck = Check("Performance overlay", !OperatingSystem.IsBrowser());
        _wireframeCheck = Check("Wireframe overlay", false);

        _animateCheck.IsCheckedChanged += (_, _) =>
        {
            _sceneControl.IsSimulationPaused = _animateCheck.IsChecked != true;
            UpdateStatus(force: true);
        };
        _metricsCheck.IsCheckedChanged += (_, _) => ApplyRuntimeToggles();
        _wireframeCheck.IsCheckedChanged += (_, _) => ApplyRuntimeToggles();

        _selectionText = MonospaceText("Selection: none");
        _backendText = MonospaceText("Backend: detecting...");
        _statusText = MonospaceText("Status will appear after first frame.");

        _primaryActionButton = Button("Action", RunPrimaryAction);

        var previousButton = Button("← Previous demo", () => SwitchDemo(-1));
        var nextButton = Button("Next demo →", () => SwitchDemo(1));
        var nav = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        nav.Children.Add(previousButton);
        Grid.SetColumn(nextButton, 1);
        nav.Children.Add(nextButton);

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(_demoTitleText);
        stack.Children.Add(_demoSummaryText);
        stack.Children.Add(Section("Demo scene"));
        stack.Children.Add(_demoBox);
        stack.Children.Add(nav);
        stack.Children.Add(Button("Reset current demo", () => LoadDemo(_activeDemo)));
        stack.Children.Add(_primaryActionButton);
        _demoSpecificPanel = new StackPanel { Spacing = 8 };
        stack.Children.Add(_demoSpecificPanel);

        stack.Children.Add(Section("Runtime switches"));
        stack.Children.Add(_animateCheck);
        stack.Children.Add(_metricsCheck);
        stack.Children.Add(_wireframeCheck);

        stack.Children.Add(Section("Scene notes"));
        stack.Children.Add(new TextBlock
        {
            Text = "Сцены используют одинаковые GPU paths на Desktop и WebGL2. В stress lab нагрузка задаётся точными числовыми параметрами; движок не подменяет отсутствующий GPU path CPU-реализацией.",
            Foreground = new SolidColorBrush(Color.FromRgb(185, 194, 210)),
            TextWrapping = TextWrapping.Wrap
        });

        stack.Children.Add(Section("Runtime"));
        stack.Children.Add(_backendText);
        stack.Children.Add(_selectionText);
        stack.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 82)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = _statusText
        });

        return new ScrollViewer { Content = stack };
    }

    private static string[] CreateDemoLabels()
    {
        var labels = new string[DemoDefinitions.Length];
        for (var i = 0; i < DemoDefinitions.Length; i++)
        {
            labels[i] = DemoDefinitions[i].Title;
        }
        return labels;
    }

    private void SwitchDemo(int delta)
    {
        var next = _demoBox.SelectedIndex + delta;
        if (next < 0) next = DemoDefinitions.Length - 1;
        if (next >= DemoDefinitions.Length) next = 0;
        _demoBox.SelectedIndex = next;
    }

    private void LoadDemo(DemoSceneKind kind)
    {
        _activeDemo = kind;
        ResetRuntimeRefs();

        ConfigureSceneControlForDemo(kind);

        var definition = GetDefinition(kind);
        _demoTitleText.Text = definition.Title;
        _demoSummaryText.Text = definition.Summary;

        var scene = _sceneControl.Scene;
        scene.UpdateLoop.Reset(resetTimeline: true);
        using (scene.BeginUpdate())
        {
            scene.Clear();
            ConfigureBaseScene(scene);

            switch (kind)
            {
                case DemoSceneKind.PrimitivesAndMaterials:
                    BuildPrimitivesAndMaterialsScene(scene);
                    break;
                case DemoSceneKind.LightingAndEnvironment:
                    BuildLightingAndEnvironmentScene(scene);
                    break;
                case DemoSceneKind.PickingAndInteraction:
                    BuildPickingAndInteractionScene(scene);
                    break;
                case DemoSceneKind.EmbeddedAvaloniaControls:
                    BuildEmbeddedAvaloniaControlsScene(scene);
                    break;
                case DemoSceneKind.HighScaleDigitalTwin:
                    BuildHighScaleDigitalTwinScene(scene);
                    break;
                case DemoSceneKind.Particles:
                    BuildParticlesScene(scene);
                    break;
                case DemoSceneKind.Physics:
                    BuildPhysicsScene(scene);
                    break;
                case DemoSceneKind.ImportedGlbModel:
                    BuildImportedGlbModelScene(scene);
                    break;
                case DemoSceneKind.CameraArcPlanetFocus:
                    BuildCameraArcPlanetScene(scene);
                    break;
                case DemoSceneKind.ShaderLightingLab:
                    BuildShaderLightingLabScene(scene);
                    break;
                case DemoSceneKind.BuildingWalkthrough:
                    BuildBuildingWalkthroughScene(scene);
                    break;
                case DemoSceneKind.BridgeDigitalTwin:
                    BuildBridgeDigitalTwinScene(scene);
                    break;
                case DemoSceneKind.RenderPipelineAndDiagnostics:
                    BuildPipelineAndDiagnosticsScene(scene);
                    break;
                case DemoSceneKind.CrossPlatformStressLab:
                    BuildCrossPlatformStressLabScene(scene);
                    break;
            }
        }

        ApplyRuntimeToggles();
        BuildDemoSpecificControls(kind);
        UpdatePrimaryActionCaption();
        SetSelection(null);
        UpdateStatus(force: true);
    }

    private static DemoDefinition GetDefinition(DemoSceneKind kind)
    {
        foreach (var definition in DemoDefinitions)
        {
            if (definition.Kind == kind)
            {
                return definition;
            }
        }
        return DemoDefinitions[0];
    }


    private void ConfigureSceneControlForDemo(DemoSceneKind kind)
    {
        if (kind == DemoSceneKind.BuildingWalkthrough)
        {
            _sceneControl.NavigationMode = SceneNavigationMode.Person;
            _sceneControl.MouseLookMode = SceneMouseLookMode.ButtonDrag;
            _sceneControl.PersonSettings.MoveSpeed = 2.75f;
            _sceneControl.PersonSettings.RunMultiplier = 1.45f;
            _sceneControl.PersonSettings.BodyRadius = 0.22f;
            _sceneControl.PersonSettings.BodyHeight = 1.72f;
            _sceneControl.PersonSettings.EyeHeight = 1.56f;
            _sceneControl.PersonSettings.StepHeight = 0.24f;
            _sceneControl.PersonSettings.JumpSpeed = 3.0f;
            _sceneControl.PersonSettings.Gravity = -9.81f;
            _sceneControl.ShowCenterCursor = true;
        }
        else
        {
            _sceneControl.NavigationMode = SceneNavigationMode.FreeFly;
            _sceneControl.MouseLookMode = SceneMouseLookMode.ButtonDrag;
            _sceneControl.ShowCenterCursor = true;
        }
    }

    private void ResetRuntimeRefs()
    {
        _selectableObjects.Clear();
        _physicsObjects.Clear();
        _characterRigObjects.Clear();
        _bridgeSensors.Clear();
        _planetSurfaceObjects.Clear();
        _stressAnimatedObjects.Clear();
        _cameraFlight.Cancel();
        _selectedObject = null;
        _rotatingBox = null;
        _orbitSphere = null;
        _rotatingCylinder = null;
        _animatedCone = null;
        _movingPointLight = null;
        _movingSpotLight = null;
        _controlPlane = null;
        _rackLayer = null;
        _stressLayer = null;
        _physicsCube = null;
        _physicsBall = null;
        _doctorWatsonModel = null;
        _doctorWatsonSequence = null;
        _doctorWatsonImportInfo = string.Empty;
        _earthModel = null;
        _planet = null;
        _planetMarker = null;
        _planetLabel = null;
        _bridgeSensorPanel = null;
        _grabbedPhysicsObject = null;
        _shaderSun = null;
        _shaderAccent = null;
        _characterSequenceTime = 0f;
        _animationTimeSeconds = 0d;
        _doctorWatsonBaseY = 0f;
        _clickCount = 0;
        _telemetryCursor = 0;
        _stressTelemetryAccumulator = 0d;
        _stressTelemetryUpdateCursor = 0;
    }

    private static void ConfigureBaseScene(Scene3D scene)
    {
        scene.BackgroundColor = new ColorRgba(0.025f, 0.030f, 0.042f, 1f);
        scene.AmbientLightColor = new ColorRgba(0.75f, 0.82f, 1f, 1f);
        scene.AmbientLightIntensity = 0.24f;
        scene.Camera.Position = new Vector3(6.8f, 4.8f, -9.2f);
        scene.Camera.Target = new Vector3(0f, 1.1f, 0f);
        scene.Camera.FarPlane = 300f;
        scene.Camera.FieldOfViewDegrees = 52f;

        scene.Environment.Skybox.Mode = SkyboxMode3D.VerticalGradient;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.05f, 0.09f, 0.17f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.13f, 0.18f, 0.27f, 1f);
        scene.Environment.Skybox.BottomColor = new ColorRgba(0.015f, 0.018f, 0.025f, 1f);
        scene.Environment.Skybox.Intensity = 1.0f;

        scene.Debug.ShowWireframeOverlay = false;
        scene.Performance.EnableWebGlClientGpuTransformAnimation = false;
        scene.Performance.WebGlClientGpuTransformAnimationAmplitude = 0f;
        scene.Performance.EnableWebGlClientHighScaleRuntime = true;
        scene.Performance.UseConservativeSkinnedPicking = true;
        scene.Performance.EnableBakedHighScaleDetailedMeshes = true;
        scene.Performance.EnableHighScalePaletteTexture = true;
        scene.FrameInterpolator.Enabled = true;
        scene.SetPhysicsEnabled(false);

        scene.RenderPipeline.Mode = RenderPipelineMode3D.Forward;
        scene.RenderPipeline.EnableDeferredLighting = false;
        scene.RenderPipeline.EnableHdr = false;
        scene.RenderPipeline.EnableTransparentForwardPass = true;
        scene.RenderPipeline.EnableMotionVectorMetadata = false;
        scene.RenderPipeline.Ssao.Enabled = false;
        scene.RenderPipeline.ToneMapping.Enabled = false;

        scene.AddLight(new DirectionalLight3D
        {
            Direction = Vector3.Normalize(new Vector3(-0.38f, -0.82f, -0.42f)),
            Intensity = 1.18f,
            Color = new ColorRgba(1f, 0.94f, 0.84f, 1f)
        });
    }

    private void BuildPrimitivesAndMaterialsScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(6.8f, 4.4f, -8.0f);
        scene.Camera.Target = new Vector3(0f, 1.0f, 0.25f);
        AddGround(scene, 10f, 7f);

        var metal = new Material3D
        {
            BaseColor = new ColorRgba(0.25f, 0.62f, 0.98f, 1f),
            Lighting = LightingMode.BlinnPhong,
            SpecularStrength = 0.82f,
            Shininess = 96f,
            Metallic = 0.35f,
            Roughness = 0.28f
        };
        var glass = new Material3D
        {
            BaseColor = new ColorRgba(0.40f, 0.85f, 1f, 0.34f),
            Opacity = 0.34f,
            Surface = SurfaceMode.Transparent,
            Lighting = LightingMode.Phong,
            SpecularStrength = 0.75f,
            Shininess = 128f
        };

        _rotatingBox = AddSelectable(scene, new Box3D
        {
            Name = "Box3D / BlinnPhong material",
            Width = 1.25f,
            Height = 1.25f,
            Depth = 1.25f,
            Position = new Vector3(-2.7f, 0.72f, 0f),
            RotationDegrees = new Vector3(0f, 28f, 0f),
            Material = metal
        });

        _orbitSphere = AddSelectable(scene, new Sphere3D
        {
            Name = "Sphere3D / Phong material",
            Radius = 0.72f,
            Segments = 40,
            Rings = 20,
            Position = new Vector3(-0.9f, 0.92f, 0f),
            Material = Material3D.CreatePhong(new ColorRgba(1.0f, 0.58f, 0.20f, 1f), 0.55f, 64f)
        });

        _rotatingCylinder = AddSelectable(scene, new Cylinder3D
        {
            Name = "Cylinder3D / Lambert material",
            Radius = 0.44f,
            Height = 1.8f,
            Segments = 40,
            Position = new Vector3(1.0f, 0.9f, 0f),
            Material = Material3D.CreateLambert(new ColorRgba(0.16f, 0.78f, 0.46f, 1f))
        });

        _animatedCone = AddSelectable(scene, new Cone3D
        {
            Name = "Cone3D / transparent material",
            Radius = 0.68f,
            Height = 1.55f,
            Segments = 36,
            Position = new Vector3(2.8f, 0.77f, 0f),
            Material = glass
        });

        AddAnimatedObjectGallery(scene, new Vector3(0f, 0.30f, 2.35f), 18, 9, 0.72f, 0.72f);
    }

    private void BuildLightingAndEnvironmentScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(7.6f, 5.1f, -8.6f);
        scene.Camera.Target = new Vector3(0.2f, 1.0f, 0.3f);
        scene.AmbientLightIntensity = 0.16f;
        AddGround(scene, 12f, 8f);

        AddSelectable(scene, new Box3D
        {
            Name = "Lighting sample cube",
            Width = 1.3f,
            Height = 1.3f,
            Depth = 1.3f,
            Position = new Vector3(-1.7f, 0.72f, 0.2f),
            Material = Material3D.CreateLambert(new ColorRgba(0.42f, 0.62f, 0.96f, 1f))
        });
        _orbitSphere = AddSelectable(scene, new Sphere3D
        {
            Name = "Specular sphere under moving point light",
            Radius = 0.72f,
            Segments = 48,
            Rings = 24,
            Position = new Vector3(1.2f, 0.86f, -0.25f),
            Material = Material3D.CreatePhong(new ColorRgba(0.88f, 0.42f, 1f, 1f), 0.72f, 96f)
        });
        AddSelectable(scene, new Cone3D
        {
            Name = "Spot light target cone",
            Radius = 0.6f,
            Height = 1.5f,
            Position = new Vector3(3.2f, 0.75f, 0.6f),
            Material = Material3D.CreateLambert(new ColorRgba(0.96f, 0.70f, 0.32f, 1f))
        });

        _movingPointLight = scene.AddLight(new PointLight3D
        {
            Position = new Vector3(-2.4f, 3.4f, -1.7f),
            Range = 9f,
            Intensity = 3.4f,
            Color = new ColorRgba(0.50f, 0.70f, 1f, 1f)
        });
        _movingSpotLight = scene.AddLight(new SpotLight3D
        {
            Position = new Vector3(3.2f, 4.1f, -2.7f),
            Direction = Vector3.Normalize(new Vector3(-0.25f, -1f, 0.55f)),
            Range = 14f,
            Intensity = 3.0f,
            InnerConeDegrees = 18f,
            OuterConeDegrees = 34f,
            Color = new ColorRgba(1f, 0.78f, 0.52f, 1f)
        });

        AddAnimatedObjectGallery(scene, new Vector3(0f, 0.24f, 2.45f), 15, 9, 0.78f, 0.64f);
    }

    private void BuildPickingAndInteractionScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(8.6f, 6.2f, -12.6f);
        scene.Camera.Target = new Vector3(0f, 0.9f, 0f);
        AddGround(scene, 13f, 20f);

        var pickableCount = 24;
        const int columns = 12;
        var rows = (pickableCount + columns - 1) / columns;
        for (var i = 0; i < pickableCount; i++)
        {
            var x = (i % columns - (columns - 1) * 0.5f) * 0.88f;
            var z = (i / columns - (rows - 1) * 0.5f) * 0.86f;
            var color = ColorFromHue(i * 0.071f);
            Object3D obj = (i % 3) switch
            {
                0 => new Box3D
                {
                    Name = "Pickable station " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Width = 0.82f,
                    Height = 0.82f,
                    Depth = 0.82f,
                    Position = new Vector3(x, 0.46f, z),
                    Material = Material3D.CreatePhong(color, 0.45f, 48f)
                },
                1 => new Sphere3D
                {
                    Name = "Pickable sensor " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Radius = 0.46f,
                    Segments = 32,
                    Rings = 16,
                    Position = new Vector3(x, 0.52f, z),
                    Material = Material3D.CreateLambert(color)
                },
                _ => new Cylinder3D
                {
                    Name = "Pickable module " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Radius = 0.34f,
                    Height = 0.92f,
                    Segments = 28,
                    Position = new Vector3(x, 0.46f, z),
                    Material = Material3D.CreatePhong(color, 0.34f, 36f)
                }
            };
            AddSelectable(scene, obj);
        }

        _rotatingBox = scene.Add(new Box3D
        {
            Name = "Selection marker / non-pickable helper",
            Width = 11.2f,
            Height = 0.04f,
            Depth = 0.08f,
            Position = new Vector3(0f, 0.04f, 0.92f),
            Material = Material3D.CreateLambert(new ColorRgba(0.32f, 0.36f, 0.44f, 1f)),
            IsPickable = false,
            IsManipulationEnabled = false
        });
    }

    private void BuildEmbeddedAvaloniaControlsScene(Scene3D scene)
    {
        scene.FrameInterpolator.Enabled = false;
        scene.Camera.Position = new Vector3(5.8f, 3.1f, -6.6f);
        scene.Camera.Target = new Vector3(-0.55f, 1.15f, 0.1f);
        AddGround(scene, 9f, 6f);

        var model = AddSelectable(scene, new Box3D
        {
            Name = "3D object controlled by Avalonia button",
            Width = 1.25f,
            Height = 1.25f,
            Depth = 1.25f,
            Position = new Vector3(-2.45f, 0.75f, 0.35f),
            RotationDegrees = new Vector3(0f, 28f, 0f),
            Material = Material3D.CreatePhong(new ColorRgba(0.26f, 0.66f, 1f, 1f), 0.55f, 72f)
        });
        _rotatingBox = model;

        var title = new TextBlock
        {
            Text = "Live Avalonia UI in 3D",
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 18
        };
        var counterText = new TextBlock
        {
            Text = "Button clicks: 0",
            Foreground = new SolidColorBrush(Color.FromRgb(205, 216, 232)),
            TextWrapping = TextWrapping.Wrap
        };
        var button = new Button
        {
            Content = "Change 3D object color",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) =>
        {
            _embeddedCounter++;
            counterText.Text = "Button clicks: " + _embeddedCounter.ToString(CultureInfo.InvariantCulture);
            if (_rotatingBox is not null)
            {
                var hue = (_embeddedCounter % 6) / 6f;
                _rotatingBox.Material.BaseColor = ColorFromHue(hue);
            }
        };

        var statusBadge = new Border
        {
            Width = 440,
            Height = 220,
            Background = new SolidColorBrush(Color.FromArgb(238, 18, 24, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(76, 110, 156)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    title,
                    new TextBlock
                    {
                        Text = "Это обычный Avalonia Button, отрисованный на 3D-плоскости. Плоскость включена в billboard/front-facing режим, поэтому текст не зеркалится к пользователю.",
                        Foreground = new SolidColorBrush(Color.FromRgb(195, 205, 220)),
                        TextWrapping = TextWrapping.Wrap
                    },
                    counterText,
                    button
                }
            }
        };

        scene.Add(new Sphere3D
        {
            Name = "UI demo reference sphere",
            Radius = 0.42f,
            Segments = 32,
            Rings = 16,
            Position = new Vector3(-0.85f, 0.48f, 0.75f),
            Material = Material3D.CreatePhong(new ColorRgba(1.0f, 0.72f, 0.18f, 1f), 0.48f, 64f),
            IsPickable = true
        });
        scene.Add(new Cylinder3D
        {
            Name = "UI demo reference cylinder",
            Radius = 0.28f,
            Height = 1.25f,
            Segments = 32,
            Position = new Vector3(0.30f, 0.63f, 1.05f),
            Material = Material3D.CreateLambert(new ColorRgba(0.22f, 0.82f, 0.48f, 1f)),
            IsPickable = true
        });

        _controlPlane = scene.Add(new ControlPlane3D(statusBadge)
        {
            Name = "Embedded Avalonia control plane / front-facing",
            Width = 2.70f,
            Height = 1.35f,
            Position = new Vector3(1.95f, 1.75f, 0.20f),
            AlwaysFaceCamera = true,
            RenderScale = 2.0d
        });
        AddAnimatedObjectGallery(scene, new Vector3(-0.25f, 0.22f, 2.05f), 12, 12, 0.62f, 0.58f);
    }

    private void BuildHighScaleDigitalTwinScene(Scene3D scene)
    {
        scene.FrameInterpolator.Enabled = false;
        scene.Camera.Position = new Vector3(13.8f, 7.0f, -14.8f);
        scene.Camera.Target = new Vector3(0.6f, 1.35f, 0.4f);
        scene.Camera.FarPlane = 500f;
        scene.Performance.DrawDistance = 1000f;
        var rackColumns = 24;
        var rackRows = 16;
        AddGround(scene, rackColumns * 1.18f, rackRows * 1.16f);
        scene.AmbientLightIntensity = 0.38f;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.05f, 0.09f, 0.15f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.13f, 0.19f, 0.25f, 1f);
        scene.AddLight(new DirectionalLight3D { Direction = Vector3.Normalize(new Vector3(-0.38f, -0.82f, -0.24f)), Intensity = 1.08f, Color = new ColorRgba(1f, 0.95f, 0.86f, 1f) });
        scene.AddLight(new PointLight3D { Position = new Vector3(-5f, 5.2f, -5f), Range = 28f, Intensity = 2.4f, Color = new ColorRgba(0.55f, 0.76f, 1f, 1f) });

        // The same retained configuration and counts are used on native OpenGL and WebGL2.
        // Animation remains disabled in this topology-focused scene; the dedicated stress lab
        // exercises the shared shader-side transform path.
        scene.Performance.EnableWebGlClientGpuTransformAnimation = false;
        scene.Performance.WebGlClientGpuTransformAnimationAmplitude = 0f;
        scene.Performance.MaxVisibleHighScaleChunks = 0;
        scene.Performance.MaxHighScaleVisibleInstances = 0;
        scene.Performance.EnableDistanceFade = false;
        // Force the same aggregate instance path for both backends so camera movement cannot
        // select a platform-specific chunk submission strategy.
        scene.Performance.EnableHighScaleAggregateLayerBatches = true;
        scene.Performance.HighScaleAggregateLayerInstanceThreshold = 10000;

        // Keep detailed rack rendering unbaked in this demo. The generic baked-detailed path is
        // still useful for large scenes, but for the current rack template it can collapse the
        // composite into invalid geometry on Desktop. Unbaked detailed parts still render via
        // instancing (one draw per part across all racks), so the scene remains lightweight while
        // the cabinets stay visually correct.
        var template = HighScaleTemplateCompiler.Compile(3001, new DemoRack3D(), false);
        scene.Performance.EnableBakedHighScaleDetailedMeshes = false;
        template.AddMaterialVariant(1, "Warning").DefaultColor = new ColorRgba(1.0f, 0.72f, 0.18f, 1f);
        template.AddMaterialVariant(2, "Critical").DefaultColor = new ColorRgba(1.0f, 0.19f, 0.14f, 1f);
        template.AddMaterialVariant(3, "Offline").DefaultColor = new ColorRgba(0.22f, 0.24f, 0.29f, 0.52f);

        var layer = new HighScaleInstanceLayer3D(template, rackColumns * rackRows, 7f) { Name = "Digital twin retained rack layer" };
        layer.LodPolicy.DetailedDistance = 44f;
        layer.LodPolicy.SimplifiedDistance = 86f;
        layer.LodPolicy.ProxyDistance = 160f;
        layer.LodPolicy.DrawDistance = 420f;
        layer.LodPolicy.FadeDistance = 0f;
        layer.LodPolicy.EnableBillboardFallback = false;
        layer.AddInstances(CreateRackTransforms(rackColumns, rackRows, 1.05f, 1.02f));
        scene.Add(layer);
        _rackLayer = layer;

        var floor = Material3D.CreateLambert(new ColorRgba(0.12f, 0.14f, 0.16f, 1f));
        var aisle = Material3D.CreateLambert(new ColorRgba(0.20f, 0.23f, 0.26f, 1f));
        var cable = Material3D.CreatePhong(new ColorRgba(0.08f, 0.12f, 0.18f, 1f), 0.28f, 44f);

            // Do not duplicate rack cabinet geometry here. The retained layer above is the
            // single source of rack rendering; overlapping StaticBox cabinets caused depth
            // fighting and angle-dependent disappearing/flickering in the digital twin demo.
            for (var z = -rackRows / 2; z <= rackRows / 2; z += 3)
            {
                StaticBox(scene, "Service aisle " + z.ToString(CultureInfo.InvariantCulture), rackColumns * 1.10f, 0.035f, 0.10f, new Vector3(0f, 0.055f, z * 1.02f), aisle, pickable: false);
            }
            for (var x = -rackColumns / 2; x <= rackColumns / 2; x += 4)
            {
                StaticBox(scene, "Overhead cable tray " + x.ToString(CultureInfo.InvariantCulture), 0.08f, 0.10f, rackRows * 1.08f, new Vector3(x * 1.05f, 2.65f, 0f), cable, pickable: false);
            }
            var beaconCount = 24;
            for (var i = 0; i < beaconCount; i++)
            {
                var x = ((i * 7) % rackColumns - rackColumns * 0.5f) * 1.05f;
                var z = ((i * 11) % rackRows - rackRows * 0.5f) * 1.02f;
                var beacon = scene.Add(new Sphere3D
                {
                    Name = "Telemetry beacon " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Radius = 0.08f,
                    Segments = 16,
                    Rings = 8,
                    Position = new Vector3(x, 1.38f + (i % 3) * 0.18f, z),
                    Material = new Material3D { BaseColor = i % 5 == 0 ? new ColorRgba(1f, 0.22f, 0.12f, 1f) : new ColorRgba(0.18f, 1f, 0.44f, 1f), Lighting = LightingMode.Unlit },
                    IsPickable = true,
                    DataContext = "rack telemetry: temp " + (32 + i % 7).ToString(CultureInfo.InvariantCulture) + "°C, load " + (58 + i * 2 % 35).ToString(CultureInfo.InvariantCulture) + "%"
                });
                _selectableObjects.Add(beacon);
            }
        StaticBox(scene, "Operations floor mat", rackColumns * 1.12f, 0.02f, rackRows * 1.10f, new Vector3(0f, 0.07f, 0f), floor, pickable: false);
        RandomizeRackTelemetry();
    }


    private void BuildParticlesScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(6.2f, 4.0f, -7.4f);
        scene.Camera.Target = new Vector3(0.2f, 1.25f, 0f);
        scene.AmbientLightIntensity = 0.30f;
        AddGround(scene, 9f, 6.5f);

        scene.Add(new Cylinder3D
        {
            Name = "Particle emitters base",
            Radius = 0.72f,
            Height = 0.18f,
            Segments = 40,
            Position = new Vector3(0f, 0.10f, 0f),
            Material = Material3D.CreatePhong(new ColorRgba(0.24f, 0.28f, 0.37f, 1f), 0.42f, 48f),
            IsPickable = false
        });

        AddParticleSystem(scene,
            "Blue fountain / soft quads",
            new Vector3(-1.35f, 0.28f, -0.1f),
            new ParticleSystemSettings3D
            {
                Capacity = 520,
                EmissionRatePerSecond = 110f,
                ParticleLifetimeSeconds = 2.35f,
                StartSize = 0.13f,
                EndSize = 0.018f,
                StartColor = new ColorRgba(0.42f, 0.82f, 1f, 0.92f),
                EndColor = new ColorRgba(0.10f, 0.20f, 0.55f, 0f),
                InitialSpeed = 1.75f,
                Spread = 0.55f,
                Prewarm = true,
                RenderMode = ParticleRenderMode3D.CameraFacingQuad
            },
            new ParticleEmitter3D(101)
            {
                Direction = new Vector3(0.08f, 1f, -0.03f),
                Gravity = new Vector3(0f, -0.54f, 0f)
            });

        AddParticleSystem(scene,
            "Orange sparks / fast quads",
            new Vector3(0.25f, 0.32f, 0.25f),
            new ParticleSystemSettings3D
            {
                Capacity = 360,
                EmissionRatePerSecond = 75f,
                ParticleLifetimeSeconds = 1.25f,
                StartSize = 0.075f,
                EndSize = 0.006f,
                StartColor = new ColorRgba(1.0f, 0.74f, 0.20f, 1f),
                EndColor = new ColorRgba(0.88f, 0.12f, 0.04f, 0f),
                InitialSpeed = 2.35f,
                Spread = 0.92f,
                Prewarm = true,
                RenderMode = ParticleRenderMode3D.CameraFacingQuad
            },
            new ParticleEmitter3D(202)
            {
                Direction = new Vector3(-0.15f, 1f, 0.18f),
                Gravity = new Vector3(0f, -1.25f, 0f)
            });

        AddParticleSystem(scene,
            "3D cube particles / debris",
            new Vector3(1.35f, 0.36f, -0.18f),
            new ParticleSystemSettings3D
            {
                Capacity = 130,
                EmissionRatePerSecond = 28f,
                ParticleLifetimeSeconds = 2.8f,
                StartSize = 0.105f,
                EndSize = 0.045f,
                StartColor = new ColorRgba(0.72f, 1.0f, 0.46f, 0.95f),
                EndColor = new ColorRgba(0.16f, 0.36f, 0.10f, 0f),
                InitialSpeed = 1.15f,
                Spread = 0.72f,
                Prewarm = true,
                RenderMode = ParticleRenderMode3D.Cube3D
            },
            new ParticleEmitter3D(303)
            {
                Direction = new Vector3(0f, 0.95f, 0.08f),
                Gravity = new Vector3(0f, -0.42f, 0f)
            });

        AddParticleSystem(scene,
            "Volumetric snow / slow quads",
            new Vector3(-0.15f, 2.55f, 1.05f),
            new ParticleSystemSettings3D
            {
                Capacity = 720,
                EmissionRatePerSecond = 135f,
                ParticleLifetimeSeconds = 4.2f,
                StartSize = 0.045f,
                EndSize = 0.020f,
                StartColor = new ColorRgba(0.90f, 0.96f, 1f, 0.82f),
                EndColor = new ColorRgba(0.72f, 0.86f, 1f, 0f),
                InitialSpeed = 0.55f,
                Spread = 1.45f,
                Prewarm = true,
                RenderMode = ParticleRenderMode3D.CameraFacingQuad
            },
            new ParticleEmitter3D(404)
            {
                Direction = new Vector3(0.08f, -1f, 0.06f),
                Gravity = new Vector3(0.03f, -0.22f, 0.02f)
            });

        AddParticleSystem(scene,
            "3D cube orbit field / telemetry blocks",
            new Vector3(2.45f, 1.05f, 0.75f),
            new ParticleSystemSettings3D
            {
                Capacity = 90,
                EmissionRatePerSecond = 16f,
                ParticleLifetimeSeconds = 5.2f,
                StartSize = 0.075f,
                EndSize = 0.030f,
                StartColor = new ColorRgba(1f, 0.40f, 0.95f, 0.95f),
                EndColor = new ColorRgba(0.28f, 0.12f, 0.82f, 0f),
                InitialSpeed = 0.82f,
                Spread = 1.95f,
                Prewarm = true,
                RenderMode = ParticleRenderMode3D.Cube3D
            },
            new ParticleEmitter3D(505)
            {
                Direction = new Vector3(-0.55f, 0.48f, -0.35f),
                Gravity = new Vector3(0f, 0.02f, 0f)
            });
    }

    private ParticleSystem3D AddParticleSystem(Scene3D scene, string name, Vector3 position, ParticleSystemSettings3D settings, ParticleEmitter3D emitter)
    {
        var loadMultiplier = 1.75f;
        settings.Capacity = global::System.Math.Max(1, (int)(settings.Capacity * loadMultiplier));
        settings.EmissionRatePerSecond *= loadMultiplier;
        var system = scene.Add(new ParticleSystem3D(settings, emitter)
        {
            Name = name,
            Position = position,
            Material = new Material3D
            {
                BaseColor = settings.StartColor,
                Lighting = settings.RenderMode == ParticleRenderMode3D.Cube3D ? LightingMode.Phong : LightingMode.Unlit,
                Surface = SurfaceMode.Transparent,
                Opacity = settings.StartColor.A
            }
        });
        return system;
    }

    private void BuildPhysicsScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(5.6f, 3.4f, -6.8f);
        scene.Camera.Target = new Vector3(0.3f, 0.9f, 0.2f);
        scene.ReplacePhysicsCore(new Jitter2PhysicsCore
        {
            FixedTimeStep = 1f / 120f,
            MaxStepsPerFrame = 10,
            SubstepCount = 4,
            SolverIterations = (solver: 14, relaxation: 5),
            Gravity = new Vector3(0f, -9.81f, 0f)
        });

        scene.Add(new Plane3D
        {
            Name = "Tilted finite collision plane / slope",
            Width = 8.5f,
            Height = 4.8f,
            Position = new Vector3(0f, 0.03f, 0f),
            RotationDegrees = new Vector3(0f, 0f, -8.5f),
            Material = new Material3D
            {
                BaseColor = new ColorRgba(0.30f, 0.33f, 0.38f, 1f),
                Lighting = LightingMode.Lambert,
                Roughness = 0.96f
            },
            IsPickable = false,
            IsManipulationEnabled = false
        });

        scene.Add(new Box3D
        {
            Name = "Lower catch rail / static collider",
            Width = 8.6f,
            Height = 0.32f,
            Depth = 0.24f,
            Position = new Vector3(0f, 0.18f, 2.52f),
            RotationDegrees = new Vector3(0f, 0f, -8.5f),
            Material = Material3D.CreateLambert(new ColorRgba(0.10f, 0.12f, 0.16f, 1f)),
            IsPickable = false,
            IsManipulationEnabled = false
        });

        scene.Add(new Box3D
        {
            Name = "Physics obstacle wedge / static collider",
            Width = 0.72f,
            Height = 0.34f,
            Depth = 1.2f,
            Position = new Vector3(1.15f, 0.28f, 0.42f),
            RotationDegrees = new Vector3(0f, 28f, -8.5f),
            Material = Material3D.CreateLambert(new ColorRgba(0.20f, 0.23f, 0.28f, 1f)),
            IsPickable = false,
            IsManipulationEnabled = false
        });

        _physicsCube = AddSelectable(scene, new Box3D
        {
            Name = "Rigid box / off-center mass",
            Width = 0.74f,
            Height = 0.74f,
            Depth = 0.74f,
            Position = new Vector3(-2.5f, 2.25f, -0.8f),
            Material = Material3D.CreatePhong(new ColorRgba(1f, 0.28f, 0.20f, 1f), 0.42f, 48f),
            Rigidbody = new Rigidbody3D
            {
                Mass = 1.2f,
                FreezeRotation = false,
                GenerateContactRotation = true,
                CenterOfMassLocal = new Vector3(0.12f, -0.14f, 0.05f),
                InertiaTensor = new Vector3(0.22f, 0.25f, 0.21f),
                Restitution = 0.05f,
                Friction = 0.68f,
                RollingFriction = 0.025f,
                LinearDamping = 0.018f,
                AngularDamping = 0.020f,
                MaxAngularSpeed = 14.0f,
                CollisionTorqueScale = 1.0f,
                Velocity = new Vector3(0.25f, 0f, 0.20f),
                AngularVelocity = Vector3.Zero
            }
        });
        _physicsObjects.Add(_physicsCube);

        _physicsBall = AddSelectable(scene, new Sphere3D
        {
            Name = "Rolling sphere / angular impulse",
            Radius = 0.34f,
            Segments = 36,
            Rings = 18,
            Position = new Vector3(-1.15f, 2.80f, -0.35f),
            Material = Material3D.CreatePhong(new ColorRgba(0.22f, 0.72f, 1f, 1f), 0.68f, 80f),
            Rigidbody = new Rigidbody3D
            {
                Mass = 0.82f,
                FreezeRotation = false,
                GenerateContactRotation = true,
                InertiaTensor = new Vector3(0.075f, 0.075f, 0.075f),
                Restitution = 0.08f,
                Friction = 0.38f,
                RollingFriction = 0.010f,
                RollingRadius = 0.34f,
                LinearDamping = 0.006f,
                AngularDamping = 0.014f,
                MaxAngularSpeed = 18.0f,
                CollisionTorqueScale = 1.0f,
                Velocity = new Vector3(0.55f, 0f, 0.20f)
            }
        });
        _physicsObjects.Add(_physicsBall);

        for (var i = 0; i < 3; i++)
        {
            var body = AddSelectable(scene, new Box3D
            {
                Name = "Stable rigid body stack " + (i + 1).ToString(CultureInfo.InvariantCulture),
                Width = 0.48f,
                Height = 0.48f,
                Depth = 0.48f,
                Position = new Vector3(-0.3f + i * 0.55f, 2.1f + i * 0.44f, -1.15f + i * 0.15f),
                Material = Material3D.CreatePhong(ColorFromHue(0.18f + i * 0.12f), 0.38f, 56f),
                Rigidbody = new Rigidbody3D
                {
                    Mass = 0.65f,
                    FreezeRotation = false,
                    GenerateContactRotation = true,
                    CenterOfMassLocal = new Vector3(0.03f - i * 0.02f, -0.05f, 0.02f),
                    InertiaTensor = new Vector3(0.085f, 0.090f, 0.080f),
                    Restitution = 0.04f,
                    Friction = 0.72f,
                    RollingFriction = 0.020f,
                    LinearDamping = 0.020f,
                    AngularDamping = 0.024f,
                    MaxAngularSpeed = 14.0f,
                    CollisionTorqueScale = 1.0f,
                    Velocity = new Vector3(0.18f + i * 0.06f, 0f, 0.04f - i * 0.02f)
                }
            });
            _physicsObjects.Add(body);
        }

        var stressBodyCount = 12;
        for (var i = 0; i < stressBodyCount; i++)
        {
            var column = i % 8;
            var layer = i / 8;
            Object3D body = (i & 1) == 0
                ? new Box3D
                {
                    Name = "Physics stress box " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Width = 0.34f,
                    Height = 0.34f,
                    Depth = 0.34f,
                    Position = new Vector3(-2.3f + column * 0.62f, 3.5f + layer * 0.46f, -0.4f + (i % 3) * 0.42f),
                    Material = Material3D.CreatePhong(ColorFromHue(i * 0.093f), 0.32f, 42f),
                    Rigidbody = CreateStressRigidbody(i)
                }
                : new Sphere3D
                {
                    Name = "Physics stress sphere " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Radius = 0.19f,
                    Segments = 20,
                    Rings = 10,
                    Position = new Vector3(-2.3f + column * 0.62f, 3.5f + layer * 0.46f, -0.4f + (i % 3) * 0.42f),
                    Material = Material3D.CreatePhong(ColorFromHue(i * 0.093f), 0.38f, 48f),
                    Rigidbody = CreateStressRigidbody(i)
                };
            AddSelectable(scene, body);
            _physicsObjects.Add(body);
        }

        foreach (var obj in _physicsObjects)
        {
            EnablePhysicsGrab(obj);
        }
    }

    private static Rigidbody3D CreateStressRigidbody(int index)
        => new()
        {
            Mass = 0.42f + (index % 5) * 0.08f,
            Restitution = 0.08f + (index % 3) * 0.04f,
            Friction = 0.58f,
            RollingFriction = 0.012f,
            LinearDamping = 0.008f,
            AngularDamping = 0.014f,
            MaxAngularSpeed = 20f,
            GenerateContactRotation = true,
            Velocity = new Vector3((index % 4 - 1.5f) * 0.08f, 0f, ((index * 3) % 5 - 2f) * 0.05f)
        };

    private void EnablePhysicsGrab(Object3D obj)
    {
        obj.IsManipulationEnabled = true;
        obj.PointerPressed += (_, _) => BeginPhysicsGrab(obj);
        obj.PointerReleased += (_, _) => EndPhysicsGrab(obj);
    }

    private void BeginPhysicsGrab(Object3D obj)
    {
        if (_activeDemo != DemoSceneKind.Physics || obj.Rigidbody is null) return;
        _grabbedPhysicsObject = obj;
        obj.Rigidbody.Velocity = Vector3.Zero;
        obj.Rigidbody.AngularVelocity = Vector3.Zero;
        obj.Rigidbody.IsKinematic = true;
        obj.Rigidbody.WakeUp();
    }

    private void EndPhysicsGrab(Object3D obj)
    {
        if (_activeDemo != DemoSceneKind.Physics || obj.Rigidbody is null) return;
        obj.Rigidbody.IsKinematic = false;
        obj.Rigidbody.Velocity = Vector3.Zero;
        obj.Rigidbody.AngularVelocity = Vector3.Zero;
        obj.Rigidbody.WakeUp();
        if (ReferenceEquals(_grabbedPhysicsObject, obj)) _grabbedPhysicsObject = null;
    }

    private void BuildImportedGlbModelScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(4.3f, 2.45f, -5.4f);
        scene.Camera.Target = new Vector3(0f, 1.10f, 0f);
        scene.AmbientLightIntensity = 0.38f;
        AddGround(scene, 6.0f, 4.8f);

        scene.AddLight(new PointLight3D
        {
            Position = new Vector3(-1.7f, 2.8f, -2.1f),
            Range = 8f,
            Intensity = 2.7f,
            Color = new ColorRgba(0.72f, 0.84f, 1f, 1f)
        });

        var asset = LoadDoctorWatsonAsset();
        if (asset is null || asset.Diagnostics.HasErrors || asset.Meshes.Count == 0)
        {
            _doctorWatsonImportInfo = asset?.Diagnostics.ToSummary() ?? _doctorWatsonImportInfo;
            AddSelectable(scene, new Box3D
            {
                Name = "GLB import fallback: model asset was not loaded",
                Width = 1.1f,
                Height = 1.1f,
                Depth = 1.1f,
                Position = new Vector3(0f, 0.62f, 0f),
                Material = Material3D.CreatePhong(new ColorRgba(0.92f, 0.24f, 0.20f, 1f), 0.35f, 48f)
            });
            return;
        }

        // The rigmodels animated export is authored in centimetres. Keep the original
        // coordinate data for skinning, but scale the model at the imported-object root
        // so the character is approximately human-sized in the scene.
        const float doctorWatsonSceneScale = 0.008f;
        _doctorWatsonBaseY = asset.Bounds.IsValid ? -asset.Bounds.Min.Y * doctorWatsonSceneScale + 0.02f : 0.02f;
        _doctorWatsonModel = scene.ImportModel(asset, options =>
        {
            options.Name = "Doctor Watson / GLB imported model";
            options.Position = new Vector3(-0.45f, _doctorWatsonBaseY, 0f);
            options.RotationDegrees = new Vector3(0f, 180f, 0f);
            options.Scale = new Vector3(doctorWatsonSceneScale);
        });
        _doctorWatsonModel.IsPickable = true;
        _doctorWatsonModel.ModelClicked += (_, e) =>
        {
            _clickCount++;
            SetSelection(e.Part);
        };

        _selectableObjects.Add(_doctorWatsonModel);
        foreach (var part in _doctorWatsonModel.ModelParts)
        {
            part.IsPickable = true;
            _selectableObjects.Add(part);
        }

        var hasSkins = asset.Skins.Count > 0;
        var hasAnimations = asset.Animations.Count > 0;
        if (hasAnimations)
        {
            _doctorWatsonSequence = new ModelAnimationSequence3D(_doctorWatsonModel) { LoopSequence = true };
            foreach (var clip in asset.Animations)
            {
                _doctorWatsonSequence.Add(clip.Name, speed: 1f, loopClip: false);
            }
            _doctorWatsonSequence.PlayFromStart();
        }
        else
        {
            BuildDoctorWatsonAnimationProxy(scene);
        }

        _doctorWatsonImportInfo =
            $"Doctor Watson GLB loaded: {asset.Meshes.Count.ToString(CultureInfo.InvariantCulture)} meshes, " +
            $"{asset.PrimitiveCount.ToString(CultureInfo.InvariantCulture)} primitives, " +
            $"{asset.Textures.Count.ToString(CultureInfo.InvariantCulture)} embedded textures.\n" +
            $"Skins: {asset.Skins.Count.ToString(CultureInfo.InvariantCulture)} | animations: {asset.Animations.Count.ToString(CultureInfo.InvariantCulture)}. " +
            (hasSkins && hasAnimations
                ? "Rigged animation data is present. GPU skinning is active, so the imported mesh bends according to authored bone animation clips."
                : "This GLB has no skeleton/authored animation clips, so the demo uses a procedural proxy. For wave/jump/squat, provide GLB/FBX clips authored for this skeleton.") +
            "\n" + asset.Diagnostics.ToSummary();
    }

    private void BuildDoctorWatsonAnimationProxy(Scene3D scene)
    {
        var matBody = Material3D.CreateLambert(new ColorRgba(0.18f, 0.42f, 0.95f, 0.68f));
        matBody.Surface = SurfaceMode.Transparent;
        matBody.Opacity = 0.68f;
        var matJoint = Material3D.CreatePhong(new ColorRgba(1.0f, 0.78f, 0.30f, 0.80f), 0.35f, 40f);
        matJoint.Surface = SurfaceMode.Transparent;
        matJoint.Opacity = 0.80f;

        Object3D AddRig(Object3D obj)
        {
            obj.IsPickable = false;
            obj.IsManipulationEnabled = false;
            scene.Add(obj);
            _characterRigObjects.Add(obj);
            return obj;
        }

        AddRig(new Box3D
        {
            Name = "Procedural torso animation proxy",
            Width = 0.38f,
            Height = 0.88f,
            Depth = 0.24f,
            Position = new Vector3(0.72f, 1.05f, 0.02f),
            Material = matBody
        });
        AddRig(new Sphere3D
        {
            Name = "Procedural head animation proxy",
            Radius = 0.18f,
            Segments = 24,
            Rings = 12,
            Position = new Vector3(0.72f, 1.62f, 0.02f),
            Material = matJoint
        });
        AddRig(new Box3D
        {
            Name = "Procedural left arm",
            Width = 0.13f,
            Height = 0.72f,
            Depth = 0.13f,
            Position = new Vector3(0.43f, 1.08f, 0.03f),
            Material = matBody
        });
        AddRig(new Box3D
        {
            Name = "Procedural right arm / wave driver",
            Width = 0.13f,
            Height = 0.72f,
            Depth = 0.13f,
            Position = new Vector3(1.01f, 1.08f, 0.03f),
            Material = matBody
        });
        AddRig(new Box3D
        {
            Name = "Procedural left leg",
            Width = 0.15f,
            Height = 0.72f,
            Depth = 0.15f,
            Position = new Vector3(0.61f, 0.45f, 0.03f),
            Material = matBody
        });
        AddRig(new Box3D
        {
            Name = "Procedural right leg",
            Width = 0.15f,
            Height = 0.72f,
            Depth = 0.15f,
            Position = new Vector3(0.83f, 0.45f, 0.03f),
            Material = matBody
        });
    }

    private ModelAsset3D? LoadDoctorWatsonAsset()
    {
        if (_doctorWatsonAsset is not null)
        {
            return _doctorWatsonAsset;
        }

        var uri = new Uri("avares://Avalonia3D/Assets/Models/DoctorWatson.glb");
        try
        {
            using var stream = AssetLoader.Open(uri);
            _doctorWatsonAsset = GltfModelImporter.ImportStream(stream, uri.ToString(), new ModelImportOptions
            {
                MaxFileBytes = 16L * 1024L * 1024L,
                MaxBinaryChunkBytes = 32 * 1024 * 1024,
                AssetResolver = new AvaloniaResourceAssetResolver3D(),
                GenerateMissingNormals = true
            });
            return _doctorWatsonAsset;
        }
        catch (Exception ex)
        {
            _doctorWatsonImportInfo = "Doctor Watson GLB resource was not found or could not be opened: " + ex.Message;
            return null;
        }
    }

    private ModelAsset3D? LoadEarthAsset()
    {
        if (_earthAsset is not null)
        {
            return _earthAsset;
        }

        var uri = new Uri("avares://Avalonia3D/Assets/Models/EarthDetailed.glb");
        try
        {
            using var stream = AssetLoader.Open(uri);
            _earthAsset = GltfModelImporter.ImportStream(stream, uri.ToString(), new ModelImportOptions
            {
                MaxFileBytes = 16L * 1024L * 1024L,
                MaxBinaryChunkBytes = 32 * 1024 * 1024,
                AssetResolver = new AvaloniaResourceAssetResolver3D(),
                GenerateMissingNormals = true
            });
            return _earthAsset;
        }
        catch (Exception ex)
        {
            _doctorWatsonImportInfo = "Earth GLB resource was not found or could not be opened: " + ex.Message;
            return null;
        }
    }

    private static byte[]? TryReadAvaloniaResourceBytes(string uriText)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uriText));
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeEquirectangularTexture(byte[] textureData)
    {
        if (textureData.Length == 0) return false;
        try
        {
            using var stream = new MemoryStream(textureData, writable: false);
            using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            var width = bitmap.PixelSize.Width;
            var height = bitmap.PixelSize.Height;
            if (width < 512 || height < 256) return false;
            var ratio = width / (float)height;
            return MathF.Abs(ratio - 2f) <= 0.06f;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplySidecarBaseColorTextureIfCompatible(ImportedModel3D model, string textureKey, byte[] textureData, string mimeType)
    {
        if (!LooksLikeEquirectangularTexture(textureData)) return;

        _ = model.Children;
        foreach (var part in model.ModelParts)
        {
            if (part.Material.HasBaseColorTexture) continue;
            part.Material.SetBaseColorTexture(textureKey, textureData, mimeType);
        }
    }

    private static void ApplyFallbackEarthTextureIfCompatible(Material3D material, byte[]? textureData, string textureKey, string mimeType)
    {
        if (textureData is not { Length: > 0 } || !LooksLikeEquirectangularTexture(textureData)) return;
        material.SetBaseColorTexture(textureKey, textureData, mimeType);
    }

    private void BuildCameraArcPlanetScene(Scene3D scene)
    {
        scene.FrameInterpolator.Enabled = false;
        scene.Camera.Position = new Vector3(0.0f, 3.10f, -6.8f);
        scene.Camera.Target = new Vector3(0f, 1.38f, 0f);
        scene.Camera.FarPlane = 420f;
        scene.AmbientLightIntensity = 0.82f;
        scene.Environment.Skybox.Mode = SkyboxMode3D.StarField;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.001f, 0.003f, 0.014f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.006f, 0.010f, 0.026f, 1f);
        scene.Environment.Skybox.BottomColor = new ColorRgba(0.000f, 0.001f, 0.007f, 1f);
        scene.Environment.Skybox.Intensity = 1.55f;
        AddSpaceBackground(scene);

        var planetCenter = new Vector3(0f, 1.45f, 0f);
        const float targetRadius = 1.25f;

        // Invisible mathematical proxy used by camera flight, labels and markers.
        // The visible planet is the authored GLB below. Keeping this proxy separate
        // avoids reverting to the broken procedural UV sphere while preserving the
        // existing geospatial helper code.
        _planet = scene.Add(new Sphere3D
        {
            Name = "Earth coordinate proxy / not rendered",
            Radius = targetRadius,
            Segments = 32,
            Rings = 16,
            Position = planetCenter,
            IsVisible = false,
            IsPickable = false,
            IsManipulationEnabled = false
        });

        var asset = LoadEarthAsset();
        if (asset is not null && asset.Meshes.Count > 0 && !asset.Diagnostics.HasErrors)
        {
            var size = asset.Bounds.IsValid ? asset.Bounds.Size : new Vector3(2f);
            var maxSpan = MathF.Max(MathF.Max(MathF.Abs(size.X), MathF.Abs(size.Y)), MathF.Abs(size.Z));
            var scale = maxSpan > 0.0001f ? (targetRadius * 2f) / maxSpan : 1f;
            var centerOffset = asset.Bounds.IsValid ? asset.Bounds.Center * scale : Vector3.Zero;

            _earthModel = scene.ImportModel(asset, options =>
            {
                options.Name = "Earth / authored GLB model";
                options.Position = planetCenter - centerOffset;
                options.RotationDegrees = new Vector3(0f, -90f, 0f);
                options.Scale = new Vector3(scale);
            });
            _earthModel.IsPickable = true;
            _earthModel.IsManipulationEnabled = false;
            _earthModel.ModelClicked += (_, e) =>
            {
                SetSelection(e.Part);
                var local = e.WorldPosition - planetCenter;
                if (local.LengthSquared() > 0.0001f) SetPlanetMarkerFromNormal(Vector3.Normalize(local), showLabel: true);
            };
            // Preserve the authored GLB material and embedded texture. EarthDetailed.jpg is a
            // rendered sphere image, not an equirectangular 2:1 texture, so forcing it over
            // the GLB material corrupts the model's authored UV mapping.
            _selectableObjects.Add(_earthModel);
            foreach (var part in _earthModel.ModelParts)
            {
                part.IsPickable = true;
                _selectableObjects.Add(part);
            }
        }
        else
        {
            var earthMaterial = new Material3D
            {
                BaseColor = new ColorRgba(1f, 1f, 1f, 1f),
                Lighting = LightingMode.BlinnPhong,
                SpecularStrength = 0.14f,
                Shininess = 34f,
                Roughness = 0.72f,
                Surface = SurfaceMode.Opaque,
                Opacity = 1f
            };
            var earthTexture = TryReadAvaloniaResourceBytes("avares://Avalonia3D/Assets/Models/EarthEquirectangularFallback.png");
            ApplyFallbackEarthTextureIfCompatible(earthMaterial, earthTexture, "fallback-earth-equirectangular-texture", "image/png");
            var fallback = AddSelectable(scene, new Sphere3D
            {
                Name = "Earth / fallback UV sphere",
                Radius = targetRadius,
                Segments = 128,
                Rings = 64,
                Position = planetCenter,
                RotationDegrees = new Vector3(0f, -90f, 0f),
                Material = earthMaterial
            });
            fallback.Clicked += (_, e) =>
            {
                var local = e.WorldPosition - planetCenter;
                if (local.LengthSquared() <= 0.0001f) return;
                SetPlanetMarkerFromNormal(Vector3.Normalize(local), showLabel: true);
                SetSelection(_planetMarker);
            };
        }

        _planetMarker = AddSelectable(scene, new Sphere3D
        {
            Name = "Highlighted coordinate marker",
            Radius = 0.060f,
            Segments = 20,
            Rings = 10,
            Material = new Material3D { BaseColor = new ColorRgba(1f, 0.86f, 0.18f, 1f), Lighting = LightingMode.Unlit }
        });

        // No transparent blue/cloud shell here: it was the source of the visible blue
        // overlay and, depending on draw order, could also hide the planet. Use actual
        // lights and the authored material instead.
        scene.AddLight(new DirectionalLight3D { Direction = Vector3.Normalize(new Vector3(-0.46f, -0.62f, 0.18f)), Intensity = 1.08f, Color = new ColorRgba(1f, 0.95f, 0.86f, 1f) });
        scene.AddLight(new DirectionalLight3D { Direction = Vector3.Normalize(new Vector3(0.36f, -0.35f, -0.62f)), Intensity = 0.44f, Color = new ColorRgba(0.55f, 0.70f, 1f, 1f) });
        scene.AddLight(new PointLight3D { Position = new Vector3(-3.6f, 2.9f, -3.8f), Range = 12f, Intensity = 2.7f, Color = new ColorRgba(0.86f, 0.92f, 1f, 1f) });
        scene.AddLight(new PointLight3D { Position = new Vector3(3.8f, 1.9f, 3.0f), Range = 10f, Intensity = 1.25f, Color = new ColorRgba(0.34f, 0.56f, 1f, 1f) });

        _planetFocusPoint = PlanetPointFromLatLon(24.5f, 38.0f);
        UpdatePlanetFocusMarker(0f);
        ShowPlanetLocationLabel(24.5f, 38.0f, _planetFocusPoint);
    }


    private void AddSpaceBackground(Scene3D scene)
    {
        var starMat = new Material3D { BaseColor = new ColorRgba(0.86f, 0.92f, 1f, 1f), Lighting = LightingMode.Unlit };
        var warmStar = new Material3D { BaseColor = new ColorRgba(1f, 0.84f, 0.62f, 1f), Lighting = LightingMode.Unlit };
        var starCount = 240;
        for (var i = 0; i < starCount; i++)
        {
            var a = i * 12.9898f;
            var b = i * 78.233f;
            var x = (MathF.Sin(a) * 0.5f + 0.5f) * 2f - 1f;
            var y = (MathF.Sin(b) * 0.5f + 0.5f) * 1.4f - 0.15f;
            var z = -0.35f - (MathF.Cos(a * 0.73f) * 0.5f + 0.5f) * 0.65f;
            var dir = Vector3.Normalize(new Vector3(x, y, z));
            scene.Add(new Sphere3D
            {
                Name = "Background star " + i.ToString(CultureInfo.InvariantCulture),
                Radius = i % 11 == 0 ? 0.022f : 0.012f,
                Segments = 8,
                Rings = 4,
                Position = new Vector3(0f, 1.4f, 0f) + dir * (45f + (i % 17)),
                Material = i % 9 == 0 ? warmStar : starMat,
                IsPickable = false,
                IsManipulationEnabled = false
            });
        }
    }

    private void AddPlanetDetails(Scene3D scene)
    {
        AddSurfaceBlob(scene, "Northern continent", 42f, -32f, 0.23f, new ColorRgba(0.12f, 0.62f, 0.22f, 1f));
        AddSurfaceBlob(scene, "Eastern continent", 8f, 76f, 0.31f, new ColorRgba(0.16f, 0.68f, 0.26f, 1f));
        AddSurfaceBlob(scene, "Southern continent", -36f, 118f, 0.26f, new ColorRgba(0.22f, 0.58f, 0.20f, 1f));
        AddSurfaceBlob(scene, "Island chain A", -12f, -142f, 0.12f, new ColorRgba(0.20f, 0.66f, 0.28f, 1f));
        AddSurfaceBlob(scene, "Island chain B", 24f, 148f, 0.10f, new ColorRgba(0.18f, 0.72f, 0.30f, 1f));
        AddSurfaceBlob(scene, "Polar ice north", 78f, 0f, 0.18f, new ColorRgba(0.86f, 0.95f, 1f, 1f));
        AddSurfaceBlob(scene, "Polar ice south", -78f, 0f, 0.16f, new ColorRgba(0.86f, 0.95f, 1f, 1f));

        for (var i = 0; i < 18; i++)
        {
            var lon = -170f + i * 20f;
            AddSurfaceBlob(scene, "Cloud cell " + i.ToString(CultureInfo.InvariantCulture), 8f + MathF.Sin(i * 1.7f) * 26f, lon, 0.055f + (i % 3) * 0.018f, new ColorRgba(1f, 1f, 1f, 0.42f), transparent: true, radiusOffset: 0.115f);
        }
    }

    private void AddSurfaceBlob(Scene3D scene, string name, float lat, float lon, float radius, ColorRgba color, bool transparent = false, float radiusOffset = 0.055f)
    {
        if (_planet is null) return;
        var normal = PlanetPointFromLatLon(lat, lon);
        var material = new Material3D
        {
            BaseColor = color,
            Lighting = transparent ? LightingMode.Unlit : LightingMode.Lambert,
            Surface = transparent ? SurfaceMode.Transparent : SurfaceMode.Opaque,
            Opacity = color.A
        };
        var blob = scene.Add(new Sphere3D
        {
            Name = name,
            Radius = radius,
            Segments = 18,
            Rings = 9,
            Material = material,
            IsPickable = false
        });
        _planetSurfaceObjects.Add((blob, normal, _planet.Radius + radiusOffset));
    }

    private void BuildShaderLightingLabScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(6.6f, 4.1f, -7.4f);
        scene.Camera.Target = new Vector3(0f, 0.95f, 0f);
        scene.AmbientLightIntensity = 0.28f;
        scene.RenderPipeline.Mode = RenderPipelineMode3D.Forward;
        scene.RenderPipeline.EnableDeferredLighting = false;
        scene.RenderPipeline.EnableHdr = false;
        scene.RenderPipeline.EnableTransparentForwardPass = true;
        scene.RenderPipeline.Ssao.Enabled = false;
        scene.RenderPipeline.ToneMapping.Enabled = true;
        scene.RenderPipeline.ToneMapping.Mode = ToneMappingMode3D.AcesApproximation;
        scene.RenderPipeline.ToneMapping.Exposure = 1.05f;
        scene.RenderPipeline.ToneMapping.Gamma = 2.2f;
        AddGround(scene, 9.5f, 6.5f);

        _shaderSun = scene.AddLight(new DirectionalLight3D
        {
            Direction = Vector3.Normalize(new Vector3(-0.55f, -0.86f, -0.25f)),
            Intensity = 1.65f,
            Color = new ColorRgba(1f, 0.86f, 0.66f, 1f)
        });
        _shaderAccent = scene.AddLight(new PointLight3D
        {
            Position = new Vector3(2.2f, 1.7f, -1.4f),
            Range = 7f,
            Intensity = 2.2f,
            Color = new ColorRgba(0.36f, 0.60f, 1f, 1f)
        });

        _rotatingBox = AddSelectable(scene, new Box3D
        {
            Name = "Shader lab / metallic cube",
            Width = 1.05f,
            Height = 1.05f,
            Depth = 1.05f,
            Position = new Vector3(-1.8f, 0.62f, 0f),
            Material = new Material3D
            {
                BaseColor = new ColorRgba(0.82f, 0.84f, 0.92f, 1f),
                Lighting = LightingMode.BlinnPhong,
                Metallic = 0.75f,
                Roughness = 0.18f,
                SpecularStrength = 0.92f,
                Shininess = 128f
            }
        });
        _orbitSphere = AddSelectable(scene, new Sphere3D
        {
            Name = "Shader lab / rough sphere",
            Radius = 0.62f,
            Segments = 44,
            Rings = 22,
            Position = new Vector3(0.0f, 0.73f, -0.10f),
            Material = new Material3D
            {
                BaseColor = new ColorRgba(0.98f, 0.50f, 0.18f, 1f),
                Lighting = LightingMode.Phong,
                Metallic = 0.05f,
                Roughness = 0.76f,
                SpecularStrength = 0.38f,
                Shininess = 28f
            }
        });
        AddSelectable(scene, new Box3D
        {
            Name = "Shader lab / transparent color grading plate",
            Width = 1.7f,
            Height = 1.1f,
            Depth = 0.04f,
            Position = new Vector3(1.75f, 0.95f, 0.45f),
            RotationDegrees = new Vector3(0f, -20f, 0f),
            Material = new Material3D
            {
                BaseColor = new ColorRgba(0.42f, 0.92f, 1f, 0.28f),
                Opacity = 0.28f,
                Surface = SurfaceMode.Transparent,
                Lighting = LightingMode.Phong,
                SpecularStrength = 0.62f,
                Shininess = 84f
            }
        });
        AddAnimatedObjectGallery(scene, new Vector3(0f, 0.22f, 2.25f), 18, 12, 0.68f, 0.60f);
    }



    private void BuildBuildingWalkthroughScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(-4.4f, 1.56f, -7.2f);
        scene.Camera.Target = new Vector3(-3.8f, 1.46f, -5.7f);
        scene.Camera.FieldOfViewDegrees = 68f;
        scene.Camera.FarPlane = 220f;
        scene.AmbientLightIntensity = 0.50f;
        scene.Environment.Skybox.Mode = SkyboxMode3D.VerticalGradient;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.12f, 0.17f, 0.25f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.30f, 0.36f, 0.42f, 1f);
        scene.Environment.Skybox.BottomColor = new ColorRgba(0.045f, 0.055f, 0.065f, 1f);

        scene.SetPhysicsEnabled(false);
        scene.AddLight(new DirectionalLight3D { Direction = Vector3.Normalize(new Vector3(-0.40f, -0.76f, -0.30f)), Intensity = 1.15f, Color = new ColorRgba(1f, 0.94f, 0.84f, 1f) });
        scene.AddLight(new PointLight3D { Position = new Vector3(-2.6f, 4.2f, -3.4f), Range = 18f, Intensity = 2.0f, Color = new ColorRgba(0.70f, 0.84f, 1f, 1f) });
        scene.AddLight(new PointLight3D { Position = new Vector3(3.8f, 3.0f, 1.8f), Range = 14f, Intensity = 1.5f, Color = new ColorRgba(1f, 0.78f, 0.55f, 1f) });

        var concrete = Material3D.CreateLambert(new ColorRgba(0.34f, 0.37f, 0.39f, 1f));
        var floorMat = Material3D.CreateLambert(new ColorRgba(0.26f, 0.29f, 0.32f, 1f));
        var wallMat = Material3D.CreateLambert(new ColorRgba(0.72f, 0.74f, 0.70f, 1f));
        var glass = new Material3D { BaseColor = new ColorRgba(0.42f, 0.72f, 1f, 0.22f), Opacity = 0.22f, Surface = SurfaceMode.Transparent, Lighting = LightingMode.Phong, SpecularStrength = 0.55f, Shininess = 80f };
        var wood = Material3D.CreateLambert(new ColorRgba(0.55f, 0.36f, 0.20f, 1f));
        var accent = Material3D.CreatePhong(new ColorRgba(0.16f, 0.48f, 0.92f, 1f), 0.30f, 46f);

        StaticBox(scene, "Open plaza walkable slab", 28f, 0.20f, 24f, new Vector3(0f, -0.10f, 0f), concrete, pickable: false);
        var cityBlockCount = 18;
        for (var i = 0; i < cityBlockCount; i++)
        {
            var x = -18f + (i % 13) * 2.8f;
            var z = 10.5f + (i / 13) * 2.2f;
            StaticBox(scene, "Background city block " + i.ToString(CultureInfo.InvariantCulture), 1.2f, 1.8f + (i % 5) * 0.7f, 1.1f, new Vector3(x, 0.9f + (i % 5) * 0.35f, z), Material3D.CreateLambert(new ColorRgba(0.12f, 0.16f, 0.20f, 1f)), pickable: false);
        }

        const int floors = 4;
        const float floorHeight = 2.25f;
        const float width = 10.4f;
        const float depth = 8.0f;
        StaticBox(scene, "Building foundation / walkable", width + 1.0f, 0.22f, depth + 1.0f, new Vector3(0f, 0.02f, 0f), floorMat, pickable: false);
        for (var f = 0; f < floors; f++)
        {
            var y = 0.18f + f * floorHeight;
            StaticBox(scene, $"Floor {f + 1} slab", width, 0.14f, depth, new Vector3(0f, y, 0f), floorMat, pickable: false);
            StaticBox(scene, $"Floor {f + 1} ceiling", width, 0.10f, depth, new Vector3(0f, y + floorHeight - 0.08f, 0f), floorMat, pickable: false);
            StaticBox(scene, $"Floor {f + 1} north wall", width, 1.80f, 0.14f, new Vector3(0f, y + 0.95f, depth * 0.5f), wallMat, pickable: false);
            if (f == 0)
            {
                StaticBox(scene, "Entrance facade left", 3.6f, 1.80f, 0.14f, new Vector3(-3.4f, y + 0.95f, -depth * 0.5f), wallMat, pickable: false);
                StaticBox(scene, "Entrance facade right", 3.6f, 1.80f, 0.14f, new Vector3(3.4f, y + 0.95f, -depth * 0.5f), wallMat, pickable: false);
                StaticBox(scene, "Entrance header", 2.7f, 0.22f, 0.16f, new Vector3(0f, y + 1.92f, -depth * 0.5f), wallMat, pickable: false);
                scene.Add(new Box3D
                {
                    Name = "Flush entrance guide stripe / visual only",
                    Width = 2.3f,
                    Height = 0.012f,
                    Depth = 2.2f,
                    Position = new Vector3(0f, y + 0.095f, -depth * 0.5f - 0.95f),
                    Material = accent,
                    IsPickable = false,
                    IsManipulationEnabled = false,
                    Collider = null
                });
            }
            else
            {
                StaticBox(scene, $"Floor {f + 1} south wall", width, 1.80f, 0.14f, new Vector3(0f, y + 0.95f, -depth * 0.5f), wallMat, pickable: false);
            }
            StaticBox(scene, $"Floor {f + 1} west wall", 0.14f, 1.80f, depth, new Vector3(-width * 0.5f, y + 0.95f, 0f), wallMat, pickable: false);
            StaticBox(scene, $"Floor {f + 1} east glass facade", 0.10f, 1.72f, depth, new Vector3(width * 0.5f, y + 0.98f, 0f), glass, pickable: false);

            for (var room = 0; room < 3; room++)
            {
                var x = -3.2f + room * 3.2f;
                StaticBox(scene, $"Floor {f + 1} office partition {room + 1}", 0.08f, 1.42f, 2.55f, new Vector3(x + 1.20f, y + 0.78f, 1.25f), wallMat, pickable: false);
                StaticBox(scene, $"Floor {f + 1} office desk {room + 1}", 0.92f, 0.22f, 0.52f, new Vector3(x, y + 0.52f, 2.80f), wood, pickable: true);
            }
        }
        StaticBox(scene, "Reception counter / moved away from entrance", 1.8f, 0.70f, 0.48f, new Vector3(2.8f, 0.55f, -2.70f), accent, pickable: true);
        StaticBox(scene, "Server display wall", 0.44f, 1.55f, 1.20f, new Vector3(4.25f, 0.93f, 2.65f), Material3D.CreatePhong(new ColorRgba(0.05f, 0.08f, 0.12f, 1f), 0.25f, 42f), pickable: true);
        ResetBuildingPersonCamera();
    }


    private void ResetBuildingPersonCamera()
    {
        var scene = _sceneControl.Scene;
        scene.Camera.Position = new Vector3(-4.4f, 1.56f, -7.2f);
        scene.Camera.Target = new Vector3(-3.8f, 1.46f, -5.7f);
        scene.Camera.FieldOfViewDegrees = 68f;
        _sceneControl.ResetPersonNavigationState(grounded: true);
        _cameraFlight.Cancel();
    }

    private void BuildBridgeDigitalTwinScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(26.0f, 12.0f, -30.0f);
        scene.Camera.Target = new Vector3(0f, 3.0f, 0f);
        scene.Camera.FarPlane = 520f;
        scene.AmbientLightIntensity = 0.46f;
        scene.Environment.Skybox.Mode = SkyboxMode3D.VerticalGradient;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.05f, 0.09f, 0.15f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.18f, 0.25f, 0.32f, 1f);
        scene.Environment.Skybox.BottomColor = new ColorRgba(0.02f, 0.03f, 0.04f, 1f);
        scene.AddLight(new DirectionalLight3D { Direction = Vector3.Normalize(new Vector3(-0.36f, -0.80f, -0.24f)), Intensity = 1.15f, Color = new ColorRgba(1f, 0.92f, 0.80f, 1f) });
        scene.AddLight(new PointLight3D { Position = new Vector3(0f, 10.0f, -8.0f), Range = 52f, Intensity = 2.4f, Color = new ColorRgba(0.54f, 0.75f, 1f, 1f) });

        var water = new Material3D { BaseColor = new ColorRgba(0.03f, 0.18f, 0.30f, 0.76f), Opacity = 0.76f, Surface = SurfaceMode.Transparent, Lighting = LightingMode.Phong, SpecularStrength = 0.65f, Shininess = 110f };
        var concrete = Material3D.CreateLambert(new ColorRgba(0.31f, 0.34f, 0.34f, 1f));
        var deck = Material3D.CreateLambert(new ColorRgba(0.20f, 0.21f, 0.22f, 1f));
        var steel = Material3D.CreatePhong(new ColorRgba(0.52f, 0.56f, 0.60f, 1f), 0.44f, 68f);
        var darkSteel = Material3D.CreatePhong(new ColorRgba(0.22f, 0.25f, 0.28f, 1f), 0.30f, 50f);
        var road = Material3D.CreateLambert(new ColorRgba(0.075f, 0.080f, 0.085f, 1f));
        StaticBox(scene, "Wide river surface", 70f, 0.04f, 34f, new Vector3(0f, -0.04f, 0f), water, pickable: false);
        StaticBox(scene, "West embankment", 22f, 0.36f, 34f, new Vector3(-40f, 0.12f, 0f), concrete, pickable: false);
        StaticBox(scene, "East embankment", 22f, 0.36f, 34f, new Vector3(40f, 0.12f, 0f), concrete, pickable: false);

        for (var i = -7; i <= 7; i++)
        {
            var x = i * 4.2f;
            StaticBox(scene, "Deck segment " + i.ToString(CultureInfo.InvariantCulture), 4.0f, 0.30f, 5.2f, new Vector3(x, 1.15f, 0f), deck, pickable: true);
            StaticBox(scene, "Asphalt lane " + i.ToString(CultureInfo.InvariantCulture), 3.8f, 0.035f, 4.3f, new Vector3(x, 1.33f, 0f), road, pickable: false);
            StaticBox(scene, "Left guard rail " + i.ToString(CultureInfo.InvariantCulture), 4.0f, 0.18f, 0.12f, new Vector3(x, 1.62f, -2.72f), darkSteel, pickable: false);
            StaticBox(scene, "Right guard rail " + i.ToString(CultureInfo.InvariantCulture), 4.0f, 0.18f, 0.12f, new Vector3(x, 1.62f, 2.72f), darkSteel, pickable: false);
        }
        for (var side = -1; side <= 1; side += 2)
        {
            for (var z = -1; z <= 1; z += 2)
            {
                StaticBox(scene, "Main pylon " + side.ToString(CultureInfo.InvariantCulture) + "/" + z.ToString(CultureInfo.InvariantCulture), 0.85f, 8.5f, 0.85f, new Vector3(side * 9.2f, 5.35f, z * 3.2f), steel, pickable: true);
                StaticBox(scene, "Pylon cap " + side.ToString(CultureInfo.InvariantCulture) + "/" + z.ToString(CultureInfo.InvariantCulture), 1.35f, 0.42f, 1.35f, new Vector3(side * 9.2f, 9.85f, z * 3.2f), steel, pickable: false);
            }
            StaticBox(scene, "Pylon crossbeam " + side.ToString(CultureInfo.InvariantCulture), 1.15f, 0.42f, 7.5f, new Vector3(side * 9.2f, 8.95f, 0f), steel, pickable: true);
            StaticBox(scene, "Lift machinery house " + side.ToString(CultureInfo.InvariantCulture), 2.2f, 1.1f, 2.0f, new Vector3(side * 12.4f, 2.35f, -3.7f), darkSteel, pickable: true);
            for (var c = 0; c < 8; c++)
            {
                var z = -2.7f + c * 0.77f;
                StaticBox(scene, "Suspender cable " + side.ToString(CultureInfo.InvariantCulture) + "/" + c.ToString(CultureInfo.InvariantCulture), 0.065f, 6.0f - c * 0.18f, 0.065f, new Vector3(side * (2.4f + c * 0.75f), 5.0f - c * 0.05f, z), darkSteel, new Vector3(0f, 0f, side * (18f + c * 2f)), pickable: false);
            }
        }
        var vehicleCount = 36;
        for (var i = 0; i < vehicleCount; i++)
        {
            var lane = i % 4;
            var slot = (i / 4) % 32;
            var x = -29f + slot * 1.87f + (lane & 1) * 0.32f;
            var z = -1.55f + lane * 1.02f;
            StaticBox(scene, "Traffic vehicle " + (i + 1).ToString(CultureInfo.InvariantCulture), 0.78f, 0.42f, 0.48f, new Vector3(x, 1.63f, z), Material3D.CreatePhong(ColorFromHue(0.02f + i * 0.08f), 0.30f, 46f), pickable: true);
        }

        AddBridgeSensor(scene, "S-01 West hinge torque", new Vector3(-9.4f, 1.80f, -3.05f), "torque 41 kNm", "Main west hinge", "Hydraulic torque is within normal opening envelope.");
        AddBridgeSensor(scene, "S-02 East hinge torque", new Vector3(9.4f, 1.80f, 3.05f), "torque 39 kNm", "Main east hinge", "Paired leaf follows target angle with 0.4° deviation.");
        AddBridgeSensor(scene, "S-03 Left leaf strain", new Vector3(-3.2f, 1.72f, -2.45f), "strain 214 με", "Deck strain gauge", "Peak stress under simulated traffic lane A.");
        AddBridgeSensor(scene, "S-04 Right leaf strain", new Vector3(3.2f, 1.72f, 2.45f), "strain 198 με", "Deck strain gauge", "Load distribution is balanced after latest calibration.");
        AddBridgeSensor(scene, "S-05 West pylon vibration", new Vector3(-9.2f, 7.7f, -3.2f), "vibration 0.12 g", "Pylon accelerometer", "No resonance signature detected.");
        AddBridgeSensor(scene, "S-06 East pylon vibration", new Vector3(9.2f, 7.7f, 3.2f), "vibration 0.10 g", "Pylon accelerometer", "Slight wind-induced vibration, below alert level.");
        AddBridgeSensor(scene, "S-07 Wind mast", new Vector3(0f, 9.7f, 0f), "wind 7.4 m/s", "Anemometer", "Crosswind is safe for bridge opening sequence.");
        AddBridgeSensor(scene, "S-08 Bearing temperature", new Vector3(-11.2f, 1.65f, 3.0f), "temp 48°C", "Bearing thermal sensor", "Temperature is elevated but inside warning band.");
        AddBridgeSensor(scene, "S-09 Water level", new Vector3(0f, 0.40f, 7.8f), "level +0.8 m", "River gauge", "Navigation clearance remains sufficient.");
        AddBridgeSensor(scene, "S-10 Control cabinet", new Vector3(12.8f, 2.95f, -3.8f), "PLC online", "Control electronics", "All actuator feedback channels are responding.");
    }


    private Object3D StaticBox(Scene3D scene, string name, float width, float height, float depth, Vector3 position, Material3D material, Vector3? rotationDegrees = null, bool pickable = false)
    {
        var box = scene.Add(new Box3D
        {
            Name = name,
            Width = width,
            Height = height,
            Depth = depth,
            Position = position,
            RotationDegrees = rotationDegrees ?? Vector3.Zero,
            Material = material,
            IsPickable = pickable,
            IsManipulationEnabled = false
        });
        if (pickable) _selectableObjects.Add(box);
        return box;
    }

    private void AddBridgeSensor(Scene3D scene, string name, Vector3 position, string telemetry, string title, string details)
    {
        var sensor = scene.Add(new Sphere3D
        {
            Name = name + " / " + telemetry,
            Radius = 0.145f,
            Segments = 24,
            Rings = 12,
            Position = position,
            Material = new Material3D { BaseColor = new ColorRgba(0.16f, 1.0f, 0.42f, 1f), Lighting = LightingMode.Unlit },
            IsPickable = true,
            IsManipulationEnabled = false,
            DataContext = title + "\n" + telemetry + "\n" + details
        });
        _bridgeSensors.Add(sensor);
        _selectableObjects.Add(sensor);
    }


    private void HighlightBridgeSensor(Object3D sensor)
    {
        foreach (var item in _bridgeSensors)
        {
            item.Material = new Material3D
            {
                BaseColor = ReferenceEquals(item, sensor) ? new ColorRgba(1f, 0.22f, 0.12f, 1f) : new ColorRgba(0.16f, 1.0f, 0.42f, 1f),
                Lighting = LightingMode.Unlit
            };
        }
        ShowBridgeSensorPanel(sensor);
        _statusText.Text = "Bridge sensor selected:\n" + sensor.Name;
    }

    private void ShowBridgeSensorPanel(Object3D sensor)
    {
        var scene = _sceneControl.Scene;
        if (_bridgeSensorPanel is not null)
        {
            scene.Remove(_bridgeSensorPanel);
            _bridgeSensorPanel = null;
        }

        var lines = (sensor.DataContext as string ?? sensor.Name).Split('\n');
        var title = lines.Length > 0 ? lines[0] : sensor.Name;
        var value = lines.Length > 1 ? lines[1] : "value unavailable";
        var details = lines.Length > 2 ? lines[2] : "No diagnostics text.";
        var panel = new Border
        {
            Width = 520,
            Height = 220,
            Background = new SolidColorBrush(Color.FromArgb(238, 9, 13, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(86, 190, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 12),
            Child = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 21 },
                    new TextBlock { Text = value, Foreground = new SolidColorBrush(Color.FromRgb(112, 235, 166)), FontSize = 19, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = details, Foreground = new SolidColorBrush(Color.FromRgb(196, 210, 228)), FontSize = 14, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        _bridgeSensorPanel = scene.Add(new ControlPlane3D(panel)
        {
            Name = "Bridge sensor info panel / " + title,
            Width = 2.4f,
            Height = 1.02f,
            Position = sensor.Position + new Vector3(0.0f, 0.82f, 0.0f),
            AlwaysFaceCamera = true,
            RenderScale = 2.0d,
            IsPickable = false,
            IsManipulationEnabled = false
        });
        _bridgeSensorPanel.FaceCamera(scene.Camera);
    }


    private void RandomizeBridgeSensorAlert()
    {
        if (_bridgeSensors.Count == 0) return;
        var sensor = _bridgeSensors[_random.Next(_bridgeSensors.Count)];
        HighlightBridgeSensor(sensor);
        SetSelection(sensor);
    }

    private void BuildPipelineAndDiagnosticsScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(7.0f, 4.6f, -8.0f);
        scene.Camera.Target = new Vector3(0f, 0.95f, 0f);
        AddGround(scene, 10f, 7f);
        scene.RenderPipeline.Mode = RenderPipelineMode3D.Forward;
        scene.RenderPipeline.EnableDeferredLighting = false;
        scene.RenderPipeline.EnableHdr = false;
        scene.RenderPipeline.EnableTransparentForwardPass = true;
        scene.RenderPipeline.EnableMotionVectorMetadata = false;
        scene.RenderPipeline.Ssao.Enabled = false;
        scene.RenderPipeline.ToneMapping.Enabled = true;
        scene.RenderPipeline.ToneMapping.Mode = ToneMappingMode3D.AcesApproximation;
        scene.RenderPipeline.ToneMapping.Exposure = 1.05f;
        scene.RenderPipeline.ToneMapping.Gamma = 2.2f;

        _rotatingBox = AddSelectable(scene, new Box3D
        {
            Name = "Pipeline sample cube",
            Width = 1.1f,
            Height = 1.1f,
            Depth = 1.1f,
            Position = new Vector3(-1.6f, 0.65f, 0f),
            Material = Material3D.CreatePhong(new ColorRgba(0.30f, 0.66f, 1f, 1f), 0.70f, 92f)
        });
        AddSelectable(scene, new Sphere3D
        {
            Name = "Forward tone-mapping sample sphere",
            Radius = 0.65f,
            Segments = 42,
            Rings = 20,
            Position = new Vector3(0.2f, 0.76f, -0.15f),
            Material = new Material3D
            {
                BaseColor = new ColorRgba(1f, 0.62f, 0.26f, 1f),
                Lighting = LightingMode.BlinnPhong,
                SpecularStrength = 0.85f,
                Shininess = 120f,
                Metallic = 0.25f,
                Roughness = 0.22f
            }
        });
        AddSelectable(scene, new Box3D
        {
            Name = "Transparent forward pass panel",
            Width = 2.0f,
            Height = 1.2f,
            Depth = 0.04f,
            Position = new Vector3(1.75f, 1.0f, 0.45f),
            RotationDegrees = new Vector3(0f, -18f, 0f),
            Material = new Material3D
            {
                BaseColor = new ColorRgba(0.40f, 0.85f, 1f, 0.30f),
                Opacity = 0.30f,
                Surface = SurfaceMode.Transparent,
                Lighting = LightingMode.Phong,
                SpecularStrength = 0.7f,
                Shininess = 96f
            }
        });

        AddAnimatedObjectGallery(scene, new Vector3(0f, 0.24f, 2.30f), 18, 12, 0.70f, 0.62f);
    }

    private void BuildCrossPlatformStressLabScene(Scene3D scene)
    {
        var parameters = _stressParameters;
        var columns = parameters.Columns;
        var rows = parameters.Rows;
        var instanceCount = checked(columns * rows);

        scene.Camera.Position = new Vector3(columns * 0.42f, 16f, -rows * 0.48f);
        scene.Camera.Target = new Vector3(0f, 1.2f, 0f);
        scene.Camera.FarPlane = 900f;
        scene.AmbientLightIntensity = 0.34f;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.025f, 0.055f, 0.13f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.12f, 0.20f, 0.30f, 1f);
        AddGround(scene, columns * 0.92f, rows * 0.92f);
        scene.AddLight(new PointLight3D
        {
            Position = new Vector3(-8f, 9f, -8f),
            Range = 80f,
            Intensity = 3.2f,
            Color = new ColorRgba(0.36f, 0.66f, 1f, 1f)
        });
        scene.AddLight(new PointLight3D
        {
            Position = new Vector3(10f, 6f, 9f),
            Range = 72f,
            Intensity = 2.8f,
            Color = new ColorRgba(1f, 0.38f, 0.20f, 1f)
        });

        scene.RenderPipeline.ToneMapping.Enabled = true;
        scene.RenderPipeline.ToneMapping.Mode = ToneMappingMode3D.AcesApproximation;
        scene.RenderPipeline.ToneMapping.Exposure = 1.08f;
        scene.Performance.EnableHighScaleAggregateLayerBatches = true;
        scene.Performance.HighScaleAggregateLayerInstanceThreshold = 10000;
        scene.Performance.EnableBakedHighScaleDetailedMeshes = false;
        scene.Performance.EnableWebGlClientGpuTransformAnimation = true;
        scene.Performance.WebGlClientGpuTransformAnimationAmplitude = 0.14f;
        scene.Performance.MaxVisibleHighScaleChunks = 0;
        scene.Performance.MaxHighScaleVisibleInstances = 0;
        scene.Performance.EnableDistanceFade = false;

        var template = HighScaleTemplateCompiler.Compile(7001, new DemoStressNode3D(), false);
        template.AddMaterialVariant(1, "Cool").DefaultColor = new ColorRgba(0.18f, 0.62f, 1f, 1f);
        template.AddMaterialVariant(2, "Hot").DefaultColor = new ColorRgba(1f, 0.30f, 0.12f, 1f);
        template.AddMaterialVariant(3, "Telemetry alert").DefaultColor = new ColorRgba(1f, 0.88f, 0.18f, 1f);
        var layer = new HighScaleInstanceLayer3D(template, instanceCount, 8f)
        {
            Name = $"Cross-platform retained stress field / {instanceCount:n0} instances"
        };
        layer.LodPolicy.DetailedDistance = 80f;
        layer.LodPolicy.SimplifiedDistance = 180f;
        layer.LodPolicy.ProxyDistance = 360f;
        layer.LodPolicy.DrawDistance = 760f;
        layer.LodPolicy.FadeDistance = 0f;
        layer.LodPolicy.EnableBillboardFallback = false;
        layer.AddInstances(CreateStressFieldTransforms(columns, rows, 0.82f));
        scene.Add(layer);
        _stressLayer = layer;
        BurstStressTelemetry();
        EngineLog3D.Information(
            "Demo.Stress",
            $"Built parameterized stress workload: logicalNodes={instanceCount:n0}, " +
            $"compositePartInstances={instanceCount * template.Parts.Count:n0}, field={columns}x{rows}, " +
            $"transparent={parameters.TransparentObjects:n0}, particles={parameters.ParticleCapacityA:n0}+{parameters.ParticleCapacityB:n0}, " +
            $"telemetry={parameters.TelemetryUpdatesPerSecond:n0}/s, backendCounts=identical.");

        var transparentCount = parameters.TransparentObjects;
        for (var i = 0; i < transparentCount; i++)
        {
            var angle = i * (MathF.PI * 2f / transparentCount);
            var ring = 7.5f + (i % 5) * 0.45f;
            var origin = new Vector3(MathF.Cos(angle) * ring, 1.1f + (i % 4) * 0.34f, MathF.Sin(angle) * ring);
            var panel = scene.Add(new Box3D
            {
                Name = "Transparent sorting stress panel " + (i + 1).ToString(CultureInfo.InvariantCulture),
                Width = 0.46f,
                Height = 0.72f,
                Depth = 0.035f,
                Position = origin,
                RotationDegrees = new Vector3(0f, -angle * 180f / MathF.PI, 0f),
                Material = new Material3D
                {
                    BaseColor = WithAlpha(ColorFromHue(i * 0.071f), 0.30f),
                    Opacity = 0.30f,
                    Surface = SurfaceMode.Transparent,
                    Lighting = LightingMode.Phong,
                    SpecularStrength = 0.52f,
                    Shininess = 72f
                },
                IsPickable = false,
                IsManipulationEnabled = false
            });
            _stressAnimatedObjects.Add((panel, origin, i * 0.37f));
        }

        AddParticleSystem(scene,
            "Stress plasma stream A",
            new Vector3(-4.2f, 0.35f, -3.2f),
            new ParticleSystemSettings3D
            {
                Capacity = parameters.ParticleCapacityA,
                EmissionRatePerSecond = parameters.ParticleEmissionA,
                ParticleLifetimeSeconds = 3.0f,
                StartSize = 0.075f,
                EndSize = 0.012f,
                StartColor = new ColorRgba(0.22f, 0.72f, 1f, 0.92f),
                EndColor = new ColorRgba(0.08f, 0.16f, 0.8f, 0f),
                InitialSpeed = 2.5f,
                Spread = 1.1f,
                Prewarm = true,
                RenderMode = ParticleRenderMode3D.CameraFacingQuad
            },
            new ParticleEmitter3D(8101) { Direction = new Vector3(0.35f, 1f, 0.2f), Gravity = new Vector3(0f, -0.62f, 0f) });

        AddParticleSystem(scene,
            "Stress cube debris B",
            new Vector3(4.2f, 0.42f, 3.2f),
            new ParticleSystemSettings3D
            {
                Capacity = parameters.ParticleCapacityB,
                EmissionRatePerSecond = parameters.ParticleEmissionB,
                ParticleLifetimeSeconds = 3.4f,
                StartSize = 0.09f,
                EndSize = 0.025f,
                StartColor = new ColorRgba(1f, 0.34f, 0.12f, 0.95f),
                EndColor = new ColorRgba(0.55f, 0.05f, 0.02f, 0f),
                InitialSpeed = 1.8f,
                Spread = 1.4f,
                Prewarm = true,
                RenderMode = ParticleRenderMode3D.Cube3D
            },
            new ParticleEmitter3D(8102) { Direction = new Vector3(-0.25f, 1f, -0.2f), Gravity = new Vector3(0f, -0.48f, 0f) });
    }

    private void AddAnimatedObjectGallery(Scene3D scene, Vector3 center, int count, int columns, float spacingX, float spacingZ)
    {
        var rows = (count + columns - 1) / columns;
        var materials = new Material3D[8];
        for (var i = 0; i < materials.Length; i++)
        {
            materials[i] = Material3D.CreatePhong(ColorFromHue(i / (float)materials.Length), 0.36f + (i % 3) * 0.12f, 42f + i * 7f);
        }

        for (var i = 0; i < count; i++)
        {
            var x = (i % columns - (columns - 1) * 0.5f) * spacingX;
            var z = (i / columns - (rows - 1) * 0.5f) * spacingZ;
            var origin = center + new Vector3(x, (i % 4) * 0.12f, z);
            Object3D obj = (i % 3) switch
            {
                0 => new Box3D { Width = 0.34f, Height = 0.34f, Depth = 0.34f },
                1 => new Sphere3D { Radius = 0.20f, Segments = 20, Rings = 10 },
                _ => new Cylinder3D { Radius = 0.16f, Height = 0.42f, Segments = 18 }
            };
            obj.Name = "Dense gallery object " + (i + 1).ToString(CultureInfo.InvariantCulture);
            obj.Position = origin;
            obj.Material = materials[i % materials.Length];
            obj.IsPickable = false;
            obj.IsManipulationEnabled = false;
            scene.Add(obj);
            _stressAnimatedObjects.Add((obj, origin, i * 0.41f));
        }
    }

    private static IEnumerable<Matrix4x4> CreateStressFieldTransforms(int columns, int rows, float spacing)
    {
        var offsetX = (columns - 1) * spacing * 0.5f;
        var offsetZ = (rows - 1) * spacing * 0.5f;
        for (var z = 0; z < rows; z++)
        {
            for (var x = 0; x < columns; x++)
            {
                var wave = MathF.Sin(x * 0.31f) * 0.22f + MathF.Cos(z * 0.27f) * 0.18f;
                var rotation = ((x * 17 + z * 11) % 360) * MathF.PI / 180f;
                yield return Matrix4x4.CreateRotationY(rotation) *
                             Matrix4x4.CreateTranslation(x * spacing - offsetX, 0.58f + wave, z * spacing - offsetZ);
            }
        }
    }

    private T AddSelectable<T>(Scene3D scene, T obj) where T : Object3D
    {
        obj.IsPickable = true;
        scene.Add(obj);
        _selectableObjects.Add(obj);
        return obj;
    }

    private static void AddGround(Scene3D scene, float width, float depth)
    {
        scene.Add(new Box3D
        {
            Name = "Ground plane / static reference",
            Width = width,
            Height = 0.08f,
            Depth = depth,
            Position = new Vector3(0f, -0.06f, 0f),
            Material = new Material3D
            {
                BaseColor = new ColorRgba(0.16f, 0.18f, 0.22f, 1f),
                Lighting = LightingMode.Lambert,
                Roughness = 0.92f,
                NormalMapStrength = 0.28f
            },
            IsPickable = false,
            IsManipulationEnabled = false
        });
    }

    private void ApplyRuntimeToggles()
    {
        var scene = _sceneControl.Scene;
        _sceneControl.ShowPerformanceMetrics = _metricsCheck.IsChecked == true;
        scene.Debug.ShowWireframeOverlay = _wireframeCheck.IsChecked == true;
        UpdateStatus(force: true);
    }

    private void UpdatePrimaryActionCaption()
    {
        _primaryActionButton.Content = _activeDemo switch
        {
            DemoSceneKind.HighScaleDigitalTwin => "Randomize rack telemetry",
            DemoSceneKind.Physics => "Drop physics bodies",
            DemoSceneKind.ImportedGlbModel => "Next character animation",
            DemoSceneKind.CameraArcPlanetFocus => "Fly to lat/lon",
            DemoSceneKind.ShaderLightingLab => "Apply shader settings",
            DemoSceneKind.BuildingWalkthrough => "Reset person camera",
            DemoSceneKind.BridgeDigitalTwin => "Random bridge sensor alert",
            DemoSceneKind.EmbeddedAvaloniaControls => "Press embedded UI programmatically",
            DemoSceneKind.PickingAndInteraction => "Select next object",
            DemoSceneKind.RenderPipelineAndDiagnostics => "Toggle wireframe",
            DemoSceneKind.CrossPlatformStressLab => "Burst-update all telemetry",
            _ => "Reset camera"
        };
    }

    private void RunPrimaryAction()
    {
        switch (_activeDemo)
        {
            case DemoSceneKind.HighScaleDigitalTwin:
                RandomizeRackTelemetry();
                break;
            case DemoSceneKind.Physics:
                ResetPhysicsCube();
                break;
            case DemoSceneKind.ImportedGlbModel:
                AdvanceCharacterAnimationPhase();
                break;
            case DemoSceneKind.CameraArcPlanetFocus:
                StartPlanetCameraFlight();
                break;
            case DemoSceneKind.ShaderLightingLab:
                ApplyShaderLabValues();
                break;
            case DemoSceneKind.BuildingWalkthrough:
                ResetBuildingPersonCamera();
                break;
            case DemoSceneKind.BridgeDigitalTwin:
                RandomizeBridgeSensorAlert();
                break;
            case DemoSceneKind.EmbeddedAvaloniaControls:
                _embeddedCounter++;
                if (_rotatingBox is not null)
                {
                    _rotatingBox.Material.BaseColor = ColorFromHue((_embeddedCounter % 6) / 6f);
                }
                break;
            case DemoSceneKind.PickingAndInteraction:
                SelectNextObject();
                break;
            case DemoSceneKind.RenderPipelineAndDiagnostics:
                _wireframeCheck.IsChecked = _wireframeCheck.IsChecked != true;
                ApplyRuntimeToggles();
                break;
            case DemoSceneKind.CrossPlatformStressLab:
                BurstStressTelemetry();
                break;
            default:
                ResetCameraForCurrentDemo();
                break;
        }
        UpdateStatus(force: true);
    }

    private void ResetCameraForCurrentDemo()
    {
        var current = _activeDemo;
        LoadDemo(current);
    }

    private void SelectNextObject()
    {
        if (_selectableObjects.Count == 0)
        {
            SetSelection(null);
            return;
        }

        var currentIndex = _selectedObject is null ? -1 : _selectableObjects.IndexOf(_selectedObject);
        var next = (currentIndex + 1) % _selectableObjects.Count;
        SetSelection(_selectableObjects[next]);
    }

    private void OnSceneFixedUpdate(Scene3D scene, in SceneFixedUpdateContext3D context)
    {
        if (!ReferenceEquals(scene, _sceneControl.Scene)) return;
        _animationTimeSeconds = context.SimulationTimeSeconds;
        AnimateScene(scene, (float)context.SimulationTimeSeconds, context.DeltaSeconds);
    }

    private void OnSceneFixedUpdateCompleted(Scene3D scene, in SceneFixedUpdateContext3D context)
    {
        if (!ReferenceEquals(scene, _sceneControl.Scene)) return;
        // The engine advances the active clip before this callback. A zero-delta sequence
        // update performs only the deterministic transition to the next authored clip.
        _doctorWatsonSequence?.Advance(0f);
    }

    private void AnimateScene(Scene3D scene, float t, float dt)
    {
            if (_rotatingBox is not null)
            {
                _rotatingBox.RotationDegrees = new Vector3(10f + MathF.Sin(t * 0.9f) * 7f, t * 35f, MathF.Cos(t * 0.7f) * 5f);
            }

            if (_orbitSphere is not null)
            {
                _orbitSphere.Position = new Vector3(-0.9f + MathF.Sin(t * 1.18f) * 0.6f, 0.90f + MathF.Sin(t * 2.0f) * 0.18f, MathF.Cos(t * 1.18f) * 0.45f);
            }

            if (_rotatingCylinder is not null)
            {
                _rotatingCylinder.RotationDegrees = new Vector3(0f, t * 26f, 0f);
            }

            if (_animatedCone is not null)
            {
                _animatedCone.Scale = new Vector3(1f, 1f + MathF.Sin(t * 1.6f) * 0.10f, 1f);
            }

            if (_doctorWatsonModel is not null)
            {
                AnimateDoctorWatsonSequence(dt);
            }

            if (_cameraFlight.IsActive)
            {
                _cameraFlight.Update(dt);
            }

            if (_planet is not null)
            {
                AnimatePlanetScene(t);
            }

            if (_shaderAccent is not null)
            {
                _shaderAccent.Position = new Vector3(2.2f + MathF.Sin(t * 0.65f) * 0.75f, 1.7f + MathF.Sin(t * 1.1f) * 0.22f, -1.4f + MathF.Cos(t * 0.65f) * 0.70f);
            }

            if (_movingPointLight is not null)
            {
                _movingPointLight.Position = new Vector3(-2.2f + MathF.Sin(t * 0.8f) * 1.3f, 3.4f, -1.7f + MathF.Cos(t * 0.8f) * 1.1f);
            }

            if (_movingSpotLight is not null)
            {
                _movingSpotLight.Direction = Vector3.Normalize(new Vector3(MathF.Sin(t * 0.45f) * 0.35f - 0.25f, -1f, 0.55f));
            }

            if (_controlPlane is not null && !_controlPlane.AlwaysFaceCamera)
            {
                _controlPlane.FaceCamera(scene.Camera);
            }

            if (_bridgeSensorPanel is not null)
            {
                _bridgeSensorPanel.FaceCamera(scene.Camera);
            }

            if (_activeDemo == DemoSceneKind.Physics)
            {
                if ((_physicsCube is not null && _physicsCube.Position.Y < -4f) ||
                    (_physicsBall is not null && _physicsBall.Position.Y < -4f))
                {
                    ResetPhysicsCube();
                }
            }

            if (_rackLayer is not null)
            {
                AnimateRackTelemetry(t);
            }

            for (var i = 0; i < _stressAnimatedObjects.Count; i++)
            {
                var entry = _stressAnimatedObjects[i];
                var wave = MathF.Sin(t * 1.45f + entry.Phase);
                entry.Obj.Position = entry.Origin + new Vector3(0f, wave * 0.10f, 0f);
                entry.Obj.RotationDegrees = new Vector3(wave * 8f, t * (18f + i % 11), MathF.Cos(t + entry.Phase) * 6f);
            }

            if (_stressLayer is not null)
            {
                AnimateStressTelemetry(dt);
            }
        }


    private void BuildDemoSpecificControls(DemoSceneKind kind)
    {
        _demoSpecificPanel.Children.Clear();
        _demoSpecificPanel.Children.Add(Section("Demo-specific controls"));
        _demoSpecificPanel.Children.Add(Button("Show scene diagnostics", ShowSceneDiagnostics));

        switch (kind)
        {
            case DemoSceneKind.CameraArcPlanetFocus:
                _planetLatBox = TextInput("24.5");
                _planetLonBox = TextInput("38.0");
                _demoSpecificPanel.Children.Add(Label("Latitude, degrees"));
                _demoSpecificPanel.Children.Add(_planetLatBox);
                _demoSpecificPanel.Children.Add(Label("Longitude, degrees"));
                _demoSpecificPanel.Children.Add(_planetLonBox);
                _demoSpecificPanel.Children.Add(Button("Fly to highlighted coordinate", StartPlanetCameraFlight));
                _demoSpecificPanel.Children.Add(Button("Random coordinate", RandomizePlanetCoordinate));
                _demoSpecificPanel.Children.Add(Note("Камера строит orbit-path вокруг планеты с clearance radius, поэтому не режет путь через объект и сохраняет подсвеченную точку в фокусе."));
                break;
            case DemoSceneKind.ShaderLightingLab:
                _exposureBox = TextInput("1.05");
                _gammaBox = TextInput("2.20");
                _ambientBox = TextInput("0.28");
                _demoSpecificPanel.Children.Add(Label("Exposure"));
                _demoSpecificPanel.Children.Add(_exposureBox);
                _demoSpecificPanel.Children.Add(Label("Gamma"));
                _demoSpecificPanel.Children.Add(_gammaBox);
                _demoSpecificPanel.Children.Add(Label("Ambient intensity"));
                _demoSpecificPanel.Children.Add(_ambientBox);
                _demoSpecificPanel.Children.Add(Button("Apply shader/light settings", ApplyShaderLabValues));
                _demoSpecificPanel.Children.Add(Button("Warm sunset preset", () => SetShaderPreset(1.22f, 2.18f, 0.18f, new ColorRgba(1f, 0.72f, 0.48f, 1f))));
                _demoSpecificPanel.Children.Add(Button("Cold studio preset", () => SetShaderPreset(0.92f, 2.25f, 0.38f, new ColorRgba(0.70f, 0.82f, 1f, 1f))));
                _demoSpecificPanel.Children.Add(Note("Deferred, SSAO и HDR render targets будут добавлены на этапах 13–15. Это демо использует только реализованный forward GPU tone mapping без fallback."));
                break;
            case DemoSceneKind.ImportedGlbModel:
                if (_doctorWatsonSequence is not null)
                {
                    _demoSpecificPanel.Children.Add(Button("Restart authored GLB clip", AdvanceCharacterAnimationPhase));
                    _demoSpecificPanel.Children.Add(Note("Doctor Watson GLB содержит skeleton, skin weights и authored clip. Деформация выполняется GPU skinning path; CPU используется только для точного ray picking. Произвольные wave/jump/squat требуют отдельных authored clips или animation graph."));
                }
                else
                {
                    _demoSpecificPanel.Children.Add(Button("Wave phase", () => _characterSequenceTime = 0.05f));
                    _demoSpecificPanel.Children.Add(Button("Jump phase", () => _characterSequenceTime = 2.05f));
                    _demoSpecificPanel.Children.Add(Button("Squat phase", () => _characterSequenceTime = 4.05f));
                    _demoSpecificPanel.Children.Add(Note("Текущий Doctor Watson GLB не содержит skin/animation clips. Для настоящего махания рукой и приседаний нужен rigged/skinned GLB с bone-анимациями; здесь рядом показан procedural proxy, а imported mesh получает только root-motion."));
                }
                break;
            case DemoSceneKind.Physics:
                _demoSpecificPanel.Children.Add(Button("Drop bodies again", ResetPhysicsCube));
                _demoSpecificPanel.Children.Add(Note("Физика пересобрана как impulse-based solver: контактная скорость в точке касания, нормальный импульс, трение, rolling damping, substeps и solver-итерации."));
                break;
            case DemoSceneKind.BuildingWalkthrough:
                _demoSpecificPanel.Children.Add(Button("Reset person start", ResetBuildingPersonCamera));
                _demoSpecificPanel.Children.Add(Note("Навигация этой сцены автоматически переключается в Person mode. Используйте WASD/мышь; низкие ступени лестницы проходят через step-height коллизию."));
                break;
            case DemoSceneKind.BridgeDigitalTwin:
                _demoSpecificPanel.Children.Add(Button("Random sensor alert", RandomizeBridgeSensorAlert));
                _demoSpecificPanel.Children.Add(Note("Кликните зелёный датчик на мосту: выбранный сенсор подсвечивается красным, а имя содержит текущую телеметрию."));
                break;
            case DemoSceneKind.CrossPlatformStressLab:
                _stressColumnsBox = TextInput(_stressParameters.Columns.ToString(CultureInfo.InvariantCulture));
                _stressRowsBox = TextInput(_stressParameters.Rows.ToString(CultureInfo.InvariantCulture));
                _stressTransparentBox = TextInput(_stressParameters.TransparentObjects.ToString(CultureInfo.InvariantCulture));
                _stressParticleCapacityABox = TextInput(_stressParameters.ParticleCapacityA.ToString(CultureInfo.InvariantCulture));
                _stressParticleEmissionABox = TextInput(_stressParameters.ParticleEmissionA.ToString(CultureInfo.InvariantCulture));
                _stressParticleCapacityBBox = TextInput(_stressParameters.ParticleCapacityB.ToString(CultureInfo.InvariantCulture));
                _stressParticleEmissionBBox = TextInput(_stressParameters.ParticleEmissionB.ToString(CultureInfo.InvariantCulture));
                _stressTelemetryRateBox = TextInput(_stressParameters.TelemetryUpdatesPerSecond.ToString(CultureInfo.InvariantCulture));
                _demoSpecificPanel.Children.Add(Label("Retained field columns (1..4096)"));
                _demoSpecificPanel.Children.Add(_stressColumnsBox);
                _demoSpecificPanel.Children.Add(Label("Retained field rows (1..4096)"));
                _demoSpecificPanel.Children.Add(_stressRowsBox);
                _demoSpecificPanel.Children.Add(Label("Transparent objects (0..20000)"));
                _demoSpecificPanel.Children.Add(_stressTransparentBox);
                _demoSpecificPanel.Children.Add(Label("Particle A capacity / emission per second"));
                _demoSpecificPanel.Children.Add(_stressParticleCapacityABox);
                _demoSpecificPanel.Children.Add(_stressParticleEmissionABox);
                _demoSpecificPanel.Children.Add(Label("Particle B capacity / emission per second"));
                _demoSpecificPanel.Children.Add(_stressParticleCapacityBBox);
                _demoSpecificPanel.Children.Add(_stressParticleEmissionBBox);
                _demoSpecificPanel.Children.Add(Label("Telemetry state updates per second"));
                _demoSpecificPanel.Children.Add(_stressTelemetryRateBox);
                _demoSpecificPanel.Children.Add(Button("Apply exact stress parameters", ApplyStressParameters));
                _demoSpecificPanel.Children.Add(Button("Burst-update all telemetry", BurstStressTelemetry));
                _demoSpecificPanel.Children.Add(Note("Параметры применяются только кнопкой, чтобы ввод числа не пересобирал сцену на каждый символ. Поле ограничено 2 000 000 logical nodes; particle capacity — до 500 000 на поток. Telemetry churn распределён равномерно по fixed ticks без искусственного 4 Hz burst. Все counts идентичны на OpenGL и WebGL2."));
                break;
            default:
                _demoSpecificPanel.Children.Add(Note("Для этой сцены дополнительных параметров нет. Используйте Runtime switches и основную кнопку действия."));
                break;
        }
    }

    private void AnimateDoctorWatsonSequence(float dt)
    {
        if (_doctorWatsonModel is null) return;
        if (_doctorWatsonSequence is not null)
        {
            // Authored clips are advanced once by SceneUpdateLoop3D.
            return;
        }

        _characterSequenceTime += dt;
        var cycle = _characterSequenceTime % 6.0f;
        var importedY = _doctorWatsonBaseY;
        var rootBob = 0f;
        var rootLean = 0f;
        var rootYaw = 180f;

        if (cycle < 2f)
        {
            var wave = MathF.Sin(cycle * MathF.PI * 3.0f);
            rootYaw += wave * 5f;
            ApplyRigPose(rootY: 0f, torsoLean: 0f, rightArmZ: -95f + wave * 34f, leftArmZ: 18f, legBend: 0f);
        }
        else if (cycle < 4f)
        {
            var local = cycle - 2f;
            var jump = MathF.Sin(local * MathF.PI);
            rootBob = jump * 0.42f;
            rootLean = -jump * 5f;
            ApplyRigPose(rootY: rootBob, torsoLean: rootLean, rightArmZ: -35f - jump * 25f, leftArmZ: 35f + jump * 20f, legBend: jump * 22f);
        }
        else
        {
            var local = cycle - 4f;
            var squat = SmoothPulse(local / 2f);
            importedY -= squat * 0.14f;
            rootLean = squat * 11f;
            ApplyRigPose(rootY: -squat * 0.28f, torsoLean: rootLean, rightArmZ: -24f + squat * 20f, leftArmZ: 24f - squat * 18f, legBend: squat * 48f);
        }

        _doctorWatsonModel.Position = new Vector3(-0.45f, importedY + rootBob, 0f);
        _doctorWatsonModel.RotationDegrees = new Vector3(rootLean, rootYaw, 0f);
    }

    private void ApplyRigPose(float rootY, float torsoLean, float rightArmZ, float leftArmZ, float legBend)
    {
        if (_characterRigObjects.Count < 6) return;
        var torso = _characterRigObjects[0];
        var head = _characterRigObjects[1];
        var leftArm = _characterRigObjects[2];
        var rightArm = _characterRigObjects[3];
        var leftLeg = _characterRigObjects[4];
        var rightLeg = _characterRigObjects[5];

        torso.Position = new Vector3(0.72f, 1.05f + rootY, 0.02f);
        torso.RotationDegrees = new Vector3(torsoLean, 0f, 0f);
        head.Position = new Vector3(0.72f, 1.62f + rootY, 0.02f);
        leftArm.Position = new Vector3(0.43f, 1.08f + rootY, 0.03f);
        rightArm.Position = new Vector3(1.01f, 1.08f + rootY, 0.03f);
        leftLeg.Position = new Vector3(0.61f, 0.45f + rootY * 0.45f, 0.03f);
        rightLeg.Position = new Vector3(0.83f, 0.45f + rootY * 0.45f, 0.03f);

        leftArm.RotationDegrees = new Vector3(0f, 0f, leftArmZ);
        rightArm.RotationDegrees = new Vector3(0f, 0f, rightArmZ);
        leftLeg.RotationDegrees = new Vector3(legBend, 0f, -legBend * 0.22f);
        rightLeg.RotationDegrees = new Vector3(legBend, 0f, legBend * 0.22f);
    }

    private void AdvanceCharacterAnimationPhase()
    {
        if (_doctorWatsonSequence is not null)
        {
            _doctorWatsonSequence.PlayNext();
            return;
        }

        var cycle = _characterSequenceTime % 6f;
        _characterSequenceTime = cycle < 2f ? 2.05f : cycle < 4f ? 4.05f : 0.05f;
    }

    private static float SmoothPulse(float x)
    {
        x = Clamp01(x);
        return MathF.Sin(x * MathF.PI);
    }

    private void AnimatePlanetScene(float t)
    {
        if (_planet is null) return;
        var rotationDegrees = new Vector3(0f, t * 12.0f, 0f);
        if (_earthModel is not null) _earthModel.RotationDegrees = rotationDegrees;
        else _planet.RotationDegrees = rotationDegrees;
        UpdatePlanetFocusMarker(t);
    }

    private void UpdatePlanetFocusMarker(float t)
    {
        if (_planet is null || _planetMarker is null) return;
        var yaw = t * 12.0f * MathF.PI / 180f;
        var rotation = Matrix4x4.CreateRotationY(yaw);
        foreach (var surface in _planetSurfaceObjects)
        {
            var surfaceNormal = Vector3.Normalize(Vector3.TransformNormal(surface.Normal, rotation));
            surface.Obj.Position = _planet.Position + surfaceNormal * surface.RadiusOffset;
        }

        var rotatedNormal = Vector3.TransformNormal(_planetFocusPoint, rotation);
        var radius = _planet.Radius + 0.12f;
        var normal = Vector3.Normalize(rotatedNormal);
        _planetMarker.Position = _planet.Position + normal * radius;
        if (_planetLabel is not null)
        {
            _planetLabel.Position = _planet.Position + normal * (radius + 0.26f);
            _planetLabel.FaceCamera(_sceneControl.Scene.Camera);
        }
    }

    private void StartPlanetCameraFlight()
    {
        if (_planet is null) return;
        var lat = ParseBox(_planetLatBox, 24.5f);
        var lon = ParseBox(_planetLonBox, 38.0f);
        lat = global::System.Math.Clamp(lat, -89.5f, 89.5f);
        lon = NormalizeLongitude(lon);
        if (_planetLatBox is not null) _planetLatBox.Text = lat.ToString("0.###", CultureInfo.InvariantCulture);
        if (_planetLonBox is not null) _planetLonBox.Text = lon.ToString("0.###", CultureInfo.InvariantCulture);

        _planetFocusPoint = PlanetPointFromLatLon(lat, lon);
        ShowPlanetLocationLabel(lat, lon, _planetFocusPoint);
        UpdatePlanetFocusMarker((float)_animationTimeSeconds);
        var target = _planetMarker?.Position ?? (_planet.Position + _planetFocusPoint * (_planet.Radius + 0.12f));
        _cameraFlight.StartOrbitAround(
            _sceneControl.Scene.Camera,
            _planet.Position,
            target,
            protectedRadius: _planet.Radius + 0.18f,
            distanceFromSurface: 2.15f,
            durationSeconds: 2.35f,
            arcHeight: 0.55f);
        SetSelection(_planetMarker);
    }

    private void RandomizePlanetCoordinate()
    {
        var lat = -65f + (float)_random.NextDouble() * 130f;
        var lon = -180f + (float)_random.NextDouble() * 360f;
        if (_planetLatBox is not null) _planetLatBox.Text = lat.ToString("0.###", CultureInfo.InvariantCulture);
        if (_planetLonBox is not null) _planetLonBox.Text = lon.ToString("0.###", CultureInfo.InvariantCulture);
        StartPlanetCameraFlight();
    }

    private void SetPlanetMarkerFromNormal(Vector3 normal, bool showLabel)
    {
        if (_planet is null) return;
        normal = normal.LengthSquared() > 0.000001f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        var lat = MathF.Asin(global::System.Math.Clamp(normal.Y, -1f, 1f)) * 180f / MathF.PI;
        var lon = MathF.Atan2(normal.X, normal.Z) * 180f / MathF.PI;
        if (_planetLatBox is not null) _planetLatBox.Text = lat.ToString("0.###", CultureInfo.InvariantCulture);
        if (_planetLonBox is not null) _planetLonBox.Text = lon.ToString("0.###", CultureInfo.InvariantCulture);
        _planetFocusPoint = PlanetPointFromLatLon(lat, lon);
        UpdatePlanetFocusMarker((float)_animationTimeSeconds);
        if (showLabel) ShowPlanetLocationLabel(lat, lon, _planetFocusPoint);
    }

    private void ShowPlanetLocationLabel(float lat, float lon, Vector3 normal)
    {
        if (_planet is null) return;
        var scene = _sceneControl.Scene;
        if (_planetLabel is not null)
        {
            scene.Remove(_planetLabel);
            _planetLabel = null;
        }

        var label = ResolveEarthLocationName(lat, lon);
        var panel = new Border
        {
            Width = 560,
            Height = 160,
            Background = new SolidColorBrush(Color.FromArgb(236, 7, 11, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(88, 160, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = label, Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 24 },
                    new TextBlock { Text = $"lat {lat:0.##}, lon {lon:0.##}", Foreground = new SolidColorBrush(Color.FromRgb(190, 214, 248)), FontSize = 18 },
                    new TextBlock { Text = "interactive geospatial marker", Foreground = new SolidColorBrush(Color.FromRgb(116, 168, 244)), FontSize = 14 }
                }
            }
        };

        _planetLabel = scene.Add(new ControlPlane3D(panel)
        {
            Name = "Planet location label / " + label,
            Width = 1.75f,
            Height = 0.50f,
            AlwaysFaceCamera = true,
            RenderScale = 2.0d,
            IsPickable = false,
            IsManipulationEnabled = false
        });
        _planetLabel.Position = _planet.Position + Vector3.Normalize(normal) * (_planet.Radius + 0.48f);
    }


    private static string ResolveEarthLocationName(float lat, float lon)
    {
        lon = NormalizeLongitude(lon);

        var knownPlaces = new (string Name, float Lat, float Lon, float Radius)[]
        {
            ("Москва, Россия", 55.7558f, 37.6173f, 7.5f),
            ("Санкт-Петербург, Россия", 59.9343f, 30.3351f, 6.5f),
            ("Лондон, Великобритания", 51.5074f, -0.1278f, 6.5f),
            ("Париж, Франция", 48.8566f, 2.3522f, 6.0f),
            ("Нью-Йорк, США", 40.7128f, -74.0060f, 6.5f),
            ("Сан-Франциско, США", 37.7749f, -122.4194f, 6.0f),
            ("Рио-де-Жанейро, Бразилия", -22.9068f, -43.1729f, 6.5f),
            ("Каир, Египет", 30.0444f, 31.2357f, 7.0f),
            ("Кейптаун, ЮАР", -33.9249f, 18.4241f, 7.0f),
            ("Токио, Япония", 35.6762f, 139.6503f, 6.5f),
            ("Пекин, Китай", 39.9042f, 116.4074f, 7.0f),
            ("Сидней, Австралия", -33.8688f, 151.2093f, 7.0f),
            ("Дубай, ОАЭ", 25.2048f, 55.2708f, 6.0f),
            ("Сингапур", 1.3521f, 103.8198f, 6.0f)
        };

        foreach (var place in knownPlaces)
        {
            var dLat = lat - place.Lat;
            var dLon = NormalizeLongitude(lon - place.Lon) * MathF.Cos(place.Lat * MathF.PI / 180f);
            if (MathF.Sqrt(dLat * dLat + dLon * dLon) <= place.Radius) return place.Name;
        }

        if (lat > 66f) return "Арктика";
        if (lat < -60f) return "Антарктида";
        if (lat >= 34f && lat <= 72f && lon >= -25f && lon <= 45f) return "Европа";
        if (lat >= -35f && lat <= 36f && lon >= -20f && lon <= 52f) return "Африка";
        if (lat >= 5f && lat <= 78f && lon >= 45f && lon <= 180f) return "Азия";
        if (lat >= -45f && lat <= -10f && lon >= 110f && lon <= 155f) return "Австралия";
        if (lat >= 7f && lat <= 72f && lon >= -170f && lon <= -50f) return "Северная Америка";
        if (lat >= -56f && lat <= 13f && lon >= -82f && lon <= -34f) return "Южная Америка";
        return "океан / международная область";
    }

    private static Vector3 PlanetPointFromLatLon(float latitudeDegrees, float longitudeDegrees)
    {
        var lat = latitudeDegrees * MathF.PI / 180f;
        var lon = longitudeDegrees * MathF.PI / 180f;
        var cosLat = MathF.Cos(lat);
        return Vector3.Normalize(new Vector3(cosLat * MathF.Sin(lon), MathF.Sin(lat), cosLat * MathF.Cos(lon)));
    }

    private static float NormalizeLongitude(float lon)
    {
        while (lon > 180f) lon -= 360f;
        while (lon < -180f) lon += 360f;
        return lon;
    }

    private void ApplyShaderLabValues()
    {
        var scene = _sceneControl.Scene;
        scene.RenderPipeline.ToneMapping.Exposure = global::System.Math.Clamp(ParseBox(_exposureBox, scene.RenderPipeline.ToneMapping.Exposure), 0.15f, 4.0f);
        scene.RenderPipeline.ToneMapping.Gamma = global::System.Math.Clamp(ParseBox(_gammaBox, scene.RenderPipeline.ToneMapping.Gamma), 1.0f, 3.4f);
        scene.AmbientLightIntensity = global::System.Math.Clamp(ParseBox(_ambientBox, scene.AmbientLightIntensity), 0f, 2.0f);

        if (_exposureBox is not null) _exposureBox.Text = scene.RenderPipeline.ToneMapping.Exposure.ToString("0.###", CultureInfo.InvariantCulture);
        if (_gammaBox is not null) _gammaBox.Text = scene.RenderPipeline.ToneMapping.Gamma.ToString("0.###", CultureInfo.InvariantCulture);
        if (_ambientBox is not null) _ambientBox.Text = scene.AmbientLightIntensity.ToString("0.###", CultureInfo.InvariantCulture);
        UpdateStatus(force: true);
    }

    private void SetShaderPreset(float exposure, float gamma, float ambient, ColorRgba sunColor)
    {
        if (_exposureBox is not null) _exposureBox.Text = exposure.ToString("0.###", CultureInfo.InvariantCulture);
        if (_gammaBox is not null) _gammaBox.Text = gamma.ToString("0.###", CultureInfo.InvariantCulture);
        if (_ambientBox is not null) _ambientBox.Text = ambient.ToString("0.###", CultureInfo.InvariantCulture);
        if (_shaderSun is not null) _shaderSun.Color = sunColor;
        ApplyShaderLabValues();
    }

    private static float ParseBox(TextBox? box, float fallback)
        => float.TryParse(box?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private void ApplyStressParameters()
    {
        if (!TryReadStressValue(_stressColumnsBox, "columns", 1, 4096, out var columns, out var error) ||
            !TryReadStressValue(_stressRowsBox, "rows", 1, 4096, out var rows, out error) ||
            !TryReadStressValue(_stressTransparentBox, "transparent objects", 0, 20000, out var transparent, out error) ||
            !TryReadStressValue(_stressParticleCapacityABox, "particle A capacity", 1, 500000, out var capacityA, out error) ||
            !TryReadStressValue(_stressParticleEmissionABox, "particle A emission", 0, 1000000, out var emissionA, out error) ||
            !TryReadStressValue(_stressParticleCapacityBBox, "particle B capacity", 1, 500000, out var capacityB, out error) ||
            !TryReadStressValue(_stressParticleEmissionBBox, "particle B emission", 0, 1000000, out var emissionB, out error) ||
            !TryReadStressValue(_stressTelemetryRateBox, "telemetry updates/s", 0, 50000000, out var telemetryRate, out error))
        {
            ShowStressParameterError(error);
            return;
        }

        var logicalNodeCount = (long)columns * rows;
        if (logicalNodeCount > 2_000_000L)
        {
            ShowStressParameterError($"columns × rows must not exceed 2,000,000 logical nodes (received {logicalNodeCount:n0}).");
            return;
        }

        _stressParameters = new StressParameters(
            columns,
            rows,
            transparent,
            capacityA,
            emissionA,
            capacityB,
            emissionB,
            telemetryRate);
        EngineLog3D.Information("Demo.Stress", $"Applying exact stress parameters: {_stressParameters}.");
        LoadDemo(DemoSceneKind.CrossPlatformStressLab);
    }

    private void ShowStressParameterError(string error)
    {
        var message = "Stress parameter error: " + error;
        EngineLog3D.Warning("Demo.Stress", message);
        _lastStatusText = message;
        _statusText.Text = message;
    }

    private static bool TryReadStressValue(TextBox? box, string name, int minimum, int maximum, out int value, out string error)
    {
        if (!int.TryParse(box?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"{name} must be an integer.";
            return false;
        }
        if (value < minimum || value > maximum)
        {
            error = $"{name} must be in range {minimum:n0}..{maximum:n0}.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private void AnimateRackTelemetry(float t)
    {
        var layer = _rackLayer;
        if (layer is null || layer.Instances.Count == 0)
        {
            return;
        }

        for (var i = 0; i < 3; i++)
        {
            var index = (_telemetryCursor++ + i * 17) % layer.Instances.Count;
            var wave = MathF.Sin(t * 1.6f + index * 0.37f);
            var variant = wave > 0.72f ? 2 : wave > 0.25f ? 1 : 0;
            layer.SetInstanceMaterialVariant(index, variant);
        }
    }

    private void ResetPhysicsCube()
    {
        if (_physicsCube is not null)
        {
            _physicsCube.Position = new Vector3(-1.35f, 4.25f, 0.35f);
            _physicsCube.RotationDegrees = Vector3.Zero;
            if (_physicsCube.Rigidbody is not null)
            {
                _physicsCube.Rigidbody.Velocity = new Vector3(0.42f + (float)_random.NextDouble() * 0.35f, 0f, (float)(_random.NextDouble() - 0.5) * 0.38f);
                _physicsCube.Rigidbody.AngularVelocity = new Vector3(0.18f, 0.05f, 0.55f + (float)_random.NextDouble() * 0.25f);
                _physicsCube.Rigidbody.IsKinematic = false;
            }
        }

        if (_physicsBall is not null)
        {
            _physicsBall.Position = new Vector3(-2.45f, 3.25f, -0.45f);
            _physicsBall.RotationDegrees = Vector3.Zero;
            if (_physicsBall.Rigidbody is not null)
            {
                _physicsBall.Rigidbody.Velocity = new Vector3(0.70f + (float)_random.NextDouble() * 0.35f, 0f, 0.16f + (float)_random.NextDouble() * 0.24f);
                _physicsBall.Rigidbody.AngularVelocity = new Vector3(0.08f, 0.22f, 1.05f + (float)_random.NextDouble() * 0.35f);
                _physicsBall.Rigidbody.IsKinematic = false;
            }
        }

        for (var i = 0; i < _physicsObjects.Count; i++)
        {
            var obj = _physicsObjects[i];
            if (ReferenceEquals(obj, _physicsCube) || ReferenceEquals(obj, _physicsBall) || obj.Rigidbody is null)
            {
                continue;
            }

            var slot = global::System.Math.Max(0, i - 2);
            var column = slot % 8;
            var layer = slot / 8;
            obj.Position = new Vector3(-2.25f + column * 0.62f, 2.75f + layer * 0.46f, -0.65f + (slot % 3) * 0.44f);
            obj.RotationDegrees = Vector3.Zero;
            obj.Rigidbody.Velocity = new Vector3(0.14f + (slot % 5) * 0.05f, 0f, ((slot * 3) % 5 - 2f) * 0.035f);
            obj.Rigidbody.AngularVelocity = new Vector3(0.10f + (slot % 4) * 0.08f, 0.05f, 0.38f + (slot % 6) * 0.09f);
        }
    }

    private void RandomizeRackTelemetry()
    {
        var layer = _rackLayer;
        if (layer is null) return;
        using var batch = layer.BeginTelemetryBatch();
        for (var i = 0; i < layer.Instances.Count; i++)
        {
            var r = _random.NextDouble();
            batch.SetMaterialVariant(i, r > 0.92 ? 2 : r > 0.72 ? 1 : r > 0.66 ? 3 : 0);
        }
    }

    private void BurstStressTelemetry()
    {
        var layer = _stressLayer;
        if (layer is null) return;
        _telemetryCursor++;
        using var batch = layer.BeginTelemetryBatch();
        for (var i = 0; i < layer.Instances.Count; i++)
        {
            var signal = (i * 17 + _telemetryCursor * 29) % 23;
            batch.SetMaterialVariant(i, signal == 0 ? 3 : signal < 5 ? 2 : signal < 12 ? 1 : 0);
        }
    }

    private void AnimateStressTelemetry(float deltaSeconds)
    {
        var layer = _stressLayer;
        if (layer is null || layer.Instances.Count == 0) return;
        _stressTelemetryAccumulator += _stressParameters.TelemetryUpdatesPerSecond * global::System.Math.Max(0f, deltaSeconds);
        var scheduled = (int)global::System.Math.Min(int.MaxValue, global::System.Math.Floor(_stressTelemetryAccumulator));
        if (scheduled <= 0) return;
        _stressTelemetryAccumulator -= scheduled;

        // A logical instance needs at most one final state per fixed tick. Very high requested
        // rates therefore saturate at one complete layer pass per tick instead of producing
        // redundant CPU writes that do not increase GPU state-buffer pressure.
        var updateCount = global::System.Math.Min(layer.Instances.Count, scheduled);
        var start = _stressTelemetryUpdateCursor;
        using var batch = layer.BeginTelemetryBatch();
        for (var i = 0; i < updateCount; i++)
        {
            var index = (start + i) % layer.Instances.Count;
            batch.SetMaterialVariant(index, ((_telemetryCursor + i) * 3 + index) & 3);
        }
        _telemetryCursor++;
        _stressTelemetryUpdateCursor = (start + updateCount) % layer.Instances.Count;
    }

    private void OnObjectClicked(object? sender, ScenePointerEventArgs e)
    {
        _clickCount++;

        if (_activeDemo == DemoSceneKind.CameraArcPlanetFocus && _planet is not null)
        {
            if (e.ModelHit?.Model == _earthModel)
            {
                var normal = Vector3.Normalize(e.ModelHit.WorldPosition - _planet.Position);
                SetPlanetMarkerFromNormal(normal, showLabel: true);
                SetSelection(_planetMarker);
                return;
            }

            if (ReferenceEquals(e.Target, _planet))
            {
                var normal = Vector3.Normalize(e.WorldPosition - _planet.Position);
                SetPlanetMarkerFromNormal(normal, showLabel: true);
                SetSelection(_planetMarker);
                return;
            }

            if (ReferenceEquals(e.Target, _planetMarker))
            {
                var normal = Vector3.Normalize(_planetMarker.Position - _planet.Position);
                SetPlanetMarkerFromNormal(normal, showLabel: true);
                SetSelection(_planetMarker);
                return;
            }
        }

        if (_activeDemo == DemoSceneKind.BridgeDigitalTwin && _bridgeSensors.Contains(e.Target))
        {
            HighlightBridgeSensor(e.Target);
            SetSelection(e.Target);
            return;
        }

        SetSelection(e.Target);
    }

    private void SetSelection(Object3D? obj)
    {
        if (ReferenceEquals(_selectedObject, obj))
        {
            UpdateSelectionText();
            return;
        }

        if (_selectedObject is not null)
        {
            _selectedObject.IsSelected = false;
        }

        _selectedObject = obj;
        if (_selectedObject is not null)
        {
            _selectedObject.IsSelected = true;
        }

        UpdateSelectionText();
    }

    private void UpdateSelectionText()
    {
        if (_selectionText is null)
        {
            return;
        }

        _selectionText.Text = _selectedObject is null
            ? "Selection: none. Click a pickable object in the current demo."
            : $"Selection: {_selectedObject.Name}\nPosition: {FormatVector(_selectedObject.Position)}\nClicks: {_clickCount.ToString(CultureInfo.InvariantCulture)}";
    }

    private void OnFrameRendered(object? sender, SceneFrameRenderedEventArgs e)
    {
        if (_backendText is null)
        {
            return;
        }

        _lastFrame = e;
        _lastFps = e.PresentedFramesPerSecond;

        var now = Stopwatch.GetTimestamp();
        // Text layout competes with the graphics callback on Avalonia's UI thread. Keep
        // diagnostics live but never recreate two multi-line text layouts four times per
        // second on Desktop; the render statistics themselves are still sampled per frame.
        var backendTextInterval = Stopwatch.Frequency;
        if (_lastBackendTextTicks == 0 || (now - _lastBackendTextTicks) >= backendTextInterval)
        {
            _lastBackendTextTicks = now;
            _backendText.Text =
                $"Backend: {e.Kind} | presented {e.PresentationIntervalMilliseconds:0.00} ms | render {e.Stats.BackendMilliseconds:0.00} ms";
        }

        UpdateStatus();
    }

    private void UpdateStatus(bool force = false)
    {
        if (_statusText is null)
        {
            return;
        }

        if (_showingDiagnosticReport && !force)
        {
            return;
        }

        _showingDiagnosticReport = false;

        var now = Stopwatch.GetTimestamp();
        var statusIntervalTicks = Stopwatch.Frequency;
        if (!force && _lastStatusTicks != 0 && (now - _lastStatusTicks) < statusIntervalTicks)
        {
            return;
        }
        _lastStatusTicks = now;

        var stats = _lastFrame?.Stats ?? new RenderStats();
        var stressLine = _activeDemo == DemoSceneKind.CrossPlatformStressLab
            ? $"Stress: {_stressParameters.Columns}x{_stressParameters.Rows} nodes | transparent {_stressParameters.TransparentObjects:n0} | particles {_stressParameters.ParticleCapacityA:n0}+{_stressParameters.ParticleCapacityB:n0} | telemetry {_stressParameters.TelemetryUpdatesPerSecond:n0}/s\n"
            : string.Empty;
        var statusText =
            $"Demo: {GetDefinition(_activeDemo).Title}\n" +
            stressLine +
            $"FPS: {_lastFps:0.0}\n" +
            $"Objects: {stats.ObjectCount:n0} | Renderables: {stats.RenderableCount:n0} | Pickables: {stats.PickableCount:n0}\n" +
            $"HighScale: {stats.HighScaleInstanceCount:n0} instances | chunks {stats.VisibleChunkCount:n0}/{stats.TotalChunkCount:n0}\n" +
            $"Draw: {stats.DrawCallCount:n0} calls | batches {stats.InstancedBatchCount:n0} | triangles {stats.TriangleCount:n0}\n" +
            $"Frame: CPU prep {stats.CpuPreparationMilliseconds:0.00} ms | upload {stats.UploadMilliseconds:0.00} ms | backend {stats.BackendMilliseconds:0.00} ms\n" +
            $"Pipeline: {stats.RenderPipelineMode} | tone {OnOff(stats.ToneMappingActive)} mode={stats.ToneMappingMode}\n" +
            $"RHI: {stats.RhiBackend} gen={stats.RhiContextGeneration} resources={stats.RhiResourceCount:n0} registered={stats.RhiResidentBytes / 1024d:0.0} KB | GPU {(stats.GpuTimingAvailable ? stats.GpuFrameMilliseconds.ToString("0.00", CultureInfo.InvariantCulture) + " ms" : "unavailable")}\n" +
            $"Particles: {stats.ParticleCount:n0} | control planes: {stats.ControlPlaneCount:n0}\n" +
            $"Smooth high-scale motion: {OnOff(_sceneControl.Scene.Performance.EnableWebGlClientGpuTransformAnimation)} | overlay: {OnOff(_sceneControl.ShowPerformanceMetrics)}" +
            (_activeDemo == DemoSceneKind.ImportedGlbModel && !string.IsNullOrWhiteSpace(_doctorWatsonImportInfo)
                ? "\n" + _doctorWatsonImportInfo
                : string.Empty);

        if (!string.Equals(_lastStatusText, statusText, StringComparison.Ordinal))
        {
            _lastStatusText = statusText;
            _statusText.Text = statusText;
        }
    }

    private static IEnumerable<Matrix4x4> CreateRackTransforms(int columns, int rows, float spacingX, float spacingZ)
    {
        var offsetX = (columns - 1) * spacingX * 0.5f;
        var offsetZ = (rows - 1) * spacingZ * 0.5f;
        for (var z = 0; z < rows; z++)
        {
            for (var x = 0; x < columns; x++)
            {
                // Rack mesh parts are authored around their local origin. Keep the rack
                // center above the floor so the ground/depth pass cannot hide the retained
                // high-scale layer from the default demo camera.
                var y = 1.06f + ((x + z) % 3 == 0 ? 0.06f : 0f);
                yield return Matrix4x4.CreateRotationY(((x + z) & 1) == 0 ? 0f : MathF.PI) *
                             Matrix4x4.CreateTranslation(x * spacingX - offsetX, y, z * spacingZ - offsetZ);
            }
        }
    }

    private static ColorRgba ColorFromHue(float hue)
    {
        hue -= MathF.Floor(hue);
        var r = System.MathF.Abs(hue * 6f - 3f) - 1f;
        var g = 2f - System.MathF.Abs(hue * 6f - 2f);
        var b = 2f - System.MathF.Abs(hue * 6f - 4f);
        return new ColorRgba(Clamp01(r), Clamp01(g), Clamp01(b), 1f);
    }

    private static ColorRgba WithAlpha(ColorRgba color, float alpha)
        => new(color.R, color.G, color.B, Clamp01(alpha));

    private static float Clamp01(float value) => System.MathF.Max(0f, System.MathF.Min(1f, value));

    private static TextBox TextInput(string text)
        => new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

    private static TextBlock Label(string text)
        => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 229)),
            FontSize = 12
        };

    private static TextBlock Note(string text)
        => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(176, 188, 205)),
            TextWrapping = TextWrapping.Wrap
        };

    private static CheckBox Check(string text, bool isChecked)
        => new()
        {
            Content = text,
            IsChecked = isChecked,
            Foreground = Brushes.White
        };

    private static TextBlock Section(string text)
        => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(128, 185, 255)),
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 8, 0, 0)
        };

    private static TextBlock MonospaceText(string text)
        => new()
        {
            Text = text,
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(218, 226, 238)),
            TextWrapping = TextWrapping.Wrap
        };

    private static Button Button(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string FormatVector(Vector3 value)
        => $"({value.X:0.00}, {value.Y:0.00}, {value.Z:0.00})";
}

public sealed class DemoRack3D : CompositeObject3D
{
    public DemoRack3D()
    {
        Name = "DemoRack3D";
    }

    protected override void Build(CompositeBuilder3D b)
    {
        b.Box("RackBody", 0.82f, 2.0f, 0.92f)
            .Material(Lit(0.20f, 0.23f, 0.29f))
            .NoCollider()
            .Pickable(false)
            .Manipulation(false);

        b.Box("RackDoor", 0.74f, 1.82f, 0.045f)
            .At(0f, 0f, -0.49f)
            .Material(Lit(0.085f, 0.11f, 0.15f))
            .NoCollider()
            .Pickable(false)
            .Manipulation(false);

        for (var i = 0; i < 7; i++)
        {
            b.Box("Module" + i.ToString(CultureInfo.InvariantCulture), 0.58f, 0.065f, 0.04f)
                .At(0f, -0.68f + i * 0.20f, -0.535f)
                .Material(Lit(0.40f, 0.45f, 0.54f))
                .NoCollider()
                .Pickable(false)
                .Manipulation(false);
        }

        b.Box("StatusLed", 0.13f, 0.13f, 0.055f)
            .At(0.29f, 0.78f, -0.555f)
            .Material(new Material3D
            {
                BaseColor = new ColorRgba(0.17f, 0.92f, 0.38f, 1f),
                Lighting = LightingMode.Unlit
            })
            .NoCollider()
            .Pickable(false)
            .Manipulation(false);
    }

    private static Material3D Lit(float r, float g, float b)
        => new()
        {
            BaseColor = new ColorRgba(r, g, b, 1f),
            Lighting = LightingMode.Lambert,
            Roughness = 0.82f
        };
}

public sealed class DemoStressNode3D : CompositeObject3D
{
    public DemoStressNode3D()
    {
        Name = "DemoStressNode3D";
    }

    protected override void Build(CompositeBuilder3D b)
    {
        b.Box("Core", 0.34f, 0.72f, 0.34f)
            .At(0f, 0.36f, 0f)
            .Material(Lit(new ColorRgba(0.18f, 0.28f, 0.46f, 1f)))
            .NoCollider()
            .Pickable(false)
            .Manipulation(false);

        b.Sphere("TelemetryOrb", 0.15f, 16, 8)
            .At(0f, 0.86f, 0f)
            .Material(new Material3D
            {
                BaseColor = new ColorRgba(0.18f, 0.82f, 1f, 1f),
                Lighting = LightingMode.Phong,
                SpecularStrength = 0.62f,
                Shininess = 72f
            })
            .NoCollider()
            .Pickable(false)
            .Manipulation(false);

        b.Box("SignalBar", 0.56f, 0.055f, 0.055f)
            .At(0f, 0.58f, -0.20f)
            .Material(new Material3D
            {
                BaseColor = new ColorRgba(0.22f, 1f, 0.52f, 1f),
                Lighting = LightingMode.Unlit
            })
            .NoCollider()
            .Pickable(false)
            .Manipulation(false);
    }

    private static Material3D Lit(ColorRgba color)
        => new()
        {
            BaseColor = color,
            Lighting = LightingMode.BlinnPhong,
            SpecularStrength = 0.38f,
            Shininess = 48f,
            Roughness = 0.64f
        };
}
