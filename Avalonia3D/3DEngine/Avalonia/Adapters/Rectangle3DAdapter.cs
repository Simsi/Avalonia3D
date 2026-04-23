using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ThreeDEngine.Avalonia.Primitives;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Adapters;

public sealed class Rectangle3DAdapter : IAvalonia3DAdapter
{
    private const float PixelToWorld = 0.01f;

    public bool CanAdapt(Control control) => control is Rectangle;

    public Object3D Adapt(Control control)
    {
        var rectangle = (Rectangle)control;

        var obj = new Rectangle3D
        {
            Name = string.IsNullOrWhiteSpace(rectangle.Name) ? "Adapted Rectangle" : rectangle.Name,
            Width = ToWorld(rectangle.Width, 160),
            Height = ToWorld(rectangle.Height, 90),
            Fill = rectangle.Fill.ToColorRgba(ColorRgba.FromRgb(0.94f, 0.66f, 0.19f))
        };

        rectangle.PropertyChanged += (_, e) => Sync(rectangle, obj, e.Property);
        return obj;
    }

    private static void Sync(Rectangle rectangle, Rectangle3D obj, AvaloniaProperty property)
    {
        if (property == global::Avalonia.Layout.Layoutable.WidthProperty)
        {
            obj.Width = ToWorld(rectangle.Width, 160);
        }
        else if (property == global::Avalonia.Layout.Layoutable.HeightProperty)
        {
            obj.Height = ToWorld(rectangle.Height, 90);
        }
        else if (property == Shape.FillProperty)
        {
            obj.Fill = rectangle.Fill.ToColorRgba(obj.Fill);
        }
        else if (property == StyledElement.NameProperty && !string.IsNullOrWhiteSpace(rectangle.Name))
        {
            obj.Name = rectangle.Name;
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
