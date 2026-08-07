using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering.Rhi;

namespace ThreeDEngine.Core.Rendering.GpuDriven;

internal readonly record struct GpuDrivenMeshGroup3D(
    uint MeshIndex,
    RhiResourceHandle3D VertexBuffer,
    RhiResourceHandle3D IndexBuffer,
    int VertexStride,
    int MeshletCount,
    int ObjectCount,
    long IndirectByteOffset,
    int IndirectCommandCapacity);

internal readonly record struct GpuDrivenSceneUpload3D(
    RhiResourceHandle3D FrameConstants,
    RhiResourceHandle3D Objects,
    RhiResourceHandle3D Meshes,
    RhiResourceHandle3D Meshlets,
    RhiResourceHandle3D Materials,
    RhiResourceHandle3D SkinMatrices,
    RhiResourceHandle3D DirectionalLights,
    RhiResourceHandle3D PointLights,
    RhiResourceHandle3D SpotLights,
    RhiResourceHandle3D VisibleMeshlets,
    RhiResourceHandle3D IndirectCommands,
    RhiResourceHandle3D IndirectCounters,
    IReadOnlyList<GpuDrivenMeshGroup3D> MeshGroups,
    int ObjectCount,
    int MeshCount,
    int MeshletCount,
    int MaterialCount,
    int SkinMatrixCount,
    int IndirectCommandCapacity,
    int UploadedBytes);

/// <summary>
/// Persistent GPU scene database. Static geometry is uploaded once per geometry version while
/// compact object/material/light records are streamed each frame. Visibility and LOD results are
/// never produced on the CPU; only source records and fixed command capacities are prepared here.
/// </summary>
internal sealed class GpuDrivenSceneDatabase3D : IDisposable
{
    private const int IndirectCommandStride = 20;
    private readonly Dictionary<string, MeshAllocation> _meshAllocations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _meshIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _materialIndices = new(StringComparer.Ordinal);
    private readonly List<GpuSceneObjectRecord3D> _objects = new(1024);
    private readonly List<GpuMeshRecord3D> _meshes = new(128);
    private readonly List<GpuMeshletRecord3D> _meshlets = new(2048);
    private readonly List<GpuMaterialRecord3D> _materials = new(128);
    private readonly List<Matrix4x4> _skinMatrices = new(256);
    private readonly List<MeshGroupBuild> _meshGroups = new(128);
    private readonly List<GpuDirectionalLightRecord3D> _directionalLights = new(8);
    private readonly List<GpuPointLightRecord3D> _pointLights = new(64);
    private readonly List<GpuSpotLightRecord3D> _spotLights = new(64);
    private readonly string _owner = "gpu-driven-scene";
    private uint _deviceGeneration;
    private RhiDevice3D? _device;
    private RhiResourceHandle3D _frameConstants;
    private RhiResourceHandle3D _objectsBuffer;
    private RhiResourceHandle3D _meshesBuffer;
    private RhiResourceHandle3D _meshletsBuffer;
    private RhiResourceHandle3D _materialsBuffer;
    private RhiResourceHandle3D _skinMatricesBuffer;
    private RhiResourceHandle3D _directionalLightsBuffer;
    private RhiResourceHandle3D _pointLightsBuffer;
    private RhiResourceHandle3D _spotLightsBuffer;
    private RhiResourceHandle3D _visibleMeshletsBuffer;
    private RhiResourceHandle3D _indirectCommandsBuffer;
    private RhiResourceHandle3D _indirectCountersBuffer;
    private int _frameConstantsCapacity;
    private int _objectsCapacity;
    private int _meshesCapacity;
    private int _meshletsCapacity;
    private int _materialsCapacity;
    private int _skinMatricesCapacity;
    private int _directionalLightsCapacity;
    private int _pointLightsCapacity;
    private int _spotLightsCapacity;
    private int _visibleMeshletsCapacity;
    private int _indirectCommandsCapacity;
    private int _indirectCountersCapacity;
    private bool _disposed;

