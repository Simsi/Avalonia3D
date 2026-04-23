using Avalonia.Media;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Avalonia.Primitives;

public static class ColorExtensions
{
    public static ColorRgba ToColorRgba(this IBrush? brush, ColorRgba fallback)
    {
        if (brush is ISolidColorBrush solid)
        {
            var color = solid.Color;
            return new ColorRgba(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f);
        }

        return fallback;
    }
}
