using System.Globalization;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Stable backend-neutral ids for retained render resources and draw batches.
///
/// The desktop and browser renderers must not each invent their own batch ids: when
/// the key rules diverge, material/skinning fixes have to be applied twice. This
/// helper centralizes the policy while keeping ids WebGL-safe and compact.
/// </summary>
public static class RenderId3D
{
    public static string BuildLogicalMeshBatchKey(string meshResourceKey, string? gpuSkinOwnerId)
    {
        return string.IsNullOrEmpty(gpuSkinOwnerId)
            ? meshResourceKey
            : meshResourceKey + ":skin:" + gpuSkinOwnerId;
    }

    public static string BuildOrdinaryRetainedBatchId(string meshResourceKey, string materialKey, string? gpuSkinOwnerId)
    {
        var skinKey = string.IsNullOrEmpty(gpuSkinOwnerId) ? string.Empty : ":skin:" + StableHash(gpuSkinOwnerId);
        return "ord:" + StableHash(meshResourceKey) + ":" + StableHash(materialKey) + skinKey;
    }

    public static string BuildParticleRetainedBatchId(string particleSystemId, object renderMode)
        => "particles:" + particleSystemId + ":" + renderMode;

    public static string BuildHighScaleBatchId(string layerId, int chunkX, int chunkY, int chunkZ, int lod, int partIndex)
        => $"hs:{layerId}:{chunkX}:{chunkY}:{chunkZ}:{lod}:{partIndex}";

    public static string StableHash(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            value ??= string.Empty;
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
