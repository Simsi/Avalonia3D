using System;
using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Transforms;

public sealed class Transform3D
{
    private const float MinimumScaleMagnitude = 0.000001f;
    private Vector3 _localPosition;
    private Quaternion _localRotation = Quaternion.Identity;
    private Vector3 _localScale = Vector3.One;
    private Matrix4x4 _localMatrix = Matrix4x4.Identity;
    private bool _matrixDirty = true;
    private int _version;
    internal Func<SceneAccessLease3D>? EnterMutationScope { get; set; }

    public event EventHandler? Changed;
    public int Version => _version;

    public Vector3 LocalPosition
    {
        get => _localPosition;
        set
        {
            using var access = EnterMutationScope?.Invoke() ?? default;
            value = Guard3D.Finite(value, nameof(value));
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
            using var access = EnterMutationScope?.Invoke() ?? default;
            var normalized = Guard3D.NormalizedQuaternion(value, nameof(value));
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
            using var access = EnterMutationScope?.Invoke() ?? default;
            value = Guard3D.Finite(value, nameof(value));
            if (MathF.Abs(value.X) <= MinimumScaleMagnitude || MathF.Abs(value.Y) <= MinimumScaleMagnitude || MathF.Abs(value.Z) <= MinimumScaleMagnitude)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Scale components must be non-zero so the transform remains invertible.");
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
                _localMatrix = Matrix4x4.CreateScale(_localScale) * Matrix4x4.CreateFromQuaternion(_localRotation) * Matrix4x4.CreateTranslation(_localPosition);
                _matrixDirty = false;
            }
            return _localMatrix;
        }
    }

    public void SetEulerDegrees(Vector3 eulerDegrees)
    {
        eulerDegrees = Guard3D.Finite(eulerDegrees, nameof(eulerDegrees));
        var radians = eulerDegrees * (MathF.PI / 180f);
        LocalRotation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }

    public Vector3 ToEulerDegrees() => LocalRotation.ToEulerDegrees();

    private void Invalidate()
    {
        _matrixDirty = true;
        unchecked { _version++; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
