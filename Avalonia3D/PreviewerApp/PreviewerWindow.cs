using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Avalonia.Controls;
using ThreeDEngine.Avalonia.Preview;
using ThreeDEngine.Core.Preview;

namespace PreviewerApp;

public sealed class PreviewerWindow : Window
{
    private readonly Scene3DPreviewControl _previewControl = new();
    private PreviewAssemblyLoadContext? _loadContext;
    private string? _assemblyPath;
    private string? _typeFullName;

    public PreviewerWindow()
    {
        Title = "3DEngine Previewer";
        Width = 1200d;
        Height = 800d;
        Content = _previewControl;
        _previewControl.RefreshRequested += (_, _) => Reload();
    }

    public Scene3DPreviewControl PreviewControl => _previewControl;

    public void LoadFromAssembly(string assemblyPath, string? typeFullName = null)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            _previewControl.SetError("Assembly path is empty. Pass --assembly <path-to-dll>.");
            return;
        }

        _assemblyPath = Path.GetFullPath(assemblyPath);
        _typeFullName = string.IsNullOrWhiteSpace(typeFullName) ? null : typeFullName;
        Reload();
    }

    public void ShowStartupMessage(string message)
    {
        _previewControl.SetError(message);
    }

    protected override void OnClosed(EventArgs e)
    {
        UnloadPreviousPreview();
        base.OnClosed(e);
    }

    private void Reload()
    {
        if (string.IsNullOrWhiteSpace(_assemblyPath))
        {
            _previewControl.SetError("No assembly was selected. Run PreviewerApp with --assembly <path-to-dll>.");
            return;
        }

        UnloadPreviousPreview();

        if (!File.Exists(_assemblyPath))
        {
            _previewControl.SetError($"Assembly was not found:\n{_assemblyPath}");
            return;
        }

        var loadContext = new PreviewAssemblyLoadContext(_assemblyPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(_assemblyPath);
            var descriptors = PreviewDiscovery.Discover(assembly, _typeFullName);
            var previews = descriptors.SelectMany(PreviewDiscovery.Create).ToArray();
            if (previews.Length == 0)
            {
                var target = string.IsNullOrWhiteSpace(_typeFullName) ? assembly.GetName().Name : _typeFullName;
                throw new InvalidOperationException($"No [Preview3D] methods or previewable CompositeObject3D classes were found in '{target}'.");
            }

            _loadContext = loadContext;
            _previewControl.Previews = previews;
            Title = string.IsNullOrWhiteSpace(_typeFullName)
                ? $"3DEngine Previewer - {Path.GetFileName(_assemblyPath)}"
                : $"3DEngine Previewer - {_typeFullName}";
        }
        catch (Exception ex)
        {
            loadContext.Unload();
            _previewControl.SetError(ex.ToString());
        }
    }

    private void UnloadPreviousPreview()
    {
        if (_loadContext is null)
        {
            return;
        }

        _previewControl.ClearPreview();
        _loadContext.Unload();
        _loadContext = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private sealed class PreviewAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PreviewAssemblyLoadContext(string mainAssemblyPath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
