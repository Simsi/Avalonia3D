using System;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Future instancing key: renderers should group instances by mesh/material/lighting state.
/// </summary>
public readonly struct RenderBatchKey : IEquatable<RenderBatchKey>
{
    public RenderBatchKey(MeshResourceKey meshKey, string materialKey, int lightingMode, int surfaceMode)
    {
        MeshKey = meshKey;
        MaterialKey = materialKey ?? string.Empty;
        LightingMode = lightingMode;
        SurfaceMode = surfaceMode;
    }

    public MeshResourceKey MeshKey { get; }
    public string MaterialKey { get; }
    public int LightingMode { get; }
    public int SurfaceMode { get; }

    public bool Equals(RenderBatchKey other)
        => MeshKey.Equals(other.MeshKey)
           && string.Equals(MaterialKey, other.MaterialKey, StringComparison.Ordinal)
           && LightingMode == other.LightingMode
           && SurfaceMode == other.SurfaceMode;

    public override bool Equals(object? obj) => obj is RenderBatchKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(MeshKey, StringComparer.Ordinal.GetHashCode(MaterialKey), LightingMode, SurfaceMode);
}
