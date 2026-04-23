using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Adapters;

public sealed class Avalonia3DAdapterRegistry
{
    private readonly List<IAvalonia3DAdapter> _adapters = new List<IAvalonia3DAdapter>
    {
        new Rectangle3DAdapter(),
        new Ellipse3DAdapter()
    };

    private Scene3D _scene;

    public Avalonia3DAdapterRegistry(Scene3D scene)
    {
        _scene = scene;
    }

    public void SetScene(Scene3D scene) => _scene = scene;

    public void Register(IAvalonia3DAdapter adapter)
        => _adapters.Insert(0, adapter);

    public Object3D Add(Control control)
    {
        foreach (var adapter in _adapters)
        {
            if (adapter.CanAdapt(control))
            {
                var obj = adapter.Adapt(control);
                _scene.Add(obj);
                return obj;
            }
        }

        var plane = new ControlPlane3D(control)
        {
            Width = ToWorldUnits(control.Width, 320d),
            Height = ToWorldUnits(control.Height, 180d),
            AlwaysFaceCamera = false
        };

        _scene.Add(plane);
        return plane;
    }

    private static float ToWorldUnits(double value, double fallbackPixels)
    {
        if (double.IsNaN(value) || value <= 0d)
        {
            value = fallbackPixels;
        }

        return (float)(value * 0.01d);
    }
}
