namespace ThreeDEngine.Core.Rendering.Extensions;

public enum RenderExtensionStage3D
{
    BeforeVisibility = 0,
    AfterVisibility = 1,
    BeforeOpaque = 2,
    AfterOpaque = 3,
    BeforeParticles = 4,
    AfterParticles = 5,
    BeforeToneMapping = 6,
    AfterToneMapping = 7
}
