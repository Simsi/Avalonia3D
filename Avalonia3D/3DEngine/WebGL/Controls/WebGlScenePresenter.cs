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
using ThreeDEngine.Avalonia.Hosting;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Avalonia.WebGL.Rendering;
using ThreeDEngine.Core.Rendering;
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
    private bool _attached;
    private bool _disposed;
    private int _lastSweptUploadRegistryVersion = -1;
    private string? _performanceMetricsText;
    private bool _performanceMetricsVisible;
    private bool _centerCursorVisible;

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
        InvalidateVisual();
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
        catch
        {
            // Keep silent in presenter and simply avoid drawing if the module cannot be loaded.
        }
        finally
        {
            _initializing = false;
        }
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

        SweepUnusedUploadState();

        var uploadStart = Stopwatch.GetTimestamp();
        UploadDirtyMeshGeometry(stats);
        UploadDirtyControlTextures(stats);
        stats.UploadMilliseconds = GetElapsedMilliseconds(uploadStart);

        var packetStart = Stopwatch.GetTimestamp();
        var packet = WebGlScenePacketBuilder.Build(Scene, (float)Bounds.Width, (float)Bounds.Height, stats);
        stats.PacketBuildMilliseconds = GetElapsedMilliseconds(packetStart);

        var serializeStart = Stopwatch.GetTimestamp();
        var json = JsonSerializer.Serialize(packet, JsonOptions);
        stats.SerializationMilliseconds = GetElapsedMilliseconds(serializeStart);

        WebGlInterop.RenderScene(_hostId, json);
        stats.BackendMilliseconds = GetElapsedMilliseconds(start);

        FrameRendered?.Invoke(this, new SceneFrameRenderedEventArgs(Kind, stats.BackendMilliseconds, stats));
    }

    private void UploadDirtyMeshGeometry(RenderStats stats)
    {
        foreach (var obj in Scene.Registry.Renderables)
        {
            UploadMeshIfNeeded(obj.GetMesh(), stats);
        }

        foreach (var layer in EnumerateHighScaleLayers())
        {
            foreach (var part in layer.Template.ResolveParts(HighScaleLodLevel3D.Detailed))
            {
                UploadMeshIfNeeded(part.Mesh, stats);
            }
        }
    }

    private void UploadMeshIfNeeded(Mesh3D mesh, RenderStats stats)
    {
        var meshKey = mesh.ResourceKey;
        if (_meshGeometryVersions.ContainsKey(meshKey))
        {
            return;
        }

        var positions = new float[mesh.Positions.Length * 3];
        for (var i = 0; i < mesh.Positions.Length; i++)
        {
            var baseIndex = i * 3;
            positions[baseIndex] = mesh.Positions[i].X;
            positions[baseIndex + 1] = mesh.Positions[i].Y;
            positions[baseIndex + 2] = mesh.Positions[i].Z;
        }

        var indices = new int[mesh.Indices.Length];
        for (var i = 0; i < mesh.Indices.Length; i++)
        {
            indices[i] = mesh.Indices[i];
        }

        var sourceNormals = mesh.Normals.Length == mesh.Positions.Length ? mesh.Normals : CreateDefaultNormals(mesh.Positions.Length);
        var normals = new float[sourceNormals.Length * 3];
        for (var i = 0; i < sourceNormals.Length; i++)
        {
            var baseIndex = i * 3;
            normals[baseIndex] = sourceNormals[i].X;
            normals[baseIndex + 1] = sourceNormals[i].Y;
            normals[baseIndex + 2] = sourceNormals[i].Z;
        }

        var geometryJson = JsonSerializer.Serialize(new { positions, normals, indices }, JsonOptions);
        WebGlInterop.UploadMeshGeometry(_hostId, meshKey, geometryJson);
        _meshGeometryVersions[meshKey] = 0;
        stats.DirtyMeshUploads++;
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
        }
        foreach (var layer in EnumerateHighScaleLayers())
        {
            foreach (var part in layer.Template.ResolveParts(HighScaleLodLevel3D.Detailed))
            {
                liveMeshes.Add(part.Mesh.ResourceKey);
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
                _meshGeometryVersions.Remove(key);
            }
        }

        foreach (var key in new List<string>(_textureVersions.Keys))
        {
            if (!liveTextures.Contains(key))
            {
                _textureVersions.Remove(key);
            }
        }

        _lastSweptUploadRegistryVersion = registryVersion;
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
            WebGlInterop.DestroyHost(_hostId);
            _hostId = -1;
        }

        _moduleReady = false;
        _textureVersions.Clear();
        _meshGeometryVersions.Clear();
        _lastSweptUploadRegistryVersion = -1;
    }

    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
    }
}
