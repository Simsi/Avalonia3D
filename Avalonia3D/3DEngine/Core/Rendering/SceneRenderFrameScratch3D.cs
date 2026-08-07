using System;
using System.Threading;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Renderer-owned reusable frame-publication storage. One workspace may service only one frame
/// at a time. Its read lease protects object transforms/material state while the backend builds
/// and submits commands, closing the torn-state window that existed when the lease covered only
/// the scalar snapshot copy.
/// </summary>
internal sealed class SceneRenderFrameScratch3D
{
    private readonly SceneFrameSnapshot3D _registry = SceneFrameSnapshot3D.CreateReusable();
    private readonly SceneRenderSnapshot3D _published = new();
    private readonly SceneRenderFrameContext3D _context = new();
    private int _inUse;

    public SceneRenderFrameContext3D Begin(
        Scene3D scene,
        float width,
        float height,
        BackendKind backendKind)
    {
        ArgumentNullException.ThrowIfNull(scene);
        width = Guard3D.Positive(width, nameof(width));
        height = Guard3D.Positive(height, nameof(height));
        backendKind = Guard3D.Defined(backendKind, nameof(backendKind));
        if (Interlocked.CompareExchange(ref _inUse, 1, 0) != 0)
        {
            throw new InvalidOperationException("The render frame scratch workspace is already in use. Dispose the current frame before beginning another one.");
        }

        var sceneAccess = scene.EnterRenderReadScope();
        try
        {
            SceneRenderSnapshot3D.CaptureInto(scene, width / height, backendKind, _registry, _published);
            _context.Reset(scene, _published, width, height, sceneAccess, this);
            return _context;
        }
        catch
        {
            sceneAccess.Dispose();
            Volatile.Write(ref _inUse, 0);
            throw;
        }
    }

    internal void Release() => Volatile.Write(ref _inUse, 0);
}
