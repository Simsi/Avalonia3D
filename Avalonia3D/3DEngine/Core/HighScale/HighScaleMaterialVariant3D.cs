using System.Collections.Generic;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.HighScale;

public sealed class HighScaleMaterialVariant3D
{
    private readonly Dictionary<int, ColorRgba> _partColors = new();

    public HighScaleMaterialVariant3D(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }
    public string Name { get; }
    public ColorRgba? DefaultColor { get; set; }

    public HighScaleMaterialVariant3D SetPartColor(int materialSlot, ColorRgba color)
    {
        _partColors[materialSlot] = color;
        return this;
    }

    public ColorRgba Resolve(CompositePartTemplate3D part)
        => Resolve(part.MaterialSlot, part.BaseColor);

    public ColorRgba Resolve(int materialSlot, ColorRgba baseColor)
    {
        if (_partColors.TryGetValue(materialSlot, out var color))
        {
            return color;
        }

        return DefaultColor ?? baseColor;
    }
}
