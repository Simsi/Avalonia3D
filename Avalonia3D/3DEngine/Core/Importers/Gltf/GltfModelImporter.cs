using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Assets.Resolvers;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry.Surfaces;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Importers.Gltf;

public static class GltfModelImporter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbJsonChunkType = 0x4E4F534A;
    private const uint GlbBinaryChunkType = 0x004E4942;

    public static ModelAsset3D Import(string path, ModelImportOptions? options = null)
    {
        options ??= new ModelImportOptions();
        var diagnostics = new ModelImportDiagnostics();
        if (!File.Exists(path))
        {
            diagnostics.Error("MODEL_FILE_NOT_FOUND", $"Model file not found: {path}");
            return CreateEmptyAsset(path, diagnostics);
        }

        var extension = Path.GetExtension(path);
        try
        {
            var fileInfo = new FileInfo(path);
            if (options.MaxFileBytes > 0 && fileInfo.Length > options.MaxFileBytes)
            {
                diagnostics.Error("MODEL_FILE_TOO_LARGE", $"Model file is {fileInfo.Length} bytes; configured limit is {options.MaxFileBytes} bytes.");
                return CreateEmptyAsset(path, diagnostics);
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(extension, ".glb"))
            {
                var bytes = File.ReadAllBytes(path);
                return ImportGlbBytes(bytes, path, options, diagnostics);
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(extension, ".gltf"))
            {
                var json = File.ReadAllText(path);
                return ImportGltfJson(json, path, options, diagnostics);
            }

            diagnostics.Error("MODEL_FORMAT_UNSUPPORTED", $"Only .glb and .gltf are supported. File: {path}");
            return CreateEmptyAsset(path, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Error("MODEL_IMPORT_EXCEPTION", ex.Message);
            return CreateEmptyAsset(path, diagnostics);
        }
    }

    public static ModelAsset3D ImportBytes(byte[] bytes, string sourcePath = "memory.glb", ModelImportOptions? options = null)
    {
        options ??= new ModelImportOptions();
        var diagnostics = new ModelImportDiagnostics();
        sourcePath = string.IsNullOrWhiteSpace(sourcePath) ? "memory.glb" : sourcePath;
        if (bytes is null || bytes.Length == 0)
        {
            diagnostics.Error("MODEL_BYTES_EMPTY", "GLB byte array is empty.");
            return CreateEmptyAsset(sourcePath, diagnostics);
        }

        if (options.MaxFileBytes > 0 && bytes.LongLength > options.MaxFileBytes)
        {
            diagnostics.Error("MODEL_FILE_TOO_LARGE", $"Model byte array is {bytes.LongLength} bytes; configured limit is {options.MaxFileBytes} bytes.");
            return CreateEmptyAsset(sourcePath, diagnostics);
        }

        try
        {
            return ImportGlbBytes(bytes, sourcePath, options, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Error("MODEL_IMPORT_EXCEPTION", ex.Message);
            return CreateEmptyAsset(sourcePath, diagnostics);
        }
    }

    public static ModelAsset3D ImportStream(Stream stream, string sourcePath = "stream.glb", ModelImportOptions? options = null)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        options ??= new ModelImportOptions();
        var diagnostics = new ModelImportDiagnostics();
        var bytes = ReadStreamBounded(stream, options.MaxFileBytes, diagnostics, "MODEL_STREAM_TOO_LARGE");
        if (bytes is null) return CreateEmptyAsset(sourcePath, diagnostics);
        if (StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(sourcePath), ".gltf"))
        {
            try
            {
                return ImportGltfJson(Encoding.UTF8.GetString(bytes), sourcePath, options, diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics.Error("MODEL_IMPORT_EXCEPTION", ex.Message);
                return CreateEmptyAsset(sourcePath, diagnostics);
            }
        }

        return ImportBytes(bytes, sourcePath, options);
    }

    private static ModelAsset3D ImportGlbBytes(byte[] bytes, string sourcePath, ModelImportOptions options, ModelImportDiagnostics diagnostics)
    {
        var glb = ReadGlb(bytes, diagnostics, options);
        if (diagnostics.HasErrors)
        {
            return CreateEmptyAsset(sourcePath, diagnostics);
        }

        if (options.MaxJsonBytes > 0 && Encoding.UTF8.GetByteCount(glb.Json) > options.MaxJsonBytes)
        {
            diagnostics.Error("GLTF_JSON_TOO_LARGE", $"GLB JSON chunk exceeds configured limit of {options.MaxJsonBytes} bytes.");
            return CreateEmptyAsset(sourcePath, diagnostics);
        }

        if (options.MaxBinaryChunkBytes > 0 && glb.BinaryChunk.Length > options.MaxBinaryChunkBytes)
        {
            diagnostics.Error("GLTF_BINARY_TOO_LARGE", $"GLB BIN chunk exceeds configured limit of {options.MaxBinaryChunkBytes} bytes.");
            return CreateEmptyAsset(sourcePath, diagnostics);
        }

        using var doc = JsonDocument.Parse(glb.Json, new JsonDocumentOptions { MaxDepth = 128, AllowTrailingCommas = false });
        return ImportRoot(doc.RootElement, sourcePath, glb.BinaryChunk, options, diagnostics);
    }

    private static ModelAsset3D ImportGltfJson(string json, string sourcePath, ModelImportOptions options, ModelImportDiagnostics diagnostics)
    {
        if (options.MaxJsonBytes > 0 && Encoding.UTF8.GetByteCount(json) > options.MaxJsonBytes)
        {
            diagnostics.Error("GLTF_JSON_TOO_LARGE", $"glTF JSON exceeds configured limit of {options.MaxJsonBytes} bytes.");
            return CreateEmptyAsset(sourcePath, diagnostics);
        }

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128, AllowTrailingCommas = false });
        var binary = ResolvePrimaryBuffer(doc.RootElement, sourcePath, options, diagnostics) ?? Array.Empty<byte>();
        return ImportRoot(doc.RootElement, sourcePath, binary, options, diagnostics);
    }

    private static ModelAsset3D ImportRoot(JsonElement root, string sourcePath, byte[] binaryChunk, ModelImportOptions options, ModelImportDiagnostics diagnostics)
    {
        var context = new ImportContext(sourcePath, binaryChunk ?? Array.Empty<byte>(), diagnostics, options);
        var materials = ReadMaterials(root, diagnostics);
        var textures = ReadTextures(root, context);
        var meshes = ReadMeshes(root, context);
        var nodes = ReadNodes(root, meshes, diagnostics);
        ResolveNodeWorldTransforms(root, nodes, meshes, diagnostics);
        var skins = ReadSkins(root, nodes, context);
        var animations = ReadAnimations(root, nodes, context);

        if (options.TreatWarningsAsErrors && diagnostics.HasWarnings)
        {
            diagnostics.Error("MODEL_WARNINGS_TREATED_AS_ERRORS", "Import warnings were promoted to an error by ModelImportOptions.TreatWarningsAsErrors.");
        }

        return new ModelAsset3D(CreateAssetId(sourcePath), sourcePath, nodes, meshes, materials, textures, diagnostics, skins, animations);
    }

    private static ModelAsset3D CreateEmptyAsset(string sourcePath, ModelImportDiagnostics diagnostics)
        => new(CreateAssetId(sourcePath), sourcePath, Array.Empty<ModelNode3D>(), Array.Empty<MeshAsset3D>(), Array.Empty<ModelMaterialAsset3D>(), Array.Empty<ModelTextureAsset3D>(), diagnostics);

    private static GlbContainer ReadGlb(byte[] bytes, ModelImportDiagnostics diagnostics, ModelImportOptions options)
    {
        if (bytes.Length < 20)
        {
            diagnostics.Error("GLB_TOO_SMALL", "File is too small to be a GLB 2.0 container.");
            return new GlbContainer("{}", Array.Empty<byte>());
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (magic != GlbMagic)
        {
            diagnostics.Error("GLB_BAD_MAGIC", "Invalid GLB magic header.");
            return new GlbContainer("{}", Array.Empty<byte>());
        }

        if (version != 2)
        {
            diagnostics.Error("GLB_BAD_VERSION", $"Only GLB version 2 is supported. Found version {version}.");
            return new GlbContainer("{}", Array.Empty<byte>());
        }

        if (length != bytes.Length)
        {
            diagnostics.Error("GLB_BAD_LENGTH", $"GLB declared length ({length}) does not match file size ({bytes.Length}).");
            return new GlbContainer("{}", Array.Empty<byte>());
        }

        string? json = null;
        var binary = Array.Empty<byte>();
        var offset = 12;
        var chunkIndex = 0;
        var declaredLength = checked((int)length);
        while (offset < declaredLength)
        {
            if (offset + 8 > declaredLength)
            {
                diagnostics.Error("GLB_BAD_CHUNK_HEADER", "A GLB chunk header extends beyond the declared file length.");
                return new GlbContainer("{}", Array.Empty<byte>());
            }

            var chunkLengthRaw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            if (chunkLengthRaw > int.MaxValue)
            {
                diagnostics.Error("GLB_CHUNK_TOO_LARGE", "A GLB chunk is too large for this importer.");
                return new GlbContainer("{}", Array.Empty<byte>());
            }

            var chunkLength = (int)chunkLengthRaw;
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            var chunkEnd = (long)offset + chunkLength;
            if (chunkLength < 0 || chunkEnd > declaredLength || chunkEnd > int.MaxValue)
            {
                diagnostics.Error("GLB_BAD_CHUNK", "A GLB chunk extends beyond the declared file length.");
                return new GlbContainer("{}", Array.Empty<byte>());
            }

            if (options.StrictGlbValidation && chunkLength % 4 != 0)
            {
                diagnostics.Error("GLB_BAD_CHUNK_ALIGNMENT", "GLB chunk length is not 4-byte aligned.");
                return new GlbContainer("{}", Array.Empty<byte>());
            }

            var chunk = bytes.AsSpan(offset, chunkLength).ToArray();
            offset = (int)chunkEnd;
            if (chunkType == GlbJsonChunkType)
            {
                if (chunkIndex != 0 && options.StrictGlbValidation)
                {
                    diagnostics.Error("GLB_JSON_CHUNK_ORDER", "The JSON chunk must be the first GLB chunk.");
                    return new GlbContainer("{}", Array.Empty<byte>());
                }

                if (json is not null && options.StrictGlbValidation)
                {
                    diagnostics.Error("GLB_DUPLICATE_JSON", "GLB contains more than one JSON chunk.");
                    return new GlbContainer("{}", Array.Empty<byte>());
                }

                json = Encoding.UTF8.GetString(chunk).TrimEnd('\0', ' ', '\t', '\r', '\n');
            }
            else if (chunkType == GlbBinaryChunkType)
            {
                if (binary.Length > 0 && options.StrictGlbValidation)
                {
                    diagnostics.Error("GLB_DUPLICATE_BIN", "GLB contains more than one BIN chunk.");
                    return new GlbContainer("{}", Array.Empty<byte>());
                }

                binary = chunk;
            }

            chunkIndex++;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostics.Error("GLB_NO_JSON", "GLB file does not contain a JSON chunk.");
            json = "{}";
        }

        return new GlbContainer(json, binary);
    }


    private static byte[]? ResolvePrimaryBuffer(JsonElement root, string sourcePath, ModelImportOptions options, ModelImportDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("buffers", out var buffers) || buffers.ValueKind != JsonValueKind.Array || buffers.GetArrayLength() == 0)
        {
            diagnostics.Warning("GLTF_NO_BUFFERS", "glTF file contains no buffers.");
            return Array.Empty<byte>();
        }

        var buffer = buffers[0];
        var declaredLength = GetInt(buffer, "byteLength", 0);
        var uri = GetString(buffer, "uri");
        if (string.IsNullOrWhiteSpace(uri))
        {
            diagnostics.Error("GLTF_EXTERNAL_BUFFER_URI_MISSING", "Non-GLB glTF buffer 0 has no uri.");
            return Array.Empty<byte>();
        }

        byte[]? bytes = null;
        if (IsDataUri(uri))
        {
            if (!options.ResolveDataUris)
            {
                diagnostics.Warning("GLTF_DATA_URI_DISABLED", "Buffer data URI resolution is disabled by ModelImportOptions.ResolveDataUris.");
                return Array.Empty<byte>();
            }

            if (!TryDecodeDataUri(uri, options.MaxBinaryChunkBytes, out bytes, out _))
            {
                diagnostics.Error("GLTF_BUFFER_DATA_URI_INVALID", "Buffer 0 data URI could not be decoded.");
                return Array.Empty<byte>();
            }
        }
        else
        {
            if (!options.ResolveExternalBuffers)
            {
                diagnostics.Warning("GLTF_EXTERNAL_BUFFER_DISABLED", $"External buffer resolution is disabled. Buffer uri: {uri}");
                return Array.Empty<byte>();
            }

            bytes = TryReadExternalBytes(sourcePath, uri, options, diagnostics, "GLTF_EXTERNAL_BUFFER_NOT_FOUND", options.MaxBinaryChunkBytes);
        }

        bytes ??= Array.Empty<byte>();
        if (options.MaxBinaryChunkBytes > 0 && bytes.Length > options.MaxBinaryChunkBytes)
        {
            diagnostics.Error("GLTF_BINARY_TOO_LARGE", $"Resolved buffer is {bytes.Length} bytes; configured limit is {options.MaxBinaryChunkBytes} bytes.");
            return Array.Empty<byte>();
        }

        if (declaredLength > 0 && bytes.Length < declaredLength && options.StrictValidation)
        {
            diagnostics.Error("GLTF_BUFFER_TOO_SHORT", $"Resolved buffer is {bytes.Length} bytes but glTF declares {declaredLength} bytes.");
        }

        return bytes;
    }

    private static IReadOnlyList<ModelMaterialAsset3D> ReadMaterials(JsonElement root, ModelImportDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("materials", out var materialsElement) || materialsElement.ValueKind != JsonValueKind.Array)
        {
            return new[] { ModelMaterialAsset3D.Default };
        }

        var materials = new List<ModelMaterialAsset3D>();
        var index = 0;
        foreach (var materialElement in materialsElement.EnumerateArray())
        {
            var name = GetString(materialElement, "name") ?? $"Material_{index}";
            var baseColor = ColorRgba.White;
            var metallic = 1f;
            var roughness = 1f;
            int? baseColorTextureIndex = null;
            int? metallicRoughnessTextureIndex = null;
            int? normalTextureIndex = null;
            int? emissiveTextureIndex = null;
            var emissiveColor = ColorRgba.Transparent;
            var normalTextureScale = 1f;
            if (materialElement.TryGetProperty("pbrMetallicRoughness", out var pbr))
            {
                if (pbr.TryGetProperty("baseColorFactor", out var colorFactor) && colorFactor.ValueKind == JsonValueKind.Array)
                {
                    baseColor = ReadColor(colorFactor, ColorRgba.White);
                }

                metallic = GetSingle(pbr, "metallicFactor", 1f);
                roughness = GetSingle(pbr, "roughnessFactor", 1f);
                if (pbr.TryGetProperty("baseColorTexture", out var textureInfo) && textureInfo.TryGetProperty("index", out var textureIndexElement))
                {
                    baseColorTextureIndex = textureIndexElement.GetInt32();
                }

                if (pbr.TryGetProperty("metallicRoughnessTexture", out var metallicRoughnessTextureInfo) && metallicRoughnessTextureInfo.TryGetProperty("index", out var metallicRoughnessTextureIndexElement))
                {
                    metallicRoughnessTextureIndex = metallicRoughnessTextureIndexElement.GetInt32();
                }
            }

            if (materialElement.TryGetProperty("normalTexture", out var normalTexture))
            {
                if (normalTexture.TryGetProperty("index", out var normalTextureIndexElement))
                {
                    normalTextureIndex = normalTextureIndexElement.GetInt32();
                }

                normalTextureScale = GetSingle(normalTexture, "scale", 1f);
            }

            if (materialElement.TryGetProperty("emissiveTexture", out var emissiveTexture) && emissiveTexture.TryGetProperty("index", out var emissiveTextureIndexElement))
            {
                emissiveTextureIndex = emissiveTextureIndexElement.GetInt32();
            }

            if (materialElement.TryGetProperty("emissiveFactor", out var emissiveFactor) && emissiveFactor.ValueKind == JsonValueKind.Array)
            {
                var values = ReadFloatArray(emissiveFactor, 3);
                if (values.Length >= 3) emissiveColor = new ColorRgba(values[0], values[1], values[2], 1f);
            }

            var doubleSided = GetBool(materialElement, "doubleSided", false);
            var alphaMode = GetString(materialElement, "alphaMode") ?? "OPAQUE";
            var alphaCutoff = GetSingle(materialElement, "alphaCutoff", 0.5f);
            if (alphaMode != "OPAQUE" && alphaMode != "MASK" && alphaMode != "BLEND")
            {
                diagnostics.Warning("GLTF_ALPHA_MODE_UNKNOWN", $"Unknown alphaMode '{alphaMode}' on material '{name}'. Treating it as OPAQUE.");
                alphaMode = "OPAQUE";
            }

            materials.Add(new ModelMaterialAsset3D(index, name, baseColor, metallic, roughness, alphaMode, alphaCutoff, baseColorTextureIndex, normalTextureIndex, normalTextureScale, metallicRoughnessTextureIndex, emissiveTextureIndex, emissiveColor, doubleSided));
            index++;
        }

        if (materials.Count == 0) materials.Add(ModelMaterialAsset3D.Default);
        return materials;
    }

    private static IReadOnlyList<ModelTextureAsset3D> ReadTextures(JsonElement root, ImportContext context)
    {
        var images = ReadImages(root, context);
        if (images.Count == 0)
        {
            return Array.Empty<ModelTextureAsset3D>();
        }

        // glTF material texture indices point into the `textures` array. Older importer code
        // treated image indices as texture indices, which worked only for trivial files where
        // texture[i].source == i. Read the texture indirection explicitly and preserve embedded
        // image payloads for render backends.
        if (!root.TryGetProperty("textures", out var texturesElement) || texturesElement.ValueKind != JsonValueKind.Array)
        {
            var fallback = new List<ModelTextureAsset3D>();
            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                fallback.Add(new ModelTextureAsset3D(i, image.Name, image.MimeType, image.Uri, image.Data));
            }
            return fallback;
        }

        var textures = new List<ModelTextureAsset3D>();
        var textureIndex = 0;
        foreach (var textureElement in texturesElement.EnumerateArray())
        {
            var source = GetInt(textureElement, "source", textureIndex);
            if (source < 0 || source >= images.Count)
            {
                context.Diagnostics.Warning("GLTF_TEXTURE_SOURCE_INVALID", $"Texture {textureIndex} references missing image source {source}.");
                textures.Add(new ModelTextureAsset3D(textureIndex, $"Texture_{textureIndex}", null, null, null));
            }
            else
            {
                var image = images[source];
                textures.Add(new ModelTextureAsset3D(textureIndex, image.Name, image.MimeType, image.Uri, image.Data));
            }
            textureIndex++;
        }

        return textures;
    }

    private static IReadOnlyList<ImagePayload> ReadImages(JsonElement root, ImportContext context)
    {
        if (!root.TryGetProperty("images", out var imagesElement) || imagesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ImagePayload>();
        }

        var images = new List<ImagePayload>();
        var index = 0;
        foreach (var image in imagesElement.EnumerateArray())
        {
            var name = GetString(image, "name") ?? $"Image_{index}";
            var mimeType = GetString(image, "mimeType");
            var uri = GetString(image, "uri");
            byte[]? data = null;
            if (image.TryGetProperty("bufferView", out var bufferViewElement))
            {
                var bufferView = bufferViewElement.GetInt32();
                data = ReadBufferViewBytes(root, context, bufferView);
            }
            else if (!string.IsNullOrWhiteSpace(uri))
            {
                if (IsDataUri(uri))
                {
                    if (context.Options.ResolveDataUris)
                    {
                        if (!TryDecodeDataUri(uri, context.Options.MaxTextureBytes, out data, out var dataMime))
                        {
                            context.Diagnostics.Warning("GLTF_IMAGE_DATA_URI_INVALID", $"Image {index} data URI could not be decoded.");
                        }
                        else
                        {
                            mimeType ??= dataMime;
                        }
                    }
                    else
                    {
                        context.Diagnostics.Warning("GLTF_IMAGE_DATA_URI_DISABLED", $"Image {index} uses a data URI but ResolveDataUris is disabled.");
                    }
                }
                else if (context.Options.ResolveExternalImages)
                {
                    data = TryReadExternalBytes(context.SourcePath, uri, context.Options, context.Diagnostics, "GLTF_EXTERNAL_IMAGE_NOT_FOUND", context.Options.MaxTextureBytes);
                    mimeType ??= GuessMimeType(uri);
                }
                else
                {
                    context.Diagnostics.Warning("GLTF_EXTERNAL_IMAGE_DISABLED", $"External image resolution is disabled. Image uri: {uri}");
                }
            }
            else
            {
                context.Diagnostics.Warning("GLTF_IMAGE_URI_MISSING", $"Image {index} ('{name}') has neither uri nor bufferView.");
            }

            if ((data is null || data.Length == 0) && context.Options.ResolveSidecarImages)
            {
                data = TryReadSidecarImageBytes(context.SourcePath, name, context.Options, context.Diagnostics, out var sidecarMime);
                if (data is { Length: > 0 }) mimeType = sidecarMime ?? mimeType;
            }

            if (data is { Length: > 0 } && context.Options.MaxTextureBytes > 0 && data.Length > context.Options.MaxTextureBytes)
            {
                context.Diagnostics.Warning("GLTF_TEXTURE_TOO_LARGE", $"Image {index} is {data.Length} bytes; configured limit is {context.Options.MaxTextureBytes} bytes. Texture payload was skipped.");
                data = null;
            }

            images.Add(new ImagePayload(name, mimeType, uri, data));
            index++;
        }

        return images;
    }

    private static IReadOnlyList<MeshAsset3D> ReadMeshes(JsonElement root, ImportContext context)
    {
        if (!root.TryGetProperty("meshes", out var meshesElement) || meshesElement.ValueKind != JsonValueKind.Array)
        {
            context.Diagnostics.Warning("GLTF_NO_MESHES", "glTF file contains no meshes.");
            return Array.Empty<MeshAsset3D>();
        }

        var meshes = new List<MeshAsset3D>();
        var meshIndex = 0;
        foreach (var meshElement in meshesElement.EnumerateArray())
        {
            var name = GetString(meshElement, "name") ?? $"Mesh_{meshIndex}";
            var primitives = new List<MeshPrimitiveAsset3D>();
            if (meshElement.TryGetProperty("primitives", out var primitivesElement) && primitivesElement.ValueKind == JsonValueKind.Array)
            {
                var primitiveIndex = 0;
                foreach (var primitiveElement in primitivesElement.EnumerateArray())
                {
                    var mode = GetInt(primitiveElement, "mode", 4);
                    if (mode != 4)
                    {
                        context.Diagnostics.Warning("GLTF_PRIMITIVE_MODE_UNSUPPORTED", $"Mesh '{name}' primitive {primitiveIndex} uses mode {mode}. Only TRIANGLES mode 4 is imported.");
                        primitiveIndex++;
                        continue;
                    }

                    if (!primitiveElement.TryGetProperty("attributes", out var attrs) || !attrs.TryGetProperty("POSITION", out var positionAccessorElement))
                    {
                        context.Diagnostics.Warning("GLTF_PRIMITIVE_NO_POSITION", $"Mesh '{name}' primitive {primitiveIndex} has no POSITION attribute and was skipped.");
                        primitiveIndex++;
                        continue;
                    }

                    var positions = ReadVec3Accessor(root, context, positionAccessorElement.GetInt32(), "POSITION");
                    if (positions.Length == 0)
                    {
                        context.Diagnostics.Warning("GLTF_PRIMITIVE_EMPTY_POSITION", $"Mesh '{name}' primitive {primitiveIndex} has no POSITION values and was skipped.");
                        primitiveIndex++;
                        continue;
                    }

                    if (context.Options.MaxVerticesPerPrimitive > 0 && positions.Length > context.Options.MaxVerticesPerPrimitive)
                    {
                        context.Diagnostics.Error("GLTF_PRIMITIVE_TOO_MANY_VERTICES", $"Mesh '{name}' primitive {primitiveIndex} has {positions.Length} vertices; configured limit is {context.Options.MaxVerticesPerPrimitive}.");
                        primitiveIndex++;
                        continue;
                    }
                    var normals = attrs.TryGetProperty("NORMAL", out var normalAccessorElement)
                        ? ReadVec3Accessor(root, context, normalAccessorElement.GetInt32(), "NORMAL")
                        : null;
                    var texCoords0 = attrs.TryGetProperty("TEXCOORD_0", out var uvAccessorElement)
                        ? ReadVec2Accessor(root, context, uvAccessorElement.GetInt32(), "TEXCOORD_0")
                        : null;
                    var joints0 = attrs.TryGetProperty("JOINTS_0", out var jointsAccessorElement)
                        ? ReadVec4IntAccessor(root, context, jointsAccessorElement.GetInt32(), "JOINTS_0")
                        : null;
                    var weights0 = attrs.TryGetProperty("WEIGHTS_0", out var weightsAccessorElement)
                        ? ReadVec4Accessor(root, context, weightsAccessorElement.GetInt32(), "WEIGHTS_0")
                        : null;
                    var skinWeights0 = BuildSkinWeights(joints0, weights0, positions.Length, name, primitiveIndex, context.Diagnostics);
                    var indices = primitiveElement.TryGetProperty("indices", out var indicesAccessorElement)
                        ? ReadIndexAccessor(root, context, indicesAccessorElement.GetInt32())
                        : null;
                    if (indices is not null && context.Options.MaxIndicesPerPrimitive > 0 && indices.Length > context.Options.MaxIndicesPerPrimitive)
                    {
                        context.Diagnostics.Error("GLTF_PRIMITIVE_TOO_MANY_INDICES", $"Mesh '{name}' primitive {primitiveIndex} has {indices.Length} indices; configured limit is {context.Options.MaxIndicesPerPrimitive}.");
                        primitiveIndex++;
                        continue;
                    }

                    indices ??= CreateSequentialIndices(positions.Length);
                    if (!ValidatePrimitiveIndices(indices, positions.Length, name, primitiveIndex, context.Diagnostics))
                    {
                        primitiveIndex++;
                        continue;
                    }

                    if ((normals is null || normals.Length != positions.Length) && context.Options.GenerateMissingNormals)
                    {
                        normals = TangentGenerator3D.GenerateNormals(positions, indices);
                        context.Diagnostics.Info("GLTF_NORMALS_GENERATED", $"Generated missing normals for mesh '{name}' primitive {primitiveIndex}.");
                    }

                    var materialIndex = GetInt(primitiveElement, "material", 0);
                    var id = $"{context.AssetId}:mesh:{meshIndex}:primitive:{primitiveIndex}";
                    primitives.Add(new MeshPrimitiveAsset3D(id, positions, normals, texCoords0, indices, materialIndex, $"{name}_Primitive_{primitiveIndex}", skinWeights0));
                    primitiveIndex++;
                }
            }

            meshes.Add(new MeshAsset3D(meshIndex, name, primitives));
            meshIndex++;
        }

        return meshes;
    }

    private static List<ModelNode3D> ReadNodes(JsonElement root, IReadOnlyList<MeshAsset3D> meshes, ModelImportDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("nodes", out var nodesElement) || nodesElement.ValueKind != JsonValueKind.Array)
        {
            if (meshes.Count == 0)
            {
                diagnostics.Warning("GLTF_NO_NODES", "glTF file contains no nodes.");
                return new List<ModelNode3D>();
            }

            var fallback = new List<ModelNode3D>();
            for (var i = 0; i < meshes.Count; i++)
            {
                fallback.Add(new ModelNode3D(i, meshes[i].Name, null, i, Matrix4x4.Identity, Matrix4x4.Identity, Array.Empty<int>(), meshes[i].Name));
            }
            return fallback;
        }

        var temp = new List<TempNode>();
        var index = 0;
        foreach (var nodeElement in nodesElement.EnumerateArray())
        {
            var name = GetString(nodeElement, "name") ?? $"Node_{index}";
            var meshIndex = nodeElement.TryGetProperty("mesh", out var meshElement) ? meshElement.GetInt32() : (int?)null;
            var skinIndex = nodeElement.TryGetProperty("skin", out var skinElement) ? skinElement.GetInt32() : (int?)null;
            var children = new List<int>();
            if (nodeElement.TryGetProperty("children", out var childrenElement) && childrenElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var childElement in childrenElement.EnumerateArray()) children.Add(childElement.GetInt32());
            }
            var local = ReadLocalTransform(nodeElement);
            temp.Add(new TempNode(index, name, meshIndex, skinIndex, children, local));
            index++;
        }

        var parents = new int?[temp.Count];
        foreach (var node in temp)
        {
            foreach (var child in node.Children)
            {
                if (child >= 0 && child < parents.Length) parents[child] = node.Index;
            }
        }

        var result = new List<ModelNode3D>(temp.Count);
        for (var i = 0; i < temp.Count; i++)
        {
            result.Add(new ModelNode3D(i, temp[i].Name, parents[i], temp[i].MeshIndex, temp[i].LocalTransform, Matrix4x4.Identity, temp[i].Children, temp[i].Name, temp[i].SkinIndex));
        }

        return result;
    }

    private static void ResolveNodeWorldTransforms(JsonElement root, List<ModelNode3D> nodes, IReadOnlyList<MeshAsset3D> meshes, ModelImportDiagnostics diagnostics)
    {
        if (nodes.Count == 0) return;
        var rootIndices = ResolveSceneRootNodes(root, nodes);
        var byIndex = new ModelNode3D[nodes.Count];
        nodes.CopyTo(byIndex);
        var visited = new bool[nodes.Count];
        foreach (var rootIndex in rootIndices)
        {
            Visit(rootIndex, Matrix4x4.Identity, string.Empty);
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            if (!visited[i]) Visit(i, Matrix4x4.Identity, string.Empty);
        }

        nodes.Clear();
        nodes.AddRange(byIndex);

        void Visit(int index, Matrix4x4 parentWorld, string parentPath)
        {
            if (index < 0 || index >= byIndex.Length || visited[index]) return;
            var node = byIndex[index];
            visited[index] = true;
            var world = node.LocalTransform * parentWorld;
            var path = string.IsNullOrWhiteSpace(parentPath) ? node.Name : parentPath + "/" + node.Name;
            var bounds = Bounds3D.Empty;
            if (node.MeshIndex.HasValue && node.MeshIndex.Value >= 0 && node.MeshIndex.Value < meshes.Count)
            {
                bounds = meshes[node.MeshIndex.Value].Bounds.Transform(world);
            }

            var updated = new ModelNode3D(node.Index, node.Name, node.ParentIndex, node.MeshIndex, node.LocalTransform, world, node.ChildIndices, path, node.SkinIndex)
            {
                Bounds = bounds
            };
            byIndex[index] = updated;
            foreach (var child in node.ChildIndices)
            {
                Visit(child, world, path);
            }
        }
    }

    private static IReadOnlyList<int> ResolveSceneRootNodes(JsonElement root, IReadOnlyList<ModelNode3D> nodes)
    {
        if (root.TryGetProperty("scenes", out var scenesElement) && scenesElement.ValueKind == JsonValueKind.Array && scenesElement.GetArrayLength() > 0)
        {
            var sceneIndex = GetInt(root, "scene", 0);
            if (sceneIndex >= 0 && sceneIndex < scenesElement.GetArrayLength())
            {
                var scene = scenesElement[sceneIndex];
                if (scene.TryGetProperty("nodes", out var sceneNodes) && sceneNodes.ValueKind == JsonValueKind.Array)
                {
                    var result = new List<int>();
                    foreach (var node in sceneNodes.EnumerateArray()) result.Add(node.GetInt32());
                    return result;
                }
            }
        }

        var roots = new List<int>();
        foreach (var node in nodes)
        {
            if (!node.ParentIndex.HasValue) roots.Add(node.Index);
        }
        return roots;
    }

    private static Matrix4x4 ReadLocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var matrixElement) && matrixElement.ValueKind == JsonValueKind.Array && matrixElement.GetArrayLength() == 16)
        {
            var m = ReadFloatArray(matrixElement, 16);
            // glTF stores matrices in column-major, column-vector convention.
            // System.Numerics.Vector3.Transform uses row-vector convention, so the
            // runtime matrix must be the transpose of the glTF conceptual matrix.
            return new Matrix4x4(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);
        }

        var translation = ReadVector3Property(node, "translation", Vector3.Zero);
        var rotation = ReadQuaternionProperty(node, "rotation", Quaternion.Identity);
        var scale = ReadVector3Property(node, "scale", Vector3.One);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }

    private static Vector3[] ReadVec3Accessor(JsonElement root, ImportContext context, int accessorIndex, string semantic)
    {
        var accessor = ResolveAccessor(root, context, accessorIndex);
        if (accessor.Count == 0) return Array.Empty<Vector3>();
        if (accessor.Type != "VEC3")
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_TYPE", $"Accessor {accessorIndex} for {semantic} is {accessor.Type}; expected VEC3.");
            return Array.Empty<Vector3>();
        }
        var values = new Vector3[accessor.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new Vector3(
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 0),
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 1),
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 2));
        }
        return values;
    }

    private static Vector2[] ReadVec2Accessor(JsonElement root, ImportContext context, int accessorIndex, string semantic)
    {
        var accessor = ResolveAccessor(root, context, accessorIndex);
        if (accessor.Count == 0) return Array.Empty<Vector2>();
        if (accessor.Type != "VEC2")
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_TYPE", $"Accessor {accessorIndex} for {semantic} is {accessor.Type}; expected VEC2.");
            return Array.Empty<Vector2>();
        }
        var values = new Vector2[accessor.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new Vector2(
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 0),
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 1));
        }
        return values;
    }

    private static int[] ReadIndexAccessor(JsonElement root, ImportContext context, int accessorIndex)
    {
        var accessor = ResolveAccessor(root, context, accessorIndex);
        if (accessor.Count == 0) return Array.Empty<int>();
        if (accessor.Type != "SCALAR" || accessor.ComponentType is not (5121 or 5123 or 5125))
        {
            context.Diagnostics.Error("GLTF_INDEX_ACCESSOR_TYPE", $"Index accessor {accessorIndex} must be SCALAR with UNSIGNED_BYTE, UNSIGNED_SHORT, or UNSIGNED_INT component type.");
            return Array.Empty<int>();
        }
        var values = new int[accessor.Count];
        for (var i = 0; i < values.Length; i++) values[i] = ReadComponentAsInt(context.BinaryChunk, accessor, i, 0);
        return values;
    }

    private static float[] ReadFloatAccessor(JsonElement root, ImportContext context, int accessorIndex, string semantic)
    {
        var accessor = ResolveAccessor(root, context, accessorIndex);
        if (accessor.Count == 0) return Array.Empty<float>();
        if (accessor.Type != "SCALAR")
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_TYPE", $"Accessor {accessorIndex} for {semantic} is {accessor.Type}; expected SCALAR.");
            return Array.Empty<float>();
        }
        var values = new float[accessor.Count];
        for (var i = 0; i < values.Length; i++) values[i] = ReadComponentAsFloat(context.BinaryChunk, accessor, i, 0);
        return values;
    }

    private static Vector4[] ReadVec4Accessor(JsonElement root, ImportContext context, int accessorIndex, string semantic)
    {
        var accessor = ResolveAccessor(root, context, accessorIndex);
        if (accessor.Count == 0) return Array.Empty<Vector4>();
        if (accessor.Type != "VEC4")
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_TYPE", $"Accessor {accessorIndex} for {semantic} is {accessor.Type}; expected VEC4.");
            return Array.Empty<Vector4>();
        }
        var values = new Vector4[accessor.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new Vector4(
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 0),
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 1),
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 2),
                ReadComponentAsFloat(context.BinaryChunk, accessor, i, 3));
        }
        return values;
    }

    private static Vector4[] ReadVec4IntAccessor(JsonElement root, ImportContext context, int accessorIndex, string semantic)
    {
        var accessor = ResolveAccessor(root, context, accessorIndex);
        if (accessor.Count == 0) return Array.Empty<Vector4>();
        if (accessor.Type != "VEC4")
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_TYPE", $"Accessor {accessorIndex} for {semantic} is {accessor.Type}; expected VEC4.");
            return Array.Empty<Vector4>();
        }
        var values = new Vector4[accessor.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new Vector4(
                ReadComponentAsInt(context.BinaryChunk, accessor, i, 0),
                ReadComponentAsInt(context.BinaryChunk, accessor, i, 1),
                ReadComponentAsInt(context.BinaryChunk, accessor, i, 2),
                ReadComponentAsInt(context.BinaryChunk, accessor, i, 3));
        }
        return values;
    }

    private static Matrix4x4[] ReadMat4Accessor(JsonElement root, ImportContext context, int accessorIndex, string semantic)
    {
        var accessor = ResolveAccessor(root, context, accessorIndex);
        if (accessor.Count == 0) return Array.Empty<Matrix4x4>();
        if (accessor.Type != "MAT4")
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_TYPE", $"Accessor {accessorIndex} for {semantic} is {accessor.Type}; expected MAT4.");
            return Array.Empty<Matrix4x4>();
        }
        var values = new Matrix4x4[accessor.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var m = new float[16];
            for (var c = 0; c < 16; c++) m[c] = ReadComponentAsFloat(context.BinaryChunk, accessor, i, c);
            values[i] = new Matrix4x4(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);
        }
        return values;
    }


    private static VertexSkinWeights3D[]? BuildSkinWeights(Vector4[]? joints, Vector4[]? weights, int vertexCount, string meshName, int primitiveIndex, ModelImportDiagnostics diagnostics)
    {
        if (joints is null || weights is null || joints.Length != vertexCount || weights.Length != vertexCount)
        {
            if (joints is not null || weights is not null)
            {
                diagnostics.Warning("GLTF_SKIN_WEIGHTS_INCOMPLETE", $"Mesh '{meshName}' primitive {primitiveIndex} has incomplete JOINTS_0/WEIGHTS_0 data. Skinning data was ignored for this primitive.");
            }
            return null;
        }

        var result = new VertexSkinWeights3D[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            result[i] = new VertexSkinWeights3D(joints[i], weights[i]).Normalize();
        }
        return result;
    }

    private static IReadOnlyList<SkinAsset3D> ReadSkins(JsonElement root, IReadOnlyList<ModelNode3D> nodes, ImportContext context)
    {
        if (!root.TryGetProperty("skins", out var skinsElement) || skinsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SkinAsset3D>();
        }

        var skins = new List<SkinAsset3D>();
        var index = 0;
        foreach (var skinElement in skinsElement.EnumerateArray())
        {
            var name = GetString(skinElement, "name") ?? $"Skin_{index}";
            var skeletonRoot = skinElement.TryGetProperty("skeleton", out var skeletonElement) ? skeletonElement.GetInt32() : (int?)null;
            var joints = new List<int>();
            if (skinElement.TryGetProperty("joints", out var jointsElement) && jointsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var joint in jointsElement.EnumerateArray()) joints.Add(joint.GetInt32());
            }

            if (joints.Count == 0)
            {
                context.Diagnostics.Warning("GLTF_SKIN_NO_JOINTS", $"Skin '{name}' has no joints and was skipped.");
                index++;
                continue;
            }

            var inverseBindMatrices = Array.Empty<Matrix4x4>();
            if (skinElement.TryGetProperty("inverseBindMatrices", out var ibmElement))
            {
                inverseBindMatrices = ReadMat4Accessor(root, context, ibmElement.GetInt32(), "inverseBindMatrices");
            }

            var jointToBone = new Dictionary<int, int>();
            for (var i = 0; i < joints.Count; i++) jointToBone[joints[i]] = i;
            var bones = new List<BoneAsset3D>();
            for (var i = 0; i < joints.Count; i++)
            {
                var nodeIndex = joints[i];
                var nodeName = nodeIndex >= 0 && nodeIndex < nodes.Count ? nodes[nodeIndex].Name : $"Joint_{nodeIndex}";
                int? parentBone = null;
                if (nodeIndex >= 0 && nodeIndex < nodes.Count && nodes[nodeIndex].ParentIndex.HasValue && jointToBone.TryGetValue(nodes[nodeIndex].ParentIndex.Value, out var parentIndex))
                {
                    parentBone = parentIndex;
                }
                var inverseBind = i < inverseBindMatrices.Length ? inverseBindMatrices[i] : Matrix4x4.Identity;
                bones.Add(new BoneAsset3D(i, nodeIndex, nodeName, parentBone, inverseBind));
            }

            skins.Add(new SkinAsset3D(index, name, skeletonRoot, bones));
            context.Diagnostics.Info("GLTF_SKIN_IMPORTED", $"Imported skin '{name}' with {bones.Count} bones.");
            index++;
        }

        return skins;
    }

    private static IReadOnlyList<AnimationClip3D> ReadAnimations(JsonElement root, IReadOnlyList<ModelNode3D> nodes, ImportContext context)
    {
        if (!root.TryGetProperty("animations", out var animationsElement) || animationsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AnimationClip3D>();
        }

        var clips = new List<AnimationClip3D>();
        var animationIndex = 0;
        foreach (var animationElement in animationsElement.EnumerateArray())
        {
            var name = GetString(animationElement, "name") ?? $"Animation_{animationIndex}";
            var samplers = new List<AnimationSampler3D>();
            if (animationElement.TryGetProperty("samplers", out var samplersElement) && samplersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var samplerElement in samplersElement.EnumerateArray())
                {
                    var input = GetInt(samplerElement, "input", -1);
                    var output = GetInt(samplerElement, "output", -1);
                    var interpolation = ParseInterpolation(GetString(samplerElement, "interpolation") ?? "LINEAR");
                    var times = input >= 0 ? ReadFloatAccessor(root, context, input, "animation.input") : Array.Empty<float>();
                    var outputData = output >= 0
                        ? ReadAnimationOutputAccessor(root, context, output, times.Length, interpolation)
                        : AnimationOutputData.Empty;
                    if (outputData.Values.Length != times.Length)
                    {
                        samplers.Add(new AnimationSampler3D(Array.Empty<float>(), Array.Empty<Vector4>(), interpolation));
                    }
                    else
                    {
                        samplers.Add(new AnimationSampler3D(times, outputData.Values, interpolation, outputData.InTangents, outputData.OutTangents));
                    }
                }
            }

            var channels = new List<AnimationChannel3D>();
            if (animationElement.TryGetProperty("channels", out var channelsElement) && channelsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var channelElement in channelsElement.EnumerateArray())
                {
                    var samplerIndex = GetInt(channelElement, "sampler", -1);
                    if (samplerIndex < 0 || samplerIndex >= samplers.Count) continue;
                    if (!channelElement.TryGetProperty("target", out var targetElement)) continue;
                    var nodeIndex = GetInt(targetElement, "node", -1);
                    var path = ParseAnimationPath(GetString(targetElement, "path") ?? string.Empty);
                    if (path == AnimationPath3D.Weights)
                    {
                        context.Diagnostics.Warning("GLTF_ANIMATION_WEIGHTS_UNSUPPORTED", $"Animation '{name}' contains morph target weights. Morph animation is not evaluated in this stage.");
                    }
                    if (path == AnimationPath3D.Unsupported || nodeIndex < 0 || nodeIndex >= nodes.Count) continue;
                    channels.Add(new AnimationChannel3D(nodeIndex, path, samplers[samplerIndex]));
                }
            }

            var clip = new AnimationClip3D(animationIndex, name, channels);
            clips.Add(clip);
            context.Diagnostics.Info("GLTF_ANIMATION_IMPORTED", $"Imported animation '{clip.Name}' with {clip.Channels.Count} channels and duration {clip.Duration:0.###}s.");
            animationIndex++;
        }

        return clips;
    }

    private static AnimationOutputData ReadAnimationOutputAccessor(JsonElement root, ImportContext context, int accessorIndex, int keyCount, AnimationInterpolation3D interpolation)
    {
        var accessor = ResolveAccessor(root, context, accessorIndex);
        if (accessor.Count == 0) return AnimationOutputData.Empty;
        var raw = new Vector4[accessor.Count];
        for (var i = 0; i < raw.Length; i++)
        {
            raw[i] = new Vector4(
                accessor.ComponentCount > 0 ? ReadComponentAsFloat(context.BinaryChunk, accessor, i, 0) : 0f,
                accessor.ComponentCount > 1 ? ReadComponentAsFloat(context.BinaryChunk, accessor, i, 1) : 0f,
                accessor.ComponentCount > 2 ? ReadComponentAsFloat(context.BinaryChunk, accessor, i, 2) : 0f,
                accessor.ComponentCount > 3 ? ReadComponentAsFloat(context.BinaryChunk, accessor, i, 3) : 0f);
        }

        if (interpolation == AnimationInterpolation3D.CubicSpline)
        {
            if (keyCount <= 0 || raw.Length != keyCount * 3)
            {
                context.Diagnostics.Error("GLTF_CUBIC_SPLINE_COUNT", $"CUBICSPLINE output contains {raw.Length} elements for {keyCount} input keys; exactly three output elements per key are required.");
                return AnimationOutputData.Empty;
            }
            var inTangents = new Vector4[keyCount];
            var values = new Vector4[keyCount];
            var outTangents = new Vector4[keyCount];
            for (var i = 0; i < keyCount; i++)
            {
                inTangents[i] = raw[i * 3];
                values[i] = raw[i * 3 + 1];
                outTangents[i] = raw[i * 3 + 2];
            }
            return new AnimationOutputData(values, inTangents, outTangents);
        }

        if (raw.Length != keyCount)
        {
            context.Diagnostics.Error("GLTF_ANIMATION_OUTPUT_COUNT", $"Animation output contains {raw.Length} elements for {keyCount} input keys.");
            return AnimationOutputData.Empty;
        }
        return new AnimationOutputData(raw, null, null);
    }

    private readonly record struct AnimationOutputData(Vector4[] Values, Vector4[]? InTangents, Vector4[]? OutTangents)
    {
        public static AnimationOutputData Empty => new(Array.Empty<Vector4>(), null, null);
    }

    private static AnimationInterpolation3D ParseInterpolation(string value)
        => value switch
        {
            "STEP" => AnimationInterpolation3D.Step,
            "CUBICSPLINE" => AnimationInterpolation3D.CubicSpline,
            _ => AnimationInterpolation3D.Linear
        };

    private static AnimationPath3D ParseAnimationPath(string value)
        => value switch
        {
            "translation" => AnimationPath3D.Translation,
            "rotation" => AnimationPath3D.Rotation,
            "scale" => AnimationPath3D.Scale,
            "weights" => AnimationPath3D.Weights,
            _ => AnimationPath3D.Unsupported
        };

    private static AccessorView ResolveAccessor(JsonElement root, ImportContext context, int accessorIndex)
    {
        if (!root.TryGetProperty("accessors", out var accessors) || accessorIndex < 0 || accessorIndex >= accessors.GetArrayLength())
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_MISSING", $"Accessor index {accessorIndex} is missing.");
            return AccessorView.Empty;
        }

        var accessor = accessors[accessorIndex];
        var bufferViewIndex = GetInt(accessor, "bufferView", -1);
        if (bufferViewIndex < 0)
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_NO_BUFFERVIEW", $"Accessor {accessorIndex} has no bufferView. Sparse accessors are not supported in this stage.");
            return AccessorView.Empty;
        }

        var bufferView = ResolveBufferView(root, context, bufferViewIndex);
        var componentType = GetInt(accessor, "componentType", 5126);
        var type = GetString(accessor, "type") ?? "SCALAR";
        var count = GetInt(accessor, "count", 0);
        var accessorByteOffset = GetInt(accessor, "byteOffset", 0);
        var normalized = GetBool(accessor, "normalized", false);
        var componentCount = GetComponentCount(type);
        var componentSize = GetComponentSize(componentType);
        if (componentCount <= 0 || componentSize <= 0 || count < 0 || accessorByteOffset < 0)
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_INVALID", $"Accessor {accessorIndex} has invalid layout metadata.");
            return AccessorView.Empty;
        }

        var elementSize = componentCount * componentSize;
        var stride = bufferView.ByteStride > 0 ? bufferView.ByteStride : elementSize;
        if (stride < elementSize)
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_STRIDE_INVALID", $"Accessor {accessorIndex} stride is smaller than its element size.");
            return AccessorView.Empty;
        }

        var byteOffsetLong = (long)bufferView.ByteOffset + accessorByteOffset;
        var lastByteExclusive = count == 0 ? byteOffsetLong : byteOffsetLong + (long)(count - 1) * stride + elementSize;
        var bufferViewEnd = bufferView.ByteOffset + (long)bufferView.ByteLength;
        if (bufferView.ByteOffset < 0 || bufferView.ByteLength < 0 || byteOffsetLong < bufferView.ByteOffset || byteOffsetLong > int.MaxValue || lastByteExclusive > bufferViewEnd || lastByteExclusive > context.BinaryChunk.Length)
        {
            context.Diagnostics.Error("GLTF_ACCESSOR_OUT_OF_RANGE", $"Accessor {accessorIndex} extends outside its bufferView or the GLB binary chunk.");
            return AccessorView.Empty;
        }

        return new AccessorView((int)byteOffsetLong, stride, count, componentType, type, normalized, componentCount);
    }

    private static BufferViewInfo ResolveBufferView(JsonElement root, ImportContext context, int bufferViewIndex)
    {
        if (!root.TryGetProperty("bufferViews", out var bufferViews) || bufferViewIndex < 0 || bufferViewIndex >= bufferViews.GetArrayLength())
        {
            context.Diagnostics.Error("GLTF_BUFFERVIEW_MISSING", $"bufferView index {bufferViewIndex} is missing.");
            return BufferViewInfo.Empty;
        }

        var view = bufferViews[bufferViewIndex];
        var bufferIndex = GetInt(view, "buffer", 0);
        if (bufferIndex != 0)
        {
            var message = $"bufferView {bufferViewIndex} references buffer {bufferIndex}; this importer stage resolves only buffer 0. Multi-buffer glTF support must be enabled before this asset can be loaded safely.";
            if (context.Options.StrictValidation)
            {
                context.Diagnostics.Error("GLTF_MULTIBUFFER_UNSUPPORTED", message);
            }
            else
            {
                context.Diagnostics.Warning("GLTF_MULTIBUFFER_UNSUPPORTED", message);
            }
            return BufferViewInfo.Empty;
        }

        var byteOffset = GetInt(view, "byteOffset", 0);
        var byteLength = GetInt(view, "byteLength", 0);
        var byteStride = GetInt(view, "byteStride", 0);
        if (byteOffset < 0 || byteLength < 0 || byteOffset + (long)byteLength > context.BinaryChunk.Length)
        {
            context.Diagnostics.Error("GLTF_BUFFERVIEW_OUT_OF_RANGE", $"bufferView {bufferViewIndex} extends outside the GLB binary chunk.");
            return BufferViewInfo.Empty;
        }

        return new BufferViewInfo(byteOffset, byteLength, byteStride);
    }

    private static byte[]? ReadBufferViewBytes(JsonElement root, ImportContext context, int bufferViewIndex)
    {
        var view = ResolveBufferView(root, context, bufferViewIndex);
        if (view.ByteLength <= 0) return null;
        if (view.ByteOffset < 0 || view.ByteOffset + view.ByteLength > context.BinaryChunk.Length)
        {
            context.Diagnostics.Warning("GLTF_IMAGE_BUFFERVIEW_OUT_OF_RANGE", $"Image bufferView {bufferViewIndex} is out of range.");
            return null;
        }

        var bytes = new byte[view.ByteLength];
        Array.Copy(context.BinaryChunk, view.ByteOffset, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float ReadComponentAsFloat(byte[] buffer, AccessorView accessor, int elementIndex, int componentIndex)
    {
        var offset = accessor.ByteOffset + elementIndex * accessor.ByteStride + componentIndex * GetComponentSize(accessor.ComponentType);
        if (offset < 0 || offset + GetComponentSize(accessor.ComponentType) > buffer.Length) return 0f;
        return accessor.ComponentType switch
        {
            5126 => BitConverter.ToSingle(buffer, offset),
            5120 => NormalizeIfNeeded((sbyte)buffer[offset], sbyte.MinValue, sbyte.MaxValue, accessor.Normalized),
            5121 => NormalizeIfNeeded(buffer[offset], byte.MinValue, byte.MaxValue, accessor.Normalized),
            5122 => NormalizeIfNeeded(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2)), short.MinValue, short.MaxValue, accessor.Normalized),
            5123 => NormalizeIfNeeded(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2)), ushort.MinValue, ushort.MaxValue, accessor.Normalized),
            _ => 0f
        };
    }

    private static int ReadComponentAsInt(byte[] buffer, AccessorView accessor, int elementIndex, int componentIndex)
    {
        var offset = accessor.ByteOffset + elementIndex * accessor.ByteStride + componentIndex * GetComponentSize(accessor.ComponentType);
        if (offset < 0 || offset + GetComponentSize(accessor.ComponentType) > buffer.Length) return 0;
        return accessor.ComponentType switch
        {
            5120 => (sbyte)buffer[offset],
            5121 => buffer[offset],
            5122 => BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2)),
            5123 => BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2)),
            5125 => unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4))),
            _ => 0
        };
    }

    private static float NormalizeIfNeeded(int value, int min, int max, bool normalized)
    {
        if (!normalized) return value;
        if (min < 0)
        {
            return value < 0
                ? MathF.Max(value / (float)global::System.Math.Abs(min), -1f)
                : value / (float)max;
        }

        return value / (float)max;
    }

    private static int GetComponentSize(int componentType)
    {
        return componentType switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            _ => 0
        };
    }

    private static int GetComponentCount(string type)
    {
        return type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT2" => 4,
            "MAT3" => 9,
            "MAT4" => 16,
            _ => 0
        };
    }


    private static bool ValidatePrimitiveIndices(int[] indices, int vertexCount, string meshName, int primitiveIndex, ModelImportDiagnostics diagnostics)
    {
        if (indices.Length == 0)
        {
            diagnostics.Warning("GLTF_PRIMITIVE_NO_INDICES", $"Mesh '{meshName}' primitive {primitiveIndex} has no indices and was skipped.");
            return false;
        }

        if (indices.Length % 3 != 0)
        {
            diagnostics.Error("GLTF_PRIMITIVE_BAD_INDEX_COUNT", $"Mesh '{meshName}' primitive {primitiveIndex} index count is not divisible by 3.");
            return false;
        }

        for (var i = 0; i < indices.Length; i++)
        {
            if ((uint)indices[i] >= (uint)vertexCount)
            {
                diagnostics.Error("GLTF_PRIMITIVE_INDEX_OUT_OF_RANGE", $"Mesh '{meshName}' primitive {primitiveIndex} index {i} points outside the POSITION accessor.");
                return false;
            }
        }

        return true;
    }

    private static int[] CreateSequentialIndices(int vertexCount)
    {
        var indices = new int[vertexCount];
        for (var i = 0; i < indices.Length; i++) indices[i] = i;
        return indices;
    }


    private static bool IsDataUri(string uri)
        => uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeDataUri(string uri, long maxDecodedBytes, out byte[]? bytes, out string? mimeType)
    {
        bytes = null;
        mimeType = null;
        try
        {
            var comma = uri.IndexOf(',');
            if (comma <= 5) return false;
            var header = uri.Substring(5, comma - 5);
            var payload = uri[(comma + 1)..];
            var parts = header.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && parts[0].Contains('/')) mimeType = parts[0];
            var isBase64 = header.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0;
            if (maxDecodedBytes > 0)
            {
                var estimatedDecodedBytes = isBase64
                    ? (long)global::System.Math.Ceiling(payload.Length * 3.0 / 4.0)
                    : Encoding.UTF8.GetByteCount(payload);
                if (estimatedDecodedBytes > maxDecodedBytes) return false;
            }

            bytes = isBase64 ? Convert.FromBase64String(payload) : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return bytes.Length > 0 && (maxDecodedBytes <= 0 || bytes.LongLength <= maxDecodedBytes);
        }
        catch
        {
            bytes = null;
            mimeType = null;
            return false;
        }
    }

    private static byte[]? TryReadExternalBytes(string sourcePath, string relativeUri, ModelImportOptions options, ModelImportDiagnostics diagnostics, string notFoundCode, long maxBytes)
    {
        var baseUri = string.IsNullOrWhiteSpace(options.BaseDirectory) ? sourcePath : options.BaseDirectory!;
        var resolver = options.AssetResolver ?? FileSystemAssetResolver3D.Shared;
        try
        {
            using var stream = resolver.Open(baseUri, relativeUri);
            if (stream is null)
            {
                diagnostics.Warning(notFoundCode, $"Could not resolve external asset '{relativeUri}' relative to '{baseUri}'.");
                return null;
            }

            return ReadStreamBounded(stream, maxBytes, diagnostics, "GLTF_EXTERNAL_ASSET_TOO_LARGE");
        }
        catch (Exception ex)
        {
            diagnostics.Warning("GLTF_EXTERNAL_ASSET_READ_FAILED", $"Could not read external asset '{relativeUri}': {ex.Message}");
            return null;
        }
    }

    private static byte[]? ReadStreamBounded(Stream stream, long maxBytes, ModelImportDiagnostics diagnostics, string errorCode)
    {
        if (maxBytes > 0 && stream.CanSeek && stream.Length > maxBytes)
        {
            diagnostics.Error(errorCode, $"Stream is {stream.Length} bytes; configured limit is {maxBytes} bytes.");
            return null;
        }

        using var memory = maxBytes > 0 && maxBytes < int.MaxValue ? new MemoryStream((int)global::System.Math.Min(maxBytes, 1024 * 1024)) : new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            total += read;
            if (maxBytes > 0 && total > maxBytes)
            {
                diagnostics.Error(errorCode, $"Stream exceeds configured limit of {maxBytes} bytes.");
                return null;
            }
            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static byte[]? TryReadSidecarImageBytes(string sourcePath, string imageName, ModelImportOptions options, ModelImportDiagnostics diagnostics, out string? mimeType)
    {
        mimeType = null;
        var baseUri = string.IsNullOrWhiteSpace(options.BaseDirectory) ? sourcePath : options.BaseDirectory!;
        var resolver = options.AssetResolver ?? FileSystemAssetResolver3D.Shared;
        var sourceStem = GetSourceStem(sourcePath);
        var candidates = new List<string>();
        AddSidecarCandidates(candidates, sourceStem);
        AddSidecarCandidates(candidates, imageName);

        foreach (var candidate in candidates)
        {
            try
            {
                using var stream = resolver.Open(baseUri, candidate);
                if (stream is null) continue;
                var data = ReadStreamBounded(stream, options.MaxTextureBytes, diagnostics, "GLTF_SIDECAR_IMAGE_TOO_LARGE");
                if (data is null) continue;
                if (data.Length == 0) continue;
                mimeType = GuessMimeType(candidate);
                diagnostics.Info("GLTF_SIDECAR_IMAGE_RESOLVED", $"Resolved missing image payload from sidecar '{candidate}'.");
                return data;
            }
            catch
            {
                // Try the next candidate; sidecar probing is intentionally non-fatal.
            }
        }

        return null;
    }

    private static void AddSidecarCandidates(List<string> candidates, string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) return;
        var safe = stem.Trim();
        var extensions = new[] { ".png", ".jpg", ".jpeg", ".webp" };
        foreach (var ext in extensions)
        {
            var candidate = safe + ext;
            if (!candidates.Exists(existing => string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase))) candidates.Add(candidate);
        }
    }

    private static string? GetSourceStem(string sourcePath)
    {
        try
        {
            if (Uri.TryCreate(sourcePath, UriKind.Absolute, out var uri))
            {
                var segment = uri.Segments.Length == 0 ? sourcePath : uri.Segments[^1];
                return Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(segment));
            }

            return Path.GetFileNameWithoutExtension(sourcePath);
        }
        catch
        {
            return null;
        }
    }

    private static string? GuessMimeType(string pathOrUri)
    {
        var ext = Path.GetExtension(pathOrUri).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
    }

    private static string CreateAssetId(string path)
    {
        var source = path ?? string.Empty;
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                source = uri.IsFile ? Path.GetFullPath(uri.LocalPath) : uri.ToString();
            }
            else
            {
                source = Path.GetFullPath(source);
            }
        }
        catch
        {
            source = path ?? string.Empty;
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source.ToLowerInvariant()));
        return "model:" + Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement element, string name, int fallback)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : fallback;

    private static bool GetBool(JsonElement element, string name, bool fallback)
        => element.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) ? value.GetBoolean() : fallback;

    private static float GetSingle(JsonElement element, string name, float fallback)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : fallback;

    private static ColorRgba ReadColor(JsonElement array, ColorRgba fallback)
    {
        var values = ReadFloatArray(array, 4);
        if (values.Length < 4) return fallback;
        return new ColorRgba(values[0], values[1], values[2], values[3]);
    }

    private static Vector3 ReadVector3Property(JsonElement element, string name, Vector3 fallback)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return fallback;
        var values = ReadFloatArray(array, 3);
        return values.Length >= 3 ? new Vector3(values[0], values[1], values[2]) : fallback;
    }

    private static Quaternion ReadQuaternionProperty(JsonElement element, string name, Quaternion fallback)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return fallback;
        var values = ReadFloatArray(array, 4);
        return values.Length >= 4 ? new Quaternion(values[0], values[1], values[2], values[3]) : fallback;
    }

    private static float[] ReadFloatArray(JsonElement array, int expected)
    {
        var values = new List<float>(expected);
        foreach (var item in array.EnumerateArray()) values.Add(item.GetSingle());
        return values.ToArray();
    }

    private readonly record struct ImagePayload(string Name, string? MimeType, string? Uri, byte[]? Data);

    private sealed record GlbContainer(string Json, byte[] BinaryChunk);
    private sealed record ImportContext(string SourcePath, byte[] BinaryChunk, ModelImportDiagnostics Diagnostics, ModelImportOptions Options)
    {
        public string AssetId { get; } = CreateAssetId(SourcePath);
    }
    private readonly record struct BufferViewInfo(int ByteOffset, int ByteLength, int ByteStride)
    {
        public static BufferViewInfo Empty => new(0, 0, 0);
    }
    private readonly record struct AccessorView(int ByteOffset, int ByteStride, int Count, int ComponentType, string Type, bool Normalized, int ComponentCount)
    {
        public static AccessorView Empty => new(0, 0, 0, 5126, "SCALAR", false, 1);
    }
    private sealed record TempNode(int Index, string Name, int? MeshIndex, int? SkinIndex, IReadOnlyList<int> Children, Matrix4x4 LocalTransform);
}
