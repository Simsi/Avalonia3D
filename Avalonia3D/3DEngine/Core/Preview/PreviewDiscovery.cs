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
        var types = ResolveTargetTypes(assembly, typeFullName);

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


    public static IReadOnlyList<Type> ResolveTargetTypes(Assembly assembly, string? typeFullName)
    {
        var allTypes = GetLoadableTypes(assembly)
            .Where(t => !t.IsAbstract)
            .ToArray();

        if (string.IsNullOrWhiteSpace(typeFullName))
        {
            return allTypes;
        }

        var requested = typeFullName.Trim();
        var requestedClr = requested.Replace('/', '+');
        var shortName = requestedClr;
        var lastDot = shortName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot + 1 < shortName.Length)
        {
            shortName = shortName.Substring(lastDot + 1);
        }

        // First try exact CLR names. Nested classes use Outer+Inner in reflection.
        var exact = allTypes
            .Where(t => string.Equals(t.FullName, requestedClr, StringComparison.Ordinal) ||
                        string.Equals(t.FullName, requested, StringComparison.Ordinal))
            .ToArray();
        if (exact.Length > 0)
        {
            return exact;
        }

        // Then tolerate a wrong namespace from the VSIX/source regex. This is common with
        // file-scoped namespaces, generated source, moved files, and partial classes.
        var suffixMatches = allTypes
            .Where(t => t.FullName is not null &&
                        (t.FullName.EndsWith("." + shortName, StringComparison.Ordinal) ||
                         t.FullName.EndsWith("+" + shortName, StringComparison.Ordinal) ||
                         string.Equals(t.Name, shortName, StringComparison.Ordinal)))
            .OrderByDescending(IsPreviewCandidate)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();
        if (suffixMatches.Length > 0)
        {
            return suffixMatches;
        }

        return Array.Empty<Type>();
    }

    public static IReadOnlyList<string> FindSimilarTypeNames(Assembly assembly, string typeFullName, int maxCount = 12)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
        {
            return Array.Empty<string>();
        }

        var requested = typeFullName.Trim().Replace('/', '+');
        var shortName = requested;
        var lastDot = shortName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot + 1 < shortName.Length)
        {
            shortName = shortName.Substring(lastDot + 1);
        }

        return GetLoadableTypes(assembly)
            .Where(t => t.FullName is not null)
            .Where(t => t.FullName!.IndexOf(shortName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf(shortName, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(IsPreviewCandidate)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => t.FullName!)
            .Take(System.Math.Max(1, maxCount))
            .ToArray();
    }

    private static bool IsPreviewCandidate(Type type)
    {
        if (type.GetCustomAttribute<Preview3DAttribute>() is not null)
        {
            return true;
        }

        if (typeof(CompositeObject3D).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
        {
            return true;
        }

        return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Any(method => method.GetCustomAttribute<Preview3DAttribute>() is not null &&
                           method.GetParameters().Length == 0 &&
                           IsSupportedReturnType(method.ReturnType));
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
