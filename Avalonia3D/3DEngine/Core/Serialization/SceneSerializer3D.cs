using System;
using System.Buffers;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Assets.Streaming;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Resources;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Serialization;

public static class SceneSerializer3D
{
    public static SceneDocument3D Capture(Scene3D scene, SceneSerializerOptions3D? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options ??= new SceneSerializerOptions3D();
        ValidateOptions(options);
        using var read = scene.EnterRenderReadScope();
        if (scene.Objects.Count > options.MaximumObjects) throw new InvalidOperationException($"Scene contains {scene.Objects.Count} roots; serializer limit is {options.MaximumObjects}.");
        var document = new SceneDocument3D
        {
            Camera = new SceneCameraDocument3D
            {
                Position = ToDocument(scene.Camera.Position),
                Target = ToDocument(scene.Camera.Target),
                Up = ToDocument(scene.Camera.Up),
                FieldOfViewDegrees = scene.Camera.FieldOfViewDegrees,
                NearPlane = scene.Camera.NearPlane,
                FarPlane = scene.Camera.FarPlane
            },
            Appearance = new SceneAppearanceDocument3D
            {
                Background = ToDocument(scene.BackgroundColor),
                AmbientColor = ToDocument(scene.AmbientLightColor),
                AmbientIntensity = scene.AmbientLightIntensity
            }
        };

        for (var i = 0; i < scene.Objects.Count; i++) document.Objects.Add(CaptureObject(scene.Objects[i], options));
        for (var i = 0; i < scene.Lights.Count; i++) document.Lights.Add(Capture(scene.Lights[i]));
        for (var i = 0; i < scene.PointLights.Count; i++) document.Lights.Add(Capture(scene.PointLights[i]));
        for (var i = 0; i < scene.SpotLights.Count; i++) document.Lights.Add(Capture(scene.SpotLights[i]));
        if (document.Lights.Count > options.MaximumLights) throw new InvalidOperationException($"Scene contains {document.Lights.Count} lights; serializer limit is {options.MaximumLights}.");
        return ValidateDocument(document, migrations: null, options);
    }

