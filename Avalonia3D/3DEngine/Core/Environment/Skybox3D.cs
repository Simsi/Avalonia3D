using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Resources;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Environment;

public sealed class Skybox3D
{
    private SkyboxMode3D _mode = SkyboxMode3D.None;
    private ColorRgba _topColor = new(0.28f, 0.45f, 0.72f, 1f);
    private ColorRgba _horizonColor = new(0.62f, 0.76f, 0.94f, 1f);
    private ColorRgba _bottomColor = new(0.82f, 0.86f, 0.90f, 1f);
    private float _intensity = 1f;
    private readonly TextureResource3D?[] _cubemapTextures = new TextureResource3D?[6];
    private readonly ReadOnlyCollection<TextureResource3D?> _cubemapTexturesView;
    private TextureResource3D? _equirectangularTexture;
    private int _environmentTextureVersion;

    public Skybox3D()
    {
        _cubemapTexturesView = Array.AsReadOnly(_cubemapTextures);
    }

    public event EventHandler? Changed;
    internal Func<SceneAccessLease3D>? MutationScopeRequested { get; set; }

    public SkyboxMode3D Mode
    {
        get => _mode;
        set
        {
            using var mutation = EnterMutationScope();
            value = Guard3D.Defined(value, nameof(value));
            if (value == SkyboxMode3D.Equirectangular && !HasEquirectangularTexture)
                throw new InvalidOperationException("Equirectangular mode requires a complete immutable texture resource.");
            if (value == SkyboxMode3D.Cubemap && !HasCubemapTextures)
                throw new InvalidOperationException("Cubemap mode requires six complete immutable texture resources.");
            if (_mode == value) return;
            _mode = value;
            RaiseChanged();
        }
    }

