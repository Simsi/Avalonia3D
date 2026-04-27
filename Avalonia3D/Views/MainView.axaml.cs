using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace Avalonia3D.Views;

public partial class MainView : UserControl
{
    private readonly Scene3DControl _sceneControl;
    private readonly TextBlock _statusText;
    private readonly DispatcherTimer _telemetryTimer;
    private readonly DispatcherTimer _logTimer;
    private readonly Stopwatch _runClock = Stopwatch.StartNew();
    private readonly Random _random = new(42);

    private ComboBox _scenarioBox = null!;
    private TextBox _instanceText = null!;
    private TextBox _proxyText = null!;
    private TextBox _telemetryText = null!;
    private TextBox _chunkText = null!;
    private TextBox _columnsText = null!;
    private TextBox _spacingText = null!;
    private TextBox _detailedLodText = null!;
    private TextBox _simplifiedLodText = null!;
    private TextBox _proxyLodText = null!;
    private TextBox _logIntervalText = null!;
    private CheckBox _overlayCheck = null!;
    private CheckBox _loggingCheck = null!;
    private CheckBox _telemetryCheck = null!;
    private CheckBox _animateCheck = null!;

    private HighScaleInstanceLayer3D? _layer;
    private string _scenarioName = "Simple markers";
    private int _configuredInstances;
    private int _configuredProxies;
    private int _configuredTelemetryPerSecond;
    private int _telemetryCursor;
    private int _telemetryAppliedInWindow;
    private int _lastTelemetryAppliedPerSecond;
    private long _lastTelemetrySecondTicks;
    private StreamWriter? _csv;
    private string? _csvPath;
    private SceneFrameRenderedEventArgs? _lastFrame;
    private double _lastFps;
    private double _lastAverageFrameMs;
    private int _frameCountWindow;
    private double _frameMsWindow;
    private long _lastFrameWindowTicks;
    private long _lastAllocatedBytes;
    private long _lastAllocationTicks;
    private double _allocatedMbPerSecond;

    public MainView()
    {
        InitializeComponent();

        _sceneControl = new Scene3DControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ShowPerformanceMetrics = true,
            EnableSceneNavigation = true,
            Width = double.NaN,
            Height = double.NaN
        };
        _sceneControl.FrameRendered += OnFrameRendered;

        _statusText = new TextBlock
        {
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 12,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        };

        _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _telemetryTimer.Tick += OnTelemetryTick;
        _telemetryTimer.Start();

        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _logTimer.Tick += OnLogTick;
        _logTimer.Start();

