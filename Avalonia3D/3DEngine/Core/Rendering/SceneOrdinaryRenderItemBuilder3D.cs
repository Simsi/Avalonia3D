using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Instancing;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Shared ordinary render extraction for all GPU backends.
///
/// Keep visibility, particle exclusion, interpolation, color override, batch key and
/// skinned mesh fallback rules here so OpenGL and WebGL do not drift apart.
/// </summary>
public static class SceneOrdinaryRenderItemBuilder3D
{
    public static void Build(
        Scene3D scene,
        SceneFrameSnapshot3D snapshot,
        List<OrdinaryRenderItem3D> output,
        Func<ModelPart3D?, bool>? requiresCpuSkinFallback = null,
        RenderStats? stats = null)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (output is null) throw new ArgumentNullException(nameof(output));

        output.Clear();
        foreach (var obj in snapshot.Renderables)
        {
            if (!obj.IsVisible || !obj.UseMeshRendering)
            {
                continue;
            }

            // Particle systems have their own retained/billboard runtime on both backends.
            if (obj is ParticleSystem3D)
            {
                continue;
            }

            if (obj is InstancedMesh3D instancedMesh && stats is not null)
            {
                stats.InstancedMeshLayerCount++;
                stats.InstancedMeshInstanceCount += instancedMesh.Instances.Count;
            }

            var skinnedPart = obj as ModelPart3D;
            var useCpuSkinFallback = requiresCpuSkinFallback?.Invoke(skinnedPart) == true;
            var mesh = useCpuSkinFallback ? skinnedPart!.GetCpuSkinnedFallbackMesh() : obj.GetMesh();
            if (mesh.Positions.Length == 0 || mesh.Indices.Length == 0)
            {
                continue;
            }

            var model = scene.FrameInterpolator.TryGetInterpolatedModel(obj.Id, out var interpolatedModel)
                ? interpolatedModel
                : obj.GetModelMatrix();
            var material = MaterialBinding3D.FromMaterial(obj.Material);
            var usesGpuSkinning = skinnedPart is not null && skinnedPart.IsSkinned && !useCpuSkinFallback;
            var skinOwnerId = usesGpuSkinning ? obj.Id : null;
            var logicalBatchKey = RenderId3D.BuildLogicalMeshBatchKey(mesh.ResourceKey, skinOwnerId);
            var retainedBatchId = RenderId3D.BuildOrdinaryRetainedBatchId(mesh.ResourceKey, material.Key, skinOwnerId);

            output.Add(new OrdinaryRenderItem3D(
                obj,
                mesh,
                material,
                model,
                ResolveColor(obj),
                useCpuSkinFallback,
                usesGpuSkinning,
                logicalBatchKey,
                retainedBatchId));

            if (stats is not null)
            {
                stats.VisibleMeshCount++;
                stats.TriangleCount += mesh.Indices.Length / 3;
                if (material.HasNormalMap) stats.NormalMappedMeshCount++;
            }
        }
    }

    public static ColorRgba ResolveColor(Object3D obj)
    {
        var color = obj.Material.EffectiveColor;
        if (obj.IsEffectivelyHovered) color = color.BlendTowards(ColorRgba.White, 0.10f);
        if (obj.IsEffectivelySelected) color = color.BlendTowards(ColorRgba.White, 0.22f);
        return color;
    }
}
