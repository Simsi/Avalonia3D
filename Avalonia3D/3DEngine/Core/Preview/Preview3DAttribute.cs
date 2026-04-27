using System;

namespace ThreeDEngine.Core.Preview;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class Preview3DAttribute : Attribute
{
    public Preview3DAttribute()
    {
    }

    public Preview3DAttribute(string name)
    {
        Name = name;
    }

    public string? Name { get; }
}
