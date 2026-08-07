using System;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Avalonia.Hosting;

namespace ThreeDEngine.Avalonia.WebGL;

public static class WebGlEngineBuilderExtensions3D
{
    public static Engine3DBuilder UseWebGl(this Engine3DBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UsePresenterFactory(new WebGlScenePresenterFactory());
    }
}
