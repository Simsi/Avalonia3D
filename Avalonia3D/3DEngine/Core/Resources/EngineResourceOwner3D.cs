using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Core.Resources;

/// <summary>
/// Explicit lifetime scope for immutable CPU resources. Replacing a category's set adjusts
/// reference counts atomically; disposing the owner releases all texture and shader references.
/// </summary>
public sealed class EngineResourceOwner3D : IDisposable
{
    private EngineResourceManager3D? _manager;

    internal EngineResourceOwner3D(EngineResourceManager3D manager, string id, string name)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsDisposed => _manager is null;

    public void SetTextures(IReadOnlyList<TextureResource3D> textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        Manager.SynchronizeOwnerTextures(Id, textures);
    }

    public void SetShaders(IReadOnlyList<ShaderResource3D> shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        Manager.SynchronizeOwnerShaders(Id, shaders);
    }

    public void ClearTextures() => Manager.SynchronizeOwnerTextures(Id, Array.Empty<TextureResource3D>());
    public void ClearShaders() => Manager.SynchronizeOwnerShaders(Id, Array.Empty<ShaderResource3D>());
    public void Clear() => Manager.ClearOwner(Id);

    internal void SetRenderTextures(IReadOnlyList<RenderTextureResource3D> textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        Manager.SynchronizeOwnerTextures(Id, textures);
    }

    public void Dispose()
    {
        var manager = _manager;
        if (manager is null) return;
        _manager = null;
        manager.ReleaseOwner(Id);
    }

    private EngineResourceManager3D Manager
        => _manager ?? throw new ObjectDisposedException(nameof(EngineResourceOwner3D));
}
