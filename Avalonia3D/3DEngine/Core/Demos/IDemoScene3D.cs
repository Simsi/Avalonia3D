using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Demos;

public interface IDemoScene3D
{
    DemoSceneDescriptor3D Descriptor { get; }
    void Build(Scene3D scene, DemoSceneContext3D context);
}
