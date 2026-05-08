using System;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;

namespace ThreeDEngine.Core.Rendering;

public readonly struct ShaderProgramDescriptor3D : IEquatable<ShaderProgramDescriptor3D>
{
    public ShaderProgramDescriptor3D(string shaderId, VertexLayout3D vertexLayout, LightingMode lightingMode, SurfaceMode surfaceMode, bool usesInstancing = false, bool usesTexture = false)
    {
        ShaderId = string.IsNullOrWhiteSpace(shaderId) ? "mesh" : shaderId;
        VertexLayout = vertexLayout ?? throw new ArgumentNullException(nameof(vertexLayout));
        LightingMode = lightingMode;
        SurfaceMode = surfaceMode;
        UsesInstancing = usesInstancing;
        UsesTexture = usesTexture;
    }

    public string ShaderId { get; }
    public VertexLayout3D VertexLayout { get; }
    public LightingMode LightingMode { get; }
    public SurfaceMode SurfaceMode { get; }
    public bool UsesInstancing { get; }
    public bool UsesTexture { get; }

    public RendererResourceKey ResourceKey => RendererResourceKey.Shader(
        ShaderId + "|layout=" + VertexLayout.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "|light=" + LightingMode
        + "|surface=" + SurfaceMode
        + "|inst=" + UsesInstancing
        + "|tex=" + UsesTexture);

    public bool Equals(ShaderProgramDescriptor3D other)
        => string.Equals(ShaderId, other.ShaderId, StringComparison.Ordinal)
           && VertexLayout.Equals(other.VertexLayout)
           && LightingMode == other.LightingMode
           && SurfaceMode == other.SurfaceMode
           && UsesInstancing == other.UsesInstancing
           && UsesTexture == other.UsesTexture;

    public override bool Equals(object? obj) => obj is ShaderProgramDescriptor3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(ShaderId), VertexLayout, LightingMode, SurfaceMode, UsesInstancing, UsesTexture);
}
