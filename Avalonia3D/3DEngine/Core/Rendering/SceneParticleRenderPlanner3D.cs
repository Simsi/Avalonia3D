using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Culling;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Shared particle extraction/culling/stats path for all GPU backends.
/// Backend implementations only pack the resulting particles into their API-specific buffers.
/// </summary>
internal static class SceneParticleRenderPlanner3D
{
    public static void BuildVisible(
        SceneRenderFrameContext3D frame,
        List<ParticleRenderItem3D> output,
        SceneRenderPlanScratch3D scratch,
        RenderStats? stats = null,
        bool frustumCull = true)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (scratch is null) throw new ArgumentNullException(nameof(scratch));

        output.Clear();
        var cameraPosition = frame.Published.CameraPosition;
        foreach (var obj in frame.Snapshot.RenderablesInternal)
        {
            if (obj is not ParticleSystem3D particles)
            {
                continue;
            }

            if (stats is not null)
            {
                stats.ParticleSystemCount++;
                stats.ParticleCount += particles.AliveCount;
            }

            if (!particles.IsVisible || particles.AliveCount <= 0)
            {
                continue;
            }

            var parent = particles.Settings.SimulationSpace == ParticleSimulationSpace3D.World
                ? Matrix4x4.Identity
                : frame.Scene.FrameInterpolator.TryGetInterpolatedModel(particles.Id, out var interpolatedParent)
                    ? interpolatedParent
                    : particles.GetModelMatrix();

            var localBounds = particles.GetLocalParticleBounds();
            if (frustumCull && !FrustumCuller3D.IntersectsLocalBounds(localBounds, parent, frame.ViewProjection))
            {
                if (stats is not null)
                {
                    stats.CulledObjectCount++;
                }

                continue;
            }

            var mesh = ParticleSystem3D.GetStaticRenderMesh(particles.Settings.RenderMode);
            var material = MaterialBinding3D.FromMaterial(particles.Material);
            var billboard = particles.Settings.RenderMode == ParticleRenderMode3D.CameraFacingQuad;
            var transparent = ResolveTransparency(particles, material, billboard);
            var cameraDependentOrder = transparent || billboard;
            var batchId = scratch.GetParticleRetainedBatchId(particles.Id, (int)particles.Settings.RenderMode);
            var center = localBounds.IsValid
                ? Vector3.Transform(localBounds.Center, parent)
                : new Vector3(parent.M41, parent.M42, parent.M43);
            var item = new ParticleRenderItem3D(
                particles,
                mesh,
                material,
                parent,
                billboard,
                transparent,
                cameraDependentOrder,
                particles.Settings.SimulationSpace == ParticleSimulationSpace3D.World ? 1f : ResolveSizeScale(parent),
                batchId,
                Vector3.DistanceSquared(cameraPosition, center),
                output.Count);
            output.Add(item);

            if (stats is not null)
            {
                stats.VisibleMeshCount++;
                stats.ParticleVertexCount += mesh.Positions.Length * particles.AliveCount;
                stats.TriangleCount += (mesh.Indices.Length / 3) * particles.AliveCount;
            }
        }

        output.Sort(ParticleRenderItem3D.CompareForDraw);
    }

    public static bool RequiresCameraDependentOrder(ParticleRenderItem3D item)
        => item.CameraDependentOrder;

    public static bool ResolveTransparency(ParticleSystem3D particles, MaterialBinding3D material, bool billboard)
    {
        if (billboard || material.Surface == SurfaceMode.Transparent || material.BaseColor.A < 0.999f)
        {
            return true;
        }

        var settings = particles.Settings;
        if (settings.StartColor.A < 0.999f || settings.EndColor.A < 0.999f)
        {
            return true;
        }

        var active = particles.Particles;
        var count = particles.AliveCount;
        for (var i = 0; i < count; i++)
        {
            var particle = active[i];
            if (particle.StartColor.A < 0.999f || particle.EndColor.A < 0.999f)
            {
                return true;
            }
        }

        return false;
    }

    public static float ResolveSizeScale(Matrix4x4 model)
    {
        var x = Vector3.TransformNormal(Vector3.UnitX, model).Length();
        var y = Vector3.TransformNormal(Vector3.UnitY, model).Length();
        var z = Vector3.TransformNormal(Vector3.UnitZ, model).Length();
        var scale = MathF.Max(x, MathF.Max(y, z));
        return float.IsFinite(scale) && scale > 0.0001f ? scale : 1f;
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static ColorRgba Lerp(ColorRgba a, ColorRgba b, float t)
        => new(Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t), Lerp(a.A, b.A, t));
}
