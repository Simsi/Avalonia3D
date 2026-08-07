using System.Globalization;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Stable backend-neutral ids for retained render resources and draw batches.
///
/// The desktop and browser renderers must not each invent their own batch ids: when
/// the key rules diverge, material/skinning fixes have to be applied twice. This
/// helper centralizes the policy while keeping ids WebGL-safe and compact.
/// </summary>
internal static class RenderId3D
{
    public const ulong FnvOffsetBasis = 14695981039346656037UL;
    public const ulong FnvPrime = 1099511628211UL;

    public static string BuildLogicalMeshBatchKey(string meshResourceKey, string? gpuSkinOwnerId)
    {
        return string.IsNullOrEmpty(gpuSkinOwnerId)
            ? meshResourceKey
            : meshResourceKey + ":skin:" + gpuSkinOwnerId;
    }

    public static string BuildOrdinaryRetainedBatchId(string meshResourceKey, string materialKey, string? gpuSkinOwnerId)
    {
        var materialHash = StableHash64(materialKey);
        return BuildOrdinaryRetainedBatchId(meshResourceKey, materialHash, gpuSkinOwnerId);
    }

    public static string BuildOrdinaryRetainedBatchId(string meshResourceKey, ulong materialBatchHash, string? gpuSkinOwnerId)
    {
        var skinKey = string.IsNullOrEmpty(gpuSkinOwnerId) ? string.Empty : ":skin:" + StableHash(gpuSkinOwnerId);
        return "ord:" + FormatStableHash(StableHash64(meshResourceKey)) + ":" + FormatStableHash(materialBatchHash) + skinKey;
    }

    public static string BuildParticleRetainedBatchId(string particleSystemId, int renderMode)
        => "particles:" + particleSystemId + ":" + renderMode.ToString(CultureInfo.InvariantCulture);

    public static string BuildHighScaleBatchId(string layerId, int chunkX, int chunkY, int chunkZ, int lod, int partIndex)
        => $"hs:{layerId}:{chunkX}:{chunkY}:{chunkZ}:{lod}:{partIndex}";

    public static string BuildTransparentDrawId(string retainedBatchId, string ownerId)
    {
        // Source order is deliberately excluded. Packed scene registries may swap the last
        // element into a removed slot, and camera sorting may reorder items; neither event
        // changes the retained identity of the same object/material/mesh draw.
        var hash = StableHash64(retainedBatchId);
        hash = CombineStableHash(hash, StableHash64(ownerId));
        return FormatStableHash(hash, "tr:");
    }

    public static string BuildTransparentDepthBatchId(string retainedBatchId, int depthBin)
    {
        var hash = StableHash64(retainedBatchId);
        hash = CombineStableHash(hash, unchecked((ulong)(uint)depthBin));
        return FormatStableHash(hash, "tb:");
    }

    public static ulong CombineStableHash(ulong hash, ulong value)
    {
        unchecked
        {
            hash ^= value & 0xFFUL; hash *= FnvPrime;
            hash ^= (value >> 8) & 0xFFUL; hash *= FnvPrime;
            hash ^= (value >> 16) & 0xFFUL; hash *= FnvPrime;
            hash ^= (value >> 24) & 0xFFUL; hash *= FnvPrime;
            hash ^= (value >> 32) & 0xFFUL; hash *= FnvPrime;
            hash ^= (value >> 40) & 0xFFUL; hash *= FnvPrime;
            hash ^= (value >> 48) & 0xFFUL; hash *= FnvPrime;
            hash ^= (value >> 56) & 0xFFUL; hash *= FnvPrime;
            return hash;
        }
    }

    public static string StableHash(string value) => FormatStableHash(StableHash64(value));

    public static string FormatStableHash(ulong hash, string prefix = "")
        => prefix + hash.ToString("x16", CultureInfo.InvariantCulture);

    public static ulong StableHash64(string? value)
    {
        unchecked
        {
            ulong hash = FnvOffsetBasis;
            value ??= string.Empty;
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= FnvPrime;
            }

            return hash;
        }
    }
}
