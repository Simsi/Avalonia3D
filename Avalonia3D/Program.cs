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
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .LogToTrace();
}

internal sealed class PreviewerArguments
{
    public string? AssemblyPath { get; private set; }
    public string? TypeFullName { get; private set; }
    public string? ProjectPath { get; private set; }
    public string? HostProjectPath { get; private set; }

    public static PreviewerArguments Parse(string[] args)
    {
        var parsed = new PreviewerArguments();

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;

            if (key is "--assembly" or "-a")
            {
                parsed.AssemblyPath = value;
                i++;
            }
            else if (key is "--type" or "-t")
            {
                parsed.TypeFullName = value;
                i++;
            }
            else if (key is "--project" or "-p")
            {
                parsed.ProjectPath = value;
                i++;
            }
            else if (key is "--host-project")
            {
                parsed.HostProjectPath = value;
                i++;
            }
        }

        return parsed;
    }
}
