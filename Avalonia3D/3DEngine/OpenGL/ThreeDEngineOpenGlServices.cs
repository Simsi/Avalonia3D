using ThreeDEngine.Avalonia.Hosting;

namespace ThreeDEngine.Avalonia.OpenGL;

public static class ThreeDEngineOpenGlServices
{
    public static void Register() => Scene3DPlatform.RegisterFactory(new OpenGlScenePresenterFactory());
}
