using System;
using System.Numerics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

/// <summary>Built-in immutable commands suitable for command buffers and deterministic replay.</summary>
public static class SceneCommands3D
{
    public static IReplayableSceneCommand3D SetTransform(
        string objectId,
        Vector3 position,
        Vector3 rotationDegrees,
        Vector3 scale)
        => new SetObjectTransformCommand(objectId, position, rotationDegrees, scale);

    public static IReplayableSceneCommand3D SetVisibility(string objectId, bool visible)
        => new SetObjectVisibilityCommand(objectId, visible);

    public static IReplayableSceneCommand3D SetCameraPose(Vector3 position, Vector3 target, Vector3 up)
        => new SetCameraPoseCommand(position, target, up);

    private static Object3D RequireObject(Scene3D scene, string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId)) throw new ArgumentException("Object ID is required.", nameof(objectId));
        var objects = scene.Registry.AllObjects;
        for (var i = 0; i < objects.Count; i++)
        {
            if (string.Equals(objects[i].Id, objectId, StringComparison.Ordinal)) return objects[i];
        }
        throw new InvalidOperationException($"Replay command could not resolve object '{objectId}'.");
    }

    private readonly record struct SetObjectTransformCommand(
        string ObjectId,
        Vector3 Position,
        Vector3 RotationDegrees,
        Vector3 Scale) : IReplayableSceneCommand3D
    {
        public string Name => "SetObjectTransform";

        public void Execute(Scene3D scene)
        {
            var obj = RequireObject(scene, ObjectId);
            obj.Position = Position;
            obj.RotationDegrees = RotationDegrees;
            obj.Scale = Scale;
        }

        public IReplayableSceneCommand3D CloneForReplay() => this;
    }

    private readonly record struct SetObjectVisibilityCommand(string ObjectId, bool Visible) : IReplayableSceneCommand3D
    {
        public string Name => "SetObjectVisibility";

        public void Execute(Scene3D scene) => RequireObject(scene, ObjectId).IsVisible = Visible;

        public IReplayableSceneCommand3D CloneForReplay() => this;
    }

    private readonly record struct SetCameraPoseCommand(Vector3 Position, Vector3 Target, Vector3 Up) : IReplayableSceneCommand3D
    {
        public string Name => "SetCameraPose";

        public void Execute(Scene3D scene) => scene.Camera.SetPose(Position, Target, Up);

        public IReplayableSceneCommand3D CloneForReplay() => this;
    }
}
