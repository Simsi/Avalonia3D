namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelSkinningDiagnostics3D
{
    public static ModelSkinningDiagnostics3D None { get; } = new(false, string.Empty, 0f, 0f);

    public ModelSkinningDiagnostics3D(bool fallbackToBindPose, string reason, float sourceBoundsSpan, float deformedBoundsSpan)
    {
        FallbackToBindPose = fallbackToBindPose;
        Reason = reason ?? string.Empty;
        SourceBoundsSpan = sourceBoundsSpan;
        DeformedBoundsSpan = deformedBoundsSpan;
    }

    public bool FallbackToBindPose { get; }
    public string Reason { get; }
    public float SourceBoundsSpan { get; }
    public float DeformedBoundsSpan { get; }
}
