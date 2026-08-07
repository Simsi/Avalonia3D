using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThreeDEngine.Core.Assets.Resolvers;

public sealed class CompositeAssetResolver3D : IAssetResolver3D
{
    private readonly List<IAssetResolver3D> _resolvers = new();
    private readonly ReadOnlyCollection<IAssetResolver3D> _resolversView;

    public CompositeAssetResolver3D(params IAssetResolver3D[] resolvers)
    {
        _resolversView = _resolvers.AsReadOnly();
        if (resolvers is null) throw new ArgumentNullException(nameof(resolvers));
        foreach (var resolver in resolvers)
        {
            _resolvers.Add(resolver ?? throw new ArgumentNullException(nameof(resolver)));
        }
    }

    public IReadOnlyList<IAssetResolver3D> Resolvers => _resolversView;

    public void Add(IAssetResolver3D resolver)
    {
        _resolvers.Add(resolver ?? throw new ArgumentNullException(nameof(resolver)));
    }

    public Stream? Open(string baseUri, string relativeUri)
    {
        foreach (var resolver in _resolvers)
        {
            var stream = resolver.Open(baseUri, relativeUri);
            if (stream is not null) return stream;
        }
        return null;
    }

    public bool Exists(string baseUri, string relativeUri)
    {
        foreach (var resolver in _resolvers)
        {
            if (resolver.Exists(baseUri, relativeUri)) return true;
        }
        return false;
    }
}
