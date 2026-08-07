using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Rhi;

[Flags]
internal enum RhiShaderStage3D
{
    None = 0,
    Vertex = 1 << 0,
    Fragment = 1 << 1,
    Compute = 1 << 2,
    AllGraphics = Vertex | Fragment,
    All = Vertex | Fragment | Compute
}

internal enum RhiShaderLanguage3D
{
    Wgsl = 0,
    Glsl = 1,
    SpirV = 2
}

internal enum RhiBindingType3D
{
    UniformBuffer = 0,
    ReadOnlyStorageBuffer = 1,
    StorageBuffer = 2,
    SampledTexture = 3,
    StorageTexture = 4,
    Sampler = 5,
    ComparisonSampler = 6
}

internal enum RhiPrimitiveTopology3D
{
    TriangleList = 0,
    TriangleStrip = 1,
    LineList = 2,
    LineStrip = 3,
    PointList = 4
}

internal enum RhiFrontFace3D
{
    CounterClockwise = 0,
    Clockwise = 1
}

internal enum RhiCullMode3D
{
    None = 0,
    Front = 1,
    Back = 2
}

internal enum RhiCompareFunction3D
{
    Never = 0,
    Less = 1,
    LessEqual = 2,
    Equal = 3,
    GreaterEqual = 4,
    Greater = 5,
    NotEqual = 6,
    Always = 7
}



internal enum RhiVertexFormat3D
{
    Float32 = 0,
    Float32x2 = 1,
    Float32x3 = 2,
    Float32x4 = 3,
    Uint32 = 4,
    Uint32x2 = 5,
    Uint32x3 = 6,
    Uint32x4 = 7
}

internal enum RhiVertexStepMode3D
{
    Vertex = 0,
    Instance = 1
}

internal readonly struct RhiVertexAttribute3D : IEquatable<RhiVertexAttribute3D>
{
    public RhiVertexAttribute3D(int shaderLocation, int offset, RhiVertexFormat3D format)
    {
        if (shaderLocation < 0) throw new ArgumentOutOfRangeException(nameof(shaderLocation));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        ShaderLocation = shaderLocation;
        Offset = offset;
        Format = format;
    }

    public int ShaderLocation { get; }
    public int Offset { get; }
    public RhiVertexFormat3D Format { get; }
    public int ByteSize => Format switch
    {
        RhiVertexFormat3D.Float32 or RhiVertexFormat3D.Uint32 => 4,
        RhiVertexFormat3D.Float32x2 or RhiVertexFormat3D.Uint32x2 => 8,
        RhiVertexFormat3D.Float32x3 or RhiVertexFormat3D.Uint32x3 => 12,
        RhiVertexFormat3D.Float32x4 or RhiVertexFormat3D.Uint32x4 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(Format))
    };

    public bool Equals(RhiVertexAttribute3D other)
        => ShaderLocation == other.ShaderLocation && Offset == other.Offset && Format == other.Format;
    public override bool Equals(object? obj) => obj is RhiVertexAttribute3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ShaderLocation, Offset, Format);
}

internal sealed class RhiVertexBufferLayout3D : IEquatable<RhiVertexBufferLayout3D>
{
    private readonly RhiVertexAttribute3D[] _attributes;

    public RhiVertexBufferLayout3D(
        int arrayStride,
        IEnumerable<RhiVertexAttribute3D> attributes,
        RhiVertexStepMode3D stepMode = RhiVertexStepMode3D.Vertex)
    {
        if (arrayStride <= 0) throw new ArgumentOutOfRangeException(nameof(arrayStride));
        if (!Enum.IsDefined(stepMode)) throw new ArgumentOutOfRangeException(nameof(stepMode));
        _attributes = attributes is null
            ? throw new ArgumentNullException(nameof(attributes))
            : new List<RhiVertexAttribute3D>(attributes).ToArray();
        if (_attributes.Length == 0) throw new ArgumentException("A vertex buffer layout requires at least one attribute.", nameof(attributes));
        Array.Sort(_attributes, static (a, b) => a.ShaderLocation.CompareTo(b.ShaderLocation));
        for (var i = 0; i < _attributes.Length; i++)
        {
            var attribute = _attributes[i];
            if (checked(attribute.Offset + attribute.ByteSize) > arrayStride)
                throw new ArgumentOutOfRangeException(nameof(attributes), $"Vertex attribute {attribute.ShaderLocation} exceeds the {arrayStride}-byte stride.");
            if (i > 0 && _attributes[i - 1].ShaderLocation == attribute.ShaderLocation)
                throw new ArgumentException($"Duplicate vertex shader location {attribute.ShaderLocation}.", nameof(attributes));
        }
        ArrayStride = arrayStride;
        StepMode = stepMode;
    }

