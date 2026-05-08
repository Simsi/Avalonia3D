using System.Numerics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Lighting;

public static class SceneLightingResolver3D
{
    private static readonly Vector3 DefaultDirectionalDirection = Vector3.Normalize(new Vector3(-0.35f, -0.75f, -0.55f));

    public static SceneLightingSnapshot3D Resolve(Scene3D scene)
    {
        var ambient = new Vector3(scene.AmbientLightColor.R, scene.AmbientLightColor.G, scene.AmbientLightColor.B) * scene.AmbientLightIntensity;

        var directionalColor = Vector3.Zero;
        var directionalDirection = DefaultDirectionalDirection;
        foreach (var light in scene.Lights)
        {
            if (!light.IsEnabled) continue;
            directionalDirection = light.Direction.LengthSquared() < 0.000001f ? DefaultDirectionalDirection : Vector3.Normalize(light.Direction);
            directionalColor = new Vector3(light.Color.R, light.Color.G, light.Color.B) * light.Intensity;
            break;
        }

        var pointPosition = new Vector4(0f, 0f, 0f, 1f);
        var pointColor = Vector4.Zero;
        foreach (var light in scene.PointLights)
        {
            if (!light.IsEnabled) continue;
            pointPosition = new Vector4(light.Position, light.Range);
            pointColor = new Vector4(light.Color.R * light.Intensity, light.Color.G * light.Intensity, light.Color.B * light.Intensity, 1f);
            break;
        }

        var spotPosition = new Vector4(0f, 0f, 0f, 1f);
        var spotDirection = new Vector4(0f, -1f, 0f, 0f);
        var spotColor = Vector4.Zero;
        var spotCone = new Vector4(0.95f, 0.85f, 1f, 0f);
        foreach (var light in scene.SpotLights)
        {
            if (!light.IsEnabled) continue;
            var direction = light.Direction.LengthSquared() < 0.000001f ? -Vector3.UnitY : Vector3.Normalize(light.Direction);
            spotPosition = new Vector4(light.Position, light.Range);
            spotDirection = new Vector4(direction, 0f);
            spotColor = new Vector4(light.Color.R * light.Intensity, light.Color.G * light.Intensity, light.Color.B * light.Intensity, 1f);
            spotCone = new Vector4(light.InnerCosine, light.OuterCosine, light.Range, 1f);
            break;
        }

        return new SceneLightingSnapshot3D(ambient, directionalDirection, directionalColor, pointPosition, pointColor, spotPosition, spotDirection, spotColor, spotCone);
    }
}
