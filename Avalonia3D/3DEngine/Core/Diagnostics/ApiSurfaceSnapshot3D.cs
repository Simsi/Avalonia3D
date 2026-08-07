using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ThreeDEngine.Core.Diagnostics;

/// <summary>
/// Creates a deterministic text representation of the exported API. The snapshot is a
/// review aid for intentional breaking changes, not a backward-compatibility guarantee.
/// </summary>
public static class ApiSurfaceSnapshot3D
{
    public static string Capture(Assembly? assembly = null)
        => Capture(new[] { assembly ?? typeof(ApiSurfaceSnapshot3D).Assembly });

    public static string Capture(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var normalized = assemblies
            .Where(static assembly => assembly is not null)
            .Distinct()
            .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0) throw new ArgumentException("At least one assembly is required.", nameof(assemblies));

        var lines = new List<string>(8192)
        {
            "# Avalonia3D exported API",
            "# Assemblies: " + string.Join(", ", normalized.Select(static assembly => assembly.GetName().Name ?? "unknown"))
        };

        foreach (var assembly in normalized)
        {
            lines.Add(string.Empty);
            lines.Add("## Assembly: " + (assembly.GetName().Name ?? "unknown"));
            foreach (var type in assembly.GetExportedTypes().OrderBy(static type => type.FullName, StringComparer.Ordinal))
            {
                lines.Add(string.Empty);
                lines.Add(FormatTypeDeclaration(type));
                AppendMembers(type, lines);
            }
        }

        return string.Join("\n", lines).TrimEnd() + "\n";
    }

    private static void AppendMembers(Type type, List<string> lines)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var members = new List<string>();

        foreach (var constructor in type.GetConstructors(flags))
        {
            members.Add("  ctor " + type.Name + "(" + FormatParameters(constructor.GetParameters()) + ")");
        }

        foreach (var property in type.GetProperties(flags))
        {
            var access = new StringBuilder();
            if (property.GetMethod is not null) access.Append("get; ");
            if (property.SetMethod is not null) access.Append("set; ");
            members.Add("  property " + FormatType(property.PropertyType) + " " + property.Name + " { " + access.ToString().TrimEnd() + " }");
        }

        foreach (var field in type.GetFields(flags))
        {
            members.Add("  field " + FormatType(field.FieldType) + " " + field.Name);
        }

        foreach (var eventInfo in type.GetEvents(flags))
        {
            members.Add("  event " + FormatType(eventInfo.EventHandlerType ?? typeof(void)) + " " + eventInfo.Name);
        }

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal)) continue;
            var generic = method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(static argument => argument.Name)) + ">" : string.Empty;
            members.Add("  method " + FormatType(method.ReturnType) + " " + method.Name + generic + "(" + FormatParameters(method.GetParameters()) + ")");
        }

        members.Sort(StringComparer.Ordinal);
        lines.AddRange(members);
    }

    private static string FormatTypeDeclaration(Type type)
    {
        var kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";
        return kind + " " + FormatType(type);
    }

    private static string FormatParameters(ParameterInfo[] parameters)
        => string.Join(", ", parameters.Select(static parameter =>
        {
            var prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty;
            var parameterType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
            var optional = parameter.IsOptional ? " = default" : string.Empty;
            return prefix + FormatType(parameterType) + optional;
        }));

    private static string FormatType(Type type)
    {
        if (type.IsGenericParameter) return type.Name;
        if (type.IsArray) return FormatType(type.GetElementType()!) + "[]";
        if (type.IsPointer) return FormatType(type.GetElementType()!) + "*";
        if (type.IsByRef) return FormatType(type.GetElementType()!) + "&";
        if (!type.IsGenericType) return type.FullName ?? type.Name;

        var definitionName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var tick = definitionName.IndexOf('`');
        if (tick >= 0) definitionName = definitionName[..tick];
        return definitionName + "<" + string.Join(",", type.GetGenericArguments().Select(FormatType)) + ">";
    }
}

