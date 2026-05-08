using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Environment;

public sealed class Skybox3D
{
    private SkyboxMode3D _mode = SkyboxMode3D.None;
    private ColorRgba _topColor = new(0.28f, 0.45f, 0.72f, 1f);
    private ColorRgba _horizonColor = new(0.62f, 0.76f, 0.94f, 1f);
    private ColorRgba _bottomColor = new(0.82f, 0.86f, 0.90f, 1f);
    private float _intensity = 1f;
    private readonly string?[] _cubemapFaces = new string?[6];
    private readonly string?[] _cubemapTextureKeys = new string?[6];
    private readonly byte[]?[] _cubemapTextureData = new byte[6][];
    private readonly string?[] _cubemapTextureMimeTypes = new string?[6];
    private string? _equirectangularTextureKey;
    private byte[]? _equirectangularTextureData;
    private string? _equirectangularTextureMimeType;
    private int _environmentTextureVersion;

    public event EventHandler? Changed;

    public SkyboxMode3D Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            RaiseChanged();
        }
    }

    public ColorRgba TopColor
    {
        get => _topColor;
        set
        {
            if (_topColor.Equals(value)) return;
            _topColor = value;
            RaiseChanged();
        }
    }

    public ColorRgba HorizonColor
    {
        get => _horizonColor;
        set
        {
            if (_horizonColor.Equals(value)) return;
            _horizonColor = value;
            RaiseChanged();
        }
    }

    public ColorRgba BottomColor
    {
        get => _bottomColor;
        set
        {
            if (_bottomColor.Equals(value)) return;
            _bottomColor = value;
            RaiseChanged();
        }
    }

    public float Intensity
    {
        get => _intensity;
        set
        {
            var clamped = global::System.Math.Clamp(value, 0f, 8f);
            if (MathF.Abs(_intensity - clamped) < 0.0001f) return;
            _intensity = clamped;
            RaiseChanged();
        }
    }

    public IReadOnlyList<string?> CubemapFaces => _cubemapFaces;
    public IReadOnlyList<string?> CubemapTextureKeys => _cubemapTextureKeys;
    public IReadOnlyList<byte[]?> CubemapTextureData => _cubemapTextureData;
    public IReadOnlyList<string?> CubemapTextureMimeTypes => _cubemapTextureMimeTypes;
    public string? EquirectangularTextureKey => _equirectangularTextureKey;
    public byte[]? EquirectangularTextureData => _equirectangularTextureData;
    public string? EquirectangularTextureMimeType => _equirectangularTextureMimeType;
    public int EnvironmentTextureVersion => _environmentTextureVersion;
    public bool HasEquirectangularTexture => !string.IsNullOrWhiteSpace(_equirectangularTextureKey) && _equirectangularTextureData is { Length: > 0 };
    public bool HasCubemapTextures
    {
        get
        {
            for (var i = 0; i < _cubemapTextureKeys.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(_cubemapTextureKeys[i]) || _cubemapTextureData[i] is not { Length: > 0 }) return false;
            }
            return true;
        }
    }

    public void SetEquirectangularTexture(string textureKey, byte[] textureData, string? mimeType = null)
    {
        _equirectangularTextureKey = string.IsNullOrWhiteSpace(textureKey) ? null : textureKey;
        _equirectangularTextureData = textureData is { Length: > 0 } ? (byte[])textureData.Clone() : null;
        _equirectangularTextureMimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType;
        _environmentTextureVersion++;
        Mode = SkyboxMode3D.Equirectangular;
        RaiseChanged();
    }

    public void ClearEquirectangularTexture()
    {
        _equirectangularTextureKey = null;
        _equirectangularTextureData = null;
        _equirectangularTextureMimeType = null;
        _environmentTextureVersion++;
        RaiseChanged();
    }


    public void SetCubemapFaceTextures(
        string positiveXKey, byte[] positiveXData, string? positiveXMime,
        string negativeXKey, byte[] negativeXData, string? negativeXMime,
        string positiveYKey, byte[] positiveYData, string? positiveYMime,
        string negativeYKey, byte[] negativeYData, string? negativeYMime,
        string positiveZKey, byte[] positiveZData, string? positiveZMime,
        string negativeZKey, byte[] negativeZData, string? negativeZMime)
    {
        SetCubemapTexture(0, positiveXKey, positiveXData, positiveXMime);
        SetCubemapTexture(1, negativeXKey, negativeXData, negativeXMime);
        SetCubemapTexture(2, positiveYKey, positiveYData, positiveYMime);
        SetCubemapTexture(3, negativeYKey, negativeYData, negativeYMime);
        SetCubemapTexture(4, positiveZKey, positiveZData, positiveZMime);
        SetCubemapTexture(5, negativeZKey, negativeZData, negativeZMime);
        _environmentTextureVersion++;
        Mode = SkyboxMode3D.Cubemap;
        RaiseChanged();
    }

    private void SetCubemapTexture(int index, string key, byte[] data, string? mimeType)
    {
        _cubemapTextureKeys[index] = string.IsNullOrWhiteSpace(key) ? null : key;
        _cubemapTextureData[index] = data is { Length: > 0 } ? (byte[])data.Clone() : null;
        _cubemapTextureMimeTypes[index] = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType;
    }

    public void SetCubemapFaces(string? positiveX, string? negativeX, string? positiveY, string? negativeY, string? positiveZ, string? negativeZ)
    {
        _cubemapFaces[0] = positiveX;
        _cubemapFaces[1] = negativeX;
        _cubemapFaces[2] = positiveY;
        _cubemapFaces[3] = negativeY;
        _cubemapFaces[4] = positiveZ;
        _cubemapFaces[5] = negativeZ;
        RaiseChanged();
    }

    public void ClearCubemapFaces()
    {
        Array.Clear(_cubemapFaces, 0, _cubemapFaces.Length);
        Array.Clear(_cubemapTextureKeys, 0, _cubemapTextureKeys.Length);
        Array.Clear(_cubemapTextureData, 0, _cubemapTextureData.Length);
        Array.Clear(_cubemapTextureMimeTypes, 0, _cubemapTextureMimeTypes.Length);
        _environmentTextureVersion++;
        RaiseChanged();
    }

    public bool HasCubemapFaces
    {
        get
        {
            for (var i = 0; i < _cubemapFaces.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(_cubemapFaces[i])) return false;
            }
            return true;
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
