using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.OpenGL.Rendering;

internal sealed class OpenGlSceneRenderer
{
    private const int GlColorBufferBit = 0x00004000;
    private const int GlDepthBufferBit = 0x00000100;
    private const int GlTriangles = 0x0004;
    private const int GlFloat = 0x1406;
    private const int GlUnsignedInt = 0x1405;
    private const int GlArrayBuffer = 0x8892;
    private const int GlElementArrayBuffer = 0x8893;
    private const int GlStaticDraw = 0x88E4;
    private const int GlDynamicDraw = 0x88E8;
    private const int GlDepthTest = 0x0B71;
    private const int GlBlend = 0x0BE2;
    private const int GlTexture2D = 0x0DE1;
    private const int GlTexture0 = 0x84C0;
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlLinear = 0x2601;
    private const int GlClampToEdge = 0x812F;
    private const int GlRgba = 0x1908;
    private const int GlUnsignedByte = 0x1401;
    private const int GlVertexShader = 0x8B31;
    private const int GlFragmentShader = 0x8B30;
    private const int GlSrcAlpha = 0x0302;
    private const int GlOne = 1;
    private const int GlOneMinusSrcAlpha = 0x0303;

    private readonly Dictionary<string, MeshGpuResource> _meshResources = new();
    private readonly Dictionary<string, ControlTextureResource> _controlTextures = new();

    private int _meshProgram;
    private int _texturedProgram;
    private int _meshPositionLocation;
    private int _meshColorLocation;
    private int _texturePositionLocation;
    private int _textureUvLocation;
    private int _textureSamplerLocation;
    private int _controlVertexBuffer;
    private int _controlIndexBuffer;
    private bool _initialized;
    private GlBlendFuncDelegate? _blendFunc;
    private GlDepthMaskDelegate? _depthMask;
    private GlDisableDelegate? _disable;
    private GlUniform1iDelegate? _uniform1i;
    private GlUniform4fDelegate? _uniform4f;

    public void Initialize(GlInterface gl)
    {
        if (_initialized)
        {
            return;
        }

        _blendFunc = LoadDelegate<GlBlendFuncDelegate>(gl, "glBlendFunc");
        _depthMask = LoadDelegate<GlDepthMaskDelegate>(gl, "glDepthMask");
        _disable = LoadDelegate<GlDisableDelegate>(gl, "glDisable");
        _uniform1i = LoadDelegate<GlUniform1iDelegate>(gl, "glUniform1i");
        _uniform4f = LoadDelegate<GlUniform4fDelegate>(gl, "glUniform4f");

        _meshProgram = CreateProgram(gl, MeshVertexSource, MeshFragmentSource, (0, "aPosition"));
        _meshPositionLocation = gl.GetAttribLocationString(_meshProgram, "aPosition");
        _meshColorLocation = gl.GetUniformLocationString(_meshProgram, "uColor");

        _texturedProgram = CreateProgram(gl, TexturedVertexSource, TexturedFragmentSource, (0, "aPosition"), (1, "aTexCoord"));
        _texturePositionLocation = gl.GetAttribLocationString(_texturedProgram, "aPosition");
        _textureUvLocation = gl.GetAttribLocationString(_texturedProgram, "aTexCoord");
        _textureSamplerLocation = gl.GetUniformLocationString(_texturedProgram, "uTexture");

        _controlVertexBuffer = gl.GenBuffer();
        _controlIndexBuffer = gl.GenBuffer();

        var indices = new[] { 0, 1, 2, 0, 2, 3 };
        gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
        UploadInts(gl, GlElementArrayBuffer, indices, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, 0);

        _initialized = true;
    }

    public void Render(GlInterface gl, int framebuffer, Scene3D scene, Rect bounds)
    {
        if (!_initialized)
        {
            Initialize(gl);
        }

        var width = System.Math.Max((int)System.Math.Ceiling(bounds.Width), 1);
        var height = System.Math.Max((int)System.Math.Ceiling(bounds.Height), 1);

        gl.BindFramebuffer(0x8D40, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.Enable(GlDepthTest);
        gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);
        gl.Clear(GlColorBufferBit | GlDepthBufferBit);

        var aspect = (float)width / height;
        var view = scene.Camera.GetViewMatrix();
        var projection = scene.Camera.GetProjectionMatrix(aspect);

        DrawMeshes(gl, scene, view, projection);
        DrawControlPlanes(gl, scene, view, projection);

        gl.BindBuffer(GlArrayBuffer, 0);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        gl.BindTexture(GlTexture2D, 0);
        gl.UseProgram(0);
    }