    public GpuDrivenSceneUpload3D Prepare(
        SceneRenderPlan3D plan,
        RhiDevice3D device,
        RhiCommandEncoder3D encoder,
        GpuDrivenRenderSettings3D settings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        device.Capabilities.RequireProfile(RhiCapabilityProfile3D.GpuDriven, "GPU scene preparation");
        ResetForDevice(device);
        ResetFrameState();

        AddOrdinary(plan);
        AddHighScale(plan);
        AddLights(plan);
        ValidateCapacities(settings);
        BuildMeshGroups(settings);

        var ring = device.CurrentUploadRing ?? throw new InvalidOperationException("GPU scene upload requires an open RHI frame.");
        var uploadedBytes = UploadPendingMeshes(encoder, ring);
        var frameConstants = BuildFrameConstants(plan, settings);
        uploadedBytes += UploadStruct(device, encoder, ring, ref _frameConstants, ref _frameConstantsCapacity,
            "frame-constants", frameConstants, RhiBufferUsage3D.Uniform | RhiBufferUsage3D.CopyDestination);
        uploadedBytes += UploadList(device, encoder, ring, ref _objectsBuffer, ref _objectsCapacity,
            "objects", _objects, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);
        uploadedBytes += UploadList(device, encoder, ring, ref _meshesBuffer, ref _meshesCapacity,
            "meshes", _meshes, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);
        uploadedBytes += UploadList(device, encoder, ring, ref _meshletsBuffer, ref _meshletsCapacity,
            "meshlets", _meshlets, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);
        uploadedBytes += UploadList(device, encoder, ring, ref _materialsBuffer, ref _materialsCapacity,
            "materials", _materials, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);
        uploadedBytes += UploadList(device, encoder, ring, ref _skinMatricesBuffer, ref _skinMatricesCapacity,
            "skin-matrices", _skinMatrices, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);
        uploadedBytes += UploadList(device, encoder, ring, ref _directionalLightsBuffer, ref _directionalLightsCapacity,
            "directional-lights", _directionalLights, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);
        uploadedBytes += UploadList(device, encoder, ring, ref _pointLightsBuffer, ref _pointLightsCapacity,
            "point-lights", _pointLights, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);
        uploadedBytes += UploadList(device, encoder, ring, ref _spotLightsBuffer, ref _spotLightsCapacity,
            "spot-lights", _spotLights, RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);

        var commandCapacity = 0;
        foreach (var group in _meshGroups) commandCapacity = checked(commandCapacity + group.CommandCapacity);
        EnsureBuffer(device, ref _visibleMeshletsBuffer, ref _visibleMeshletsCapacity, "visible-meshlets",
            checked(commandCapacity * sizeof(uint)), RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination);
        EnsureBuffer(device, ref _indirectCommandsBuffer, ref _indirectCommandsCapacity, "indirect-commands",
            checked(commandCapacity * IndirectCommandStride),
            RhiBufferUsage3D.Storage | RhiBufferUsage3D.Indirect | RhiBufferUsage3D.CopyDestination);
        EnsureBuffer(device, ref _indirectCountersBuffer, ref _indirectCountersCapacity, "indirect-counters",
            checked(global::System.Math.Max(1, _meshGroups.Count) * sizeof(uint)),
            RhiBufferUsage3D.Storage | RhiBufferUsage3D.Indirect | RhiBufferUsage3D.CopyDestination);
        encoder.ClearBuffer(_indirectCommandsBuffer, 0, checked((long)commandCapacity * IndirectCommandStride));
        encoder.ClearBuffer(_indirectCountersBuffer, 0, checked((long)global::System.Math.Max(1, _meshGroups.Count) * sizeof(uint)));

        var groups = new GpuDrivenMeshGroup3D[_meshGroups.Count];
        var commandOffset = 0;
        for (var i = 0; i < _meshGroups.Count; i++)
        {
            var build = _meshGroups[i];
            groups[i] = new GpuDrivenMeshGroup3D(
                checked((uint)build.MeshIndex),
                build.Allocation.VertexBuffer,
                build.Allocation.MeshletIndexBuffer,
                build.Allocation.VertexStride,
                build.Allocation.MeshletCount,
                build.ObjectCount,
                checked((long)commandOffset * IndirectCommandStride),
                build.CommandCapacity);
            commandOffset = checked(commandOffset + build.CommandCapacity);
        }

        return new GpuDrivenSceneUpload3D(
            _frameConstants,
            _objectsBuffer,
            _meshesBuffer,
            _meshletsBuffer,
            _materialsBuffer,
            _skinMatricesBuffer,
            _directionalLightsBuffer,
            _pointLightsBuffer,
            _spotLightsBuffer,
            _visibleMeshletsBuffer,
            _indirectCommandsBuffer,
            _indirectCountersBuffer,
            groups,
            _objects.Count,
            _meshes.Count,
            _meshlets.Count,
            _materials.Count,
            _skinMatrices.Count,
            commandCapacity,
            uploadedBytes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_device is not null && !_device.IsDisposed)
        {
            foreach (var allocation in _meshAllocations.Values) allocation.Release(_device);
            Release(_frameConstants);
            Release(_objectsBuffer);
            Release(_meshesBuffer);
            Release(_meshletsBuffer);
            Release(_materialsBuffer);
            Release(_skinMatricesBuffer);
            Release(_directionalLightsBuffer);
            Release(_pointLightsBuffer);
            Release(_spotLightsBuffer);
            Release(_visibleMeshletsBuffer);
            Release(_indirectCommandsBuffer);
            Release(_indirectCountersBuffer);
        }
        _meshAllocations.Clear();

        void Release(RhiResourceHandle3D handle)
        {
            if (handle.IsValid) _device.Resources.Release(handle);
        }
    }

