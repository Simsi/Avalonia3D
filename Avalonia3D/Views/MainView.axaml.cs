using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System.Numerics;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace Avalonia3D.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        // ContentGrid
        Add3D();
    }

    public void Add3D()
    {
        var sceneControl = new Scene3DControl
        {
            Height = 1000,
            Width = 1000,
        };

        sceneControl.Scene.BackgroundColor = new ColorRgba(1f, 0f, 0f, 1f);
        sceneControl.Scene.Camera.Position = new Vector3(0, 0, -10);
        sceneControl.Scene.Camera.Target = Vector3.Zero;

        var rect = new Rectangle3D
        {
            Name = "Rect",
            Width = 2.2f,
            Height = 1.2f,
            Depth = 1.2f,
            Position = new Vector3(-2.2f, 0f, 0f),
            Color = new ColorRgba(0.9f, 0.25f, 0.25f, 1f)
        };

        var ellipse = new Ellipse3D
        {
            Name = "Ellipse",
            RadiusX = 0.9f,
            RadiusY = 0.7f,
            Depth = 1.0f,
            Position = new Vector3(2.2f, 0f, 0.4f),
            Color = new ColorRgba(0.2f, 0.65f, 0.95f, 1f)
        };

        rect.Clicked += (_, _) =>
        {
            rect.Rotation += new Vector3(0, 15f, 0);
        };

        ellipse.Clicked += (_, _) =>
        {
            ellipse.Position += new Vector3(0.2f, 0.1f, 0f);
        };

        sceneControl.Add(rect);
        sceneControl.Add(ellipse);

        var livePanel = new Border
        {
            Width = 260,
            Height = 170,
            Background = Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
        {
            new TextBlock
            {
                Text = "Live control inside 3D scene",
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.Black
            },
            new TextBox
            {
                Watermark = "Type here...",
                Foreground = Brushes.Black,
                Background = Brushes.LightGray
            },
            new Button
            {
                Content = "Click me",
                Foreground = Brushes.Black,
                Background = Brushes.LightGray
            }
        }
            }
        };

        if (livePanel.Child is StackPanel panel && panel.Children[2] is Button button)
        {
            button.Click += (_, _) =>
            {
                rect.Color = new ColorRgba(0.25f, 0.75f, 0.35f, 1f);
                ellipse.Color = new ColorRgba(0.95f, 0.55f, 0.2f, 1f);
            };
        }

        var liveSprite = sceneControl.AddLiveControl(livePanel);
        liveSprite.Position = new Vector3(0f, 1.8f, 1.5f);
        liveSprite.Width = 3.8f;
        liveSprite.Height = 2.4f;
        liveSprite.AlwaysFaceCamera = false;

        var outerbtn = new Button()
        {
            Content = "Change ClickMe button color"
        };

        outerbtn.Click += (_, _) =>
        {
            if (livePanel.Child is StackPanel panel && panel.Children[2] is Button button)
            {
                button.Background = new SolidColorBrush(Colors.Red);
            }

        };
        ContentGrid.Children.Add(sceneControl);
        ContentGrid.Children.Add(outerbtn);

    }
}