    public void Deinitialize(GlInterface gl)
    {
        foreach (var resource in _meshResources.Values)
        {
            resource.Dispose(gl);
        }
        _meshResources.Clear();

        foreach (var texture in _controlTextures.Values)
        {
            texture.Dispose(gl);
        }
        _controlTextures.Clear();

        if (_controlVertexBuffer != 0)
        {
            gl.DeleteBuffer(_controlVertexBuffer);
            _controlVertexBuffer = 0;
        }

        if (_controlIndexBuffer != 0)
        {
            gl.DeleteBuffer(_controlIndexBuffer);
            _controlIndexBuffer = 0;
        }

        if (_meshProgram != 0)
        {
            gl.DeleteProgram(_meshProgram);
            _meshProgram = 0;
        }

        if (_texturedProgram != 0)
        {
            gl.DeleteProgram(_texturedProgram);
            _texturedProgram = 0;
        }

        _initialized = false;
    }

    public void Reset()
    {
        _initialized = false;
        _meshResources.Clear();
        _controlTextures.Clear();
        _controlVertexBuffer = 0;
        _controlIndexBuffer = 0;
        _meshProgram = 0;
        _texturedProgram = 0;
    }

    private void DrawMeshes(GlInterface gl, Scene3D scene, Matrix4x4 view, Matrix4x4 projection)
    {
        gl.UseProgram(_meshProgram);

        foreach (var obj in scene.Objects)
        {
            if (!obj.IsVisible || !obj.UseMeshRendering)
            {
                continue;
            }

            var mesh = obj.GetMesh();
            var model = obj.GetModelMatrix();
            var resource = EnsureMeshResource(gl, obj.Id, obj.GeometryVersion, mesh, model, view, projection);

            var color = obj.Fill;
            if (obj.IsHovered)
            {
                color = color.BlendTowards(ColorRgba.White, 0.10f);
            }
            if (obj.IsSelected)
            {
                color = color.BlendTowards(ColorRgba.White, 0.22f);
            }

            gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
            gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
            gl.EnableVertexAttribArray(_meshPositionLocation);
            gl.VertexAttribPointer(_meshPositionLocation, 3, GlFloat, 0, sizeof(float) * 3, IntPtr.Zero);
            UploadColor(_uniform4f, _meshColorLocation, color);
            gl.DrawElements(GlTriangles, resource.IndexCount, GlUnsignedInt, IntPtr.Zero);
        }
    }

    private void DrawControlPlanes(GlInterface gl, Scene3D scene, Matrix4x4 view, Matrix4x4 projection)
    {
        var planes = new List<(ControlPlane3D Plane, float Depth)>();
        foreach (var obj in scene.Objects)
        {
            if (obj is not ControlPlane3D plane || !plane.IsVisible || plane.Snapshot is null)
            {
                continue;
            }

            var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
            var depth = 0f;
            for (var i = 0; i < corners.Length; i++)
            {
                depth += Vector3.DistanceSquared(scene.Camera.Position, corners[i]);
            }
            planes.Add((plane, depth / 4f));
        }

        if (planes.Count == 0)
        {
            return;
        }

        gl.Enable(GlBlend);
        _blendFunc?.Invoke(GlOne, GlOneMinusSrcAlpha);
        _depthMask?.Invoke(0);
        gl.UseProgram(_texturedProgram);
        gl.ActiveTexture(GlTexture0);
        if (_textureSamplerLocation >= 0)
        {
            _uniform1i?.Invoke(_textureSamplerLocation, 0);
        }

        foreach (var (plane, _) in planes)
        {
            var texture = EnsureControlTexture(gl, plane);
            if (texture is null)
            {
                continue;
            }

            var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
            if (!TryBuildProjectedControlVertices(corners, view, projection, out var vertexData))
            {
                continue;
            }

            gl.BindTexture(GlTexture2D, texture.TextureId);
            gl.BindBuffer(GlArrayBuffer, _controlVertexBuffer);
            UploadFloats(gl, GlArrayBuffer, vertexData, GlDynamicDraw);
            gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);

            gl.EnableVertexAttribArray(_texturePositionLocation);
            gl.VertexAttribPointer(_texturePositionLocation, 3, GlFloat, 0, sizeof(float) * 5, IntPtr.Zero);
            gl.EnableVertexAttribArray(_textureUvLocation);
            gl.VertexAttribPointer(_textureUvLocation, 2, GlFloat, 0, sizeof(float) * 5, new IntPtr(sizeof(float) * 3));
            gl.DrawElements(GlTriangles, 6, GlUnsignedInt, IntPtr.Zero);
        }

