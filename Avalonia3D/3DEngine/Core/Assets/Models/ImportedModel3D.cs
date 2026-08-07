using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Interaction;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Assets.Models;

public sealed class ImportedModel3D : CompositeObject3D
{
    private readonly List<ModelPart3D> _modelParts = new();
    private readonly ReadOnlyCollection<ModelPart3D> _modelPartsView;
    private readonly List<ModelEventBinding3D> _eventBindings = new();
    private readonly ReadOnlyCollection<ModelEventBinding3D> _eventBindingsView;
    private readonly Dictionary<string, Material3D> _nodeMaterialOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Material3D> _partMaterialOverrides = new(StringComparer.Ordinal);

    public ImportedModel3D(ModelAsset3D asset)
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _modelPartsView = _modelParts.AsReadOnly();
        _eventBindingsView = _eventBindings.AsReadOnly();
        Name = CreateDisplayName(asset.SourcePath, asset.AssetId);
        Animation = new ModelAnimationController3D(this);
    }


    private static string CreateDisplayName(string sourcePath, string fallback)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return fallback;
        try
        {
            if (Uri.TryCreate(sourcePath, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                var segment = uri.Segments.Length > 0 ? uri.Segments[^1] : sourcePath;
                var display = System.IO.Path.GetFileNameWithoutExtension(segment);
                return string.IsNullOrWhiteSpace(display) ? fallback : display;
            }

            var name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }
        catch
        {
            return fallback;
        }
    }

    public event EventHandler<ModelPointerEventArgs>? ModelClicked;
    public event EventHandler<ModelPointerEventArgs>? ModelPointerEntered;
    public event EventHandler<ModelPointerEventArgs>? ModelPointerExited;
    public event EventHandler<ModelPointerEventArgs>? ModelPointerMoved;
    public event EventHandler<ModelPointerEventArgs>? ModelPointerPressed;
    public event EventHandler<ModelPointerEventArgs>? ModelPointerReleased;

    public ModelAsset3D Asset { get; }
    public string SourcePath => Asset.SourcePath;
    public ModelImportDiagnostics Diagnostics => Asset.Diagnostics;
    public ModelAnimationController3D Animation { get; }
    public bool HasAnimations => Asset.Animations.Count > 0;
    public bool HasSkins => Asset.Skins.Count > 0;
    public IReadOnlyList<ModelEventBinding3D> EventBindings => _eventBindingsView;
    public IReadOnlyList<ModelPart3D> ModelParts
    {
        get
        {
            _ = Children;
            return _modelPartsView;
        }
    }

    public void SetNodeMaterial(string nodePathOrName, Material3D material)
    {
        using var mutation = EnterOwnedMutationScope();
        if (string.IsNullOrWhiteSpace(nodePathOrName)) throw new ArgumentException("Node path or name cannot be empty.", nameof(nodePathOrName));
        _nodeMaterialOverrides[nodePathOrName] = (material ?? throw new ArgumentNullException(nameof(material))).Clone();
        foreach (var part in ModelParts)
        {
            if (MatchesNode(part, nodePathOrName)) part.ApplyMaterial(_nodeMaterialOverrides[nodePathOrName]);
        }
    }

    public void SetPrimitiveMaterial(string modelElementPath, Material3D material)
    {
        using var mutation = EnterOwnedMutationScope();
        if (string.IsNullOrWhiteSpace(modelElementPath)) return;
        _partMaterialOverrides[modelElementPath] = (material ?? throw new ArgumentNullException(nameof(material))).Clone();
        foreach (var part in ModelParts)
        {
            if (StringComparer.Ordinal.Equals(part.ModelElementPath, modelElementPath)) part.ApplyMaterial(_partMaterialOverrides[modelElementPath]);
        }
    }

    public bool ClearMaterialOverride(string nodePathOrElementPath)
    {
        using var mutation = EnterOwnedMutationScope();
        var removed = _nodeMaterialOverrides.Remove(nodePathOrElementPath) | _partMaterialOverrides.Remove(nodePathOrElementPath);
        if (!removed) return false;
        foreach (var part in ModelParts) ApplyResolvedMaterial(part);
        return true;
    }

    public IDisposable BindModelEvent(ModelPointerEventKind eventKind, EventHandler<ModelPointerEventArgs> handler)
        => AddEventBinding(new ModelEventBinding3D(ModelEventTargetKind3D.Model, string.Empty, eventKind, handler));

    public IDisposable BindNodeEvent(string nodePathOrName, ModelPointerEventKind eventKind, EventHandler<ModelPointerEventArgs> handler)
        => AddEventBinding(new ModelEventBinding3D(ModelEventTargetKind3D.Node, nodePathOrName, eventKind, handler));

    public IDisposable BindPrimitiveEvent(string modelElementPath, ModelPointerEventKind eventKind, EventHandler<ModelPointerEventArgs> handler)
        => AddEventBinding(new ModelEventBinding3D(ModelEventTargetKind3D.Primitive, modelElementPath, eventKind, handler));

    public IDisposable BindTriangleEvent(string trianglePath, ModelPointerEventKind eventKind, EventHandler<ModelPointerEventArgs> handler)
        => AddEventBinding(new ModelEventBinding3D(ModelEventTargetKind3D.Triangle, trianglePath, eventKind, handler));

    public bool UnbindEvent(ModelEventBinding3D binding)
    {
        using var mutation = EnterOwnedMutationScope();
        return binding is not null && _eventBindings.Remove(binding);
    }

    public bool RaiseModelPointerEvent(ModelPointerEventKind eventKind, ModelHitResult3D hit, Vector2 viewportPosition, SceneMouseButton button)
    {
        if (!ReferenceEquals(hit.Model, this))
        {
            return false;
        }

        var args = new ModelPointerEventArgs(eventKind, hit, viewportPosition, button);
        RaiseBuiltInModelEvent(args);

        var invoked = false;
        var snapshot = _eventBindings.Count == 0 ? Array.Empty<ModelEventBinding3D>() : _eventBindings.ToArray();
        foreach (var binding in snapshot)
        {
            if (!binding.Matches(eventKind, hit))
            {
                continue;
            }

            binding.Handler(this, args);
            invoked = true;
        }

        return invoked;
    }

    public void AdvanceAnimation(float deltaSeconds) => Animation.Advance(deltaSeconds);

    internal SceneAccessLease3D EnterModelMutationScope() => EnterOwnedMutationScope();

    internal void NotifyAnimationPlaybackChanged()
        => RaiseChanged(SceneChangeKind.AnimationPose);

    internal void ApplyAnimationPose(ModelAnimationPose3D pose)
    {
        foreach (var part in ModelParts)
        {
            part.ApplyAnimationPose(pose);
        }
    }

    public ModelPart3D? FindModelPart(string nodePathOrName)
    {
        _ = Children;
        foreach (var part in _modelParts)
        {
            if (StringComparer.Ordinal.Equals(part.Node.Path, nodePathOrName) ||
                StringComparer.Ordinal.Equals(part.Node.Name, nodePathOrName) ||
                StringComparer.Ordinal.Equals(part.ModelElementPath, nodePathOrName))
            {
                return part;
            }
        }

        return null;
    }

    protected override void Build(CompositeBuilder3D builder)
    {
        _modelParts.Clear();
        foreach (var node in Asset.Nodes)
        {
            if (!node.MeshIndex.HasValue || node.MeshIndex.Value < 0 || node.MeshIndex.Value >= Asset.Meshes.Count)
            {
                continue;
            }

            var mesh = Asset.Meshes[node.MeshIndex.Value];
            for (var i = 0; i < mesh.Primitives.Count; i++)
            {
                var primitive = mesh.Primitives[i];
                var material = Asset.ResolveMaterial(primitive.MaterialIndex);
                var part = new ModelPart3D(this, node, mesh, primitive, material, i);
                ApplyResolvedMaterial(part);
                _modelParts.Add(part);
                builder.Add(MakeUniquePartName(node, mesh, i), part);
            }
        }
    }

    protected override Mesh3D BuildMesh() => Mesh3D.Empty;

    private IDisposable AddEventBinding(ModelEventBinding3D binding)
    {
        using var mutation = EnterOwnedMutationScope();
        _eventBindings.Add(binding);
        return new BindingSubscription(this, binding);
    }

    private void RaiseBuiltInModelEvent(ModelPointerEventArgs args)
    {
        switch (args.EventKind)
        {
            case ModelPointerEventKind.Clicked:
                ModelClicked?.Invoke(this, args);
                break;
            case ModelPointerEventKind.PointerEntered:
                ModelPointerEntered?.Invoke(this, args);
                break;
            case ModelPointerEventKind.PointerExited:
                ModelPointerExited?.Invoke(this, args);
                break;
            case ModelPointerEventKind.PointerMoved:
                ModelPointerMoved?.Invoke(this, args);
                break;
            case ModelPointerEventKind.PointerPressed:
                ModelPointerPressed?.Invoke(this, args);
                break;
            case ModelPointerEventKind.PointerReleased:
                ModelPointerReleased?.Invoke(this, args);
                break;
        }
    }

    private void ApplyResolvedMaterial(ModelPart3D part)
    {
        if (_partMaterialOverrides.TryGetValue(part.ModelElementPath, out var partMaterial))
        {
            part.ApplyMaterial(partMaterial);
            return;
        }

        foreach (var pair in _nodeMaterialOverrides)
        {
            if (MatchesNode(part, pair.Key))
            {
                part.ApplyMaterial(pair.Value);
                return;
            }
        }

        part.ApplyAssetMaterial();
    }

    private static bool MatchesNode(ModelPart3D part, string nodePathOrName)
        => StringComparer.Ordinal.Equals(part.Node.Path, nodePathOrName)
           || StringComparer.Ordinal.Equals(part.Node.Name, nodePathOrName)
           || StringComparer.Ordinal.Equals(part.ModelElementPath, nodePathOrName);

    private static string MakeUniquePartName(ModelNode3D node, MeshAsset3D mesh, int primitiveIndex)
        => $"{Sanitize(node.Path)}__mesh{mesh.Index}__prim{primitiveIndex}";

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "model_part";
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
        }

        return new string(chars);
    }

    private sealed class BindingSubscription : IDisposable
    {
        private ImportedModel3D? _owner;
        private readonly ModelEventBinding3D _binding;

        public BindingSubscription(ImportedModel3D owner, ModelEventBinding3D binding)
        {
            _owner = owner;
            _binding = binding;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null) return;
            owner.UnbindEvent(_binding);
            _owner = null;
        }
    }
}
