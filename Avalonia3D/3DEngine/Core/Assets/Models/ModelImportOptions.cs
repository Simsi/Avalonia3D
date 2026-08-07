using System;
using System.Numerics;
using ThreeDEngine.Core.Assets.Resolvers;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ModelImportOptions
{
    private long _maxFileBytes = 128L * 1024L * 1024L;
    private int _maxJsonBytes = 32 * 1024 * 1024;
    private int _maxBinaryChunkBytes = 256 * 1024 * 1024;
    private int _maxTextureBytes = 64 * 1024 * 1024;
    private int _maxVerticesPerPrimitive = 2_000_000;
    private int _maxIndicesPerPrimitive = 6_000_000;
    private Vector3 _position = Vector3.Zero;
    private Vector3 _rotationDegrees = Vector3.Zero;
    private Vector3 _scale = Vector3.One;

    public string? Name { get; set; }
    public string? BaseDirectory { get; set; }
    public IAssetResolver3D? AssetResolver { get; set; }
    public bool ResolveExternalBuffers { get; set; } = true;
    public bool ResolveExternalImages { get; set; } = true;
    public bool ResolveDataUris { get; set; } = true;
    public bool ResolveSidecarImages { get; set; } = true;
    public bool StrictValidation { get; set; } = true;
    public bool GenerateMissingNormals { get; set; } = true;

    /// <summary>Instance transform applied by Scene3D.ImportModel; it is not part of the shared asset-cache identity.</summary>
    public Vector3 Position
    {
        get => _position;
        set => _position = Guard3D.Finite(value, nameof(value));
    }

    public Vector3 RotationDegrees
    {
        get => _rotationDegrees;
        set => _rotationDegrees = Guard3D.Finite(value, nameof(value));
    }

    public Vector3 Scale
    {
        get => _scale;
        set
        {
            value = Guard3D.Finite(value, nameof(value));
            if (MathF.Abs(value.X) <= 0.000001f || MathF.Abs(value.Y) <= 0.000001f || MathF.Abs(value.Z) <= 0.000001f)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Model instance scale components must be non-zero.");
            _scale = value;
        }
    }
    public bool TreatWarningsAsErrors { get; set; }
    public bool StrictGlbValidation { get; set; } = true;

    /// <summary>Maximum accepted source size. Zero explicitly disables this limit.</summary>
    public long MaxFileBytes
    {
        get => _maxFileBytes;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "File-size limit cannot be negative.");
            _maxFileBytes = value;
        }
    }

    public int MaxJsonBytes
    {
        get => _maxJsonBytes;
        set => _maxJsonBytes = Guard3D.NonNegative(value, nameof(value));
    }

    public int MaxBinaryChunkBytes
    {
        get => _maxBinaryChunkBytes;
        set => _maxBinaryChunkBytes = Guard3D.NonNegative(value, nameof(value));
    }

    public int MaxTextureBytes
    {
        get => _maxTextureBytes;
        set => _maxTextureBytes = Guard3D.NonNegative(value, nameof(value));
    }

    public int MaxVerticesPerPrimitive
    {
        get => _maxVerticesPerPrimitive;
        set => _maxVerticesPerPrimitive = Guard3D.Positive(value, nameof(value));
    }

    public int MaxIndicesPerPrimitive
    {
        get => _maxIndicesPerPrimitive;
        set => _maxIndicesPerPrimitive = Guard3D.Positive(value, nameof(value));
    }
    /// <summary>
    /// Creates a stable request snapshot. Streaming and importer queues use this copy so later
    /// caller mutations cannot change validation limits or resolver behavior while a load is in flight.
    /// Instance-only name/transform values are preserved but remain excluded from shared asset identity.
    /// </summary>
    public ModelImportOptions Clone()
        => new()
        {
            Name = Name,
            BaseDirectory = BaseDirectory,
            AssetResolver = AssetResolver,
            ResolveExternalBuffers = ResolveExternalBuffers,
            ResolveExternalImages = ResolveExternalImages,
            ResolveDataUris = ResolveDataUris,
            ResolveSidecarImages = ResolveSidecarImages,
            StrictValidation = StrictValidation,
            GenerateMissingNormals = GenerateMissingNormals,
            Position = Position,
            RotationDegrees = RotationDegrees,
            Scale = Scale,
            TreatWarningsAsErrors = TreatWarningsAsErrors,
            StrictGlbValidation = StrictGlbValidation,
            MaxFileBytes = MaxFileBytes,
            MaxJsonBytes = MaxJsonBytes,
            MaxBinaryChunkBytes = MaxBinaryChunkBytes,
            MaxTextureBytes = MaxTextureBytes,
            MaxVerticesPerPrimitive = MaxVerticesPerPrimitive,
            MaxIndicesPerPrimitive = MaxIndicesPerPrimitive
        };

}
