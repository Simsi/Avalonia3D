using System;
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
    private readonly AsyncPackage _package;

    private Open3DPreviewCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;

        var commandSet = new Guid(Open3DPreviewCommandPackageGuids.CommandSetGuidString);

        commandService.AddCommand(new OleMenuCommand(
            Execute,
            new CommandID(commandSet, Open3DPreviewCommandPackageIds.Open3DPreviewCommandId)));

        commandService.AddCommand(new OleMenuCommand(
            Execute,
            new CommandID(commandSet, Open3DPreviewCommandPackageIds.Open3DPreviewContextCommandId)));
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

    private async Task ExecuteAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try
        {
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte?.ActiveDocument is null)
            {
                ShowMessage("Open a C# source file and place the caret inside a previewable class.", OLEMSGICON.OLEMSGICON_INFO);
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
                ShowMessage("Place the caret inside a class declaration. The class should inherit CompositeObject3D or contain [Preview3D] previews.", OLEMSGICON.OLEMSGICON_INFO);
                return;
            }

            if (!previewType.IsPreviewCandidate)
            {
                ShowMessage($"'{previewType.FullName}' does not look previewable. Add ': CompositeObject3D' or at least one [Preview3D] method.", OLEMSGICON.OLEMSGICON_INFO);
                return;
            }

            var projectPath = project.FullName;
            var projectDir = Path.GetDirectoryName(projectPath)!;
            var previewerProject = LocatePreviewerProject(projectDir);
            if (previewerProject is null)
            {
                ShowMessage("Could not find PreviewerApp\\PreviewerApp.csproj near the main Avalonia3D project. Make sure PreviewerApp is placed next to Views and 3DEngine.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var buildOk = await BuildProjectAsync(projectPath, projectDir);
            if (!buildOk)
            {
                ShowMessage("Project build failed. Fix build errors before opening the 3D preview.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var assemblyPath = LocateAssembly(projectPath, projectDir);
            if (assemblyPath is null)
            {
                ShowMessage("Project built, but the output assembly was not found under bin\\Debug or bin\\Release.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            LaunchPreviewer(previewerProject, assemblyPath, previewType.FullName, projectPath, projectDir);
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
        var namespaceName = FindNamespace(source);
        var matches = Regex.Matches(
            source,
            @"(?<attributes>(?:\s*\[[^\]]+\]\s*)*)(?<declaration>\b(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<tail>[^\{;]*)\{)",
            RegexOptions.Multiline);

        PreviewTypeInfo? best = null;

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

            var containsCaret = cursorOffset >= match.Index && cursorOffset <= closeBraceIndex;
            if (!containsCaret)
            {
                continue;
            }

            var className = match.Groups["name"].Value;
            var fullName = string.IsNullOrWhiteSpace(namespaceName) ? className : namespaceName + "." + className;
            var classText = source.Substring(match.Index, Math.Max(0, closeBraceIndex - match.Index + 1));
            var tail = match.Groups["tail"].Value;
            var attributes = match.Groups["attributes"].Value;
            var isPreviewCandidate =
                tail.Contains("CompositeObject3D") ||
                attributes.Contains("Preview3D") ||
                classText.Contains("[Preview3D") ||
                classText.Contains("Preview3DAttribute");

            best = new PreviewTypeInfo(fullName, isPreviewCandidate);
        }

        return best;
    }

    private static string? FindNamespace(string source)
    {
        var fileScoped = Regex.Match(source, @"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;", RegexOptions.Multiline);
        if (fileScoped.Success)
        {
            return fileScoped.Groups[1].Value;
        }

        var blockScopedMatches = Regex.Matches(source, @"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*\{", RegexOptions.Multiline);
        if (blockScopedMatches.Count > 0)
        {
            return blockScopedMatches[blockScopedMatches.Count - 1].Groups[1].Value;
        }

        return null;
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

    private static string? LocatePreviewerProject(string projectDir)
    {
        var direct = Path.Combine(projectDir, "PreviewerApp", "PreviewerApp.csproj");
        if (File.Exists(direct))
        {
            return direct;
        }

        var parent = Directory.GetParent(projectDir)?.FullName;
        if (parent is not null)
        {
            var nested = Path.Combine(parent, "Avalonia3D", "PreviewerApp", "PreviewerApp.csproj");
            if (File.Exists(nested))
            {
                return nested;
            }
        }

        return null;
    }

    private static async Task<bool> BuildProjectAsync(string projectPath, string workingDirectory)
    {
        var arguments = "build " + Quote(projectPath);
        var exitCode = await RunProcessAsync("dotnet", arguments, workingDirectory);
        return exitCode == 0;
    }

    private static string? LocateAssembly(string projectPath, string projectDir)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var candidates = Directory
            .EnumerateFiles(Path.Combine(projectDir, "bin"), projectName + ".dll", SearchOption.AllDirectories)
            .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
            .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "refint" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        return candidates.FirstOrDefault();
    }

    private static void LaunchPreviewer(string previewerProject, string assemblyPath, string typeFullName, string projectPath, string workingDirectory)
    {
        var arguments = new StringBuilder();
        arguments.Append("run --project ").Append(Quote(previewerProject)).Append(" -- ");
        arguments.Append("--assembly ").Append(Quote(assemblyPath)).Append(' ');
        arguments.Append("--type ").Append(Quote(typeFullName)).Append(' ');
        arguments.Append("--project ").Append(Quote(projectPath));

        var startInfo = new DiagnosticsProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments.ToString(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        DiagnosticsProcess.Start(startInfo);
    }

    private static Task<int> RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        var tcs = new TaskCompletionSource<int>();
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
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.Exited += (_, _) =>
        {
            var exitCode = process.ExitCode;
            process.Dispose();
            tcs.TrySetResult(exitCode);
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
            tcs.TrySetException(ex);
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
}
