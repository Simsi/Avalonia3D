using System.Numerics;

namespace ThreeDEngine.Avalonia.Hosting;

public interface IPointerLockPresenter
{
    bool SupportsPointerLock { get; }
    bool IsPointerLockActive { get; }
    void RequestPointerLock();
    void ExitPointerLock();
    bool TryConsumePointerDelta(out Vector2 delta);
}
