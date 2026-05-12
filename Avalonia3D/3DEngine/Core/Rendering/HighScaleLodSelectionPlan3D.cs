using System.Collections.Generic;
using ThreeDEngine.Core.HighScale;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral high-scale LOD selection result. It is reused as scratch to avoid
/// duplicating detailed/simplified/proxy/billboard partitioning in renderer backends.
/// </summary>
public sealed class HighScaleLodSelectionPlan3D
{
    public List<int> Detailed { get; } = new(256);
    public List<int> Simplified { get; } = new(256);
    public List<int> Proxy { get; } = new(256);
    public List<int> Billboard { get; } = new(256);

    public void Reset()
    {
        Detailed.Clear();
        Simplified.Clear();
        Proxy.Clear();
        Billboard.Clear();
    }

    public List<int> Get(HighScaleLodLevel3D lod)
        => lod == HighScaleLodLevel3D.Detailed ? Detailed :
           lod == HighScaleLodLevel3D.Simplified ? Simplified :
           lod == HighScaleLodLevel3D.Billboard ? Billboard :
           Proxy;
}
