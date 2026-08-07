using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThreeDEngine.Core.Demos;

public sealed class DemoSceneCatalog3D
{
    private readonly List<IDemoScene3D> _demos = new();
    private readonly ReadOnlyCollection<IDemoScene3D> _demosView;

    public DemoSceneCatalog3D()
    {
        _demosView = _demos.AsReadOnly();
    }

    public IReadOnlyList<IDemoScene3D> Demos => _demosView;
    public void Add(IDemoScene3D demo)
    {
        _demos.Add(demo ?? throw new ArgumentNullException(nameof(demo)));
    }
}
