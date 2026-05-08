using System;
using System.Numerics;

namespace ThreeDEngine.Core.Assets.Models;

public static class ModelAnimationEvaluator3D
{
    public static ModelAnimationPose3D Evaluate(ModelAsset3D asset, AnimationClip3D? clip, float timeSeconds)
    {
        if (asset.Nodes.Count == 0) return ModelAnimationPose3D.Empty;

        var local = new Matrix4x4[asset.Nodes.Count];
        var translations = new Vector3[asset.Nodes.Count];
        var rotations = new Quaternion[asset.Nodes.Count];
        var scales = new Vector3[asset.Nodes.Count];
        var hasTrs = new bool[asset.Nodes.Count];

        for (var i = 0; i < asset.Nodes.Count; i++)
        {
            local[i] = asset.Nodes[i].LocalTransform;
            if (Matrix4x4.Decompose(local[i], out var scale, out var rotation, out var translation))
            {
                translations[i] = translation;
                rotations[i] = rotation;
                scales[i] = scale;
                hasTrs[i] = true;
            }
            else
            {
                translations[i] = Vector3.Zero;
                rotations[i] = Quaternion.Identity;
                scales[i] = Vector3.One;
            }
        }

        if (clip is not null && clip.IsValid)
        {
            var t = clip.Duration > 0f ? global::System.Math.Clamp(timeSeconds, 0f, clip.Duration) : timeSeconds;
            foreach (var channel in clip.Channels)
            {
                var nodeIndex = channel.TargetNodeIndex;
                if (nodeIndex < 0 || nodeIndex >= local.Length || !hasTrs[nodeIndex]) continue;
                switch (channel.Path)
                {
                    case AnimationPath3D.Translation:
                    {
                        var v = channel.Sampler.Evaluate(t, new Vector4(translations[nodeIndex], 0f));
                        translations[nodeIndex] = new Vector3(v.X, v.Y, v.Z);
                        break;
                    }
                    case AnimationPath3D.Scale:
                    {
                        var v = channel.Sampler.Evaluate(t, new Vector4(scales[nodeIndex], 0f));
                        scales[nodeIndex] = new Vector3(v.X, v.Y, v.Z);
                        break;
                    }
                    case AnimationPath3D.Rotation:
                    {
                        rotations[nodeIndex] = channel.Sampler.EvaluateQuaternion(t, rotations[nodeIndex]);
                        break;
                    }
                }
            }

            for (var i = 0; i < local.Length; i++)
            {
                if (!hasTrs[i]) continue;
                local[i] = Matrix4x4.CreateScale(scales[i]) * Matrix4x4.CreateFromQuaternion(rotations[i]) * Matrix4x4.CreateTranslation(translations[i]);
            }
        }

        var world = new Matrix4x4[asset.Nodes.Count];
        var visited = new bool[asset.Nodes.Count];
        var visiting = new bool[asset.Nodes.Count];
        for (var i = 0; i < asset.Nodes.Count; i++) Visit(i);

        var skinMatrices = new Matrix4x4[asset.Skins.Count][];
        for (var s = 0; s < asset.Skins.Count; s++)
        {
            var skin = asset.Skins[s];
            var matrices = new Matrix4x4[skin.BoneCount];
            for (var b = 0; b < skin.BoneCount; b++)
            {
                var bone = skin.Bones[b];
                var jointWorld = bone.NodeIndex >= 0 && bone.NodeIndex < world.Length ? world[bone.NodeIndex] : Matrix4x4.Identity;
                // System.Numerics is used consistently as row-vector math in this engine
                // (local * parent for hierarchical transforms). The glTF column-vector
                // skinning formula is jointGlobal * inverseBind. The row-vector equivalent
                // is inverseBind * jointGlobal; mesh-node inverse is applied in ModelPart3D
                // so the resulting CPU-skinned vertices remain in the part's local space.
                matrices[b] = bone.InverseBindMatrix * jointWorld;
            }
            skinMatrices[s] = matrices;
        }

        return new ModelAnimationPose3D(world, skinMatrices);

        void Visit(int index)
        {
            if (index < 0 || index >= asset.Nodes.Count || visited[index]) return;
            if (visiting[index])
            {
                world[index] = local[index];
                visiting[index] = false;
                visited[index] = true;
                return;
            }

            visiting[index] = true;
            var node = asset.Nodes[index];
            if (node.ParentIndex.HasValue && node.ParentIndex.Value >= 0 && node.ParentIndex.Value < asset.Nodes.Count && node.ParentIndex.Value != index)
            {
                Visit(node.ParentIndex.Value);
                world[index] = local[index] * world[node.ParentIndex.Value];
            }
            else
            {
                world[index] = local[index];
            }
            visiting[index] = false;
            visited[index] = true;
        }
    }
}
