using System;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Controls;

public class ControlPlane3D : Object3D
{
    private float _width = 2f;
    private float _height = 1f;
    private bool _alwaysFaceCamera;
    private int _renderPixelWidth;
    private int _renderPixelHeight;
    private RenderTargetBitmap? _snapshot;
    private bool _snapshotDirty = true;
    private int _snapshotVersion;

    public ControlPlane3D(Control content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Name = string.IsNullOrWhiteSpace(content.Name) ? content.GetType().Name : content.Name;
    }

    public override bool UseMeshRendering => false;
    public override bool UseScenePicking => true;

    public Control Content { get; }

    public float Width
    {
        get => _width;
        set
        {
            value = System.MathF.Max(value, 0.01f);
            if (System.MathF.Abs(_width - value) < float.Epsilon)
            {
                return;
            }

            _width = value;
            MarkGeometryDirty();
        }
    }

    public float Height
    {
        get => _height;
        set
        {
            value = System.MathF.Max(value, 0.01f);
            if (System.MathF.Abs(_height - value) < float.Epsilon)
            {
                return;
            }

            _height = value;
            MarkGeometryDirty();
        }
    }

    public bool AlwaysFaceCamera
    {
        get => _alwaysFaceCamera;
        set => SetField(ref _alwaysFaceCamera, value);
    }

    public int RenderPixelWidth
    {
        get => _renderPixelWidth;
        internal set => _renderPixelWidth = System.Math.Max(value, 1);
    }

    public int RenderPixelHeight
    {
        get => _renderPixelHeight;
        internal set => _renderPixelHeight = System.Math.Max(value, 1);
    }

    internal RenderTargetBitmap? Snapshot => _snapshot;
    internal bool SnapshotDirty => _snapshotDirty;
    internal int SnapshotVersion => _snapshotVersion;

    internal void MarkSnapshotDirty()
    {
        _snapshotDirty = true;
        RaiseChanged();
    }

    internal void UpdateSnapshot(RenderTargetBitmap? bitmap, int pixelWidth, int pixelHeight)
    {
        if (!ReferenceEquals(_snapshot, bitmap) && _snapshot is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _snapshot = bitmap;
        RenderPixelWidth = pixelWidth;
        RenderPixelHeight = pixelHeight;
        _snapshotDirty = false;
        _snapshotVersion++;
        RaiseChanged();
    }

    public override Matrix4x4 GetModelMatrix()
    {
        var radians = RotationDegrees * (System.MathF.PI / 180f);
        return Matrix4x4.CreateScale(Scale)
             * Matrix4x4.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z)
             * Matrix4x4.CreateTranslation(Position);
    }

    protected override Mesh3D BuildMesh()
    {
        return MeshFactory.CreateRectangle(Width, Height);
    }
}