        BuildUi();
        ApplyPreset(10_000, 0, 0, "Simple markers");
        RebuildBenchmarkScene();
    }

    private void BuildUi()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(360)));

        root.Children.Add(_sceneControl);

        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 20)),
            Padding = new Thickness(12),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = BuildControlPanel()
        };
        Grid.SetColumn(panel, 1);
        root.Children.Add(panel);

        ContentGrid.Children.Clear();
        ContentGrid.Children.Add(root);
    }

    private Control BuildControlPanel()
    {
        _scenarioBox = new ComboBox
        {
            ItemsSource = new[] { "Simple markers", "Rack composite" },
            SelectedIndex = 0
        };
        _instanceText = TextBox("10000");
        _proxyText = TextBox("0");
        _telemetryText = TextBox("0");
        _chunkText = TextBox("32");
        _columnsText = TextBox("316");
        _spacingText = TextBox("1.4");
        _detailedLodText = TextBox("24");
        _simplifiedLodText = TextBox("96");
        _proxyLodText = TextBox("320");
        _logIntervalText = TextBox("1");
        _overlayCheck = new CheckBox { Content = "Overlay", IsChecked = true, Foreground = Brushes.White };
        _loggingCheck = new CheckBox { Content = "CSV logging", IsChecked = true, Foreground = Brushes.White };
        _telemetryCheck = new CheckBox { Content = "Telemetry updates", IsChecked = true, Foreground = Brushes.White };
        _animateCheck = new CheckBox { Content = "Transform animation", IsChecked = false, Foreground = Brushes.White };
        _overlayCheck.IsCheckedChanged += (_, _) => _sceneControl.ShowPerformanceMetrics = _overlayCheck.IsChecked == true;

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = "3DEngine Highload Benchmark",
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 16
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Runtime-load test. Change values and press Apply. CSV is written to Documents/Avalonia3D/Benchmarks.",
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap
        });

        stack.Children.Add(Row("Scenario", _scenarioBox));
        stack.Children.Add(Row("HighScale instances", _instanceText));
        stack.Children.Add(Row("Interactive proxies", _proxyText));
        stack.Children.Add(Row("Telemetry / sec", _telemetryText));
        stack.Children.Add(Row("Chunk size", _chunkText));
        stack.Children.Add(Row("Grid columns", _columnsText));
        stack.Children.Add(Row("Spacing", _spacingText));
        stack.Children.Add(Row("LOD detailed", _detailedLodText));
        stack.Children.Add(Row("LOD simplified", _simplifiedLodText));
        stack.Children.Add(Row("LOD proxy", _proxyLodText));
        stack.Children.Add(Row("CSV interval sec", _logIntervalText));
        stack.Children.Add(_overlayCheck);
        stack.Children.Add(_loggingCheck);
        stack.Children.Add(_telemetryCheck);
        stack.Children.Add(_animateCheck);

        var apply = new Button { Content = "Apply / Rebuild scene" };
        apply.Click += (_, _) => RebuildBenchmarkScene();
        stack.Children.Add(apply);

        stack.Children.Add(PresetRow(
            Button("10k simple", () => ApplyPreset(10_000, 0, 0, "Simple markers")),
            Button("100k simple", () => ApplyPreset(100_000, 0, 0, "Simple markers")),
            Button("1M simple", () => ApplyPreset(1_000_000, 0, 0, "Simple markers"))));

        stack.Children.Add(PresetRow(
            Button("10k proxies", () => ApplyPreset(100_000, 10_000, 0, "Simple markers")),
            Button("10k racks", () => ApplyPreset(10_000, 0, 50_000, "Rack composite")),
            Button("100k racks", () => ApplyPreset(100_000, 0, 100_000, "Rack composite"))));

        var openLog = new Button { Content = "Open log folder" };
        openLog.Click += (_, _) => OpenLogFolder();
        stack.Children.Add(openLog);

        stack.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            Padding = new Thickness(8),
            Child = _statusText
        });

        return new ScrollViewer { Content = stack };
    }

    private void ApplyPreset(int instances, int proxies, int telemetryPerSecond, string scenario)
    {
        _scenarioBox.SelectedIndex = scenario == "Rack composite" ? 1 : 0;
        _instanceText.Text = instances.ToString(CultureInfo.InvariantCulture);
        _proxyText.Text = proxies.ToString(CultureInfo.InvariantCulture);
        _telemetryText.Text = telemetryPerSecond.ToString(CultureInfo.InvariantCulture);
        _columnsText.Text = Math.Max(1, (int)Math.Sqrt(instances)).ToString(CultureInfo.InvariantCulture);
        RebuildBenchmarkScene();
    }

    private void RebuildBenchmarkScene()
    {
        CloseCsv();
        _scenarioName = _scenarioBox.SelectedIndex == 1 ? "Rack composite" : "Simple markers";
        _configuredInstances = Clamp(ParseInt(_instanceText.Text, 10_000), 0, 1_500_000);
        _configuredProxies = Clamp(ParseInt(_proxyText.Text, 0), 0, 100_000);
        _configuredTelemetryPerSecond = Clamp(ParseInt(_telemetryText.Text, 0), 0, 200_000);
        var chunkSize = Math.Max(2f, ParseFloat(_chunkText.Text, 32f));
        var columns = Math.Max(1, ParseInt(_columnsText.Text, 316));
        var spacing = Math.Max(0.05f, ParseFloat(_spacingText.Text, 1.4f));
        var detailed = Math.Max(1f, ParseFloat(_detailedLodText.Text, 24f));
        var simplified = Math.Max(detailed + 1f, ParseFloat(_simplifiedLodText.Text, 96f));
        var proxy = Math.Max(simplified + 1f, ParseFloat(_proxyLodText.Text, 320f));

        var scene = _sceneControl.Scene;
        using (scene.BeginUpdate())
        {
            scene.Clear();
            scene.BackgroundColor = new ColorRgba(0.04f, 0.045f, 0.055f, 1f);
            scene.Camera.Position = new Vector3(40, 35, -70);
            scene.Camera.Target = Vector3.Zero;
            scene.AddLight(new DirectionalLight3D
            {
                Direction = Vector3.Normalize(new Vector3(-0.35f, -0.85f, -0.35f)),
                Intensity = 1.35f,
                Color = ColorRgba.White
            });
            scene.AddLight(new DirectionalLight3D
            {
                Direction = Vector3.Normalize(new Vector3(0.4f, -0.35f, 0.6f)),
                Intensity = 0.55f,
                Color = new ColorRgba(0.65f, 0.75f, 1f, 1f)
            });

            var template = _scenarioName == "Rack composite"
                ? HighScaleTemplateCompiler.Compile(1, new BenchmarkRack3D())
                : HighScaleTemplateCompiler.Compile(1, new BenchmarkMarker3D());
            template.AddMaterialVariant(1, "Warning").DefaultColor = new ColorRgba(0.95f, 0.72f, 0.20f, 1f);
            template.AddMaterialVariant(2, "Critical").DefaultColor = new ColorRgba(0.95f, 0.16f, 0.12f, 1f);
            template.AddMaterialVariant(3, "Offline").DefaultColor = new ColorRgba(0.22f, 0.24f, 0.28f, 0.55f);

            var layer = new HighScaleInstanceLayer3D(template, Math.Max(1, _configuredInstances), chunkSize)
            {
                Name = "Benchmark HighScale Layer"
            };
            layer.LodPolicy.DetailedDistance = detailed;
            layer.LodPolicy.SimplifiedDistance = simplified;
            layer.LodPolicy.ProxyDistance = proxy;
            layer.AddInstances(CreateTransforms(_configuredInstances, columns, spacing));
            scene.Add(layer);
            _layer = layer;

            CreateInteractiveProxies(scene, _configuredProxies, columns, spacing);
        }

        _telemetryCursor = 0;
        _telemetryAppliedInWindow = 0;
        _lastTelemetryAppliedPerSecond = 0;
        _lastTelemetrySecondTicks = Stopwatch.GetTimestamp();
        ResetPerfWindows();
        if (_loggingCheck.IsChecked == true)
        {
            OpenCsv();
        }
        UpdateStatus();
    }

    private static System.Collections.Generic.IEnumerable<Matrix4x4> CreateTransforms(int count, int columns, float spacing)
    {
        var rows = Math.Max(1, (count + columns - 1) / columns);
        var offsetX = columns * spacing * 0.5f;
        var offsetZ = rows * spacing * 0.5f;
        for (var i = 0; i < count; i++)
        {
            var x = i % columns;
            var z = i / columns;
            yield return Matrix4x4.CreateTranslation(x * spacing - offsetX, 0f, z * spacing - offsetZ);
        }
    }

    private static void CreateInteractiveProxies(Scene3D scene, int count, int columns, float spacing)
    {
        if (count <= 0)
        {
            return;
        }

        var rows = Math.Max(1, (count + columns - 1) / columns);
        var offsetX = columns * spacing * 0.5f;
        var offsetZ = rows * spacing * 0.5f;
        var proxyMaterial = new Material3D
        {
            BaseColor = new ColorRgba(0.10f, 0.45f, 0.95f, 0.35f),
            Opacity = 0.35f,
            Surface = SurfaceMode.Transparent,
            Lighting = LightingMode.Unlit
        };

        for (var i = 0; i < count; i++)
        {
            var x = i % columns;
            var z = i / columns;
            scene.Add(new Box3D
            {
                Name = "InteractiveProxy" + i.ToString(CultureInfo.InvariantCulture),
                Width = 0.35f,
                Height = 0.35f,
                Depth = 0.35f,
                Position = new Vector3(x * spacing - offsetX, 0.55f, z * spacing - offsetZ),
                Material = proxyMaterial,
                IsPickable = true,
                IsManipulationEnabled = false
            });
        }
    }

    private void OnTelemetryTick(object? sender, EventArgs e)
    {
        var layer = _layer;
        if (layer is null || _telemetryCheck.IsChecked != true || layer.Instances.Count == 0)
        {
            return;
        }

        var nowTicks = Stopwatch.GetTimestamp();
        if (_lastTelemetrySecondTicks == 0)
        {
            _lastTelemetrySecondTicks = nowTicks;
        }

        var updates = Math.Max(0, (int)(_configuredTelemetryPerSecond * _telemetryTimer.Interval.TotalSeconds));
        if (updates == 0)
        {
            return;
        }

        var animate = _animateCheck.IsChecked == true;
        var count = layer.Instances.Count;
        var time = (float)_runClock.Elapsed.TotalSeconds;
        using (var batch = layer.BeginTelemetryBatch())
        {
            for (var i = 0; i < updates; i++)
            {
                var index = _telemetryCursor++ % count;
                var variant = (index + _random.Next(4)) & 3;
                batch.SetMaterialVariant(index, variant);
                if (animate)
                {
                    var r = layer.Instances[index];
                    var dx = MathF.Sin(time + index * 0.001f) * 0.015f;
                    var dz = MathF.Cos(time * 0.7f + index * 0.0017f) * 0.015f;
                    var t = r.Transform;
                    t.M41 += dx;
                    t.M43 += dz;
                    batch.SetTransform(index, t);
                }
            }
        }

        _telemetryAppliedInWindow += updates;
        var elapsed = (nowTicks - _lastTelemetrySecondTicks) / (double)Stopwatch.Frequency;
        if (elapsed >= 1d)
        {
            _lastTelemetryAppliedPerSecond = (int)(_telemetryAppliedInWindow / elapsed);
            _telemetryAppliedInWindow = 0;
            _lastTelemetrySecondTicks = nowTicks;
            UpdateStatus();
        }
    }

    private void OnFrameRendered(object? sender, SceneFrameRenderedEventArgs e)
    {
        _lastFrame = e;
        _frameCountWindow++;
        _frameMsWindow += e.FrameMilliseconds;

        var nowTicks = Stopwatch.GetTimestamp();
        if (_lastFrameWindowTicks == 0)
        {
            _lastFrameWindowTicks = nowTicks;
        }

        var elapsed = (nowTicks - _lastFrameWindowTicks) / (double)Stopwatch.Frequency;
        if (elapsed >= 1d)
        {
            _lastFps = _frameCountWindow / elapsed;
            _lastAverageFrameMs = _frameMsWindow / Math.Max(1, _frameCountWindow);
            _frameCountWindow = 0;
            _frameMsWindow = 0d;
            _lastFrameWindowTicks = nowTicks;
            UpdateAllocationRate(nowTicks);
            UpdateStatus();
        }
    }

    private void UpdateAllocationRate(long nowTicks)
    {
        var allocated = GC.GetTotalAllocatedBytes(false);
        if (_lastAllocationTicks != 0)
        {
            var seconds = (nowTicks - _lastAllocationTicks) / (double)Stopwatch.Frequency;
            if (seconds > 0)
            {
                _allocatedMbPerSecond = (allocated - _lastAllocatedBytes) / (1024d * 1024d) / seconds;
            }
        }

        _lastAllocatedBytes = allocated;
        _lastAllocationTicks = nowTicks;
    }

    private void OnLogTick(object? sender, EventArgs e)
    {
        var interval = Math.Max(1, ParseInt(_logIntervalText.Text, 1));
        _logTimer.Interval = TimeSpan.FromSeconds(interval);
        if (_loggingCheck.IsChecked == true && _csv is null)
        {
            try
            {
                OpenCsv();
            }
            catch (Exception ex)
            {
                CloseCsv();
                _loggingCheck.IsChecked = false;
                _statusText.Text = "CSV logging failed: " + ex.Message;
                return;
            }
        }
        else if (_loggingCheck.IsChecked != true && _csv is not null)
        {
            CloseCsv();
        }

        if (_csv is not null)
        {
            try
            {
                WriteCsvSample();
            }
            catch (Exception ex)
            {
                CloseCsv();
                _loggingCheck.IsChecked = false;
                _statusText.Text = "CSV write failed: " + ex.Message;
            }
        }
    }

    private void OpenCsv()
    {
        CloseCsv();

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Avalonia3D", "Benchmarks");
        Directory.CreateDirectory(dir);

        var processId = Process.GetCurrentProcess().Id;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        Exception? lastError = null;

        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : "_" + attempt.ToString("000", CultureInfo.InvariantCulture);
            var path = Path.Combine(dir, $"benchmark_{timestamp}_p{processId}{suffix}.csv");

            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);

                _csvPath = path;
                _csv = new StreamWriter(stream);
                _csv.WriteLine("time_s,scenario,instances,interactive_proxies,telemetry_target_s,telemetry_applied_s,fps,frame_ms,avg_frame_ms,alloc_mb_s,objects,renderables,pickables,colliders,highscale_instances,total_chunks,visible_chunks,culled,lod_simplified,lod_proxy,lod_billboard,draw_calls,batches,triangles,instance_upload_mb,backend_ms,packet_ms,serialize_ms,upload_ms,picking_ms,physics_ms,live_ms,mesh_cache,registry_version");
                _csv.Flush();
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
        }

        throw new IOException("Could not create a unique benchmark CSV file.", lastError);
    }

    private void CloseCsv()
    {
        _csv?.Dispose();
        _csv = null;
    }

    private void WriteCsvSample()
    {
        var frame = _lastFrame;
        var stats = frame?.Stats ?? RenderStats.Empty;
        _csv?.WriteLine(string.Join(",",
            F(_runClock.Elapsed.TotalSeconds),
            Q(_scenarioName),
            _configuredInstances.ToString(CultureInfo.InvariantCulture),
            _configuredProxies.ToString(CultureInfo.InvariantCulture),
            _configuredTelemetryPerSecond.ToString(CultureInfo.InvariantCulture),
            _lastTelemetryAppliedPerSecond.ToString(CultureInfo.InvariantCulture),
            F(_lastFps),
            F(frame?.FrameMilliseconds ?? 0),
            F(_lastAverageFrameMs),
            F(_allocatedMbPerSecond),
            stats.ObjectCount.ToString(CultureInfo.InvariantCulture),
            stats.RenderableCount.ToString(CultureInfo.InvariantCulture),
            stats.PickableCount.ToString(CultureInfo.InvariantCulture),
            stats.ColliderCount.ToString(CultureInfo.InvariantCulture),
            stats.HighScaleInstanceCount.ToString(CultureInfo.InvariantCulture),
            stats.TotalChunkCount.ToString(CultureInfo.InvariantCulture),
            stats.VisibleChunkCount.ToString(CultureInfo.InvariantCulture),
            stats.CulledObjectCount.ToString(CultureInfo.InvariantCulture),
            stats.LodSimplifiedCount.ToString(CultureInfo.InvariantCulture),
            stats.LodProxyCount.ToString(CultureInfo.InvariantCulture),
            stats.LodBillboardCount.ToString(CultureInfo.InvariantCulture),
            stats.DrawCallCount.ToString(CultureInfo.InvariantCulture),
            stats.InstancedBatchCount.ToString(CultureInfo.InvariantCulture),
            stats.TriangleCount.ToString(CultureInfo.InvariantCulture),
            F(stats.InstanceUploadBytes / (1024d * 1024d)),
            F(stats.BackendMilliseconds),
            F(stats.PacketBuildMilliseconds),
            F(stats.SerializationMilliseconds),
            F(stats.UploadMilliseconds),
            F(stats.PickingMilliseconds),
            F(stats.PhysicsMilliseconds),
            F(stats.LiveSnapshotMilliseconds),
            stats.MeshCacheCount.ToString(CultureInfo.InvariantCulture),
            stats.RegistryVersion.ToString(CultureInfo.InvariantCulture)));
        _csv?.Flush();
    }

    private void UpdateStatus()
    {
        var frame = _lastFrame;
        var stats = frame?.Stats ?? RenderStats.Empty;
        _statusText.Text =
            $"Scenario: {_scenarioName}\n" +
            $"Instances: {_configuredInstances:n0} | Proxies: {_configuredProxies:n0}\n" +
            $"Telemetry: target {_configuredTelemetryPerSecond:n0}/s, applied {_lastTelemetryAppliedPerSecond:n0}/s\n" +
            $"FPS: {_lastFps:0.0} | Frame: {(frame?.FrameMilliseconds ?? 0):0.00} ms | Avg: {_lastAverageFrameMs:0.00} ms\n" +
            $"Alloc: {_allocatedMbPerSecond:0.00} MB/s\n" +
            $"HighScale: {stats.HighScaleInstanceCount:n0} | Chunks: {stats.VisibleChunkCount:n0}/{stats.TotalChunkCount:n0}\n" +
            $"Draw: {stats.DrawCallCount:n0} | Batches: {stats.InstancedBatchCount:n0} | Tris: {stats.TriangleCount:n0}\n" +
            $"Upload: {stats.InstanceUploadBytes / (1024d * 1024d):0.00} MB | Backend: {stats.BackendMilliseconds:0.00} ms\n" +
            $"CSV: {(_csvPath ?? "off")}";
    }

    private void ResetPerfWindows()
    {
        _lastFrame = null;
        _lastFps = 0;
        _lastAverageFrameMs = 0;
        _frameCountWindow = 0;
        _frameMsWindow = 0;
        _lastFrameWindowTicks = 0;
        _lastAllocatedBytes = GC.GetTotalAllocatedBytes(false);
        _lastAllocationTicks = Stopwatch.GetTimestamp();
        _allocatedMbPerSecond = 0;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, MinWidth = 96 };
        button.Click += (_, _) => action();
        return button;
    }

    private static StackPanel PresetRow(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var control in controls)
        {
            row.Children.Add(control);
        }
        return row;
    }

    private static TextBox TextBox(string text) => new() { Text = text, MinWidth = 110 };

    private static Control Row(string label, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static float ParseFloat(string? value, float fallback)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Q(string value) => '"' + value.Replace("\"", "\"\"") + '"';

    private void OpenLogFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Avalonia3D", "Benchmarks");
        Directory.CreateDirectory(dir);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch
        {
            // Not critical for the benchmark.
        }
    }
}

