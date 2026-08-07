using System;

namespace ThreeDEngine.Core.Rendering.Extensions;

public readonly record struct RenderExtensionResource3D(
    string Name,
    RenderExtensionResourceAccess3D Access,
    bool IsOptional = false)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Access != RenderExtensionResourceAccess3D.None;

    internal void Validate(string owner)
    {
        if (!IsValid) throw new InvalidOperationException($"Render extension '{owner}' contains an invalid resource declaration.");
        if ((Access & RenderExtensionResourceAccess3D.ColorAttachment) != 0 &&
            (Access & RenderExtensionResourceAccess3D.DepthAttachment) != 0)
            throw new InvalidOperationException($"Render extension '{owner}' resource '{Name}' cannot be both color and depth attachment.");
    }
}
