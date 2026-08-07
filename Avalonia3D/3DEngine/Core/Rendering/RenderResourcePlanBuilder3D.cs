using System;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Builds exact frame resource plans from the backend-neutral render plan.
/// Backends should upload/sweep render meshes and material/environment textures from this
/// output instead of scanning raw scene snapshots.
/// </summary>
internal static class RenderResourcePlanBuilder3D
{
    public static void BuildInto(
        SceneRenderFrameContext3D frame,
        System.Collections.Generic.IReadOnlyList<OrdinaryRenderBatch3D> ordinaryBatches,
        System.Collections.Generic.IReadOnlyList<TransparentOrdinaryRenderItem3D> transparentOrdinaryItems,
        System.Collections.Generic.IReadOnlyList<TransparentOrdinaryBatch3D> transparentOrdinaryBatches,
        System.Collections.Generic.IReadOnlyList<ParticleRenderItem3D> particleItems,
        System.Collections.Generic.IReadOnlyList<HighScaleInstanceLayer3D> highScaleLayers,
        bool includesOrdinary,
        bool includesParticles,
        bool includesHighScale,
        RenderResourcePlan3D plan)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        plan.Reset(includesOrdinary, includesParticles, includesHighScale);

        if (includesOrdinary)
        {
            for (var i = 0; i < ordinaryBatches.Count; i++)
            {
                var batch = ordinaryBatches[i];
                plan.AddMesh(batch.Mesh);
                AddMaterialTextures(plan, batch.Material);
            }

            for (var i = 0; i < transparentOrdinaryItems.Count; i++)
            {
                var item = transparentOrdinaryItems[i].Item;
                plan.AddMesh(item.Mesh);
                AddMaterialTextures(plan, item.Material);
            }

            for (var i = 0; i < transparentOrdinaryBatches.Count; i++)
            {
                var batch = transparentOrdinaryBatches[i];
                plan.AddMesh(batch.Mesh);
                AddMaterialTextures(plan, batch.Material);
            }
        }

        if (includesParticles)
        {
            for (var i = 0; i < particleItems.Count; i++)
            {
                var item = particleItems[i];
                plan.AddMesh(item.Mesh);
                AddMaterialTextures(plan, item.Material);
            }
        }

        if (includesHighScale)
        {
            for (var i = 0; i < highScaleLayers.Count; i++)
            {
                AddHighScaleRuntimeMeshes(plan, highScaleLayers[i]);
            }
        }

        AddEnvironmentTextures(plan, frame.Scene.Environment.Skybox);
        frame.Scene.SynchronizeResourceOwnership(frame.Snapshot);
    }

    private static void AddMaterialTextures(RenderResourcePlan3D plan, MaterialBinding3D material)
    {
        plan.AddTexture(material.BaseColorTextureResource);
        plan.AddTexture(material.NormalMapTextureResource);
        plan.AddTexture(material.MetallicRoughnessTextureResource);
        plan.AddTexture(material.EmissiveTextureResource);
    }

    private static void AddEnvironmentTextures(RenderResourcePlan3D plan, Skybox3D skybox)
    {
        if (skybox.HasEquirectangularTexture)
        {
            plan.AddTexture(skybox.EquirectangularTextureInternal);
        }

        if (!skybox.HasCubemapTextures) return;
        for (var i = 0; i < 6 && i < skybox.CubemapTexturesInternal.Count; i++)
        {
            plan.AddTexture(skybox.CubemapTexturesInternal[i]);
        }
    }

    private static void AddHighScaleRuntimeMeshes(RenderResourcePlan3D plan, HighScaleInstanceLayer3D layer)
    {
        AddHighScaleLodMeshes(plan, layer, HighScaleLodLevel3D.Detailed);
        AddHighScaleLodMeshes(plan, layer, HighScaleLodLevel3D.Simplified);
        AddHighScaleLodMeshes(plan, layer, HighScaleLodLevel3D.Proxy);
        AddHighScaleLodMeshes(plan, layer, HighScaleLodLevel3D.Billboard);
    }

    private static void AddHighScaleLodMeshes(RenderResourcePlan3D plan, HighScaleInstanceLayer3D layer, HighScaleLodLevel3D lod)
    {
        foreach (var part in layer.Template.ResolveParts(lod))
        {
            plan.AddMesh(part.Mesh);
        }
    }
}
