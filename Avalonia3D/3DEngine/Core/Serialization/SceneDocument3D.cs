using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Serialization;

public sealed class SceneDocument3D
{
    public const string FormatName = "Avalonia3D.Scene";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public string? Name { get; set; }
    public SceneCameraDocument3D Camera { get; set; } = new();
    public SceneAppearanceDocument3D Appearance { get; set; } = new();
    public List<SceneObjectDocument3D> Objects { get; set; } = new();
    public List<SceneLightDocument3D> Lights { get; set; } = new();
}

public sealed class SceneCameraDocument3D
{
    public Vector3Document3D Position { get; set; } = new(0f, 0f, 6f);
    public Vector3Document3D Target { get; set; } = new(0f, 0f, 0f);
    public Vector3Document3D Up { get; set; } = new(0f, 1f, 0f);
    public float FieldOfViewDegrees { get; set; } = 55f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 100f;
}

public sealed class SceneAppearanceDocument3D
{
    public ColorDocument3D Background { get; set; } = new(1f, 1f, 1f, 1f);
    public ColorDocument3D AmbientColor { get; set; } = new(1f, 1f, 1f, 1f);
    public float AmbientIntensity { get; set; } = 0.28f;
}

public sealed class SceneObjectDocument3D
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Vector3Document3D Position { get; set; }
    public Vector3Document3D RotationDegrees { get; set; }
    public Vector3Document3D Scale { get; set; } = new(1f, 1f, 1f);
    public bool IsVisible { get; set; } = true;
    public bool IsPickable { get; set; } = true;
    public SceneMaterialDocument3D Material { get; set; } = new();
    public Dictionary<string, float> Geometry { get; set; } = new(StringComparer.Ordinal);
    public string? AssetPath { get; set; }
}

public sealed class SceneMaterialDocument3D
{
    public ColorDocument3D BaseColor { get; set; } = new(1f, 1f, 1f, 1f);
    public ColorDocument3D SpecularColor { get; set; } = new(1f, 1f, 1f, 1f);
    public ColorDocument3D EmissiveColor { get; set; }
    public float Opacity { get; set; } = 1f;
    public float AmbientStrength { get; set; } = 1f;
    public float DiffuseStrength { get; set; } = 1f;
    public float Metallic { get; set; }
    public float Roughness { get; set; } = 1f;
    public float SpecularStrength { get; set; } = 0.35f;
    public float Shininess { get; set; } = 32f;
    public float AlphaCutoff { get; set; } = 0.5f;
    public int Lighting { get; set; }
    public int Surface { get; set; }
    public int CullMode { get; set; }
    public List<SceneTextureDocument3D> Textures { get; set; } = new();
    public SceneMaterialExtensionDocument3D? Extension { get; set; }
}

public sealed class SceneTextureDocument3D
{
    public string Slot { get; set; } = string.Empty;
    public string LogicalKey { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public string Base64Data { get; set; } = string.Empty;
    public float Strength { get; set; } = 1f;
}

public sealed class SceneMaterialExtensionDocument3D
{
    public string ExtensionId { get; set; } = string.Empty;
    public int MaterialType { get; set; }
    public string Base64Parameters { get; set; } = string.Empty;
    public List<SceneTextureDocument3D> Textures { get; set; } = new();
}

public sealed class SceneLightDocument3D
{
    public string Type { get; set; } = string.Empty;
    public Vector3Document3D Position { get; set; }
    public Vector3Document3D Direction { get; set; } = new(0f, -1f, 0f);
    public ColorDocument3D Color { get; set; } = new(1f, 1f, 1f, 1f);
    public float Intensity { get; set; } = 1f;
    public float Range { get; set; } = 10f;
    public float InnerConeDegrees { get; set; } = 18f;
    public float OuterConeDegrees { get; set; } = 32f;
    public bool IsEnabled { get; set; } = true;
}

public readonly record struct Vector3Document3D(float X, float Y, float Z);
public readonly record struct ColorDocument3D(float R, float G, float B, float A);
