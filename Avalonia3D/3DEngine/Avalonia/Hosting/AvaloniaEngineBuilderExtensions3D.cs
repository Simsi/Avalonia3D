using System;
using ThreeDEngine.Core.Hosting;

namespace ThreeDEngine.Avalonia.Hosting;

/// <summary>Registers an Avalonia scene presenter factory in an engine scope.</summary>
public static class AvaloniaEngineBuilderExtensions3D
{
    public static Engine3DBuilder UsePresenterFactory(
        this Engine3DBuilder builder,
        IScenePresenterFactory factory,
        EngineServiceOwnership3D ownership = EngineServiceOwnership3D.External)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        builder.Services.ReplaceSingleton(factory, ownership);
        return builder;
    }
}
