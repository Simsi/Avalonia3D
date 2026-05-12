using System;
using System.Numerics;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering.Shadows;

public static class DirectionalShadowResolver3D
{
    public static DirectionalShadowSnapshot3D Resolve(Scene3D scene)
        => Resolve(scene, scene.Registry.GetFrameSnapshot());

    public static DirectionalShadowSnapshot3D Resolve(Scene3D scene, SceneFrameSnapshot3D snapshot)
    {
        var settings = scene.Environment.DirectionalShadows;
        if (!settings.IsEnabled)
        {
            return new DirectionalShadowSnapshot3D { IsEnabled = false, Reason = "disabled" };
        }

        var light = ResolvePrimaryLight(scene);
        if (light is null)
        {
            return new DirectionalShadowSnapshot3D { IsEnabled = false, Reason = "no-enabled-directional-light" };
        }

        if (snapshot.Renderables.Length == 0)
        {
            return new DirectionalShadowSnapshot3D { IsEnabled = false, Reason = "no-shadow-casters" };
        }

        var center = ResolveSceneCenter(snapshot);
        var direction = light.Direction.LengthSquared() > 0.000001f ? Vector3.Normalize(light.Direction) : Vector3.Normalize(new Vector3(-0.35f, -0.75f, -0.55f));
        var up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        var distance = settings.Distance;
        var lightPosition = center - direction * distance;
        var view = Matrix4x4.CreateLookAt(lightPosition, center, up);
        var size = settings.OrthographicSize;
        var projection = Matrix4x4.CreateOrthographic(size, size, 0.1f, distance * 2.5f);

        return new DirectionalShadowSnapshot3D
        {
            IsEnabled = true,
            Resolution = settings.Resolution,
            Strength = settings.Strength,
            Bias = settings.Bias,
            NormalBias = settings.NormalBias,
            LightViewProjection = view * projection,
            Reason = "directional-shadow-map"
        };
    }

    private static DirectionalLight3D? ResolvePrimaryLight(Scene3D scene)
    {
        foreach (var light in scene.Lights)
        {
            if (light.IsEnabled && light.Intensity > 0.0001f) return light;
        }

        return null;
    }

    private static Vector3 ResolveSceneCenter(SceneFrameSnapshot3D snapshot)
    {
        if (snapshot.Renderables.Length == 0) return Vector3.Zero;

        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var any = false;
        foreach (var obj in snapshot.Renderables)
        {
            var bounds = obj.GetMesh().LocalBounds;
            var model = obj.GetModelMatrix();
            var world = bounds.Transform(model);
            if (!world.IsValid) continue;
            min = Vector3.Min(min, world.Min);
            max = Vector3.Max(max, world.Max);
            any = true;
        }

        return any ? (min + max) * 0.5f : Vector3.Zero;
    }
}
