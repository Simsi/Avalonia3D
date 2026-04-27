namespace ThreeDEngine.Core.HighScale;

/// <summary>
/// Semantic LOD level for high scale templates. Detailed keeps every template part,
/// Simplified uses explicitly supplied simplified parts when available, Proxy uses a
/// cheap bounds proxy, and Billboard is reserved for overlay/marker renderers.
/// </summary>
public enum HighScaleLodLevel3D
{
    Detailed = 0,
    Simplified = 1,
    Proxy = 2,
    Billboard = 3
}