    private void AddOrdinary(SceneRenderPlan3D plan)
    {
        foreach (var batch in plan.OrdinaryBatches)
            foreach (var item in batch.Items) AddItem(item, transparent: false, highScale: false);
        foreach (var transparent in plan.TransparentOrdinaryItems)
            AddItem(transparent.Item, transparent: true, highScale: false);
        foreach (var batch in plan.TransparentOrdinaryBatches)
            foreach (var item in batch.Items) AddItem(item, transparent: true, highScale: false);
    }

    private void AddHighScale(SceneRenderPlan3D plan)
    {
        foreach (var layer in plan.HighScaleLayers)
        {
            var instances = layer.Instances.Records;
            var parts = layer.Template.Parts;
            for (var instanceIndex = 0; instanceIndex < instances.Length; instanceIndex++)
            {
                var instance = instances[instanceIndex];
                if ((instance.Flags & InstanceFlags3D.Visible) == 0) continue;
                for (var partIndex = 0; partIndex < parts.Count; partIndex++)
                {
                    var part = parts[partIndex];
                    var color = layer.ResolveColor(part, instance);
                    var materialKey = $"high-scale:{layer.Template.Id}:{instance.MaterialVariantId}:{part.MaterialSlot}:{color.GetHashCode()}";
                    if (color.A < 0.999f)
                        throw new InvalidOperationException("GPU-driven high-scale transparency requires the GPU sorting path, which is not available in this source drop. No unsorted or CPU fallback is permitted.");
                    var materialIndex = GetOrAddMaterial(materialKey, BuildHighScaleMaterial(color, part.LightingMode));
                    var meshIndex = GetOrAddMesh(part.Mesh);
                    var model = part.LocalTransform * instance.Transform;
                    AddObject(part.Mesh, model, meshIndex, materialIndex, transparent: color.A < 0.999f, usesSkinning: false, highScale: true, skinPaletteOffset: uint.MaxValue);
                }
            }
        }
    }

    private void AddItem(OrdinaryRenderItem3D item, bool transparent, bool highScale)
    {
        if (transparent)
            throw new InvalidOperationException("GPU-driven ordinary transparency requires GPU depth sorting and a transparent pipeline, which are not available in this source drop. No unsorted or CPU fallback is permitted.");
        var meshIndex = GetOrAddMesh(item.Mesh);
        var materialIndex = GetOrAddMaterial(item.Material.Key, BuildMaterial(item.Material));
        var skinPaletteOffset = item.UsesGpuSkinning ? AddSkinPalette(item) : uint.MaxValue;
        AddObject(item.Mesh, item.Model, meshIndex, materialIndex, transparent, item.UsesGpuSkinning, highScale, skinPaletteOffset);
    }

    private void AddObject(Mesh3D mesh, Matrix4x4 model, int meshIndex, int materialIndex, bool transparent, bool usesSkinning, bool highScale, uint skinPaletteOffset)
    {
        var bounds = mesh.LocalBounds;
        var center = bounds.IsValid ? Vector3.Transform(bounds.Center, model) : new Vector3(model.M41, model.M42, model.M43);
        var scaleX = new Vector3(model.M11, model.M12, model.M13).Length();
        var scaleY = new Vector3(model.M21, model.M22, model.M23).Length();
        var scaleZ = new Vector3(model.M31, model.M32, model.M33).Length();
        var radius = mesh.RenderGeometry.BoundingRadius * global::System.MathF.Max(scaleX, global::System.MathF.Max(scaleY, scaleZ));
        uint flags = 1;
        if (transparent) flags |= 1u << 1;
        if (usesSkinning) flags |= 1u << 2;
        if (highScale) flags |= 1u << 3;
        _objects.Add(new GpuSceneObjectRecord3D
        {
            Model = model,
            BoundingSphere = new Vector4(center, radius),
            MeshIndex = checked((uint)meshIndex),
            MaterialIndex = checked((uint)materialIndex),
            Flags = flags,
            SkinPaletteOffset = skinPaletteOffset
        });
        _meshGroups[meshIndex].ObjectCount++;
    }

