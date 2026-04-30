using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Preview;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;
using ProjectionHelper3D = ThreeDEngine.Core.Math.ProjectionHelper;

namespace ThreeDEngine.Avalonia.Preview;

public sealed partial class Scene3DPreviewControl
{
    private static Border ToolPanel(params Control[] children)
    {
        var stack = new StackPanel
        {
            Spacing = 4d,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var child in children)
        {
            child.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(child);
        }

        return new Border
        {
            Padding = new Thickness(6d),
            CornerRadius = new CornerRadius(3d),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            Child = stack
        };
    }

    private static Expander CollapsibleSection(string title, bool expanded, params Control[] children)
    {
        var stack = new StackPanel
        {
            Spacing = 5d,
            Margin = new Thickness(0d, 4d, 0d, 2d),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var child in children)
        {
            child.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(child);
        }

        return new Expander
        {
            Header = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12d },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = expanded,
            Content = stack
        };
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0d, 6d, 0d, 0d),
        FontSize = 12d
    };

    private static Control LabeledEditor(string label, Control editor)
    {
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions("92,*"),
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap },
                PlaceInColumn(editor, 1)
            }
        };
    }

    private static Control VectorRow(string label, params TextBox[] boxes)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions("92,*")
        };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap });
        var row = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", Math.Max(1, boxes.Length))))
        };
        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            box.Width = double.NaN;
            box.MinWidth = 0d;
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.Margin = new Thickness(i == 0 ? 0d : 4d, 0d, 0d, 0d);
            Grid.SetColumn(box, i);
            row.Children.Add(box);
        }

        Grid.SetColumn(row, 1);
        grid.Children.Add(row);
        return grid;
    }

    private static Control PlaceInColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private TextBox CreateSceneBox(string watermark)
    {
        var box = new TextBox
        {
            Watermark = watermark,
            MinWidth = 0d,
            Height = 24d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12d
        };
        box.KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplySceneSettingsFromControls();
                e.Handled = true;
            }
        };
        return box;
    }

    private static TextBox CreateWorkbenchBox(string watermark) => new()
    {
        Watermark = watermark,
        MinWidth = 0d,
        Height = 24d,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        FontSize = 12d
    };

    private TextBox CreateEditorBox(string watermark)
    {
        var box = new TextBox
        {
            Watermark = watermark,
            MinWidth = 0d,
            Height = 24d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12d
        };
        box.TextChanged += OnEditorChanged;
        box.LostFocus += OnEditorChanged;
        box.KeyDown += OnEditorKeyDown;
        return box;
    }

    private CheckBox CreateEditorCheckBox(string label)
    {
        var checkBox = new CheckBox { Content = label, FontSize = 12d };
        checkBox.Click += OnEditorChanged;
        return checkBox;
    }

    private ComboBox CreateEnumBox<TEnum>() where TEnum : struct, Enum
    {
        var combo = new ComboBox
        {
            ItemsSource = Enum.GetValues<TEnum>(),
            Height = 24d,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        combo.SelectionChanged += OnEditorChanged;
        return combo;
    }

    private static void SetVector(TextBox x, TextBox y, TextBox z, Vector3 value)
    {
        x.Text = FormatFloat(value.X);
        y.Text = FormatFloat(value.Y);
        z.Text = FormatFloat(value.Z);
    }

    private static bool TryReadVector(TextBox x, TextBox y, TextBox z, out Vector3 value)
    {
        value = default;
        if (!TryReadFloat(x.Text, out var vx) || !TryReadFloat(y.Text, out var vy) || !TryReadFloat(z.Text, out var vz))
        {
            return false;
        }

        value = new Vector3(vx, vy, vz);
        return true;
    }

    private bool TryReadColor(out ColorRgba color)
    {
        color = ColorRgba.White;
        if (!TryReadFloat(_colorRBox.Text, out var r) ||
            !TryReadFloat(_colorGBox.Text, out var g) ||
            !TryReadFloat(_colorBBox.Text, out var b) ||
            !TryReadFloat(_colorABox.Text, out var a))
        {
            return false;
        }

        color = new ColorRgba(Clamp01(r), Clamp01(g), Clamp01(b), Clamp01(a));
        return true;
    }

    private static bool TryReadFloat(string? text, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().Replace(',', '.');
        return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool TryReadInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);

    private void SetStatus(string message, bool isError)
    {
        _statusText.Text = message;
        _statusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(255, 145, 145))
            : new SolidColorBrush(Color.FromRgb(178, 204, 255));
    }

    private static string FormatVector(Vector3 value)
        => $"({FormatFloat(value.X)}, {FormatFloat(value.Y)}, {FormatFloat(value.Z)})";

    private static string FormatFloat(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatFloatCode(float value)
        => value.ToString("0.######", CultureInfo.InvariantCulture) + "f";

    private static string FormatBool(bool value) => value ? "true" : "false";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

}
