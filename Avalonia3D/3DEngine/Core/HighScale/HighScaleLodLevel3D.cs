namespace ThreeDEngine.Core.HighScale;

/// <summary>
/// Semantic LOD level for high scale templates. Detailed keeps every template part,
/// Simplified uses explicitly supplied simplified parts when available, Proxy uses a
/// cheap bounds proxy, Billboard is rendered through a proxy when no billboard pass is available,
/// and Culled is not submitted to the backend.
/// </summary>
public enum HighScaleLodLevel3D
{
    Detailed = 0,
    Simplified = 1,
    Proxy = 2,
    Billboard = 3,
    Culled = 4
}
