using System;

namespace ThreeDEngine.Core.Rendering.Rhi;

internal enum RhiResourceKind3D
{
    Buffer = 0,
    Texture = 1,
    RenderTarget = 2,
    Sampler = 3,
    Pipeline = 4,
    VertexArray = 5,
    ShaderModule = 6,
    PipelineLayout = 7,
    BindGroupLayout = 8,
    BindGroup = 9
}

[Flags]
internal enum RhiBufferUsage3D
{
    None = 0,
    Vertex = 1 << 0,
    Index = 1 << 1,
    Uniform = 1 << 2,
    Instance = 1 << 3,
    Storage = 1 << 4,
    Dynamic = 1 << 5,
    CopySource = 1 << 6,
    CopyDestination = 1 << 7,
    Indirect = 1 << 8
}

[Flags]
internal enum RhiTextureUsage3D
{
    None = 0,
    Sampled = 1 << 0,
    RenderTarget = 1 << 1,
    DepthStencil = 1 << 2,
    Storage = 1 << 3,
    CopySource = 1 << 4,
    CopyDestination = 1 << 5
}

internal enum RhiTextureFormat3D
{
    Rgba8Unorm = 0,
    Rgba16Float = 1,
    Rgba32Float = 2,
    Depth16 = 3,
    Depth24 = 4,
    Depth24Stencil8 = 5,
    Depth32Float = 6
}

internal readonly struct RhiBufferDescriptor3D : IEquatable<RhiBufferDescriptor3D>
{
    public RhiBufferDescriptor3D(long byteSize, RhiBufferUsage3D usage, int stride = 0)
    {
        if (byteSize <= 0) throw new ArgumentOutOfRangeException(nameof(byteSize), "RHI buffers must have a positive byte size.");
        if (usage == RhiBufferUsage3D.None) throw new ArgumentOutOfRangeException(nameof(usage));
        if ((usage & ~AllBufferUsages) != 0) throw new ArgumentOutOfRangeException(nameof(usage), "RHI buffer usage contains unknown flags.");
        if (stride < 0) throw new ArgumentOutOfRangeException(nameof(stride));
        if (stride > byteSize) throw new ArgumentOutOfRangeException(nameof(stride), "RHI buffer stride cannot exceed its allocation size.");
        if (stride > 0 && byteSize % stride != 0) throw new ArgumentException("Buffer byte size must be divisible by stride.", nameof(stride));
        ByteSize = byteSize;
        Usage = usage;
        Stride = stride;
    }

    public long ByteSize { get; }
    public RhiBufferUsage3D Usage { get; }
    public int Stride { get; }

    public bool Equals(RhiBufferDescriptor3D other) => ByteSize == other.ByteSize && Usage == other.Usage && Stride == other.Stride;
    public override bool Equals(object? obj) => obj is RhiBufferDescriptor3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ByteSize, Usage, Stride);

    private const RhiBufferUsage3D AllBufferUsages =
        RhiBufferUsage3D.Vertex | RhiBufferUsage3D.Index | RhiBufferUsage3D.Uniform |
        RhiBufferUsage3D.Instance | RhiBufferUsage3D.Storage | RhiBufferUsage3D.Dynamic |
        RhiBufferUsage3D.CopySource | RhiBufferUsage3D.CopyDestination | RhiBufferUsage3D.Indirect;
}

