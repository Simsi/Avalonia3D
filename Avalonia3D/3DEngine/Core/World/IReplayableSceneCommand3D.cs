using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

/// <summary>
/// Deterministic scene command that can be copied into a replay log. Implementations must keep
/// all input data immutable and must not read wall-clock time, random globals or UI state.
/// </summary>
public interface IReplayableSceneCommand3D
{
    string Name { get; }
    void Execute(Scene3D scene);
    IReplayableSceneCommand3D CloneForReplay();
}