    private uint AddSkinPalette(OrdinaryRenderItem3D item)
    {
        var part = item.SkinnedPart ?? throw new InvalidOperationException("GPU-skinned render item does not expose its model part.");
        var palette = part.CurrentGpuSkinMatricesInternal;
        if (palette.Length == 0)
            throw new InvalidOperationException($"GPU-skinned render item '{part.ModelElementPath}' has no current skin palette.");
        var offset = checked((uint)_skinMatrices.Count);
        for (var i = 0; i < palette.Length; i++) _skinMatrices.Add(palette[i]);
        return offset;
    }

    private static byte[] BuildCanonicalVertices(RenderGeometry3D geometry)
    {
        var count = geometry.VertexCount;
        if (count == 0) return Array.Empty<byte>();
        var records = new GpuDrivenVertex3D[count];
        var normals = geometry.Normals;
        var tangents = geometry.HasTangents ? geometry.Tangents : GeometryBuffer3D<Vector4>.Empty;
        for (var i = 0; i < count; i++)
        {
            var hasTexCoord = geometry.HasTexCoords0;
            var hasColor = geometry.HasColors0;
            var hasTangent = geometry.HasTangents;
            var hasMaterialSlot = geometry.HasMaterialSlots;
            var hasSkin = geometry.HasSkinWeights;
            records[i] = new GpuDrivenVertex3D
            {
                Position = geometry.Positions[i],
                Normal = normals[i],
                TexCoord = hasTexCoord ? geometry.TexCoords0[i] : Vector2.Zero,
                Tangent = hasTangent ? tangents[i] : new Vector4(1f, 0f, 0f, 1f),
                Color = hasColor ? new Vector4(geometry.Colors0[i].R, geometry.Colors0[i].G, geometry.Colors0[i].B, geometry.Colors0[i].A) : Vector4.One,
                MaterialSlot = hasMaterialSlot ? geometry.MaterialSlots[i] : 0f,
                BoneIndices = hasSkin ? geometry.BoneIndices0[i] : Vector4.Zero,
                BoneWeights = hasSkin ? geometry.BoneWeights0[i] : new Vector4(1f, 0f, 0f, 0f)
            };
        }
        return MemoryMarshal.AsBytes(records.AsSpan()).ToArray();
    }

    private int GetOrAddMesh(Mesh3D mesh)
    {
        var geometry = mesh.RenderGeometry;
        if (_meshIndices.TryGetValue(geometry.ResourceKey, out var existing)) return existing;
        var device = _device ?? throw new InvalidOperationException("GPU scene database has no active device.");
        var allocation = EnsureMeshAllocation(mesh, device);
        var meshIndex = _meshes.Count;
        _meshIndices.Add(geometry.ResourceKey, meshIndex);
        var meshletOffset = _meshlets.Count;
        var meshlets = geometry.GetMeshlets();
        for (var i = 0; i < meshlets.Meshlets.Length; i++)
        {
            var meshlet = meshlets.Meshlets[i];
            var center = meshlet.Bounds.IsValid ? meshlet.Bounds.Center : Vector3.Zero;
            var radius = meshlet.Bounds.IsValid ? Vector3.Distance(meshlet.Bounds.Min, meshlet.Bounds.Max) * 0.5f : 0f;
            _meshlets.Add(new GpuMeshletRecord3D
            {
                BoundingSphere = new Vector4(center, radius),
                NormalCone = new Vector4(meshlet.NormalConeAxis, meshlet.NormalConeCutoff),
                VertexOffset = checked((uint)meshlet.VertexOffset),
                VertexCount = checked((uint)meshlet.VertexCount),
                TriangleOffset = checked((uint)meshlet.TriangleOffset),
                TriangleCount = checked((uint)meshlet.TriangleCount),
                MeshIndex = checked((uint)meshIndex)
            });
        }
        _meshes.Add(new GpuMeshRecord3D
        {
            VertexCount = checked((uint)geometry.VertexCount),
            IndexCount = checked((uint)geometry.IndexCount),
            MeshletOffset = checked((uint)meshletOffset),
            MeshletCount = checked((uint)meshlets.Count),
            IndexElementSize = sizeof(uint),
            VertexStride = checked((uint)allocation.VertexStride)
        });
        _meshGroups.Add(new MeshGroupBuild(meshIndex, allocation));
        return meshIndex;
    }