    public int ArrayStride { get; }
    public RhiVertexStepMode3D StepMode { get; }
    public ReadOnlySpan<RhiVertexAttribute3D> Attributes => _attributes;

    public bool Equals(RhiVertexBufferLayout3D? other)
    {
        if (other is null || ArrayStride != other.ArrayStride || StepMode != other.StepMode || _attributes.Length != other._attributes.Length)
            return false;
        for (var i = 0; i < _attributes.Length; i++)
            if (!_attributes[i].Equals(other._attributes[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is RhiVertexBufferLayout3D other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ArrayStride);
        hash.Add(StepMode);
        for (var i = 0; i < _attributes.Length; i++) hash.Add(_attributes[i]);
        return hash.ToHashCode();
    }
}

internal enum RhiFilterMode3D
{
    Nearest = 0,
    Linear = 1
}

internal enum RhiAddressMode3D
{
    ClampToEdge = 0,
    Repeat = 1,
    MirrorRepeat = 2
}

internal readonly struct RhiShaderBindingReflection3D : IEquatable<RhiShaderBindingReflection3D>
{
    public RhiShaderBindingReflection3D(int group, int binding, string name, RhiBindingType3D type, RhiShaderStage3D visibility, int minimumByteSize = 0)
    {
        if (group < 0) throw new ArgumentOutOfRangeException(nameof(group));
        if (binding < 0) throw new ArgumentOutOfRangeException(nameof(binding));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Shader binding name cannot be empty.", nameof(name));
        if (visibility == RhiShaderStage3D.None) throw new ArgumentOutOfRangeException(nameof(visibility));
        if (minimumByteSize < 0) throw new ArgumentOutOfRangeException(nameof(minimumByteSize));
        Group = group;
        Binding = binding;
        Name = name;
        Type = type;
        Visibility = visibility;
        MinimumByteSize = minimumByteSize;
    }

    public int Group { get; }
    public int Binding { get; }
    public string Name { get; }
    public RhiBindingType3D Type { get; }
    public RhiShaderStage3D Visibility { get; }
    public int MinimumByteSize { get; }

    public bool Equals(RhiShaderBindingReflection3D other)
        => Group == other.Group && Binding == other.Binding && string.Equals(Name, other.Name, StringComparison.Ordinal) &&
           Type == other.Type && Visibility == other.Visibility && MinimumByteSize == other.MinimumByteSize;
    public override bool Equals(object? obj) => obj is RhiShaderBindingReflection3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Group, Binding, StringComparer.Ordinal.GetHashCode(Name), Type, Visibility, MinimumByteSize);
}

internal sealed class RhiShaderReflection3D : IEquatable<RhiShaderReflection3D>
{
    private readonly RhiShaderBindingReflection3D[] _bindings;