        _depthMask?.Invoke(1);
        _disable?.Invoke(GlBlend);
    }

    private MeshGpuResource EnsureMeshResource(GlInterface gl, string id, int geometryVersion, Mesh3D mesh, Matrix4x4 model, Matrix4x4 view, Matrix4x4 projection)
    {
        if (!_meshResources.TryGetValue(id, out var resource) || resource.GeometryVersion != geometryVersion)
        {
            resource?.Dispose(gl);
            resource = new MeshGpuResource
            {
                GeometryVersion = geometryVersion,
                VertexBuffer = gl.GenBuffer(),
                IndexBuffer = gl.GenBuffer(),
                IndexCount = mesh.Indices.Length
            };
            gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
            UploadInts(gl, GlElementArrayBuffer, mesh.Indices, GlStaticDraw);
            _meshResources[id] = resource;
        }

        var clipPositions = BuildProjectedMesh(mesh.Positions, model, view, projection);
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        UploadFloats(gl, GlArrayBuffer, clipPositions, GlDynamicDraw);
        return resource;
    }

    private ControlTextureResource? EnsureControlTexture(GlInterface gl, ControlPlane3D plane)
    {
        var snapshot = plane.Snapshot;
        if (snapshot is null)
        {
            return null;
        }

        if (!_controlTextures.TryGetValue(plane.Id, out var resource))
        {
            resource = new ControlTextureResource
            {
                TextureId = gl.GenTexture(),
                SnapshotVersion = -1
            };
            _controlTextures[plane.Id] = resource;
        }

        if (resource.SnapshotVersion == plane.SnapshotVersion)
        {
            return resource;
        }

        var pixelWidth = System.Math.Max(plane.RenderPixelWidth, 1);
        var pixelHeight = System.Math.Max(plane.RenderPixelHeight, 1);
        var stride = pixelWidth * 4;
        var bufferSize = stride * pixelHeight;
        var bgraPixels = new byte[bufferSize];
        var bgraHandle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
        try
        {
            snapshot.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), bgraHandle.AddrOfPinnedObject(), bufferSize, stride);
        }
        finally
        {
            bgraHandle.Free();
        }

        var rgbaPixels = new byte[bufferSize];
        for (var i = 0; i < bufferSize; i += 4)
        {
            rgbaPixels[i + 0] = bgraPixels[i + 2];
            rgbaPixels[i + 1] = bgraPixels[i + 1];
            rgbaPixels[i + 2] = bgraPixels[i + 0];
            rgbaPixels[i + 3] = bgraPixels[i + 3];
        }

        var rgbaHandle = GCHandle.Alloc(rgbaPixels, GCHandleType.Pinned);
        try
        {
            gl.BindTexture(GlTexture2D, resource.TextureId);
            gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
            gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
            gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
            gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
            gl.TexImage2D(GlTexture2D, 0, GlRgba, pixelWidth, pixelHeight, 0, GlRgba, GlUnsignedByte, rgbaHandle.AddrOfPinnedObject());
            resource.SnapshotVersion = plane.SnapshotVersion;
            resource.Width = pixelWidth;
            resource.Height = pixelHeight;
        }
        finally
        {
            rgbaHandle.Free();
        }

        return resource;
    }

    private static float[] BuildProjectedMesh(Vector3[] positions, Matrix4x4 model, Matrix4x4 view, Matrix4x4 projection)
    {
        var result = new float[positions.Length * 3];
        for (var i = 0; i < positions.Length; i++)
        {
            var world = Vector3.Transform(positions[i], model);
            var ndc = ProjectToNdc(world, view, projection);
            var baseIndex = i * 3;
            result[baseIndex] = ndc.X;
            result[baseIndex + 1] = ndc.Y;
            result[baseIndex + 2] = ndc.Z;
        }
        return result;
    }

    private static bool TryBuildProjectedControlVertices(Vector3[] worldCorners, Matrix4x4 view, Matrix4x4 projection, out float[] vertexData)
    {
        vertexData = new float[20];
        for (var i = 0; i < 4; i++)
        {
            var clip = Vector4.Transform(Vector4.Transform(new Vector4(worldCorners[i], 1f), view), projection);
            if (System.MathF.Abs(clip.W) < 0.00001f || clip.W <= 0f)
            {
                vertexData = Array.Empty<float>();
                return false;
            }

            var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
            var baseIndex = i * 5;
            vertexData[baseIndex] = ndc.X;
            vertexData[baseIndex + 1] = ndc.Y;
            vertexData[baseIndex + 2] = ndc.Z;
        }

        // TL, TR, BR, BL
        vertexData[3] = 0f; vertexData[4] = 0f;
        vertexData[8] = 1f; vertexData[9] = 0f;
        vertexData[13] = 1f; vertexData[14] = 1f;
        vertexData[18] = 0f; vertexData[19] = 1f;
        return true;
    }

    private static Vector3 ProjectToNdc(Vector3 world, Matrix4x4 view, Matrix4x4 projection)
    {
        var clip = Vector4.Transform(Vector4.Transform(new Vector4(world, 1f), view), projection);
        if (System.MathF.Abs(clip.W) < 0.00001f)
        {
            return new Vector3(2f, 2f, 2f);
        }
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }

    private static void UploadFloats(GlInterface gl, int target, float[] data, int usage)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            gl.BufferData(target, new IntPtr(data.Length * sizeof(float)), handle.AddrOfPinnedObject(), usage);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void UploadInts(GlInterface gl, int target, int[] data, int usage)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            gl.BufferData(target, new IntPtr(data.Length * sizeof(int)), handle.AddrOfPinnedObject(), usage);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void UploadColor(GlUniform4fDelegate? uniform4f, int location, ColorRgba color)
    {
        if (location < 0)
        {
            return;
        }
        uniform4f?.Invoke(location, color.R, color.G, color.B, color.A);
    }

    private static int CreateProgram(GlInterface gl, string vertexSource, string fragmentSource, params (int Location, string Name)[] attributes)
    {
        var vertexShader = gl.CreateShader(GlVertexShader);
        var vertexError = gl.CompileShaderAndGetError(vertexShader, vertexSource);
        if (!string.IsNullOrWhiteSpace(vertexError))
        {
            throw new InvalidOperationException($"Vertex shader compilation failed: {vertexError}");
        }

        var fragmentShader = gl.CreateShader(GlFragmentShader);
        var fragmentError = gl.CompileShaderAndGetError(fragmentShader, fragmentSource);
        if (!string.IsNullOrWhiteSpace(fragmentError))
        {
            throw new InvalidOperationException($"Fragment shader compilation failed: {fragmentError}");
        }

        var program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        foreach (var (location, name) in attributes)
        {
            gl.BindAttribLocationString(program, location, name);
        }

        var linkError = gl.LinkProgramAndGetError(program);
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
        if (!string.IsNullOrWhiteSpace(linkError))
        {
            throw new InvalidOperationException($"Program link failed: {linkError}");
        }

        return program;
    }

    private static T? LoadDelegate<T>(GlInterface gl, string procName) where T : class
    {
        var proc = gl.GetProcAddress(procName);
        if (proc == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer(proc, typeof(T)) as T;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBlendFuncDelegate(int sfactor, int dfactor);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDepthMaskDelegate(byte flag);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDisableDelegate(int cap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlUniform1iDelegate(int location, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlUniform4fDelegate(int location, float x, float y, float z, float w);

    private sealed class MeshGpuResource
    {
        public int GeometryVersion { get; init; }
        public int VertexBuffer { get; init; }
        public int IndexBuffer { get; init; }
        public int IndexCount { get; init; }

        public void Dispose(GlInterface gl)
        {
            if (VertexBuffer != 0)
            {
                gl.DeleteBuffer(VertexBuffer);
            }
            if (IndexBuffer != 0)
            {
                gl.DeleteBuffer(IndexBuffer);
            }
        }
    }

    private sealed class ControlTextureResource
    {
        public int TextureId { get; init; }
        public int SnapshotVersion { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public void Dispose(GlInterface gl)
        {
            if (TextureId != 0)
            {
                gl.DeleteTexture(TextureId);
            }
        }
    }

    private const string MeshVertexSource = @"attribute vec3 aPosition;
void main()
{
    gl_Position = vec4(aPosition, 1.0);
}";

    private const string MeshFragmentSource = @"#ifdef GL_ES
precision mediump float;
#endif
uniform vec4 uColor;
void main()
{
    gl_FragColor = uColor;
}";

    private const string TexturedVertexSource = @"attribute vec3 aPosition;
attribute vec2 aTexCoord;
varying vec2 vTexCoord;
void main()
{
    vTexCoord = aTexCoord;
    gl_Position = vec4(aPosition, 1.0);
}";

    private const string TexturedFragmentSource = @"#ifdef GL_ES
precision mediump float;
#endif
uniform sampler2D uTexture;
varying vec2 vTexCoord;
void main()
{
    gl_FragColor = texture2D(uTexture, vTexCoord);
}";
}
