namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Stack-only, allocation-free scene transaction token. The token owns one recursive scene
/// write lease for its complete lifetime. Transactions are strictly LIFO; disposing a copied
/// token or closing an outer scope before an inner scope is rejected.
/// </summary>
public readonly ref struct SceneUpdateTransaction3D
{
    private readonly Scene3D? _scene;
    private readonly long _token;
    private readonly SceneAccessLease3D _writeLease;

    internal SceneUpdateTransaction3D(Scene3D scene, long token, SceneAccessLease3D writeLease)
    {
        _scene = scene;
        _token = token;
        _writeLease = writeLease;
    }

    public void Dispose()
    {
        if (_scene is null) return;
        _scene.EndUpdateTransaction(_token, _writeLease);
    }
}
