using System;
using System.Numerics;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Shared particle instance packing and per-system transparent ordering. Backends provide
/// storage/upload; Core owns the transform, color, scale and order math.
/// </summary>
internal static class ParticleInstanceStream3D
{
    public const int BillboardFloatStride = 8; // center.xyz + size + color.rgba
    public const int MeshFloatStride = 20; // model matrix + color.rgba
    public const int MaxFloatStride = MeshFloatStride;

    public static int GetFloatStride(bool billboard) => billboard ? BillboardFloatStride : MeshFloatStride;

    public static bool ShouldSortBackToFront(ParticleRenderItem3D item)
        => item.CameraDependentOrder;

    public static void EnsureSortScratch(ref int[] order, ref float[] sortKeys, int count)
    {
        if (count <= 0) return;
        if (order.Length < count) Array.Resize(ref order, global::System.Math.Max(count, global::System.Math.Max(16, order.Length * 2)));
        if (sortKeys.Length < count) Array.Resize(ref sortKeys, global::System.Math.Max(count, global::System.Math.Max(16, sortKeys.Length * 2)));
    }

    /// <summary>
    /// Produces source particle indices in back-to-front order. The sort key is negated
    /// squared distance so Array.Sort's ascending order yields farthest particles first.
    /// </summary>
    public static void BuildBackToFrontOrder(ParticleRenderItem3D item, Vector3 cameraPosition, int[] order, float[] sortKeys)
    {
        var particles = item.System.Particles;
        var count = item.System.AliveCount;
        for (var i = 0; i < count; i++)
        {
            var worldPosition = Vector3.Transform(particles[i].Position, item.ParentModel);
            order[i] = i;
            sortKeys[i] = -Vector3.DistanceSquared(cameraPosition, worldPosition);
        }

        Array.Sort(sortKeys, order, 0, count);
    }

    public static ParticleRenderInstance3D ResolveInstance(ParticleRenderItem3D item, Particle3D particle, Vector3 cameraPosition)
    {
        var t = particle.NormalizedAge;
        var size = SceneParticleRenderPlanner3D.Lerp(particle.StartSize, particle.EndSize, t) * item.SizeScale;
        var color = SceneParticleRenderPlanner3D.Lerp(particle.StartColor, particle.EndColor, t);
        var center = Vector3.Transform(particle.Position, item.ParentModel);
        var model = item.Billboard
            ? Matrix4x4.Identity
            : Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(particle.Position) * item.ParentModel;
        return new ParticleRenderInstance3D(center, size, model, color, Vector3.DistanceSquared(cameraPosition, center));
    }

    public static int WriteInstances(
        ParticleRenderItem3D item,
        Vector3 cameraPosition,
        float[] destination,
        int[]? sourceOrder = null)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        var count = item.System.AliveCount;
        var stride = GetFloatStride(item.Billboard);
        var required = count * stride;
        if (destination.Length < required)
        {
            throw new ArgumentException("Destination particle buffer is too small.", nameof(destination));
        }

        var particles = item.System.Particles;
        for (var writeIndex = 0; writeIndex < count; writeIndex++)
        {
            var sourceIndex = sourceOrder is null ? writeIndex : sourceOrder[writeIndex];
            var instance = ResolveInstance(item, particles[sourceIndex], cameraPosition);
            WriteInstance(item.Billboard, destination, writeIndex * stride, instance);
        }

        return required;
    }

    public static void WriteInstance(bool billboard, float[] destination, int offset, ParticleRenderInstance3D instance)
    {
        if (billboard)
        {
            destination[offset + 0] = instance.Center.X;
            destination[offset + 1] = instance.Center.Y;
            destination[offset + 2] = instance.Center.Z;
            destination[offset + 3] = instance.Size;
            destination[offset + 4] = instance.Color.R;
            destination[offset + 5] = instance.Color.G;
            destination[offset + 6] = instance.Color.B;
            destination[offset + 7] = instance.Color.A;
            return;
        }

        WriteMatrix(destination, offset, instance.Model);
        destination[offset + 16] = instance.Color.R;
        destination[offset + 17] = instance.Color.G;
        destination[offset + 18] = instance.Color.B;
        destination[offset + 19] = instance.Color.A;
    }

    public static void WriteMatrix(float[] buffer, int offset, Matrix4x4 matrix)
    {
        buffer[offset + 0] = matrix.M11;
        buffer[offset + 1] = matrix.M12;
        buffer[offset + 2] = matrix.M13;
        buffer[offset + 3] = matrix.M14;
        buffer[offset + 4] = matrix.M21;
        buffer[offset + 5] = matrix.M22;
        buffer[offset + 6] = matrix.M23;
        buffer[offset + 7] = matrix.M24;
        buffer[offset + 8] = matrix.M31;
        buffer[offset + 9] = matrix.M32;
        buffer[offset + 10] = matrix.M33;
        buffer[offset + 11] = matrix.M34;
        buffer[offset + 12] = matrix.M41;
        buffer[offset + 13] = matrix.M42;
        buffer[offset + 14] = matrix.M43;
        buffer[offset + 15] = matrix.M44;
    }
}
