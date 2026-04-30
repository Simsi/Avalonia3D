#if THREE_DENGINE_PREVIEWER_APP
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
    private string? _projectPath;

    public PreviewerWindow()
    {
        Title = "3DEngine Debugger";
        Width = 1200d;
        Height = 800d;
        Content = _previewControl;
        _previewControl.SetSourceExportHandler(RoslynDebuggerSourceExporter.ExportAsync);
        _previewControl.RefreshRequested += (_, _) => Reload();
    }

    public Scene3DPreviewControl PreviewControl => _previewControl;

    public void LoadFromAssembly(string assemblyPath, string? typeFullName = null, string? projectPath = null)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            _previewControl.SetError("Assembly path is empty. Pass --assembly <path-to-dll>.");
            return;
        }

        _assemblyPath = Path.GetFullPath(assemblyPath);
        _typeFullName = string.IsNullOrWhiteSpace(typeFullName) ? null : typeFullName;
        _projectPath = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFullPath(projectPath);
        _previewControl.SetSourceGenerationContext(_assemblyPath, _typeFullName, _projectPath);
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
            var assembly = FindAlreadyLoadedAssembly(_assemblyPath) ?? loadContext.LoadFromAssemblyPath(_assemblyPath);
            var descriptors = PreviewDiscovery.Discover(assembly, _typeFullName);

            if (descriptors.Count == 0 && !string.IsNullOrWhiteSpace(_typeFullName))
            {
                var similarTypes = PreviewDiscovery.FindSimilarTypeNames(assembly, _typeFullName!);
                var availablePreviews = PreviewDiscovery.Discover(assembly);
                var hint = similarTypes.Count > 0
                    ? "\n\nSimilar types in the assembly:\n  " + string.Join("\n  ", similarTypes)
                    : string.Empty;
                var availableHint = availablePreviews.Count > 0
                    ? "\n\nPreviewable types were found in the assembly, but not under the requested name. Try running without --type to list all previews."
                    : string.Empty;

                throw new InvalidOperationException(
                    "Preview type was not found in the target assembly.\n\n" +
                    $"Requested type: {_typeFullName}\n" +
                    $"Assembly: {_assemblyPath}\n\n" +
                    "Rebuild the host project and check the real namespace of the class. " +
                    "File-scoped namespaces apply to all classes in the file; nested classes use CLR name Outer+Inner." +
                    hint +
                    availableHint);
            }

            var previews = descriptors.SelectMany(PreviewDiscovery.Create).ToArray();
            if (previews.Length == 0)
            {
                var target = string.IsNullOrWhiteSpace(_typeFullName) ? assembly.GetName().Name : _typeFullName;
                throw new InvalidOperationException(
                    $"No previewable 3D entry points were found in '{target}'.\n\n" +
                    "Supported forms:\n" +
                    "  public sealed class MyControl3D : CompositeObject3D { public MyControl3D() { ... } }\n" +
                    "  [Preview3D] public static Object3D Preview() { ... }\n" +
                    "  [Preview3D] public static Scene3D PreviewScene() { ... }\n" +
                    "  [Preview3D] public static IEnumerable<PreviewScene3D> Previews() { ... }");
            }

            _loadContext = loadContext;
            _previewControl.Previews = previews;
            Title = string.IsNullOrWhiteSpace(_typeFullName)
                ? $"3DEngine Debugger - {Path.GetFileName(_assemblyPath)}"
                : $"3DEngine Debugger - {_typeFullName}";
        }
        catch (Exception ex)
        {
            loadContext.Unload();
            _previewControl.SetError(FormatException(ex));
        }
    }

    private static string FormatException(Exception ex)
    {
        if (ex is ReflectionTypeLoadException reflectionEx)
        {
            var loaderErrors = string.Join("\n\n", reflectionEx.LoaderExceptions.Where(e => e is not null).Select(e => e!.ToString()));
            return reflectionEx + "\n\nLoader exceptions:\n" + loaderErrors;
        }

        return ex.ToString();
    }

    private static Assembly? FindAlreadyLoadedAssembly(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        string? targetAssemblyName = null;
        try
        {
            targetAssemblyName = AssemblyName.GetAssemblyName(fullPath).Name;
        }
        catch
        {
            // Let LoadFromAssemblyPath report malformed assemblies later.
        }

        Assembly? sameNameAssembly = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(assembly.Location) &&
                    string.Equals(Path.GetFullPath(assembly.Location), fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return assembly;
                }

                if (sameNameAssembly is null &&
                    !string.IsNullOrWhiteSpace(targetAssemblyName) &&
                    string.Equals(assembly.GetName().Name, targetAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    sameNameAssembly = assembly;
                }
            }
            catch
            {
                // Dynamic assemblies can throw on Location. Ignore them.
            }
        }

        // Source-drop mode compiles 3DEngine into the host assembly. PreviewerApp references
        // that same host project, so the assembly is often already loaded from PreviewerApp/bin,
        // while VSIX passes the host project's original bin path. Returning the loaded assembly
        // preserves type identity for CompositeObject3D/Scene3D/Preview3DAttribute.
        return sameNameAssembly;
    }

    private void UnloadPreviousPreview()
    {
        _previewControl.ClearPreview();

        if (_loadContext is null)
        {
            return;
        }

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
            var shared = TryFindAlreadyLoadedByName(assemblyName);
            if (shared is not null && IsSharedContractAssembly(assemblyName.Name))
            {
                return shared;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is not null)
            {
                return LoadFromAssemblyPath(path);
            }

            return shared;
        }

        private static bool IsSharedContractAssembly(string? assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return false;
            }

            return assemblyName.StartsWith("ThreeDEngine", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Equals("PreviewerApp", StringComparison.OrdinalIgnoreCase);
        }

        private static Assembly? TryFindAlreadyLoadedByName(AssemblyName assemblyName)
        {
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                var loadedName = loaded.GetName();
                if (string.Equals(loadedName.Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            return null;
        }
    }
}
#endif
