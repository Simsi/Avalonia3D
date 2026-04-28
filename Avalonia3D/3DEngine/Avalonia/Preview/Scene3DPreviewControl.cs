using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Preview;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Preview;

public sealed class Scene3DPreviewControl : UserControl
{
    private readonly Scene3DControl _viewport;
    private readonly ListBox _partList;
    private readonly ComboBox _previewSelector;
    private readonly TextBlock _inspectorText;
    private readonly Button _refreshButton;
    private IReadOnlyList<PreviewScene3D> _previews = Array.Empty<PreviewScene3D>();
    private List<Object3D> _listedObjects = new();
    private bool _updatingPreviewSelector;

    public Scene3DPreviewControl()
    {
        _viewport = new Scene3DControl
        {
            ShowPerformanceMetrics = true,
            ShowCenterCursor = false
        };

        _partList = new ListBox();
        _partList.SelectionChanged += OnPartSelectionChanged;

        _previewSelector = new ComboBox
        {
            MinWidth = 160d,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _previewSelector.SelectionChanged += OnPreviewSelectionChanged;

        _refreshButton = new Button
        {
            Content = "Refresh"
        };
        _refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        _inspectorText = new TextBlock
        {
            Text = "No selection",
            TextWrapping = TextWrapping.Wrap
        };

        var leftPanel = new StackPanel
        {
            Spacing = 8d,
            Margin = new Thickness(8d),
            Children =
            {
                new TextBlock { Text = "3D Preview", FontWeight = FontWeight.SemiBold },
                _previewSelector,
                _refreshButton,
                new TextBlock { Text = "Parts", FontWeight = FontWeight.SemiBold },
                _partList
            }
        };

        var rightPanel = new Border
        {
            Width = 260d,
            Padding = new Thickness(8d),
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            Child = new ScrollViewer
            {
                Content = _inspectorText
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("260,*,260")
        };

        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(_viewport, 1);
        Grid.SetColumn(rightPanel, 2);

        grid.Children.Add(leftPanel);
        grid.Children.Add(_viewport);
        grid.Children.Add(rightPanel);
        Content = grid;
    }

    public event EventHandler? RefreshRequested;

    public Scene3DControl Viewport => _viewport;

    public IReadOnlyList<PreviewScene3D> Previews
    {
        get => _previews;
        set
        {
            _previews = value ?? Array.Empty<PreviewScene3D>();
            _updatingPreviewSelector = true;
            try
            {
                _previewSelector.ItemsSource = _previews.Select(p => p.Name).ToArray();
                _previewSelector.SelectedIndex = _previews.Count > 0 ? 0 : -1;
            }
            finally
            {
                _updatingPreviewSelector = false;
            }

            if (_previews.Count > 0)
            {
                SetPreview(_previews[0]);
            }
            else
            {
                ClearPreview();
            }
        }
    }

    public void SetPreview(PreviewScene3D preview)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        _viewport.Scene = preview.Scene;
        BuildPartList();
        var report = PreviewComplexityReport3D.Analyze(preview.Scene);
        _inspectorText.Text = $"Preview: {preview.Name}\n\n" + report.Summary;
    }

    public void ClearPreview()
    {
        foreach (var obj in _listedObjects)
        {
            obj.IsSelected = false;
        }

        _viewport.Scene = PreviewScene3D.CreateDefaultScene();
        _listedObjects.Clear();
        _partList.ItemsSource = Array.Empty<string>();
        _partList.SelectedIndex = -1;
        _inspectorText.Text = "No preview";
    }

    public void SetError(string message)
    {
        ClearPreview();
        _inspectorText.Text = string.IsNullOrWhiteSpace(message)
            ? "Preview error"
            : "Preview error\n\n" + message;
    }


    private void OnPreviewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPreviewSelector)
        {
            return;
        }

        var index = _previewSelector.SelectedIndex;
        if (index >= 0 && index < _previews.Count)
        {
            SetPreview(_previews[index]);
        }
    }

    private void BuildPartList()
    {
        _listedObjects = _viewport.Scene.Registry.AllObjects.ToList();
        _partList.ItemsSource = _listedObjects.Select(FormatObjectName).ToArray();
        _partList.SelectedIndex = -1;
    }

    private string FormatObjectName(Object3D obj)
    {
        var depth = 0;
        var parent = obj.Parent;
        while (parent is not null)
        {
            depth++;
            parent = parent.Parent;
        }

        var indent = new string(' ', depth * 2);
        return $"{indent}{obj.Name} [{obj.GetType().Name}]";
    }

    private void OnPartSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = _partList.SelectedIndex;
        if (index < 0 || index >= _listedObjects.Count)
        {
            _inspectorText.Text = "No selection";
            return;
        }

        var selected = _listedObjects[index];
        foreach (var obj in _listedObjects)
        {
            obj.IsSelected = ReferenceEquals(obj, selected);
        }

        var bounds = selected.WorldBounds;
        var boundsText = bounds.IsValid ? $"{bounds.Min} - {bounds.Max}" : "empty";

        _inspectorText.Text =
            $"Name: {selected.Name}\n" +
            $"Type: {selected.GetType().FullName}\n" +
            $"Id: {selected.Id}\n\n" +
            $"Position: {selected.Position}\n" +
            $"Rotation: {selected.RotationDegrees}\n" +
            $"Scale: {selected.Scale}\n\n" +
            $"Visible: {selected.IsVisible}\n" +
            $"Pickable: {selected.IsPickable}\n" +
            $"Material: {selected.Material.Lighting}, {selected.Material.Surface}\n" +
            $"Color: {selected.Material.EffectiveColor.R:0.###}, {selected.Material.EffectiveColor.G:0.###}, {selected.Material.EffectiveColor.B:0.###}, {selected.Material.EffectiveColor.A:0.###}\n" +
            $"Collider: {selected.Collider?.GetType().Name ?? "none"}\n" +
            $"Rigidbody: {selected.Rigidbody?.GetType().Name ?? "none"}\n" +
            $"Bounds: {boundsText}";
    }
}