internal readonly struct RhiTextureDescriptor3D : IEquatable<RhiTextureDescriptor3D>
{
    public RhiTextureDescriptor3D(int width, int height, RhiTextureFormat3D format, RhiTextureUsage3D usage, int mipLevels = 1, int samples = 1)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (usage == RhiTextureUsage3D.None) throw new ArgumentOutOfRangeException(nameof(usage));
        if ((usage & ~AllTextureUsages) != 0) throw new ArgumentOutOfRangeException(nameof(usage), "RHI texture usage contains unknown flags.");
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (mipLevels <= 0) throw new ArgumentOutOfRangeException(nameof(mipLevels));
        if (samples <= 0) throw new ArgumentOutOfRangeException(nameof(samples));
        var maximumMipLevels = 1;
        for (var size = global::System.Math.Max(width, height); size > 1; size >>= 1) maximumMipLevels++;
        if (mipLevels > maximumMipLevels) throw new ArgumentOutOfRangeException(nameof(mipLevels), "Mip count exceeds the complete chain for the texture dimensions.");
        var depthFormat = format is RhiTextureFormat3D.Depth16 or RhiTextureFormat3D.Depth24 or
            RhiTextureFormat3D.Depth24Stencil8 or RhiTextureFormat3D.Depth32Float;
        if (depthFormat != ((usage & RhiTextureUsage3D.DepthStencil) != 0))
        {
            throw new ArgumentException("Depth formats and DepthStencil usage must be declared together.", nameof(usage));
        }
        Width = width;
        Height = height;
        Format = format;
        Usage = usage;
        MipLevels = mipLevels;
        Samples = samples;
        EstimatedByteSize = EstimateByteSize(width, height, format, mipLevels, samples);
    }

    public int Width { get; }
    public int Height { get; }
    public RhiTextureFormat3D Format { get; }
    public RhiTextureUsage3D Usage { get; }
    public int MipLevels { get; }
    public int Samples { get; }
    public long EstimatedByteSize { get; }

    public bool Equals(RhiTextureDescriptor3D other)
        => Width == other.Width && Height == other.Height && Format == other.Format && Usage == other.Usage && MipLevels == other.MipLevels && Samples == other.Samples;
    public override bool Equals(object? obj) => obj is RhiTextureDescriptor3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Width, Height, Format, Usage, MipLevels, Samples);

    private const RhiTextureUsage3D AllTextureUsages =
        RhiTextureUsage3D.Sampled | RhiTextureUsage3D.RenderTarget |
        RhiTextureUsage3D.DepthStencil | RhiTextureUsage3D.Storage |
        RhiTextureUsage3D.CopySource | RhiTextureUsage3D.CopyDestination;

    private static long EstimateByteSize(int width, int height, RhiTextureFormat3D format, int mipLevels, int samples)
    {
        var bytesPerPixel = format switch
        {
            RhiTextureFormat3D.Rgba16Float => 8,
            RhiTextureFormat3D.Rgba32Float => 16,
            RhiTextureFormat3D.Depth16 => 2,
            _ => 4
        };
        long total = 0;
        var levelWidth = width;
        var levelHeight = height;
        for (var level = 0; level < mipLevels; level++)
        {
            total = checked(total + checked((long)levelWidth * levelHeight * bytesPerPixel * samples));
            levelWidth = global::System.Math.Max(1, levelWidth >> 1);
            levelHeight = global::System.Math.Max(1, levelHeight >> 1);
        }

        return total;
    }
}

/// <summary>Strong, generation-checked logical GPU resource identity.</summary>
internal readonly struct RhiResourceHandle3D : IEquatable<RhiResourceHandle3D>
{
    internal RhiResourceHandle3D(ulong id, uint generation, RhiResourceKind3D kind)
    {
        Id = id;
        Generation = generation;
        Kind = kind;
    }

    public ulong Id { get; }
    public uint Generation { get; }
    public RhiResourceKind3D Kind { get; }
    public bool IsValid => Id != 0 && Generation != 0;

    public bool Equals(RhiResourceHandle3D other) => Id == other.Id && Generation == other.Generation && Kind == other.Kind;
    public override bool Equals(object? obj) => obj is RhiResourceHandle3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Generation, Kind);
    public override string ToString() => IsValid ? $"{Kind}:{Id}@{Generation}" : "invalid";
}

public readonly struct RhiResourceSnapshot3D
{
    internal RhiResourceSnapshot3D(
        int liveCount, int bufferCount, int textureCount, int ownershipReferences,
        long residentBytes, long textureBytes, long peakResidentBytes,
        long maxResidentBytes, long maxTextureBytes,
        long creates, long updates, long releases, uint contextGeneration)
    {
        LiveCount = liveCount;
        BufferCount = bufferCount;
        TextureCount = textureCount;
        OwnershipReferences = ownershipReferences;
        ResidentBytes = residentBytes;
        TextureBytes = textureBytes;
        PeakResidentBytes = peakResidentBytes;
        MaxResidentBytes = maxResidentBytes;
        MaxTextureBytes = maxTextureBytes;
        Creates = creates;
        Updates = updates;
        Releases = releases;
        ContextGeneration = contextGeneration;
    }

    public int LiveCount { get; }
    public int BufferCount { get; }
    public int TextureCount { get; }
    public int OwnershipReferences { get; }
    public long ResidentBytes { get; }
    public long TextureBytes { get; }
    public long PeakResidentBytes { get; }
    public long MaxResidentBytes { get; }
    public long MaxTextureBytes { get; }
    public long Creates { get; }
    public long Updates { get; }
    public long Releases { get; }
    public uint ContextGeneration { get; }
}
