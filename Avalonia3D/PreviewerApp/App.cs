#if THREE_DENGINE_PREVIEWER_APP
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
                    "dotnet run --project .\\PreviewerApp\\PreviewerApp.csproj -p:ThreeDEngineHostProject=.\\Avalonia3D.csproj -- --assembly .\\bin\\Debug\\net8.0\\Avalonia3D.dll --type MyNamespace.MyControl3D\n\n" +
                    "The Visual Studio connector normally builds and launches this command automatically. Build the host Avalonia3D project first for manual runs.");
            }
            else
            {
                window.LoadFromAssembly(args.AssemblyPath!, args.TypeFullName, args.ProjectPath ?? args.HostProjectPath);
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
#endif