    private MeshAllocation EnsureMeshAllocation(Mesh3D mesh, RhiDevice3D device)
    {
        var geometry = mesh.RenderGeometry;
        if (_meshAllocations.TryGetValue(geometry.ResourceKey, out var existing) &&
            existing.GeometryVersion == geometry.GeometryVersion &&
            existing.Generation == device.Resources.ContextGeneration)
        {
            return existing;
        }

        var vertexBytes = BuildCanonicalVertices(geometry);
        var vertexStride = Marshal.SizeOf<GpuDrivenVertex3D>();
        var meshlets = geometry.GetMeshlets();
        var expandedIndices = BuildExpandedMeshletIndices(meshlets);
        var keyPrefix = "gpu-scene:mesh:" + geometry.ResourceKey;
        var vertex = device.CreateBuffer(
            keyPrefix + ":vertices",
            new RhiBufferDescriptor3D(global::System.Math.Max(vertexStride, vertexBytes.LongLength), RhiBufferUsage3D.Vertex | RhiBufferUsage3D.CopyDestination, vertexStride),
            geometry.GeometryVersion,
            _owner);
        var index = device.CreateBuffer(
            keyPrefix + ":meshlet-indices",
            new RhiBufferDescriptor3D(global::System.Math.Max(16, expandedIndices.LongLength * sizeof(uint)), RhiBufferUsage3D.Index | RhiBufferUsage3D.CopyDestination, sizeof(uint)),
            geometry.GeometryVersion,
            _owner);
        var allocation = new MeshAllocation(
            geometry.GeometryVersion,
            device.Resources.ContextGeneration,
            vertex,
            index,
            vertexStride,
            meshlets.Count,
            vertexBytes,
            expandedIndices);
        _meshAllocations[geometry.ResourceKey] = allocation;
        return allocation;
    }

    private void BuildMeshGroups(GpuDrivenRenderSettings3D settings)
    {
        var total = 0;
        for (var i = 0; i < _meshGroups.Count; i++)
        {
            var group = _meshGroups[i];
            group.CommandCapacity = checked(group.ObjectCount * global::System.Math.Max(1, group.Allocation.MeshletCount));
            var mesh = _meshes[group.MeshIndex];
            mesh.Reserved0 = checked((uint)total);
            mesh.Reserved1 = checked((uint)i);
            _meshes[group.MeshIndex] = mesh;
            total = checked(total + group.CommandCapacity);
        }
        if (total > settings.MaximumIndirectCommands)
            throw new InvalidOperationException($"GPU-driven indirect command capacity exceeded: required={total}, maximum={settings.MaximumIndirectCommands}.");
    }

    private void AddLights(SceneRenderPlan3D plan)
    {
        foreach (var light in plan.Frame.Published.DirectionalLights.Span)
        {
            _directionalLights.Add(new GpuDirectionalLightRecord3D
            {
                DirectionIntensity = new Vector4(light.Direction, light.Intensity),
                ColorEnabled = new Vector4(light.Color.R, light.Color.G, light.Color.B, light.IsEnabled ? 1f : 0f)
            });
        }
        foreach (var light in plan.Frame.Published.PointLights.Span)
        {
            _pointLights.Add(new GpuPointLightRecord3D
            {
                PositionRange = new Vector4(light.Position, light.Range),
                ColorIntensity = new Vector4(light.Color.R, light.Color.G, light.Color.B, light.IsEnabled ? light.Intensity : 0f)
            });
        }
        foreach (var light in plan.Frame.Published.SpotLights.Span)
        {
            _spotLights.Add(new GpuSpotLightRecord3D
            {
                PositionRange = new Vector4(light.Position, light.Range),
                DirectionInnerCos = new Vector4(light.Direction, global::System.MathF.Cos(light.InnerConeDegrees * global::System.MathF.PI / 180f)),
                ColorIntensity = new Vector4(light.Color.R, light.Color.G, light.Color.B, light.Intensity),
                OuterCosEnabled = new Vector4(global::System.MathF.Cos(light.OuterConeDegrees * global::System.MathF.PI / 180f), light.IsEnabled ? 1f : 0f, 0f, 0f)
            });
        }
    }