    public RhiShaderReflection3D(IEnumerable<RhiShaderBindingReflection3D>? bindings = null)
    {
        _bindings = bindings is null ? Array.Empty<RhiShaderBindingReflection3D>() : new List<RhiShaderBindingReflection3D>(bindings).ToArray();
        Array.Sort(_bindings, static (a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : a.Binding.CompareTo(b.Binding));
        for (var i = 1; i < _bindings.Length; i++)
        {
            if (_bindings[i - 1].Group == _bindings[i].Group && _bindings[i - 1].Binding == _bindings[i].Binding)
                throw new ArgumentException($"Duplicate shader binding {_bindings[i].Group}:{_bindings[i].Binding}.", nameof(bindings));
        }
    }

    public ReadOnlySpan<RhiShaderBindingReflection3D> Bindings => _bindings;

    public bool Equals(RhiShaderReflection3D? other)
    {
        if (other is null || _bindings.Length != other._bindings.Length) return false;
        for (var i = 0; i < _bindings.Length; i++) if (!_bindings[i].Equals(other._bindings[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is RhiShaderReflection3D other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < _bindings.Length; i++) hash.Add(_bindings[i]);
        return hash.ToHashCode();
    }
}

internal sealed class RhiShaderModuleDescriptor3D : IEquatable<RhiShaderModuleDescriptor3D>
{
    public RhiShaderModuleDescriptor3D(string label, RhiShaderLanguage3D language, string sourceIdentity, RhiShaderReflection3D reflection)
    {
        Label = Require(label, nameof(label));
        SourceIdentity = Require(sourceIdentity, nameof(sourceIdentity));
        Language = language;
        Reflection = reflection ?? throw new ArgumentNullException(nameof(reflection));
    }

    public string Label { get; }
    public RhiShaderLanguage3D Language { get; }
    public string SourceIdentity { get; }
    public RhiShaderReflection3D Reflection { get; }

    public bool Equals(RhiShaderModuleDescriptor3D? other)
        => other is not null && Language == other.Language &&
           string.Equals(Label, other.Label, StringComparison.Ordinal) &&
           string.Equals(SourceIdentity, other.SourceIdentity, StringComparison.Ordinal) &&
           Reflection.Equals(other.Reflection);
    public override bool Equals(object? obj) => obj is RhiShaderModuleDescriptor3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Label), Language, StringComparer.Ordinal.GetHashCode(SourceIdentity), Reflection);

    private static string Require(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be empty.", name) : value;
}

internal readonly struct RhiBindGroupLayoutEntry3D : IEquatable<RhiBindGroupLayoutEntry3D>
{
    public RhiBindGroupLayoutEntry3D(int binding, RhiBindingType3D type, RhiShaderStage3D visibility, int minimumByteSize = 0)
    {
        if (binding < 0) throw new ArgumentOutOfRangeException(nameof(binding));
        if (visibility == RhiShaderStage3D.None) throw new ArgumentOutOfRangeException(nameof(visibility));
        if (minimumByteSize < 0) throw new ArgumentOutOfRangeException(nameof(minimumByteSize));
        Binding = binding;
        Type = type;
        Visibility = visibility;
        MinimumByteSize = minimumByteSize;
    }

    public int Binding { get; }
    public RhiBindingType3D Type { get; }
    public RhiShaderStage3D Visibility { get; }
    public int MinimumByteSize { get; }

    public bool Equals(RhiBindGroupLayoutEntry3D other)
        => Binding == other.Binding && Type == other.Type && Visibility == other.Visibility && MinimumByteSize == other.MinimumByteSize;
    public override bool Equals(object? obj) => obj is RhiBindGroupLayoutEntry3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Binding, Type, Visibility, MinimumByteSize);
}

internal sealed class RhiBindGroupLayoutDescriptor3D : IEquatable<RhiBindGroupLayoutDescriptor3D>
{
    private readonly RhiBindGroupLayoutEntry3D[] _entries;

    public RhiBindGroupLayoutDescriptor3D(string label, IEnumerable<RhiBindGroupLayoutEntry3D> entries)
    {
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Bind-group layout label cannot be empty.", nameof(label)) : label;
        _entries = entries is null ? throw new ArgumentNullException(nameof(entries)) : new List<RhiBindGroupLayoutEntry3D>(entries).ToArray();
        Array.Sort(_entries, static (a, b) => a.Binding.CompareTo(b.Binding));
        for (var i = 1; i < _entries.Length; i++) if (_entries[i - 1].Binding == _entries[i].Binding) throw new ArgumentException("Duplicate bind-group binding.", nameof(entries));
    }

    public string Label { get; }
    public ReadOnlySpan<RhiBindGroupLayoutEntry3D> Entries => _entries;

