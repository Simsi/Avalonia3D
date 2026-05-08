using System.IO;

namespace ThreeDEngine.Core.Assets.Resolvers;

public interface IAssetResolver3D
{
    Stream? Open(string baseUri, string relativeUri);
    bool Exists(string baseUri, string relativeUri);
}
