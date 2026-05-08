using System.Collections.Generic;
using ThreeDEngine.Core.Materials;

namespace ThreeDEngine.Core.Rendering.Capabilities;

public static class MaterialFeatureDiagnostics3D
{
    public static IReadOnlyList<RenderFeatureDiagnostic3D> Validate(Material3D? material, RendererCapabilities3D capabilities)
    {
        var diagnostics = new List<RenderFeatureDiagnostic3D>();
        if (material is null) return diagnostics;
        Check(material.HasBaseColorTexture, RenderingFeature3D.BaseColorTexture, capabilities, diagnostics, "base color texture");
        Check(material.HasNormalMap, RenderingFeature3D.NormalTexture, capabilities, diagnostics, "normal texture");
        Check(material.HasMetallicRoughnessTexture || material.Metallic > 0.0001f || material.Roughness < 0.999f, RenderingFeature3D.MetallicRoughness, capabilities, diagnostics, "metallic/roughness material data");
        Check(material.HasEmissiveTexture || material.EmissiveColor.A > 0.0001f || material.EmissiveColor.R > 0.0001f || material.EmissiveColor.G > 0.0001f || material.EmissiveColor.B > 0.0001f, RenderingFeature3D.Emissive, capabilities, diagnostics, "emissive material data");
        Check(material.IsTransparent, RenderingFeature3D.AlphaBlend, capabilities, diagnostics, "alpha blending");
        Check(material.DoubleSided || material.CullMode == CullMode.None, RenderingFeature3D.DoubleSided, capabilities, diagnostics, "double-sided material");
        return diagnostics;
    }

    private static void Check(bool required, RenderingFeature3D feature, RendererCapabilities3D capabilities, List<RenderFeatureDiagnostic3D> diagnostics, string label)
    {
        if (!required || capabilities.Supports(feature)) return;
        diagnostics.Add(new RenderFeatureDiagnostic3D(
            feature,
            "RENDER_FEATURE_NOT_SUPPORTED",
            $"The current {capabilities.Backend} backend does not advertise support for {label}; this material feature will be approximated or ignored."));
    }
}
