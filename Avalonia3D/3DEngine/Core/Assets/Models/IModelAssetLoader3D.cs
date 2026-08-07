using System;

namespace ThreeDEngine.Core.Assets.Models;

/// <summary>
/// Application-facing contract for model loading. Importer implementations live in optional
/// asset packages and are registered explicitly in an <see cref="ThreeDEngine.Core.Hosting.Engine3DBuilder"/>.
/// </summary>
public interface IModelAssetLoader3D : IDisposable
{
    int CachedAssetCount { get; }
    ModelAsset3D Load(string path, ModelImportOptions? options = null);
    void Clear();
    bool Remove(string path, string? baseDirectory = null);
}
