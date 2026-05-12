using System.Collections.Generic;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Shared live-resource collector for renderer sweep/upload paths.
///
/// Desktop OpenGL and WebGL used to keep separate copies of material texture and mesh
/// liveness logic. That caused stale GPU resources and double fixes when material keys
/// changed. This utility owns common scene resource discovery; backend-specific resources
/// such as Avalonia control-plane textures remain outside Core.
/// </summary>
public static class SceneRenderResourceCollector3D
{
    public static void CollectLiveMeshesAndTextures(
        Scene3D scene,
        SceneFrameSnapshot3D snapshot,
        ISet<string> liveMeshes,
        ISet<string> liveTextures,
        System.Func<ModelPart3D?, bool>? requiresCpuSkinFallback = null)
    {
        foreach (var obj in snapshot.Renderables)
        {
            if (obj is ParticleSystem3D particleSystem)
            {
                if (particleSystem.AliveCount > 0)
                {
                    liveMeshes.Add(ParticleSystem3D.GetStaticRenderMesh(particleSystem.Settings.RenderMode).ResourceKey);
                }
            }
            else
            {
                var skinnedPart = obj as ModelPart3D;
                var useCpuSkinFallback = requiresCpuSkinFallback?.Invoke(skinnedPart) == true;
                liveMeshes.Add(useCpuSkinFallback ? skinnedPart!.GetCpuSkinnedFallbackMesh().ResourceKey : obj.GetMesh().ResourceKey);
            }

            AddLiveMaterialTextures(liveTextures, obj.Material);
        }

        foreach (var layer in snapshot.HighScaleLayers)
        {
            AddHighScaleLodMeshes(liveMeshes, layer, HighScaleLodLevel3D.Detailed);
            AddHighScaleLodMeshes(liveMeshes, layer, HighScaleLodLevel3D.Simplified);
            AddHighScaleLodMeshes(liveMeshes, layer, HighScaleLodLevel3D.Proxy);
            AddHighScaleLodMeshes(liveMeshes, layer, HighScaleLodLevel3D.Billboard);
        }

        AddEnvironmentTextures(scene, liveTextures);
    }

    public static void AddLiveMaterialTextures(ISet<string> liveTextures, Material3D material)
        => AddLiveMaterialTextures(liveTextures, MaterialBinding3D.FromMaterial(material));

    public static void AddLiveMaterialTextures(ISet<string> liveTextures, MaterialBinding3D material)
    {
        AddLiveTexture(liveTextures, material.BaseColorTextureKey, material.HasBaseColorTexture);
        AddLiveTexture(liveTextures, material.NormalMapTextureKey, material.HasNormalMap);
        AddLiveTexture(liveTextures, material.MetallicRoughnessTextureKey, material.HasMetallicRoughnessTexture);
        AddLiveTexture(liveTextures, material.EmissiveTextureKey, material.HasEmissiveTexture);
    }

    public static void AddEnvironmentTextures(Scene3D scene, ISet<string> liveTextures)
    {
        var skybox = scene.Environment.Skybox;
        AddLiveTexture(liveTextures, skybox.EquirectangularTextureKey, skybox.HasEquirectangularTexture);
        if (!skybox.HasCubemapTextures) return;

        for (var i = 0; i < skybox.CubemapTextureKeys.Count; i++)
        {
            AddLiveTexture(liveTextures, skybox.CubemapTextureKeys[i], true);
        }
    }

    public static void AddHighScaleLodMeshes(ISet<string> liveMeshes, HighScaleInstanceLayer3D layer, HighScaleLodLevel3D lod)
    {
        foreach (var part in layer.Template.ResolveParts(lod))
        {
            liveMeshes.Add(part.Mesh.ResourceKey);
        }
    }

    private static void AddLiveTexture(ISet<string> liveTextures, string? key, bool active)
    {
        if (active && !string.IsNullOrWhiteSpace(key)) liveTextures.Add(key);
    }
}
