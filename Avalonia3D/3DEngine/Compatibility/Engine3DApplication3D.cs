using System;
using System.Runtime.CompilerServices;
using ThreeDEngine.Core.Hosting;
using ThreeDEngine.Core.Importers.Gltf;
using ThreeDEngine.Core.Physics.Jitter2;

#if AVALONIA3D_ENGINE_AGGREGATE
    #if AVALONIA3D_BROWSER
using ThreeDEngine.Avalonia.WebGL;
    #else
using ThreeDEngine.Avalonia.OpenGL;
    #endif
#else
// A source-drop host compiles both backend folders into its application assembly. Runtime
// platform selection is therefore required because a shared net8.0 Avalonia project can be
// consumed by both desktop and browser launchers.
using ThreeDEngine.Avalonia.OpenGL;
using ThreeDEngine.Avalonia.WebGL;
#endif

namespace ThreeDEngine;

/// <summary>
/// Convenience entry point supplied by the aggregate Avalonia3D.Engine package and by the
/// complete 3DEngine source-drop. Modular applications may reference individual packages and
/// compose the same builder explicitly.
/// </summary>
public static class Engine3DApplication3D
{
    public static Engine3DBuilder CreateDefaultBuilder()
    {
        var builder = new Engine3DBuilder()
            .UseGltfAssets()
            .UseJitter2Physics();

#if AVALONIA3D_ENGINE_AGGREGATE
    #if AVALONIA3D_BROWSER
        return builder.UseWebGl();
    #else
        return builder.UseOpenGl();
    #endif
#else
        return OperatingSystem.IsBrowser()
            ? builder.UseWebGl()
            : builder.UseOpenGl();
#endif
    }

    public static Engine3D CreateDefaultEngine() => CreateDefaultBuilder().Build();
}

/// <summary>
/// Registers the compatibility default stack before any XAML-created Scene3DControl can invoke
/// its parameterless constructor. In source-drop mode this runs inside the host application
/// assembly; in package mode it runs when Avalonia3D.Engine is loaded lazily by Core.
/// </summary>
internal static class Engine3DDefaultStackBootstrap3D
{
    [ModuleInitializer]
    internal static void RegisterDefaultFactory()
    {
        Engine3DDefaultStack3D.Register(Engine3DApplication3D.CreateDefaultEngine);
    }
}
