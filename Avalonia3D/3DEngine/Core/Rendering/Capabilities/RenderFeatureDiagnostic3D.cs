namespace ThreeDEngine.Core.Rendering.Capabilities;

public readonly record struct RenderFeatureDiagnostic3D(
    RenderingFeature3D Feature,
    string Code,
    string Message);
