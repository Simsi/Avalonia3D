using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Runtime.InteropServices;
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
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Controls;

public sealed class WebGlScenePresenter : Control, IScenePresenter
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

        UploadDirtyMeshGeometry();
        UploadDirtyControlTextures();

        var packet = WebGlScenePacketBuilder.Build(Scene, (float)Bounds.Width, (float)Bounds.Height);
        var json = JsonSerializer.Serialize(packet, JsonOptions);
        WebGlInterop.RenderScene(_hostId, json);
    }

    private void UploadDirtyMeshGeometry()
    {
        foreach (var obj in Scene.Objects)
        {
            if (!obj.IsVisible || !obj.UseMeshRendering)
            {
                continue;
            }

            var mesh = obj.GetMesh();
            if (_meshGeometryVersions.TryGetValue(obj.Id, out var knownVersion) && knownVersion == obj.GeometryVersion)
            {
                continue;
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

            var geometryJson = JsonSerializer.Serialize(new { positions, indices }, JsonOptions);
            WebGlInterop.UploadMeshGeometry(_hostId, obj.Id, geometryJson);
            _meshGeometryVersions[obj.Id] = obj.GeometryVersion;
        }
    }

    private void UploadDirtyControlTextures()
    {
        foreach (var obj in Scene.Objects)
        {
            if (obj is not ControlPlane3D plane)
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

            var pixelWidth = Math.Max(plane.RenderPixelWidth, 1);
            var pixelHeight = Math.Max(plane.RenderPixelHeight, 1);
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
        }
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
        WebGlInterop.UpdateHost(_hostId, x, y, Bounds.Width, Bounds.Height, IsVisible && Bounds.Width > 0 && Bounds.Height > 0);
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
    }
}
