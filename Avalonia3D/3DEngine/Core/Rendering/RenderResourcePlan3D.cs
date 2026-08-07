using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral resource upload/liveness plan derived from a render plan. Texture entries
/// are unique by immutable content identity; logical aliases are validated separately.
/// </summary>
internal sealed class RenderResourcePlan3D
{
    private readonly List<Mesh3D> _meshes = new(64);
    private readonly ReadOnlyCollection<Mesh3D> _meshesView;
    private readonly List<RenderGeometry3D> _geometries = new(64);
    private readonly ReadOnlyCollection<RenderGeometry3D> _geometriesView;
    private readonly List<RenderTextureResource3D> _textures = new(32);
    private readonly ReadOnlyCollection<RenderTextureResource3D> _texturesView;
    private readonly Dictionary<string, long> _meshVersions = new(StringComparer.Ordinal);
    private readonly HashSet<long> _geometryVersions = new();
    private readonly HashSet<string> _textureKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _textureAliasTargets = new(StringComparer.Ordinal);

    internal RenderResourcePlan3D()
    {
        _meshesView = _meshes.AsReadOnly();
        _geometriesView = _geometries.AsReadOnly();
        _texturesView = _textures.AsReadOnly();
    }

    internal RenderResourcePlan3D(bool includesOrdinary, bool includesParticles, bool includesHighScale)
        : this() => Reset(includesOrdinary, includesParticles, includesHighScale);

    internal void Reset(bool includesOrdinary, bool includesParticles, bool includesHighScale)
    {
        _meshes.Clear();
        _geometries.Clear();
        _textures.Clear();
        _meshVersions.Clear();
        _geometryVersions.Clear();
        _textureKeys.Clear();
        _textureAliasTargets.Clear();
        IncludesOrdinary = includesOrdinary;
        IncludesParticles = includesParticles;
        IncludesHighScale = includesHighScale;
    }

    public IReadOnlyList<Mesh3D> Meshes => _meshesView;
    public IReadOnlyList<RenderGeometry3D> Geometries => _geometriesView;
    public IReadOnlyList<RenderTextureResource3D> Textures => _texturesView;
    public bool IncludesOrdinary { get; private set; }
    public bool IncludesParticles { get; private set; }
    public bool IncludesHighScale { get; private set; }
    public bool IsCompleteForMeshSweep => IncludesOrdinary && IncludesParticles && IncludesHighScale;
    public bool IsCompleteForResourceOwnership => IsCompleteForMeshSweep;

    public bool ContainsMesh(string key) => _meshVersions.ContainsKey(key ?? string.Empty);
    public bool ContainsTexture(string physicalResourceKey) => _textureKeys.Contains(physicalResourceKey ?? string.Empty);

    internal void AddMesh(Mesh3D mesh)
    {
        if (mesh is null) return;
        var key = mesh.ResourceKey;
        if (string.IsNullOrWhiteSpace(key)) return;
        if (_meshVersions.TryGetValue(key, out var existingVersion))
        {
            if (existingVersion != mesh.GeometryVersion)
            {
                throw new InvalidOperationException($"Mesh resource key collision: '{key}' identifies immutable geometry versions {existingVersion} and {mesh.GeometryVersion} in one render plan.");
            }
            return;
        }

        _meshVersions.Add(key, mesh.GeometryVersion);
        _meshes.Add(mesh);
        if (_geometryVersions.Add(mesh.GeometryVersion)) _geometries.Add(mesh.RenderGeometry);
    }

    internal void AddTexture(TextureResource3D? resource)
    {
        if (resource is null) return;
        if (_textureAliasTargets.TryGetValue(resource.LogicalKey, out var existingTarget))
        {
            if (!string.Equals(existingTarget, resource.ResourceKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Texture alias collision: logical key '{resource.LogicalKey}' identifies immutable resources '{existingTarget}' and '{resource.ResourceKey}' in one render plan.");
            }
        }
        else
        {
            _textureAliasTargets.Add(resource.LogicalKey, resource.ResourceKey);
        }

        if (!_textureKeys.Add(resource.ResourceKey)) return;
        _textures.Add(new RenderTextureResource3D(resource));
    }

    internal void AddTextureKey(string? physicalResourceKey)
    {
        if (!string.IsNullOrWhiteSpace(physicalResourceKey)) _textureKeys.Add(physicalResourceKey);
    }
}
