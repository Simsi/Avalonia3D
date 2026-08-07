using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Demos;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.TestControls;

/// <summary>
/// Drop-in visual harness for the deterministic performance scenes. Applications can use
/// this control as temporary Window.Content on desktop and browser without changing the
/// PreviewerApp or the Visual Studio extension.
/// </summary>
public sealed class PerformanceBaselineControl3D : Grid, IDisposable
{
    private readonly Scene3DControl _sceneControl;
    private readonly ComboBox _sceneSelector;
    private readonly Button _animationButton;
    private readonly TextBlock _status;
    private readonly TextBox _diagnosticReport;
    private readonly List<IPerformanceBaselineScene3D> _workloads = new();
    private IPerformanceBaselineScene3D? _activeWorkload;
    private bool _animationEnabled = true;
    private bool _attached;
    private bool _workloadUpdateSubscribed;
    private bool _disposed;

    [Obsolete("Use PerformanceBaselineControl3D(Engine3D) with an explicitly composed engine. This compatibility constructor requires Avalonia3D.Engine or the complete 3DEngine source-drop.")]
    public PerformanceBaselineControl3D()
        : this(engine: null, useDefaultEngine: true)
    {
    }

    public PerformanceBaselineControl3D(Engine3D engine)
        : this(engine ?? throw new ArgumentNullException(nameof(engine)), useDefaultEngine: false)
    {
    }