    private GpuFrameConstants3D BuildFrameConstants(SceneRenderPlan3D plan, GpuDrivenRenderSettings3D settings)
    {
        var width = global::System.MathF.Max(1f, plan.Frame.Width);
        var height = global::System.MathF.Max(1f, plan.Frame.Height);
        return new GpuFrameConstants3D
        {
            View = plan.Frame.View,
            Projection = plan.Frame.Projection,
            ViewProjection = plan.Frame.ViewProjection,
            CameraPositionTime = new Vector4(plan.Frame.Published.CameraPosition, (float)plan.Frame.Published.SimulationTimeSeconds),
            ViewportAndInverse = new Vector4(width, height, 1f / width, 1f / height),
            Counts = new Vector4(_objects.Count, _meshes.Count, _meshlets.Count, _materials.Count),
            LightCounts = new Vector4(_directionalLights.Count, _pointLights.Count, _spotLights.Count, settings.MaximumLightsPerCluster),
            ClusterDimensions = new Vector4(settings.ClusterCountX, settings.ClusterCountY, settings.ClusterCountZ, settings.ClusterCount),
            FeatureFlags = new Vector4(
                settings.EnableOcclusionCulling ? 1f : 0f,
                settings.EnableMeshletConeCulling ? 1f : 0f,
                settings.EnableGpuParticles ? 1f : 0f,
                settings.EnableClusteredLighting ? 1f : 0f),
            Timing = new Vector4(1f / 60f, (float)plan.Frame.Published.SimulationTimeSeconds, (float)plan.Frame.Published.InterpolationAlpha, 0f)
        };
    }

    private int GetOrAddMaterial(string key, GpuMaterialRecord3D material)
    {
        if (_materialIndices.TryGetValue(key, out var existing)) return existing;
        var index = _materials.Count;
        _materialIndices.Add(key, index);
        _materials.Add(material);
        return index;
    }

    private static GpuMaterialRecord3D BuildMaterial(MaterialBinding3D material)
    {
        if (material.HasBaseColorTexture || material.HasNormalMap || material.HasMetallicRoughnessTexture || material.HasEmissiveTexture)
            throw new InvalidOperationException("GPU-driven textured materials require descriptor-indexed texture arrays, which are not available in this source drop. No texture omission or legacy fallback is permitted.");
        return new GpuMaterialRecord3D
        {
            BaseColor = ToVector(material.BaseColor),
            EmissiveMetallic = new Vector4(material.EmissiveColor.R, material.EmissiveColor.G, material.EmissiveColor.B, material.Metallic),
            SurfaceParameters = new Vector4(material.Roughness, material.AlphaCutoff, material.NormalMapStrength, material.DoubleSided ? 1f : 0f),
            TextureIndices = new Vector4(-1f, -1f, -1f, -1f)
        };
    }

    private static GpuMaterialRecord3D BuildHighScaleMaterial(ColorRgba color, LightingMode lighting)
        => new()
        {
            BaseColor = ToVector(color),
            EmissiveMetallic = Vector4.Zero,
            SurfaceParameters = new Vector4(0.8f, 0.5f, 0f, 0f),
            TextureIndices = new Vector4(-1f, -1f, -1f, (float)lighting)
        };

    private static Vector4 ToVector(ColorRgba color) => new(color.R, color.G, color.B, color.A);

    private void ValidateCapacities(GpuDrivenRenderSettings3D settings)
    {
        if (_objects.Count > settings.MaximumObjects) throw Capacity("objects", _objects.Count, settings.MaximumObjects);
        if (_meshes.Count > settings.MaximumMeshes) throw Capacity("meshes", _meshes.Count, settings.MaximumMeshes);
        if (_meshlets.Count > settings.MaximumMeshlets) throw Capacity("meshlets", _meshlets.Count, settings.MaximumMeshlets);
        if (_materials.Count > settings.MaximumMaterials) throw Capacity("materials", _materials.Count, settings.MaximumMaterials);
    }

    private static InvalidOperationException Capacity(string kind, int required, int maximum)
        => new($"GPU-driven {kind} capacity exceeded: required={required}, maximum={maximum}. No CPU fallback or silent truncation is permitted.");

