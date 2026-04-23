using Avalonia.Controls;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Adapters;

public interface IAvalonia3DAdapter
{
    bool CanAdapt(Control control);
    Object3D Adapt(Control control);
}
