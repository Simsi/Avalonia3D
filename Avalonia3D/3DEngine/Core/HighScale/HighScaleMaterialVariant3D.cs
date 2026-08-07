using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.HighScale;

public sealed class HighScaleMaterialVariant3D
{
    private readonly Dictionary<int, ColorRgba> _partColors = new();
    private readonly ReadOnlyDictionary<int, ColorRgba> _partColorsView;
    private ColorRgba? _defaultColor;
    private int _version;

    public HighScaleMaterialVariant3D(int id, string name)
    {
        Id = Guard3D.NonNegative(id, nameof(id));
        Name = Guard3D.RequiredText(name, nameof(name));
        _partColorsView = new ReadOnlyDictionary<int, ColorRgba>(_partColors);
    }

    internal event EventHandler? Changed;

    public int Id { get; }
    public string Name { get; }
    public int Version => _version;
    public IReadOnlyDictionary<int, ColorRgba> PartColors => _partColorsView;

    public ColorRgba? DefaultColor
    {
        get => _defaultColor;
        set
        {
            if (value.HasValue) Guard3D.Color(value.Value, nameof(value));
            if (_defaultColor.Equals(value)) return;
            _defaultColor = value;
            RaiseChanged();
        }
    }

    public HighScaleMaterialVariant3D SetPartColor(int materialSlot, ColorRgba color)
    {
        Guard3D.NonNegative(materialSlot, nameof(materialSlot));
        color = Guard3D.Color(color, nameof(color));
        if (_partColors.TryGetValue(materialSlot, out var current) && current.Equals(color)) return this;
        _partColors[materialSlot] = color;
        RaiseChanged();
        return this;
    }

    public bool RemovePartColor(int materialSlot)
    {
        Guard3D.NonNegative(materialSlot, nameof(materialSlot));
        if (!_partColors.Remove(materialSlot)) return false;
        RaiseChanged();
        return true;
    }

    public void ClearPartColors()
    {
        if (_partColors.Count == 0) return;
        _partColors.Clear();
        RaiseChanged();
    }

    public ColorRgba Resolve(CompositePartTemplate3D part)
    {
        if (part is null) throw new ArgumentNullException(nameof(part));
        return Resolve(part.MaterialSlot, part.BaseColor);
    }

    public ColorRgba Resolve(int materialSlot, ColorRgba baseColor)
    {
        if (_partColors.TryGetValue(materialSlot, out var color)) return color;
        return DefaultColor ?? baseColor;
    }

    private void RaiseChanged()
    {
        unchecked { _version++; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
