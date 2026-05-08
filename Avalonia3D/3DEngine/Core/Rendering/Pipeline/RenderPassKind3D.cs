namespace ThreeDEngine.Core.Rendering.Pipeline;

public enum RenderPassKind3D
{
    ForwardOpaque = 0,
    GBuffer = 1,
    DeferredLighting = 2,
    Ssao = 3,
    HdrToneMapping = 4,
    TransparentForward = 5,
    Overlay = 6
}
