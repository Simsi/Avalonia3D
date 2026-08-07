using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Rendering.Pipeline;

internal sealed class RenderTargetDescriptor3D
{
    private string _name = string.Empty;
    private RenderTargetFormat3D _format = RenderTargetFormat3D.Rgba8;
    private float _scale = 1f;

    public string Name
    {
        get => _name;
        init => _name = Guard3D.RequiredText(value, nameof(Name));
    }

    public RenderTargetFormat3D Format
    {
        get => _format;
        init => _format = Guard3D.Defined(value, nameof(Format));
    }

    public float Scale
    {
        get => _scale;
        init => _scale = Guard3D.Positive(value, nameof(Scale));
    }

    public bool IsDepth { get; init; }
    public bool IsTransient { get; init; } = true;
}
