using System;
using System.Numerics;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Scene;

/// <summary>Fluent scene construction for common application scenarios.</summary>
public sealed class SceneBuilder3D : IDisposable
{
    private readonly Scene3D _scene;
    private bool _built;
    private bool _disposed;

    public SceneBuilder3D(Engine3D engine, Scene3DOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _scene = engine.CreateScene(options);
    }

    public SceneBuilder3D Camera(Vector3 position, Vector3 target, float fieldOfViewDegrees = 55f, float nearPlane = 0.1f, float farPlane = 1000f)
    {
        ThrowIfUnavailable();
        _scene.Camera.SetPose(position, target, Vector3.UnitY);
        _scene.Camera.FieldOfViewDegrees = fieldOfViewDegrees;
        _scene.Camera.NearPlane = nearPlane;
        _scene.Camera.FarPlane = farPlane;
        return this;
    }

    public SceneBuilder3D Environment(ColorRgba background, ColorRgba ambientColor, float ambientIntensity)
    {
        ThrowIfUnavailable();
        _scene.BackgroundColor = background;
        _scene.AmbientLightColor = ambientColor;
        _scene.AmbientLightIntensity = ambientIntensity;
        return this;
    }

    public SceneBuilder3D Add(Object3D obj)
    {
        ThrowIfUnavailable();
        _scene.Add(obj ?? throw new ArgumentNullException(nameof(obj)));
        return this;
    }

    public SceneBuilder3D Box(string name, Vector3 position, Vector3 size, ColorRgba color)
        => Add(new Box3D
        {
            Name = name,
            Position = position,
            Width = size.X,
            Height = size.Y,
            Depth = size.Z,
            Material = new Material3D { BaseColor = color }
        });

    public SceneBuilder3D Sphere(string name, Vector3 position, float radius, ColorRgba color, int segments = 32, int rings = 20)
        => Add(new Sphere3D
        {
            Name = name,
            Position = position,
            Radius = radius,
            Segments = segments,
            Rings = rings,
            Material = new Material3D { BaseColor = color }
        });

    public SceneBuilder3D Plane(string name, Vector3 position, Vector2 size, ColorRgba color, int segmentsX = 1, int segmentsY = 1)
        => Add(new Plane3D
        {
            Name = name,
            Position = position,
            Width = size.X,
            Height = size.Y,
            SegmentsX = segmentsX,
            SegmentsY = segmentsY,
            Material = new Material3D { BaseColor = color }
        });

    public SceneBuilder3D DirectionalLight(Vector3 direction, ColorRgba color, float intensity = 1f)
    {
        ThrowIfUnavailable();
        _scene.AddLight(new DirectionalLight3D { Direction = direction, Color = color, Intensity = intensity });
        return this;
    }

    public SceneBuilder3D PointLight(Vector3 position, ColorRgba color, float intensity = 1f, float range = 10f)
    {
        ThrowIfUnavailable();
        _scene.AddLight(new PointLight3D { Position = position, Color = color, Intensity = intensity, Range = range });
        return this;
    }

    public Scene3D Build()
    {
        ThrowIfUnavailable();
        _built = true;
        return _scene;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_built) _scene.Dispose();
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_built) throw new InvalidOperationException("A SceneBuilder3D can build exactly one scene.");
    }
}
