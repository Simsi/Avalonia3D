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
    private async Task ExportSourceExperimentalAsync()
    {
        var target = _sourceTargetInfo ?? ResolveSourceTarget(_sourcePatchDirectory, _sourceTypeFullName);
        _sourceTargetInfo = target;
        if (target is null)
        {
            SetStatus("Source class was not found. Use Copy scene code instead.", isError: true);
            return;
        }

        try
        {
            if (_sourceExportHandler is null)
            {
                SetStatus("Roslyn source exporter is not configured. Start the debugger through PreviewerApp/VSIX.", isError: true);
                return;
            }

            var refreshed = ResolveSourceTarget(Path.GetDirectoryName(target.FilePath), _sourceTypeFullName) ?? target;
            var modeText = refreshed.HasBuildMethod
                ? "partial replacement of the Build(CompositeBuilder3D builder) method"
                : "insertion of a new Build(CompositeBuilder3D builder) method into the selected class";

            var previewRequest = new DebuggerSourceExportRequest(
                refreshed.FilePath,
                refreshed.ClassName,
                _sourceTypeFullName,
                refreshed.Line,
                refreshed.ClassStart,
                refreshed.HasBuildMethod,
                BuildGeneratedBuildMethod(refreshed.BuildParameterName ?? "builder", refreshed.Indent),
                BuildGeneratedClass(refreshed.ClassName, refreshed.Indent),
                BuildGeneratedEventMembers(refreshed.Indent),
                previewOnly: true);

            var preview = await _sourceExportHandler(previewRequest);
            if (!preview.Success)
            {
                SetStatus(preview.Message, isError: true);
                return;
            }

            var confirmed = await ConfirmDestructiveSourceExportAsync(refreshed, modeText, preview.DiffPreview);
            if (!confirmed)
            {
                SetStatus("Source export cancelled.", isError: false);
                return;
            }

            var applyRequest = new DebuggerSourceExportRequest(
                refreshed.FilePath,
                refreshed.ClassName,
                _sourceTypeFullName,
                refreshed.Line,
                refreshed.ClassStart,
                refreshed.HasBuildMethod,
                previewRequest.GeneratedBuildMethodSource,
                previewRequest.GeneratedClassSource,
                previewRequest.GeneratedEventMembersSource,
                previewOnly: false);

            var result = await _sourceExportHandler(applyRequest);
            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            _sourceTargetInfo = ResolveSourceTarget(Path.GetDirectoryName(result.FilePath), _sourceTypeFullName);
            _sourceTargetBox.Text = $"Rewritten with Roslyn: {result.FilePath}\nBackup: {result.BackupPath}\nMode: {result.Mode}";
            SetStatus(result.Message, isError: false);
        }
        catch (Exception ex)
        {
            SetStatus("Roslyn source export failed: " + ex.Message, isError: true);
        }
    }

    private async Task<bool> ConfirmDestructiveSourceExportAsync(SourceTargetInfo target, string modeText, string diffPreview)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var dialog = new Window
        {
            Title = "Experimental source export",
            Width = 860d,
            Height = 640d,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(18d),
            Child = new StackPanel
            {
                Spacing = 12d,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Эта операция полностью заменит код изменяемого класса. Рекомендуется использовать для быстрого прототипирования 3D представления объекта перед последующим наполнением класса.",
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = $"Target: {target.FilePath}\nClass: {target.ClassName}\nMode: {modeText}\n\nA .3ddebugger.bak backup will be written next to the source file before the rewrite.",
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = FontFamily.Parse("Consolas"),
                        FontSize = 12d
                    },
                    new TextBox
                    {
                        Text = string.IsNullOrWhiteSpace(diffPreview) ? "No diff preview was produced." : diffPreview,
                        AcceptsReturn = true,
                        IsReadOnly = true,
                        TextWrapping = TextWrapping.NoWrap,
                        FontFamily = FontFamily.Parse("Consolas"),
                        FontSize = 11d,
                        MinHeight = 280d
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8d,
                        Children =
                        {
                            CreateDialogButton(dialog, "Cancel", false),
                            CreateDialogButton(dialog, "I understand, rewrite source", true)
                        }
                    }
                }
            }
        };

        if (owner is not null)
        {
            return await dialog.ShowDialog<bool>(owner);
        }

        dialog.Show();
        return false;

        static Button CreateDialogButton(Window ownerDialog, string text, bool result)
        {
            var button = new Button { Content = text, MinWidth = result ? 190d : 90d };
            button.Click += (_, _) => ownerDialog.Close(result);
            return button;
        }
    }


    private string BuildGeneratedClass(string className, string indent)
    {
        var sb = new StringBuilder();
        sb.Append(indent).Append("public sealed class ").Append(className).Append(" : CompositeObject3D").AppendLine();
        sb.Append(indent).AppendLine("{");
        sb.Append(BuildGeneratedBuildMethod("builder", indent + "    "));
        sb.Append(indent).AppendLine("}");
        return sb.ToString();
    }

    private string BuildGeneratedBuildMethod(string builderName, string indent)
    {
        var sb = new StringBuilder();
        sb.Append(indent).AppendLine("protected override void Build(CompositeBuilder3D " + builderName + ")");
        sb.Append(indent).AppendLine("{");
        foreach (var line in BuildCompositeBuildBody(builderName).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(indent).Append("    ").AppendLine(line);
        }
        sb.Append(indent).AppendLine("}");
        return sb.ToString();
    }

    private string BuildCompositeBuildBody(string builderName)
    {
        var parts = GetExportObjectsForCompositeBuild().ToArray();
        if (parts.Length == 0)
        {
            return "// No debugger objects were available for export.";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var obj = parts[i];
            var variable = "part" + (i + 1).ToString(CultureInfo.InvariantCulture);
            var name = string.IsNullOrWhiteSpace(obj.Name) ? variable : obj.Name;
            var construction = BuildPrimitiveConstructionExpression(obj);
            if (construction.StartsWith("/*", StringComparison.Ordinal))
            {
                sb.AppendLine("// Unsupported debugger object: " + obj.GetType().FullName);
                continue;
            }

            sb.Append("var ").Append(variable).Append(" = ").Append(builderName).Append(".Add(\"").Append(Escape(name)).Append("\", ").Append(construction).AppendLine(");");
            AppendEventSubscriptionLines(sb, obj, variable);
            sb.Append(variable).Append(".At(").Append(FormatFloatCode(obj.Position.X)).Append(", ").Append(FormatFloatCode(obj.Position.Y)).Append(", ").Append(FormatFloatCode(obj.Position.Z)).AppendLine(");");
            sb.Append(variable).Append(".Rotate(").Append(FormatFloatCode(obj.RotationDegrees.X)).Append(", ").Append(FormatFloatCode(obj.RotationDegrees.Y)).Append(", ").Append(FormatFloatCode(obj.RotationDegrees.Z)).AppendLine(");");
            sb.Append(variable).Append(".WithScale(new Vector3(").Append(FormatFloatCode(obj.Scale.X)).Append(", ").Append(FormatFloatCode(obj.Scale.Y)).Append(", ").Append(FormatFloatCode(obj.Scale.Z)).AppendLine("));");
            sb.Append(variable).Append(".Color(new ColorRgba(")
                .Append(FormatFloatCode(obj.Material.BaseColor.R)).Append(", ")
                .Append(FormatFloatCode(obj.Material.BaseColor.G)).Append(", ")
                .Append(FormatFloatCode(obj.Material.BaseColor.B)).Append(", ")
                .Append(FormatFloatCode(obj.Material.BaseColor.A)).AppendLine("));");
            sb.Append(variable).Append(".Pickable(").Append(FormatBool(obj.IsPickable)).AppendLine(");");
            sb.Append(variable).Append(".Visible(").Append(FormatBool(obj.IsVisible)).AppendLine(");");
            sb.Append(variable).Append(".Manipulation(").Append(FormatBool(obj.IsManipulationEnabled)).AppendLine(");");
            sb.Append(variable).Append(".Object.Material.Opacity = ").Append(FormatFloatCode(obj.Material.Opacity)).AppendLine(";");
            sb.Append(variable).Append(".Object.Material.Lighting = LightingMode.").Append(obj.Material.Lighting).AppendLine(";");
            sb.Append(variable).Append(".Object.Material.Surface = SurfaceMode.").Append(obj.Material.Surface).AppendLine(";");
            sb.Append(variable).Append(".Object.Material.CullMode = CullMode.").Append(obj.Material.CullMode).AppendLine(";");
            AppendRigidbodyBuildLines(sb, variable + ".Object", obj.Rigidbody);
            if (i + 1 < parts.Length)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private void AppendEventSubscriptionLines(StringBuilder sb, Object3D obj, string variable)
    {
        foreach (var binding in GetEventBindingsForObject(obj))
        {
            var handlerName = ResolveEventHandlerName(variable, binding.EventName);
            sb.Append(variable).Append(".Object.").Append(binding.EventName).Append(" += ").Append(handlerName).AppendLine(";");
        }
    }

    private string BuildGeneratedEventMembers(string indent)
    {
        var parts = GetExportObjectsForCompositeBuild().ToArray();
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var variable = "part" + (i + 1).ToString(CultureInfo.InvariantCulture);
            foreach (var binding in GetEventBindingsForObject(parts[i]))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(BuildEventHandlerSource(parts[i], binding.EventName, ResolveEventHandlerName(variable, binding.EventName), binding.Body, indent));
            }
        }

        return sb.ToString();
    }

    private IEnumerable<DebugEventBinding> GetEventBindingsForObject(Object3D obj)
    {
        foreach (var binding in _eventBindings.Values)
        {
            if (string.Equals(binding.ObjectId, obj.Id, StringComparison.Ordinal))
            {
                yield return binding;
            }
        }
    }

    private static string BuildEventHandlerSource(Object3D obj, string eventName, string handlerName, string body, string indent)
    {
        var sb = new StringBuilder();
        sb.Append(indent).Append("private void ").Append(handlerName).AppendLine("(object? sender, ScenePointerEventArgs e)");
        sb.Append(indent).AppendLine("{");
        var normalized = string.IsNullOrWhiteSpace(body) ? DefaultEventBody(eventName) : body.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            sb.Append(indent).Append("    ").AppendLine(line);
        }
        sb.Append(indent).AppendLine("}");
        return sb.ToString();
    }

    private static string ResolveEventHandlerName(string variable, string eventName)
        => "On" + ToPascalIdentifier(variable) + ToPascalIdentifier(eventName);

    private static string ToPascalIdentifier(string value)
    {
        var sb = new StringBuilder();
        var capitalize = true;
        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                capitalize = true;
                continue;
            }

            sb.Append(capitalize ? char.ToUpperInvariant(ch) : ch);
            capitalize = false;
        }

        return sb.Length == 0 ? "Generated" : sb.ToString();
    }

    private IEnumerable<Object3D> GetExportObjectsForCompositeBuild()
    {
        var workbench = _viewport.Scene.Objects
            .Where(o => ReferenceEquals(o.DataContext, DebugWorkbenchTag.Instance))
            .ToArray();
        if (workbench.Length > 0)
        {
            return workbench;
        }

        var targetRoot = FindTargetCompositeRoot();
        if (targetRoot is not null)
        {
            return targetRoot.Children.Where(o => !ReferenceEquals(o.DataContext, DebugGuideTag.Instance));
        }

        if (_selectedObject is not null && !ReferenceEquals(_selectedObject.DataContext, DebugGuideTag.Instance))
        {
            return new[] { _selectedObject };
        }

        return _viewport.Scene.Objects.Where(o => !ReferenceEquals(o.DataContext, DebugGuideTag.Instance));
    }

    private CompositeObject3D? FindTargetCompositeRoot()
    {
        if (string.IsNullOrWhiteSpace(_sourceTypeFullName))
        {
            return _viewport.Scene.Objects.OfType<CompositeObject3D>().FirstOrDefault();
        }

        var fullName = _sourceTypeFullName.Replace('+', '.');
        var shortName = fullName.Split('.').LastOrDefault() ?? fullName;
        foreach (var obj in _viewport.Scene.Objects.OfType<CompositeObject3D>())
        {
            var type = obj.GetType();
            var candidateFull = (type.FullName ?? type.Name).Replace('+', '.');
            if (string.Equals(candidateFull, fullName, StringComparison.Ordinal) || string.Equals(type.Name, shortName, StringComparison.Ordinal))
            {
                return obj;
            }
        }

        return _viewport.Scene.Objects.OfType<CompositeObject3D>().FirstOrDefault();
    }

    private string BuildSceneWorkbenchSnippet()
    {
        var roots = _viewport.Scene.Objects
            .Where(o => ReferenceEquals(o.DataContext, DebugWorkbenchTag.Instance))
            .ToArray();
        if (roots.Length == 0 && _selectedObject is not null)
        {
            roots = new[] { _selectedObject };
        }

        if (roots.Length == 0)
        {
            return "// No workbench-created or selected object to export.";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < roots.Length; i++)
        {
            var variable = "obj" + (i + 1).ToString(CultureInfo.InvariantCulture);
            sb.AppendLine(BuildObjectConstructionSnippet(roots[i], variable));
        }

        return sb.ToString();
    }

    private string BuildDiagnosticsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("3DEngine Debugger diagnostics");
        sb.AppendLine(_sceneSummaryText.Text ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("Selected:");
        sb.AppendLine(_selectionDetailsText.Text ?? "none");
        sb.AppendLine();
        sb.AppendLine("Objects:");
        foreach (var entry in _listedObjects)
        {
            sb.AppendLine(entry.Path + " | " + entry.Object.GetType().FullName + " | visible=" + entry.Object.IsVisible);
        }

        return sb.ToString();
    }

    private static IEnumerable<Object3D> EnumerateObjects(Scene3D scene)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in scene.Objects)
        {
            foreach (var obj in EnumerateObject(root, seen))
            {
                yield return obj;
            }
        }
    }

    private static IEnumerable<Object3D> EnumerateObject(Object3D root, HashSet<string> seen)
    {
        if (!seen.Add(root.Id))
        {
            yield break;
        }

        yield return root;
        if (root is CompositeObject3D composite)
        {
            foreach (var child in composite.Children)
            {
                foreach (var nested in EnumerateObject(child, seen))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string? ResolveSourcePatchDirectory(string? assemblyPath, string? projectPath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var fullProjectPath = Path.GetFullPath(projectPath);
            if (File.Exists(fullProjectPath))
            {
                return Path.GetDirectoryName(fullProjectPath);
            }

            if (Directory.Exists(fullProjectPath))
            {
                return fullProjectPath;
            }
        }

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return null;
        }

        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath) ?? Environment.CurrentDirectory);
        for (var current = dir; current is not null; current = current.Parent)
        {
            if (current.GetFiles("*.csproj").Length > 0)
            {
                return current.FullName;
            }

            if (current.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) && current.Parent is not null)
            {
                return current.Parent.FullName;
            }
        }

        return dir.FullName;
    }

    private static SourceTargetInfo? ResolveSourceTarget(string? searchRoot, string? typeFullName)
    {
        if (string.IsNullOrWhiteSpace(searchRoot) || string.IsNullOrWhiteSpace(typeFullName) || !Directory.Exists(searchRoot))
        {
            return null;
        }

        var normalized = typeFullName.Trim().Replace('+', '.');
        var className = normalized.Split('.').LastOrDefault();
        if (string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        var files = Directory.EnumerateFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path).Equals(className + ".cs", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            SourceTargetInfo? info = null;
            try
            {
                var text = File.ReadAllText(file);
                info = TryResolveSourceTargetInFile(file, text, className);
            }
            catch
            {
                // Ignore unreadable source candidates.
            }

            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }

    private static SourceTargetInfo? TryResolveSourceTargetInFile(string filePath, string source, string className)
    {
        var classPattern = @"\b(?:public|internal|private|protected|sealed|abstract|partial|static|new|unsafe|record|\s)+class\s+" + Regex.Escape(className) + @"\b|\bclass\s+" + Regex.Escape(className) + @"\b";
        foreach (Match match in Regex.Matches(source, classPattern, RegexOptions.Multiline))
        {
            var openBrace = source.IndexOf('{', match.Index + match.Length);
            if (openBrace < 0)
            {
                continue;
            }

            var closeBrace = FindMatchingBrace(source, openBrace);
            if (closeBrace < 0)
            {
                continue;
            }

            var method = TryFindBuildMethod(source, openBrace, closeBrace);
            var indent = GetLineIndent(source, match.Index);
            var line = CountLineNumber(source, match.Index);
            return new SourceTargetInfo(filePath, className, match.Index, closeBrace, openBrace, closeBrace, indent, line, method.Start, method.End, method.ParameterName);
        }

        return null;
    }

    private static (int Start, int End, string? ParameterName) TryFindBuildMethod(string source, int classOpenBrace, int classCloseBrace)
    {
        var classBodyStart = classOpenBrace + 1;
        var classBody = source.Substring(classBodyStart, Math.Max(0, classCloseBrace - classBodyStart));
        var regex = new Regex(@"\bprotected\s+override\s+void\s+Build\s*\(\s*(?:global::ThreeDEngine\.Core\.Scene\.)?CompositeBuilder3D\s+(?<param>[A-Za-z_][A-Za-z0-9_]*)\s*\)", RegexOptions.Multiline);
        var match = regex.Match(classBody);
        if (!match.Success)
        {
            return (-1, -1, null);
        }

        var absoluteMethodStart = classBodyStart + match.Index;
        var openBrace = source.IndexOf('{', absoluteMethodStart + match.Length);
        if (openBrace < 0 || openBrace > classCloseBrace)
        {
            return (-1, -1, null);
        }

        var closeBrace = FindMatchingBrace(source, openBrace);
        if (closeBrace < 0 || closeBrace > classCloseBrace)
        {
            return (-1, -1, null);
        }

        return (absoluteMethodStart, closeBrace, match.Groups["param"].Value);
    }

    private static int FindMatchingBrace(string source, int openBraceIndex)
    {
        var depth = 0;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;
        for (var i = openBraceIndex; i < source.Length; i++)
        {
            var ch = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (inLineComment)
            {
                if (ch is '\r' or '\n') inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }

            if (inString)
            {
                if (ch == '\\') { i++; continue; }
                if (ch == '"') inString = false;
                continue;
            }

            if (inChar)
            {
                if (ch == '\\') { i++; continue; }
                if (ch == '\'') inChar = false;
                continue;
            }

            if (ch == '/' && next == '/') { inLineComment = true; i++; continue; }
            if (ch == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (ch == '"') { inString = true; continue; }
            if (ch == '\'') { inChar = true; continue; }
            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private static string GetLineIndent(string source, int index)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, index - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var i = lineStart;
        while (i < source.Length && char.IsWhiteSpace(source[i]) && source[i] is not '\r' and not '\n')
        {
            i++;
        }

        return source[lineStart..i];
    }

    private static int CountLineNumber(string source, int index)
    {
        var line = 1;
        var limit = System.Math.Clamp(index, 0, source.Length);
        for (var i = 0; i < limit; i++)
        {
            if (source[i] == '\n') line++;
        }

        return line;
    }

}
