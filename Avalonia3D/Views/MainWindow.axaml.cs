using Avalonia.Controls;
using Avalonia3D.ViewModels;

namespace Avalonia3D.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Content = new MainView
        {
            DataContext = new MainViewModel()
        };
    }
}
