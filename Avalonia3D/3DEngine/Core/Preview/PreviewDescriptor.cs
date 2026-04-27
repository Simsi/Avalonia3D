using System;
using System.Reflection;

namespace ThreeDEngine.Core.Preview;

public sealed class PreviewDescriptor
{
    public required string Name { get; init; }
    public required Type DeclaringType { get; init; }
    public MethodInfo? Method { get; init; }
    public bool IsDefaultConstructorPreview { get; init; }
}
