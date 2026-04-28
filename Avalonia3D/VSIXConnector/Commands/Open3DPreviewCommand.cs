using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ThreeDEngine.PreviewerVsix.Commands;

internal sealed class Open3DPreviewCommand
{
    private const int MaxMessageLength = 3600;
    private readonly AsyncPackage _package;

    private Open3DPreviewCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;

        RegisterCommand(commandService, Open3DPreviewCommandPackageIds.Open3DPreviewCommandId, Execute);
        RegisterCommand(commandService, Open3DPreviewCommandPackageIds.ShowDiagnosticsCommandId, ExecuteDiagnostics);
    }

    private static void RegisterCommand(OleMenuCommandService commandService, int commandId, EventHandler handler)
    {
        var commandSet = new Guid(Open3DPreviewCommandPackageGuids.CommandSetGuidString);
        var menuCommand = new OleMenuCommand(handler, new CommandID(commandSet, commandId));
        menuCommand.BeforeQueryStatus += OnBeforeQueryStatus;
        commandService.AddCommand(menuCommand);
    }

    private static void OnBeforeQueryStatus(object sender, EventArgs e)
    {
        if (sender is OleMenuCommand command)
        {
            command.Visible = true;
            command.Enabled = true;
            command.Text = command.CommandID.ID == Open3DPreviewCommandPackageIds.ShowDiagnosticsCommandId
                ? "Show 3DEngine Previewer Diagnostics"
                : "Open 3D Preview";
        }
    }

    public static Task InitializeAsync(AsyncPackage package, OleMenuCommandService commandService)
    {
        _ = new Open3DPreviewCommand(package, commandService);
        return Task.CompletedTask;
    }

    private void Execute(object sender, EventArgs e)
    {
        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await ExecuteAsync();
        }).FileAndForget("ThreeDEngine/Previewer/Open3DPreview");
    }

    private void ExecuteDiagnostics(object sender, EventArgs e)
    {
        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await ShowDiagnosticsAsync();
        }).FileAndForget("ThreeDEngine/Previewer/Diagnostics");
    }

    private async Task ShowDiagnosticsAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var builder = new StringBuilder();
        builder.AppendLine("3DEngine Previewer Connector diagnostics");
        builder.AppendLine("VSIX assembly: " + typeof(Open3DPreviewCommand).Assembly.FullName);
        builder.AppendLine("Package GUID: " + Open3DPreviewCommandPackageGuids.PackageGuidString);
        builder.AppendLine("Command set GUID: " + Open3DPreviewCommandPackageGuids.CommandSetGuidString);
        builder.AppendLine("Open command ID: 0x" + Open3DPreviewCommandPackageIds.Open3DPreviewCommandId.ToString("X4"));
        builder.AppendLine("Diagnostics command ID: 0x" + Open3DPreviewCommandPackageIds.ShowDiagnosticsCommandId.ToString("X4"));
        builder.AppendLine();

        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
        if (dte is null)
        {
            builder.AppendLine("DTE service: not available");
        }
        else
        {
            builder.AppendLine("Solution: " + (dte.Solution?.FullName ?? "<none>"));
            builder.AppendLine("Active document: " + (dte.ActiveDocument?.FullName ?? "<none>"));
            builder.AppendLine("Active project: " + (dte.ActiveDocument?.ProjectItem?.ContainingProject?.FullName ?? "<none>"));

            var projectPath = dte.ActiveDocument?.ProjectItem?.ContainingProject?.FullName;
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                var projectDir = Path.GetDirectoryName(projectPath!);
                var solutionDir = TryGetSolutionDirectory(dte);
                var previewer = projectDir is null ? null : LocatePreviewerProject(projectDir, solutionDir);
                builder.AppendLine("PreviewerApp: " + (previewer ?? "not found"));
            }
        }

        builder.AppendLine();
        builder.AppendLine("Expected UI locations after install:");
        builder.AppendLine("- Extensions > 3DEngine > Open 3D Preview");
        builder.AppendLine("- Tools > Open 3D Preview");
        builder.AppendLine("- Command Window / keyboard command: Tools.Open3DPreview");
        builder.AppendLine("- Diagnostics command: Tools.ThreeDEnginePreviewerDiagnostics");

        ShowMessage(builder.ToString(), OLEMSGICON.OLEMSGICON_INFO);
    }

    private async Task ExecuteAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try
        {
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte?.ActiveDocument is null)
            {
                ShowMessage("Open a C# source file and place the caret inside a previewable 3D class.", OLEMSGICON.OLEMSGICON_INFO);
                return;
            }

            var activeDocument = dte.ActiveDocument;
            var project = activeDocument.ProjectItem?.ContainingProject;
            if (project is null || string.IsNullOrWhiteSpace(project.FullName))
            {
                ShowMessage("Could not resolve the containing project for the active document.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var source = TryReadDocumentText(activeDocument, out var cursorOffset);
            if (string.IsNullOrWhiteSpace(source))
            {
                ShowMessage("Could not read the active C# source file.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var previewType = FindPreviewType(source!, cursorOffset);
            if (previewType is null)
            {
                ShowMessage("Place the caret inside a C# class. The previewer can open CompositeObject3D classes and classes with [Preview3D] methods.", OLEMSGICON.OLEMSGICON_INFO);
                return;
            }

            var projectPath = project.FullName;
            var projectDir = Path.GetDirectoryName(projectPath)!;
            var solutionDir = TryGetSolutionDirectory(dte);
            var configuration = GetActiveConfiguration(project, dte);

            var previewerProject = LocatePreviewerProject(projectDir, solutionDir);
            if (previewerProject is null)
            {
                ShowMessage(
                    "Could not find PreviewerApp\\PreviewerApp.csproj.\n\n" +
                    "Expected layout:\n" +
                    "  Avalonia3D.csproj\n" +
                    "  3DEngine\\...\n" +
                    "  PreviewerApp\\PreviewerApp.csproj\n" +
                    "  VSIXConnector\\...\n\n" +
                    "If your project is in a different folder, place PreviewerApp next to the main host csproj.",
                    OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var hostBuild = await BuildProjectAsync(projectPath, projectDir, configuration);
            if (!hostBuild.Success)
            {
                ShowMessage("Host project build failed.\n\n" + hostBuild.GetTail(), OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var assemblyPath = LocateAssembly(projectPath, projectDir, configuration);
            if (assemblyPath is null)
            {
                ShowMessage("Host project built, but the output assembly was not found under bin\\" + configuration + ".", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var previewerBuild = await BuildPreviewerAsync(previewerProject, projectPath, configuration);
            if (!previewerBuild.Success)
            {
                ShowMessage("PreviewerApp build failed.\n\n" + previewerBuild.GetTail(), OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var previewerOutput = LocatePreviewerOutput(previewerProject, configuration);
            if (previewerOutput is null)
            {
                ShowMessage("PreviewerApp built, but PreviewerApp.exe/dll was not found under bin\\" + configuration + ".", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            LaunchPreviewer(previewerOutput, assemblyPath, previewType.FullName, projectPath, Path.GetDirectoryName(previewerProject)!);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.ToString(), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private string? TryReadDocumentText(Document document, out int cursorOffset)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        cursorOffset = 0;

        try
        {
            if (document.Object("TextDocument") is TextDocument textDocument)
            {
                var start = textDocument.StartPoint.CreateEditPoint();
                var text = start.GetText(textDocument.EndPoint);

                if (textDocument.Selection is TextSelection selection)
                {
                    cursorOffset = CalculateOffset(text, selection.ActivePoint.Line, selection.ActivePoint.LineCharOffset);
                }

                return text;
            }
        }
        catch
        {
            // Fall back to file content below.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(document.FullName) && File.Exists(document.FullName))
            {
                return File.ReadAllText(document.FullName);
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static int CalculateOffset(string text, int oneBasedLine, int oneBasedColumn)
    {
        if (oneBasedLine <= 1 && oneBasedColumn <= 1)
        {
            return 0;
        }

        var line = 1;
        var column = 1;

        for (var i = 0; i < text.Length; i++)
        {
            if (line == oneBasedLine && column == oneBasedColumn)
            {
                return i;
            }

            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return text.Length;
    }

    private static PreviewTypeInfo? FindPreviewType(string source, int cursorOffset)
    {
        var classes = FindClassSpans(source);
        if (classes.Count == 0)
        {
            return null;
        }

        var containing = classes
            .Where(c => cursorOffset >= c.MatchStart && cursorOffset <= c.CloseBrace)
            .OrderBy(c => c.MatchStart)
            .ToList();

        if (containing.Count == 0)
        {
            return null;
        }

        var innermost = containing[containing.Count - 1];
        var namespaceName = FindNamespaceForOffset(source, innermost.MatchStart);
        var metadataNames = containing.Select(c => c.MetadataName).ToArray();
        var fullName = string.Join("+", metadataNames);
        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            fullName = namespaceName + "." + fullName;
        }

        var classText = source.Substring(innermost.MatchStart, Math.Max(0, innermost.CloseBrace - innermost.MatchStart + 1));
        var isPreviewCandidate =
            innermost.Tail.IndexOf("CompositeObject3D", StringComparison.OrdinalIgnoreCase) >= 0 ||
            innermost.Attributes.IndexOf("Preview3D", StringComparison.OrdinalIgnoreCase) >= 0 ||
            classText.IndexOf("[Preview3D", StringComparison.OrdinalIgnoreCase) >= 0 ||
            classText.IndexOf("Preview3DAttribute", StringComparison.OrdinalIgnoreCase) >= 0;

        return new PreviewTypeInfo(fullName, isPreviewCandidate);
    }

    private static List<ClassSpan> FindClassSpans(string source)
    {
        var result = new List<ClassSpan>();
        var matches = Regex.Matches(
            source,
            @"(?<attributes>(?:\s*\[[^\]]+\]\s*)*)(?<declaration>\b(?:(?:public|internal|private|protected|sealed|abstract|static|partial|new)\s+)*(?:(?:record)\s+)?class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<typeparams>\s*<[^>{}]+>)?(?<tail>[^\{;]*)\{)",
            RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            var openBraceIndex = source.IndexOf('{', match.Index + match.Length - 1);
            if (openBraceIndex < 0)
            {
                continue;
            }

            var closeBraceIndex = FindMatchingBrace(source, openBraceIndex);
            if (closeBraceIndex < 0)
            {
                closeBraceIndex = source.Length - 1;
            }

            var name = match.Groups["name"].Value;
            var typeParams = match.Groups["typeparams"].Value;
            var arity = CountGenericArity(typeParams);
            var metadataName = arity > 0 ? name + "`" + arity : name;

            result.Add(new ClassSpan(
                match.Index,
                openBraceIndex,
                closeBraceIndex,
                metadataName,
                match.Groups["tail"].Value,
                match.Groups["attributes"].Value));
        }

        return result;
    }

    private static int CountGenericArity(string typeParams)
    {
        if (string.IsNullOrWhiteSpace(typeParams))
        {
            return 0;
        }

        var trimmed = typeParams.Trim();
        if (!trimmed.StartsWith("<", StringComparison.Ordinal) || !trimmed.EndsWith(">", StringComparison.Ordinal))
        {
            return 0;
        }

        var depth = 0;
        var count = 1;
        for (var i = 1; i < trimmed.Length - 1; i++)
        {
            var c = trimmed[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0) count++;
        }

        return count;
    }

    private static string? FindNamespaceForOffset(string source, int offset)
    {
        var fileScoped = Regex.Match(source, @"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;", RegexOptions.Multiline);
        if (fileScoped.Success)
        {
            return fileScoped.Groups[1].Value;
        }

        var bestName = (string?)null;
        var bestSpan = int.MaxValue;
        var blockScopedMatches = Regex.Matches(source, @"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*\{", RegexOptions.Multiline);
        foreach (Match match in blockScopedMatches)
        {
            var openBraceIndex = source.IndexOf('{', match.Index + match.Length - 1);
            if (openBraceIndex < 0)
            {
                continue;
            }

            var closeBraceIndex = FindMatchingBrace(source, openBraceIndex);
            if (closeBraceIndex < 0)
            {
                closeBraceIndex = source.Length - 1;
            }

            if (offset >= match.Index && offset <= closeBraceIndex)
            {
                var span = closeBraceIndex - match.Index;
                if (span < bestSpan)
                {
                    bestSpan = span;
                    bestName = match.Groups[1].Value;
                }
            }
        }

        return bestName;
    }

    private static int FindMatchingBrace(string text, int openBraceIndex)
    {
        var depth = 0;
        for (var i = openBraceIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string? TryGetSolutionDirectory(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var solutionPath = dte.Solution?.FullName;
            return string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
        }
        catch
        {
            return null;
        }
    }

    private static string GetActiveConfiguration(Project project, DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var configuration = project.ConfigurationManager?.ActiveConfiguration?.ConfigurationName;
            if (!string.IsNullOrWhiteSpace(configuration))
            {
                return configuration!;
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            var solutionConfiguration = dte.Solution?.SolutionBuild?.ActiveConfiguration?.Name;
            if (!string.IsNullOrWhiteSpace(solutionConfiguration))
            {
                var separator = solutionConfiguration!.IndexOf('|');
                return separator > 0 ? solutionConfiguration.Substring(0, separator) : solutionConfiguration;
            }
        }
        catch
        {
            // ignored
        }

        return "Debug";
    }

    private static string? LocatePreviewerProject(string projectDir, string? solutionDir)
    {
        var roots = new List<string>();
        AddRootWithParents(roots, projectDir);
        if (!string.IsNullOrWhiteSpace(solutionDir))
        {
            AddRootWithParents(roots, solutionDir!);
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var direct = Path.Combine(root, "PreviewerApp", "PreviewerApp.csproj");
            if (File.Exists(direct))
            {
                return direct;
            }

            var nested = Path.Combine(root, "Avalonia3D", "PreviewerApp", "PreviewerApp.csproj");
            if (File.Exists(nested))
            {
                return nested;
            }
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var found = Directory.EnumerateFiles(root, "PreviewerApp.csproj", SearchOption.AllDirectories)
                    .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                    .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "PreviewerApp", StringComparison.OrdinalIgnoreCase));
                if (found is not null)
                {
                    return found;
                }
            }
            catch
            {
                // Some solution roots may contain inaccessible folders. Ignore and continue.
            }
        }

        return null;
    }

    private static void AddRootWithParents(List<string> roots, string start)
    {
        var current = new DirectoryInfo(start);
        for (var i = 0; i < 5 && current is not null; i++)
        {
            roots.Add(current.FullName);
            current = current.Parent;
        }
    }

    private static Task<ProcessResult> BuildProjectAsync(string projectPath, string workingDirectory, string configuration)
    {
        var arguments = "build " + Quote(projectPath) + " -c " + Quote(configuration);
        return RunProcessAsync("dotnet", arguments, workingDirectory);
    }

    private static Task<ProcessResult> BuildPreviewerAsync(string previewerProject, string hostProjectPath, string configuration)
    {
        var workingDirectory = Path.GetDirectoryName(previewerProject)!;
        var arguments = new StringBuilder();
        arguments.Append("build ").Append(Quote(previewerProject));
        arguments.Append(" -c ").Append(Quote(configuration));
        arguments.Append(" -p:ThreeDEngineHostProject=").Append(Quote(hostProjectPath));
        return RunProcessAsync("dotnet", arguments.ToString(), workingDirectory);
    }

    private static string? LocateAssembly(string projectPath, string projectDir, string configuration)
    {
        var assemblyName = ReadProjectProperty(projectPath, "AssemblyName") ?? Path.GetFileNameWithoutExtension(projectPath);
        var binRoot = Path.Combine(projectDir, "bin");
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        var searchRoots = new List<string>();
        var configRoot = Path.Combine(binRoot, configuration);
        if (Directory.Exists(configRoot))
        {
            searchRoots.Add(configRoot);
        }
        searchRoots.Add(binRoot);

        foreach (var root in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidates = Directory
                .EnumerateFiles(root, assemblyName + ".dll", SearchOption.AllDirectories)
                .Where(IsRuntimeAssemblyCandidate)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();

            if (candidates.Length > 0)
            {
                return candidates[0];
            }
        }

        return null;
    }

    private static string? LocatePreviewerOutput(string previewerProject, string configuration)
    {
        var previewerDir = Path.GetDirectoryName(previewerProject)!;
        var binRoot = Path.Combine(previewerDir, "bin", configuration);
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        var exe = Directory.EnumerateFiles(binRoot, "PreviewerApp.exe", SearchOption.AllDirectories)
            .Where(IsRuntimeAssemblyCandidate)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (exe is not null)
        {
            return exe;
        }

        return Directory.EnumerateFiles(binRoot, "PreviewerApp.dll", SearchOption.AllDirectories)
            .Where(IsRuntimeAssemblyCandidate)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool IsRuntimeAssemblyCandidate(string path)
    {
        return path.IndexOf(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0 &&
               path.IndexOf(Path.DirectorySeparatorChar + "refint" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0 &&
               path.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static string? ReadProjectProperty(string projectPath, string propertyName)
    {
        try
        {
            var text = File.ReadAllText(projectPath);
            var match = Regex.Match(text, "<" + Regex.Escape(propertyName) + @">\s*(?<value>[^<]+?)\s*</" + Regex.Escape(propertyName) + ">", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void LaunchPreviewer(string previewerOutput, string assemblyPath, string typeFullName, string projectPath, string workingDirectory)
    {
        var arguments = new StringBuilder();
        var isDll = string.Equals(Path.GetExtension(previewerOutput), ".dll", StringComparison.OrdinalIgnoreCase);

        var fileName = isDll ? "dotnet" : previewerOutput;
        if (isDll)
        {
            arguments.Append(Quote(previewerOutput)).Append(' ');
        }

        arguments.Append("--assembly ").Append(Quote(assemblyPath)).Append(' ');
        arguments.Append("--type ").Append(Quote(typeFullName)).Append(' ');
        arguments.Append("--project ").Append(Quote(projectPath)).Append(' ');
        arguments.Append("--host-project ").Append(Quote(projectPath));

        var startInfo = new DiagnosticsProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments.ToString(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        DiagnosticsProcess.Start(startInfo);
    }

    private static Task<ProcessResult> RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        var tcs = new TaskCompletionSource<ProcessResult>();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var process = new DiagnosticsProcess
        {
            StartInfo = new DiagnosticsProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            var exitCode = process.ExitCode;
            process.Dispose();
            tcs.TrySetResult(new ProcessResult(exitCode, stdout.ToString(), stderr.ToString()));
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            process.Dispose();
            tcs.TrySetResult(new ProcessResult(-1, stdout.ToString(), stderr.ToString() + ex.ToString()));
        }

        return tcs.Task;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private void ShowMessage(string message, OLEMSGICON icon)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (message.Length > MaxMessageLength)
        {
            message = message.Substring(0, MaxMessageLength) + "\n...";
        }

        VsShellUtilities.ShowMessageBox(
            _package,
            message,
            "3DEngine Previewer",
            icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private sealed class PreviewTypeInfo
    {
        public PreviewTypeInfo(string fullName, bool isPreviewCandidate)
        {
            FullName = fullName;
            IsPreviewCandidate = isPreviewCandidate;
        }

        public string FullName { get; }
        public bool IsPreviewCandidate { get; }
    }

    private sealed class ClassSpan
    {
        public ClassSpan(int matchStart, int openBrace, int closeBrace, string metadataName, string tail, string attributes)
        {
            MatchStart = matchStart;
            OpenBrace = openBrace;
            CloseBrace = closeBrace;
            MetadataName = metadataName;
            Tail = tail;
            Attributes = attributes;
        }

        public int MatchStart { get; }
        public int OpenBrace { get; }
        public int CloseBrace { get; }
        public string MetadataName { get; }
        public string Tail { get; }
        public string Attributes { get; }
    }

    private sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public bool Success => ExitCode == 0;

        public string GetTail()
        {
            var combined = (StandardOutput + "\n" + StandardError).Trim();
            if (combined.Length <= MaxMessageLength)
            {
                return combined;
            }

            return combined.Substring(combined.Length - MaxMessageLength);
        }
    }
}
