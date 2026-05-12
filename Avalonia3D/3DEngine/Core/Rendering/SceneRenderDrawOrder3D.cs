using System;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral ordering and hashing for retained draw packets.  The backend may own
/// the actual buffer/draw implementation, but the ordering rule must stay identical:
/// opaque first, transparent last, transparent back-to-front, stable source order as tie-breaker.
/// </summary>
public static class SceneRenderDrawOrder3D
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static int Compare(
        bool aTransparent,
        float aSortDistanceSquared,
        int aSourceOrder,
        string? aId,
        bool bTransparent,
        float bSortDistanceSquared,
        int bSourceOrder,
        string? bId)
    {
        if (aTransparent != bTransparent) return aTransparent ? 1 : -1;
        if (aTransparent)
        {
            var distanceCompare = bSortDistanceSquared.CompareTo(aSortDistanceSquared);
            if (distanceCompare != 0) return distanceCompare;
        }

        var orderCompare = aSourceOrder.CompareTo(bSourceOrder);
        if (orderCompare != 0) return orderCompare;
        return string.CompareOrdinal(aId, bId);
    }

    public static ulong HashPacket(
        ulong hash,
        string? id,
        bool transparent,
        float sortDistanceSquared,
        int sourceOrder = 0,
        bool includeSourceOrder = false)
    {
        hash = HashString(hash, id);
        hash = HashBool(hash, transparent);
        hash = HashFloat(hash, sortDistanceSquared);
        if (includeSourceOrder)
        {
            hash = HashInt(hash, sourceOrder);
        }

        return hash;
    }

    public static ulong CreateHashSeed() => FnvOffset;

    private static ulong HashString(ulong hash, string? value)
    {
        unchecked
        {
            if (string.IsNullOrEmpty(value)) return HashInt(hash, 0);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch == '\n' || ch == '\r') continue;
                hash ^= ch;
                hash *= FnvPrime;
            }

            return hash;
        }
    }

    private static ulong HashBool(ulong hash, bool value)
    {
        unchecked
        {
            hash ^= value ? 1UL : 0UL;
            hash *= FnvPrime;
            return hash;
        }
    }

    private static ulong HashFloat(ulong hash, float value)
        => HashInt(hash, BitConverter.SingleToInt32Bits(value));

    private static ulong HashInt(ulong hash, int value)
    {
        unchecked
        {
            var bits = (uint)value;
            hash ^= bits & 0xFFUL;
            hash *= FnvPrime;
            hash ^= (bits >> 8) & 0xFFUL;
            hash *= FnvPrime;
            hash ^= (bits >> 16) & 0xFFUL;
            hash *= FnvPrime;
            hash ^= (bits >> 24) & 0xFFUL;
            hash *= FnvPrime;
            return hash;
        }
    }
}
