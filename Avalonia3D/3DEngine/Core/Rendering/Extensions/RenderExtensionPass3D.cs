using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Rendering.Extensions;

/// <summary>
/// Immutable backend-neutral custom pass declaration. Shader modules are content-addressed and
/// resource dependencies are explicit. Native backends must validate reflection and capabilities
/// before compiling this pass; legacy backends must reject it.
/// </summary>
public sealed class RenderExtensionPass3D
{
    private readonly RenderExtensionResource3D[] _resources;
    private readonly ReadOnlyCollection<RenderExtensionResource3D> _resourcesView;

    public RenderExtensionPass3D(
        string id,
        RenderExtensionStage3D stage,
        RenderExtensionPassKind3D kind,
        ShaderResource3D shader,
        IEnumerable<RenderExtensionResource3D> resources,
        string entryPoint = "main")
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Pass id cannot be empty.", nameof(id)) : id.Trim();
        Stage = Enum.IsDefined(stage) ? stage : throw new ArgumentOutOfRangeException(nameof(stage));
        Kind = Enum.IsDefined(kind) ? kind : throw new ArgumentOutOfRangeException(nameof(kind));
        Shader = shader ?? throw new ArgumentNullException(nameof(shader));
        EntryPoint = string.IsNullOrWhiteSpace(entryPoint) ? throw new ArgumentException("Entry point cannot be empty.", nameof(entryPoint)) : entryPoint.Trim();
        _resources = resources?.ToArray() ?? throw new ArgumentNullException(nameof(resources));
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _resources.Length; i++)
        {
            _resources[i].Validate(Id);
            if (!names.Add(_resources[i].Name)) throw new ArgumentException($"Duplicate resource '{_resources[i].Name}' in pass '{Id}'.", nameof(resources));
        }
        _resourcesView = Array.AsReadOnly(_resources);
    }

    public string Id { get; }
    public RenderExtensionStage3D Stage { get; }
    public RenderExtensionPassKind3D Kind { get; }
    public ShaderResource3D Shader { get; }
    public string EntryPoint { get; }
    public IReadOnlyList<RenderExtensionResource3D> Resources => _resourcesView;
}
