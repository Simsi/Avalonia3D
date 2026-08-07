using System;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Collision;

public abstract class Collider3D
{
    public event EventHandler? Changed;

    public Object3D? Owner { get; internal set; }
    public int Version { get; private set; }

    public abstract Bounds3D GetWorldBounds(Object3D owner);
    public abstract bool Raycast(Object3D owner, Ray ray, out RaycastHit3D hit);

    private protected SceneAccessLease3D EnterMutationScope()
        => Owner?.OwnerScene?.EnterMutationScope(GetType().Name) ?? default;

    protected void RaiseChanged()
    {
        unchecked { Version++; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
