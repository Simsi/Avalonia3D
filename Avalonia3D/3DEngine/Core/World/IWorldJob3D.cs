using System.Collections.Generic;

namespace ThreeDEngine.Core.World;

/// <summary>
/// Deterministic simulation job. Read-only jobs may run concurrently and must publish mutations
/// only through the command buffer supplied by their context. Exclusive jobs execute on the
/// simulation owner and may access the mutable scene directly.
/// </summary>
public interface IWorldJob3D
{
    string Name { get; }
    WorldJobAccess3D Access { get; }
    IReadOnlyList<string> Dependencies { get; }
    void Execute(WorldJobContext3D context);
}
