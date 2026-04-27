using System.Numerics;

namespace ThreeDEngine.Core.Physics;

public sealed class Rigidbody3D
{
    public float Mass { get; set; } = 1f;
    public Vector3 Velocity { get; set; }
    public Vector3 AngularVelocity { get; set; }
    public bool IsKinematic { get; set; }
    public bool UseGravity { get; set; } = true;
    public bool FreezeRotation { get; set; } = true;
    public float Restitution { get; set; } = 0.15f;
    public float Friction { get; set; } = 0.55f;
    public float LinearDamping { get; set; } = 0.01f;
    public bool IsGrounded { get; internal set; }
}