    public bool Equals(RhiBindGroupLayoutDescriptor3D? other)
    {
        if (other is null || !string.Equals(Label, other.Label, StringComparison.Ordinal) || _entries.Length != other._entries.Length) return false;
        for (var i = 0; i < _entries.Length; i++) if (!_entries[i].Equals(other._entries[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is RhiBindGroupLayoutDescriptor3D other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label, StringComparer.Ordinal);
        for (var i = 0; i < _entries.Length; i++) hash.Add(_entries[i]);
        return hash.ToHashCode();
    }
}

internal sealed class RhiPipelineLayoutDescriptor3D : IEquatable<RhiPipelineLayoutDescriptor3D>
{
    private readonly RhiResourceHandle3D[] _bindGroupLayouts;

    public RhiPipelineLayoutDescriptor3D(string label, IEnumerable<RhiResourceHandle3D> bindGroupLayouts)
    {
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Pipeline-layout label cannot be empty.", nameof(label)) : label;
        _bindGroupLayouts = bindGroupLayouts is null ? throw new ArgumentNullException(nameof(bindGroupLayouts)) : new List<RhiResourceHandle3D>(bindGroupLayouts).ToArray();
        for (var i = 0; i < _bindGroupLayouts.Length; i++)
            if (!_bindGroupLayouts[i].IsValid || _bindGroupLayouts[i].Kind != RhiResourceKind3D.BindGroupLayout)
                throw new ArgumentException("Pipeline layouts require valid bind-group-layout handles.", nameof(bindGroupLayouts));
    }

    public string Label { get; }
    public ReadOnlySpan<RhiResourceHandle3D> BindGroupLayouts => _bindGroupLayouts;

    public bool Equals(RhiPipelineLayoutDescriptor3D? other)
    {
        if (other is null || !string.Equals(Label, other.Label, StringComparison.Ordinal) || _bindGroupLayouts.Length != other._bindGroupLayouts.Length) return false;
        for (var i = 0; i < _bindGroupLayouts.Length; i++) if (!_bindGroupLayouts[i].Equals(other._bindGroupLayouts[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is RhiPipelineLayoutDescriptor3D other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label, StringComparer.Ordinal);
        for (var i = 0; i < _bindGroupLayouts.Length; i++) hash.Add(_bindGroupLayouts[i]);
        return hash.ToHashCode();
    }
}

internal readonly struct RhiSamplerDescriptor3D : IEquatable<RhiSamplerDescriptor3D>
{
    public RhiSamplerDescriptor3D(
        RhiFilterMode3D minFilter = RhiFilterMode3D.Linear,
        RhiFilterMode3D magFilter = RhiFilterMode3D.Linear,
        RhiFilterMode3D mipFilter = RhiFilterMode3D.Linear,
        RhiAddressMode3D addressU = RhiAddressMode3D.Repeat,
        RhiAddressMode3D addressV = RhiAddressMode3D.Repeat,
        RhiAddressMode3D addressW = RhiAddressMode3D.Repeat,
        RhiCompareFunction3D? compare = null,
        int maxAnisotropy = 1)
    {
        if (maxAnisotropy < 1) throw new ArgumentOutOfRangeException(nameof(maxAnisotropy));
        MinFilter = minFilter;
        MagFilter = magFilter;
        MipFilter = mipFilter;
        AddressU = addressU;
        AddressV = addressV;
        AddressW = addressW;
        Compare = compare;
        MaxAnisotropy = maxAnisotropy;
    }

    public RhiFilterMode3D MinFilter { get; }
    public RhiFilterMode3D MagFilter { get; }
    public RhiFilterMode3D MipFilter { get; }
    public RhiAddressMode3D AddressU { get; }
    public RhiAddressMode3D AddressV { get; }
    public RhiAddressMode3D AddressW { get; }
    public RhiCompareFunction3D? Compare { get; }
    public int MaxAnisotropy { get; }

    public bool Equals(RhiSamplerDescriptor3D other)
        => MinFilter == other.MinFilter && MagFilter == other.MagFilter && MipFilter == other.MipFilter &&
           AddressU == other.AddressU && AddressV == other.AddressV && AddressW == other.AddressW &&
           Compare == other.Compare && MaxAnisotropy == other.MaxAnisotropy;
    public override bool Equals(object? obj) => obj is RhiSamplerDescriptor3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(MinFilter, MagFilter, MipFilter, AddressU, AddressV, AddressW, Compare, MaxAnisotropy);
}

internal sealed class RhiRenderPipelineDescriptor3D : IEquatable<RhiRenderPipelineDescriptor3D>
{
    private readonly RhiVertexBufferLayout3D[] _vertexBuffers;

    public RhiRenderPipelineDescriptor3D(
        string label,
        RhiResourceHandle3D layout,
        RhiResourceHandle3D vertexShader,
        RhiResourceHandle3D fragmentShader,
        RhiPrimitiveTopology3D topology = RhiPrimitiveTopology3D.TriangleList,
        RhiFrontFace3D frontFace = RhiFrontFace3D.CounterClockwise,
        RhiCullMode3D cullMode = RhiCullMode3D.Back,
        RhiTextureFormat3D colorFormat = RhiTextureFormat3D.Rgba8Unorm,
        RhiTextureFormat3D? depthFormat = RhiTextureFormat3D.Depth24,
        int sampleCount = 1,
        IEnumerable<RhiVertexBufferLayout3D>? vertexBuffers = null)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Render-pipeline label cannot be empty.", nameof(label));
        if (!layout.IsValid || layout.Kind != RhiResourceKind3D.PipelineLayout) throw new ArgumentException("Pipeline layout handle is invalid.", nameof(layout));
        if (!vertexShader.IsValid || vertexShader.Kind != RhiResourceKind3D.ShaderModule) throw new ArgumentException("Vertex shader handle is invalid.", nameof(vertexShader));
        if (!fragmentShader.IsValid || fragmentShader.Kind != RhiResourceKind3D.ShaderModule) throw new ArgumentException("Fragment shader handle is invalid.", nameof(fragmentShader));
        if (sampleCount <= 0) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        _vertexBuffers = vertexBuffers is null ? Array.Empty<RhiVertexBufferLayout3D>() : new List<RhiVertexBufferLayout3D>(vertexBuffers).ToArray();
        for (var i = 0; i < _vertexBuffers.Length; i++)
            if (_vertexBuffers[i] is null) throw new ArgumentException("Vertex buffer layouts cannot contain null entries.", nameof(vertexBuffers));
        Label = label;
        Layout = layout;
        VertexShader = vertexShader;
        FragmentShader = fragmentShader;
        Topology = topology;
        FrontFace = frontFace;
        CullMode = cullMode;
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;
        SampleCount = sampleCount;
    }

    public string Label { get; }
    public RhiResourceHandle3D Layout { get; }
    public RhiResourceHandle3D VertexShader { get; }
    public RhiResourceHandle3D FragmentShader { get; }
    public RhiPrimitiveTopology3D Topology { get; }
    public RhiFrontFace3D FrontFace { get; }
    public RhiCullMode3D CullMode { get; }
    public RhiTextureFormat3D ColorFormat { get; }
    public RhiTextureFormat3D? DepthFormat { get; }
    public int SampleCount { get; }
    public ReadOnlySpan<RhiVertexBufferLayout3D> VertexBuffers => _vertexBuffers;

    public bool Equals(RhiRenderPipelineDescriptor3D? other)
    {
        if (other is null || !string.Equals(Label, other.Label, StringComparison.Ordinal) || !Layout.Equals(other.Layout) ||
            !VertexShader.Equals(other.VertexShader) || !FragmentShader.Equals(other.FragmentShader) || Topology != other.Topology ||
            FrontFace != other.FrontFace || CullMode != other.CullMode || ColorFormat != other.ColorFormat ||
            DepthFormat != other.DepthFormat || SampleCount != other.SampleCount || _vertexBuffers.Length != other._vertexBuffers.Length)
            return false;
        for (var i = 0; i < _vertexBuffers.Length; i++)
            if (!_vertexBuffers[i].Equals(other._vertexBuffers[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is RhiRenderPipelineDescriptor3D other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label, StringComparer.Ordinal);
        hash.Add(Layout);
        hash.Add(VertexShader);
        hash.Add(FragmentShader);
        hash.Add(Topology);
        hash.Add(FrontFace);
        hash.Add(CullMode);
        hash.Add(ColorFormat);
        hash.Add(DepthFormat);
        hash.Add(SampleCount);
        for (var i = 0; i < _vertexBuffers.Length; i++) hash.Add(_vertexBuffers[i]);
        return hash.ToHashCode();
    }
}

internal sealed class RhiComputePipelineDescriptor3D : IEquatable<RhiComputePipelineDescriptor3D>
{
    public RhiComputePipelineDescriptor3D(string label, RhiResourceHandle3D layout, RhiResourceHandle3D computeShader)
    {
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Compute-pipeline label cannot be empty.", nameof(label)) : label;
        if (!layout.IsValid || layout.Kind != RhiResourceKind3D.PipelineLayout) throw new ArgumentException("Pipeline layout handle is invalid.", nameof(layout));
        if (!computeShader.IsValid || computeShader.Kind != RhiResourceKind3D.ShaderModule) throw new ArgumentException("Compute shader handle is invalid.", nameof(computeShader));
        Layout = layout;
        ComputeShader = computeShader;
    }

    public string Label { get; }
    public RhiResourceHandle3D Layout { get; }
    public RhiResourceHandle3D ComputeShader { get; }

    public bool Equals(RhiComputePipelineDescriptor3D? other)
        => other is not null && string.Equals(Label, other.Label, StringComparison.Ordinal) && Layout.Equals(other.Layout) && ComputeShader.Equals(other.ComputeShader);
    public override bool Equals(object? obj) => obj is RhiComputePipelineDescriptor3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Label), Layout, ComputeShader);
}


internal readonly struct RhiBindGroupEntry3D : IEquatable<RhiBindGroupEntry3D>
{
    public RhiBindGroupEntry3D(int binding, RhiResourceHandle3D resource, long offset = 0, long byteSize = 0)
    {
        if (binding < 0) throw new ArgumentOutOfRangeException(nameof(binding));
        if (!resource.IsValid) throw new ArgumentException("Bind-group resource handle is invalid.", nameof(resource));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (byteSize < 0) throw new ArgumentOutOfRangeException(nameof(byteSize));
        Binding = binding;
        Resource = resource;
        Offset = offset;
        ByteSize = byteSize;
    }

    public int Binding { get; }
    public RhiResourceHandle3D Resource { get; }
    public long Offset { get; }
    public long ByteSize { get; }

    public bool Equals(RhiBindGroupEntry3D other)
        => Binding == other.Binding && Resource.Equals(other.Resource) && Offset == other.Offset && ByteSize == other.ByteSize;
    public override bool Equals(object? obj) => obj is RhiBindGroupEntry3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Binding, Resource, Offset, ByteSize);
}

internal sealed class RhiBindGroupDescriptor3D : IEquatable<RhiBindGroupDescriptor3D>
{
    private readonly RhiBindGroupEntry3D[] _entries;

    public RhiBindGroupDescriptor3D(string label, RhiResourceHandle3D layout, IEnumerable<RhiBindGroupEntry3D> entries)
    {
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Bind-group label cannot be empty.", nameof(label)) : label;
        if (!layout.IsValid || layout.Kind != RhiResourceKind3D.BindGroupLayout)
            throw new ArgumentException("Bind-group layout handle is invalid.", nameof(layout));
        Layout = layout;
        _entries = entries is null ? throw new ArgumentNullException(nameof(entries)) : new List<RhiBindGroupEntry3D>(entries).ToArray();
        Array.Sort(_entries, static (a, b) => a.Binding.CompareTo(b.Binding));
        for (var i = 1; i < _entries.Length; i++)
            if (_entries[i - 1].Binding == _entries[i].Binding)
                throw new ArgumentException("Duplicate bind-group binding.", nameof(entries));
    }

    public string Label { get; }
    public RhiResourceHandle3D Layout { get; }
    public ReadOnlySpan<RhiBindGroupEntry3D> Entries => _entries;

    public bool Equals(RhiBindGroupDescriptor3D? other)
    {
        if (other is null || !string.Equals(Label, other.Label, StringComparison.Ordinal) || !Layout.Equals(other.Layout) || _entries.Length != other._entries.Length)
            return false;
        for (var i = 0; i < _entries.Length; i++) if (!_entries[i].Equals(other._entries[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is RhiBindGroupDescriptor3D other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label, StringComparer.Ordinal);
        hash.Add(Layout);
        for (var i = 0; i < _entries.Length; i++) hash.Add(_entries[i]);
        return hash.ToHashCode();
    }
}
