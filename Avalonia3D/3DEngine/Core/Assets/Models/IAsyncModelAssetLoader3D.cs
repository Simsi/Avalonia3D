using System.Threading;
using System.Threading.Tasks;

namespace ThreeDEngine.Core.Assets.Models;

/// <summary>
/// Non-blocking model-loader contract used by <see cref="ThreeDEngine.Core.Assets.Streaming.AssetManager3D"/>.
/// Implementations must honor cancellation before publishing a completed asset and must not execute
/// synchronous file I/O on the browser UI thread.
/// </summary>
public interface IAsyncModelAssetLoader3D : IModelAssetLoader3D
{
    ValueTask<ModelAsset3D> LoadAsync(
        string path,
        ModelImportOptions? options = null,
        CancellationToken cancellationToken = default);
}
