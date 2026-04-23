using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Scene;

public sealed class Scene3D
{
    private readonly List<Object3D> _objects = new List<Object3D>();
    private readonly Camera3D _camera;
    private ColorRgba _backgroundColor = ColorRgba.White;

    public Scene3D()
    {
        _camera = new Camera3D();
        _camera.Changed += OnCameraChanged;
    }

    public event EventHandler? SceneChanged;

    public Camera3D Camera => _camera;

    public IReadOnlyList<Object3D> Objects => _objects;

    public ColorRgba BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (_backgroundColor.Equals(value))
            {
                return;
            }

            _backgroundColor = value;
            RaiseChanged();
        }
    }

    public T Add<T>(T obj) where T : Object3D
    {
        _objects.Add(obj);
        obj.Changed += OnObjectChanged;
        RaiseChanged();
        return obj;
    }

    public bool Remove(Object3D obj)
    {
        var removed = _objects.Remove(obj);
        if (!removed)
        {
            return false;
        }

        obj.Changed -= OnObjectChanged;
        RaiseChanged();
        return true;
    }

    public void Clear()
    {
        foreach (var obj in _objects)
        {
            obj.Changed -= OnObjectChanged;
        }

        _objects.Clear();
        RaiseChanged();
    }

    public void Invalidate() => RaiseChanged();

    private void OnObjectChanged(object? sender, EventArgs e) => RaiseChanged();
    private void OnCameraChanged(object? sender, EventArgs e) => RaiseChanged();
    private void RaiseChanged() => SceneChanged?.Invoke(this, EventArgs.Empty);
}