    public ColorRgba TopColor
    {
        get => _topColor;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Color(value, nameof(value)); if (_topColor.Equals(value)) return; _topColor = value; RaiseChanged(); }
    }

    public ColorRgba HorizonColor
    {
        get => _horizonColor;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Color(value, nameof(value)); if (_horizonColor.Equals(value)) return; _horizonColor = value; RaiseChanged(); }
    }

    public ColorRgba BottomColor
    {
        get => _bottomColor;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Color(value, nameof(value)); if (_bottomColor.Equals(value)) return; _bottomColor = value; RaiseChanged(); }
    }

    public float Intensity
    {
        get => _intensity;
        set
        {
            using var mutation = EnterMutationScope();
            var validated = Guard3D.Range(value, 0f, 8f, nameof(value));
            if (MathF.Abs(_intensity - validated) < 0.0001f) return;
            _intensity = validated;
            RaiseChanged();
        }
    }

    public IReadOnlyList<TextureResource3D?> CubemapTextures => _cubemapTexturesView;

    public IReadOnlyList<string?> CubemapTextureKeys
    {
        get
        {
            var result = new string?[6];
            for (var i = 0; i < result.Length; i++) result[i] = _cubemapTextures[i]?.LogicalKey;
            return Array.AsReadOnly(result);
        }
    }

    public IReadOnlyList<string?> CubemapTextureResourceKeys
    {
        get
        {
            var result = new string?[6];
            for (var i = 0; i < result.Length; i++) result[i] = _cubemapTextures[i]?.ResourceKey;
            return Array.AsReadOnly(result);
        }
    }

    public IReadOnlyList<byte[]?> CubemapTextureData
    {
        get
        {
            var result = new byte[]?[6];
            for (var i = 0; i < result.Length; i++) result[i] = _cubemapTextures[i]?.CopyEncodedData();
            return Array.AsReadOnly(result);
        }
    }

    public IReadOnlyList<string?> CubemapTextureMimeTypes
    {
        get
        {
            var result = new string?[6];
            for (var i = 0; i < result.Length; i++) result[i] = _cubemapTextures[i]?.MimeType;
            return Array.AsReadOnly(result);
        }
    }

    public TextureResource3D? EquirectangularTexture => _equirectangularTexture;
    public string? EquirectangularTextureKey => _equirectangularTexture?.LogicalKey;
    public string? EquirectangularTextureResourceKey => _equirectangularTexture?.ResourceKey;
    public byte[]? EquirectangularTextureData => _equirectangularTexture?.CopyEncodedData();
    public string? EquirectangularTextureMimeType => _equirectangularTexture?.MimeType;
    public int EnvironmentTextureVersion => _environmentTextureVersion;
    public bool HasEquirectangularTexture => _equirectangularTexture is not null;
    internal TextureResource3D? EquirectangularTextureInternal => _equirectangularTexture;
    internal IReadOnlyList<TextureResource3D?> CubemapTexturesInternal => _cubemapTexturesView;

    public bool HasCubemapTextures
    {
        get
        {
            for (var i = 0; i < _cubemapTextures.Length; i++) if (_cubemapTextures[i] is null) return false;
            return true;
        }
    }

    public void SetEquirectangularTexture(TextureResource3D texture)
    {
        using var mutation = EnterMutationScope();
        ArgumentNullException.ThrowIfNull(texture);
        if (TextureSlotEquals(_equirectangularTexture, texture) && _mode == SkyboxMode3D.Equirectangular) return;
        _equirectangularTexture = texture;
        unchecked { _environmentTextureVersion++; }
        _mode = SkyboxMode3D.Equirectangular;
        RaiseChanged();
    }

    public void SetEquirectangularTexture(string textureKey, byte[] textureData, string? mimeType = null)
        => SetEquirectangularTexture(TextureResource3D.Create(textureKey, textureData, mimeType));

    public void ClearEquirectangularTexture()
    {
        using var mutation = EnterMutationScope();
        if (_equirectangularTexture is null) return;
        _equirectangularTexture = null;
        if (_mode == SkyboxMode3D.Equirectangular) _mode = SkyboxMode3D.None;
        unchecked { _environmentTextureVersion++; }
        RaiseChanged();
    }

    public void SetCubemapFaceTextures(
        TextureResource3D positiveX,
        TextureResource3D negativeX,
        TextureResource3D positiveY,
        TextureResource3D negativeY,
        TextureResource3D positiveZ,
        TextureResource3D negativeZ)
    {
        using var mutation = EnterMutationScope();
        var textures = new[] { positiveX, negativeX, positiveY, negativeY, positiveZ, negativeZ };
        for (var i = 0; i < textures.Length; i++) ArgumentNullException.ThrowIfNull(textures[i], $"cubemapTexture[{i}]");

        var changed = _mode != SkyboxMode3D.Cubemap;
        for (var i = 0; i < 6; i++) changed |= !TextureSlotEquals(_cubemapTextures[i], textures[i]);
        if (!changed) return;

        Array.Copy(textures, _cubemapTextures, textures.Length);
        unchecked { _environmentTextureVersion++; }
        _mode = SkyboxMode3D.Cubemap;
        RaiseChanged();
    }

    public void SetCubemapFaceTextures(
        string positiveXKey, byte[] positiveXData, string? positiveXMime,
        string negativeXKey, byte[] negativeXData, string? negativeXMime,
        string positiveYKey, byte[] positiveYData, string? positiveYMime,
        string negativeYKey, byte[] negativeYData, string? negativeYMime,
        string positiveZKey, byte[] positiveZData, string? positiveZMime,
        string negativeZKey, byte[] negativeZData, string? negativeZMime)
        => SetCubemapFaceTextures(
            TextureResource3D.Create(positiveXKey, positiveXData, positiveXMime),
            TextureResource3D.Create(negativeXKey, negativeXData, negativeXMime),
            TextureResource3D.Create(positiveYKey, positiveYData, positiveYMime),
            TextureResource3D.Create(negativeYKey, negativeYData, negativeYMime),
            TextureResource3D.Create(positiveZKey, positiveZData, positiveZMime),
            TextureResource3D.Create(negativeZKey, negativeZData, negativeZMime));

    public void ClearCubemapTextures()
    {
        using var mutation = EnterMutationScope();
        if (!HasAny(_cubemapTextures)) return;
        Array.Clear(_cubemapTextures, 0, _cubemapTextures.Length);
        if (_mode == SkyboxMode3D.Cubemap) _mode = SkyboxMode3D.None;
        unchecked { _environmentTextureVersion++; }
        RaiseChanged();
    }

    private static bool HasAny(TextureResource3D?[] values)
    {
        for (var i = 0; i < values.Length; i++) if (values[i] is not null) return true;
        return false;
    }

    private static bool TextureSlotEquals(TextureResource3D? left, TextureResource3D right)
        => left is not null
           && left.Equals(right)
           && string.Equals(left.LogicalKey, right.LogicalKey, StringComparison.Ordinal)
           && string.Equals(left.MimeType, right.MimeType, StringComparison.Ordinal);

    private SceneAccessLease3D EnterMutationScope()
        => MutationScopeRequested?.Invoke() ?? default;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
