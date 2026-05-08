namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelImportMessage
{
    public ModelImportMessage(ModelImportSeverity severity, string code, string message)
    {
        Severity = severity;
        Code = string.IsNullOrWhiteSpace(code) ? "MODEL" : code;
        Message = message ?? string.Empty;
    }

    public ModelImportSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public override string ToString() => $"{Severity}: {Code}: {Message}";
}
