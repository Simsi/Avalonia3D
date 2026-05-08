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
using Avalonia.Threading;
using Avalonia.VisualTree;
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
using ThreeDEngine.Core.Rendering.Capabilities;
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
        RenderPipelineAndDiagnostics
    }

    private sealed record DemoDefinition(DemoSceneKind Kind, string Title, string Summary);

    private static readonly DemoDefinition[] DemoDefinitions =
    {
        new(DemoSceneKind.PrimitivesAndMaterials, "01. Геометрия и материалы", "Процедурные примитивы, прозрачность, разные lighting-модели и базовая анимация transform."),
        new(DemoSceneKind.LightingAndEnvironment, "02. Свет, тени и окружение", "Directional, point и spot lights, skybox, ambient light и directional shadows."),
        new(DemoSceneKind.PickingAndInteraction, "03. Picking и selection", "Объекты сцены кликаются, получают selection state и отдают данные обратно в Avalonia UI."),
        new(DemoSceneKind.EmbeddedAvaloniaControls, "04. Avalonia UI внутри 3D", "Обычные Avalonia controls рендерятся на 3D-плоскость и принимают pointer input."),
        new(DemoSceneKind.HighScaleDigitalTwin, "05. High-scale / digital twin", "Retained instance layer для большого числа похожих объектов, palette variants, telemetry updates и smooth GPU motion."),
        new(DemoSceneKind.Particles, "06. Particle system", "Несколько emitter-типов: мягкий fountain, искры и полноценные 3D cube particles."),
        new(DemoSceneKind.Physics, "07. Default rigidbody physics", "Встроенная default-физика: rigidbody, вращения, friction/restitution, sleep, CCD и многоточечные контакты для устойчивых столкновений."),
        new(DemoSceneKind.ImportedGlbModel, "08. GLB character / Doctor Watson", "Загрузка rigged/skinned GLB с embedded текстурами и authored skeletal animation clip."),
        new(DemoSceneKind.CameraArcPlanetFocus, "09. Камера: дуговой облёт и фокус", "Детализированная планета, координаты точки вводятся вручную, камера облетает тело по безопасной дуге."),
        new(DemoSceneKind.ShaderLightingLab, "10. Шейдеры, свет и цветокор", "Отдельная сцена для настройки света, HDR/tone mapping, SSAO и визуальных пресетов."),
        new(DemoSceneKind.BuildingWalkthrough, "11. Person camera / 4-этажное здание", "Площадка и 4-этажное здание с кабинетами, мебелью и Person-навигацией с физическими коллизиями."),
        new(DemoSceneKind.BridgeDigitalTwin, "12. Цифровой двойник разводного моста", "Большой разводной мост, створки, опоры, трафик и множество интерактивных датчиков телеметрии."),
        new(DemoSceneKind.RenderPipelineAndDiagnostics, "13. Pipeline и diagnostics", "HDR/deferred-if-supported/SSAO flags, wireframe overlay, frame stats и runtime metrics.")
    };

    private readonly Scene3DControl _sceneControl;
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new(20260506);
    private readonly List<Object3D> _selectableObjects = new();
    private readonly List<Object3D> _physicsObjects = new();
    private readonly List<Object3D> _characterRigObjects = new();
    private readonly List<ParticleSystem3D> _particleSystems = new();
    private readonly List<Object3D> _bridgeSensors = new();
    private readonly List<(Object3D Obj, Vector3 Normal, float RadiusOffset)> _planetSurfaceObjects = new();
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
    private CheckBox _shadowsCheck = null!;
    private Button _primaryActionButton = null!;
    private StackPanel _demoSpecificPanel = null!;
    private TextBox? _planetLatBox;
    private TextBox? _planetLonBox;
    private TextBox? _exposureBox;
    private TextBox? _gammaBox;
    private TextBox? _ambientBox;
    private TextBox? _ssaoBox;

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
    private ParticleSystem3D? _particleSystem;
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
    private Vector3 _planetFocusPoint = Vector3.UnitZ;
    private DirectionalLight3D? _shaderSun;
    private PointLight3D? _shaderAccent;
    private float _characterSequenceTime;
    private float _doctorWatsonBaseY;
    private SceneFrameRenderedEventArgs? _lastFrame;
    private double _lastFrameTime;
    private double _lastFps;
    private int _clickCount;
    private int _telemetryCursor;
    private int _embeddedCounter;
    private long _lastStatusTicks;

    public MainView()
    {
        InitializeComponent();

        _sceneControl = new Scene3DControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ShowPerformanceMetrics = true,
            ContinuousRendering = true,
            ContinuousRenderingFps = 60d,
            FpsLockEnabled = true,
            TargetFps = 60d,
            UnlockedMaxFps = 180d,
            FrameInterpolationEnabled = true,
            FrameInterpolationTickFps = 30d,
            AdaptivePerformanceEnabled = false,
            EnableSceneNavigation = true,
            ShowCenterCursor = true,
            Width = double.NaN,
            Height = double.NaN
        };
        _sceneControl.ObjectClicked += OnObjectClicked;
        _sceneControl.SelectionChanged += (_, e) => SetSelection(e.NewSelection);
        _sceneControl.FrameRendered += OnFrameRendered;

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animationTimer.Tick += OnAnimationTick;

        BuildUi();
        LoadDemo(DemoSceneKind.PrimitivesAndMaterials);
        _animationTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _animationTimer.Stop();
        base.OnDetachedFromVisualTree(e);
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
        _metricsCheck = Check("Performance overlay", true);
        _wireframeCheck = Check("Wireframe overlay", false);
        _shadowsCheck = Check("Directional shadows", true);

        _metricsCheck.IsCheckedChanged += (_, _) => ApplyRuntimeToggles();
        _wireframeCheck.IsCheckedChanged += (_, _) => ApplyRuntimeToggles();
        _shadowsCheck.IsCheckedChanged += (_, _) => ApplyRuntimeToggles();

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
        stack.Children.Add(_shadowsCheck);

        stack.Children.Add(Section("Scene notes"));
        stack.Children.Add(new TextBlock
        {
            Text = "Каждая сцена намеренно маленькая: она показывает одну группу возможностей, чтобы поведение было проще проверить на Desktop и Web backend.",
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
            _sceneControl.PersonSettings.MoveSpeed = 3.2f;
            _sceneControl.PersonSettings.RunMultiplier = 1.7f;
            _sceneControl.PersonSettings.BodyRadius = 0.28f;
            _sceneControl.PersonSettings.BodyHeight = 1.72f;
            _sceneControl.PersonSettings.EyeHeight = 1.56f;
            _sceneControl.PersonSettings.StepHeight = 0.34f;
            _sceneControl.PersonSettings.JumpSpeed = 4.4f;
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
        _particleSystems.Clear();
        _bridgeSensors.Clear();
        _planetSurfaceObjects.Clear();
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
        _particleSystem = null;
        _physicsCube = null;
        _physicsBall = null;
        _doctorWatsonModel = null;
        _doctorWatsonSequence = null;
        _doctorWatsonImportInfo = string.Empty;
        _earthModel = null;
        _planet = null;
        _planetMarker = null;
        _planetLabel = null;
        _shaderSun = null;
        _shaderAccent = null;
        _characterSequenceTime = 0f;
        _doctorWatsonBaseY = 0f;
        _clickCount = 0;
        _telemetryCursor = 0;
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
        scene.Environment.DirectionalShadows.IsEnabled = true;
        scene.Environment.DirectionalShadows.Resolution = 1024;
        scene.Environment.DirectionalShadows.OrthographicSize = 24f;
        scene.Environment.DirectionalShadows.Distance = 36f;
        scene.Environment.DirectionalShadows.Strength = 0.46f;
        scene.Environment.DirectionalShadows.Bias = 0.004f;

        scene.Debug.ShowWireframeOverlay = false;
        scene.Performance.EnableWebGlClientGpuTransformAnimation = false;
        scene.Performance.WebGlClientGpuTransformAnimationAmplitude = 0f;
        scene.Performance.EnableWebGlClientHighScaleRuntime = true;
        scene.Performance.EnableBakedHighScaleDetailedMeshes = true;
        scene.Performance.EnableHighScalePaletteTexture = true;
        scene.FrameInterpolator.Enabled = true;
        scene.FrameInterpolator.SimulationTickFps = 30d;
        scene.PhysicsCore = null;

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
    }

    private void BuildLightingAndEnvironmentScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(7.6f, 5.1f, -8.6f);
        scene.Camera.Target = new Vector3(0.2f, 1.0f, 0.3f);
        scene.AmbientLightIntensity = 0.16f;
        scene.Environment.DirectionalShadows.Strength = 0.62f;
        AddGround(scene, 12f, 8f);

        AddSelectable(scene, new Box3D
        {
            Name = "Shadow receiver cube",
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
    }

    private void BuildPickingAndInteractionScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(6.0f, 3.8f, -7.4f);
        scene.Camera.Target = new Vector3(0f, 0.9f, 0f);
        AddGround(scene, 9f, 6f);

        for (var i = 0; i < 6; i++)
        {
            var x = -3.0f + i * 1.2f;
            Object3D obj = (i % 3) switch
            {
                0 => new Box3D
                {
                    Name = "Pickable station " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Width = 0.82f,
                    Height = 0.82f,
                    Depth = 0.82f,
                    Position = new Vector3(x, 0.46f, 0f),
                    Material = Material3D.CreatePhong(new ColorRgba(0.20f + i * 0.10f, 0.54f, 0.96f, 1f), 0.45f, 48f)
                },
                1 => new Sphere3D
                {
                    Name = "Pickable sensor " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Radius = 0.46f,
                    Segments = 32,
                    Rings = 16,
                    Position = new Vector3(x, 0.52f, 0f),
                    Material = Material3D.CreateLambert(new ColorRgba(0.20f, 0.82f - i * 0.04f, 0.42f, 1f))
                },
                _ => new Cylinder3D
                {
                    Name = "Pickable module " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Radius = 0.34f,
                    Height = 0.92f,
                    Segments = 28,
                    Position = new Vector3(x, 0.46f, 0f),
                    Material = Material3D.CreatePhong(new ColorRgba(0.96f, 0.56f, 0.18f, 1f), 0.34f, 36f)
                }
            };
            AddSelectable(scene, obj);
        }

        _rotatingBox = scene.Add(new Box3D
        {
            Name = "Selection marker / non-pickable helper",
            Width = 7.4f,
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
        scene.Camera.Position = new Vector3(4.4f, 2.8f, -5.8f);
        scene.Camera.Target = new Vector3(0f, 1.3f, 0f);
        AddGround(scene, 8f, 5f);

        var model = AddSelectable(scene, new Box3D
        {
            Name = "3D object controlled by Avalonia button",
            Width = 1.25f,
            Height = 1.25f,
            Depth = 1.25f,
            Position = new Vector3(-1.9f, 0.75f, 0.45f),
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
                _rotatingBox.Material = Material3D.CreatePhong(ColorFromHue(hue), 0.55f, 72f);
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

        _controlPlane = scene.Add(new ControlPlane3D(statusBadge)
        {
            Name = "Embedded Avalonia control plane / front-facing",
            Width = 4.4f,
            Height = 2.2f,
            Position = new Vector3(1.15f, 1.75f, 0.1f),
            AlwaysFaceCamera = true
        });
    }

    private void BuildHighScaleDigitalTwinScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(8.8f, 5.5f, -10.4f);
        scene.Camera.Target = new Vector3(0.6f, 1.0f, 0.7f);
        AddGround(scene, 13f, 10f);
        scene.Performance.EnableWebGlClientGpuTransformAnimation = true;
        scene.Performance.WebGlClientGpuTransformAnimationAmplitude = 0.11f;

        var template = HighScaleTemplateCompiler.Compile(3001, new DemoRack3D(), true);
        template.AddMaterialVariant(1, "Warning").DefaultColor = new ColorRgba(1.0f, 0.72f, 0.18f, 1f);
        template.AddMaterialVariant(2, "Critical").DefaultColor = new ColorRgba(1.0f, 0.19f, 0.14f, 1f);
        template.AddMaterialVariant(3, "Offline").DefaultColor = new ColorRgba(0.22f, 0.24f, 0.29f, 0.52f);

        var layer = new HighScaleInstanceLayer3D(template, 128, 6f)
        {
            Name = "Digital twin rack layer"
        };
        layer.LodPolicy.DetailedDistance = 38f;
        layer.LodPolicy.SimplifiedDistance = 72f;
        layer.LodPolicy.ProxyDistance = 140f;
        layer.LodPolicy.DrawDistance = 220f;
        layer.LodPolicy.FadeDistance = 24f;
        layer.AddInstances(CreateRackTransforms(12, 8, 1.08f, 1.05f));
        scene.Add(layer);
        _rackLayer = layer;
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

        _particleSystem = AddParticleSystem(scene,
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
        _particleSystems.Add(system);
        return system;
    }

    private void BuildPhysicsScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(5.6f, 3.4f, -6.8f);
        scene.Camera.Target = new Vector3(0.3f, 0.9f, 0.2f);
        scene.PhysicsCore = new Jitter2PhysicsCore
        {
            FixedTimeStep = 1f / 120f,
            MaxStepsPerFrame = 10,
            SubstepCount = 4,
            SolverIterations = (solver: 14, relaxation: 5),
            Gravity = new Vector3(0f, -9.81f, 0f)
        };

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
    }

    private void BuildImportedGlbModelScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(4.3f, 2.45f, -5.4f);
        scene.Camera.Target = new Vector3(0f, 1.10f, 0f);
        scene.AmbientLightIntensity = 0.38f;
        scene.Environment.DirectionalShadows.Strength = 0.42f;
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
                ? "Rigged animation data is present. CPU skinning is enabled, so the imported mesh bends according to authored bone animation clips."
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

        var uri = new Uri("avares://Avalonia3D/Assets/Models/Earth.glb");
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

    private static void ApplySidecarBaseColorTexture(ImportedModel3D model, string textureKey, byte[] textureData, string mimeType)
    {
        if (textureData.Length == 0) return;
        _ = model.Children;
        foreach (var part in model.ModelParts)
        {
            part.Material.SetBaseColorTexture(textureKey, textureData, mimeType);
        }
    }

    private void BuildCameraArcPlanetScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(0.0f, 3.05f, -6.7f);
        scene.Camera.Target = new Vector3(0f, 1.35f, 0f);
        scene.AmbientLightIntensity = 0.14f;
        scene.Environment.Skybox.Mode = SkyboxMode3D.StarField;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.002f, 0.006f, 0.022f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.010f, 0.016f, 0.040f, 1f);
        scene.Environment.Skybox.BottomColor = new ColorRgba(0.000f, 0.002f, 0.010f, 1f);
        scene.Environment.Skybox.Intensity = 1.15f;

        var earthAsset = LoadEarthAsset();
        var planetCenter = new Vector3(0f, 1.45f, 0f);
        if (earthAsset is not null && !earthAsset.Diagnostics.HasErrors)
        {
            _earthModel = scene.ImportModel(earthAsset, options =>
            {
                options.Name = "Earth GLB / textured planet";
                options.Position = planetCenter;
                options.RotationDegrees = new Vector3(0f, 0f, 0f);
                options.Scale = Vector3.One;
            });
            var earthTexture = TryReadAvaloniaResourceBytes("avares://Avalonia3D/Assets/Models/Earth.jpg");
            if (earthTexture is { Length: > 0 })
            {
                ApplySidecarBaseColorTexture(_earthModel, "sidecar-texture:Earth.jpg", earthTexture, "image/jpeg");
            }
            foreach (var part in _earthModel.ModelParts)
            {
                part.Material.Surface = SurfaceMode.Opaque;
                part.Material.Opacity = 1f;
                part.Material.Lighting = LightingMode.BlinnPhong;
                part.Material.DoubleSided = true;
            }

            _planet = scene.Add(new Sphere3D
            {
                Name = "Earth interaction proxy / hidden",
                Radius = 1.25f,
                Segments = 24,
                Rings = 12,
                Position = planetCenter,
                IsVisible = false,
                IsPickable = false,
                IsManipulationEnabled = false
            });

            _earthModel.IsPickable = true;
            _earthModel.ModelClicked += (_, e) =>
            {
                _clickCount++;
                var normal = Vector3.Normalize(e.WorldPosition - planetCenter);
                SetPlanetMarkerFromNormal(normal, showLabel: true);
                SetSelection(_planetMarker);
            };
            _selectableObjects.Add(_earthModel);
            foreach (var part in _earthModel.ModelParts)
            {
                part.IsPickable = true;
                _selectableObjects.Add(part);
            }
        }
        else
        {
            _planet = AddSelectable(scene, new Sphere3D
            {
                Name = "Fallback procedural planet sphere",
                Radius = 1.25f,
                Segments = 96,
                Rings = 48,
                Position = planetCenter,
                Material = new Material3D
                {
                    BaseColor = new ColorRgba(0.08f, 0.30f, 0.80f, 1f),
                    Lighting = LightingMode.BlinnPhong,
                    SpecularStrength = 0.36f,
                    Shininess = 72f,
                    Roughness = 0.50f
                }
            });
            AddPlanetDetails(scene);
        }


        _planetMarker = AddSelectable(scene, new Sphere3D
        {
            Name = "Highlighted coordinate marker",
            Radius = 0.060f,
            Segments = 20,
            Rings = 10,
            Material = new Material3D
            {
                BaseColor = new ColorRgba(1f, 0.86f, 0.18f, 1f),
                Lighting = LightingMode.Unlit
            }
        });

        scene.AddLight(new PointLight3D
        {
            Position = new Vector3(-3.3f, 2.8f, -3.4f),
            Range = 10f,
            Intensity = 2.8f,
            Color = new ColorRgba(0.86f, 0.91f, 1f, 1f)
        });

        scene.AddLight(new DirectionalLight3D
        {
            Direction = Vector3.Normalize(new Vector3(-0.45f, -0.75f, 0.18f)),
            Intensity = 0.86f,
            Color = new ColorRgba(1f, 0.92f, 0.78f, 1f)
        });

        _planetFocusPoint = PlanetPointFromLatLon(24.5f, 38.0f);
        UpdatePlanetFocusMarker(0f);
        ShowPlanetLocationLabel(24.5f, 38.0f, _planetFocusPoint);
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
        scene.RenderPipeline.Mode = RenderPipelineMode3D.DeferredIfSupported;
        scene.RenderPipeline.EnableDeferredLighting = true;
        scene.RenderPipeline.EnableHdr = true;
        scene.RenderPipeline.EnableTransparentForwardPass = true;
        scene.RenderPipeline.Ssao.Enabled = true;
        scene.RenderPipeline.Ssao.Strength = 0.36f;
        scene.RenderPipeline.Ssao.Radius = 0.65f;
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
    }



    private void BuildBuildingWalkthroughScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(-4.8f, 1.56f, -6.6f);
        scene.Camera.Target = new Vector3(-4.2f, 1.45f, -5.6f);
        scene.Camera.FieldOfViewDegrees = 68f;
        scene.Camera.FarPlane = 180f;
        scene.AmbientLightIntensity = 0.42f;
        scene.Environment.Skybox.Mode = SkyboxMode3D.VerticalGradient;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.08f, 0.12f, 0.18f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.16f, 0.20f, 0.26f, 1f);
        scene.Environment.Skybox.BottomColor = new ColorRgba(0.025f, 0.030f, 0.038f, 1f);

        scene.PhysicsCore = null; // Building walkthrough uses KinematicCharacterController3D in Scene3DControl, not default rigidbody physics.

        scene.AddLight(new DirectionalLight3D
        {
            Direction = Vector3.Normalize(new Vector3(-0.42f, -0.72f, -0.34f)),
            Intensity = 1.05f,
            Color = new ColorRgba(1f, 0.95f, 0.86f, 1f)
        });
        scene.AddLight(new PointLight3D
        {
            Position = new Vector3(0f, 9.0f, -3.5f),
            Range = 22f,
            Intensity = 2.6f,
            Color = new ColorRgba(0.78f, 0.86f, 1f, 1f)
        });

        var concrete = Material3D.CreateLambert(new ColorRgba(0.36f, 0.39f, 0.43f, 1f));
        concrete.Roughness = 0.96f;
        var wallMat = Material3D.CreateLambert(new ColorRgba(0.72f, 0.75f, 0.72f, 1f));
        var glass = new Material3D
        {
            BaseColor = new ColorRgba(0.42f, 0.72f, 1f, 0.28f),
            Opacity = 0.28f,
            Surface = SurfaceMode.Transparent,
            Lighting = LightingMode.Phong,
            SpecularStrength = 0.55f,
            Shininess = 80f
        };
        var floorMat = Material3D.CreateLambert(new ColorRgba(0.30f, 0.33f, 0.37f, 1f));
        var accentA = Material3D.CreatePhong(new ColorRgba(0.20f, 0.48f, 0.86f, 1f), 0.30f, 48f);
        var accentB = Material3D.CreateLambert(new ColorRgba(0.84f, 0.58f, 0.28f, 1f));

        StaticBox(scene, "Outdoor site slab / walkable", 18f, 0.22f, 16f, new Vector3(0f, -0.11f, 0f), concrete, pickable: false);
        StaticBox(scene, "Building foundation", 12.8f, 0.30f, 10.8f, new Vector3(0.0f, 0.05f, 0.0f), floorMat, pickable: false);

        const int floors = 4;
        const float floorHeight = 2.45f;
        const float buildingWidth = 10.8f;
        const float buildingDepth = 8.6f;
        for (var f = 0; f < floors; f++)
        {
            var y = 0.22f + f * floorHeight;
            StaticBox(scene, $"Floor {f + 1} walkable slab", buildingWidth, 0.16f, buildingDepth, new Vector3(0f, y, 0f), floorMat, pickable: false);
            StaticBox(scene, $"Floor {f + 1} ceiling slab", buildingWidth, 0.12f, buildingDepth, new Vector3(0f, y + floorHeight - 0.10f, 0f), floorMat, pickable: false);

            StaticBox(scene, $"Floor {f + 1} north wall", buildingWidth, 1.85f, 0.16f, new Vector3(0f, y + 0.96f, buildingDepth * 0.5f), wallMat, pickable: false);
            if (f == 0)
            {
                // Front facade with a real walk-through entrance. Earlier versions used a
                // single solid south wall, so the Person camera started outside a sealed box.
                StaticBox(scene, "Ground floor south wall / left of entrance", 0.36f, 1.85f, 0.16f, new Vector3(-5.22f, y + 0.96f, -buildingDepth * 0.5f), wallMat, pickable: false);
                StaticBox(scene, "Ground floor south wall / right of entrance", 8.62f, 1.85f, 0.16f, new Vector3(1.10f, y + 0.96f, -buildingDepth * 0.5f), wallMat, pickable: false);
                StaticBox(scene, "Ground floor entrance lintel", 1.82f, 0.24f, 0.18f, new Vector3(-4.16f, y + 1.78f, -buildingDepth * 0.5f), wallMat, pickable: false);
                StaticBox(scene, "Open entrance threshold", 1.82f, 0.04f, 0.42f, new Vector3(-4.16f, y + 0.12f, -buildingDepth * 0.5f - 0.20f), accentB, pickable: true);
            }
            else
            {
                StaticBox(scene, $"Floor {f + 1} south wall", buildingWidth, 1.85f, 0.16f, new Vector3(0f, y + 0.96f, -buildingDepth * 0.5f), wallMat, pickable: false);
            }
            StaticBox(scene, $"Floor {f + 1} west wall", 0.16f, 1.85f, buildingDepth, new Vector3(-buildingWidth * 0.5f, y + 0.96f, 0f), wallMat, pickable: false);
            StaticBox(scene, $"Floor {f + 1} east glass wall", 0.10f, 1.75f, buildingDepth, new Vector3(buildingWidth * 0.5f, y + 1.0f, 0f), glass, pickable: false);

            for (var room = 0; room < 4; room++)
            {
                var x = -3.9f + room * 2.55f;
                StaticBox(scene, $"Floor {f + 1} office {room + 1} partition A", 0.08f, 1.55f, 3.0f, new Vector3(x + 1.08f, y + 0.84f, -1.82f), wallMat, pickable: false);
                StaticBox(scene, $"Floor {f + 1} office {room + 1} desk", 0.92f, 0.22f, 0.52f, new Vector3(x, y + 0.55f, -2.95f), accentA, pickable: true);
                StaticBox(scene, $"Floor {f + 1} office {room + 1} cabinet", 0.32f, 1.10f, 0.44f, new Vector3(x - 0.72f, y + 0.73f, -3.74f), accentB, pickable: true);
                var chair = scene.Add(new Cylinder3D
                {
                    Name = $"Floor {f + 1} office {room + 1} chair",
                    Radius = 0.22f,
                    Height = 0.34f,
                    Segments = 24,
                    Position = new Vector3(x + 0.12f, y + 0.36f, -2.36f),
                    Material = Material3D.CreatePhong(new ColorRgba(0.10f, 0.12f, 0.15f, 1f), 0.35f, 38f),
                    IsPickable = true
                });
                _selectableObjects.Add(chair);
            }

            // Low stair steps. Person collision can step over each riser but still collides with walls/floors.
            if (f < floors - 1)
            {
                const int stairSteps = 22;
                for (var s = 0; s < stairSteps; s++)
                {
                    var stepY = y + 0.08f + s * (floorHeight / stairSteps);
                    var stepZ = 3.45f - s * 0.18f;
                    StaticBox(scene, $"Stair {f + 1}->{f + 2} step {s + 1}", 1.25f, 0.105f, 0.32f, new Vector3(-4.65f, stepY, stepZ), concrete, pickable: false);
                }
            }
        }

        // Door opening guide / navigation landmarks.
        StaticBox(scene, "Reception counter", 2.2f, 0.72f, 0.55f, new Vector3(-2.6f, 0.60f, -3.82f), accentA, pickable: true);
        StaticBox(scene, "Server rack prop", 0.62f, 1.80f, 0.72f, new Vector3(3.9f, 0.98f, 2.95f), Material3D.CreatePhong(new ColorRgba(0.05f, 0.08f, 0.12f, 1f), 0.25f, 42f), pickable: true);
        StaticBox(scene, "Elevator shaft marker", 1.25f, floors * floorHeight, 1.25f, new Vector3(4.25f, floors * floorHeight * 0.5f, 3.3f), new Material3D { BaseColor = new ColorRgba(0.18f, 0.22f, 0.28f, 0.48f), Opacity = 0.48f, Surface = SurfaceMode.Transparent, Lighting = LightingMode.Lambert }, pickable: false);

        ResetBuildingPersonCamera();
    }

    private void ResetBuildingPersonCamera()
    {
        var scene = _sceneControl.Scene;
        scene.Camera.Position = new Vector3(-4.7f, 1.56f, -6.35f);
        scene.Camera.Target = new Vector3(-4.16f, 1.48f, -4.35f);
        scene.Camera.FieldOfViewDegrees = 68f;
        _sceneControl.ResetPersonNavigationState(grounded: false);
        _cameraFlight.Cancel();
    }

    private void BuildBridgeDigitalTwinScene(Scene3D scene)
    {
        scene.Camera.Position = new Vector3(9.5f, 5.4f, -12.0f);
        scene.Camera.Target = new Vector3(0f, 1.4f, 0f);
        scene.Camera.FarPlane = 220f;
        scene.AmbientLightIntensity = 0.32f;
        scene.Environment.Skybox.Mode = SkyboxMode3D.VerticalGradient;
        scene.Environment.Skybox.TopColor = new ColorRgba(0.06f, 0.10f, 0.16f, 1f);
        scene.Environment.Skybox.HorizonColor = new ColorRgba(0.18f, 0.24f, 0.31f, 1f);
        scene.Environment.Skybox.BottomColor = new ColorRgba(0.02f, 0.03f, 0.04f, 1f);

        scene.AddLight(new DirectionalLight3D
        {
            Direction = Vector3.Normalize(new Vector3(-0.32f, -0.82f, -0.36f)),
            Intensity = 1.12f,
            Color = new ColorRgba(1f, 0.92f, 0.80f, 1f)
        });
        scene.AddLight(new PointLight3D
        {
            Position = new Vector3(0f, 6.6f, -4.0f),
            Range = 24f,
            Intensity = 2.1f,
            Color = new ColorRgba(0.55f, 0.75f, 1f, 1f)
        });

        var water = new Material3D
        {
            BaseColor = new ColorRgba(0.04f, 0.18f, 0.30f, 0.70f),
            Opacity = 0.70f,
            Surface = SurfaceMode.Transparent,
            Lighting = LightingMode.Phong,
            SpecularStrength = 0.62f,
            Shininess = 96f
        };
        var steel = Material3D.CreatePhong(new ColorRgba(0.46f, 0.50f, 0.56f, 1f), 0.44f, 64f);
        var deckMat = Material3D.CreateLambert(new ColorRgba(0.22f, 0.23f, 0.24f, 1f));
        var towerMat = Material3D.CreatePhong(new ColorRgba(0.66f, 0.70f, 0.74f, 1f), 0.32f, 50f);

        StaticBox(scene, "River water plane", 22f, 0.045f, 15f, new Vector3(0f, -0.035f, 0f), water, pickable: false);
        StaticBox(scene, "West embankment", 7f, 0.30f, 15f, new Vector3(-10.8f, 0.08f, 0f), Material3D.CreateLambert(new ColorRgba(0.28f, 0.31f, 0.30f, 1f)), pickable: false);
        StaticBox(scene, "East embankment", 7f, 0.30f, 15f, new Vector3(10.8f, 0.08f, 0f), Material3D.CreateLambert(new ColorRgba(0.28f, 0.31f, 0.30f, 1f)), pickable: false);

        StaticBox(scene, "Fixed west approach deck", 6.2f, 0.26f, 2.5f, new Vector3(-6.8f, 0.78f, 0f), deckMat, pickable: true);
        StaticBox(scene, "Fixed east approach deck", 6.2f, 0.26f, 2.5f, new Vector3(6.8f, 0.78f, 0f), deckMat, pickable: true);
        StaticBox(scene, "Left bascule leaf / raised 18 deg", 4.0f, 0.24f, 2.42f, new Vector3(-2.15f, 1.12f, 0f), deckMat, new Vector3(0f, 0f, -18f), pickable: true);
        StaticBox(scene, "Right bascule leaf / raised 18 deg", 4.0f, 0.24f, 2.42f, new Vector3(2.15f, 1.12f, 0f), deckMat, new Vector3(0f, 0f, 18f), pickable: true);

        for (var side = -1; side <= 1; side += 2)
        {
            StaticBox(scene, side < 0 ? "West drawbridge tower A" : "East drawbridge tower A", 0.58f, 4.6f, 0.58f, new Vector3(side * 4.0f, 2.55f, -1.55f), towerMat, pickable: true);
            StaticBox(scene, side < 0 ? "West drawbridge tower B" : "East drawbridge tower B", 0.58f, 4.6f, 0.58f, new Vector3(side * 4.0f, 2.55f, 1.55f), towerMat, pickable: true);
            StaticBox(scene, side < 0 ? "West tower crossbeam" : "East tower crossbeam", 0.76f, 0.35f, 3.65f, new Vector3(side * 4.0f, 4.85f, 0f), steel, pickable: true);
            for (var z = -1; z <= 1; z += 2)
            {
                var cable = scene.Add(new Cylinder3D
                {
                    Name = side < 0 ? "West counterweight cable" : "East counterweight cable",
                    Radius = 0.035f,
                    Height = 4.2f,
                    Segments = 16,
                    Position = new Vector3(side * 2.85f, 2.95f, z * 1.55f),
                    RotationDegrees = new Vector3(0f, 0f, side < 0 ? -32f : 32f),
                    Material = steel,
                    IsPickable = true
                });
                _selectableObjects.Add(cable);
            }
        }

        // Vehicles and service assets.
        for (var i = 0; i < 6; i++)
        {
            var x = -8.2f + i * 3.1f;
            StaticBox(scene, "Traffic vehicle " + (i + 1).ToString(CultureInfo.InvariantCulture), 0.76f, 0.42f, 0.52f, new Vector3(x, 1.14f, i % 2 == 0 ? -0.58f : 0.62f), Material3D.CreatePhong(ColorFromHue(0.02f + i * 0.12f), 0.30f, 46f), pickable: true);
        }

        AddBridgeSensor(scene, "S-01 hinge torque west", new Vector3(-4.06f, 1.22f, -1.62f), "hinge torque: 41 kNm");
        AddBridgeSensor(scene, "S-02 hinge torque east", new Vector3(4.06f, 1.22f, 1.62f), "hinge torque: 39 kNm");
        AddBridgeSensor(scene, "S-03 leaf angle left", new Vector3(-1.45f, 1.52f, 1.38f), "leaf angle: 18.2°");
        AddBridgeSensor(scene, "S-04 leaf angle right", new Vector3(1.45f, 1.52f, -1.38f), "leaf angle: 18.0°");
        AddBridgeSensor(scene, "S-05 vibration tower W", new Vector3(-4.05f, 3.72f, -1.55f), "vibration: 0.12 g");
        AddBridgeSensor(scene, "S-06 vibration tower E", new Vector3(4.05f, 3.72f, 1.55f), "vibration: 0.10 g");
        AddBridgeSensor(scene, "S-07 wind sensor", new Vector3(0f, 5.45f, 0f), "wind: 7.4 m/s");
        AddBridgeSensor(scene, "S-08 bearing temperature", new Vector3(-3.90f, 0.98f, 1.62f), "bearing temp: 48°C");
        AddBridgeSensor(scene, "S-09 deck strain", new Vector3(0f, 1.28f, -1.30f), "strain: 214 με");
        AddBridgeSensor(scene, "S-10 water level", new Vector3(0f, 0.32f, 2.65f), "water level: +0.8 m");
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

    private void AddBridgeSensor(Scene3D scene, string name, Vector3 position, string telemetry)
    {
        var sensor = scene.Add(new Sphere3D
        {
            Name = name + " / " + telemetry,
            Radius = 0.115f,
            Segments = 20,
            Rings = 10,
            Position = position,
            Material = new Material3D
            {
                BaseColor = new ColorRgba(0.16f, 1.0f, 0.42f, 1f),
                Lighting = LightingMode.Unlit
            },
            IsPickable = true,
            IsManipulationEnabled = false
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
        _statusText.Text = "Bridge sensor selected:\n" + sensor.Name;
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
        scene.RenderPipeline.Mode = RenderPipelineMode3D.DeferredIfSupported;
        scene.RenderPipeline.EnableDeferredLighting = true;
        scene.RenderPipeline.EnableHdr = true;
        scene.RenderPipeline.EnableTransparentForwardPass = true;
        scene.RenderPipeline.EnableMotionVectorMetadata = true;
        scene.RenderPipeline.Ssao.Enabled = true;
        scene.RenderPipeline.Ssao.Strength = 0.45f;
        scene.RenderPipeline.Ssao.Radius = 0.65f;
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
            Name = "HDR/SSAO sample sphere",
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
        scene.Environment.DirectionalShadows.IsEnabled = _shadowsCheck.IsChecked == true;
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
                    _rotatingBox.Material = Material3D.CreatePhong(ColorFromHue((_embeddedCounter % 6) / 6f), 0.55f, 72f);
                }
                break;
            case DemoSceneKind.PickingAndInteraction:
                SelectNextObject();
                break;
            case DemoSceneKind.RenderPipelineAndDiagnostics:
                _wireframeCheck.IsChecked = _wireframeCheck.IsChecked != true;
                ApplyRuntimeToggles();
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

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = _lastFrameTime <= 0d ? 1d / 60d : System.Math.Clamp(now - _lastFrameTime, 0.001d, 0.05d);
        _lastFrameTime = now;

        if (_animateCheck.IsChecked == true)
        {
            AnimateScene((float)now, (float)dt);
        }

        UpdateStatus();
    }

    private void AnimateScene(float t, float dt)
    {
        var scene = _sceneControl.Scene;
        using (scene.BeginUpdate())
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

            if (_particleSystem is not null)
            {
                _particleSystem.Advance(dt);
            }
            foreach (var particleSystem in _particleSystems)
            {
                if (!ReferenceEquals(particleSystem, _particleSystem))
                {
                    particleSystem.Advance(dt);
                }
            }

            if (_activeDemo == DemoSceneKind.Physics)
            {
                scene.StepPhysics(dt);
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
                _ssaoBox = TextInput("0.36");
                _demoSpecificPanel.Children.Add(Label("Exposure"));
                _demoSpecificPanel.Children.Add(_exposureBox);
                _demoSpecificPanel.Children.Add(Label("Gamma"));
                _demoSpecificPanel.Children.Add(_gammaBox);
                _demoSpecificPanel.Children.Add(Label("Ambient intensity"));
                _demoSpecificPanel.Children.Add(_ambientBox);
                _demoSpecificPanel.Children.Add(Label("SSAO strength"));
                _demoSpecificPanel.Children.Add(_ssaoBox);
                _demoSpecificPanel.Children.Add(Button("Apply shader/light settings", ApplyShaderLabValues));
                _demoSpecificPanel.Children.Add(Button("Warm sunset preset", () => SetShaderPreset(1.22f, 2.18f, 0.18f, 0.52f, new ColorRgba(1f, 0.72f, 0.48f, 1f))));
                _demoSpecificPanel.Children.Add(Button("Cold studio preset", () => SetShaderPreset(0.92f, 2.25f, 0.38f, 0.22f, new ColorRgba(0.70f, 0.82f, 1f, 1f))));
                break;
            case DemoSceneKind.ImportedGlbModel:
                if (_doctorWatsonSequence is not null)
                {
                    _demoSpecificPanel.Children.Add(Button("Restart authored GLB clip", AdvanceCharacterAnimationPhase));
                    _demoSpecificPanel.Children.Add(Note("В этой версии Doctor Watson GLB содержит skeleton, skin weights и authored clip. Движок выполняет CPU skinning, поэтому модель реально сгибается по костям. Произвольные wave/jump/squat требуют отдельных authored clips или отдельного animation-authoring слоя."));
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
            _doctorWatsonSequence.Advance(dt);
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
        UpdatePlanetFocusMarker((float)_clock.Elapsed.TotalSeconds);
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
        UpdatePlanetFocusMarker((float)_clock.Elapsed.TotalSeconds);
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
            Background = new SolidColorBrush(Color.FromArgb(226, 8, 12, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(72, 148, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = label, Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 13 },
                    new TextBlock { Text = $"lat {lat:0.##}, lon {lon:0.##}", Foreground = new SolidColorBrush(Color.FromRgb(188, 208, 238)), FontSize = 11 }
                }
            }
        };

        _planetLabel = scene.Add(new ControlPlane3D(panel)
        {
            Name = "Planet location label / " + label,
            Width = 1.18f,
            Height = 0.38f,
            AlwaysFaceCamera = true,
            IsPickable = false,
            IsManipulationEnabled = false
        });
        _planetLabel.Position = _planet.Position + Vector3.Normalize(normal) * (_planet.Radius + 0.38f);
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
        scene.RenderPipeline.Ssao.Strength = global::System.Math.Clamp(ParseBox(_ssaoBox, scene.RenderPipeline.Ssao.Strength), 0f, 2.0f);

        if (_exposureBox is not null) _exposureBox.Text = scene.RenderPipeline.ToneMapping.Exposure.ToString("0.###", CultureInfo.InvariantCulture);
        if (_gammaBox is not null) _gammaBox.Text = scene.RenderPipeline.ToneMapping.Gamma.ToString("0.###", CultureInfo.InvariantCulture);
        if (_ambientBox is not null) _ambientBox.Text = scene.AmbientLightIntensity.ToString("0.###", CultureInfo.InvariantCulture);
        if (_ssaoBox is not null) _ssaoBox.Text = scene.RenderPipeline.Ssao.Strength.ToString("0.###", CultureInfo.InvariantCulture);
        UpdateStatus(force: true);
    }

    private void SetShaderPreset(float exposure, float gamma, float ambient, float ssao, ColorRgba sunColor)
    {
        if (_exposureBox is not null) _exposureBox.Text = exposure.ToString("0.###", CultureInfo.InvariantCulture);
        if (_gammaBox is not null) _gammaBox.Text = gamma.ToString("0.###", CultureInfo.InvariantCulture);
        if (_ambientBox is not null) _ambientBox.Text = ambient.ToString("0.###", CultureInfo.InvariantCulture);
        if (_ssaoBox is not null) _ssaoBox.Text = ssao.ToString("0.###", CultureInfo.InvariantCulture);
        if (_shaderSun is not null) _shaderSun.Color = sunColor;
        ApplyShaderLabValues();
    }

    private static float ParseBox(TextBox? box, float fallback)
        => float.TryParse(box?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

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

            obj.Position = new Vector3(-0.15f + i * 0.22f, 2.7f + i * 0.28f, -0.55f + i * 0.18f);
            obj.RotationDegrees = Vector3.Zero;
            obj.Rigidbody.Velocity = new Vector3(0.22f + i * 0.08f, 0f, 0.07f - i * 0.03f);
            obj.Rigidbody.AngularVelocity = new Vector3(0.10f + i * 0.08f, 0.05f, 0.38f + i * 0.12f);
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
        _selectionText.Text = _selectedObject is null
            ? "Selection: none. Click a pickable object in the current demo."
            : $"Selection: {_selectedObject.Name}\nPosition: {FormatVector(_selectedObject.Position)}\nClicks: {_clickCount.ToString(CultureInfo.InvariantCulture)}";
    }

    private void OnFrameRendered(object? sender, SceneFrameRenderedEventArgs e)
    {
        _lastFrame = e;
        _lastFps = e.FrameMilliseconds > 0d ? 1000d / e.FrameMilliseconds : 0d;
        _backendText.Text = $"Backend: {e.Kind} | frame {e.FrameMilliseconds:0.00} ms";
    }

    private void UpdateStatus(bool force = false)
    {
        var now = Stopwatch.GetTimestamp();
        if (!force && _lastStatusTicks != 0 && (now - _lastStatusTicks) < Stopwatch.Frequency / 4)
        {
            return;
        }
        _lastStatusTicks = now;

        var stats = _lastFrame?.Stats ?? new RenderStats();
        _statusText.Text =
            $"Demo: {GetDefinition(_activeDemo).Title}\n" +
            $"FPS: {_lastFps:0.0}\n" +
            $"Objects: {stats.ObjectCount:n0} | Renderables: {stats.RenderableCount:n0} | Pickables: {stats.PickableCount:n0}\n" +
            $"HighScale: {stats.HighScaleInstanceCount:n0} instances | chunks {stats.VisibleChunkCount:n0}/{stats.TotalChunkCount:n0}\n" +
            $"Draw: {stats.DrawCallCount:n0} calls | batches {stats.InstancedBatchCount:n0} | triangles {stats.TriangleCount:n0}\n" +
            $"Pipeline: {stats.RenderPipelineMode} | HDR {OnOff(stats.HdrActive)} | SSAO {OnOff(stats.SsaoActive)} | shadows {OnOff(stats.DirectionalShadowEnabled)}\n" +
            $"Particles: {stats.ParticleCount:n0} | control planes: {stats.ControlPlaneCount:n0}\n" +
            $"Smooth high-scale motion: {OnOff(_sceneControl.Scene.Performance.EnableWebGlClientGpuTransformAnimation)} | overlay: {OnOff(_sceneControl.ShowPerformanceMetrics)}" +
            (_activeDemo == DemoSceneKind.ImportedGlbModel && !string.IsNullOrWhiteSpace(_doctorWatsonImportInfo)
                ? "\n" + _doctorWatsonImportInfo
                : string.Empty);
    }

    private static IEnumerable<Matrix4x4> CreateRackTransforms(int columns, int rows, float spacingX, float spacingZ)
    {
        var offsetX = (columns - 1) * spacingX * 0.5f;
        var offsetZ = (rows - 1) * spacingZ * 0.5f;
        for (var z = 0; z < rows; z++)
        {
            for (var x = 0; x < columns; x++)
            {
                var y = (x + z) % 3 == 0 ? 0.06f : 0f;
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
