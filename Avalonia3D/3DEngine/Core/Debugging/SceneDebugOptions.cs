using System;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Debugging;

public sealed class SceneDebugOptions
{
    private bool _showPerformanceMetrics;
    private bool _showBounds;
    private bool _showColliders;
    private bool _showAxes;
    private bool _showGrid;
    private bool _showPickingRay;
    private bool _showWireframeOverlay;
    private bool _showSilhouetteOverlay;
    private bool _showSurfaceNormals;

    public event EventHandler? Changed;
    internal Func<SceneAccessLease3D>? MutationScopeRequested { get; set; }

    public bool ShowPerformanceMetrics { get => _showPerformanceMetrics; set => Set(ref _showPerformanceMetrics, value); }
    public bool ShowBounds { get => _showBounds; set => Set(ref _showBounds, value); }
    public bool ShowColliders { get => _showColliders; set => Set(ref _showColliders, value); }
    public bool ShowAxes { get => _showAxes; set => Set(ref _showAxes, value); }
    public bool ShowGrid { get => _showGrid; set => Set(ref _showGrid, value); }
    public bool ShowPickingRay { get => _showPickingRay; set => Set(ref _showPickingRay, value); }
    public bool ShowWireframeOverlay { get => _showWireframeOverlay; set => Set(ref _showWireframeOverlay, value); }
    public bool ShowSilhouetteOverlay { get => _showSilhouetteOverlay; set => Set(ref _showSilhouetteOverlay, value); }
    public bool ShowSurfaceNormals { get => _showSurfaceNormals; set => Set(ref _showSurfaceNormals, value); }

    private void Set(ref bool field, bool value)
    {
        using var mutation = MutationScopeRequested?.Invoke() ?? default;
        if (field == value) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