    private PerformanceBaselineControl3D(Engine3D? engine, bool useDefaultEngine)
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(8, 11, 18));

        _sceneControl = useDefaultEngine
            ? new Scene3DControl()
            : new Scene3DControl(engine ?? throw new ArgumentNullException(nameof(engine)));
        _sceneControl.ShowPerformanceMetrics = true;
        _sceneControl.ContinuousRendering = true;
        _sceneControl.FpsLockEnabled = true;
        _sceneControl.TargetFps = 60d;
        _sceneControl.HorizontalAlignment = HorizontalAlignment.Stretch;
        _sceneControl.VerticalAlignment = VerticalAlignment.Stretch;
        Children.Add(_sceneControl);

        foreach (var demo in PerformanceBaselineCatalog3D.Create().Demos)
        {
            if (demo is IPerformanceBaselineScene3D workload) _workloads.Add(workload);
        }

        _sceneSelector = new ComboBox
        {
            MinWidth = 280d,
            ItemsSource = _workloads,
            SelectedIndex = _workloads.Count > 0 ? 0 : -1,
            ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<IPerformanceBaselineScene3D>(
                (item, _) => new TextBlock { Text = item.Descriptor.Title })
        };
        _sceneSelector.SelectionChanged += (_, _) => LoadSelectedWorkload();

        _animationButton = new Button { Content = "Pause mutations" };
        _animationButton.Click += (_, _) => ToggleAnimation();

        var singleStepButton = new Button { Content = "Single fixed step" };
        singleStepButton.Click += (_, _) => SingleStep();

        var copyReportButton = new Button { Content = "Copy diagnostic report" };
        copyReportButton.Click += async (_, _) => await CopyDiagnosticReportAsync();

        var showReportButton = new Button { Content = "Show/hide report" };
        showReportButton.Click += (_, _) => ToggleDiagnosticReport();

        var exportReportButton = new Button { Content = "Export diagnostic file" };
        exportReportButton.Click += (_, _) => ExportDiagnosticReport();

        var acceptanceButton = new Button { Content = "Evaluate acceptance" };
        acceptanceButton.Click += (_, _) => EvaluateProductionAcceptance();

        var captureButton = new Button { Content = "Export frame capture" };
        captureButton.Click += async (_, _) => await ExportFrameCaptureAsync();

        _status = new TextBlock
        {
            Text = "Select a baseline scene.",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        _diagnosticReport = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            FontSize = 11d,
            MinHeight = 180d,
            MaxHeight = 320d,
            IsVisible = false
        };

        var toolbar = new StackPanel
        {
            Spacing = 8d,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8d,
                    Children = { _sceneSelector, _animationButton, singleStepButton, copyReportButton, showReportButton, exportReportButton, acceptanceButton, captureButton }
                },
                _status,
                _diagnosticReport
            }
        };
        var toolbarHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8d),
            Padding = new Thickness(10d),
            CornerRadius = new CornerRadius(5d),
            Background = new SolidColorBrush(Color.FromArgb(220, 18, 23, 34)),
            Child = toolbar
        };
        toolbarHost.ZIndex = int.MaxValue - 10;
        Children.Add(toolbarHost);

        LoadSelectedWorkload();
    }

    public Scene3DControl SceneControl => _sceneControl;

    public string CreateDiagnosticReport() => _sceneControl.CreateDiagnosticReport();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateWorkloadSubscription();
        EngineLog3D.Information("BaselineHarness", "Performance baseline control attached.");
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        UpdateWorkloadSubscription();
        EngineLog3D.Information("BaselineHarness", "Performance baseline control detached.");
        base.OnDetachedFromVisualTree(e);
    }

    private void LoadSelectedWorkload()
    {
        if (_sceneSelector.SelectedIndex < 0 || _sceneSelector.SelectedIndex >= _workloads.Count) return;
        _activeWorkload = _workloads[_sceneSelector.SelectedIndex];
        try
        {
            _activeWorkload.Build(_sceneControl.Scene, new DemoSceneContext3D
            {
                Status = message => _status.Text = message,
                Warning = message =>
                {
                    _status.Text = message;
                    EngineLog3D.Warning("BaselineHarness", message);
                },
                Diagnostics = message => EngineLog3D.Information("BaselineHarness", message)
            });
            _sceneControl.Scene.UpdateLoop.Reset();
            _diagnosticReport.Text = string.Empty;
            EngineLog3D.Information("BaselineHarness", $"Loaded '{_activeWorkload.Descriptor.Id}'.");
        }
        catch (Exception exception)
        {
            _status.Text = exception.GetType().Name + ": " + exception.Message;
            EngineLog3D.Critical("BaselineHarness", $"Failed to build '{_activeWorkload.Descriptor.Id}'.", exception);
        }
    }

    private void OnSceneFixedUpdate(Scene3D scene, in SceneFixedUpdateContext3D context)
    {
        if (_activeWorkload is null) return;
        try
        {
            _activeWorkload.Update((float)context.SimulationTimeSeconds);
        }
        catch (Exception exception)
        {
            _animationEnabled = false;
            UpdateWorkloadSubscription();
            _animationButton.Content = "Resume mutations";
            _status.Text = exception.GetType().Name + ": " + exception.Message;
            EngineLog3D.Critical("BaselineHarness", "Baseline update failed; mutations were stopped.", exception);
        }
    }

    private void ToggleAnimation()
    {
        _animationEnabled = !_animationEnabled;
        _animationButton.Content = _animationEnabled ? "Pause mutations" : "Resume mutations";
        UpdateWorkloadSubscription();
    }

    private void SingleStep()
    {
        try
        {
            _sceneControl.Scene.UpdateLoop.StepOnce();
            _status.Text = $"Executed fixed tick {_sceneControl.Scene.UpdateLoop.SimulationTick}.";
        }
        catch (Exception exception)
        {
            _status.Text = exception.GetType().Name + ": " + exception.Message;
            EngineLog3D.Error("BaselineHarness", "Single fixed step failed.", exception);
        }
    }

    private void UpdateWorkloadSubscription()
    {
        var shouldSubscribe = _attached;
        _sceneControl.IsSimulationPaused = !_animationEnabled;
        if (shouldSubscribe == _workloadUpdateSubscribed) return;
        if (shouldSubscribe)
        {
            _sceneControl.Scene.FixedUpdate += OnSceneFixedUpdate;
        }
        else
        {
            _sceneControl.Scene.FixedUpdate -= OnSceneFixedUpdate;
        }
        _workloadUpdateSubscribed = shouldSubscribe;
    }

    private void ToggleDiagnosticReport()
    {
        _diagnosticReport.IsVisible = !_diagnosticReport.IsVisible;
        if (_diagnosticReport.IsVisible) _diagnosticReport.Text = CreateDiagnosticReport();
    }

    private void ExportDiagnosticReport()
    {
        try
        {
            var path = _sceneControl.ExportDiagnosticReport();
            _status.Text = path is null
                ? "Browser diagnostic report download requested."
                : $"Diagnostic report written to {path}";
        }
        catch (Exception exception)
        {
            _status.Text = exception.GetType().Name + ": " + exception.Message;
            _diagnosticReport.Text = CreateDiagnosticReport();
            _diagnosticReport.IsVisible = true;
            EngineLog3D.Error("BaselineHarness", "Diagnostic export failed.", exception);
        }
    }

    private void EvaluateProductionAcceptance()
    {
        try
        {
            var snapshot = _sceneControl.Scene.Engine.Profiler.Capture(600);
            var result = ProductionAcceptance3D.Evaluate(snapshot);
            if (result.Passed)
            {
                _status.Text = $"Production acceptance passed for {snapshot.Frames.Count} captured frame(s).";
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Production acceptance failed with {result.Failures.Count} issue(s):");
            for (var i = 0; i < result.Failures.Count; i++)
            {
                var failure = result.Failures[i];
                report.Append("- ").Append(failure.Metric).Append(": actual=").Append(failure.Actual)
                    .Append(", required=").Append(failure.Required).Append(". ").AppendLine(failure.Explanation);
            }
            _diagnosticReport.Text = report.ToString();
            _diagnosticReport.IsVisible = true;
            _status.Text = $"Production acceptance failed ({result.Failures.Count} metric(s)).";
        }
        catch (Exception exception)
        {
            _status.Text = exception.GetType().Name + ": " + exception.Message;
            EngineLog3D.Error("BaselineHarness", "Production acceptance evaluation failed.", exception);
        }
    }

    private async System.Threading.Tasks.Task ExportFrameCaptureAsync()
    {
        try
        {
            var capture = _sceneControl.Scene.Engine.CaptureFrame(600, 1024);
            if (!OperatingSystem.IsBrowser())
            {
                var directory = EngineLog3D.LogDirectory;
                if (string.IsNullOrWhiteSpace(directory)) directory = Path.Combine(AppContext.BaseDirectory, "Avalonia3D", "Captures");
                var path = await capture.SaveAsync(directory);
                _status.Text = $"Frame capture written to {path}";
                return;
            }

            var json = capture.ToJson(indented: true);
            _diagnosticReport.Text = json;
            _diagnosticReport.IsVisible = true;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                _status.Text = "Browser frame capture is visible; copy it manually.";
                return;
            }
            await clipboard.SetTextAsync(json);
            _status.Text = "Browser frame capture copied to the clipboard.";
        }
        catch (Exception exception)
        {
            _status.Text = exception.GetType().Name + ": " + exception.Message;
            EngineLog3D.Error("BaselineHarness", "Frame capture export failed.", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _attached = false;
        if (_workloadUpdateSubscribed)
        {
            _sceneControl.Scene.FixedUpdate -= OnSceneFixedUpdate;
            _workloadUpdateSubscribed = false;
        }
        _sceneControl.Dispose();
    }

    private async System.Threading.Tasks.Task CopyDiagnosticReportAsync()
    {
        var report = CreateDiagnosticReport();
        _diagnosticReport.Text = report;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            _diagnosticReport.IsVisible = true;
            _status.Text = "Clipboard is unavailable; copy the visible diagnostic report manually.";
            return;
        }

        try
        {
            await clipboard.SetTextAsync(report);
            _status.Text = "Diagnostic report copied to the clipboard.";
        }
        catch (Exception exception)
        {
            _diagnosticReport.IsVisible = true;
            _status.Text = "Clipboard write failed; copy the visible report manually.";
            EngineLog3D.Error("BaselineHarness", "Clipboard write failed.", exception);
        }
    }
}
