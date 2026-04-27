using System;
using System.Numerics;

namespace ThreeDEngine.Core.Transforms;

public sealed class Transform3D
{
    private Vector3 _localPosition;
    private Quaternion _localRotation = Quaternion.Identity;
    private Vector3 _localScale = Vector3.One;
    private Matrix4x4 _localMatrix = Matrix4x4.Identity;
    private bool _matrixDirty = true;
    private int _version;

    public event EventHandler? Changed;

    public int Version => _version;

    public Vector3 LocalPosition
    {
        get => _localPosition;
        set
        {
            if (_localPosition == value) return;
            _localPosition = value;
            Invalidate();
        }
    }

    public Quaternion LocalRotation
    {
        get => _localRotation;
        set
        {
            var normalized = value.LengthSquared() < 0.000001f ? Quaternion.Identity : Quaternion.Normalize(value);
            if (_localRotation == normalized) return;
            _localRotation = normalized;
            Invalidate();
        }
    }

    public Vector3 LocalScale
    {
        get => _localScale;
        set
        {
            if (_localScale == value) return;
            _localScale = value;
            Invalidate();
        }
    }

    public Matrix4x4 LocalMatrix
    {
        get
        {
            if (_matrixDirty)
            {
                _localMatrix = Matrix4x4.CreateScale(LocalScale) * Matrix4x4.CreateFromQuaternion(LocalRotation) * Matrix4x4.CreateTranslation(LocalPosition);
                _matrixDirty = false;
            }

            return _localMatrix;
        }
    }

    public void SetEulerDegrees(Vector3 eulerDegrees)
    {
        var radians = eulerDegrees * (MathF.PI / 180f);
        LocalRotation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }

    private void Invalidate()
    {
        _matrixDirty = true;
        _version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
