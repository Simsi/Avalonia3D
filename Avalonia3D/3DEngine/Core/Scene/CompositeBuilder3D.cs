using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Physics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Scene;

public sealed class CompositeBuilder3D
{
    private readonly CompositeObject3D _owner;

    internal CompositeBuilder3D(CompositeObject3D owner)
    {
        _owner = owner;
    }

    public CompositePartBuilder<T> Add<T>(string name, T obj) where T : Object3D
    {
        _owner.AddBuiltPart(name, obj);
        return new CompositePartBuilder<T>(obj);
    }

    public CompositePartBuilder<Box3D> Box(string name, float width, float height, float depth)
        => Add(name, new Box3D { Width = width, Height = height, Depth = depth });

    public CompositePartBuilder<Rectangle3D> Rectangle(string name, float width, float height, float depth = 0.02f)
        => Add(name, new Rectangle3D { Width = width, Height = height, Depth = depth });

    public CompositePartBuilder<Plane3D> Plane(string name, float width, float height)
        => Add(name, new Plane3D { Width = width, Height = height });

    public CompositePartBuilder<Ellipse3D> Ellipse(string name, float radiusX, float radiusY, float depth = 0.02f, int segments = 48)
        => Add(name, new Ellipse3D { RadiusX = radiusX, RadiusY = radiusY, Depth = depth, Segments = segments });

    public CompositePartBuilder<Sphere3D> Sphere(string name, float radius, int segments = 32, int rings = 16)
        => Add(name, new Sphere3D { Radius = radius, Segments = segments, Rings = rings });

    public CompositePartBuilder<Cylinder3D> Cylinder(string name, float radius, float height, int segments = 32)
        => Add(name, new Cylinder3D { Radius = radius, Height = height, Segments = segments });

    public CompositePartBuilder<Cone3D> Cone(string name, float radius, float height, int segments = 32)
        => Add(name, new Cone3D { Radius = radius, Height = height, Segments = segments });
}

public sealed class CompositePartBuilder<T> where T : Object3D
{
    internal CompositePartBuilder(T obj)
    {
        Object = obj;
    }

    public T Object { get; }

    public CompositePartBuilder<T> At(float x, float y, float z)
        => At(new Vector3(x, y, z));

    public CompositePartBuilder<T> At(Vector3 position)
    {
        Object.Position = position;
        return this;
    }

    public CompositePartBuilder<T> Rotate(float xDegrees, float yDegrees, float zDegrees)
        => Rotate(new Vector3(xDegrees, yDegrees, zDegrees));

    public CompositePartBuilder<T> Rotate(Vector3 rotationDegrees)
    {
        Object.RotationDegrees = rotationDegrees;
        return this;
    }

    public CompositePartBuilder<T> WithScale(float uniformScale)
        => WithScale(new Vector3(uniformScale));

    public CompositePartBuilder<T> WithScale(Vector3 scale)
    {
        Object.Scale = scale;
        return this;
    }

    public CompositePartBuilder<T> Material(Material3D material)
    {
        Object.Material = material;
        return this;
    }

    public CompositePartBuilder<T> Color(ColorRgba color)
    {
        Object.Color = color;
        return this;
    }

    public CompositePartBuilder<T> Collider()
    {
        Object.Collider ??= CreateDefaultCollider(Object);
        return this;
    }

    public CompositePartBuilder<T> Collider(Collider3D collider)
    {
        Object.Collider = collider;
        return this;
    }

    public CompositePartBuilder<T> NoCollider()
    {
        Object.Collider = null;
        return this;
    }

    public CompositePartBuilder<T> Rigidbody(Rigidbody3D rigidbody)
    {
        Object.Rigidbody = rigidbody;
        return this;
    }

    public CompositePartBuilder<T> Pickable(bool isPickable = true)
    {
        Object.IsPickable = isPickable;
        return this;
    }

    public CompositePartBuilder<T> Visible(bool isVisible = true)
    {
        Object.IsVisible = isVisible;
        return this;
    }

    public CompositePartBuilder<T> Manipulation(bool isEnabled = true)
    {
        Object.IsManipulationEnabled = isEnabled;
        return this;
    }

    private static Collider3D? CreateDefaultCollider(Object3D obj)
    {
        return obj switch
        {
            Box3D box => new BoxCollider3D { Size = new Vector3(box.Width, box.Height, box.Depth) },
            Rectangle3D rectangle => new BoxCollider3D { Size = new Vector3(rectangle.Width, rectangle.Height, rectangle.Depth) },
            Plane3D plane => new PlaneCollider3D { Size = new Vector2(plane.Width, plane.Height) },
            Ellipse3D ellipse => new SphereCollider3D { Radius = System.MathF.Max(System.MathF.Max(ellipse.Width, ellipse.Height), ellipse.Depth) * 0.5f },
            Sphere3D sphere => new SphereCollider3D { Radius = sphere.Radius },
            Cylinder3D cylinder => new BoxCollider3D { Size = new Vector3(cylinder.Radius * 2f, cylinder.Height, cylinder.Radius * 2f) },
            Cone3D cone => new BoxCollider3D { Size = new Vector3(cone.Radius * 2f, cone.Height, cone.Radius * 2f) },
            _ => null
        };
    }
}
