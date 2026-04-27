using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace PreviewerApp;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = PreviewerArguments.Parse(desktop.Args ?? System.Array.Empty<string>());
            var window = new PreviewerWindow();

            if (string.IsNullOrWhiteSpace(args.AssemblyPath))
            {
                window.ShowStartupMessage(
                    "Run PreviewerApp with:\n\n" +
                    "dotnet run --project .\\PreviewerApp\\PreviewerApp.csproj -- --assembly .\\bin\\Debug\\net8.0\\Avalonia3D.dll --type Avalonia3D.Views.DemoServerRack3D\n\n" +
                    "Build the host Avalonia3D project first. The demo preview type may live in Views/MainView.axaml.cs.");
            }
            else
            {
                window.LoadFromAssembly(args.AssemblyPath!, args.TypeFullName);
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
