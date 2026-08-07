using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Materials;

/// <summary>Versioned custom material shader contract compiled only by native GPU backends.</summary>
public sealed class MaterialShaderExtensionDefinition3D
{
    private readonly HashSet<int> _materialTypes;
    private readonly ReadOnlyCollection<int> _materialTypesView;

    public MaterialShaderExtensionDefinition3D(
        string extensionId,
        int version,
        IEnumerable<int> materialTypes,
        ShaderResource3D vertexShader,
        ShaderResource3D fragmentShader,
        int parameterByteSize,
        int parameterAlignment = 16,
        int maximumTextures = 8)
    {
        ExtensionId = string.IsNullOrWhiteSpace(extensionId) ? throw new ArgumentException("Extension id cannot be empty.", nameof(extensionId)) : extensionId.Trim();
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        Version = version;
        _materialTypes = materialTypes?.ToHashSet() ?? throw new ArgumentNullException(nameof(materialTypes));
        if (_materialTypes.Count == 0 || _materialTypes.Any(static type => type < 0)) throw new ArgumentException("At least one non-negative material type is required.", nameof(materialTypes));
        var orderedTypes = _materialTypes.OrderBy(static type => type).ToArray();
        _materialTypesView = Array.AsReadOnly(orderedTypes);
        VertexShader = vertexShader ?? throw new ArgumentNullException(nameof(vertexShader));
        FragmentShader = fragmentShader ?? throw new ArgumentNullException(nameof(fragmentShader));
        if (VertexShader.Stage != ShaderStage3D.Vertex) throw new ArgumentException("Vertex shader resource has the wrong stage.", nameof(vertexShader));
        if (FragmentShader.Stage != ShaderStage3D.Fragment) throw new ArgumentException("Fragment shader resource has the wrong stage.", nameof(fragmentShader));
        if (parameterByteSize < 0) throw new ArgumentOutOfRangeException(nameof(parameterByteSize));
        if (parameterAlignment <= 0 || (parameterAlignment & (parameterAlignment - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(parameterAlignment), "Parameter alignment must be a positive power of two.");
        if (parameterByteSize != 0 && parameterByteSize % parameterAlignment != 0) throw new ArgumentException("Parameter byte size must be aligned.", nameof(parameterByteSize));
        if (maximumTextures < 0 || maximumTextures > 64) throw new ArgumentOutOfRangeException(nameof(maximumTextures));
        ParameterByteSize = parameterByteSize;
        ParameterAlignment = parameterAlignment;
        MaximumTextures = maximumTextures;
    }

    public string ExtensionId { get; }
    public int Version { get; }
    public IReadOnlyCollection<int> MaterialTypes => _materialTypesView;
    public ShaderResource3D VertexShader { get; }
    public ShaderResource3D FragmentShader { get; }
    public int ParameterByteSize { get; }
    public int ParameterAlignment { get; }
    public int MaximumTextures { get; }

    public void Validate(MaterialShaderExtension3D material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!StringComparer.Ordinal.Equals(material.ExtensionId, ExtensionId)) throw new InvalidOperationException($"Material extension '{material.ExtensionId}' does not match definition '{ExtensionId}'.");
        if (!_materialTypes.Contains(material.MaterialType)) throw new InvalidOperationException($"Material type {material.MaterialType} is not registered for extension '{ExtensionId}'.");
        if (material.ParameterByteLength != ParameterByteSize) throw new InvalidOperationException($"Material extension '{ExtensionId}' requires {ParameterByteSize} parameter bytes, but {material.ParameterByteLength} were supplied.");
        if (material.Textures.Count > MaximumTextures) throw new InvalidOperationException($"Material extension '{ExtensionId}' allows at most {MaximumTextures} textures, but {material.Textures.Count} were supplied.");
    }
}

public sealed class MaterialShaderExtensionRegistry3D
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MaterialShaderExtensionDefinition3D> _definitions = new(StringComparer.Ordinal);
    private long _version;

    public long Version { get { lock (_gate) return _version; } }
    public int Count { get { lock (_gate) return _definitions.Count; } }

    public void Register(MaterialShaderExtensionDefinition3D definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_gate)
        {
            if (_definitions.ContainsKey(definition.ExtensionId)) throw new InvalidOperationException($"Material shader extension '{definition.ExtensionId}' is already registered.");
            _definitions.Add(definition.ExtensionId, definition);
            _version = checked(_version + 1);
        }
    }

    public void Replace(MaterialShaderExtensionDefinition3D definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_gate)
        {
            if (!_definitions.TryGetValue(definition.ExtensionId, out var existing))
                throw new KeyNotFoundException($"Material shader extension '{definition.ExtensionId}' is not registered.");
            if (definition.Version <= existing.Version)
                throw new InvalidOperationException($"Material shader extension '{definition.ExtensionId}' replacement version {definition.Version} must be greater than current version {existing.Version}.");
            _definitions[definition.ExtensionId] = definition;
            _version = checked(_version + 1);
        }
    }

    public bool Remove(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId)) return false;
        lock (_gate)
        {
            if (!_definitions.Remove(extensionId.Trim())) return false;
            _version = checked(_version + 1);
            return true;
        }
    }

    public MaterialShaderExtensionDefinition3D GetRequired(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId)) throw new ArgumentException("Extension id cannot be empty.", nameof(extensionId));
        lock (_gate)
            return _definitions.TryGetValue(extensionId.Trim(), out var definition)
                ? definition
                : throw new KeyNotFoundException($"Material shader extension '{extensionId}' is not registered.");
    }

    public void Validate(MaterialShaderExtension3D material)
    {
        ArgumentNullException.ThrowIfNull(material);
        GetRequired(material.ExtensionId).Validate(material);
    }

    public MaterialShaderExtensionRegistrySnapshot3D CaptureSnapshot()
    {
        lock (_gate)
            return new MaterialShaderExtensionRegistrySnapshot3D(_version, _definitions.Values.OrderBy(static item => item.ExtensionId, StringComparer.Ordinal).ToArray());
    }
}

public sealed class MaterialShaderExtensionRegistrySnapshot3D
{
    private readonly ReadOnlyCollection<MaterialShaderExtensionDefinition3D> _definitions;
    internal MaterialShaderExtensionRegistrySnapshot3D(long version, MaterialShaderExtensionDefinition3D[] definitions)
    {
        Version = version;
        _definitions = Array.AsReadOnly(definitions ?? throw new ArgumentNullException(nameof(definitions)));
    }
    public long Version { get; }
    public IReadOnlyList<MaterialShaderExtensionDefinition3D> Definitions => _definitions;
}
