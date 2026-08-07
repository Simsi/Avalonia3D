using System;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Hosting;

namespace ThreeDEngine.Core.Importers.Gltf;

/// <summary>Registers the glTF/GLB importer and its engine-scoped cache.</summary>
public static class GltfEngineBuilderExtensions3D
{
    public static Engine3DBuilder UseGltfAssets(this Engine3DBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseModelAssets(services => new ModelAssetCache3D(services.GetRequiredService<ContentAddressedAssetCache3D>()));
    }
}
