using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Preview;

public static class PreviewDiscovery
{
    public static IReadOnlyList<PreviewDescriptor> Discover(Assembly assembly, string? typeFullName = null)
    {
        var result = new List<PreviewDescriptor>();
        var types = GetLoadableTypes(assembly)
            .Where(t => !t.IsAbstract && (typeFullName is null || string.Equals(t.FullName, typeFullName, StringComparison.Ordinal)))
            .ToArray();

        foreach (var type in types)
        {
            var typePreview = type.GetCustomAttribute<Preview3DAttribute>();
            if (typeof(CompositeObject3D).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                result.Add(new PreviewDescriptor
                {
                    Name = typePreview?.Name ?? type.Name,
                    DeclaringType = type,
                    IsDefaultConstructorPreview = true
                });
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<Preview3DAttribute>();
                if (attr is null || method.GetParameters().Length != 0 || !IsSupportedReturnType(method.ReturnType))
                {
                    continue;
                }

                result.Add(new PreviewDescriptor
                {
                    Name = attr.Name ?? method.Name,
                    DeclaringType = type,
                    Method = method
                });
            }
        }

        return result;
    }

    public static IReadOnlyList<PreviewScene3D> Create(PreviewDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        object? value;
        try
        {
            if (descriptor.IsDefaultConstructorPreview)
            {
                value = Activator.CreateInstance(descriptor.DeclaringType);
            }
            else if (descriptor.Method is not null)
            {
                value = descriptor.Method.Invoke(null, null);
            }
            else
            {
                return Array.Empty<PreviewScene3D>();
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidOperationException($"3D preview '{descriptor.Name}' failed: {ex.InnerException.Message}", ex.InnerException);
        }

        return Normalize(descriptor.Name, value);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static bool IsSupportedReturnType(Type type)
    {
        if (typeof(Object3D).IsAssignableFrom(type) ||
            typeof(Scene3D).IsAssignableFrom(type) ||
            typeof(PreviewScene3D).IsAssignableFrom(type))
        {
            return true;
        }

        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return false;
        }

        if (!type.IsGenericType)
        {
            return true;
        }

        return type.GetGenericArguments().Any(argument =>
            typeof(Object3D).IsAssignableFrom(argument) ||
            typeof(Scene3D).IsAssignableFrom(argument) ||
            typeof(PreviewScene3D).IsAssignableFrom(argument));
    }

    private static IReadOnlyList<PreviewScene3D> Normalize(string name, object? value)
    {
        switch (value)
        {
            case null:
                return Array.Empty<PreviewScene3D>();
            case PreviewScene3D preview:
                return new[] { preview };
            case Scene3D scene:
                return new[] { PreviewScene3D.FromScene(name, scene) };
            case Object3D obj:
                return new[] { PreviewScene3D.Object(name, obj) };
            case IEnumerable enumerable when value is not string:
            {
                var result = new List<PreviewScene3D>();
                foreach (var item in enumerable)
                {
                    result.AddRange(Normalize(name, item));
                }

                return result;
            }
            default:
                return Array.Empty<PreviewScene3D>();
        }
    }
}
