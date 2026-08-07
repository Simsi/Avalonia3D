using System;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Avalonia.Hosting;

namespace ThreeDEngine.Avalonia.OpenGL;

public static class OpenGlEngineBuilderExtensions3D
{
    public static Engine3DBuilder UseOpenGl(this Engine3DBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UsePresenterFactory(new OpenGlScenePresenterFactory());
    }
}
