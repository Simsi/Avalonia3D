#if THREE_DENGINE_PREVIEWER_APP || THREE_DENGINE_SMOKE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThreeDEngine.Avalonia.Preview;

namespace PreviewerApp;

internal static class RoslynDebuggerSourceExporter
{
    public static Task<DebuggerSourceExportResult> ExportAsync(DebuggerSourceExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return Task.FromResult(DebuggerSourceExportResult.Failed("Source file was not found: " + request.FilePath, request.FilePath));
        }

        try
        {
            var original = File.ReadAllText(request.FilePath, Encoding.UTF8);
            var tree = CSharpSyntaxTree.ParseText(original, path: request.FilePath);
            var root = tree.GetCompilationUnitRoot();
            if (root.ContainsDiagnostics)
            {
                var parseErrors = root.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(5)
                    .Select(d => d.ToString())
                    .ToArray();
                if (parseErrors.Length > 0)
                {
                    return Task.FromResult(DebuggerSourceExportResult.Failed(
                        "Roslyn could not parse the source file. Fix syntax errors first:\n" + string.Join("\n", parseErrors),
                        request.FilePath));
                }
            }

            var classNode = FindTargetClass(root, request);
            if (classNode is null)
            {
                return Task.FromResult(DebuggerSourceExportResult.Failed("Roslyn could not find class '" + request.ClassName + "' in source file.", request.FilePath));
            }

            var buildMethod = FindBuildMethod(classNode);
            var generatedMethod = SyntaxFactory.ParseMemberDeclaration(request.GeneratedBuildMethodSource) as MethodDeclarationSyntax;
            if (generatedMethod is null)
            {
                return Task.FromResult(DebuggerSourceExportResult.Failed("Generated Build method is not valid C# syntax.", request.FilePath));
            }

            CompilationUnitSyntax rewrittenRoot;
            string mode;
            if (buildMethod is not null)
            {
                generatedMethod = generatedMethod
                    .NormalizeWhitespace(elasticTrivia: true)
                    .WithLeadingTrivia(buildMethod.GetLeadingTrivia())
                    .WithTrailingTrivia(buildMethod.GetTrailingTrivia());
                rewrittenRoot = root.ReplaceNode(buildMethod, generatedMethod);
                mode = "Roslyn method replacement: Build(CompositeBuilder3D ...)";
            }
            else
            {
                generatedMethod = generatedMethod
                    .NormalizeWhitespace(elasticTrivia: true)
                    .WithLeadingTrivia(CreateInsertedMethodLeadingTrivia(classNode))
                    .WithTrailingTrivia(CreateInsertedMethodTrailingTrivia(classNode));

                var updatedClass = classNode.WithMembers(classNode.Members.Add(generatedMethod));
                rewrittenRoot = root.ReplaceNode(classNode, updatedClass);
                mode = "Roslyn method insertion: Build(CompositeBuilder3D ...)";
            }

            rewrittenRoot = ApplyGeneratedEventMembers(rewrittenRoot, classNode, request);
            rewrittenRoot = EnsureDebuggerUsings(rewrittenRoot);
            var rewritten = rewrittenRoot.NormalizeWhitespace(elasticTrivia: true).ToFullString();
            if (string.Equals(original, rewritten, StringComparison.Ordinal))
            {
                return Task.FromResult(DebuggerSourceExportResult.Failed("Roslyn produced no source changes.", request.FilePath));
            }

            var diffPreview = BuildUnifiedDiff(request.FilePath, original, rewritten);
            if (IsPreviewOnly(request))
            {
                return Task.FromResult(CreatePreviewResult(
                    "Roslyn source export preview generated. Review the diff before applying.",
                    request.FilePath,
                    mode,
                    diffPreview));
            }

            var backupPath = request.FilePath + ".3ddebugger.bak";
            File.WriteAllText(backupPath, original, Encoding.UTF8);
            File.WriteAllText(request.FilePath, rewritten, Encoding.UTF8);
            return Task.FromResult(CreateCompletedResult(
                "Experimental Roslyn source export completed. Rebuild the project to reload the debugger view.",
                request.FilePath,
                backupPath,
                mode,
                diffPreview));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DebuggerSourceExportResult.Failed("Roslyn source export failed: " + ex.Message, request.FilePath));
        }
    }


    private static bool IsPreviewOnly(DebuggerSourceExportRequest request)
    {
        // Keep PreviewerApp build-compatible with older source drops while still
        // using the v89+/v90 preview-first contract when it is present.
        var property = typeof(DebuggerSourceExportRequest).GetProperty("PreviewOnly");
        return property?.GetValue(request) is bool previewOnly && previewOnly;
    }

    private static DebuggerSourceExportResult CreatePreviewResult(string message, string filePath, string mode, string diffPreview)
    {
        var resultType = typeof(DebuggerSourceExportResult);
        var previewMethod = resultType.GetMethods()
            .FirstOrDefault(m =>
                string.Equals(m.Name, "Preview", StringComparison.Ordinal) &&
                m.IsStatic &&
                m.GetParameters().Length == 4);
        if (previewMethod is not null)
        {
            return (DebuggerSourceExportResult)previewMethod.Invoke(null, new object[] { message, filePath, mode, diffPreview })!;
        }

        var constructor = resultType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 6);
        if (constructor is not null)
        {
            return (DebuggerSourceExportResult)constructor.Invoke(new object?[] { true, message, filePath, null, mode, diffPreview });
        }

        return DebuggerSourceExportResult.Failed(
            "Roslyn source export preview was generated, but the loaded debugger contract does not expose preview-result support. " +
            "Apply the current 3DEngine/Avalonia/Preview/DebuggerSourceExportRequest.cs source drop and rebuild.\n\n" + diffPreview,
            filePath);
    }

    private static DebuggerSourceExportResult CreateCompletedResult(string message, string filePath, string backupPath, string mode, string diffPreview)
    {
        var resultType = typeof(DebuggerSourceExportResult);
        var completedMethods = resultType.GetMethods()
            .Where(m => string.Equals(m.Name, "Completed", StringComparison.Ordinal) && m.IsStatic)
            .ToArray();

        var completedWithDiff = completedMethods.FirstOrDefault(m => m.GetParameters().Length == 5);
        if (completedWithDiff is not null)
        {
            return (DebuggerSourceExportResult)completedWithDiff.Invoke(null, new object[] { message, filePath, backupPath, mode, diffPreview })!;
        }

        var completedLegacy = completedMethods.FirstOrDefault(m => m.GetParameters().Length == 4);
        if (completedLegacy is not null)
        {
            return (DebuggerSourceExportResult)completedLegacy.Invoke(null, new object[] { message, filePath, backupPath, mode })!;
        }

        var constructorWithDiff = resultType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 6);
        if (constructorWithDiff is not null)
        {
            return (DebuggerSourceExportResult)constructorWithDiff.Invoke(new object?[] { true, message, filePath, backupPath, mode, diffPreview });
        }

        var legacyConstructor = resultType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 5);
        if (legacyConstructor is not null)
        {
            return (DebuggerSourceExportResult)legacyConstructor.Invoke(new object?[] { true, message, filePath, backupPath, mode });
        }

        return DebuggerSourceExportResult.Failed("Roslyn source export completed, but the debugger result contract could not be constructed.", filePath);
    }

    private static string BuildUnifiedDiff(string path, string original, string rewritten)
    {
        var originalLines = original.Replace("\r\n", "\n").Split('\n');
        var rewrittenLines = rewritten.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        sb.Append("--- ").Append(path).AppendLine(" (current)");
        sb.Append("+++ ").Append(path).AppendLine(" (debugger export)");
        var max = Math.Max(originalLines.Length, rewrittenLines.Length);
        for (var i = 0; i < max; i++)
        {
            var oldLine = i < originalLines.Length ? originalLines[i] : null;
            var newLine = i < rewrittenLines.Length ? rewrittenLines[i] : null;
            if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                continue;
            }

            sb.Append("@@ line ").Append(i + 1).AppendLine(" @@");
            if (oldLine is not null) sb.Append('-').AppendLine(oldLine);
            if (newLine is not null) sb.Append('+').AppendLine(newLine);
        }

        return sb.ToString();
    }

    private static CompilationUnitSyntax ApplyGeneratedEventMembers(CompilationUnitSyntax root, ClassDeclarationSyntax originalClassNode, DebuggerSourceExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GeneratedEventMembersSource))
        {
            return root;
        }

        var currentClassNode = FindTargetClass(root, request);
        if (currentClassNode is null)
        {
            return root;
        }

        var parsedMembers = SyntaxFactory.ParseCompilationUnit("class __DebuggerEventContainer {\n" + request.GeneratedEventMembersSource + "\n}")
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault()
            ?.Members
            .OfType<MethodDeclarationSyntax>()
            .ToArray() ?? Array.Empty<MethodDeclarationSyntax>();
        if (parsedMembers.Length == 0)
        {
            return root;
        }

        var updatedClass = currentClassNode;
        foreach (var member in parsedMembers)
        {
            var existing = updatedClass.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => string.Equals(m.Identifier.ValueText, member.Identifier.ValueText, StringComparison.Ordinal));
            var normalized = member.NormalizeWhitespace(elasticTrivia: true);
            if (existing is not null)
            {
                normalized = normalized.WithLeadingTrivia(existing.GetLeadingTrivia()).WithTrailingTrivia(existing.GetTrailingTrivia());
                updatedClass = updatedClass.ReplaceNode(existing, normalized);
            }
            else
            {
                normalized = normalized
                    .WithLeadingTrivia(CreateInsertedMethodLeadingTrivia(updatedClass))
                    .WithTrailingTrivia(CreateInsertedMethodTrailingTrivia(updatedClass));
                updatedClass = updatedClass.WithMembers(updatedClass.Members.Add(normalized));
            }
        }

        return root.ReplaceNode(currentClassNode, updatedClass);
    }

    private static CompilationUnitSyntax EnsureDebuggerUsings(CompilationUnitSyntax root)
    {
        var required = new[]
        {
            "System.Numerics",
            "ThreeDEngine.Core.Scene",
            "ThreeDEngine.Core.Primitives",
            "ThreeDEngine.Core.Materials",
            "ThreeDEngine.Core.Physics",
            "ThreeDEngine.Core.Interaction"
        };

        foreach (var name in required)
        {
            if (root.Usings.Any(u => string.Equals(u.Name?.ToString(), name, StringComparison.Ordinal)))
            {
                continue;
            }

            root = root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name)));
        }

        return root;
    }

    private static SyntaxTriviaList CreateInsertedMethodLeadingTrivia(ClassDeclarationSyntax classNode)
    {
        var indentation = ResolveClassMemberIndentation(classNode);
        if (classNode.Members.Count == 0)
        {
            return SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(Environment.NewLine), SyntaxFactory.Whitespace(indentation));
        }

        return SyntaxFactory.TriviaList(
            SyntaxFactory.EndOfLine(Environment.NewLine),
            SyntaxFactory.EndOfLine(Environment.NewLine),
            SyntaxFactory.Whitespace(indentation));
    }

    private static SyntaxTriviaList CreateInsertedMethodTrailingTrivia(ClassDeclarationSyntax classNode)
    {
        var indentation = ResolveClassMemberIndentation(classNode);
        return SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(Environment.NewLine), SyntaxFactory.Whitespace(indentation));
    }

    private static string ResolveClassMemberIndentation(ClassDeclarationSyntax classNode)
    {
        foreach (var member in classNode.Members)
        {
            var text = member.GetLeadingTrivia().ToFullString();
            var lastLineBreak = Math.Max(text.LastIndexOf('\n'), text.LastIndexOf('\r'));
            var suffix = lastLineBreak >= 0 ? text[(lastLineBreak + 1)..] : text;
            if (!string.IsNullOrWhiteSpace(suffix) && suffix.All(char.IsWhiteSpace))
            {
                return suffix;
            }
        }

        var classText = classNode.GetLeadingTrivia().ToFullString();
        var classLineBreak = Math.Max(classText.LastIndexOf('\n'), classText.LastIndexOf('\r'));
        var classIndent = classLineBreak >= 0 ? classText[(classLineBreak + 1)..] : classText;
        if (!string.IsNullOrWhiteSpace(classIndent) && classIndent.All(char.IsWhiteSpace))
        {
            return classIndent + "    ";
        }

        return "    ";
    }

    private static ClassDeclarationSyntax? FindTargetClass(CompilationUnitSyntax root, DebuggerSourceExportRequest request)
    {
        var candidates = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(c => string.Equals(c.Identifier.ValueText, request.ClassName, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var normalizedRequestedName = NormalizeTypeName(request.TypeFullName);
        if (!string.IsNullOrWhiteSpace(normalizedRequestedName))
        {
            var exact = candidates.FirstOrDefault(c => string.Equals(NormalizeTypeName(GetFullTypeName(c)), normalizedRequestedName, StringComparison.Ordinal));
            if (exact is not null)
            {
                return exact;
            }
        }

        var byPosition = candidates
            .OrderBy(c => Math.Abs(c.SpanStart - request.ClassStart))
            .FirstOrDefault();
        return byPosition ?? candidates[0];
    }

    private static MethodDeclarationSyntax? FindBuildMethod(ClassDeclarationSyntax classNode)
    {
        return classNode.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method =>
                string.Equals(method.Identifier.ValueText, "Build", StringComparison.Ordinal) &&
                method.ParameterList.Parameters.Count == 1 &&
                method.ReturnType.ToString().EndsWith("void", StringComparison.Ordinal));
    }

    private static string? GetFullTypeName(ClassDeclarationSyntax classNode)
    {
        var typeNames = classNode.AncestorsAndSelf()
            .OfType<ClassDeclarationSyntax>()
            .Reverse()
            .Select(c => c.Identifier.ValueText)
            .ToArray();
        if (typeNames.Length == 0)
        {
            return null;
        }

        var namespaceName = classNode.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(ns => ns.Name.ToString())
            .FirstOrDefault();

        var nested = string.Join(".", typeNames);
        return string.IsNullOrWhiteSpace(namespaceName) ? nested : namespaceName + "." + nested;
    }

    private static string? NormalizeTypeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Trim().Replace('+', '.');
    }
}
#endif
