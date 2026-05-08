using System;

namespace ThreeDEngine.Core.Geometry.Surfaces;

/// <summary>
/// Portable tessellation-compatible options. Backends that do not support hardware tessellation
/// can use CPU subdivision previews or ignore the setting safely.
/// </summary>
public sealed class SurfaceTessellationOptions3D
{
    private int _subdivisionLevel;
    private bool _isEnabled;

    public event EventHandler? Changed;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SubdivisionLevel
    {
        get => _subdivisionLevel;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0, 4);
            if (_subdivisionLevel == clamped) return;
            _subdivisionLevel = clamped;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public SurfaceTessellationOptions3D Clone()
        => new SurfaceTessellationOptions3D { IsEnabled = IsEnabled, SubdivisionLevel = SubdivisionLevel };
}