    public static string Serialize(Scene3D scene, SceneSerializerOptions3D? options = null)
    {
        options ??= new SceneSerializerOptions3D();
        var jsonOptions = new JsonSerializerOptions { WriteIndented = options.WriteIndented, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(Capture(scene, options), jsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > options.MaximumDocumentBytes)
            throw new InvalidOperationException($"Serialized scene exceeds configured limit {options.MaximumDocumentBytes} bytes.");
        return json;
    }

    public static async ValueTask SerializeAsync(
        Scene3D scene,
        Stream output,
        SceneSerializerOptions3D? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        options ??= new SceneSerializerOptions3D();
        ValidateOptions(options);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = options.WriteIndented, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await using var bounded = new BoundedWriteStream3D(output, options.MaximumDocumentBytes);
        await JsonSerializer.SerializeAsync(bounded, Capture(scene, options), jsonOptions, cancellationToken).ConfigureAwait(false);
        await bounded.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static SceneDocument3D Deserialize(
        string json,
        SceneDocumentMigrationRegistry3D? migrations = null,
        SceneSerializerOptions3D? options = null)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Scene JSON cannot be empty.", nameof(json));
        options ??= new SceneSerializerOptions3D();
        ValidateOptions(options);
        if (Encoding.UTF8.GetByteCount(json) > options.MaximumDocumentBytes)
            throw new InvalidDataException($"Scene JSON exceeds configured limit {options.MaximumDocumentBytes} bytes.");
        var document = JsonSerializer.Deserialize<SceneDocument3D>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Scene JSON deserialized to null.");
        return ValidateDocument(document, migrations, options);
    }

    public static async ValueTask<SceneDocument3D> DeserializeAsync(
        Stream input,
        SceneDocumentMigrationRegistry3D? migrations = null,
        SceneSerializerOptions3D? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        options ??= new SceneSerializerOptions3D();
        ValidateOptions(options);
        await using var bounded = await ReadBoundedDocumentAsync(input, options.MaximumDocumentBytes, cancellationToken).ConfigureAwait(false);
        var document = await JsonSerializer.DeserializeAsync<SceneDocument3D>(bounded, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Scene JSON deserialized to null.");
        return ValidateDocument(document, migrations, options);
    }

    public static async ValueTask<Scene3D> RestoreAsync(
        ThreeDEngine.Core.Hosting.Engine3D engine,
        SceneDocument3D document,
        SceneSerializerOptions3D? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        options ??= new SceneSerializerOptions3D();
        document = ValidateDocument(document, migrations: null, options: options);
        var restoredObjects = new Object3D[document.Objects.Count];
        for (var i = 0; i < document.Objects.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = document.Objects[i];
            Object3D obj;
            if (StringComparer.Ordinal.Equals(item.Type, "ImportedModel"))
            {
                if (string.IsNullOrWhiteSpace(item.AssetPath)) throw new InvalidDataException($"Imported model '{item.Name}' has no assetPath.");
                if (!StringComparer.Ordinal.Equals(item.AssetPath, item.AssetPath.Trim())) throw new InvalidDataException($"Imported model '{item.Name}' assetPath contains surrounding whitespace.");
                var asset = await engine.Assets.LoadModelAsync(item.AssetPath, priority: AssetLoadPriority3D.High, cancellationToken: cancellationToken).ConfigureAwait(false);
                obj = new ImportedModel3D(asset);
            }
            else
            {
                obj = CreatePrimitive(item);
            }
            ApplyCommon(obj, item);
            if (obj.Material.ShaderExtension is { } customMaterial) engine.MaterialExtensions.Validate(customMaterial);
            restoredObjects[i] = obj;
        }

        var scene = engine.CreateScene();
        try
        {
            using var update = scene.BeginUpdate();
            scene.Camera.SetPose(ToVector(document.Camera.Position), ToVector(document.Camera.Target), ToVector(document.Camera.Up));
            scene.Camera.FieldOfViewDegrees = document.Camera.FieldOfViewDegrees;
            scene.Camera.NearPlane = document.Camera.NearPlane;
            scene.Camera.FarPlane = document.Camera.FarPlane;
            scene.BackgroundColor = ToColor(document.Appearance.Background);
            scene.AmbientLightColor = ToColor(document.Appearance.AmbientColor);
            scene.AmbientLightIntensity = document.Appearance.AmbientIntensity;
            RestoreLights(scene, document);
            for (var i = 0; i < restoredObjects.Length; i++) scene.Add(restoredObjects[i]);
            return scene;
        }
        catch
        {
            scene.Dispose();
            throw;
        }
    }

    private static SceneObjectDocument3D CaptureObject(Object3D obj, SceneSerializerOptions3D options)
    {
        var document = new SceneObjectDocument3D
        {
            Name = obj.Name,
            Position = ToDocument(obj.Position),
            RotationDegrees = ToDocument(obj.RotationDegrees),
            Scale = ToDocument(obj.Scale),
            IsVisible = obj.IsVisible,
            IsPickable = obj.IsPickable,
            Material = CaptureMaterial(obj.Material, options)
        };
        switch (obj)
        {
            case ImportedModel3D model:
                document.Type = "ImportedModel";
                document.AssetPath = string.IsNullOrWhiteSpace(model.Asset.SourcePath)
                    ? throw new InvalidOperationException($"Imported model '{obj.Name}' has no stable source path and cannot be serialized losslessly.")
                    : model.Asset.SourcePath;
                break;
            case Box3D box:
                document.Type = "Box"; Set(document, "width", box.Width); Set(document, "height", box.Height); Set(document, "depth", box.Depth); break;
            case Rectangle3D box:
                document.Type = "Rectangle"; Set(document, "width", box.Width); Set(document, "height", box.Height); Set(document, "depth", box.Depth); break;
            case Sphere3D sphere:
                document.Type = "Sphere"; Set(document, "radius", sphere.Radius); Set(document, "segments", sphere.Segments); Set(document, "rings", sphere.Rings); break;
            case Plane3D plane:
                document.Type = "Plane"; Set(document, "width", plane.Width); Set(document, "height", plane.Height); Set(document, "segmentsX", plane.SegmentsX); Set(document, "segmentsY", plane.SegmentsY); break;
            case Cylinder3D cylinder:
                document.Type = "Cylinder"; Set(document, "radius", cylinder.Radius); Set(document, "height", cylinder.Height); Set(document, "segments", cylinder.Segments); break;
            case Cone3D cone:
                document.Type = "Cone"; Set(document, "radius", cone.Radius); Set(document, "height", cone.Height); Set(document, "segments", cone.Segments); break;
            case Ellipse3D ellipse:
                document.Type = "Ellipse"; Set(document, "width", ellipse.Width); Set(document, "height", ellipse.Height); Set(document, "depth", ellipse.Depth); Set(document, "segments", ellipse.Segments); break;
            case Billboard3D billboard:
                document.Type = "Billboard"; Set(document, "width", billboard.Width); Set(document, "height", billboard.Height); break;
            default:
                throw new NotSupportedException($"Scene serialization does not support root object type '{obj.GetType().FullName}'. Register a versioned application serializer instead of silently dropping it.");
        }
        return document;
    }

    private static SceneMaterialDocument3D CaptureMaterial(Material3D material, SceneSerializerOptions3D options)
    {
        var document = new SceneMaterialDocument3D
        {
            BaseColor = ToDocument(material.BaseColor), SpecularColor = ToDocument(material.SpecularColor), EmissiveColor = ToDocument(material.EmissiveColor),
            Opacity = material.Opacity, AmbientStrength = material.AmbientStrength, DiffuseStrength = material.DiffuseStrength,
            Metallic = material.Metallic, Roughness = material.Roughness,
            SpecularStrength = material.SpecularStrength, Shininess = material.Shininess,
            AlphaCutoff = material.AlphaCutoff,
            Lighting = (int)material.Lighting, Surface = (int)material.Surface, CullMode = (int)material.CullMode
        };
        if (!options.IncludeEmbeddedTextures && HasEncodedTextures(material))
            throw new InvalidOperationException("The scene contains encoded material textures, but IncludeEmbeddedTextures is false and no external texture-reference serializer is configured. Silent texture loss is prohibited.");
        if (options.IncludeEmbeddedTextures)
        {
            AddTexture(document, "baseColor", material.BaseColorTextureResourceKey, material.BaseColorTextureMimeType, material.BaseColorTextureData, 1f, options);
            AddTexture(document, "normal", material.NormalMapTextureResourceKey, material.NormalMapTextureMimeType, material.NormalMapTextureData, material.NormalMapStrength, options);
            AddTexture(document, "metallicRoughness", material.MetallicRoughnessTextureResourceKey, material.MetallicRoughnessTextureMimeType, material.MetallicRoughnessTextureData, 1f, options);
            AddTexture(document, "emissive", material.EmissiveTextureResourceKey, material.EmissiveTextureMimeType, material.EmissiveTextureData, 1f, options);
        }
        if (material.ShaderExtension is { } extension)
        {
            document.Extension = new SceneMaterialExtensionDocument3D
            {
                ExtensionId = extension.ExtensionId,
                MaterialType = extension.MaterialType,
                Base64Parameters = Convert.ToBase64String(extension.Parameters.Span)
            };
            for (var i = 0; i < extension.Textures.Count; i++)
            {
                var texture = extension.Textures[i];
                AddTexture(document.Extension.Textures, "extension", texture.ResourceKey, texture.MimeType, texture.CopyEncodedData(), 1f, options);
            }
        }
        return document;
    }

    private static void ApplyMaterial(Material3D material, SceneMaterialDocument3D source)
    {
        material.BaseColor = ToColor(source.BaseColor); material.SpecularColor = ToColor(source.SpecularColor); material.EmissiveColor = ToColor(source.EmissiveColor);
        material.Opacity = source.Opacity; material.AmbientStrength = source.AmbientStrength; material.DiffuseStrength = source.DiffuseStrength;
        material.Metallic = source.Metallic; material.Roughness = source.Roughness;
        material.SpecularStrength = source.SpecularStrength; material.Shininess = source.Shininess;
        material.AlphaCutoff = source.AlphaCutoff;
        material.Lighting = (LightingMode)source.Lighting; material.Surface = (SurfaceMode)source.Surface; material.CullMode = (CullMode)source.CullMode;
        for (var i = 0; i < source.Textures.Count; i++)
        {
            var texture = source.Textures[i];
            var data = Convert.FromBase64String(texture.Base64Data);
            var resource = TextureResource3D.Create(texture.LogicalKey, data, texture.MimeType);
            switch (texture.Slot)
            {
                case "baseColor": material.SetBaseColorTexture(resource); break;
                case "normal": material.SetNormalMapTexture(resource, texture.Strength); break;
                case "metallicRoughness": material.SetMetallicRoughnessTexture(resource); break;
                case "emissive": material.SetEmissiveTexture(resource); break;
                default: throw new InvalidDataException($"Unknown material texture slot '{texture.Slot}'.");
            }
        }
        if (source.Extension is { } extension)
        {
            var textures = new TextureResource3D[extension.Textures.Count];
            for (var i = 0; i < textures.Length; i++)
            {
                var texture = extension.Textures[i];
                textures[i] = TextureResource3D.Create(texture.LogicalKey, Convert.FromBase64String(texture.Base64Data), texture.MimeType);
            }
            material.ShaderExtension = new MaterialShaderExtension3D(extension.ExtensionId, extension.MaterialType, Convert.FromBase64String(extension.Base64Parameters), textures);
        }
    }

    private static Object3D CreatePrimitive(SceneObjectDocument3D item)
        => item.Type switch
        {
            "Box" => new Box3D { Width = Get(item, "width"), Height = Get(item, "height"), Depth = Get(item, "depth") },
            "Rectangle" => new Rectangle3D { Width = Get(item, "width"), Height = Get(item, "height"), Depth = Get(item, "depth") },
            "Sphere" => new Sphere3D { Radius = Get(item, "radius"), Segments = GetInt(item, "segments"), Rings = GetInt(item, "rings") },
            "Plane" => new Plane3D { Width = Get(item, "width"), Height = Get(item, "height"), SegmentsX = GetInt(item, "segmentsX"), SegmentsY = GetInt(item, "segmentsY") },
            "Cylinder" => new Cylinder3D { Radius = Get(item, "radius"), Height = Get(item, "height"), Segments = GetInt(item, "segments") },
            "Cone" => new Cone3D { Radius = Get(item, "radius"), Height = Get(item, "height"), Segments = GetInt(item, "segments") },
            "Ellipse" => new Ellipse3D { Width = Get(item, "width"), Height = Get(item, "height"), Depth = Get(item, "depth"), Segments = GetInt(item, "segments") },
            "Billboard" => new Billboard3D { Width = Get(item, "width"), Height = Get(item, "height") },
            _ => throw new NotSupportedException($"Unknown scene object type '{item.Type}'.")
        };

    private static void ApplyCommon(Object3D obj, SceneObjectDocument3D source)
    {
        obj.Name = source.Name; obj.Position = ToVector(source.Position); obj.RotationDegrees = ToVector(source.RotationDegrees); obj.Scale = ToVector(source.Scale);
        obj.IsVisible = source.IsVisible; obj.IsPickable = source.IsPickable; ApplyMaterial(obj.Material, source.Material);
    }

    private static void RestoreLights(Scene3D scene, SceneDocument3D document)
    {
        for (var i = 0; i < document.Lights.Count; i++)
        {
            var item = document.Lights[i];
            switch (item.Type)
            {
                case "Directional": scene.AddLight(new DirectionalLight3D { Direction = ToVector(item.Direction), Color = ToColor(item.Color), Intensity = item.Intensity, IsEnabled = item.IsEnabled }); break;
                case "Point": scene.AddLight(new PointLight3D { Position = ToVector(item.Position), Color = ToColor(item.Color), Intensity = item.Intensity, Range = item.Range, IsEnabled = item.IsEnabled }); break;
                case "Spot": var spot = new SpotLight3D { Position = ToVector(item.Position), Direction = ToVector(item.Direction), Color = ToColor(item.Color), Intensity = item.Intensity, Range = item.Range, IsEnabled = item.IsEnabled }; spot.SetCone(item.InnerConeDegrees, item.OuterConeDegrees); scene.AddLight(spot); break;
                default: throw new InvalidDataException($"Unknown scene light type '{item.Type}'.");
            }
        }
    }

    private static SceneLightDocument3D Capture(DirectionalLight3D light) => new() { Type = "Directional", Direction = ToDocument(light.Direction), Color = ToDocument(light.Color), Intensity = light.Intensity, IsEnabled = light.IsEnabled };
    private static SceneLightDocument3D Capture(PointLight3D light) => new() { Type = "Point", Position = ToDocument(light.Position), Color = ToDocument(light.Color), Intensity = light.Intensity, Range = light.Range, IsEnabled = light.IsEnabled };
    private static SceneLightDocument3D Capture(SpotLight3D light) => new() { Type = "Spot", Position = ToDocument(light.Position), Direction = ToDocument(light.Direction), Color = ToDocument(light.Color), Intensity = light.Intensity, Range = light.Range, InnerConeDegrees = light.InnerConeDegrees, OuterConeDegrees = light.OuterConeDegrees, IsEnabled = light.IsEnabled };

    private static SceneDocument3D ValidateDocument(SceneDocument3D document, SceneDocumentMigrationRegistry3D? migrations, SceneSerializerOptions3D options)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!StringComparer.Ordinal.Equals(document.Format, SceneDocument3D.FormatName)) throw new InvalidDataException($"Unsupported scene format '{document.Format}'.");
        if (document.Version <= 0) throw new InvalidDataException($"Scene version {document.Version} is invalid; versions must be positive.");
        if (document.Version > SceneDocument3D.CurrentVersion) throw new NotSupportedException($"Scene version {document.Version} is newer than supported version {SceneDocument3D.CurrentVersion}.");
        if (document.Version < SceneDocument3D.CurrentVersion) document = migrations?.Upgrade(document) ?? throw new NotSupportedException($"Scene version {document.Version} requires an explicit migration registry.");
        if (document.Version != SceneDocument3D.CurrentVersion) throw new InvalidDataException($"Scene migration returned version {document.Version}; expected {SceneDocument3D.CurrentVersion}.");
        if (document.Camera is null) throw new InvalidDataException("Scene document camera is missing.");
        if (document.Appearance is null) throw new InvalidDataException("Scene document appearance is missing.");
        if (document.Objects is null) throw new InvalidDataException("Scene document object list is missing.");
        if (document.Lights is null) throw new InvalidDataException("Scene document light list is missing.");
        if (document.Objects.Count > options.MaximumObjects) throw new InvalidDataException($"Scene object count exceeds configured limit {options.MaximumObjects}.");
        if (document.Lights.Count > options.MaximumLights) throw new InvalidDataException($"Scene light count exceeds configured limit {options.MaximumLights}.");
        ValidateVector(document.Camera.Position, "camera.position");
        ValidateVector(document.Camera.Target, "camera.target");
        ValidateVector(document.Camera.Up, "camera.up");
        var cameraPosition = ToVector(document.Camera.Position);
        var cameraTarget = ToVector(document.Camera.Target);
        var cameraUp = ToVector(document.Camera.Up);
        var cameraDirection = cameraTarget - cameraPosition;
        var cameraDirectionLengthSquared = cameraDirection.LengthSquared();
        var cameraUpLengthSquared = cameraUp.LengthSquared();
        var cameraCrossLengthSquared = Vector3.Cross(cameraDirection, cameraUp).LengthSquared();
        if (!float.IsFinite(cameraDirectionLengthSquared) || cameraDirectionLengthSquared < 0.000001f) throw new InvalidDataException("Scene camera position and target must differ and remain within a finite numeric range.");
        if (!float.IsFinite(cameraUpLengthSquared) || cameraUpLengthSquared < 0.000001f) throw new InvalidDataException("Scene camera up vector must be finite and non-zero.");
        if (!float.IsFinite(cameraCrossLengthSquared) || cameraCrossLengthSquared < 0.000001f) throw new InvalidDataException("Scene camera up vector cannot be parallel to its view direction or overflow the numeric range.");
        ValidateFinite(document.Camera.FieldOfViewDegrees, "camera.fieldOfViewDegrees");
        ValidateFinite(document.Camera.NearPlane, "camera.nearPlane");
        ValidateFinite(document.Camera.FarPlane, "camera.farPlane");
        if (document.Camera.FieldOfViewDegrees < 10f || document.Camera.FieldOfViewDegrees > 120f) throw new InvalidDataException("Scene camera field of view must be between 10 and 120 degrees.");
        if (document.Camera.NearPlane < 0.001f || document.Camera.NearPlane > 10f || document.Camera.FarPlane <= document.Camera.NearPlane) throw new InvalidDataException("Scene camera clip planes are invalid.");
        ValidateColor(document.Appearance.Background, "appearance.background");
        ValidateColor(document.Appearance.AmbientColor, "appearance.ambientColor");
        ValidateFinite(document.Appearance.AmbientIntensity, "appearance.ambientIntensity");
        if (document.Appearance.AmbientIntensity < 0f) throw new InvalidDataException("Scene ambient intensity cannot be negative.");

        long textureBytes = 0;
        long extensionParameterBytes = 0;
        for (var i = 0; i < document.Objects.Count; i++)
        {
            var item = document.Objects[i] ?? throw new InvalidDataException($"Scene object {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Type)) throw new InvalidDataException($"Scene object {i} has no type.");
            if (!StringComparer.Ordinal.Equals(item.Type, item.Type.Trim())) throw new InvalidDataException($"Scene object {i} type contains surrounding whitespace.");
            item.Name ??= string.Empty;
            if (item.Geometry is null) throw new InvalidDataException($"Scene object '{item.Name}' geometry dictionary is missing.");
            if (item.Material is null) throw new InvalidDataException($"Scene object '{item.Name}' material is missing.");
            if (item.Material.Textures is null) throw new InvalidDataException($"Scene object '{item.Name}' material texture list is missing.");
            ValidateVector(item.Position, $"object[{i}].position");
            ValidateVector(item.RotationDegrees, $"object[{i}].rotationDegrees");
            ValidateVector(item.Scale, $"object[{i}].scale");
            if (item.Scale.X == 0f || item.Scale.Y == 0f || item.Scale.Z == 0f) throw new InvalidDataException($"Scene object '{item.Name}' scale components must be non-zero.");
            foreach (var geometryValue in item.Geometry) ValidateFinite(geometryValue.Value, $"object[{i}].geometry.{geometryValue.Key}");
            ValidateGeometry(item);
            ValidateMaterial(item.Material, $"object[{i}].material");
            textureBytes = checked(textureBytes + ValidateTextures(item.Material.Textures, $"object '{item.Name}' material", options.MaximumEmbeddedTextureBytes - textureBytes, extensionTextures: false));
            if (item.Material.Extension is { } extension)
            {
                if (string.IsNullOrWhiteSpace(extension.ExtensionId)) throw new InvalidDataException($"Scene object '{item.Name}' material extension has no id.");
                if (!StringComparer.Ordinal.Equals(extension.ExtensionId, extension.ExtensionId.Trim())) throw new InvalidDataException($"Scene object '{item.Name}' material extension id contains surrounding whitespace.");
                if (extension.MaterialType < 0) throw new InvalidDataException($"Scene object '{item.Name}' material extension type is negative.");
                extension.Base64Parameters ??= string.Empty;
                extensionParameterBytes = checked(extensionParameterBytes + ValidateBase64(
                    extension.Base64Parameters,
                    $"object '{item.Name}' material extension parameters",
                    options.MaximumExtensionParameterBytes - extensionParameterBytes));
                if (extension.Textures is null) throw new InvalidDataException($"Scene object '{item.Name}' material extension texture list is missing.");
                textureBytes = checked(textureBytes + ValidateTextures(extension.Textures, $"object '{item.Name}' material extension", options.MaximumEmbeddedTextureBytes - textureBytes, extensionTextures: true));
            }
        }
        if (textureBytes > options.MaximumEmbeddedTextureBytes) throw new InvalidDataException($"Embedded texture payload exceeds configured limit {options.MaximumEmbeddedTextureBytes} bytes.");
        for (var i = 0; i < document.Lights.Count; i++)
        {
            var light = document.Lights[i] ?? throw new InvalidDataException($"Scene light {i} is null.");
            if (light.Type is not ("Directional" or "Point" or "Spot")) throw new InvalidDataException($"Unknown scene light type '{light.Type}'.");
            ValidateVector(light.Position, $"light[{i}].position");
            ValidateVector(light.Direction, $"light[{i}].direction");
            ValidateColor(light.Color, $"light[{i}].color");
            ValidateFinite(light.Intensity, $"light[{i}].intensity");
            ValidateFinite(light.Range, $"light[{i}].range");
            ValidateFinite(light.InnerConeDegrees, $"light[{i}].innerConeDegrees");
            ValidateFinite(light.OuterConeDegrees, $"light[{i}].outerConeDegrees");
            if (light.Intensity < 0f) throw new InvalidDataException($"Scene light {i} intensity cannot be negative.");
            if (light.Type is "Point" or "Spot" && light.Range <= 0f) throw new InvalidDataException($"Scene light {i} range must be positive.");
            if (light.Type is "Directional" or "Spot")
            {
                var directionLengthSquared = ToVector(light.Direction).LengthSquared();
                if (!float.IsFinite(directionLengthSquared) || directionLengthSquared < 0.000001f) throw new InvalidDataException($"Scene light {i} direction must be finite and non-zero.");
            }
            if (light.Type == "Spot" && (light.InnerConeDegrees < 0f || light.OuterConeDegrees > 89f || light.InnerConeDegrees > light.OuterConeDegrees))
                throw new InvalidDataException($"Scene spotlight {i} cone angles are invalid.");
        }
        return document;
    }

    private static long ValidateTextures(
        System.Collections.Generic.IReadOnlyList<SceneTextureDocument3D> textures,
        string owner,
        long maximumBytes,
        bool extensionTextures)
    {
        if (maximumBytes < 0) throw new InvalidDataException($"{owner} exceeds the remaining embedded texture budget.");
        var identities = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        for (var i = 0; i < textures.Count; i++)
        {
            var texture = textures[i] ?? throw new InvalidDataException($"{owner} texture {i} is null.");
            if (string.IsNullOrWhiteSpace(texture.Slot)) throw new InvalidDataException($"{owner} texture {i} has no slot.");
            if (!StringComparer.Ordinal.Equals(texture.Slot, texture.Slot.Trim())) throw new InvalidDataException($"{owner} texture {i} slot contains surrounding whitespace.");
            if (extensionTextures)
            {
                if (texture.Slot != "extension") throw new InvalidDataException($"{owner} texture {i} must use slot 'extension', not '{texture.Slot}'.");
            }
            else if (texture.Slot is not ("baseColor" or "normal" or "metallicRoughness" or "emissive"))
            {
                throw new InvalidDataException($"{owner} texture {i} has unsupported slot '{texture.Slot}'.");
            }
            if (string.IsNullOrWhiteSpace(texture.LogicalKey)) throw new InvalidDataException($"{owner} texture {i} has no logical key.");
            if (!StringComparer.Ordinal.Equals(texture.LogicalKey, texture.LogicalKey.Trim())) throw new InvalidDataException($"{owner} texture {i} logical key contains surrounding whitespace.");
            var identity = extensionTextures ? texture.LogicalKey : texture.Slot;
            if (!identities.Add(identity)) throw new InvalidDataException($"{owner} contains duplicate texture identity '{identity}'.");
            texture.Base64Data ??= string.Empty;
            if (texture.Base64Data.Length == 0) throw new InvalidDataException($"{owner} texture '{texture.LogicalKey}' has no embedded payload.");
            ValidateFinite(texture.Strength, $"{owner} texture '{texture.LogicalKey}' strength");
            if (texture.Strength < 0f) throw new InvalidDataException($"{owner} texture '{texture.LogicalKey}' strength cannot be negative.");
            bytes = checked(bytes + ValidateBase64(texture.Base64Data, $"{owner} texture '{texture.LogicalKey}'", maximumBytes - bytes));
        }
        return bytes;
    }

    private static int ValidateBase64(string value, string owner, long maximumBytes)
    {
        if (maximumBytes < 0) throw new InvalidDataException($"{owner} exceeds its configured byte budget.");
        if (value.Length == 0) return 0;
        var maximumDecodedLength = checked(((long)value.Length + 3L) / 4L * 3L);
        if (maximumDecodedLength > maximumBytes + 2L)
            throw new InvalidDataException($"{owner} encoded payload can exceed its configured limit of {maximumBytes} bytes.");
        if (maximumDecodedLength > Array.MaxLength)
            throw new InvalidDataException($"{owner} is too large to validate safely.");
        var buffer = ArrayPool<byte>.Shared.Rent(global::System.Math.Max(1, (int)maximumDecodedLength));
        try
        {
            if (!Convert.TryFromBase64String(value, buffer, out var written)) throw new InvalidDataException($"{owner} contains invalid base64 data.");
            if (written > maximumBytes) throw new InvalidDataException($"{owner} exceeds its configured limit of {maximumBytes} bytes.");
            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void ValidateMaterial(SceneMaterialDocument3D material, string name)
    {
        ValidateColor(material.BaseColor, name + ".baseColor");
        ValidateColor(material.SpecularColor, name + ".specularColor");
        ValidateColor(material.EmissiveColor, name + ".emissiveColor");
        ValidateFinite(material.Opacity, name + ".opacity");
        ValidateFinite(material.Metallic, name + ".metallic");
        ValidateFinite(material.Roughness, name + ".roughness");
        ValidateFinite(material.SpecularStrength, name + ".specularStrength");
        ValidateFinite(material.Shininess, name + ".shininess");
        ValidateFinite(material.AmbientStrength, name + ".ambientStrength");
        ValidateFinite(material.DiffuseStrength, name + ".diffuseStrength");
        ValidateFinite(material.AlphaCutoff, name + ".alphaCutoff");
        if (material.Opacity is < 0f or > 1f || material.Metallic is < 0f or > 1f || material.Roughness is < 0f or > 1f || material.AlphaCutoff is < 0f or > 1f)
            throw new InvalidDataException($"Scene material '{name}' contains a normalized value outside [0,1].");
        if (material.AmbientStrength is < 0f or > 4f || material.DiffuseStrength is < 0f or > 4f || material.SpecularStrength is < 0f or > 4f)
            throw new InvalidDataException($"Scene material '{name}' lighting strengths must be between 0 and 4.");
        if (material.Shininess is < 1f or > 512f) throw new InvalidDataException($"Scene material '{name}' shininess must be between 1 and 512.");
        if (!Enum.IsDefined((LightingMode)material.Lighting)) throw new InvalidDataException($"Scene value '{name}.lighting' is invalid.");
        if (!Enum.IsDefined((SurfaceMode)material.Surface)) throw new InvalidDataException($"Scene value '{name}.surface' is invalid.");
        if (!Enum.IsDefined((CullMode)material.CullMode)) throw new InvalidDataException($"Scene value '{name}.cullMode' is invalid.");
    }

    private static void ValidateVector(Vector3Document3D value, string name)
    {
        ValidateFinite(value.X, name + ".x");
        ValidateFinite(value.Y, name + ".y");
        ValidateFinite(value.Z, name + ".z");
    }

    private static void ValidateColor(ColorDocument3D value, string name)
    {
        ValidateFinite(value.R, name + ".r");
        ValidateFinite(value.G, name + ".g");
        ValidateFinite(value.B, name + ".b");
        ValidateFinite(value.A, name + ".a");
        if (value.R is < 0f or > 1f || value.G is < 0f or > 1f || value.B is < 0f or > 1f || value.A is < 0f or > 1f)
            throw new InvalidDataException($"Scene color '{name}' components must be between 0 and 1.");
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException($"Scene value '{name}' must be finite.");
    }

    private static void ValidateOptions(SceneSerializerOptions3D options)
    {
        if (options.MaximumObjects <= 0 || options.MaximumLights <= 0 || options.MaximumEmbeddedTextureBytes < 0 || options.MaximumExtensionParameterBytes < 0 || options.MaximumDocumentBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    private static bool HasEncodedTextures(Material3D material)
    {
        if (material.HasBaseColorTexture || material.HasNormalMap || material.HasMetallicRoughnessTexture || material.HasEmissiveTexture) return true;
        return material.ShaderExtension is { Textures.Count: > 0 };
    }

    private static void ValidateGeometry(SceneObjectDocument3D item)
    {
        if (item.Type == "ImportedModel")
        {
            if (string.IsNullOrWhiteSpace(item.AssetPath)) throw new InvalidDataException($"Imported model '{item.Name}' has no assetPath.");
            if (!StringComparer.Ordinal.Equals(item.AssetPath, item.AssetPath.Trim())) throw new InvalidDataException($"Imported model '{item.Name}' assetPath contains surrounding whitespace.");
            if (item.Geometry.Count != 0) throw new InvalidDataException($"Imported model '{item.Name}' contains unsupported primitive geometry fields.");
            return;
        }

        string[] expected = item.Type switch
        {
            "Box" or "Rectangle" => new[] { "width", "height", "depth" },
            "Sphere" => new[] { "radius", "segments", "rings" },
            "Plane" => new[] { "width", "height", "segmentsX", "segmentsY" },
            "Cylinder" or "Cone" => new[] { "radius", "height", "segments" },
            "Ellipse" => new[] { "width", "height", "depth", "segments" },
            "Billboard" => new[] { "width", "height" },
            _ => throw new InvalidDataException($"Unknown scene object type '{item.Type}'.")
        };
        if (item.Geometry.Count != expected.Length)
            throw new InvalidDataException($"Object '{item.Name}' type '{item.Type}' has an unexpected geometry field count.");
        for (var i = 0; i < expected.Length; i++)
            if (!item.Geometry.ContainsKey(expected[i])) throw new InvalidDataException($"Object '{item.Name}' is missing geometry value '{expected[i]}'.");
        try { _ = CreatePrimitive(item); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"Object '{item.Name}' geometry is invalid: {exception.Message}", exception);
        }
    }

    private static async ValueTask<MemoryStream> ReadBoundedDocumentAsync(Stream input, int maximumBytes, CancellationToken cancellationToken)
    {
        if (input.CanSeek && input.Length - input.Position > maximumBytes)
            throw new InvalidDataException($"Scene JSON exceeds configured limit {maximumBytes} bytes.");
        var output = new MemoryStream(global::System.Math.Min(maximumBytes, 64 * 1024));
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total = checked(total + read);
                if (total > maximumBytes) throw new InvalidDataException($"Scene JSON exceeds configured limit {maximumBytes} bytes.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AddTexture(SceneMaterialDocument3D target, string slot, string? key, string? mime, byte[]? data, float strength, SceneSerializerOptions3D options)
        => AddTexture(target.Textures, slot, key, mime, data, strength, options);

    private static void AddTexture(System.Collections.Generic.ICollection<SceneTextureDocument3D> target, string slot, string? key, string? mime, byte[]? data, float strength, SceneSerializerOptions3D options)
    {
        if (data is null || data.Length == 0) return;
        if (data.Length > options.MaximumEmbeddedTextureBytes) throw new InvalidOperationException($"Texture '{key}' exceeds per-document embedded texture limit.");
        target.Add(new SceneTextureDocument3D { Slot = slot, LogicalKey = key ?? slot, MimeType = mime, Base64Data = Convert.ToBase64String(data), Strength = strength });
    }

    private sealed class BoundedWriteStream3D : Stream
    {
        private readonly Stream _inner;
        private readonly long _maximumBytes;
        private long _written;

        public BoundedWriteStream3D(Stream inner, long maximumBytes)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (!inner.CanWrite) throw new ArgumentException("Output stream is not writable.", nameof(inner));
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            _maximumBytes = maximumBytes;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Reserve(count);
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Reserve(buffer.Length);
            _inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reserve(buffer.Length);
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Reserve(count);
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            // The caller owns the destination stream.
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Reserve(int byteCount)
        {
            if (byteCount < 0 || _written > _maximumBytes - byteCount)
                throw new InvalidDataException($"Serialized scene exceeds configured limit {_maximumBytes} bytes.");
            _written += byteCount;
        }
    }

    private static void Set(SceneObjectDocument3D target, string name, float value) => target.Geometry[name] = value;
    private static float Get(SceneObjectDocument3D source, string name) => source.Geometry.TryGetValue(name, out var value) ? value : throw new InvalidDataException($"Object '{source.Name}' is missing geometry value '{name}'.");
    private static int GetInt(SceneObjectDocument3D source, string name)
    {
        var value = Get(source, name);
        if (!float.IsFinite(value) || value != MathF.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException($"Object '{source.Name}' geometry value '{name}' must be an exact 32-bit integer.");
        return (int)value;
    }
    private static Vector3Document3D ToDocument(Vector3 value) => new(value.X, value.Y, value.Z);
    private static Vector3 ToVector(Vector3Document3D value) => new(value.X, value.Y, value.Z);
    private static ColorDocument3D ToDocument(ColorRgba value) => new(value.R, value.G, value.B, value.A);
    private static ColorRgba ToColor(ColorDocument3D value) => new(value.R, value.G, value.B, value.A);
}
