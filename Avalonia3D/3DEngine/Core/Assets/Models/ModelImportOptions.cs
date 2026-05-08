using System.Numerics;
using ThreeDEngine.Core.Assets.Resolvers;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelImportOptions
{
    public string? Name { get; set; }
    public string? BaseDirectory { get; set; }
    public IAssetResolver3D? AssetResolver { get; set; }
    public bool ResolveExternalBuffers { get; set; } = true;
    public bool ResolveExternalImages { get; set; } = true;
    public bool ResolveDataUris { get; set; } = true;
    public bool ResolveSidecarImages { get; set; } = true;
    public bool StrictValidation { get; set; } = true;
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 RotationDegrees { get; set; } = Vector3.Zero;
    public Vector3 Scale { get; set; } = Vector3.One;
    public bool GenerateMissingNormals { get; set; } = true;
    public bool NormalizeToUnitSize { get; set; }
    public bool TreatWarningsAsErrors { get; set; }
    public long MaxFileBytes { get; set; } = 128L * 1024L * 1024L;
    public int MaxJsonBytes { get; set; } = 32 * 1024 * 1024;
    public int MaxBinaryChunkBytes { get; set; } = 256 * 1024 * 1024;
    public int MaxTextureBytes { get; set; } = 64 * 1024 * 1024;
    public int MaxVerticesPerPrimitive { get; set; } = 2_000_000;
    public int MaxIndicesPerPrimitive { get; set; } = 6_000_000;
    public bool StrictGlbValidation { get; set; } = true;
}

