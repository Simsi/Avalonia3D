using System;
using System.Numerics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Navigation;

public sealed class CameraArcFlight3D
{
    private Camera3D? _camera;
    private Vector3 _startPosition;
    private Vector3 _startTarget;
    private Vector3 _endPosition;
    private Vector3 _endTarget;
    private Vector3 _pivot;
    private Vector3 _startDirection;
    private Vector3 _endDirection;
    private bool _useOrbitPath;
    private float _startRadius;
    private float _endRadius;
    private float _pathRadius;
    private float _elapsed;
    private float _duration = 1f;
    private float _arcHeight;
    private float _clearanceRadius;

    public bool IsActive { get; private set; }
    public float Progress => !IsActive || _duration <= 0f ? 1f : MathF.Min(_elapsed / _duration, 1f);

    public void Start(Camera3D camera, Vector3 target, float distance = 4f, float elevation = 1.2f, float durationSeconds = 1.8f, float arcHeight = 1.0f)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _startPosition = camera.Position;
        _startTarget = camera.Target;
        _endTarget = target;
        var fromTarget = camera.Position - target;
        if (fromTarget.LengthSquared() < 0.0001f) fromTarget = new Vector3(0f, elevation, -distance);
        var horizontal = new Vector3(fromTarget.X, 0f, fromTarget.Z);
        if (horizontal.LengthSquared() < 0.0001f) horizontal = new Vector3(0f, 0f, -1f);
        horizontal = Vector3.Normalize(horizontal) * MathF.Max(distance, 0.2f);
        _endPosition = target + horizontal + Vector3.UnitY * elevation;
        _duration = MathF.Max(0.05f, durationSeconds);
        _arcHeight = MathF.Max(0f, arcHeight);
        _elapsed = 0f;
        _useOrbitPath = false;
        IsActive = true;
    }

    public void StartOrbitAround(
        Camera3D camera,
        Vector3 pivot,
        Vector3 focusPoint,
        float protectedRadius,
        float distanceFromSurface = 2.2f,
        float durationSeconds = 2.4f,
        float arcHeight = 0.45f)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _pivot = pivot;
        _startPosition = camera.Position;
        _startTarget = camera.Target;
        _endTarget = focusPoint;
        _clearanceRadius = MathF.Max(0.1f, protectedRadius + 0.35f);

        var focusDirection = SafeNormalize(focusPoint - pivot, Vector3.UnitZ);
        var endOffset = focusDirection * MathF.Max(_clearanceRadius, protectedRadius + MathF.Max(0.4f, distanceFromSurface));
        _endPosition = pivot + endOffset;

        var startOffset = _startPosition - pivot;
        if (startOffset.LengthSquared() < 0.0001f)
        {
            startOffset = -camera.Forward * MathF.Max(_clearanceRadius, protectedRadius + distanceFromSurface);
        }

        _startRadius = MathF.Max(startOffset.Length(), _clearanceRadius);
        _endRadius = MathF.Max(endOffset.Length(), _clearanceRadius);
        _pathRadius = MathF.Max(MathF.Max(_startRadius, _endRadius), _clearanceRadius + MathF.Max(0f, arcHeight));
        _startDirection = SafeNormalize(startOffset, -focusDirection);
        _endDirection = SafeNormalize(endOffset, focusDirection);
        _duration = MathF.Max(0.05f, durationSeconds);
        _arcHeight = MathF.Max(0f, arcHeight);
        _elapsed = 0f;
        _useOrbitPath = true;
        IsActive = true;
    }

    public void Cancel() => IsActive = false;

    public void Update(float deltaSeconds)
    {
        if (!IsActive || _camera is null) return;
        _elapsed += MathF.Max(0f, deltaSeconds);
        var t = MathF.Min(_elapsed / _duration, 1f);
        var eased = SmoothStep(t);

        if (_useOrbitPath)
        {
            var direction = SlerpDirection(_startDirection, _endDirection, eased);
            var radius = Lerp(_startRadius, _endRadius, eased);
            var safeRadius = MathF.Max(radius, _pathRadius - MathF.Sin(eased * MathF.PI) * _arcHeight * 0.35f);
            safeRadius = MathF.Max(safeRadius, _clearanceRadius);
            var position = _pivot + direction * safeRadius;
            var target = Vector3.Lerp(_startTarget, _endTarget, eased);
            target = Vector3.Lerp(target, _endTarget, eased * eased);
            _camera.Position = position;
            _camera.Target = target;
        }
        else
        {
            var target = Vector3.Lerp(_startTarget, _endTarget, eased);
            var linear = Vector3.Lerp(_startPosition, _endPosition, eased);
            var chord = _endPosition - _startPosition;
            var side = Vector3.Cross(chord, Vector3.UnitY);
            if (side.LengthSquared() < 0.0001f) side = Vector3.UnitX;
            else side = Vector3.Normalize(side);
            var sideBend = side * (MathF.Sin(eased * MathF.PI) * _arcHeight);
            var verticalBend = Vector3.UnitY * (MathF.Sin(eased * MathF.PI) * _arcHeight * 0.35f);
            _camera.Position = linear + sideBend + verticalBend;
            _camera.Target = target;
        }

        if (t >= 1f) IsActive = false;
    }

    private static float SmoothStep(float t)
    {
        t = MathF.Min(MathF.Max(t, 0f), 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        => value.LengthSquared() < 0.000001f ? SafeNormalize(fallback, Vector3.UnitZ) : Vector3.Normalize(value);

    private static Vector3 SlerpDirection(Vector3 from, Vector3 to, float t)
    {
        from = SafeNormalize(from, Vector3.UnitZ);
        to = SafeNormalize(to, Vector3.UnitZ);
        var dot = global::System.Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (dot > 0.9995f)
        {
            return SafeNormalize(Vector3.Lerp(from, to, t), to);
        }

        if (dot < -0.9995f)
        {
            var axis = Vector3.Cross(from, Vector3.UnitY);
            if (axis.LengthSquared() < 0.0001f) axis = Vector3.Cross(from, Vector3.UnitX);
            axis = Vector3.Normalize(axis);
            var q = Quaternion.CreateFromAxisAngle(axis, MathF.PI * t);
            return SafeNormalize(Vector3.Transform(from, q), to);
        }

        var theta = MathF.Acos(dot);
        var sinTheta = MathF.Sin(theta);
        var a = MathF.Sin((1f - t) * theta) / sinTheta;
        var b = MathF.Sin(t * theta) / sinTheta;
        return SafeNormalize(from * a + to * b, to);
    }
}
