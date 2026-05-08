using System.Collections.Generic;
using System.IO;

namespace ThreeDEngine.Core.Assets.Resolvers;

public sealed class CompositeAssetResolver3D : IAssetResolver3D
{
    private readonly List<IAssetResolver3D> _resolvers = new();

    public CompositeAssetResolver3D(params IAssetResolver3D[] resolvers)
    {
        if (resolvers is null) return;
        foreach (var resolver in resolvers)
        {
            if (resolver is not null) _resolvers.Add(resolver);
        }
    }

    public IReadOnlyList<IAssetResolver3D> Resolvers => _resolvers;

    public void Add(IAssetResolver3D resolver)
    {
        if (resolver is not null) _resolvers.Add(resolver);
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