    private void ResetForDevice(RhiDevice3D device)
    {
        var generation = device.Resources.ContextGeneration;
        if (ReferenceEquals(_device, device) && _deviceGeneration == generation) return;
        if (_device is not null && !_device.IsDisposed && _deviceGeneration == _device.Resources.ContextGeneration)
        {
            foreach (var allocation in _meshAllocations.Values) allocation.Release(_device);
            ReleaseFromPreviousDevice(_frameConstants);
            ReleaseFromPreviousDevice(_objectsBuffer);
            ReleaseFromPreviousDevice(_meshesBuffer);
            ReleaseFromPreviousDevice(_meshletsBuffer);
            ReleaseFromPreviousDevice(_materialsBuffer);
            ReleaseFromPreviousDevice(_skinMatricesBuffer);
            ReleaseFromPreviousDevice(_directionalLightsBuffer);
            ReleaseFromPreviousDevice(_pointLightsBuffer);
            ReleaseFromPreviousDevice(_spotLightsBuffer);
            ReleaseFromPreviousDevice(_visibleMeshletsBuffer);
            ReleaseFromPreviousDevice(_indirectCommandsBuffer);
            ReleaseFromPreviousDevice(_indirectCountersBuffer);
        }
        _device = device;
        _deviceGeneration = generation;
        _meshAllocations.Clear();
        _frameConstants = default;
        _objectsBuffer = default;
        _meshesBuffer = default;
        _meshletsBuffer = default;
        _materialsBuffer = default;
        _skinMatricesBuffer = default;
        _directionalLightsBuffer = default;
        _pointLightsBuffer = default;
        _spotLightsBuffer = default;
        _visibleMeshletsBuffer = default;
        _indirectCommandsBuffer = default;
        _indirectCountersBuffer = default;
        _frameConstantsCapacity = _objectsCapacity = _meshesCapacity = _meshletsCapacity = _materialsCapacity = _skinMatricesCapacity = 0;
        _directionalLightsCapacity = _pointLightsCapacity = _spotLightsCapacity = 0;
        _visibleMeshletsCapacity = _indirectCommandsCapacity = _indirectCountersCapacity = 0;

        void ReleaseFromPreviousDevice(RhiResourceHandle3D handle)
        {
            if (handle.IsValid && _device.Resources.Contains(handle)) _device.Resources.Release(handle);
        }
    }

    private void ResetFrameState()
    {
        _meshIndices.Clear();
        _materialIndices.Clear();
        _objects.Clear();
        _meshes.Clear();
        _meshlets.Clear();
        _materials.Clear();
        _skinMatrices.Clear();
        _meshGroups.Clear();
        _directionalLights.Clear();
        _pointLights.Clear();
        _spotLights.Clear();
    }

    private int UploadPendingMeshes(RhiCommandEncoder3D encoder, RhiUploadRing3D ring)
    {
        var uploaded = 0;
        for (var i = 0; i < _meshGroups.Count; i++)
        {
            var allocation = _meshGroups[i].Allocation;
            if (!allocation.UploadPending) continue;
            if (allocation.VertexBytes.Length > 0)
            {
                var vertexSlice = ring.Allocate(allocation.VertexBytes.Length, 16);
                allocation.VertexBytes.AsSpan().CopyTo(vertexSlice.Memory.Span);
                encoder.WriteBuffer(allocation.VertexBuffer, 0, vertexSlice.Memory);
                uploaded = checked(uploaded + allocation.VertexBytes.Length);
            }
            if (allocation.IndexValues.Length > 0)
            {
                var indexBytes = MemoryMarshal.AsBytes(allocation.IndexValues.AsSpan());
                var indexSlice = ring.Allocate(indexBytes.Length, 16);
                indexBytes.CopyTo(indexSlice.Memory.Span);
                encoder.WriteBuffer(allocation.MeshletIndexBuffer, 0, indexSlice.Memory);
                uploaded = checked(uploaded + indexBytes.Length);
            }
            allocation.UploadPending = false;
        }
        return uploaded;
    }

    private int UploadStruct<T>(
        RhiDevice3D device,
        RhiCommandEncoder3D encoder,
        RhiUploadRing3D ring,
        ref RhiResourceHandle3D handle,
        ref int capacity,
        string name,
        T value,
        RhiBufferUsage3D usage) where T : struct
    {
        var array = new[] { value };
        return UploadArray(device, encoder, ring, ref handle, ref capacity, name, array, usage);
    }

