using System.Numerics;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Preview;

public sealed class PreviewScene3D
{
    public string Name { get; init; } = "Preview";
    public Scene3D Scene { get; init; } = new Scene3D();

    public static PreviewScene3D Object(string name, Object3D obj)
    {
        var scene = CreateDefaultScene();
        scene.Add(obj);
        return new PreviewScene3D { Name = name, Scene = scene };
    }

    public static PreviewScene3D FromScene(string name, Scene3D scene)
        => new PreviewScene3D { Name = name, Scene = scene };

    public static Scene3D CreateDefaultScene()
    {
        var scene = new Scene3D
        {
            BackgroundColor = new ColorRgba(0.13f, 0.15f, 0.18f, 1f)
        };
        scene.Camera.Position = new Vector3(2.8f, 2.1f, -4.0f);
        scene.Camera.Target = new Vector3(0f, 0.8f, 0f);
        scene.AddLight(new DirectionalLight3D
        {
            Direction = new Vector3(-0.35f, -0.8f, -0.45f),
            Color = ColorRgba.White,
            Intensity = 1.25f
        });
        scene.AddLight(new PointLight3D
        {
            Position = new Vector3(1.5f, 2.5f, -2f),
            Color = ColorRgba.White,
            Intensity = 0.75f,
            Range = 6f
        });
        return scene;
    }
}
