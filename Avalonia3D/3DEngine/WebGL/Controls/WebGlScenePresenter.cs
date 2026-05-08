using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Avalonia.Rendering;
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Avalonia.WebGL.Rendering;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Controls;

public sealed class WebGlScenePresenter : Control, IScenePresenter, IPerformanceMetricsOverlayPresenter, ICenterCursorOverlayPresenter, IPointerLockPresenter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Dictionary<string, int> _textureVersions = new();
    private readonly Dictionary<string, int> _meshGeometryVersions = new();
    private Scene3D _scene = new();
    private int _hostId = -1;
    private bool _moduleReady;
    private bool _initializing;
    private bool _renderPending;
    private bool _invalidateScheduled;
    private bool _attached;
    private bool _disposed;
    private int _lastSweptUploadRegistryVersion = -1;
    private string? _performanceMetricsText;
    private bool _performanceMetricsVisible;
    private bool _centerCursorVisible;
    private readonly WebGlRetainedHighScaleRenderer _retainedHighScale = new();
    private readonly WebGlClientHighScaleRenderer _clientHighScale = new();

    public WebGlScenePresenter()
    {
        Focusable = false;
        ClipToBounds = true;
        //Background = Brushes.Transparent;
        LayoutUpdated += (_, _) =>
        {
            if (_moduleReady && _hostId >= 0)
            {
                UpdateHostRect();
            }
        };
    }

    public event EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;

    public BackendKind Kind => BackendKind.WebGlBrowser;
    public Control View => this;

    public Scene3D Scene
    {
        get => _scene;
        set
        {
            _scene = value ?? throw new ArgumentNullException(nameof(value));
            RequestRender();
        }
    }

    public void SetPerformanceMetricsOverlay(string? text, bool visible)
    {
        _performanceMetricsText = text;
        _performanceMetricsVisible = visible && !string.IsNullOrWhiteSpace(text);
        if (_moduleReady && _hostId >= 0)
        {
            WebGlInterop.UpdateMetrics(_hostId, _performanceMetricsText ?? string.Empty, _performanceMetricsVisible);
        }
    }

    public bool SupportsPointerLock => true;

    public bool IsPointerLockActive => _moduleReady && _hostId >= 0 && WebGlInterop.IsPointerLockActive(_hostId);

    public void SetCenterCursorOverlay(bool visible)
    {
        _centerCursorVisible = visible;
        if (_moduleReady && _hostId >= 0)
        {
            WebGlInterop.UpdateCenterCursor(_hostId, visible);
        }
    }

    public void RequestPointerLock()
    {
        if (_moduleReady && _hostId >= 0)
        {
            WebGlInterop.RequestPointerLock(_hostId);
        }
    }

    public void ExitPointerLock()
    {
        if (_moduleReady && _hostId >= 0)
        {
            WebGlInterop.ExitPointerLock(_hostId);
        }
    }

    public bool TryConsumePointerDelta(out Vector2 delta)
    {
        delta = Vector2.Zero;
        if (!_moduleReady || _hostId < 0)
        {
            return false;
        }

        var encoded = WebGlInterop.ConsumePointerDelta(_hostId);
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        var parts = encoded.Split(',');
        if (parts.Length < 2 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        delta = new Vector2(x, y);
        return delta.LengthSquared() > 0.000001f;
    }

    public void RequestRender()
    {
        if (_disposed)
        {
            return;
        }

        _renderPending = true;
        ScheduleInvalidateVisual();
    }

    private void ScheduleInvalidateVisual()
    {
        if (_disposed || _invalidateScheduled)
        {
            return;
        }

        _invalidateScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _invalidateScheduled = false;
            if (!_disposed)
            {
                InvalidateVisual();
            }
        }, DispatcherPriority.Render);
    }


    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _ = EnsureHostAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        DestroyHost();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!_attached || !_moduleReady || _hostId < 0)
        {
            return;
        }

        UpdateHostRect();

        if (_renderPending)
        {
            _renderPending = false;
            RenderToWebGl();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == IsVisibleProperty)
        {
            RequestRender();
        }
    }

    private async System.Threading.Tasks.Task EnsureHostAsync()
    {
        if (_disposed || !_attached || _initializing || _moduleReady)
        {
            return;
        }

        _initializing = true;
        try
        {
            await WebGlInterop.EnsureImportedAsync();
            if (_disposed || !_attached)
            {
                return;
            }

            _hostId = WebGlInterop.CreateHost();
            _moduleReady = true;
            WebGlInterop.UpdateMetrics(_hostId, _performanceMetricsText ?? string.Empty, _performanceMetricsVisible);
            WebGlInterop.UpdateCenterCursor(_hostId, _centerCursorVisible);
            RequestRender();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("3DEngine WebGL backend initialization failed: " + ex);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void ApplyPipelineStats(RenderStats stats)
    {
        var pipeline = RenderPipelinePlanner3D.Plan(Scene, BackendKind.WebGlBrowser);
        stats.RenderPipelineMode = (int)pipeline.ActiveMode;
        stats.DeferredRequested = pipeline.DeferredRequested;
        stats.DeferredActive = pipeline.DeferredActive;
        stats.GBufferActive = pipeline.GBufferActive;
        stats.GBufferTargetCount = pipeline.GBufferActive ? 4 : 0;
        stats.SsaoRequested = pipeline.SsaoRequested;
        stats.SsaoActive = pipeline.SsaoActive;
        stats.SsaoSampleCount = Scene.RenderPipeline.Ssao.SampleCount;
        stats.HdrRequested = pipeline.HdrRequested;
        stats.HdrActive = pipeline.HdrActive;
        stats.ToneMappingMode = (int)pipeline.ToneMappingMode;
        stats.ToneMappingActive = pipeline.ToneMappingActive;
        stats.ToneMappingExposure = Scene.RenderPipeline.ToneMapping.Exposure;
        stats.ToneMappingGamma = Scene.RenderPipeline.ToneMapping.Gamma;
        stats.RenderPassCount = pipeline.Passes.Count;
        stats.MotionVectorsRequested = pipeline.MotionVectorsRequested;
        stats.MotionVectorsActive = pipeline.MotionVectorsActive;
        stats.RenderPipelineReason = pipeline.Reason;
    }

    private void RenderToWebGl()
    {
        if (_hostId < 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var stats = new RenderStats
        {
            ObjectCount = Scene.Registry.AllObjects.Count,
            RenderableCount = Scene.Registry.Renderables.Count,
            PickableCount = Scene.Registry.Pickables.Count,
            ColliderCount = Scene.Registry.Colliders.Count,
            DynamicBodyCount = Scene.Registry.DynamicBodies.Count,
            StaticColliderCount = Scene.Registry.StaticColliders.Count,
            RegistryVersion = Scene.Registry.Version,
            MeshCacheCount = MeshCache3D.Shared.Count
        };
        ApplyAnimationStats(stats, Scene, gpuSkinningActive: false, fallbackReason: "WebGL static mesh fallback; GPU skinning stage not active");

        SweepUnusedUploadState();
        ApplyPipelineStats(stats);

        var uploadStart = Stopwatch.GetTimestamp();
        UploadDirtyMeshGeometry(stats);
        UploadDirtyControlTextures(stats);
        UploadDirtyMaterialTextures(stats);
        UploadDirtyEnvironmentTextures(stats);
        stats.UploadMilliseconds = GetElapsedMilliseconds(uploadStart);

        var packetStart = Stopwatch.GetTimestamp();
        var aspect = (float)(Bounds.Width / global::System.Math.Max(Bounds.Height, 1d));
        var viewProjection = Scene.Camera.GetViewMatrix() * Scene.Camera.GetProjectionMatrix(aspect);
        if (ShouldUseClientHighScaleRuntime())
        {
            var serializeStart = Stopwatch.GetTimestamp();
            _clientHighScale.RenderFrame(_hostId, Scene, (float)Bounds.Width, (float)Bounds.Height, viewProjection, stats);
            stats.SerializationMilliseconds = GetElapsedMilliseconds(serializeStart);
            stats.PacketBuildMilliseconds = GetElapsedMilliseconds(packetStart);
        }
        else
        {
            if (_clientHighScale.HasRuntimeState)
            {
                _clientHighScale.Reset(_hostId);
            }

            var retainedBatches = Scene.Performance.EnableRetainedInstanceBuffers
                ? _retainedHighScale.BuildAndUpload(_hostId, Scene, viewProjection, stats)
                : null;
            var packet = WebGlScenePacketBuilder.Build(Scene, (float)Bounds.Width, (float)Bounds.Height, stats, retainedBatches);
            stats.PacketBuildMilliseconds = GetElapsedMilliseconds(packetStart);

            var serializeStart = Stopwatch.GetTimestamp();
            var json = JsonSerializer.Serialize(packet, JsonOptions);
            stats.SerializationMilliseconds = GetElapsedMilliseconds(serializeStart);

            WebGlInterop.RenderScene(_hostId, json);
        }
        stats.BackendMilliseconds = GetElapsedMilliseconds(start);

        // In WebAssembly, user callbacks must not invalidate Avalonia visuals
        // while Control.Render is active. Defer FrameRendered so benchmark UI
        // updates and telemetry state changes run after the render pass.
        var frame = new SceneFrameRenderedEventArgs(Kind, stats.BackendMilliseconds, stats);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                FrameRendered?.Invoke(this, frame);
            }
        }, DispatcherPriority.Background);
    }

    private bool ShouldUseClientHighScaleRuntime()
    {
        if (!Scene.Performance.EnableRetainedInstanceBuffers || !Scene.Performance.EnableWebGlClientHighScaleRuntime)
        {
            return false;
        }

        var hasHighScale = false;
        foreach (var layer in EnumerateHighScaleLayers())
        {
            if (layer.IsVisible && layer.Instances.Count > 0)
            {
                hasHighScale = true;
                break;
            }
        }

        if (!hasHighScale)
        {
            return false;
        }

        // v57 client runtime owns only retained high-scale drawing. Mixed scenes keep the
        // legacy packet path until the non-high-scale/object/control-plane path is moved too.
        if (Scene.Registry.Renderables.Count != 0)
        {
            return false;
        }

        foreach (var obj in Scene.Registry.AllObjects)
        {
            if (obj is ControlPlane3D plane && plane.IsVisible && plane.Snapshot is not null)
            {
                return false;
            }
        }

        return true;
    }


    private static void ApplyAnimationStats(RenderStats stats, Scene3D scene, bool gpuSkinningActive, string fallbackReason)
    {
        var imported = 0;
        var skinned = 0;
        var animated = 0;
        var skinMatrices = 0;
        var skinnedPrimitives = 0;
        long skinPayloadBytes = 0;
        foreach (var obj in scene.Registry.AllObjects)
        {
            if (obj is not ImportedModel3D model) continue;
            imported++;
            if (model.HasSkins)
            {
                skinned++;
                foreach (var skin in model.Asset.Skins) skinMatrices += skin.BoneCount;
            }
            if (model.HasAnimations || model.Animation.CurrentClip is not null) animated++;
            foreach (var part in model.ModelParts)
            {
                if (!part.IsSkinned) continue;
                skinnedPrimitives++;
                skinPayloadBytes += part.Primitive.Positions.LongLength * sizeof(float) * 8L;
            }
        }

        stats.ImportedModelCount = imported;
        stats.SkinnedModelCount = skinned;
        stats.AnimatedModelCount = animated;
        stats.SkinMatrixCount = skinMatrices;
        stats.SkinnedPrimitiveCount = skinnedPrimitives;
        stats.SkinningVertexPayloadBytes = skinPayloadBytes;
        stats.GpuSkinningRequested = skinned > 0;
        stats.GpuSkinningActive = gpuSkinningActive;
        stats.SkinningFallbackReason = gpuSkinningActive || skinned == 0 ? string.Empty : fallbackReason;
    }

    private void UploadDirtyMeshGeometry(RenderStats stats)
    {
        foreach (var obj in Scene.Registry.Renderables)
        {
            UploadMeshIfNeeded(obj.GetMesh(), stats);
        }

        foreach (var layer in EnumerateHighScaleLayers())
        {
            foreach (var lod in new[] { HighScaleLodLevel3D.Detailed, HighScaleLodLevel3D.Simplified, HighScaleLodLevel3D.Proxy, HighScaleLodLevel3D.Billboard })
            {
                foreach (var part in layer.Template.ResolveParts(lod))
                {
                    UploadMeshIfNeeded(part.Mesh, stats);
                }
            }
        }
    }

    private void UploadMeshIfNeeded(Mesh3D mesh, RenderStats stats)
    {
        var meshKey = mesh.ResourceKey;
        if (_meshGeometryVersions.TryGetValue(meshKey, out var knownGeometryVersion) && knownGeometryVersion == mesh.GeometryVersion)
        {
            return;
        }

        var geometry = mesh.RenderGeometry;
        var positions = geometry.FlattenPositions();
        var normals = geometry.FlattenNormals();
        var texCoords0 = geometry.FlattenTexCoords0();
        var tangents = geometry.FlattenTangents();
        var boneIndices0 = geometry.FlattenBoneIndices0();
        var boneWeights0 = geometry.FlattenBoneWeights0();
        var indices = (int[])geometry.Indices.Clone();
        var wireframeIndices = (int[])geometry.WireframeIndices.Clone();
        var materialSlots = geometry.HasMaterialSlots ? geometry.MaterialSlots : Array.Empty<float>();
        var vertexLayout = geometry.Layout.ToString();
        var geometryJson = JsonSerializer.Serialize(new { positions, normals, texCoords0, tangents, boneIndices0, boneWeights0, indices, wireframeIndices, materialSlots, vertexLayout }, JsonOptions);
        WebGlInterop.UploadMeshGeometry(_hostId, meshKey, geometryJson);
        _meshGeometryVersions[meshKey] = mesh.GeometryVersion;
        stats.DirtyMeshUploads++;
        stats.RenderGeometryCount++;
        stats.VertexBufferUploadCount += 5;
        stats.IndexBufferUploadCount += 2;
        stats.VertexBufferUploadBytes += geometry.EstimatedVertexUploadBytes;
        stats.IndexBufferUploadBytes += geometry.EstimatedIndexUploadBytes;
        stats.MeshUploadBytes += geometry.EstimatedUploadBytes;
        stats.TangentUploadBytes += geometry.HasTangents ? geometry.Tangents.LongLength * sizeof(float) * 4L : 0L;
        stats.WireframeIndexUploadBytes += geometry.EstimatedWireframeIndexUploadBytes;
        if (geometry.HasTangentSpace) stats.TangentSpaceMeshCount++;
        stats.PacketBytes += geometryJson.Length * sizeof(char);
    }

    private IEnumerable<HighScaleInstanceLayer3D> EnumerateHighScaleLayers()
    {
        foreach (var obj in Scene.Registry.AllObjects)
        {
            if (obj is HighScaleInstanceLayer3D layer)
            {
                yield return layer;
            }
        }
    }

    private static System.Numerics.Vector3[] CreateDefaultNormals(int count)
    {
        var normals = new System.Numerics.Vector3[count];
        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = System.Numerics.Vector3.UnitZ;
        }

        return normals;
    }

    private void UploadDirtyMaterialTextures(RenderStats stats)
    {
        foreach (var obj in Scene.Registry.Renderables)
        {
            var material = MaterialBinding3D.FromMaterial(obj.Material);
            UploadTextureIfDirty(material.BaseColorTextureKey, material.BaseColorTextureData, material.BaseColorTextureVersion, stats);
            UploadTextureIfDirty(material.NormalMapTextureKey, material.NormalMapTextureData, material.NormalMapTextureVersion, stats);
            UploadTextureIfDirty(material.MetallicRoughnessTextureKey, material.MetallicRoughnessTextureData, material.MetallicRoughnessTextureVersion, stats);
            UploadTextureIfDirty(material.EmissiveTextureKey, material.EmissiveTextureData, material.EmissiveTextureVersion, stats);
        }
    }

    private void UploadDirtyEnvironmentTextures(RenderStats stats)
    {
        var skybox = Scene.Environment.Skybox;
        if (skybox.HasEquirectangularTexture)
        {
            UploadTextureIfDirty(skybox.EquirectangularTextureKey, skybox.EquirectangularTextureData, skybox.EnvironmentTextureVersion, stats);
        }
        if (skybox.HasCubemapTextures)
        {
            for (var i = 0; i < 6 && i < skybox.CubemapTextureKeys.Count && i < skybox.CubemapTextureData.Count; i++)
            {
                UploadTextureIfDirty(skybox.CubemapTextureKeys[i], skybox.CubemapTextureData[i], skybox.EnvironmentTextureVersion, stats);
            }
        }
    }

    private void UploadTextureIfDirty(string? textureKey, byte[]? textureData, int version, RenderStats stats)
    {
        if (string.IsNullOrWhiteSpace(textureKey) || textureData is not { Length: > 0 })
        {
            return;
        }

        if (_textureVersions.TryGetValue(textureKey, out var knownVersion) && knownVersion == version)
        {
            return;
        }

        if (!TextureDecodeHelper3D.TryDecodeRgba(textureData, out var decoded, out _))
        {
            return;
        }

        WebGlInterop.UploadTexture(_hostId, textureKey, decoded.Width, decoded.Height, Convert.ToBase64String(decoded.RgbaPixels));
        _textureVersions[textureKey] = version;
        stats.DirtyTextureUploads++;
        stats.TextureUploadBytes += decoded.ByteLength;
    }

    private void UploadDirtyControlTextures(RenderStats stats)
    {
        foreach (var obj in Scene.Registry.AllObjects)
        {
            if (obj is not ControlPlane3D plane || !plane.IsVisible)
            {
                continue;
            }

            var snapshot = plane.Snapshot;
            if (snapshot is null)
            {
                continue;
            }

            if (_textureVersions.TryGetValue(plane.Id, out var knownVersion) && knownVersion == plane.SnapshotVersion)
            {
                continue;
            }

            var pixelWidth = System.Math.Max(plane.RenderPixelWidth, 1);
            var pixelHeight = System.Math.Max(plane.RenderPixelHeight, 1);
            var stride = pixelWidth * 4;
            var bufferSize = stride * pixelHeight;
            var bgraPixels = new byte[bufferSize];
            var bgraHandle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
            try
            {
                snapshot.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), bgraHandle.AddrOfPinnedObject(), bufferSize, stride);
            }
            finally
            {
                bgraHandle.Free();
            }

            var rgbaPixels = new byte[bufferSize];
            for (var i = 0; i < bufferSize; i += 4)
            {
                rgbaPixels[i + 0] = bgraPixels[i + 2];
                rgbaPixels[i + 1] = bgraPixels[i + 1];
                rgbaPixels[i + 2] = bgraPixels[i + 0];
                rgbaPixels[i + 3] = bgraPixels[i + 3];
            }

            var rgbaBase64 = Convert.ToBase64String(rgbaPixels);
            WebGlInterop.UploadTexture(_hostId, plane.Id, pixelWidth, pixelHeight, rgbaBase64);
            _textureVersions[plane.Id] = plane.SnapshotVersion;
            stats.DirtyTextureUploads++;
            stats.TextureUploadBytes += bufferSize;
        }

        SweepUnusedUploadState();
    }

    private void SweepUnusedUploadState()
    {
        var registryVersion = Scene.Registry.Version;
        if (_lastSweptUploadRegistryVersion == registryVersion)
        {
            return;
        }

        var liveMeshes = new HashSet<string>(StringComparer.Ordinal);
        var liveTextures = new HashSet<string>(StringComparer.Ordinal);

        foreach (var obj in Scene.Registry.Renderables)
        {
            liveMeshes.Add(obj.GetMesh().ResourceKey);
            var material = MaterialBinding3D.FromMaterial(obj.Material);
            if (material.HasBaseColorTexture && !string.IsNullOrWhiteSpace(material.BaseColorTextureKey))
            {
                liveTextures.Add(material.BaseColorTextureKey);
            }
        }
        foreach (var layer in EnumerateHighScaleLayers())
        {
            foreach (var lod in new[] { HighScaleLodLevel3D.Detailed, HighScaleLodLevel3D.Simplified, HighScaleLodLevel3D.Proxy, HighScaleLodLevel3D.Billboard })
            {
                foreach (var part in layer.Template.ResolveParts(lod))
                {
                    liveMeshes.Add(part.Mesh.ResourceKey);
                }
            }
        }


        foreach (var obj in Scene.Registry.AllObjects)
        {
            if (obj is ControlPlane3D plane && plane.IsVisible && plane.Snapshot is not null)
            {
                liveTextures.Add(plane.Id);
            }
        }

        foreach (var key in new List<string>(_meshGeometryVersions.Keys))
        {
            if (!liveMeshes.Contains(key))
            {
                TryDestroyMeshGeometry(key);
                _meshGeometryVersions.Remove(key);
            }
        }

        foreach (var key in new List<string>(_textureVersions.Keys))
        {
            if (!liveTextures.Contains(key))
            {
                TryDestroyTexture(key);
                _textureVersions.Remove(key);
            }
        }

        _lastSweptUploadRegistryVersion = registryVersion;
    }

    private void TryDestroyMeshGeometry(string meshKey)
    {
        if (_hostId < 0) return;
        try
        {
            WebGlInterop.DestroyMeshGeometry(_hostId, meshKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("3DEngine WebGL mesh resource destruction failed: " + ex);
        }
    }

    private void TryDestroyTexture(string textureKey)
    {
        if (_hostId < 0) return;
        try
        {
            WebGlInterop.DestroyTexture(_hostId, textureKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("3DEngine WebGL texture resource destruction failed: " + ex);
        }
    }

    private int CountVisibleTriangles()
    {
        var count = 0;
        foreach (var obj in Scene.Registry.Renderables)
        {
            count += obj.GetMesh().Indices.Length / 3;
        }

        return count;
    }

    private void UpdateHostRect()
    {
        if (_hostId < 0)
        {
            return;
        }

        var root = this.GetVisualRoot() as Visual;
        var origin = root is null ? null : this.TranslatePoint(new Point(0, 0), root);
        var x = origin?.X ?? 0d;
        var y = origin?.Y ?? 0d;
        var visible = IsVisible && Bounds.Width > 0 && Bounds.Height > 0;
        WebGlInterop.UpdateHost(_hostId, x, y, Bounds.Width, Bounds.Height, visible);
        WebGlInterop.UpdateMetrics(_hostId, _performanceMetricsText ?? string.Empty, visible && _performanceMetricsVisible);
        WebGlInterop.UpdateCenterCursor(_hostId, visible && _centerCursorVisible);
    }

    private void DestroyHost()
    {
        if (_hostId >= 0)
        {
            _retainedHighScale.Reset(_hostId);
            _clientHighScale.Reset(_hostId);
            WebGlInterop.DestroyHost(_hostId);
            _hostId = -1;
        }

        _moduleReady = false;
        _renderPending = false;
        _invalidateScheduled = false;
        _textureVersions.Clear();
        _meshGeometryVersions.Clear();
        _lastSweptUploadRegistryVersion = -1;
    }

    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
    }
}
