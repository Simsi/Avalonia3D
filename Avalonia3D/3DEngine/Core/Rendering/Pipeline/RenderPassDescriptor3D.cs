using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Rendering.Pipeline;

internal sealed class RenderPassDescriptor3D
{
    private RenderPassKind3D _kind;
    private string _name = string.Empty;
    private IReadOnlyList<RenderTargetDescriptor3D> _inputs = Array.Empty<RenderTargetDescriptor3D>();
    private IReadOnlyList<RenderTargetDescriptor3D> _outputs = Array.Empty<RenderTargetDescriptor3D>();

    public RenderPassKind3D Kind
    {
        get => _kind;
        init => _kind = Guard3D.Defined(value, nameof(Kind));
    }

    public string Name
    {
        get => _name;
        init => _name = Guard3D.RequiredText(value, nameof(Name));
    }

    public IReadOnlyList<RenderTargetDescriptor3D> Inputs
    {
        get => _inputs;
        init => _inputs = Snapshot(value, nameof(Inputs));
    }

    public IReadOnlyList<RenderTargetDescriptor3D> Outputs
    {
        get => _outputs;
        init => _outputs = Snapshot(value, nameof(Outputs));
    }

    private static IReadOnlyList<RenderTargetDescriptor3D> Snapshot(
        IReadOnlyList<RenderTargetDescriptor3D>? values,
        string parameterName)
    {
        if (values is null) throw new ArgumentNullException(parameterName);
        var array = values.ToArray();
        if (array.Any(static item => item is null))
        {
            throw new ArgumentException("Render target collections cannot contain null entries.", parameterName);
        }

        return new ReadOnlyCollection<RenderTargetDescriptor3D>(array);
    }
}
