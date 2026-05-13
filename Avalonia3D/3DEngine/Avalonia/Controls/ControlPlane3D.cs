using System;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Controls;

public class ControlPlane3D : Object3D
{
    private float _width = 2f;
    private float _height = 1f;
    private bool _alwaysFaceCamera;
    private int _renderPixelWidth;
    private int _renderPixelHeight;
    private double _renderLogicalWidth;
    private double _renderLogicalHeight;
    private double _renderScale = 2.0d;
    private RenderTargetBitmap? _snapshot;
    private bool _snapshotDirty = true;
    private int _snapshotVersion;

    public ControlPlane3D(Control content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Name = string.IsNullOrWhiteSpace(content.Name) ? content.GetType().Name : content.Name;
        IsManipulationEnabled = false;
        Collider = new PlaneCollider3D { Size = new Vector2(_width, _height), LocalNormal = Vector3.UnitZ };
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
            UpdateColliderSize();
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
            UpdateColliderSize();
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

    /// <summary>
    /// Supersampling factor for Avalonia controls rendered into a 3D texture.
    /// 1.0 is native control size; 2.0-4.0 keeps text readable on perspective planes.
    /// </summary>
    public double RenderScale
    {
        get => _renderScale;
        set
        {
            value = global::System.Math.Clamp(value, 1.0d, 4.0d);
            if (global::System.Math.Abs(_renderScale - value) < 0.001d) return;
            _renderScale = value;
            MarkSnapshotDirty();
        }
    }

    internal double RenderLogicalWidth => _renderLogicalWidth > 0d ? _renderLogicalWidth : global::System.Math.Max(_renderPixelWidth / global::System.Math.Max(_renderScale, 1d), 1d);
    internal double RenderLogicalHeight => _renderLogicalHeight > 0d ? _renderLogicalHeight : global::System.Math.Max(_renderPixelHeight / global::System.Math.Max(_renderScale, 1d), 1d);

    internal RenderTargetBitmap? Snapshot => _snapshot;
    internal bool SnapshotDirty => _snapshotDirty;
    internal int SnapshotVersion => _snapshotVersion;

    internal void MarkSnapshotDirty()
    {
        if (_snapshotDirty)
        {
            return;
        }

        _snapshotDirty = true;
        RaiseChanged(SceneChangeKind.Control);
    }

    internal void UpdateSnapshot(RenderTargetBitmap? bitmap, int pixelWidth, int pixelHeight, double logicalWidth, double logicalHeight)
    {
        if (!ReferenceEquals(_snapshot, bitmap) && _snapshot is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _snapshot = bitmap;
        RenderPixelWidth = pixelWidth;
        RenderPixelHeight = pixelHeight;
        _renderLogicalWidth = global::System.Math.Max(logicalWidth, 1d);
        _renderLogicalHeight = global::System.Math.Max(logicalHeight, 1d);
        _snapshotDirty = false;
        _snapshotVersion++;
        RaiseChanged(SceneChangeKind.Control);
    }

    public override Matrix4x4 GetModelMatrix() => base.GetModelMatrix();

    /// <summary>
    /// Rotates the plane so that its front side (+Z local normal) faces the current camera position.
    /// This is useful for fixed-position UI panels that should be readable without using billboard mode.
    /// </summary>
    public void FaceCamera(Camera3D camera)
    {
        if (camera is null)
        {
            throw new ArgumentNullException(nameof(camera));
        }

        var direction = camera.Position - Position;
        RotationDegrees = DirectionToEulerDegrees(direction);
    }

    private static Vector3 DirectionToEulerDegrees(Vector3 direction)
    {
        direction = direction.LengthSquared() > 0.000001f ? Vector3.Normalize(direction) : Vector3.UnitZ;
        var yaw = MathF.Atan2(direction.X, direction.Z) * 180f / MathF.PI;
        var horizontal = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        var pitch = -MathF.Atan2(direction.Y, horizontal) * 180f / MathF.PI;
        return new Vector3(pitch, yaw, 0f);
    }

    private void UpdateColliderSize()
    {
        if (Collider is PlaneCollider3D plane)
        {
            plane.Size = new Vector2(_width, _height);
            plane.LocalNormal = Vector3.UnitZ;
        }
    }

    protected override Mesh3D BuildMesh()
    {
        return MeshFactory.CreateRectangle(Width, Height);
    }
}
