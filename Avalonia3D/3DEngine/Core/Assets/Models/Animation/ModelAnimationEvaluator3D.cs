using System;
using System.Numerics;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Assets.Models;

public static class ModelAnimationEvaluator3D
{
    public static ModelAnimationPose3D Evaluate(ModelAsset3D asset, AnimationClip3D? clip, float timeSeconds)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (asset.Nodes.Count == 0) return ModelAnimationPose3D.Empty;
        var runtime = new ModelAnimationEvaluatorRuntime3D(asset);
        return runtime.Evaluate(clip, timeSeconds);
    }
}

internal sealed class ModelAnimationEvaluatorRuntime3D
{
    private readonly ModelAsset3D _asset;
    private readonly Matrix4x4[] _baseLocal;
    private readonly Vector3[] _baseTranslations;
    private readonly Quaternion[] _baseRotations;
    private readonly Vector3[] _baseScales;
    private readonly bool[] _baseHasTrs;
    private readonly Matrix4x4[] _local;
    private readonly Vector3[] _translations;
    private readonly Quaternion[] _rotations;
    private readonly Vector3[] _scales;
    private readonly Matrix4x4[] _world;
    private readonly byte[] _visitState;
    private readonly Matrix4x4[][] _skinMatrices;
    private readonly ModelAnimationPose3D _pose;

    public ModelAnimationEvaluatorRuntime3D(ModelAsset3D asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        var nodeCount = asset.Nodes.Count;
        _baseLocal = new Matrix4x4[nodeCount];
        _baseTranslations = new Vector3[nodeCount];
        _baseRotations = new Quaternion[nodeCount];
        _baseScales = new Vector3[nodeCount];
        _baseHasTrs = new bool[nodeCount];
        _local = new Matrix4x4[nodeCount];
        _translations = new Vector3[nodeCount];
        _rotations = new Quaternion[nodeCount];
        _scales = new Vector3[nodeCount];
        _world = new Matrix4x4[nodeCount];
        _visitState = new byte[nodeCount];

        for (var i = 0; i < nodeCount; i++)
        {
            var local = asset.Nodes[i].LocalTransform;
            _baseLocal[i] = local;
            if (Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
            {
                _baseTranslations[i] = translation;
                _baseRotations[i] = rotation;
                _baseScales[i] = scale;
                _baseHasTrs[i] = true;
            }
            else
            {
                _baseTranslations[i] = Vector3.Zero;
                _baseRotations[i] = Quaternion.Identity;
                _baseScales[i] = Vector3.One;
                _baseHasTrs[i] = false;
            }
        }

        _skinMatrices = new Matrix4x4[asset.Skins.Count][];
        for (var s = 0; s < asset.Skins.Count; s++)
        {
            var boneCount = asset.Skins[s].BoneCount;
            _skinMatrices[s] = boneCount <= 0 ? Array.Empty<Matrix4x4>() : new Matrix4x4[boneCount];
        }

        _pose = new ModelAnimationPose3D(_world, _skinMatrices);
    }

    public ModelAnimationPose3D Evaluate(AnimationClip3D? clip, float timeSeconds)
    {
        Guard3D.Finite(timeSeconds, nameof(timeSeconds));
        var nodeCount = _asset.Nodes.Count;
        if (nodeCount == 0) return ModelAnimationPose3D.Empty;

        for (var i = 0; i < nodeCount; i++)
        {
            _local[i] = _baseLocal[i];
            _translations[i] = _baseTranslations[i];
            _rotations[i] = _baseRotations[i];
            _scales[i] = _baseScales[i];
            _world[i] = Matrix4x4.Identity;
            _visitState[i] = 0;
        }

        if (clip is not null && clip.IsValid)
        {
            var t = clip.Duration > 0f ? global::System.Math.Clamp(timeSeconds, 0f, clip.Duration) : timeSeconds;
            foreach (var channel in clip.Channels)
            {
                var nodeIndex = channel.TargetNodeIndex;
                if ((uint)nodeIndex >= (uint)nodeCount)
                    throw new InvalidOperationException($"Animation channel targets node index {nodeIndex}, which is outside the asset node catalog.");
                if (!_baseHasTrs[nodeIndex])
                    throw new InvalidOperationException($"Animation channel targets node '{_asset.Nodes[nodeIndex].Name}', whose local matrix cannot be decomposed into TRS components.");
                switch (channel.Path)
                {
                    case AnimationPath3D.Translation:
                    {
                        var v = channel.Sampler.Evaluate(t, new Vector4(_translations[nodeIndex], 0f));
                        _translations[nodeIndex] = new Vector3(v.X, v.Y, v.Z);
                        break;
                    }
                    case AnimationPath3D.Scale:
                    {
                        var v = channel.Sampler.Evaluate(t, new Vector4(_scales[nodeIndex], 0f));
                        _scales[nodeIndex] = new Vector3(v.X, v.Y, v.Z);
                        break;
                    }
                    case AnimationPath3D.Rotation:
                    {
                        _rotations[nodeIndex] = channel.Sampler.EvaluateQuaternion(t, _rotations[nodeIndex]);
                        break;
                    }
                }
            }

            for (var i = 0; i < nodeCount; i++)
            {
                if (!_baseHasTrs[i]) continue;
                _local[i] = Matrix4x4.CreateScale(_scales[i]) * Matrix4x4.CreateFromQuaternion(_rotations[i]) * Matrix4x4.CreateTranslation(_translations[i]);
            }
        }

        for (var i = 0; i < nodeCount; i++) Visit(i);

        for (var s = 0; s < _asset.Skins.Count; s++)
        {
            var skin = _asset.Skins[s];
            var matrices = _skinMatrices[s];
            for (var b = 0; b < skin.BoneCount && b < matrices.Length; b++)
            {
                var bone = skin.Bones[b];
                if ((uint)bone.NodeIndex >= (uint)_world.Length)
                    throw new InvalidOperationException($"Skin {skin.Index} bone {bone.Index} targets node index {bone.NodeIndex}, which is outside the evaluated pose.");
                var jointWorld = _world[bone.NodeIndex];
                // System.Numerics is used consistently as row-vector math in this engine
                // (local * parent for hierarchical transforms). The glTF column-vector
                // skinning formula is jointGlobal * inverseBind. The row-vector equivalent
                // is inverseBind * jointGlobal; mesh-node inverse is applied in ModelPart3D
                // so the resulting CPU-skinned vertices remain in the part's local space.
                matrices[b] = bone.InverseBindMatrix * jointWorld;
            }
        }

        _pose.Reset(_world, _skinMatrices);
        return _pose;

        void Visit(int index)
        {
            if ((uint)index >= (uint)nodeCount) return;
            if (_visitState[index] == 2) return;
            if (_visitState[index] == 1)
                throw new InvalidOperationException($"Model node hierarchy contains a cycle at node {index} ('{_asset.Nodes[index].Name}').");

            _visitState[index] = 1;
            var node = _asset.Nodes[index];
            if (node.ParentIndex.HasValue)
            {
                var parentIndex = node.ParentIndex.Value;
                if ((uint)parentIndex >= (uint)nodeCount || parentIndex == index)
                    throw new InvalidOperationException($"Model node {index} ('{node.Name}') has invalid parent index {parentIndex}.");
                Visit(parentIndex);
                _world[index] = _local[index] * _world[parentIndex];
            }
            else
            {
                _world[index] = _local[index];
            }
            _visitState[index] = 2;
        }
    }
}
