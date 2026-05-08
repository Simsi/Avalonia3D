using System.Collections.Generic;

namespace ThreeDEngine.Core.Demos;

public sealed class DemoSceneCatalog3D
{
    private readonly List<IDemoScene3D> _demos = new();
    public IReadOnlyList<IDemoScene3D> Demos => _demos;
    public void Add(IDemoScene3D demo)
    {
        if (demo is not null) _demos.Add(demo);
    }
}
