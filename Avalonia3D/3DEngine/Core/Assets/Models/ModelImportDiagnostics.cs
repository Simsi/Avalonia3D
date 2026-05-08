using System.Collections.Generic;
using System.Linq;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelImportDiagnostics
{
    private readonly List<ModelImportMessage> _messages = new();

    public IReadOnlyList<ModelImportMessage> Messages => _messages;
    public bool HasErrors => _messages.Any(m => m.Severity == ModelImportSeverity.Error);
    public bool HasWarnings => _messages.Any(m => m.Severity == ModelImportSeverity.Warning);

    public void Info(string code, string message) => Add(ModelImportSeverity.Info, code, message);
    public void Warning(string code, string message) => Add(ModelImportSeverity.Warning, code, message);
    public void Error(string code, string message) => Add(ModelImportSeverity.Error, code, message);

    public void Add(ModelImportSeverity severity, string code, string message)
        => _messages.Add(new ModelImportMessage(severity, code, message));

    public void AddRange(ModelImportDiagnostics other)
    {
        foreach (var message in other.Messages)
        {
            _messages.Add(message);
        }
    }

    public string ToSummary()
    {
        if (_messages.Count == 0)
        {
            return "No import diagnostics.";
        }

        return string.Join("\n", _messages.Select(m => m.ToString()));
    }
}