    private int UploadList<T>(
        RhiDevice3D device,
        RhiCommandEncoder3D encoder,
        RhiUploadRing3D ring,
        ref RhiResourceHandle3D handle,
        ref int capacity,
        string name,
        List<T> values,
        RhiBufferUsage3D usage) where T : struct
        => UploadArray(device, encoder, ring, ref handle, ref capacity, name, values.ToArray(), usage);

    private int UploadArray<T>(
        RhiDevice3D device,
        RhiCommandEncoder3D encoder,
        RhiUploadRing3D ring,
        ref RhiResourceHandle3D handle,
        ref int capacity,
        string name,
        T[] values,
        RhiBufferUsage3D usage) where T : struct
    {
        var byteCount = checked(global::System.Math.Max(16, Marshal.SizeOf<T>() * global::System.Math.Max(1, values.Length)));
        EnsureBuffer(device, ref handle, ref capacity, name, byteCount, usage);
        if (values.Length == 0) return 0;
        var bytes = MemoryMarshal.AsBytes(values.AsSpan());
        var slice = ring.Allocate(bytes.Length, 16);
        bytes.CopyTo(slice.Memory.Span);
        encoder.WriteBuffer(handle, 0, slice.Memory);
        return bytes.Length;
    }

    private void EnsureBuffer(
        RhiDevice3D device,
        ref RhiResourceHandle3D handle,
        ref int capacity,
        string name,
        int requiredBytes,
        RhiBufferUsage3D usage)
    {
        requiredBytes = global::System.Math.Max(16, requiredBytes);
        if (capacity < requiredBytes) capacity = GrowCapacity(capacity, requiredBytes);
        handle = device.CreateBuffer(
            "gpu-scene:" + name,
            new RhiBufferDescriptor3D(capacity, usage),
            capacity,
            _owner);
    }

    private static int GrowCapacity(int current, int required)
    {
        var capacity = current <= 0 ? 256 : current;
        while (capacity < required) capacity = checked(capacity * 2);
        return capacity;
    }

    private static uint[] BuildExpandedMeshletIndices(MeshletSet3D meshlets)
    {
        if (meshlets.Count == 0) return Array.Empty<uint>();
        var output = new uint[meshlets.LocalTriangleIndices.Length];
        for (var meshletIndex = 0; meshletIndex < meshlets.Meshlets.Length; meshletIndex++)
        {
            var meshlet = meshlets.Meshlets[meshletIndex];
            var sourceOffset = checked(meshlet.TriangleOffset * 3);
            var sourceCount = checked(meshlet.TriangleCount * 3);
            for (var i = 0; i < sourceCount; i++)
            {
                var localVertex = meshlets.LocalTriangleIndices[sourceOffset + i];
                output[sourceOffset + i] = checked((uint)meshlets.VertexIndices[meshlet.VertexOffset + localVertex]);
            }
        }
        return output;
    }

    private sealed class MeshAllocation
    {
        public MeshAllocation(
            long geometryVersion,
            uint generation,
            RhiResourceHandle3D vertexBuffer,
            RhiResourceHandle3D meshletIndexBuffer,
            int vertexStride,
            int meshletCount,
            byte[] vertexBytes,
            uint[] indexValues)
        {
            GeometryVersion = geometryVersion;
            Generation = generation;
            VertexBuffer = vertexBuffer;
            MeshletIndexBuffer = meshletIndexBuffer;
            VertexStride = vertexStride;
            MeshletCount = meshletCount;
            VertexBytes = vertexBytes;
            IndexValues = indexValues;
            UploadPending = true;
        }

        public long GeometryVersion { get; }
        public uint Generation { get; }
        public RhiResourceHandle3D VertexBuffer { get; }
        public RhiResourceHandle3D MeshletIndexBuffer { get; }
        public int VertexStride { get; }
        public int MeshletCount { get; }
        public byte[] VertexBytes { get; }
        public uint[] IndexValues { get; }
        public bool UploadPending { get; set; }

        public void Release(RhiDevice3D device)
        {
            if (device.Resources.Contains(VertexBuffer)) device.Resources.Release(VertexBuffer);
            if (device.Resources.Contains(MeshletIndexBuffer)) device.Resources.Release(MeshletIndexBuffer);
        }
    }

    private sealed class MeshGroupBuild
    {
        public MeshGroupBuild(int meshIndex, MeshAllocation allocation)
        {
            MeshIndex = meshIndex;
            Allocation = allocation;
        }

        public int MeshIndex { get; }
        public MeshAllocation Allocation { get; }
        public int ObjectCount { get; set; }
        public int CommandCapacity { get; set; }
    }
}
