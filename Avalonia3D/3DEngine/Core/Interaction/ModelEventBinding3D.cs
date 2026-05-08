using System;

namespace ThreeDEngine.Core.Interaction;

public sealed class ModelEventBinding3D
{
    public ModelEventBinding3D(
        ModelEventTargetKind3D targetKind,
        string targetPath,
        ModelPointerEventKind eventKind,
        EventHandler<ModelPointerEventArgs> handler)
    {
        TargetKind = targetKind;
        TargetPath = targetPath ?? string.Empty;
        EventKind = eventKind;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public ModelEventTargetKind3D TargetKind { get; }
    public string TargetPath { get; }
    public ModelPointerEventKind EventKind { get; }
    public EventHandler<ModelPointerEventArgs> Handler { get; }

    public bool Matches(ModelPointerEventKind eventKind, ModelHitResult3D hit)
    {
        if (EventKind != eventKind) return false;
        return TargetKind switch
        {
            ModelEventTargetKind3D.Model => string.IsNullOrWhiteSpace(TargetPath) ||
                                            string.Equals(TargetPath, hit.Model.Name, StringComparison.Ordinal) ||
                                            string.Equals(TargetPath, hit.Model.Asset.AssetId, StringComparison.Ordinal),
            ModelEventTargetKind3D.Node => MatchesPath(TargetPath, hit.NodePath) ||
                                           string.Equals(TargetPath, hit.NodeName, StringComparison.Ordinal),
            ModelEventTargetKind3D.Primitive => MatchesPath(TargetPath, hit.PrimitivePath) ||
                                                MatchesPath(TargetPath, hit.Part.ModelElementPath),
            ModelEventTargetKind3D.Triangle => MatchesPath(TargetPath, hit.ElementPath),
            _ => false
        };
    }

    private static bool MatchesPath(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)) return false;
        return string.Equals(expected, actual, StringComparison.Ordinal) ||
               actual.EndsWith("/" + expected, StringComparison.Ordinal);
    }
}
