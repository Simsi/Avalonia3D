using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace PreviewerApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

internal sealed record PreviewerArguments(string? AssemblyPath, string? TypeFullName, string? ProjectPath)
{
    public static PreviewerArguments Parse(string[] args)
    {
        string? assembly = null;
        string? type = null;
        string? project = null;

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;

            if (key is "--assembly" or "-a")
            {
                assembly = value;
                i++;
            }
            else if (key is "--type" or "-t")
            {
                type = value;
                i++;
            }
            else if (key is "--project" or "-p")
            {
                project = value;
                i++;
            }
        }

        return new PreviewerArguments(assembly, type, project);
    }
}
