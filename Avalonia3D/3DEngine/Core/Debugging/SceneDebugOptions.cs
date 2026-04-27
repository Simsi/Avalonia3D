using System;

namespace ThreeDEngine.Core.Debugging;

public sealed class SceneDebugOptions
{
    private bool _showPerformanceMetrics;
    private bool _showBounds;
    private bool _showColliders;
    private bool _showAxes;
    private bool _showGrid;
    private bool _showPickingRay;

    public event EventHandler? Changed;

    public bool ShowPerformanceMetrics { get => _showPerformanceMetrics; set => Set(ref _showPerformanceMetrics, value); }
    public bool ShowBounds { get => _showBounds; set => Set(ref _showBounds, value); }
    public bool ShowColliders { get => _showColliders; set => Set(ref _showColliders, value); }
    public bool ShowAxes { get => _showAxes; set => Set(ref _showAxes, value); }
    public bool ShowGrid { get => _showGrid; set => Set(ref _showGrid, value); }
    public bool ShowPickingRay { get => _showPickingRay; set => Set(ref _showPickingRay, value); }

    private void Set(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
