using ThreeDEngine.Avalonia.Hosting;

namespace ThreeDEngine.Avalonia.WebGL;

public static class ThreeDEngineWebGlServices
{
    public static void Register() => Scene3DPlatform.RegisterFactory(new WebGlScenePresenterFactory());
}
