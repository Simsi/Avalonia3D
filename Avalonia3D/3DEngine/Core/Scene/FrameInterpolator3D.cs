using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Deterministic render interpolation backed by the scene change journal. Steady-state
/// ticks touch only objects whose transform/physics/animation pose actually changed.
/// </summary>
public sealed class FrameInterpolator3D
{
    private readonly Scene3D _scene;
    private readonly Dictionary<string, Matrix4x4> _previous = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Matrix4x4> _current = new(StringComparer.Ordinal);
    private readonly List<Object3D> _activeObjects = new(64);
    private readonly HashSet<Object3D> _activeSet = new(ObjectReferenceComparer3D<Object3D>.Instance);
    private readonly List<Object3D> _dirtyObjects = new(64);
    private readonly HashSet<Object3D> _dirtySet = new(ObjectReferenceComparer3D<Object3D>.Instance);
    private readonly List<SceneChangeRecord3D> _changeScratch = new(64);
    private long _lastCapturedSequence;
    private long _tickStartSequence;
    private int _renderVersion;
    private double _lastPublishedAlpha = double.NaN;
    private bool _enabled;
    private bool _initialized;

    internal FrameInterpolator3D(Scene3D scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            _initialized = false;
            _activeObjects.Clear();
            _activeSet.Clear();
            Alpha = value
                ? System.Math.Clamp(_scene.UpdateLoop.AccumulatorSeconds / _scene.UpdateLoop.FixedDeltaSeconds, 0d, 1d)
                : 1d;
            _lastPublishedAlpha = Alpha;
            _renderVersion++;
        }
    }

    public double Alpha { get; private set; } = 1d;

    public int RenderVersion => _renderVersion;
    public int ActiveObjectCount => _activeObjects.Count;

    internal void BeginTick(Scene3D scene)
    {
        if (!Enabled) return;
        if (!_initialized)
        {
            FullSynchronize(scene);
        }
        else
        {
            CollapseCompletedInterpolation();
            ApplyJournalChanges(scene, _lastCapturedSequence, interpolate: false);
        }

        _tickStartSequence = scene.ChangeSequence;
    }

    internal void EndTick(Scene3D scene)
    {
        if (!Enabled) return;
        ApplyJournalChanges(scene, _tickStartSequence, interpolate: true);
        Alpha = 0d;
        _lastPublishedAlpha = 0d;
        if (_activeObjects.Count > 0) _renderVersion++;
    }

    internal void SetAlpha(double alpha)
    {
        var previousAlpha = Alpha;
        Alpha = Enabled ? System.Math.Clamp(alpha, 0d, 1d) : 1d;
        if (_activeObjects.Count == 0)
        {
            _lastPublishedAlpha = Alpha;
            return;
        }

        if (double.IsNaN(_lastPublishedAlpha) ||
            System.Math.Abs(Alpha - previousAlpha) > 0.000001d ||
            System.Math.Abs(Alpha - _lastPublishedAlpha) > 0.000001d)
        {
            _lastPublishedAlpha = Alpha;
            _renderVersion++;
        }
    }

    internal void CopyActiveObjects(List<Object3D> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        output.AddRange(_activeObjects);
    }

    public bool TryGetInterpolatedModel(string objectId, out Matrix4x4 model)
    {
        model = Matrix4x4.Identity;
        if (!Enabled || !_current.TryGetValue(objectId, out var current)) return false;
        if (!_previous.TryGetValue(objectId, out var previous) || Alpha >= 0.999d)
        {
            model = current;
            return true;
        }

        model = Interpolate(previous, current, (float)Alpha);
        return true;
    }

    public void Reset()
    {
        _previous.Clear();
        _current.Clear();
        _activeObjects.Clear();
        _activeSet.Clear();
        _dirtyObjects.Clear();
        _dirtySet.Clear();
        _changeScratch.Clear();
        _initialized = false;
        _lastCapturedSequence = _scene.ChangeSequence;
        _tickStartSequence = _lastCapturedSequence;
        Alpha = 1d;
        _lastPublishedAlpha = double.NaN;
        _renderVersion++;
    }

    private void ApplyJournalChanges(Scene3D scene, long cursor, bool interpolate)
    {
        if (cursor == scene.ChangeSequence)
        {
            _lastCapturedSequence = cursor;
            return;
        }

        if (!scene.TryCopyChangesSince(cursor, _changeScratch) || RequiresFullSynchronization(_changeScratch))
        {
            FullSynchronize(scene);
            return;
        }

        _dirtyObjects.Clear();
        _dirtySet.Clear();
        for (var i = 0; i < _changeScratch.Count; i++)
        {
            var change = _changeScratch[i];
            if (change.Source is null || !AffectsTransform(change.Kind)) continue;
            scene.Registry.AddSubtreeObjects(change.Source, _dirtyObjects, _dirtySet);
        }

        for (var i = 0; i < _dirtyObjects.Count; i++)
        {
            var obj = _dirtyObjects[i];
            if (!obj.IsVisible || !obj.UseMeshRendering || !scene.Registry.Contains(obj)) continue;
            var model = obj.GetModelMatrix();
            if (!_current.TryGetValue(obj.Id, out var oldModel)) oldModel = model;

            if (interpolate && oldModel != model)
            {
                _previous[obj.Id] = oldModel;
                _current[obj.Id] = model;
                if (_activeSet.Add(obj)) _activeObjects.Add(obj);
            }
            else
            {
                _previous[obj.Id] = model;
                _current[obj.Id] = model;
            }
        }

        _dirtyObjects.Clear();
        _dirtySet.Clear();
        _changeScratch.Clear();
        _lastCapturedSequence = scene.ChangeSequence;
    }

    private void FullSynchronize(Scene3D scene)
    {
        _previous.Clear();
        _current.Clear();
        _activeObjects.Clear();
        _activeSet.Clear();
        var renderables = scene.Registry.Renderables;
        for (var i = 0; i < renderables.Count; i++)
        {
            var obj = renderables[i];
            var model = obj.GetModelMatrix();
            _previous[obj.Id] = model;
            _current[obj.Id] = model;
        }

        _changeScratch.Clear();
        _dirtyObjects.Clear();
        _dirtySet.Clear();
        _lastCapturedSequence = scene.ChangeSequence;
        _tickStartSequence = _lastCapturedSequence;
        _initialized = true;
    }

    private void CollapseCompletedInterpolation()
    {
        for (var i = 0; i < _activeObjects.Count; i++)
        {
            var id = _activeObjects[i].Id;
            if (_current.TryGetValue(id, out var model)) _previous[id] = model;
        }
        _activeObjects.Clear();
        _activeSet.Clear();
    }

    private static bool RequiresFullSynchronization(List<SceneChangeRecord3D> changes)
    {
        for (var i = 0; i < changes.Count; i++)
        {
            switch (changes[i].Kind)
            {
                case SceneChangeKind.Structure:
                case SceneChangeKind.Visibility:
                case SceneChangeKind.Control:
                case SceneChangeKind.Unknown:
                    return true;
            }
        }
        return false;
    }

    private static bool AffectsTransform(SceneChangeKind kind)
        => kind == SceneChangeKind.Transform ||
           kind == SceneChangeKind.Physics ||
           kind == SceneChangeKind.AnimationPose;

    private static Matrix4x4 Interpolate(Matrix4x4 from, Matrix4x4 to, float t)
    {
        if (Matrix4x4.Decompose(from, out var scaleFrom, out var rotFrom, out var posFrom) &&
            Matrix4x4.Decompose(to, out var scaleTo, out var rotTo, out var posTo))
        {
            var scale = Vector3.Lerp(scaleFrom, scaleTo, t);
            var rotation = Quaternion.Slerp(rotFrom, rotTo, t);
            var position = Vector3.Lerp(posFrom, posTo, t);
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }
        return Matrix4x4.Lerp(from, to, t);
    }
}