public sealed class BenchmarkMarker3D : CompositeObject3D
{
    public BenchmarkMarker3D()
    {
        Name = "BenchmarkMarker3D";
    }

    protected override void Build(CompositeBuilder3D b)
    {
        b.Box("Marker", 0.45f, 0.45f, 0.45f)
            .Material(new Material3D
            {
                BaseColor = new ColorRgba(0.25f, 0.78f, 0.95f, 1f),
                Lighting = LightingMode.Lambert
            })
            .NoCollider()
            .Pickable(false)
            .Manipulation(false);
    }
}

public sealed class BenchmarkRack3D : CompositeObject3D
{
    public BenchmarkRack3D()
    {
        Name = "BenchmarkRack3D";
    }

    protected override void Build(CompositeBuilder3D b)
    {
        b.Box("RackBody", 0.8f, 2.2f, 0.9f)
            .Material(Lit(0.22f, 0.24f, 0.28f))
            .NoCollider()
            .Pickable(false);

        b.Box("RackDoor", 0.74f, 2.05f, 0.04f)
            .At(0f, 0f, -0.48f)
            .Material(Lit(0.10f, 0.13f, 0.17f))
            .NoCollider()
            .Pickable(false);

        for (var i = 0; i < 8; i++)
        {
            b.Box("Slot" + i.ToString(CultureInfo.InvariantCulture), 0.62f, 0.055f, 0.035f)
                .At(0f, -0.78f + i * 0.19f, -0.515f)
                .Material(Lit(0.42f, 0.46f, 0.52f))
                .NoCollider()
                .Pickable(false);
        }

        b.Box("Status", 0.12f, 0.12f, 0.045f)
            .At(0.29f, 0.89f, -0.535f)
            .Material(Lit(0.20f, 0.85f, 0.35f))
            .NoCollider()
            .Pickable(false);
    }

    private static Material3D Lit(float r, float g, float b)
        => new()
        {
            BaseColor = new ColorRgba(r, g, b, 1f),
            Lighting = LightingMode.Lambert
        };
}
