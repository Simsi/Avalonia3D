using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral resource upload/liveness plan for a render frame.
///
/// The plan is intentionally derived from <see cref="SceneRenderPlan3D"/> rather than a raw
/// scene snapshot. This keeps browser and desktop backends from independently rediscovering
/// meshes/textures and prevents partial retained frames from deleting cached resources that
/// were intentionally not re-planned.
/// </summary>
public sealed class RenderResourcePlan3D
{
    private readonly List<Mesh3D> _meshes = new(64);
    private readonly List<RenderTextureResource3D> _textures = new(32);
    private readonly HashSet<string> _meshKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _textureKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _textureIndexByKey = new(StringComparer.Ordinal);

    internal RenderResourcePlan3D()
    {
    }

    internal RenderResourcePlan3D(bool includesOrdinary, bool includesParticles, bool includesHighScale)
    {
        Reset(includesOrdinary, includesParticles, includesHighScale);
    }

    internal void Reset(bool includesOrdinary, bool includesParticles, bool includesHighScale)
    {
        _meshes.Clear();
        _textures.Clear();
        _meshKeys.Clear();
        _textureKeys.Clear();
        _textureIndexByKey.Clear();
        IncludesOrdinary = includesOrdinary;
        IncludesParticles = includesParticles;
        IncludesHighScale = includesHighScale;
    }

    public IReadOnlyList<Mesh3D> Meshes => _meshes;
    public IReadOnlyList<RenderTextureResource3D> Textures => _textures;

    public bool IncludesOrdinary { get; private set; }
    public bool IncludesParticles { get; private set; }
    public bool IncludesHighScale { get; private set; }

    /// <summary>
    /// True only when the plan covers all scene categories that can own retained WebGL mesh resources.
    /// Partial retained frames use cached renderer state, so sweeping from such a plan would be unsafe.
    /// </summary>
    public bool IsCompleteForMeshSweep => IncludesOrdinary && IncludesParticles && IncludesHighScale;

    public bool ContainsMesh(string key) => _meshKeys.Contains(key ?? string.Empty);
    public bool ContainsTexture(string key) => _textureKeys.Contains(key ?? string.Empty);

    internal void AddMesh(Mesh3D mesh)
    {
        if (mesh is null) return;
        var key = mesh.ResourceKey;
        if (string.IsNullOrWhiteSpace(key) || !_meshKeys.Add(key)) return;
        _meshes.Add(mesh);
    }

    internal void AddTexture(string? key, byte[]? data, int version)
    {
        if (string.IsNullOrWhiteSpace(key) || data is not { Length: > 0 }) return;

        if (_textureIndexByKey.TryGetValue(key, out var existingIndex))
        {
            // Same texture key should normally have a single version. If content was replaced under
            // the same key, keep the newest descriptor so upload state converges deterministically.
            if (version >= _textures[existingIndex].Version)
            {
                _textures[existingIndex] = new RenderTextureResource3D(key, data, version);
            }
            return;
        }

        _textureKeys.Add(key);
        _textureIndexByKey.Add(key, _textures.Count);
        _textures.Add(new RenderTextureResource3D(key, data, version));
    }

    internal void AddTextureKey(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key)) _textureKeys.Add(key);
    }
}
