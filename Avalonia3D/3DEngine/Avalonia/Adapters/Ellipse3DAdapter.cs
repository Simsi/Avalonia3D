using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ThreeDEngine.Avalonia.Primitives;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Adapters;

public sealed class Ellipse3DAdapter : IAvalonia3DAdapter
{
    private const float PixelToWorld = 0.01f;

    public bool CanAdapt(Control control) => control is Ellipse;

    public Object3D Adapt(Control control)
    {
        var ellipse = (Ellipse)control;

        var obj = new Ellipse3D
        {
            Name = string.IsNullOrWhiteSpace(ellipse.Name) ? "Adapted Ellipse" : ellipse.Name,
            Width = ToWorld(ellipse.Width, 120),
            Height = ToWorld(ellipse.Height, 120),
            Fill = ellipse.Fill.ToColorRgba(ColorRgba.FromRgb(0.25f, 0.69f, 0.95f))
        };

        ellipse.PropertyChanged += (_, e) => Sync(ellipse, obj, e.Property);
        return obj;
    }

    private static void Sync(Ellipse ellipse, Ellipse3D obj, AvaloniaProperty property)
    {
        if (property == global::Avalonia.Layout.Layoutable.WidthProperty)
        {
            obj.Width = ToWorld(ellipse.Width, 120);
        }
        else if (property == global::Avalonia.Layout.Layoutable.HeightProperty)
        {
            obj.Height = ToWorld(ellipse.Height, 120);
        }
        else if (property == Shape.FillProperty)
        {
            obj.Fill = ellipse.Fill.ToColorRgba(obj.Fill);
        }
        else if (property == StyledElement.NameProperty && !string.IsNullOrWhiteSpace(ellipse.Name))
        {
            obj.Name = ellipse.Name;
        }
    }

    private static float ToWorld(double value, double fallbackPixels)
    {
        if (double.IsNaN(value) || value <= 0d)
        {
            value = fallbackPixels;
        }

        return (float)(value * PixelToWorld);
    }
}
