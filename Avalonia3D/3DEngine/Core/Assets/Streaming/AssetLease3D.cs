using System;
using ThreeDEngine.Core.Assets.Models;

namespace ThreeDEngine.Core.Assets.Streaming;

/// <summary>Pins a resident model until disposed. Leases are idempotent and thread-safe.</summary>
public sealed class AssetLease3D : IDisposable
{
    private AssetManager3D? _owner;

    internal AssetLease3D(AssetManager3D owner, string key, ModelAsset3D asset)
    {
        _owner = owner;
        Key = key;
        Asset = asset;
    }

    public string Key { get; }
    public ModelAsset3D Asset { get; }
    public bool IsDisposed => _owner is null;

    public void Dispose()
    {
        var owner = System.Threading.Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseLease(Key);
    }
}
