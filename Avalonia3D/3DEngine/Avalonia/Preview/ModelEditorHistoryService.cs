using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Preview;

/// <summary>
/// UI-independent undo/redo history for the 3D model editor.
/// It stores runtime object references plus value snapshots, so create/delete/duplicate
/// can be undone in the current editing session without introducing a full scene serializer.
/// </summary>
public sealed class ModelEditorHistoryService
{
    private readonly Stack<ModelEditorHistoryEntry> _undo = new();
    private readonly Stack<ModelEditorHistoryEntry> _redo = new();

    public int Capacity { get; set; } = 100;

    public bool IsDirty { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Count > 0 ? _undo.Peek().Label : null;

    public string? RedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

    public ModelEditorSceneSnapshot Capture(Scene3D scene, string? selectedObjectId = null)
        => ModelEditorSceneSnapshot.Capture(scene, selectedObjectId);

    public bool Commit(string label, ModelEditorSceneSnapshot before, ModelEditorSceneSnapshot after)
    {
        if (string.Equals(before.Fingerprint, after.Fingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        _undo.Push(new ModelEditorHistoryEntry(label, before, after));
        TrimUndoStack();
        _redo.Clear();
        IsDirty = true;
        return true;
    }

    public bool Undo(Scene3D scene, out string? selectedObjectId)
    {
        selectedObjectId = null;
        if (_undo.Count == 0)
        {
            return false;
        }

        var entry = _undo.Pop();
        entry.Before.Restore(scene);
        selectedObjectId = entry.Before.SelectedObjectId;
        _redo.Push(entry);
        IsDirty = true;
        return true;
    }

    public bool Redo(Scene3D scene, out string? selectedObjectId)
    {
        selectedObjectId = null;
        if (_redo.Count == 0)
        {
            return false;
        }

        var entry = _redo.Pop();
        entry.After.Restore(scene);
        selectedObjectId = entry.After.SelectedObjectId;
        _undo.Push(entry);
        IsDirty = true;
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
    }

    public void MarkClean() => IsDirty = false;

    private void TrimUndoStack()
    {
        if (Capacity <= 0 || _undo.Count <= Capacity)
        {
            return;
        }

        var entries = _undo.Reverse().TakeLast(Capacity).ToArray();
        _undo.Clear();
        foreach (var entry in entries)
        {
            _undo.Push(entry);
        }
    }
}

public sealed class ModelEditorSceneSnapshot
{
    private ModelEditorSceneSnapshot(
        IReadOnlyList<Object3D> roots,
        IReadOnlyDictionary<string, ModelEditorObjectSnapshot> objects,
        string? selectedObjectId,
        string fingerprint)
    {
        Roots = roots;
        Objects = objects;
        SelectedObjectId = selectedObjectId;
        Fingerprint = fingerprint;
    }

    public IReadOnlyList<Object3D> Roots { get; }

    public IReadOnlyDictionary<string, ModelEditorObjectSnapshot> Objects { get; }

    public string? SelectedObjectId { get; }

    public string Fingerprint { get; }

    public static ModelEditorSceneSnapshot Capture(Scene3D scene, string? selectedObjectId = null)
    {
        var roots = scene.Objects.ToArray();
        var states = new Dictionary<string, ModelEditorObjectSnapshot>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            foreach (var obj in Enumerate(root))
            {
                states[obj.Id] = ModelEditorObjectSnapshot.Capture(obj);
            }
        }

        return new ModelEditorSceneSnapshot(roots, states, selectedObjectId, BuildFingerprint(roots, states, selectedObjectId));
    }

    public void Restore(Scene3D scene)
    {
        using (scene.BeginUpdate())
        {
            var desiredRootIds = new HashSet<string>(Roots.Select(r => r.Id), StringComparer.Ordinal);
            foreach (var root in scene.Objects.ToArray())
            {
                if (!desiredRootIds.Contains(root.Id))
                {
                    scene.Remove(root);
                }
            }

            foreach (var root in Roots)
            {
                if (!scene.Objects.Any(current => ReferenceEquals(current, root) || current.Id == root.Id))
                {
                    scene.Add(root);
                }
            }

            foreach (var root in Roots)
            {
                foreach (var obj in Enumerate(root))
                {
                    if (Objects.TryGetValue(obj.Id, out var snapshot))
                    {
                        snapshot.Restore(obj);
                    }
                }
            }
        }

        scene.Registry.Invalidate();
        scene.Invalidate();
    }

    private static IEnumerable<Object3D> Enumerate(Object3D root)
    {
        yield return root;
        if (root is CompositeObject3D composite)
        {
            foreach (var child in composite.Children)
            {
                foreach (var nested in Enumerate(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string BuildFingerprint(IReadOnlyList<Object3D> roots, IReadOnlyDictionary<string, ModelEditorObjectSnapshot> states, string? selectedObjectId)
    {
        var sb = new StringBuilder();
        sb.Append("selected=").Append(selectedObjectId ?? string.Empty).Append('|');
        sb.Append("roots=").Append(string.Join(",", roots.Select(r => r.Id))).Append('|');
        foreach (var state in states.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            sb.Append(state.Fingerprint).Append('|');
        }

        return sb.ToString();
    }
}

public sealed class ModelEditorObjectSnapshot
{
    private ModelEditorObjectSnapshot(Object3D obj)
    {
        Id = obj.Id;
        Name = obj.Name;
        Position = obj.Position;
        RotationDegrees = obj.RotationDegrees;
        Scale = obj.Scale;
        IsVisible = obj.IsVisible;
        IsPickable = obj.IsPickable;
        IsManipulationEnabled = obj.IsManipulationEnabled;
        BaseColor = obj.Material.BaseColor;
        Opacity = obj.Material.Opacity;
        Lighting = obj.Material.Lighting;
        Surface = obj.Material.Surface;
        CullMode = obj.Material.CullMode;
        Rigidbody = RigidbodySnapshot.Capture(obj.Rigidbody);
        Primitive = PrimitiveSnapshot.Capture(obj);
        Fingerprint = BuildFingerprint();
    }

    public string Id { get; }
    public string Name { get; }
    public Vector3 Position { get; }
    public Vector3 RotationDegrees { get; }
    public Vector3 Scale { get; }
    public bool IsVisible { get; }
    public bool IsPickable { get; }
    public bool IsManipulationEnabled { get; }
    public ColorRgba BaseColor { get; }
    public float Opacity { get; }
    public LightingMode Lighting { get; }
    public SurfaceMode Surface { get; }
    public CullMode CullMode { get; }
    public RigidbodySnapshot? Rigidbody { get; }
    public PrimitiveSnapshot Primitive { get; }
    public string Fingerprint { get; }

    public static ModelEditorObjectSnapshot Capture(Object3D obj) => new(obj);

    public void Restore(Object3D obj)
    {
        obj.Name = Name;
        obj.Position = Position;
        obj.RotationDegrees = RotationDegrees;
        obj.Scale = Scale;
        obj.IsVisible = IsVisible;
        obj.IsPickable = IsPickable;
        obj.IsManipulationEnabled = IsManipulationEnabled;
        obj.Material.BaseColor = BaseColor;
        obj.Material.Opacity = Opacity;
        obj.Material.Lighting = Lighting;
        obj.Material.Surface = Surface;
        obj.Material.CullMode = CullMode;
        obj.Rigidbody = Rigidbody?.Create();
        Primitive.Restore(obj);
    }

    private string BuildFingerprint()
    {
        return string.Join(";",
            Id,
            Name,
            F(Position.X), F(Position.Y), F(Position.Z),
            F(RotationDegrees.X), F(RotationDegrees.Y), F(RotationDegrees.Z),
            F(Scale.X), F(Scale.Y), F(Scale.Z),
            IsVisible ? "1" : "0",
            IsPickable ? "1" : "0",
            IsManipulationEnabled ? "1" : "0",
            F(BaseColor.R), F(BaseColor.G), F(BaseColor.B), F(BaseColor.A),
            F(Opacity),
            Lighting.ToString(),
            Surface.ToString(),
            CullMode.ToString(),
            Rigidbody?.Fingerprint ?? "rigidbody:none",
            Primitive.Fingerprint);
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}

public sealed class RigidbodySnapshot
{
    private RigidbodySnapshot(Rigidbody3D body)
    {
        Mass = body.Mass;
        Velocity = body.Velocity;
        AngularVelocity = body.AngularVelocity;
        IsKinematic = body.IsKinematic;
        UseGravity = body.UseGravity;
        FreezeRotation = body.FreezeRotation;
        Restitution = body.Restitution;
        Friction = body.Friction;
        LinearDamping = body.LinearDamping;
        Fingerprint = string.Join(";", "rigidbody", F(Mass), F(Velocity.X), F(Velocity.Y), F(Velocity.Z), F(AngularVelocity.X), F(AngularVelocity.Y), F(AngularVelocity.Z), IsKinematic, UseGravity, FreezeRotation, F(Restitution), F(Friction), F(LinearDamping));
    }

    public float Mass { get; }
    public Vector3 Velocity { get; }
    public Vector3 AngularVelocity { get; }
    public bool IsKinematic { get; }
    public bool UseGravity { get; }
    public bool FreezeRotation { get; }
    public float Restitution { get; }
    public float Friction { get; }
    public float LinearDamping { get; }
    public string Fingerprint { get; }

    public static RigidbodySnapshot? Capture(Rigidbody3D? body) => body is null ? null : new RigidbodySnapshot(body);

    public Rigidbody3D Create() => new()
    {
        Mass = Mass,
        Velocity = Velocity,
        AngularVelocity = AngularVelocity,
        IsKinematic = IsKinematic,
        UseGravity = UseGravity,
        FreezeRotation = FreezeRotation,
        Restitution = Restitution,
        Friction = Friction,
        LinearDamping = LinearDamping
    };

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}

public sealed class PrimitiveSnapshot
{
    private PrimitiveSnapshot(string kind)
    {
        Kind = kind;
        Fingerprint = kind;
    }

    private PrimitiveSnapshot(string kind, float a, float b, float c, int segmentsA, int segmentsB)
    {
        Kind = kind;
        A = a;
        B = b;
        C = c;
        SegmentsA = segmentsA;
        SegmentsB = segmentsB;
        Fingerprint = string.Join(";", kind, F(a), F(b), F(c), segmentsA.ToString(CultureInfo.InvariantCulture), segmentsB.ToString(CultureInfo.InvariantCulture));
    }

    public string Kind { get; }
    public float A { get; }
    public float B { get; }
    public float C { get; }
    public int SegmentsA { get; }
    public int SegmentsB { get; }
    public string Fingerprint { get; }

    public static PrimitiveSnapshot Capture(Object3D obj)
    {
        return obj switch
        {
            Sphere3D sphere => new PrimitiveSnapshot("Sphere", sphere.Radius, 0f, 0f, sphere.Segments, sphere.Rings),
            Cylinder3D cylinder => new PrimitiveSnapshot("Cylinder", cylinder.Radius, cylinder.Height, 0f, cylinder.Segments, 0),
            Cone3D cone => new PrimitiveSnapshot("Cone", cone.Radius, cone.Height, 0f, cone.Segments, 0),
            Plane3D plane => new PrimitiveSnapshot("Plane", plane.Width, plane.Height, 0f, plane.SegmentsX, plane.SegmentsY),
            Ellipse3D ellipse => new PrimitiveSnapshot("Ellipse", ellipse.Width, ellipse.Height, ellipse.Depth, ellipse.Segments, 0),
            Rectangle3D rectangle => new PrimitiveSnapshot("Rectangle", rectangle.Width, rectangle.Height, rectangle.Depth, 0, 0),
            _ => new PrimitiveSnapshot(obj.GetType().Name)
        };
    }

    public void Restore(Object3D obj)
    {
        switch (obj)
        {
            case Sphere3D sphere when Kind == "Sphere":
                sphere.Radius = A;
                sphere.Segments = SegmentsA;
                sphere.Rings = SegmentsB;
                break;
            case Cylinder3D cylinder when Kind == "Cylinder":
                cylinder.Radius = A;
                cylinder.Height = B;
                cylinder.Segments = SegmentsA;
                break;
            case Cone3D cone when Kind == "Cone":
                cone.Radius = A;
                cone.Height = B;
                cone.Segments = SegmentsA;
                break;
            case Plane3D plane when Kind == "Plane":
                plane.Width = A;
                plane.Height = B;
                plane.SegmentsX = SegmentsA;
                plane.SegmentsY = SegmentsB;
                break;
            case Ellipse3D ellipse when Kind == "Ellipse":
                ellipse.Width = A;
                ellipse.Height = B;
                ellipse.Depth = C;
                ellipse.Segments = SegmentsA;
                break;
            case Rectangle3D rectangle when Kind is "Rectangle":
                rectangle.Width = A;
                rectangle.Height = B;
                rectangle.Depth = C;
                break;
        }
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}

internal sealed record ModelEditorHistoryEntry(string Label, ModelEditorSceneSnapshot Before, ModelEditorSceneSnapshot After);
