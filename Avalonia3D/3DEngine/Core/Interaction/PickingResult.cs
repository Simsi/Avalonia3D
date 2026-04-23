using System.Numerics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Interaction;

public sealed record PickingResult(Object3D Object, Vector3 WorldPosition, float Distance);
