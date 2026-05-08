using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Avalonia.Rendering;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Culling;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Instancing;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Shadows;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.OpenGL.Rendering;

internal sealed partial class OpenGlSceneRenderer
{
    private const int GlColorBufferBit = 0x00004000;
    private const int GlDepthBufferBit = 0x00000100;
    private const int GlFramebuffer = 0x8D40;
    private const int GlFrameBufferComplete = 0x8CD5;
    private const int GlDepthAttachment = 0x8D00;
    private const int GlDepthComponent = 0x1902;
    private const int GlTextureCompareMode = 0x884C;
    private const int GlNone = 0;
    private const int GlTriangles = 0x0004;
    private const int GlLines = 0x0001;
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
    private const int GlTexture1 = 0x84C1;
    private const int GlTexture2 = 0x84C2;
    private const int GlTexture3 = 0x84C3;
    private const int GlTexture4 = 0x84C4;
    private const int GlTexture5 = 0x84C5;
    private const int GlTexture6 = 0x84C6;
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlNearest = 0x2600;
    private const int GlLinear = 0x2601;
    private const int GlClampToEdge = 0x812F;
    private const int GlRgba = 0x1908;
    private const int GlUnsignedByte = 0x1401;
    private const int GlVertexShader = 0x8B31;
    private const int GlFragmentShader = 0x8B30;
    private const int GlSrcAlpha = 0x0302;
    private const int GlOneMinusSrcAlpha = 0x0303;
    private const int InstanceFloatStride = 20;
    private const int InstanceByteStride = InstanceFloatStride * sizeof(float);
    private const int HighScaleTransformFloatStride = 16;
    private const int HighScaleTransformByteStride = HighScaleTransformFloatStride * sizeof(float);
    private const int HighScaleStateFloatStride = 4;
    private const int HighScaleStateByteStride = HighScaleStateFloatStride * sizeof(float);
    private const int MaxHighScaleMaterialVariants = 32;
    private static readonly HighScaleChunkKey3D AggregateChunkKey = new(int.MinValue, int.MinValue, int.MinValue);

    private readonly Dictionary<string, MeshGpuResource> _meshResources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControlTextureResource> _controlTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MaterialTextureResource> _materialTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MeshBatchData> _meshBatches = new(StringComparer.Ordinal);
    private readonly Dictionary<HighScaleBatchKey, HighScaleGpuBatchData> _highScaleGpuBatches = new();
    private readonly float[] _matrixUploadBuffer = new float[16];
    private readonly float[] _controlVertexData = new float[20];
    private readonly List<string> _meshSweepScratch = new();
    private readonly List<string> _textureSweepScratch = new();
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private int _lastSweptRegistryVersion = -1;
    private int _highScaleTransformBatchUploadsThisFrame;

    private int _meshProgram;
    private int _texturedProgram;
    private int _shadowProgram;
    private int _skyboxProgram;
    private int _meshPositionLocation;
    private int _meshNormalLocation;
    private int _meshTexCoordLocation;
    private int _meshTangentLocation;
    private int _meshInstanceModel0Location;
    private int _meshInstanceModel1Location;
    private int _meshInstanceModel2Location;
    private int _meshInstanceModel3Location;
    private int _meshInstanceColorLocation;
    private int _meshInstanceStateColorLocation;
    private int _meshMaterialSlotLocation;
    private int _meshAmbientLightLocation;
    private int _meshDirectionalLightDirectionLocation;
    private int _meshDirectionalLightColorLocation;
    private int _meshPointLightPositionLocation;
    private int _meshPointLightColorLocation;
    private int _meshSpotLightPositionLocation;
    private int _meshSpotLightDirectionLocation;
    private int _meshSpotLightColorLocation;
    private int _meshSpotLightConeLocation;
    private int _meshCameraPositionLocation;
    private int _meshSpecularColorLocation;
    private int _meshSpecularParamsLocation;
    private int _meshMaterialStrengthsLocation;
    private int _meshNormalMapStrengthLocation;
    private int _meshPostProcessParamsLocation;
    private int _meshSsaoParamsLocation;
    private int _meshColorLocation;
    private int _meshUseInstancingLocation;
    private int _meshLightingEnabledLocation;
    private int _meshModelLocation;
    private int _meshViewProjLocation;
    private int _meshPartLocalLocation;
    private int _meshUsePartLocalLocation;
    private int _meshUseHighScaleStateLocation;
    private int _meshUsePaletteTextureLocation;
    private int _meshShadowEnabledLocation;
    private int _meshShadowMapLocation;
    private int _meshLightViewProjLocation;
    private int _meshShadowParamsLocation;
    private int _meshPaletteTextureLocation;
    private int _meshPaletteWidthLocation;
    private int _meshPaletteHeightLocation;
    private int _meshBaseColorTextureLocation;
    private int _meshBaseColorTextureEnabledLocation;
    private int _meshNormalTextureLocation;
    private int _meshNormalTextureEnabledLocation;
    private int _meshMetallicRoughnessTextureLocation;
    private int _meshMetallicRoughnessTextureEnabledLocation;
    private int _meshEmissiveTextureLocation;
    private int _meshEmissiveTextureEnabledLocation;
    private int _meshAlphaParamsLocation;
    private int _meshEmissiveColorLocation;
    private int _meshClientAnimationEnabledLocation;
    private int _meshClientAnimationTimeLocation;
    private int _meshClientAnimationAmplitudeLocation;
    private readonly int[] _meshVariantColorLocations = new int[MaxHighScaleMaterialVariants];
    private int _texturePositionLocation;
    private int _textureUvLocation;
    private int _textureSamplerLocation;
    private int _textureViewProjLocation;
    private int _shadowPositionLocation;
    private int _shadowInstanceModel0Location;
    private int _shadowInstanceModel1Location;
    private int _shadowInstanceModel2Location;
    private int _shadowInstanceModel3Location;
    private int _shadowModelLocation;
    private int _shadowLightViewProjLocation;
    private int _shadowUseInstancingLocation;
    private int _skyboxPositionLocation;
    private int _skyboxTopColorLocation;
    private int _skyboxHorizonColorLocation;
    private int _skyboxBottomColorLocation;
    private int _skyboxIntensityLocation;
    private int _skyboxModeLocation;
    private int _skyboxCameraRightLocation;
    private int _skyboxCameraUpLocation;
    private int _skyboxCameraForwardLocation;
    private int _skyboxTextureLocation;
    private int _skyboxTextureEnabledLocation;
    private int _skyboxPXLocation;
    private int _skyboxNXLocation;
    private int _skyboxPYLocation;
    private int _skyboxNYLocation;
    private int _skyboxPZLocation;
    private int _skyboxNZLocation;
    private int _skyboxCubemapEnabledLocation;
    private int _skyboxVertexBuffer;
    private int _skyboxIndexBuffer;
    private DirectionalShadowMapResource? _directionalShadowMap;
    private int _controlVertexBuffer;
    private int _controlIndexBuffer;
    private int _meshInstanceBuffer;
    private int _paletteTexture;
    private byte[] _paletteUploadBuffer = Array.Empty<byte>();
    private GlInterface? _lastGl;
    private bool _initialized;
    private bool _supportsInstancing;
    private GlBlendFuncDelegate? _blendFunc;
    private GlDepthMaskDelegate? _depthMask;
    private GlDisableDelegate? _disable;
    private GlUniform1iDelegate? _uniform1i;
    private GlUniform1fDelegate? _uniform1f;
    private GlUniform4fDelegate? _uniform4f;
    private GlUniform3fDelegate? _uniform3f;
    private GlUniformMatrix4fvDelegate? _uniformMatrix4fv;
    private GlVertexAttribDivisorDelegate? _vertexAttribDivisor;
    private GlDrawElementsInstancedDelegate? _drawElementsInstanced;
    private GlBufferSubDataDelegate? _bufferSubData;
    private GlGenFramebuffersDelegate? _genFramebuffers;
    private GlDeleteFramebuffersDelegate? _deleteFramebuffers;
    private GlFramebufferTexture2DDelegate? _framebufferTexture2D;
    private GlCheckFramebufferStatusDelegate? _checkFramebufferStatus;
    private GlDrawBufferDelegate? _drawBuffer;
    private GlReadBufferDelegate? _readBuffer;

    public void Initialize(GlInterface gl)
    {
        _lastGl = gl;
        if (_initialized) return;

        _blendFunc = LoadDelegate<GlBlendFuncDelegate>(gl, "glBlendFunc");
        _depthMask = LoadDelegate<GlDepthMaskDelegate>(gl, "glDepthMask");
        _disable = LoadDelegate<GlDisableDelegate>(gl, "glDisable");
        _uniform1i = LoadDelegate<GlUniform1iDelegate>(gl, "glUniform1i");
        _uniform1f = LoadDelegate<GlUniform1fDelegate>(gl, "glUniform1f");
        _uniform4f = LoadDelegate<GlUniform4fDelegate>(gl, "glUniform4f");
        _uniform3f = LoadDelegate<GlUniform3fDelegate>(gl, "glUniform3f");
        _uniformMatrix4fv = LoadDelegate<GlUniformMatrix4fvDelegate>(gl, "glUniformMatrix4fv");
        _vertexAttribDivisor = LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisor")
                                ?? LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisorARB");
        _drawElementsInstanced = LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstanced")
                                 ?? LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstancedARB");
        _bufferSubData = LoadDelegate<GlBufferSubDataDelegate>(gl, "glBufferSubData");
        _genFramebuffers = LoadDelegate<GlGenFramebuffersDelegate>(gl, "glGenFramebuffers")
                           ?? LoadDelegate<GlGenFramebuffersDelegate>(gl, "glGenFramebuffersEXT");
        _deleteFramebuffers = LoadDelegate<GlDeleteFramebuffersDelegate>(gl, "glDeleteFramebuffers")
                              ?? LoadDelegate<GlDeleteFramebuffersDelegate>(gl, "glDeleteFramebuffersEXT");
        _framebufferTexture2D = LoadDelegate<GlFramebufferTexture2DDelegate>(gl, "glFramebufferTexture2D")
                                ?? LoadDelegate<GlFramebufferTexture2DDelegate>(gl, "glFramebufferTexture2DEXT");
        _checkFramebufferStatus = LoadDelegate<GlCheckFramebufferStatusDelegate>(gl, "glCheckFramebufferStatus")
                                  ?? LoadDelegate<GlCheckFramebufferStatusDelegate>(gl, "glCheckFramebufferStatusEXT");
        _drawBuffer = LoadDelegate<GlDrawBufferDelegate>(gl, "glDrawBuffer");
        _readBuffer = LoadDelegate<GlReadBufferDelegate>(gl, "glReadBuffer");
        _supportsInstancing = _vertexAttribDivisor is not null && _drawElementsInstanced is not null;

        _meshProgram = CreateProgram(gl, MeshVertexSource, MeshFragmentSource,
            (0, "aPosition"), (1, "aNormal"), (2, "aInstanceModel0"), (3, "aInstanceModel1"),
            (4, "aInstanceModel2"), (5, "aInstanceModel3"), (6, "aInstanceColor"), (7, "aInstanceState"),
            (8, "aMaterialSlot"), (9, "aTexCoord0"), (10, "aTangent"));
        _meshPositionLocation = gl.GetAttribLocationString(_meshProgram, "aPosition");
        _meshNormalLocation = gl.GetAttribLocationString(_meshProgram, "aNormal");
        _meshTexCoordLocation = gl.GetAttribLocationString(_meshProgram, "aTexCoord0");
        _meshTangentLocation = gl.GetAttribLocationString(_meshProgram, "aTangent");
        _meshInstanceModel0Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel0");
        _meshInstanceModel1Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel1");
        _meshInstanceModel2Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel2");
        _meshInstanceModel3Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel3");
        _meshInstanceColorLocation = gl.GetAttribLocationString(_meshProgram, "aInstanceColor");
        _meshInstanceStateColorLocation = gl.GetAttribLocationString(_meshProgram, "aInstanceState");
        _meshMaterialSlotLocation = gl.GetAttribLocationString(_meshProgram, "aMaterialSlot");
        _meshColorLocation = gl.GetUniformLocationString(_meshProgram, "uColor");
        _meshUseInstancingLocation = gl.GetUniformLocationString(_meshProgram, "uUseInstancing");
        _meshLightingEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uLightingEnabled");
        _meshModelLocation = gl.GetUniformLocationString(_meshProgram, "uModel");
        _meshViewProjLocation = gl.GetUniformLocationString(_meshProgram, "uViewProj");
        _meshPartLocalLocation = gl.GetUniformLocationString(_meshProgram, "uPartLocal");
        _meshUsePartLocalLocation = gl.GetUniformLocationString(_meshProgram, "uUsePartLocal");
        _meshUseHighScaleStateLocation = gl.GetUniformLocationString(_meshProgram, "uUseHighScaleState");
        _meshUsePaletteTextureLocation = gl.GetUniformLocationString(_meshProgram, "uUsePaletteTexture");
        _meshPaletteTextureLocation = gl.GetUniformLocationString(_meshProgram, "uPaletteTexture");
        _meshPaletteWidthLocation = gl.GetUniformLocationString(_meshProgram, "uPaletteWidth");
        _meshPaletteHeightLocation = gl.GetUniformLocationString(_meshProgram, "uPaletteHeight");
        _meshBaseColorTextureLocation = gl.GetUniformLocationString(_meshProgram, "uBaseColorTexture");
        _meshBaseColorTextureEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uBaseColorTextureEnabled");
        _meshNormalTextureLocation = gl.GetUniformLocationString(_meshProgram, "uNormalTexture");
        _meshNormalTextureEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uNormalTextureEnabled");
        _meshMetallicRoughnessTextureLocation = gl.GetUniformLocationString(_meshProgram, "uMetallicRoughnessTexture");
        _meshMetallicRoughnessTextureEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uMetallicRoughnessTextureEnabled");
        _meshEmissiveTextureLocation = gl.GetUniformLocationString(_meshProgram, "uEmissiveTexture");
        _meshEmissiveTextureEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uEmissiveTextureEnabled");
        _meshAlphaParamsLocation = gl.GetUniformLocationString(_meshProgram, "uAlphaParams");
        _meshEmissiveColorLocation = gl.GetUniformLocationString(_meshProgram, "uEmissiveColor");
        _meshClientAnimationEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uClientAnimationEnabled");
        _meshClientAnimationTimeLocation = gl.GetUniformLocationString(_meshProgram, "uClientAnimationTime");
        _meshClientAnimationAmplitudeLocation = gl.GetUniformLocationString(_meshProgram, "uClientAnimationAmplitude");
        for (var i = 0; i < _meshVariantColorLocations.Length; i++)
        {
            _meshVariantColorLocations[i] = gl.GetUniformLocationString(_meshProgram, $"uVariantColors[{i}]");
        }
        _meshAmbientLightLocation = gl.GetUniformLocationString(_meshProgram, "uAmbientLight");
        _meshDirectionalLightDirectionLocation = gl.GetUniformLocationString(_meshProgram, "uDirectionalLightDirection");
        _meshDirectionalLightColorLocation = gl.GetUniformLocationString(_meshProgram, "uDirectionalLightColor");
        _meshPointLightPositionLocation = gl.GetUniformLocationString(_meshProgram, "uPointLightPosition");
        _meshPointLightColorLocation = gl.GetUniformLocationString(_meshProgram, "uPointLightColor");
        _meshSpotLightPositionLocation = gl.GetUniformLocationString(_meshProgram, "uSpotLightPosition");
        _meshSpotLightDirectionLocation = gl.GetUniformLocationString(_meshProgram, "uSpotLightDirection");
        _meshSpotLightColorLocation = gl.GetUniformLocationString(_meshProgram, "uSpotLightColor");
        _meshSpotLightConeLocation = gl.GetUniformLocationString(_meshProgram, "uSpotLightCone");
        _meshCameraPositionLocation = gl.GetUniformLocationString(_meshProgram, "uCameraPosition");
        _meshSpecularColorLocation = gl.GetUniformLocationString(_meshProgram, "uSpecularColor");
        _meshSpecularParamsLocation = gl.GetUniformLocationString(_meshProgram, "uSpecularParams");
        _meshMaterialStrengthsLocation = gl.GetUniformLocationString(_meshProgram, "uMaterialStrengths");
        _meshNormalMapStrengthLocation = gl.GetUniformLocationString(_meshProgram, "uNormalMapStrength");
        _meshPostProcessParamsLocation = gl.GetUniformLocationString(_meshProgram, "uPostProcessParams");
        _meshSsaoParamsLocation = gl.GetUniformLocationString(_meshProgram, "uSsaoParams");
        _meshShadowEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uShadowEnabled");
        _meshShadowMapLocation = gl.GetUniformLocationString(_meshProgram, "uShadowMap");
        _meshLightViewProjLocation = gl.GetUniformLocationString(_meshProgram, "uLightViewProj");
        _meshShadowParamsLocation = gl.GetUniformLocationString(_meshProgram, "uShadowParams");

        _shadowProgram = CreateProgram(gl, ShadowVertexSource, ShadowFragmentSource,
            (0, "aPosition"), (2, "aInstanceModel0"), (3, "aInstanceModel1"), (4, "aInstanceModel2"), (5, "aInstanceModel3"));
        _shadowPositionLocation = gl.GetAttribLocationString(_shadowProgram, "aPosition");
        _shadowInstanceModel0Location = gl.GetAttribLocationString(_shadowProgram, "aInstanceModel0");
        _shadowInstanceModel1Location = gl.GetAttribLocationString(_shadowProgram, "aInstanceModel1");
        _shadowInstanceModel2Location = gl.GetAttribLocationString(_shadowProgram, "aInstanceModel2");
        _shadowInstanceModel3Location = gl.GetAttribLocationString(_shadowProgram, "aInstanceModel3");
        _shadowModelLocation = gl.GetUniformLocationString(_shadowProgram, "uModel");
        _shadowLightViewProjLocation = gl.GetUniformLocationString(_shadowProgram, "uLightViewProj");
        _shadowUseInstancingLocation = gl.GetUniformLocationString(_shadowProgram, "uUseInstancing");

        _skyboxProgram = CreateProgram(gl, SkyboxVertexSource, SkyboxFragmentSource, (0, "aPosition"));
        _skyboxPositionLocation = gl.GetAttribLocationString(_skyboxProgram, "aPosition");
        _skyboxTopColorLocation = gl.GetUniformLocationString(_skyboxProgram, "uTopColor");
        _skyboxHorizonColorLocation = gl.GetUniformLocationString(_skyboxProgram, "uHorizonColor");
        _skyboxBottomColorLocation = gl.GetUniformLocationString(_skyboxProgram, "uBottomColor");
        _skyboxIntensityLocation = gl.GetUniformLocationString(_skyboxProgram, "uIntensity");
        _skyboxModeLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxMode");
        _skyboxCameraRightLocation = gl.GetUniformLocationString(_skyboxProgram, "uCameraRight");
        _skyboxCameraUpLocation = gl.GetUniformLocationString(_skyboxProgram, "uCameraUp");
        _skyboxCameraForwardLocation = gl.GetUniformLocationString(_skyboxProgram, "uCameraForward");
        _skyboxTextureLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxTexture");
        _skyboxTextureEnabledLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxTextureEnabled");
        _skyboxPXLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxPX");
        _skyboxNXLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxNX");
        _skyboxPYLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxPY");
        _skyboxNYLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxNY");
        _skyboxPZLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxPZ");
        _skyboxNZLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxNZ");
        _skyboxCubemapEnabledLocation = gl.GetUniformLocationString(_skyboxProgram, "uSkyboxCubemapEnabled");

        _texturedProgram = CreateProgram(gl, TexturedVertexSource, TexturedFragmentSource, (0, "aPosition"), (1, "aTexCoord"));
        _texturePositionLocation = gl.GetAttribLocationString(_texturedProgram, "aPosition");
        _textureUvLocation = gl.GetAttribLocationString(_texturedProgram, "aTexCoord");
        _textureSamplerLocation = gl.GetUniformLocationString(_texturedProgram, "uTexture");
        _textureViewProjLocation = gl.GetUniformLocationString(_texturedProgram, "uViewProj");

        _meshInstanceBuffer = gl.GenBuffer();
        _paletteTexture = gl.GenTexture();
        _controlVertexBuffer = gl.GenBuffer();
        _controlIndexBuffer = gl.GenBuffer();
        _skyboxVertexBuffer = gl.GenBuffer();
        _skyboxIndexBuffer = gl.GenBuffer();
        gl.BindBuffer(GlArrayBuffer, _skyboxVertexBuffer);
        UploadFloats(gl, GlArrayBuffer, new[] { -1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f }, 8, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, _skyboxIndexBuffer);
        UploadInts(gl, GlElementArrayBuffer, new[] { 0, 1, 2, 0, 2, 3 }, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        gl.BindBuffer(GlArrayBuffer, 0);
        gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
        UploadInts(gl, GlElementArrayBuffer, new[] { 0, 1, 2, 0, 2, 3 }, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        _initialized = true;
    }

    public RenderStats Render(GlInterface gl, int framebuffer, Scene3D scene, Rect bounds)
    {
        if (!_initialized) Initialize(gl);
        var width = System.Math.Max((int)System.Math.Ceiling(bounds.Width), 1);
        var height = System.Math.Max((int)System.Math.Ceiling(bounds.Height), 1);
        var stats = RenderSceneCore(gl, framebuffer, width, height, scene);
        stats.RenderTargetWidth = width;
        stats.RenderTargetHeight = height;
        return stats;
    }

    private RenderStats RenderSceneCore(GlInterface gl, int framebuffer, int width, int height, Scene3D scene)
    {
        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.Enable(GlDepthTest);
        gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);
        gl.Clear(GlColorBufferBit | GlDepthBufferBit);

        var aspect = (float)width / height;
        scene.FrameInterpolator.UpdateAlpha();
        var viewProjection = scene.Camera.GetViewMatrix() * scene.Camera.GetProjectionMatrix(aspect);

        SweepUnusedResources(gl, scene);
        var pipeline = RenderPipelinePlanner3D.Plan(scene, BackendKind.OpenGlDesktop);
        var stats = new RenderStats
        {
            ObjectCount = scene.Registry.AllObjects.Count,
            RenderableCount = scene.Registry.Renderables.Count,
            PickableCount = scene.Registry.Pickables.Count,
            ColliderCount = scene.Registry.Colliders.Count,
            DynamicBodyCount = scene.Registry.DynamicBodies.Count,
            StaticColliderCount = scene.Registry.StaticColliders.Count,
            RegistryVersion = scene.Registry.Version,
            MeshCacheCount = MeshCache3D.Shared.Count,
            DirectionalLightCount = scene.Lights.Count,
            PointLightCount = scene.PointLights.Count,
            SpotLightCount = scene.SpotLights.Count,
            InterpolationAlpha = scene.FrameInterpolator.Alpha
        };
        ApplyAnimationStats(stats, scene, gpuSkinningActive: false, fallbackReason: "CPU pose evaluation / static mesh fallback");
        ApplyPipelineStats(stats, scene, pipeline);

        BuildBatches(scene, viewProjection, stats);
        var shadow = DirectionalShadowResolver3D.Resolve(scene);
        RenderDirectionalShadowMap(gl, shadow, stats);

        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);
        gl.Clear(GlColorBufferBit | GlDepthBufferBit);
        DrawSkybox(gl, scene, stats);
        DrawMeshes(gl, scene, viewProjection, stats, shadow);
        DrawSurfaceOverlays(gl, scene, viewProjection, stats);
        DrawControlPlanes(gl, scene, viewProjection, stats);

        gl.BindBuffer(GlArrayBuffer, 0);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        gl.BindTexture(GlTexture2D, 0);
        gl.UseProgram(0);
        return stats;
    }

    public void Deinitialize(GlInterface gl)
    {
        foreach (var resource in _meshResources.Values) resource.Dispose(gl);
        foreach (var texture in _controlTextures.Values) texture.Dispose(gl);
        foreach (var texture in _materialTextures.Values) texture.Dispose(gl);
        foreach (var batch in _highScaleGpuBatches.Values) batch.Dispose(gl);
        _directionalShadowMap?.Dispose(gl, _deleteFramebuffers);
        _directionalShadowMap = null;
        _meshResources.Clear();
        _controlTextures.Clear();
        _materialTextures.Clear();
        _highScaleGpuBatches.Clear();
        if (_meshInstanceBuffer != 0) gl.DeleteBuffer(_meshInstanceBuffer);
        if (_paletteTexture != 0) gl.DeleteTexture(_paletteTexture);
        if (_controlVertexBuffer != 0) gl.DeleteBuffer(_controlVertexBuffer);
        if (_controlIndexBuffer != 0) gl.DeleteBuffer(_controlIndexBuffer);
        if (_skyboxVertexBuffer != 0) gl.DeleteBuffer(_skyboxVertexBuffer);
        if (_skyboxIndexBuffer != 0) gl.DeleteBuffer(_skyboxIndexBuffer);
        if (_meshProgram != 0) gl.DeleteProgram(_meshProgram);
        if (_texturedProgram != 0) gl.DeleteProgram(_texturedProgram);
        if (_shadowProgram != 0) gl.DeleteProgram(_shadowProgram);
        if (_skyboxProgram != 0) gl.DeleteProgram(_skyboxProgram);
        _meshInstanceBuffer = _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = _shadowProgram = _skyboxProgram = 0;
        _skyboxVertexBuffer = _skyboxIndexBuffer = 0;
        _paletteTexture = 0;
        _initialized = false;
    }

    public void Reset()
    {
        // Reset can be triggered from presenter/context lifecycle paths. When a live GL
        // interface is still known, release owned GPU resources before clearing ids;
        // otherwise the old implementation leaked buffers/textures/programs until process exit.
        var gl = _lastGl;
        if (gl is not null && _initialized)
        {
            try
            {
                Deinitialize(gl);
            }
            catch
            {
                // Context may already be lost; fall through to managed-state reset.
            }
        }

        _initialized = false;
        _meshResources.Clear();
        _controlTextures.Clear();
        _materialTextures.Clear();
        _highScaleGpuBatches.Clear();
        _meshBatches.Clear();
        _meshInstanceBuffer = _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = _shadowProgram = _skyboxProgram = 0;
        _skyboxVertexBuffer = _skyboxIndexBuffer = 0;
        _directionalShadowMap = null;
        _paletteTexture = 0;
        _lastSweptRegistryVersion = -1;
    }

    private void SweepUnusedResources(GlInterface gl, Scene3D scene)
    {
        var registryVersion = scene.Registry.Version;
        if (_lastSweptRegistryVersion == registryVersion) return;

        var liveMeshes = new HashSet<string>(StringComparer.Ordinal);
        var liveControlPlanes = new HashSet<string>(StringComparer.Ordinal);
        var liveMaterialTextures = new HashSet<string>(StringComparer.Ordinal);
        AddLiveMaterialTexture(liveMaterialTextures, scene.Environment.Skybox.EquirectangularTextureKey, scene.Environment.Skybox.HasEquirectangularTexture);
        if (scene.Environment.Skybox.HasCubemapTextures)
        {
            for (var i = 0; i < scene.Environment.Skybox.CubemapTextureKeys.Count; i++)
            {
                AddLiveMaterialTexture(liveMaterialTextures, scene.Environment.Skybox.CubemapTextureKeys[i], true);
            }
        }
        foreach (var obj in scene.Registry.Renderables)
        {
            if (obj is ParticleSystem3D liveParticles) liveParticles.SetBillboardBasis(scene.Camera.Right, scene.Camera.SafeUp, scene.Camera.Forward);
            liveMeshes.Add(obj.GetMesh().ResourceKey);
            var material = MaterialBinding3D.FromMaterial(obj.Material);
            AddLiveMaterialTexture(liveMaterialTextures, material.BaseColorTextureKey, material.HasBaseColorTexture);
            AddLiveMaterialTexture(liveMaterialTextures, material.NormalMapTextureKey, material.HasNormalMap);
            AddLiveMaterialTexture(liveMaterialTextures, material.MetallicRoughnessTextureKey, material.HasMetallicRoughnessTexture);
            AddLiveMaterialTexture(liveMaterialTextures, material.EmissiveTextureKey, material.HasEmissiveTexture);
        }
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            foreach (var part in layer.Template.Parts) liveMeshes.Add(part.Mesh.ResourceKey);
        }
        foreach (var obj in scene.Registry.AllObjects)
        {
            if (obj is ControlPlane3D plane && plane.IsVisible && plane.Snapshot is not null) liveControlPlanes.Add(plane.Id);
        }
        _meshSweepScratch.Clear();
        foreach (var pair in _meshResources)
        {
            if (!liveMeshes.Contains(pair.Key)) _meshSweepScratch.Add(pair.Key);
        }
        foreach (var key in _meshSweepScratch)
        {
            _meshResources[key].Dispose(gl);
            _meshResources.Remove(key);
        }

        _textureSweepScratch.Clear();
        foreach (var pair in _controlTextures)
        {
            if (!liveControlPlanes.Contains(pair.Key)) _textureSweepScratch.Add(pair.Key);
        }
        foreach (var key in _textureSweepScratch)
        {
            _controlTextures[key].Dispose(gl);
            _controlTextures.Remove(key);
        }

        _textureSweepScratch.Clear();
        foreach (var pair in _materialTextures)
        {
            if (!liveMaterialTextures.Contains(pair.Key)) _textureSweepScratch.Add(pair.Key);
        }
        foreach (var key in _textureSweepScratch)
        {
            _materialTextures[key].Dispose(gl);
            _materialTextures.Remove(key);
        }
        _lastSweptRegistryVersion = registryVersion;
    }

    private static void AddLiveMaterialTexture(HashSet<string> liveTextures, string? key, bool active)
    {
        if (active && !string.IsNullOrWhiteSpace(key)) liveTextures.Add(key);
    }

    private void DrawSkybox(GlInterface gl, Scene3D scene, RenderStats stats)
    {
        var skybox = scene.Environment.Skybox;
        if (skybox.Mode == SkyboxMode3D.None || _skyboxProgram == 0) return;

        gl.UseProgram(_skyboxProgram);
        UploadColor3(_uniform3f, _skyboxTopColorLocation, skybox.TopColor, skybox.Intensity);
        UploadColor3(_uniform3f, _skyboxHorizonColorLocation, skybox.HorizonColor, skybox.Intensity);
        UploadColor3(_uniform3f, _skyboxBottomColorLocation, skybox.BottomColor, skybox.Intensity);
        UploadFloat(_uniform1f, _skyboxIntensityLocation, skybox.Intensity);
        _uniform1i?.Invoke(_skyboxModeLocation, (int)skybox.Mode);
        UploadVector3(_uniform3f, _skyboxCameraRightLocation, scene.Camera.Right);
        UploadVector3(_uniform3f, _skyboxCameraUpLocation, scene.Camera.SafeUp);
        UploadVector3(_uniform3f, _skyboxCameraForwardLocation, scene.Camera.Forward);
        UploadSkyboxTexture(gl, skybox, stats);
        UploadSkyboxCubemapTextures(gl, skybox, stats);

        _disable?.Invoke(GlDepthTest);
        _depthMask?.Invoke(0);
        gl.BindBuffer(GlArrayBuffer, _skyboxVertexBuffer);
        gl.EnableVertexAttribArray(_skyboxPositionLocation);
        gl.VertexAttribPointer(_skyboxPositionLocation, 2, GlFloat, 0, sizeof(float) * 2, IntPtr.Zero);
        gl.BindBuffer(GlElementArrayBuffer, _skyboxIndexBuffer);
        gl.DrawElements(GlTriangles, 6, GlUnsignedInt, IntPtr.Zero);
        _depthMask?.Invoke(1);
        gl.Enable(GlDepthTest);

        stats.SkyboxEnabled = true;
        stats.SkyboxMode = (int)skybox.Mode;
        stats.SkyboxDrawCalls++;
        stats.DrawCallCount++;
    }


    private void RenderDirectionalShadowMap(GlInterface gl, DirectionalShadowSnapshot3D shadow, RenderStats stats)
    {
        stats.DirectionalShadowEnabled = shadow.IsEnabled;
        stats.ShadowMapReason = shadow.Reason;
        if (!shadow.IsEnabled || _shadowProgram == 0 || _meshBatches.Count == 0) return;

        var resource = EnsureDirectionalShadowMap(gl, shadow.Resolution);
        if (resource is null)
        {
            stats.ShadowMapReason = "shadow-fbo-unavailable";
            return;
        }

        var watch = Stopwatch.StartNew();
        gl.BindFramebuffer(GlFramebuffer, resource.Framebuffer);
        gl.Viewport(0, 0, resource.Resolution, resource.Resolution);
        gl.Clear(GlDepthBufferBit);
        gl.UseProgram(_shadowProgram);
        UploadMatrix(_uniformMatrix4fv, _shadowLightViewProjLocation, shadow.LightViewProjection, _matrixUploadBuffer);

        if (_supportsInstancing)
        {
            UploadFloat(_uniform1f, _shadowUseInstancingLocation, 1f);
            foreach (var batch in _meshBatches.Values)
            {
                if (batch.InstanceCount == 0) continue;
                var mesh = EnsureMeshResource(gl, batch.MeshKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
                BindShadowAttributes(gl, mesh);
                gl.BindBuffer(GlArrayBuffer, _meshInstanceBuffer);
                UploadFloats(gl, GlArrayBuffer, batch.Data, batch.FloatCount, GlDynamicDraw);
                EnableShadowInstanceAttributes(gl);
                _drawElementsInstanced?.Invoke(GlTriangles, mesh.IndexCount, GlUnsignedInt, IntPtr.Zero, batch.InstanceCount);
                stats.ShadowCasterCount += batch.InstanceCount;
            }
            ResetShadowAttributeDivisors();
        }
        else
        {
            UploadFloat(_uniform1f, _shadowUseInstancingLocation, 0f);
            foreach (var batch in _meshBatches.Values)
            {
                if (batch.InstanceCount == 0) continue;
                var mesh = EnsureMeshResource(gl, batch.MeshKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
                BindShadowAttributes(gl, mesh);
                var data = batch.Data;
                for (var i = 0; i < batch.InstanceCount; i++)
                {
                    var offset = i * InstanceFloatStride;
                    UploadMatrixFromInstanceData(_uniformMatrix4fv, _shadowModelLocation, data, offset, _matrixUploadBuffer);
                    gl.DrawElements(GlTriangles, mesh.IndexCount, GlUnsignedInt, IntPtr.Zero);
                    stats.ShadowCasterCount++;
                }
            }
        }

        watch.Stop();
        stats.ShadowMapCount = 1;
        stats.ShadowMapResolution = resource.Resolution;
        stats.ShadowMapMilliseconds = watch.Elapsed.TotalMilliseconds;
        stats.ShadowMapReason = shadow.Reason;
    }

    private DirectionalShadowMapResource? EnsureDirectionalShadowMap(GlInterface gl, int resolution)
    {
        if (_genFramebuffers is null || _framebufferTexture2D is null) return null;
        if (_directionalShadowMap is not null && _directionalShadowMap.Resolution == resolution) return _directionalShadowMap;

        _directionalShadowMap?.Dispose(gl, _deleteFramebuffers);
        var texture = gl.GenTexture();
        gl.BindTexture(GlTexture2D, texture);
        gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlNearest);
        gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlNearest);
        gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
        gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
        gl.TexParameteri(GlTexture2D, GlTextureCompareMode, GlNone);
        gl.TexImage2D(GlTexture2D, 0, GlDepthComponent, resolution, resolution, 0, GlDepthComponent, GlUnsignedByte, IntPtr.Zero);

        var framebuffer = GenFramebuffer();
        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        _framebufferTexture2D.Invoke(GlFramebuffer, GlDepthAttachment, GlTexture2D, texture, 0);
        _drawBuffer?.Invoke(GlNone);
        _readBuffer?.Invoke(GlNone);
        var complete = _checkFramebufferStatus?.Invoke(GlFramebuffer) ?? GlFrameBufferComplete;
        gl.BindFramebuffer(GlFramebuffer, 0);
        if (complete != GlFrameBufferComplete)
        {
            gl.DeleteTexture(texture);
            DeleteFramebuffer(framebuffer);
            return null;
        }

        _directionalShadowMap = new DirectionalShadowMapResource
        {
            Texture = texture,
            Framebuffer = framebuffer,
            Resolution = resolution
        };
        return _directionalShadowMap;
    }

    private int GenFramebuffer()
    {
        if (_genFramebuffers is null) return 0;
        var ids = new int[1];
        _genFramebuffers.Invoke(1, ids);
        return ids[0];
    }

    private void DeleteFramebuffer(int framebuffer)
    {
        if (framebuffer == 0 || _deleteFramebuffers is null) return;
        var ids = new[] { framebuffer };
        _deleteFramebuffers.Invoke(1, ids);
    }

    private void BindShadowAttributes(GlInterface gl, MeshGpuResource resource)
    {
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        gl.EnableVertexAttribArray(_shadowPositionLocation);
        gl.VertexAttribPointer(_shadowPositionLocation, 3, GlFloat, 0, sizeof(float) * 3, IntPtr.Zero);
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
    }

    private void EnableShadowInstanceAttributes(GlInterface gl)
    {
        EnableInstanceAttribute(gl, _shadowInstanceModel0Location, 4, InstanceByteStride, 0);
        EnableInstanceAttribute(gl, _shadowInstanceModel1Location, 4, InstanceByteStride, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _shadowInstanceModel2Location, 4, InstanceByteStride, sizeof(float) * 8);
        EnableInstanceAttribute(gl, _shadowInstanceModel3Location, 4, InstanceByteStride, sizeof(float) * 12);
    }

    private void ResetShadowAttributeDivisors()
    {
        if (_vertexAttribDivisor is null) return;
        if (_shadowInstanceModel0Location >= 0) _vertexAttribDivisor(_shadowInstanceModel0Location, 0);
        if (_shadowInstanceModel1Location >= 0) _vertexAttribDivisor(_shadowInstanceModel1Location, 0);
        if (_shadowInstanceModel2Location >= 0) _vertexAttribDivisor(_shadowInstanceModel2Location, 0);
        if (_shadowInstanceModel3Location >= 0) _vertexAttribDivisor(_shadowInstanceModel3Location, 0);
    }

    private void ConfigureShadowSampling(GlInterface gl, DirectionalShadowSnapshot3D shadow)
    {
        var enabled = shadow.IsEnabled && _directionalShadowMap is not null;
        UploadFloat(_uniform1f, _meshShadowEnabledLocation, enabled ? 1f : 0f);
        UploadMatrix(_uniformMatrix4fv, _meshLightViewProjLocation, shadow.LightViewProjection, _matrixUploadBuffer);
        UploadVector4(_uniform4f, _meshShadowParamsLocation, new Vector4(shadow.Bias, shadow.Strength, shadow.NormalBias, 0f));
        if (!enabled) return;
        gl.ActiveTexture(GlTexture1);
        gl.BindTexture(GlTexture2D, _directionalShadowMap!.Texture);
        _uniform1i?.Invoke(_meshShadowMapLocation, 1);
        gl.ActiveTexture(GlTexture0);
    }

    private static void ApplyPipelineStats(RenderStats stats, Scene3D scene, RenderPipelinePlan3D pipeline)
    {
        stats.RenderPipelineMode = (int)pipeline.ActiveMode;
        stats.DeferredRequested = pipeline.DeferredRequested;
        stats.DeferredActive = pipeline.DeferredActive;
        stats.GBufferActive = pipeline.GBufferActive;
        stats.GBufferTargetCount = pipeline.GBufferActive ? 4 : 0;
        stats.SsaoRequested = pipeline.SsaoRequested;
        stats.SsaoActive = pipeline.SsaoActive;
        stats.SsaoSampleCount = scene.RenderPipeline.Ssao.SampleCount;
        stats.HdrRequested = pipeline.HdrRequested;
        stats.HdrActive = pipeline.HdrActive;
        stats.ToneMappingMode = (int)pipeline.ToneMappingMode;
        stats.ToneMappingActive = pipeline.ToneMappingActive;
        stats.ToneMappingExposure = scene.RenderPipeline.ToneMapping.Exposure;
        stats.ToneMappingGamma = scene.RenderPipeline.ToneMapping.Gamma;
        stats.RenderPassCount = pipeline.Passes.Count;
        stats.MotionVectorsRequested = pipeline.MotionVectorsRequested;
        stats.MotionVectorsActive = pipeline.MotionVectorsActive;
        stats.RenderPipelineReason = pipeline.Reason;
    }

    private void UploadPostProcessing(Scene3D scene)
    {
        var pipeline = RenderPipelinePlanner3D.Plan(scene, BackendKind.OpenGlDesktop);
        var tone = scene.RenderPipeline.ToneMapping;
        var toneActive = pipeline.HdrActive || pipeline.ToneMappingActive;
        UploadVector4(_uniform4f, _meshPostProcessParamsLocation, new Vector4(
            tone.Exposure,
            tone.Gamma,
            toneActive ? 1f : 0f,
            (float)pipeline.ToneMappingMode));

        var ssao = scene.RenderPipeline.Ssao;
        UploadVector4(_uniform4f, _meshSsaoParamsLocation, new Vector4(
            pipeline.SsaoRequested ? 1f : 0f,
            ssao.Strength,
            ssao.Radius,
            ssao.Bias));
    }

    private void DrawMeshes(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats, DirectionalShadowSnapshot3D shadow)
    {
        var hasHighScale = HasHighScaleLayers(scene);
        if (_meshBatches.Count == 0 && !hasHighScale) return;

        gl.UseProgram(_meshProgram);
        UploadLighting(scene);
        UploadVector3(_uniform3f, _meshCameraPositionLocation, scene.Camera.Position);
        UploadPostProcessing(scene);
        UploadMatrix(_uniformMatrix4fv, _meshViewProjLocation, viewProjection, _matrixUploadBuffer);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        UploadFloat(_uniform1f, _meshBaseColorTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshNormalTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshMetallicRoughnessTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshEmissiveTextureEnabledLocation, 0f);
        ConfigureShadowSampling(gl, shadow);
        stats.WebGlClientGpuTransformAnimation = scene.Performance.EnableWebGlClientGpuTransformAnimation;
        UploadClientTransformAnimation(scene, enabled: false);

        if (_supportsInstancing)
        {
            UploadFloat(_uniform1f, _meshUseInstancingLocation, 1f);
            UploadClientTransformAnimation(scene, enabled: false);
            DrawInstancedBatches(gl, stats);
            UploadClientTransformAnimation(scene, scene.Performance.EnableWebGlClientGpuTransformAnimation);
            DrawHighScaleLayers(gl, scene, viewProjection, stats);
            UploadClientTransformAnimation(scene, enabled: false);
        }
        else
        {
            UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
            DrawLegacyBatches(gl, stats);
            if (hasHighScale)
            {
                stats.HighScaleInstanceCount = 0;
            }
        }
    }

    private void BuildBatches(Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        foreach (var batch in _meshBatches.Values) batch.Reset();

        foreach (var obj in scene.Registry.Renderables)
        {
            if (obj is ParticleSystem3D particles)
            {
                stats.ParticleSystemCount++;
                stats.ParticleCount += particles.AliveCount;
            }
            if (obj is InstancedMesh3D instancedMesh)
            {
                stats.InstancedMeshLayerCount++;
                stats.InstancedMeshInstanceCount += instancedMesh.Instances.Count;
            }
            if (obj is ParticleSystem3D particlesForBillboard) particlesForBillboard.SetBillboardBasis(scene.Camera.Right, scene.Camera.SafeUp, scene.Camera.Forward);
            var mesh = obj.GetMesh();
            if (obj is ParticleSystem3D)
            {
                stats.ParticleVertexCount += mesh.Positions.Length;
                stats.ParticleMeshUploadBytes += mesh.RenderGeometry.EstimatedUploadBytes;
                stats.ThroughputFallbackDrawCount++;
            }
            var model = scene.FrameInterpolator.TryGetInterpolatedModel(obj.Id, out var interpolatedModel) ? interpolatedModel : obj.GetModelMatrix();
            if (!FrustumCuller3D.IntersectsLocalBounds(mesh.LocalBounds, model, viewProjection))
            {
                stats.CulledObjectCount++;
                continue;
            }
            var distanceAlpha = ResolveDistanceAlpha(scene, model);
            if (distanceAlpha <= 0.001f)
            {
                stats.CulledObjectCount++;
                continue;
            }
            var color = ApplyDistanceAlpha(ResolveColor(obj), distanceAlpha);
            var material = MaterialBinding3D.FromMaterial(obj.Material);
            var batch = GetBatch(mesh.ResourceKey, mesh, material);
            batch.Add(model, color);
            stats.VisibleMeshCount++;
            if (material.HasNormalMap) stats.NormalMappedMeshCount++;
            stats.TriangleCount += mesh.Indices.Length / 3;
        }

        // HighScaleInstanceLayer3D is intentionally not expanded into the normal per-frame mesh batch.
        // It is rendered by DrawHighScaleLayers using retained chunk/part instance buffers.
    }

    private MeshBatchData GetBatch(string meshKey, Mesh3D mesh, MaterialBinding3D material)
    {
        var key = meshKey + "|mat:" + material.Key;
        if (!_meshBatches.TryGetValue(key, out var batch))
        {
            batch = new MeshBatchData(meshKey, mesh, material);
            _meshBatches[key] = batch;
        }
        else
        {
            batch.Mesh = mesh;
            batch.Material = material;
        }
        return batch;
    }

    private static bool HasHighScaleLayers(Scene3D scene)
    {
        foreach (var obj in scene.Registry.AllObjects)
            if (obj is HighScaleInstanceLayer3D layer && layer.IsVisible && layer.Instances.Count > 0)
                return true;
        return false;
    }

    private static IEnumerable<HighScaleInstanceLayer3D> EnumerateHighScaleLayers(Scene3D scene)
    {
        foreach (var obj in scene.Registry.AllObjects)
            if (obj is HighScaleInstanceLayer3D layer)
                yield return layer;
    }

    private void DrawInstancedBatches(GlInterface gl, RenderStats stats)
    {
        foreach (var batch in _meshBatches.Values)
        {
            if (batch.InstanceCount == 0) continue;
            var resource = EnsureMeshResource(gl, batch.MeshKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
            BindMeshAttributes(gl, resource);
            gl.BindBuffer(GlArrayBuffer, _meshInstanceBuffer);
            UploadFloats(gl, GlArrayBuffer, batch.Data, batch.FloatCount, GlDynamicDraw);
            EnableInstanceAttributes(gl);
            UploadClassicMaterial(gl, batch.Material, stats);
            _drawElementsInstanced?.Invoke(GlTriangles, resource.IndexCount, GlUnsignedInt, IntPtr.Zero, batch.InstanceCount);
            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
            stats.InstancedBatchCount++;
        }
        ResetInstanceAttributeDivisors();
    }

    private void DrawLegacyBatches(GlInterface gl, RenderStats stats)
    {
        foreach (var batch in _meshBatches.Values)
        {
            if (batch.InstanceCount == 0) continue;
            var resource = EnsureMeshResource(gl, batch.MeshKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
            BindMeshAttributes(gl, resource);
            UploadClassicMaterial(gl, batch.Material, stats);
            var data = batch.Data;
            for (var i = 0; i < batch.InstanceCount; i++)
            {
                var offset = i * InstanceFloatStride;
                UploadMatrixFromInstanceData(_uniformMatrix4fv, _meshModelLocation, data, offset, _matrixUploadBuffer);
                UploadColor(_uniform4f, _meshColorLocation, new ColorRgba(data[offset + 16], data[offset + 17], data[offset + 18], data[offset + 19]));
                gl.DrawElements(GlTriangles, resource.IndexCount, GlUnsignedInt, IntPtr.Zero);
                stats.DrawCallCount++;
            }
        }
    }

    private void BindMeshAttributes(GlInterface gl, MeshGpuResource resource)
    {
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        gl.EnableVertexAttribArray(_meshPositionLocation);
        gl.VertexAttribPointer(_meshPositionLocation, 3, GlFloat, 0, sizeof(float) * 3, IntPtr.Zero);
        gl.BindBuffer(GlArrayBuffer, resource.NormalBuffer);
        gl.EnableVertexAttribArray(_meshNormalLocation);
        gl.VertexAttribPointer(_meshNormalLocation, 3, GlFloat, 0, sizeof(float) * 3, IntPtr.Zero);
        if (_meshTexCoordLocation >= 0)
        {
            gl.BindBuffer(GlArrayBuffer, resource.TexCoordBuffer);
            gl.EnableVertexAttribArray(_meshTexCoordLocation);
            gl.VertexAttribPointer(_meshTexCoordLocation, 2, GlFloat, 0, sizeof(float) * 2, IntPtr.Zero);
        }
        if (_meshTangentLocation >= 0)
        {
            gl.BindBuffer(GlArrayBuffer, resource.TangentBuffer);
            gl.EnableVertexAttribArray(_meshTangentLocation);
            gl.VertexAttribPointer(_meshTangentLocation, 4, GlFloat, 0, sizeof(float) * 4, IntPtr.Zero);
        }
        if (_meshMaterialSlotLocation >= 0)
        {
            gl.BindBuffer(GlArrayBuffer, resource.MaterialSlotBuffer);
            gl.EnableVertexAttribArray(_meshMaterialSlotLocation);
            gl.VertexAttribPointer(_meshMaterialSlotLocation, 1, GlFloat, 0, sizeof(float), IntPtr.Zero);
            _vertexAttribDivisor?.Invoke(_meshMaterialSlotLocation, 0);
        }
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
    }

    private void EnableInstanceAttributes(GlInterface gl)
    {
        EnableInstanceAttribute(gl, _meshInstanceModel0Location, 4, InstanceByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel1Location, 4, InstanceByteStride, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _meshInstanceModel2Location, 4, InstanceByteStride, sizeof(float) * 8);
        EnableInstanceAttribute(gl, _meshInstanceModel3Location, 4, InstanceByteStride, sizeof(float) * 12);
        EnableInstanceAttribute(gl, _meshInstanceColorLocation, 4, InstanceByteStride, sizeof(float) * 16);
    }

    private void EnableHighScaleInstanceAttributes(GlInterface gl, HighScaleGpuBatchData batch)
    {
        gl.BindBuffer(GlArrayBuffer, batch.TransformBuffer);
        EnableInstanceAttribute(gl, _meshInstanceModel0Location, 4, HighScaleTransformByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel1Location, 4, HighScaleTransformByteStride, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _meshInstanceModel2Location, 4, HighScaleTransformByteStride, sizeof(float) * 8);
        EnableInstanceAttribute(gl, _meshInstanceModel3Location, 4, HighScaleTransformByteStride, sizeof(float) * 12);

        gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
        EnableInstanceAttribute(gl, _meshInstanceStateColorLocation, 4, HighScaleStateByteStride, 0);
    }

    private void EnableInstanceAttribute(GlInterface gl, int location, int size, int stride, int offset)
    {
        if (location < 0) return;
        gl.EnableVertexAttribArray(location);
        gl.VertexAttribPointer(location, size, GlFloat, 0, stride, new IntPtr(offset));
        _vertexAttribDivisor?.Invoke(location, 1);
    }

    private void ResetInstanceAttributeDivisors()
    {
        ResetDivisor(_meshInstanceModel0Location);
        ResetDivisor(_meshInstanceModel1Location);
        ResetDivisor(_meshInstanceModel2Location);
        ResetDivisor(_meshInstanceModel3Location);
        ResetDivisor(_meshInstanceColorLocation);
        ResetDivisor(_meshInstanceStateColorLocation);
        ResetDivisor(_meshMaterialSlotLocation);
    }

    private void ResetDivisor(int location)
    {
        if (location >= 0) _vertexAttribDivisor?.Invoke(location, 0);
    }

    private void DrawHighScaleLayers(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 1f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 1f);
        UploadFloat(_uniform1f, _meshShadowEnabledLocation, 0f);
        var cameraPosition = scene.Camera.Position;
        _highScaleTransformBatchUploadsThisFrame = 0;
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            if (!layer.IsVisible || layer.Instances.Count == 0) continue;
            if (layer.Chunks.RebuildRequested)
            {
                layer.Chunks.Rebuild(layer.Instances, layer.Template.LocalBounds);
            }

            if (ShouldUseAggregateLayerBatches(layer, scene.Performance))
            {
                DrawHighScaleAggregateLayer(gl, scene, layer, cameraPosition, scene.Performance, stats);
                layer.StateBuffer.ClearDirty();
                continue;
            }

            var visibleChunks = layer.Chunks.QueryVisible(viewProjection);
            stats.TotalChunkCount += layer.Chunks.Chunks.Count;
            var visibleChunkLimit = scene.Performance.MaxVisibleHighScaleChunks > 0 ? System.Math.Min(scene.Performance.MaxVisibleHighScaleChunks, visibleChunks.Count) : visibleChunks.Count;
            stats.VisibleChunkCount += visibleChunkLimit;

            for (var visibleChunkIndex = 0; visibleChunkIndex < visibleChunkLimit; visibleChunkIndex++)
            {
                var chunk = visibleChunks[visibleChunkIndex];
                var planStart = Stopwatch.GetTimestamp();
                var plan = BuildHighScaleChunkPlan(layer, chunk, cameraPosition, stats, scene.Performance);
                stats.HighScalePlanMilliseconds += GetElapsedMilliseconds(planStart);

                DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Detailed, plan.Detailed, cameraPosition, scene.Performance, stats);
                DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Simplified, plan.Simplified, cameraPosition, scene.Performance, stats);
                DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Proxy, plan.Proxy, cameraPosition, scene.Performance, stats);
                DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Billboard, plan.Billboard, cameraPosition, scene.Performance, stats);
                chunk.MarkClean();
            }

            layer.StateBuffer.ClearDirty();

        }
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        ResetInstanceAttributeDivisors();
    }


    private static bool ShouldUseAggregateLayerBatches(HighScaleInstanceLayer3D layer, ScenePerformanceOptions performance)
        => performance.EnableHighScaleAggregateLayerBatches &&
           layer.Instances.Count > 0 &&
           layer.Instances.Count <= performance.HighScaleAggregateLayerInstanceThreshold;

    private void DrawHighScaleAggregateLayer(
        GlInterface gl,
        Scene3D scene,
        HighScaleInstanceLayer3D layer,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        stats.TotalChunkCount += layer.Chunks.Chunks.Count;
        stats.VisibleChunkCount += layer.Chunks.Chunks.Count;

        var planStart = Stopwatch.GetTimestamp();
        var plan = BuildHighScaleLayerPlan(layer, cameraPosition, stats, performance);
        stats.HighScalePlanMilliseconds += GetElapsedMilliseconds(planStart);

        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Detailed, plan.Detailed, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Simplified, plan.Simplified, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Proxy, plan.Proxy, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Billboard, plan.Billboard, cameraPosition, performance, stats);
    }

    private HighScaleChunkFramePlan BuildHighScaleLayerPlan(HighScaleInstanceLayer3D layer, Vector3 cameraPosition, RenderStats stats, ScenePerformanceOptions performance)
    {
        var plan = HighScaleChunkFramePlan.Shared;
        plan.Reset();

        var count = layer.Instances.Count;
        for (var index = 0; index < count; index++)
        {
            var record = layer.Instances[index];

            if (performance.MaxHighScaleVisibleInstances > 0 && stats.HighScaleInstanceCount >= performance.MaxHighScaleVisibleInstances)
            {
                stats.LodCulledCount++;
                stats.CulledObjectCount++;
                continue;
            }

            var lod = layer.LodPolicy.Resolve(cameraPosition, record.Transform);
            if (lod == HighScaleLodLevel3D.Culled)
            {
                stats.LodCulledCount++;
                stats.CulledObjectCount++;
                continue;
            }

            stats.HighScaleInstanceCount++;
            if (lod == HighScaleLodLevel3D.Detailed)
            {
                stats.LodDetailedCount++;
                plan.Detailed.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Simplified)
            {
                stats.LodSimplifiedCount++;
                plan.Simplified.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Proxy)
            {
                stats.LodProxyCount++;
                plan.Proxy.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Billboard)
            {
                stats.LodBillboardCount++;
                plan.Billboard.Add(index);
            }
        }

        return plan;
    }

    private void DrawHighScaleAggregateLod(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        HighScaleLodLevel3D lod,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        if (instanceIndices.Count == 0) return;

        var buildStart = Stopwatch.GetTimestamp();
        var key = new HighScaleBatchKey(layer.Id, AggregateChunkKey, lod);
        var batch = EnsureHighScaleGpuBatch(gl, layer, key, false, lod, instanceIndices, cameraPosition, performance, stats);
        stats.HighScaleBufferBuildMilliseconds += GetElapsedMilliseconds(buildStart);
        if (batch.InstanceCount == 0) return;

        var parts = layer.Template.ResolveParts(lod);
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var meshResource = EnsureMeshResource(gl, part.Mesh.ResourceKey, part.Mesh.GeometryVersion, part.Mesh, stats);
            BindMeshAttributes(gl, meshResource);
            EnableHighScaleInstanceAttributes(gl, batch);
            UploadHighScalePalette(gl, layer, part, performance, stats);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadHighScaleMaterial(part.LightingMode);
            _drawElementsInstanced?.Invoke(GlTriangles, meshResource.IndexCount, GlUnsignedInt, IntPtr.Zero, batch.InstanceCount);
            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
            stats.InstancedBatchCount++;
            stats.VisibleMeshCount += batch.InstanceCount;
            stats.HighScaleVisiblePartInstanceCount += batch.InstanceCount;
            if (part.UsesVertexMaterialSlots) stats.BakedHighScalePartDraws++;
            stats.TriangleCount += (part.Mesh.Indices.Length / 3) * batch.InstanceCount;
        }
    }

    private HighScaleChunkFramePlan BuildHighScaleChunkPlan(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, Vector3 cameraPosition, RenderStats stats, ScenePerformanceOptions performance)
    {
        var plan = HighScaleChunkFramePlan.Shared;
        plan.Reset();

        if (ShouldUseChunkLevelLodPlanning(layer, chunk, performance))
        {
            AddChunkAsSingleLod(layer, chunk, cameraPosition, stats, performance, plan);
            return plan;
        }

        foreach (var index in chunk.InstanceIndices)
        {
            var record = layer.Instances[index];

            if (performance.MaxHighScaleVisibleInstances > 0 && stats.HighScaleInstanceCount >= performance.MaxHighScaleVisibleInstances)
            {
                stats.LodCulledCount++;
                stats.CulledObjectCount++;
                continue;
            }

            var lod = layer.LodPolicy.Resolve(cameraPosition, record.Transform);
            if (lod == HighScaleLodLevel3D.Culled)
            {
                stats.LodCulledCount++;
                stats.CulledObjectCount++;
                continue;
            }

            stats.HighScaleInstanceCount++;
            if (lod == HighScaleLodLevel3D.Detailed)
            {
                stats.LodDetailedCount++;
                plan.Detailed.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Simplified)
            {
                stats.LodSimplifiedCount++;
                plan.Simplified.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Proxy)
            {
                stats.LodProxyCount++;
                plan.Proxy.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Billboard)
            {
                stats.LodBillboardCount++;
                plan.Billboard.Add(index);
            }
        }

        return plan;
    }

    private static bool ShouldUseChunkLevelLodPlanning(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, ScenePerformanceOptions performance)
    {
        if (!performance.EnableHighScaleChunkLodPlanning) return false;
        if (layer.Instances.Count < performance.HighScaleChunkLodPlanningInstanceThreshold) return false;
        return chunk.InstanceIndices.Count >= performance.HighScaleChunkLodPlanningChunkThreshold;
    }

    private static void AddChunkAsSingleLod(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, Vector3 cameraPosition, RenderStats stats, ScenePerformanceOptions performance, HighScaleChunkFramePlan plan)
    {
        var lod = ResolveChunkLod(layer, chunk, cameraPosition);
        var indices = chunk.InstanceIndices;
        var remaining = performance.MaxHighScaleVisibleInstances > 0
            ? System.Math.Max(0, performance.MaxHighScaleVisibleInstances - stats.HighScaleInstanceCount)
            : indices.Count;
        var count = System.Math.Min(indices.Count, remaining);

        if (count <= 0 || lod == HighScaleLodLevel3D.Culled)
        {
            stats.LodCulledCount += indices.Count;
            stats.CulledObjectCount += indices.Count;
            return;
        }

        var target = lod == HighScaleLodLevel3D.Detailed ? plan.Detailed :
            lod == HighScaleLodLevel3D.Simplified ? plan.Simplified :
            lod == HighScaleLodLevel3D.Billboard ? plan.Billboard :
            plan.Proxy;

        for (var i = 0; i < count; i++)
        {
            target.Add(indices[i]);
        }

        stats.HighScaleInstanceCount += count;
        if (lod == HighScaleLodLevel3D.Detailed) stats.LodDetailedCount += count;
        else if (lod == HighScaleLodLevel3D.Simplified) stats.LodSimplifiedCount += count;
        else if (lod == HighScaleLodLevel3D.Proxy) stats.LodProxyCount += count;
        else if (lod == HighScaleLodLevel3D.Billboard) stats.LodBillboardCount += count;

        if (count < indices.Count)
        {
            var culled = indices.Count - count;
            stats.LodCulledCount += culled;
            stats.CulledObjectCount += culled;
        }
    }

    private static HighScaleLodLevel3D ResolveChunkLod(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, Vector3 cameraPosition)
    {
        var center = chunk.Bounds.Center;
        var d2 = Vector3.DistanceSquared(cameraPosition, center);
        var policy = layer.LodPolicy;
        if (d2 > policy.DrawDistance * policy.DrawDistance) return HighScaleLodLevel3D.Culled;
        if (d2 <= policy.DetailedDistance * policy.DetailedDistance) return HighScaleLodLevel3D.Detailed;
        if (d2 <= policy.SimplifiedDistance * policy.SimplifiedDistance) return HighScaleLodLevel3D.Simplified;
        if (d2 <= policy.ProxyDistance * policy.ProxyDistance) return HighScaleLodLevel3D.Proxy;
        return policy.EnableBillboardFallback ? HighScaleLodLevel3D.Billboard : HighScaleLodLevel3D.Proxy;
    }

    private void DrawHighScaleLod(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        HighScaleChunk3D chunk,
        HighScaleLodLevel3D lod,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        if (instanceIndices.Count == 0) return;

        var buildStart = Stopwatch.GetTimestamp();
        var key = new HighScaleBatchKey(layer.Id, chunk.Key, lod);
        var batch = EnsureHighScaleGpuBatch(gl, layer, key, chunk.IsDirty, lod, instanceIndices, cameraPosition, performance, stats);
        stats.HighScaleBufferBuildMilliseconds += GetElapsedMilliseconds(buildStart);
        if (batch.InstanceCount == 0) return;

        var parts = layer.Template.ResolveParts(lod);
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var meshResource = EnsureMeshResource(gl, part.Mesh.ResourceKey, part.Mesh.GeometryVersion, part.Mesh, stats);
            BindMeshAttributes(gl, meshResource);
            EnableHighScaleInstanceAttributes(gl, batch);
            UploadHighScalePalette(gl, layer, part, performance, stats);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadHighScaleMaterial(part.LightingMode);
            _drawElementsInstanced?.Invoke(GlTriangles, meshResource.IndexCount, GlUnsignedInt, IntPtr.Zero, batch.InstanceCount);
            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
            stats.InstancedBatchCount++;
            stats.VisibleMeshCount += batch.InstanceCount;
            stats.HighScaleVisiblePartInstanceCount += batch.InstanceCount;
            if (part.UsesVertexMaterialSlots) stats.BakedHighScalePartDraws++;
            stats.TriangleCount += (part.Mesh.Indices.Length / 3) * batch.InstanceCount;
        }
    }

    private HighScaleGpuBatchData EnsureHighScaleGpuBatch(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        HighScaleBatchKey key,
        bool structuralDirty,
        HighScaleLodLevel3D lod,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        if (!_highScaleGpuBatches.TryGetValue(key, out var batch))
        {
            batch = new HighScaleGpuBatchData
            {
                TransformBuffer = gl.GenBuffer(),
                StateBuffer = gl.GenBuffer()
            };
            _highScaleGpuBatches[key] = batch;
        }

        var dynamicFadeState = performance.EnableHighScaleDynamicFadeState;
        var fadeVersion = dynamicFadeState ? QuantizeCameraForFade(cameraPosition) : 0;
        var rebuildNeeded = structuralDirty || !batch.Matches(instanceIndices);
        if (rebuildNeeded)
        {
            var uploadLimit = performance.HighScaleMaxTransformBatchUploadsPerFrame;
            if (uploadLimit > 0 && _highScaleTransformBatchUploadsThisFrame >= uploadLimit)
            {
                return batch;
            }

            RebuildHighScaleGpuBatch(gl, layer, instanceIndices, cameraPosition, batch, fadeVersion, dynamicFadeState, stats);
            _highScaleTransformBatchUploadsThisFrame++;
            return batch;
        }

        if (!performance.EnableWebGlClientGpuTransformAnimation && batch.HasStaleTransforms(layer))
        {
            var transformStart = Stopwatch.GetTimestamp();
            var changed = batch.UpdateTransforms(layer);
            if (changed > 0)
            {
                gl.BindBuffer(GlArrayBuffer, batch.TransformBuffer);
                if (_bufferSubData is not null && batch.TransformBufferCapacityBytes >= batch.TransformFloatCount * sizeof(float))
                {
                    UploadFloatsSubData(GlArrayBuffer, 0, batch.TransformData, 0, batch.TransformFloatCount);
                }
                else
                {
                    UploadFloats(gl, GlArrayBuffer, batch.TransformData, batch.TransformFloatCount, GlDynamicDraw);
                    batch.TransformBufferCapacityBytes = batch.TransformFloatCount * sizeof(float);
                    stats.InstanceBufferUploads++;
                }

                stats.InstanceUploadBytes += batch.TransformFloatCount * sizeof(float);
                stats.TransformUploadBytes += batch.TransformFloatCount * sizeof(float);
                stats.HighScaleUploadMilliseconds += GetElapsedMilliseconds(transformStart);
            }
        }

        if (batch.StateVersion != layer.StateBuffer.Version ||
            batch.MaterialResolverVersion != layer.MaterialResolverVersion ||
            batch.LodPolicyVersion != layer.LodPolicy.Version ||
            batch.FadeVersion != fadeVersion)
        {
            var stateStart = Stopwatch.GetTimestamp();
            UpdateHighScaleStateBuffer(gl, layer, cameraPosition, batch, fadeVersion, dynamicFadeState, performance, stats);
            stats.HighScaleUploadMilliseconds += GetElapsedMilliseconds(stateStart);
        }

        return batch;
    }

    private void RebuildHighScaleGpuBatch(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        HighScaleGpuBatchData batch,
        int fadeVersion,
        bool dynamicFadeState,
        RenderStats stats)
    {
        batch.ResetCpuData();
        for (var i = 0; i < instanceIndices.Count; i++)
        {
            var instanceIndex = instanceIndices[i];
            var record = layer.Instances[instanceIndex];
            batch.Add(
                instanceIndex,
                record.Transform,
                record.TransformVersion,
                record.MaterialVariantId,
                IsHighScaleVisible(record),
                ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState));
        }

        var uploadStart = Stopwatch.GetTimestamp();
        gl.BindBuffer(GlArrayBuffer, batch.TransformBuffer);
        UploadFloats(gl, GlArrayBuffer, batch.TransformData, batch.TransformFloatCount, GlStaticDraw);
        gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
        UploadFloats(gl, GlArrayBuffer, batch.StateData, batch.StateFloatCount, GlDynamicDraw);
        stats.HighScaleUploadMilliseconds += GetElapsedMilliseconds(uploadStart);

        batch.StateVersion = layer.StateBuffer.Version;
        batch.MaterialResolverVersion = layer.MaterialResolverVersion;
        batch.LodPolicyVersion = layer.LodPolicy.Version;
        batch.FadeVersion = fadeVersion;
        batch.TransformBufferCapacityBytes = batch.TransformFloatCount * sizeof(float);
        batch.StateBufferCapacityBytes = batch.StateFloatCount * sizeof(float);
        stats.InstanceBufferUploads++;
        stats.StateBufferUploads++;
        stats.InstanceUploadBytes += batch.TransformBufferCapacityBytes;
        stats.StateUploadBytes += batch.StateBufferCapacityBytes;
    }

    private void UpdateHighScaleStateBuffer(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        Vector3 cameraPosition,
        HighScaleGpuBatchData batch,
        int fadeVersion,
        bool dynamicFadeState,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        if (batch.InstanceCount == 0)
        {
            batch.StateVersion = layer.StateBuffer.Version;
            batch.MaterialResolverVersion = layer.MaterialResolverVersion;
            batch.LodPolicyVersion = layer.LodPolicy.Version;
            batch.FadeVersion = fadeVersion;
            return;
        }

        var dirtyIndices = layer.StateBuffer.DirtyIndices;
        var resolverChanged = batch.MaterialResolverVersion != layer.MaterialResolverVersion;
        var lodPolicyChanged = batch.LodPolicyVersion != layer.LodPolicy.Version;
        var fadeChanged = batch.FadeVersion != fadeVersion;

        // Important: dirtyIndices is global for the layer, while this batch contains only
        // one visible chunk/LOD subset. The previous implementation compared the global
        // dirty count with this batch size and therefore forced a full state upload for
        // almost every visible batch under telemetry load. That made state upload scale
        // like visible part/batch work instead of changed logical instances.
        var forceFullUpdate = resolverChanged || lodPolicyChanged || fadeChanged || _bufferSubData is null ||
                              (dirtyIndices.Count == 0 && batch.StateVersion != layer.StateBuffer.Version);

        if (!forceFullUpdate)
        {
            batch.ResetDirtyOffsets();
            for (var i = 0; i < dirtyIndices.Count; i++)
            {
                var instanceIndex = dirtyIndices[i];
                if (!batch.TryGetOffset(instanceIndex, out var offset))
                {
                    continue;
                }

                var record = layer.Instances[instanceIndex];
                batch.WriteState(offset, record.MaterialVariantId, IsHighScaleVisible(record), ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState));
                batch.AddDirtyOffset(offset);
            }

            // Decide full-vs-partial per batch after we know how many dirty instances
            // actually belong to this batch. This is the core fix for 10k/50k telemetry.
            forceFullUpdate = batch.DirtyOffsetCount > System.Math.Max(32, batch.InstanceCount / 3);
        }

        if (forceFullUpdate)
        {
            for (var offset = 0; offset < batch.InstanceCount; offset++)
            {
                var instanceIndex = batch.GetInstanceIndexAt(offset);
                var record = layer.Instances[instanceIndex];
                batch.WriteState(offset, record.MaterialVariantId, IsHighScaleVisible(record), ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState));
            }

            gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
            if (_bufferSubData is not null && batch.StateBufferCapacityBytes >= batch.StateFloatCount * sizeof(float))
            {
                UploadFloatsSubData(GlArrayBuffer, 0, batch.StateData, 0, batch.StateFloatCount);
                stats.StateBufferSubDataUploads++;
            }
            else
            {
                UploadFloats(gl, GlArrayBuffer, batch.StateData, batch.StateFloatCount, GlDynamicDraw);
                batch.StateBufferCapacityBytes = batch.StateFloatCount * sizeof(float);
                stats.StateBufferUploads++;
            }

            stats.StateUploadBytes += batch.StateFloatCount * sizeof(float);
        }
        else if (batch.DirtyOffsetCount > 0)
        {
            gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
            batch.SortDirtyOffsets();

            var mergeGap = System.Math.Max(0, performance.HighScalePartialStateMergeGap);
            var rangeStart = batch.GetDirtyOffsetAt(0);
            var previous = rangeStart;
            for (var i = 1; i <= batch.DirtyOffsetCount; i++)
            {
                var current = i < batch.DirtyOffsetCount ? batch.GetDirtyOffsetAt(i) : -1;
                if (current >= 0 && current <= previous + 1 + mergeGap)
                {
                    previous = current;
                    continue;
                }

                var floatOffset = rangeStart * HighScaleStateFloatStride;
                var floatCount = (previous - rangeStart + 1) * HighScaleStateFloatStride;
                UploadFloatsSubData(GlArrayBuffer, floatOffset * sizeof(float), batch.StateData, floatOffset, floatCount);
                stats.StateBufferSubDataUploads++;
                stats.StateUploadBytes += floatCount * sizeof(float);
                rangeStart = current;
                previous = current;
            }
        }

        batch.StateVersion = layer.StateBuffer.Version;
        batch.MaterialResolverVersion = layer.MaterialResolverVersion;
        batch.LodPolicyVersion = layer.LodPolicy.Version;
        batch.FadeVersion = fadeVersion;
    }

    private void UploadHighScalePalette(GlInterface gl, HighScaleInstanceLayer3D layer, CompositePartTemplate3D part, ScenePerformanceOptions performance, RenderStats stats)
    {
        if (part.UsesVertexMaterialSlots && _paletteTexture != 0)
        {
            UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 1f);
            UploadHighScalePaletteTexture(gl, layer, part);
            return;
        }

        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        var count = ResolveActiveVariantSlotCount(layer);
        for (var i = 0; i < count; i++)
        {
            UploadColor(_uniform4f, _meshVariantColorLocations[i], layer.Template.ResolveColor(part, i));
        }
    }

    private void UploadHighScalePaletteTexture(GlInterface gl, HighScaleInstanceLayer3D layer, CompositePartTemplate3D part)
    {
        var variantCount = ResolveActiveVariantSlotCount(layer);
        var slotCount = System.Math.Clamp(part.MaterialSlotBaseColors.Count, 1, 64);
        var required = variantCount * slotCount * 4;
        if (_paletteUploadBuffer.Length < required)
        {
            _paletteUploadBuffer = new byte[required];
        }

        for (var variant = 0; variant < variantCount; variant++)
        {
            for (var slot = 0; slot < slotCount; slot++)
            {
                var baseColor = slot < part.MaterialSlotBaseColors.Count ? part.MaterialSlotBaseColors[slot] : part.BaseColor;
                var color = layer.Template.ResolveColor(slot, baseColor, variant);
                var offset = ((variant * slotCount) + slot) * 4;
                _paletteUploadBuffer[offset] = ToByte(color.R);
                _paletteUploadBuffer[offset + 1] = ToByte(color.G);
                _paletteUploadBuffer[offset + 2] = ToByte(color.B);
                _paletteUploadBuffer[offset + 3] = ToByte(color.A);
            }
        }

        var handle = GCHandle.Alloc(_paletteUploadBuffer, GCHandleType.Pinned);
        try
        {
            gl.ActiveTexture(GlTexture1);
            gl.BindTexture(GlTexture2D, _paletteTexture);
            gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlNearest);
            gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlNearest);
            gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
            gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
            gl.TexImage2D(GlTexture2D, 0, GlRgba, slotCount, variantCount, 0, GlRgba, GlUnsignedByte, handle.AddrOfPinnedObject());
            if (_meshPaletteTextureLocation >= 0) _uniform1i?.Invoke(_meshPaletteTextureLocation, 1);
            UploadFloat(_uniform1f, _meshPaletteWidthLocation, slotCount);
            UploadFloat(_uniform1f, _meshPaletteHeightLocation, variantCount);
            gl.ActiveTexture(GlTexture0);
        }
        finally
        {
            handle.Free();
        }
    }

    private static byte ToByte(float value)
        => (byte)System.Math.Clamp((int)System.MathF.Round(System.Math.Clamp(value, 0f, 1f) * 255f), 0, 255);

    private static int ResolveActiveVariantSlotCount(HighScaleInstanceLayer3D layer)
    {
        var max = 0;
        foreach (var id in layer.Template.MaterialVariants.Keys)
        {
            if (id > max) max = id;
        }

        return System.Math.Clamp(max + 1, 1, MaxHighScaleMaterialVariants);
    }

    private static bool IsHighScaleVisible(InstanceRecord3D record)
        => (record.Flags & InstanceFlags3D.Visible) != 0;

    private static float ResolveHighScaleStateAlpha(HighScaleInstanceLayer3D layer, InstanceRecord3D record, Vector3 cameraPosition, bool dynamicFadeState)
    {
        if (!IsHighScaleVisible(record)) return 0f;
        return dynamicFadeState ? layer.LodPolicy.ResolveFadeAlpha(cameraPosition, record.Transform) : 1f;
    }

    private static int QuantizeCameraForFade(Vector3 cameraPosition)
    {
        // Fade alpha is allowed to update less frequently than raw camera motion.
        // This keeps retained transform batches stable while still preventing abrupt draw-distance popping.
        const float cell = 2f;
        return HashCode.Combine(
            (int)System.MathF.Floor(cameraPosition.X / cell),
            (int)System.MathF.Floor(cameraPosition.Y / cell),
            (int)System.MathF.Floor(cameraPosition.Z / cell));
    }

    private static float ResolveDistanceAlpha(Scene3D scene, Matrix4x4 model)
    {
        var drawDistance = scene.Performance.DrawDistance;
        if (drawDistance <= 0f || float.IsPositiveInfinity(drawDistance))
        {
            return 1f;
        }

        var camera = scene.Camera.Position;
        var pos = new Vector3(model.M41, model.M42, model.M43);
        var distance = Vector3.Distance(camera, pos);
        if (distance > drawDistance)
        {
            return 0f;
        }

        if (!scene.Performance.EnableDistanceFade || scene.Performance.DistanceFadeBand <= 0.001f)
        {
            return 1f;
        }

        var fadeStart = System.MathF.Max(0f, drawDistance - scene.Performance.DistanceFadeBand);
        if (distance <= fadeStart)
        {
            return 1f;
        }

        return System.Math.Clamp(1f - ((distance - fadeStart) / System.MathF.Max(scene.Performance.DistanceFadeBand, 0.001f)), 0f, 1f);
    }

    private static ColorRgba ApplyDistanceAlpha(ColorRgba color, float alpha)
        => alpha >= 0.999f ? color : new ColorRgba(color.R, color.G, color.B, color.A * alpha);

    private static ColorRgba ResolveColor(Object3D obj)
    {
        var color = obj.Material.EffectiveColor;
        if (obj.IsEffectivelyHovered) color = color.BlendTowards(ColorRgba.White, 0.10f);
        if (obj.IsEffectivelySelected) color = color.BlendTowards(ColorRgba.White, 0.22f);
        return color;
    }

    private void DrawSurfaceOverlays(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        if (!scene.Debug.ShowWireframeOverlay && !scene.Debug.ShowSilhouetteOverlay) return;
        gl.UseProgram(_meshProgram);
        UploadMatrix(_uniformMatrix4fv, _meshViewProjLocation, viewProjection, _matrixUploadBuffer);
        UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        UploadFloat(_uniform1f, _meshShadowEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshLightingEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshNormalMapStrengthLocation, 0f);
        UploadVector4(_uniform4f, _meshPostProcessParamsLocation, Vector4.Zero);
        UploadVector4(_uniform4f, _meshSsaoParamsLocation, Vector4.Zero);

        foreach (var obj in scene.Registry.Renderables)
        {
            if (!obj.IsVisible) continue;
            if (obj is ParticleSystem3D particlesForWireframe) particlesForWireframe.SetBillboardBasis(scene.Camera.Right, scene.Camera.SafeUp, scene.Camera.Forward);
            var mesh = obj.GetMesh();
            if (mesh.RenderGeometry.WireframeIndexCount == 0) continue;
            var model = obj.GetModelMatrix();
            if (!FrustumCuller3D.IntersectsLocalBounds(mesh.LocalBounds, model, viewProjection)) continue;

            var resource = EnsureMeshResource(gl, mesh.ResourceKey, mesh.GeometryVersion, mesh, stats);
            BindMeshAttributes(gl, resource);
            gl.BindBuffer(GlElementArrayBuffer, resource.WireframeIndexBuffer);
            UploadMatrix(_uniformMatrix4fv, _meshModelLocation, model, _matrixUploadBuffer);
            if (scene.Debug.ShowWireframeOverlay)
            {
                UploadColor(_uniform4f, _meshColorLocation, new ColorRgba(0.02f, 0.02f, 0.02f, 0.95f));
                gl.DrawElements(GlLines, resource.WireframeIndexCount, GlUnsignedInt, IntPtr.Zero);
                stats.WireframeOverlayDrawCalls++;
                stats.DrawCallCount++;
            }

            if (scene.Debug.ShowSilhouetteOverlay && (obj.IsEffectivelyHovered || obj.IsEffectivelySelected))
            {
                UploadColor(_uniform4f, _meshColorLocation, obj.IsEffectivelySelected ? ColorRgba.White : new ColorRgba(1f, 0.85f, 0.25f, 1f));
                gl.DrawElements(GlLines, resource.WireframeIndexCount, GlUnsignedInt, IntPtr.Zero);
                stats.SilhouetteOverlayDrawCalls++;
                stats.DrawCallCount++;
            }
        }
    }

    private void DrawControlPlanes(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        var planes = new List<(ControlPlane3D Plane, float Depth)>();
        var objects = scene.Registry.AllObjects;
        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            if (objects[objectIndex] is not ControlPlane3D plane || !plane.IsVisible || plane.Snapshot is null) continue;
            var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
            var depth = 0f;
            for (var i = 0; i < corners.Length; i++) depth += Vector3.DistanceSquared(scene.Camera.Position, corners[i]);
            planes.Add((plane, depth / 4f));
        }
        if (planes.Count == 0) return;

        gl.Enable(GlBlend);
        _blendFunc?.Invoke(GlSrcAlpha, GlOneMinusSrcAlpha);
        _depthMask?.Invoke(0);
        gl.UseProgram(_texturedProgram);
        gl.ActiveTexture(GlTexture0);
        if (_textureSamplerLocation >= 0) _uniform1i?.Invoke(_textureSamplerLocation, 0);
        UploadMatrix(_uniformMatrix4fv, _textureViewProjLocation, viewProjection, _matrixUploadBuffer);
        planes.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        foreach (var (plane, _) in planes)
        {
            var texture = EnsureControlTexture(gl, plane, stats);
            if (texture is null) continue;
            var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
            BuildWorldControlVertices(corners, _controlVertexData);
            gl.BindTexture(GlTexture2D, texture.TextureId);
            gl.BindBuffer(GlArrayBuffer, _controlVertexBuffer);
            UploadFloats(gl, GlArrayBuffer, _controlVertexData, _controlVertexData.Length, GlDynamicDraw);
            gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
            gl.EnableVertexAttribArray(_texturePositionLocation);
            gl.VertexAttribPointer(_texturePositionLocation, 3, GlFloat, 0, sizeof(float) * 5, IntPtr.Zero);
            gl.EnableVertexAttribArray(_textureUvLocation);
            gl.VertexAttribPointer(_textureUvLocation, 2, GlFloat, 0, sizeof(float) * 5, new IntPtr(sizeof(float) * 3));
            gl.DrawElements(GlTriangles, 6, GlUnsignedInt, IntPtr.Zero);
            stats.ControlPlaneCount++;
            stats.DrawCallCount++;
        }
        _depthMask?.Invoke(1);
        _disable?.Invoke(GlBlend);
    }

    private MeshGpuResource EnsureMeshResource(GlInterface gl, string id, int geometryVersion, Mesh3D mesh, RenderStats stats)
    {
        var geometry = mesh.RenderGeometry;
        var uploadUsage = geometry.HasSkinWeights || id.Contains(":cpu-skin:", StringComparison.Ordinal) ? GlDynamicDraw : GlStaticDraw;
        if (_meshResources.TryGetValue(id, out var resource))
        {
            if (resource.GeometryVersion == geometryVersion) return resource;
            if (resource.VertexCount == geometry.Positions.Length &&
                resource.IndexCount == geometry.Indices.Length &&
                resource.WireframeIndexCount == geometry.WireframeIndices.Length)
            {
                UploadMeshResourceData(gl, resource, mesh, uploadUsage);
                UpdateMeshResourceCounters(resource, geometry, geometryVersion);
                AddMeshUploadStats(stats, geometry);
                return resource;
            }

            resource.Dispose(gl);
        }

        resource = new MeshGpuResource
        {
            GeometryVersion = geometryVersion,
            VertexBuffer = gl.GenBuffer(),
            NormalBuffer = gl.GenBuffer(),
            TexCoordBuffer = gl.GenBuffer(),
            TangentBuffer = gl.GenBuffer(),
            MaterialSlotBuffer = gl.GenBuffer(),
            IndexBuffer = gl.GenBuffer(),
            WireframeIndexBuffer = gl.GenBuffer()
        };
        UploadMeshResourceData(gl, resource, mesh, uploadUsage);
        UpdateMeshResourceCounters(resource, geometry, geometryVersion);
        _meshResources[id] = resource;
        AddMeshUploadStats(stats, geometry);
        return resource;
    }

    private static void UpdateMeshResourceCounters(MeshGpuResource resource, RenderGeometry3D geometry, int geometryVersion)
    {
        resource.GeometryVersion = geometryVersion;
        resource.VertexCount = geometry.Positions.Length;
        resource.IndexCount = geometry.Indices.Length;
        resource.WireframeIndexCount = geometry.WireframeIndices.Length;
        resource.VertexUploadBytes = geometry.EstimatedVertexUploadBytes;
        resource.IndexUploadBytes = geometry.EstimatedIndexUploadBytes;
    }

    private static void AddMeshUploadStats(RenderStats stats, RenderGeometry3D geometry)
    {
        stats.DirtyMeshUploads++;
        stats.RenderGeometryCount++;
        stats.VertexBufferUploadCount += 5;
        stats.IndexBufferUploadCount += 2;
        stats.VertexBufferUploadBytes += geometry.EstimatedVertexUploadBytes;
        stats.IndexBufferUploadBytes += geometry.EstimatedIndexUploadBytes;
        stats.MeshUploadBytes += geometry.EstimatedUploadBytes;
        stats.TangentUploadBytes += geometry.HasTangents ? geometry.Tangents.LongLength * sizeof(float) * 4L : 0L;
        stats.WireframeIndexUploadBytes += geometry.EstimatedWireframeIndexUploadBytes;
        if (geometry.HasTangentSpace) stats.TangentSpaceMeshCount++;
    }

    private static void UploadMeshResourceData(GlInterface gl, MeshGpuResource resource, Mesh3D mesh, int usage)
    {
        var geometry = mesh.RenderGeometry;
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        UploadVector3(gl, GlArrayBuffer, geometry.Positions, usage);
        gl.BindBuffer(GlArrayBuffer, resource.NormalBuffer);
        UploadVector3(gl, GlArrayBuffer, geometry.HasNormals ? geometry.Normals : GetNormalsOrDefault(mesh), usage);
        gl.BindBuffer(GlArrayBuffer, resource.TexCoordBuffer);
        UploadVector2(gl, GlArrayBuffer, geometry.HasTexCoords0 ? geometry.TexCoords0 : GetTexCoordsOrDefault(mesh), usage);
        gl.BindBuffer(GlArrayBuffer, resource.TangentBuffer);
        UploadVector4(gl, GlArrayBuffer, geometry.HasTangents ? geometry.Tangents : GetTangentsOrDefault(mesh), usage);
        gl.BindBuffer(GlArrayBuffer, resource.MaterialSlotBuffer);
        UploadFloats(gl, GlArrayBuffer, geometry.HasMaterialSlots ? geometry.MaterialSlots : GetMaterialSlotsOrDefault(mesh), geometry.Positions.Length, usage);
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
        UploadInts(gl, GlElementArrayBuffer, geometry.Indices, usage);
        gl.BindBuffer(GlElementArrayBuffer, resource.WireframeIndexBuffer);
        UploadInts(gl, GlElementArrayBuffer, geometry.WireframeIndices, usage);
    }

    private MaterialTextureResource? EnsureMaterialTexture(GlInterface gl, string? key, byte[]? data, int version, int textureUnit, RenderStats stats)
    {
        if (string.IsNullOrWhiteSpace(key) || data is not { Length: > 0 }) return null;
        if (_materialTextures.TryGetValue(key, out var resource) && resource.Version == version) return resource;

        if (!TextureDecodeHelper3D.TryDecodeRgba(data, out var decoded, out _)) return null;
        if (resource is null)
        {
            resource = new MaterialTextureResource { TextureId = gl.GenTexture(), Version = -1 };
            _materialTextures[key] = resource;
        }

        var rgbaHandle = GCHandle.Alloc(decoded.RgbaPixels, GCHandleType.Pinned);
        try
        {
            gl.ActiveTexture(textureUnit);
            gl.BindTexture(GlTexture2D, resource.TextureId);
            gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
            gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
            gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
            gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
            gl.TexImage2D(GlTexture2D, 0, GlRgba, decoded.Width, decoded.Height, 0, GlRgba, GlUnsignedByte, rgbaHandle.AddrOfPinnedObject());
            gl.ActiveTexture(GlTexture0);
            resource.Version = version;
            resource.Width = decoded.Width;
            resource.Height = decoded.Height;
            stats.DirtyTextureUploads++;
            stats.TextureUploadBytes += decoded.ByteLength;
        }
        finally
        {
            rgbaHandle.Free();
        }

        return resource;
    }

    private ControlTextureResource? EnsureControlTexture(GlInterface gl, ControlPlane3D plane, RenderStats stats)
    {
        var snapshot = plane.Snapshot;
        if (snapshot is null) return null;
        if (!_controlTextures.TryGetValue(plane.Id, out var resource))
        {
            resource = new ControlTextureResource { TextureId = gl.GenTexture(), SnapshotVersion = -1 };
            _controlTextures[plane.Id] = resource;
        }
        if (resource.SnapshotVersion == plane.SnapshotVersion) return resource;

        var pixelWidth = System.Math.Max(plane.RenderPixelWidth, 1);
        var pixelHeight = System.Math.Max(plane.RenderPixelHeight, 1);
        var stride = pixelWidth * 4;
        var bufferSize = stride * pixelHeight;
        var bgraPixels = new byte[bufferSize];
        var bgraHandle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
        try { snapshot.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), bgraHandle.AddrOfPinnedObject(), bufferSize, stride); }
        finally { bgraHandle.Free(); }

        var rgbaPixels = new byte[bufferSize];
        for (var i = 0; i < bufferSize; i += 4)
        {
            rgbaPixels[i] = bgraPixels[i + 2];
            rgbaPixels[i + 1] = bgraPixels[i + 1];
            rgbaPixels[i + 2] = bgraPixels[i];
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
            stats.DirtyTextureUploads++;
            stats.TextureUploadBytes += bufferSize;
        }
        finally { rgbaHandle.Free(); }
        return resource;
    }

    private static void BuildWorldControlVertices(Vector3[] worldCorners, float[] vertexData)
    {
        for (var i = 0; i < 4; i++)
        {
            var baseIndex = i * 5;
            vertexData[baseIndex] = worldCorners[i].X;
            vertexData[baseIndex + 1] = worldCorners[i].Y;
            vertexData[baseIndex + 2] = worldCorners[i].Z;
        }
        // Avalonia snapshot memory already arrives in top-row first order for the
        // renderer upload path used here. Sampling top corners with V=0 keeps text
        // readable; V=1 at the top mirrors the UI vertically.
        vertexData[3] = 0f; vertexData[4] = 0f;
        vertexData[8] = 1f; vertexData[9] = 0f;
        vertexData[13] = 1f; vertexData[14] = 1f;
        vertexData[18] = 0f; vertexData[19] = 1f;
    }

    private static void UploadFloats(GlInterface gl, int target, float[] data, int count, int usage)
    {
        if (count <= 0) return;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { gl.BufferData(target, new IntPtr(count * sizeof(float)), handle.AddrOfPinnedObject(), usage); }
        finally { handle.Free(); }
    }

    private void UploadFloatsSubData(int target, int byteOffset, float[] data, int floatOffset, int count)
    {
        if (count <= 0 || _bufferSubData is null) return;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var source = IntPtr.Add(handle.AddrOfPinnedObject(), floatOffset * sizeof(float));
            _bufferSubData(target, new IntPtr(byteOffset), new IntPtr(count * sizeof(float)), source);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void UploadVector3(GlInterface gl, int target, Vector3[] data, int usage)
    {
        var floats = new float[data.Length * 3];
        for (var i = 0; i < data.Length; i++)
        {
            var baseIndex = i * 3;
            floats[baseIndex] = data[i].X;
            floats[baseIndex + 1] = data[i].Y;
            floats[baseIndex + 2] = data[i].Z;
        }
        UploadFloats(gl, target, floats, floats.Length, usage);
    }

    private static void UploadVector2(GlInterface gl, int target, Vector2[] data, int usage)
    {
        var floats = new float[data.Length * 2];
        for (var i = 0; i < data.Length; i++)
        {
            var baseIndex = i * 2;
            floats[baseIndex] = data[i].X;
            floats[baseIndex + 1] = data[i].Y;
        }
        UploadFloats(gl, target, floats, floats.Length, usage);
    }

    private static void UploadVector4(GlInterface gl, int target, Vector4[] data, int usage)
    {
        var floats = new float[data.Length * 4];
        for (var i = 0; i < data.Length; i++)
        {
            var baseIndex = i * 4;
            floats[baseIndex] = data[i].X;
            floats[baseIndex + 1] = data[i].Y;
            floats[baseIndex + 2] = data[i].Z;
            floats[baseIndex + 3] = data[i].W;
        }
        UploadFloats(gl, target, floats, floats.Length, usage);
    }

    private static void UploadInts(GlInterface gl, int target, int[] data, int usage)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { gl.BufferData(target, new IntPtr(data.Length * sizeof(int)), handle.AddrOfPinnedObject(), usage); }
        finally { handle.Free(); }
    }


    private static void ApplyAnimationStats(RenderStats stats, Scene3D scene, bool gpuSkinningActive, string fallbackReason)
    {
        var imported = 0;
        var skinned = 0;
        var animated = 0;
        var skinMatrices = 0;
        var skinnedPrimitives = 0;
        long skinPayloadBytes = 0;
        var objects = scene.Registry.AllObjects;
        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            if (objects[objectIndex] is not ImportedModel3D model) continue;
            imported++;
            if (model.HasSkins)
            {
                skinned++;
                foreach (var skin in model.Asset.Skins) skinMatrices += skin.BoneCount;
            }
            if (model.HasAnimations || model.Animation.CurrentClip is not null) animated++;
            foreach (var part in model.ModelParts)
            {
                if (!part.IsSkinned) continue;
                skinnedPrimitives++;
                skinPayloadBytes += part.Primitive.Positions.LongLength * sizeof(float) * 8L;
            }
        }

        stats.ImportedModelCount = imported;
        stats.SkinnedModelCount = skinned;
        stats.AnimatedModelCount = animated;
        stats.SkinMatrixCount = skinMatrices;
        stats.SkinnedPrimitiveCount = skinnedPrimitives;
        stats.SkinningVertexPayloadBytes = skinPayloadBytes;
        stats.GpuSkinningRequested = skinned > 0;
        stats.GpuSkinningActive = gpuSkinningActive;
        stats.SkinningFallbackReason = gpuSkinningActive || skinned == 0 ? string.Empty : fallbackReason;
    }

    private void UploadClientTransformAnimation(Scene3D scene, bool enabled)
    {
        var active = enabled && scene.Performance.EnableWebGlClientGpuTransformAnimation;
        UploadFloat(_uniform1f, _meshClientAnimationEnabledLocation, active ? 1f : 0f);
        UploadFloat(_uniform1f, _meshClientAnimationTimeLocation, active ? (float)_animationClock.Elapsed.TotalSeconds : 0f);
        UploadFloat(_uniform1f, _meshClientAnimationAmplitudeLocation, active ? global::System.Math.Max(0f, scene.Performance.WebGlClientGpuTransformAnimationAmplitude) : 0f);
    }

    private void UploadLighting(Scene3D scene)
    {
        var light = SceneLightingResolver3D.Resolve(scene);
        UploadVector3(_uniform3f, _meshAmbientLightLocation, light.Ambient);
        UploadVector3(_uniform3f, _meshDirectionalLightDirectionLocation, light.DirectionalDirection);
        UploadVector3(_uniform3f, _meshDirectionalLightColorLocation, light.DirectionalColor);
        UploadVector4(_uniform4f, _meshPointLightPositionLocation, light.PointPosition);
        UploadVector4(_uniform4f, _meshPointLightColorLocation, light.PointColor);
        UploadVector4(_uniform4f, _meshSpotLightPositionLocation, light.SpotPosition);
        UploadVector4(_uniform4f, _meshSpotLightDirectionLocation, light.SpotDirection);
        UploadVector4(_uniform4f, _meshSpotLightColorLocation, light.SpotColor);
        UploadVector4(_uniform4f, _meshSpotLightConeLocation, light.SpotCone);
    }

    private void UploadClassicMaterial(GlInterface gl, MaterialBinding3D material, RenderStats stats)
    {
        UploadFloat(_uniform1f, _meshLightingEnabledLocation, ToLightingUniform(material.Lighting));
        UploadVector3(_uniform3f, _meshSpecularColorLocation, new Vector3(material.SpecularColor.R, material.SpecularColor.G, material.SpecularColor.B));
        UploadVector4(_uniform4f, _meshSpecularParamsLocation, new Vector4(material.SpecularStrength, material.Shininess, material.Metallic, material.Roughness));
        UploadVector4(_uniform4f, _meshMaterialStrengthsLocation, new Vector4(material.AmbientStrength, material.DiffuseStrength, material.NormalMapStrength, material.HasNormalMap ? 1f : 0f));
        UploadFloat(_uniform1f, _meshNormalMapStrengthLocation, material.HasNormalMap ? material.NormalMapStrength : 0f);
        UploadVector4(_uniform4f, _meshAlphaParamsLocation, new Vector4(material.AlphaCutoff, material.Surface == SurfaceMode.Transparent ? 1f : 0f, 0f, 0f));
        UploadVector4(_uniform4f, _meshEmissiveColorLocation, new Vector4(material.EmissiveColor.R, material.EmissiveColor.G, material.EmissiveColor.B, material.EmissiveColor.A));
        UploadMaterialTexture(gl, material.HasBaseColorTexture, material.BaseColorTextureKey, material.BaseColorTextureData, material.BaseColorTextureVersion, GlTexture2, 2, _meshBaseColorTextureLocation, _meshBaseColorTextureEnabledLocation, stats);
        UploadMaterialTexture(gl, material.HasNormalMap, material.NormalMapTextureKey, material.NormalMapTextureData, material.NormalMapTextureVersion, GlTexture3, 3, _meshNormalTextureLocation, _meshNormalTextureEnabledLocation, stats);
        UploadMaterialTexture(gl, material.HasMetallicRoughnessTexture, material.MetallicRoughnessTextureKey, material.MetallicRoughnessTextureData, material.MetallicRoughnessTextureVersion, GlTexture4, 4, _meshMetallicRoughnessTextureLocation, _meshMetallicRoughnessTextureEnabledLocation, stats);
        UploadMaterialTexture(gl, material.HasEmissiveTexture, material.EmissiveTextureKey, material.EmissiveTextureData, material.EmissiveTextureVersion, GlTexture5, 5, _meshEmissiveTextureLocation, _meshEmissiveTextureEnabledLocation, stats);
    }

    private void UploadMaterialTexture(GlInterface gl, bool enabled, string? key, byte[]? data, int version, int glTextureUnit, int samplerSlot, int samplerLocation, int enabledLocation, RenderStats stats)
    {
        if (!enabled || string.IsNullOrWhiteSpace(key) || data is not { Length: > 0 })
        {
            UploadFloat(_uniform1f, enabledLocation, 0f);
            return;
        }

        var resource = EnsureMaterialTexture(gl, key, data, version, glTextureUnit, stats);
        if (resource is null)
        {
            UploadFloat(_uniform1f, enabledLocation, 0f);
            return;
        }

        gl.ActiveTexture(glTextureUnit);
        gl.BindTexture(GlTexture2D, resource.TextureId);
        _uniform1i?.Invoke(samplerLocation, samplerSlot);
        UploadFloat(_uniform1f, enabledLocation, 1f);
        gl.ActiveTexture(GlTexture0);
    }

    private void UploadHighScaleMaterial(LightingMode lightingMode)
    {
        UploadFloat(_uniform1f, _meshLightingEnabledLocation, ToLightingUniform(lightingMode));
        UploadVector3(_uniform3f, _meshSpecularColorLocation, new Vector3(1f, 1f, 1f));
        UploadVector4(_uniform4f, _meshSpecularParamsLocation, new Vector4(0.2f, 32f, 0f, 1f));
        UploadVector4(_uniform4f, _meshMaterialStrengthsLocation, new Vector4(1f, 1f, 0f, 0f));
        UploadFloat(_uniform1f, _meshNormalMapStrengthLocation, 0f);
        UploadVector4(_uniform4f, _meshAlphaParamsLocation, new Vector4(0.01f, 0f, 0f, 0f));
        UploadVector4(_uniform4f, _meshEmissiveColorLocation, Vector4.Zero);
        UploadFloat(_uniform1f, _meshBaseColorTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshNormalTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshMetallicRoughnessTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshEmissiveTextureEnabledLocation, 0f);
    }

    private static float ToLightingUniform(LightingMode mode)
        => mode == LightingMode.Unlit ? 0f : mode == LightingMode.Lambert ? 1f : mode == LightingMode.Phong ? 2f : 3f;

    private static float[] GetMaterialSlotsOrDefault(Mesh3D mesh)
    {
        if (mesh.MaterialSlots.Length == mesh.Positions.Length) return mesh.MaterialSlots;
        return new float[mesh.Positions.Length];
    }

    private static Vector3[] GetNormalsOrDefault(Mesh3D mesh)
    {
        if (mesh.Normals.Length == mesh.Positions.Length) return mesh.Normals;
        var normals = new Vector3[mesh.Positions.Length];
        for (var i = 0; i < normals.Length; i++) normals[i] = Vector3.UnitZ;
        return normals;
    }

    private static Vector2[] GetTexCoordsOrDefault(Mesh3D mesh)
    {
        if (mesh.TexCoords0.Length == mesh.Positions.Length) return mesh.TexCoords0;
        return new Vector2[mesh.Positions.Length];
    }

    private static Vector4[] GetTangentsOrDefault(Mesh3D mesh)
    {
        if (mesh.Tangents.Length == mesh.Positions.Length) return mesh.Tangents;
        var tangents = new Vector4[mesh.Positions.Length];
        for (var i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(1f, 0f, 0f, 1f);
        return tangents;
    }

    private static void UploadVector3(GlUniform3fDelegate? uniform3f, int location, Vector3 value)
    {
        if (location >= 0) uniform3f?.Invoke(location, value.X, value.Y, value.Z);
    }

    private static void UploadVector4(GlUniform4fDelegate? uniform4f, int location, Vector4 value)
    {
        if (location >= 0) uniform4f?.Invoke(location, value.X, value.Y, value.Z, value.W);
    }

    private static void UploadFloat(GlUniform1fDelegate? uniform1f, int location, float value)
    {
        if (location >= 0) uniform1f?.Invoke(location, value);
    }

    private static void UploadColor(GlUniform4fDelegate? uniform4f, int location, ColorRgba color)
    {
        if (location >= 0) uniform4f?.Invoke(location, color.R, color.G, color.B, color.A);
    }

    private static void UploadColor3(GlUniform3fDelegate? uniform3f, int location, ColorRgba color, float intensity = 1f)
    {
        if (location >= 0) uniform3f?.Invoke(location, color.R * intensity, color.G * intensity, color.B * intensity);
    }

    private static void UploadMatrix(GlUniformMatrix4fvDelegate? uniformMatrix4fv, int location, Matrix4x4 matrix, float[] buffer)
    {
        if (location < 0 || uniformMatrix4fv is null) return;
        WriteMatrix(buffer, 0, matrix);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { uniformMatrix4fv(location, 1, 0, handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }

    private static void UploadMatrixFromInstanceData(GlUniformMatrix4fvDelegate? uniformMatrix4fv, int location, float[] data, int offset, float[] buffer)
    {
        if (location < 0 || uniformMatrix4fv is null) return;
        Array.Copy(data, offset, buffer, 0, 16);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { uniformMatrix4fv(location, 1, 0, handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }

    private static void WriteMatrix(float[] buffer, int offset, Matrix4x4 matrix)
    {
        buffer[offset] = matrix.M11; buffer[offset + 1] = matrix.M12; buffer[offset + 2] = matrix.M13; buffer[offset + 3] = matrix.M14;
        buffer[offset + 4] = matrix.M21; buffer[offset + 5] = matrix.M22; buffer[offset + 6] = matrix.M23; buffer[offset + 7] = matrix.M24;
        buffer[offset + 8] = matrix.M31; buffer[offset + 9] = matrix.M32; buffer[offset + 10] = matrix.M33; buffer[offset + 11] = matrix.M34;
        buffer[offset + 12] = matrix.M41; buffer[offset + 13] = matrix.M42; buffer[offset + 14] = matrix.M43; buffer[offset + 15] = matrix.M44;
    }


    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
    }
    private static int CreateProgram(GlInterface gl, string vertexSource, string fragmentSource, params (int Location, string Name)[] attributes)
    {
        var vertexShader = gl.CreateShader(GlVertexShader);
        var vertexError = gl.CompileShaderAndGetError(vertexShader, vertexSource);
        if (!string.IsNullOrWhiteSpace(vertexError)) throw new InvalidOperationException($"Vertex shader compilation failed: {vertexError}");
        var fragmentShader = gl.CreateShader(GlFragmentShader);
        var fragmentError = gl.CompileShaderAndGetError(fragmentShader, fragmentSource);
        if (!string.IsNullOrWhiteSpace(fragmentError)) throw new InvalidOperationException($"Fragment shader compilation failed: {fragmentError}");
        var program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        foreach (var (location, name) in attributes) gl.BindAttribLocationString(program, location, name);
        var linkError = gl.LinkProgramAndGetError(program);
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
        if (!string.IsNullOrWhiteSpace(linkError)) throw new InvalidOperationException($"Program link failed: {linkError}");
        return program;
    }

    private static T? LoadDelegate<T>(GlInterface gl, string procName) where T : class
    {
        var proc = gl.GetProcAddress(procName);
        return proc == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(proc, typeof(T)) as T;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlBlendFuncDelegate(int sfactor, int dfactor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDepthMaskDelegate(byte flag);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDisableDelegate(int cap);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform1iDelegate(int location, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform1fDelegate(int location, float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform3fDelegate(int location, float x, float y, float z);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform4fDelegate(int location, float x, float y, float z, float w);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniformMatrix4fvDelegate(int location, int count, byte transpose, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlVertexAttribDivisorDelegate(int index, int divisor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDrawElementsInstancedDelegate(int mode, int count, int type, IntPtr indices, int instanceCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlBufferSubDataDelegate(int target, IntPtr offset, IntPtr size, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGenFramebuffersDelegate(int n, int[] framebuffers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDeleteFramebuffersDelegate(int n, int[] framebuffers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlFramebufferTexture2DDelegate(int target, int attachment, int textarget, int texture, int level);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GlCheckFramebufferStatusDelegate(int target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDrawBufferDelegate(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlReadBufferDelegate(int mode);


    private sealed class HighScaleChunkFramePlan
    {
        [ThreadStatic]
        private static HighScaleChunkFramePlan? _shared;

        public static HighScaleChunkFramePlan Shared => _shared ??= new HighScaleChunkFramePlan();

        public readonly List<int> Detailed = new(256);
        public readonly List<int> Simplified = new(1024);
        public readonly List<int> Proxy = new(1024);
        public readonly List<int> Billboard = new(1024);

        public void Reset()
        {
            Detailed.Clear();
            Simplified.Clear();
            Proxy.Clear();
            Billboard.Clear();
        }
    }
    private sealed class MeshGpuResource
    {
        public int GeometryVersion { get; set; }
        public int VertexBuffer { get; init; }
        public int NormalBuffer { get; init; }
        public int TexCoordBuffer { get; init; }
        public int TangentBuffer { get; init; }
        public int MaterialSlotBuffer { get; init; }
        public int IndexBuffer { get; init; }
        public int WireframeIndexBuffer { get; init; }
        public int VertexCount { get; set; }
        public int IndexCount { get; set; }
        public int WireframeIndexCount { get; set; }
        public long VertexUploadBytes { get; set; }
        public long IndexUploadBytes { get; set; }
        public void Dispose(GlInterface gl)
        {
            if (NormalBuffer != 0) gl.DeleteBuffer(NormalBuffer);
            if (TexCoordBuffer != 0) gl.DeleteBuffer(TexCoordBuffer);
            if (TangentBuffer != 0) gl.DeleteBuffer(TangentBuffer);
            if (MaterialSlotBuffer != 0) gl.DeleteBuffer(MaterialSlotBuffer);
            if (VertexBuffer != 0) gl.DeleteBuffer(VertexBuffer);
            if (IndexBuffer != 0) gl.DeleteBuffer(IndexBuffer);
            if (WireframeIndexBuffer != 0) gl.DeleteBuffer(WireframeIndexBuffer);
        }
    }

    private sealed class MeshBatchData
    {
        private float[] _data = new float[InstanceFloatStride * 64];
        public MeshBatchData(string meshKey, Mesh3D mesh, MaterialBinding3D material) { MeshKey = meshKey; Mesh = mesh; Material = material; }
        public string MeshKey { get; }
        public Mesh3D Mesh { get; set; }
        public MaterialBinding3D Material { get; set; }
        public int InstanceCount { get; private set; }
        public int FloatCount => InstanceCount * InstanceFloatStride;
        public float[] Data => _data;
        public void Reset() => InstanceCount = 0;
        public void Add(Matrix4x4 model, ColorRgba color)
        {
            EnsureCapacity((InstanceCount + 1) * InstanceFloatStride);
            var offset = InstanceCount * InstanceFloatStride;
            WriteMatrix(_data, offset, model);
            _data[offset + 16] = color.R; _data[offset + 17] = color.G; _data[offset + 18] = color.B; _data[offset + 19] = color.A;
            InstanceCount++;
        }
        private void EnsureCapacity(int required)
        {
            if (_data.Length >= required) return;
            var next = _data.Length;
            while (next < required) next *= 2;
            Array.Resize(ref _data, next);
        }
    }

    private readonly struct HighScaleBatchKey : IEquatable<HighScaleBatchKey>
    {
        private readonly string _layerId;
        private readonly HighScaleChunkKey3D _chunkKey;
        private readonly HighScaleLodLevel3D _lod;

        public HighScaleBatchKey(string layerId, HighScaleChunkKey3D chunkKey, HighScaleLodLevel3D lod)
        {
            _layerId = layerId;
            _chunkKey = chunkKey;
            _lod = lod;
        }

        public bool Equals(HighScaleBatchKey other)
            => string.Equals(_layerId, other._layerId, StringComparison.Ordinal) &&
               _chunkKey.Equals(other._chunkKey) &&
               _lod == other._lod;

        public override bool Equals(object? obj) => obj is HighScaleBatchKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_layerId, _chunkKey, _lod);
    }

    private sealed class HighScaleGpuBatchData
    {
        private float[] _transformData = new float[HighScaleTransformFloatStride * 128];
        private float[] _stateData = new float[HighScaleStateFloatStride * 128];
        private int[] _instanceIndices = new int[128];
        private int[] _transformVersions = new int[128];
        private readonly Dictionary<int, int> _offsetByInstanceIndex = new();
        private readonly List<int> _dirtyOffsets = new(256);

        public int TransformBuffer { get; init; }
        public int StateBuffer { get; init; }
        public int StateVersion { get; set; }
        public int MaterialResolverVersion { get; set; }
        public int LodPolicyVersion { get; set; }
        public int FadeVersion { get; set; }
        public int TransformBufferCapacityBytes { get; set; }
        public int StateBufferCapacityBytes { get; set; }
        public int InstanceCount { get; private set; }
        public int TransformFloatCount => InstanceCount * HighScaleTransformFloatStride;
        public int StateFloatCount => InstanceCount * HighScaleStateFloatStride;
        public float[] TransformData => _transformData;
        public float[] StateData => _stateData;
        public int DirtyOffsetCount => _dirtyOffsets.Count;

        public bool Matches(IReadOnlyList<int> indices)
        {
            if (InstanceCount != indices.Count)
            {
                return false;
            }

            for (var i = 0; i < indices.Count; i++)
            {
                if (_instanceIndices[i] != indices[i])
                {
                    return false;
                }
            }

            return true;
        }

        public void ResetCpuData()
        {
            InstanceCount = 0;
            _offsetByInstanceIndex.Clear();
            _dirtyOffsets.Clear();
        }

        public void Add(int instanceIndex, Matrix4x4 model, int transformVersion, int materialVariantId, bool visible, float fadeAlpha)
        {
            EnsureCapacity(InstanceCount + 1);
            _instanceIndices[InstanceCount] = instanceIndex;
            _transformVersions[InstanceCount] = transformVersion;
            _offsetByInstanceIndex[instanceIndex] = InstanceCount;

            var transformOffset = InstanceCount * HighScaleTransformFloatStride;
            WriteMatrix(_transformData, transformOffset, model);
            WriteState(InstanceCount, materialVariantId, visible, fadeAlpha);
            InstanceCount++;
        }

        public bool HasStaleTransforms(HighScaleInstanceLayer3D layer)
        {
            for (var offset = 0; offset < InstanceCount; offset++)
            {
                var instanceIndex = _instanceIndices[offset];
                if ((uint)instanceIndex >= (uint)layer.Instances.Count) continue;
                if (_transformVersions[offset] != layer.Instances[instanceIndex].TransformVersion)
                {
                    return true;
                }
            }

            return false;
        }

        public int UpdateTransforms(HighScaleInstanceLayer3D layer)
        {
            var changed = 0;
            for (var offset = 0; offset < InstanceCount; offset++)
            {
                var instanceIndex = _instanceIndices[offset];
                if ((uint)instanceIndex >= (uint)layer.Instances.Count) continue;
                var record = layer.Instances[instanceIndex];
                if (_transformVersions[offset] == record.TransformVersion) continue;
                WriteMatrix(_transformData, offset * HighScaleTransformFloatStride, record.Transform);
                _transformVersions[offset] = record.TransformVersion;
                changed++;
            }

            return changed;
        }

        public int GetInstanceIndexAt(int offset) => _instanceIndices[offset];

        public bool TryGetOffset(int instanceIndex, out int offset) => _offsetByInstanceIndex.TryGetValue(instanceIndex, out offset);

        public void WriteState(int offset, int materialVariantId, bool visible, float fadeAlpha)
        {
            var stateOffset = offset * HighScaleStateFloatStride;
            _stateData[stateOffset] = System.Math.Clamp(materialVariantId, 0, MaxHighScaleMaterialVariants - 1);
            _stateData[stateOffset + 1] = visible ? 1f : 0f;
            _stateData[stateOffset + 2] = System.Math.Clamp(fadeAlpha, 0f, 1f);
            _stateData[stateOffset + 3] = 0f;
        }

        public void ResetDirtyOffsets() => _dirtyOffsets.Clear();

        public void AddDirtyOffset(int offset) => _dirtyOffsets.Add(offset);

        public void SortDirtyOffsets() => _dirtyOffsets.Sort();

        public int GetDirtyOffsetAt(int index) => _dirtyOffsets[index];

        public void Dispose(GlInterface gl)
        {
            if (TransformBuffer != 0) gl.DeleteBuffer(TransformBuffer);
            if (StateBuffer != 0) gl.DeleteBuffer(StateBuffer);
        }

        private void EnsureCapacity(int requiredInstances)
        {
            if (_instanceIndices.Length >= requiredInstances) return;
            var next = _instanceIndices.Length;
            while (next < requiredInstances) next *= 2;
            Array.Resize(ref _instanceIndices, next);
            Array.Resize(ref _transformVersions, next);
            Array.Resize(ref _transformData, next * HighScaleTransformFloatStride);
            Array.Resize(ref _stateData, next * HighScaleStateFloatStride);
        }
    }

    private sealed class DirectionalShadowMapResource
    {
        public int Texture { get; init; }
        public int Framebuffer { get; init; }
        public int Resolution { get; init; }

        public void Dispose(GlInterface gl, GlDeleteFramebuffersDelegate? deleteFramebuffers)
        {
            if (Texture != 0) gl.DeleteTexture(Texture);
            if (Framebuffer != 0 && deleteFramebuffers is not null)
            {
                deleteFramebuffers(1, new[] { Framebuffer });
            }
        }
    }

    private sealed class ControlTextureResource
    {
        public int TextureId { get; init; }
        public int SnapshotVersion { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public void Dispose(GlInterface gl) { if (TextureId != 0) gl.DeleteTexture(TextureId); }
    }

    private sealed class MaterialTextureResource
    {
        public int TextureId { get; init; }
        public int Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public void Dispose(GlInterface gl) { if (TextureId != 0) gl.DeleteTexture(TextureId); }
    }

    private const string MeshVertexSource = @"attribute vec3 aPosition;
attribute vec3 aNormal;
attribute vec2 aTexCoord0;
attribute vec4 aTangent;
attribute vec4 aInstanceModel0;
attribute vec4 aInstanceModel1;
attribute vec4 aInstanceModel2;
attribute vec4 aInstanceModel3;
attribute vec4 aInstanceColor;
attribute vec4 aInstanceState;
attribute float aMaterialSlot;
uniform mat4 uModel;
uniform mat4 uPartLocal;
uniform mat4 uViewProj;
uniform mat4 uLightViewProj;
uniform vec4 uColor;
uniform float uUseInstancing;
uniform float uUsePartLocal;
uniform float uUseHighScaleState;
uniform float uUsePaletteTexture;
uniform float uClientAnimationEnabled;
uniform float uClientAnimationTime;
uniform float uClientAnimationAmplitude;
uniform vec4 uVariantColors[32];
varying vec3 vWorldPos;
varying vec4 vLightSpace;
varying vec3 vNormal;
varying vec3 vTangent;
varying vec2 vTexCoord0;
varying vec4 vColor;
varying float vVariantIndex;
varying float vMaterialSlot;
varying float vUsePaletteTexture;
void main()
{
    mat4 instanceModel = mat4(aInstanceModel0, aInstanceModel1, aInstanceModel2, aInstanceModel3);
    mat4 model = uUseInstancing > 0.5 ? instanceModel : uModel;
    if (uUsePartLocal > 0.5) model = uPartLocal * model;
    vec4 world = model * vec4(aPosition, 1.0);
    if (uClientAnimationEnabled > 0.5)
    {
        float phase = world.x * 0.033 + world.z * 0.047;
        world.x += sin(uClientAnimationTime + phase) * uClientAnimationAmplitude;
        world.z += cos(uClientAnimationTime * 0.7 + phase * 1.7) * uClientAnimationAmplitude;
    }
    vWorldPos = world.xyz;
    vLightSpace = uLightViewProj * world;
    mat3 normalMatrix = mat3(model);
    vNormal = normalize(normalMatrix * aNormal);
    vTangent = normalize(normalMatrix * aTangent.xyz);
    vTexCoord0 = aTexCoord0;
    vVariantIndex = 0.0;
    vMaterialSlot = aMaterialSlot;
    vUsePaletteTexture = 0.0;
    if (uUseHighScaleState > 0.5)
    {
        float variantIndex = clamp(aInstanceState.x, 0.0, 31.0);
        vVariantIndex = floor(variantIndex + 0.5);
        vUsePaletteTexture = uUsePaletteTexture;
        if (uUsePaletteTexture > 0.5)
        {
            vColor = vec4(1.0, 1.0, 1.0, aInstanceState.y * aInstanceState.z);
        }
        else
        {
            int variantUniformIndex = int(vVariantIndex);
            vec4 stateColor = uVariantColors[variantUniformIndex];
            stateColor.a *= aInstanceState.y * aInstanceState.z;
            vColor = stateColor;
        }
    }
    else
    {
        vColor = uUseInstancing > 0.5 ? aInstanceColor : uColor;
    }
    gl_Position = uViewProj * world;
}";

    private const string MeshFragmentSource = @"#ifdef GL_ES
precision mediump float;
#endif
uniform float uLightingEnabled;
uniform vec3 uAmbientLight;
uniform vec3 uDirectionalLightDirection;
uniform vec3 uDirectionalLightColor;
uniform vec4 uPointLightPosition;
uniform vec4 uPointLightColor;
uniform vec4 uSpotLightPosition;
uniform vec4 uSpotLightDirection;
uniform vec4 uSpotLightColor;
uniform vec4 uSpotLightCone;
uniform vec3 uCameraPosition;
uniform vec3 uSpecularColor;
uniform vec4 uSpecularParams;
uniform vec4 uMaterialStrengths;
uniform float uNormalMapStrength;
uniform vec4 uPostProcessParams;
uniform vec4 uSsaoParams;
uniform float uShadowEnabled;
uniform sampler2D uShadowMap;
uniform mat4 uLightViewProj;
uniform vec4 uShadowParams;
uniform sampler2D uPaletteTexture;
uniform sampler2D uBaseColorTexture;
uniform float uBaseColorTextureEnabled;
uniform sampler2D uNormalTexture;
uniform float uNormalTextureEnabled;
uniform sampler2D uMetallicRoughnessTexture;
uniform float uMetallicRoughnessTextureEnabled;
uniform sampler2D uEmissiveTexture;
uniform float uEmissiveTextureEnabled;
uniform vec4 uAlphaParams;
uniform vec4 uEmissiveColor;
uniform float uPaletteWidth;
uniform float uPaletteHeight;
varying vec3 vWorldPos;
varying vec4 vLightSpace;
varying vec3 vNormal;
varying vec3 vTangent;
varying vec2 vTexCoord0;
varying vec4 vColor;
varying float vVariantIndex;
varying float vMaterialSlot;
varying float vUsePaletteTexture;
float ResolveDirectionalShadow()
{
    if (uShadowEnabled < 0.5) return 1.0;
    vec3 proj = vLightSpace.xyz / max(abs(vLightSpace.w), 0.0001);
    vec3 uvw = proj * 0.5 + 0.5;
    if (uvw.x < 0.0 || uvw.x > 1.0 || uvw.y < 0.0 || uvw.y > 1.0 || uvw.z < 0.0 || uvw.z > 1.0) return 1.0;
    float closest = texture2D(uShadowMap, uvw.xy).r;
    float current = uvw.z - uShadowParams.x;
    float shadow = current > closest ? uShadowParams.y : 0.0;
    return 1.0 - shadow;
}
void main()
{
    vec4 materialColor = vColor;
    if (vUsePaletteTexture > 0.5)
    {
        float slot = clamp(floor(vMaterialSlot + 0.5), 0.0, max(uPaletteWidth - 1.0, 0.0));
        float variant = clamp(floor(vVariantIndex + 0.5), 0.0, max(uPaletteHeight - 1.0, 0.0));
        vec2 uv = vec2((slot + 0.5) / max(uPaletteWidth, 1.0), (variant + 0.5) / max(uPaletteHeight, 1.0));
        vec4 paletteColor = texture2D(uPaletteTexture, uv);
        materialColor = vec4(paletteColor.rgb, paletteColor.a * vColor.a);
    }
    if (uBaseColorTextureEnabled > 0.5)
    {
        vec4 texel = texture2D(uBaseColorTexture, vTexCoord0);
        materialColor = vec4(materialColor.rgb * texel.rgb, materialColor.a * texel.a);
    }
    if (materialColor.a <= 0.001) discard;
    if (uAlphaParams.y < 0.5 && materialColor.a < uAlphaParams.x) discard;
    if (uAlphaParams.y > 0.5 && materialColor.a < 0.999)
    {
        float threshold = mod(floor(gl_FragCoord.x) + floor(gl_FragCoord.y), 4.0) * 0.25;
        if (threshold > materialColor.a) discard;
    }
    vec3 outColor = materialColor.rgb;
    vec3 surfaceNormal = normalize(vNormal);
    if (uLightingEnabled > 0.5)
    {
        vec3 n = surfaceNormal;
        if (uNormalTextureEnabled > 0.5 && uNormalMapStrength > 0.0001)
        {
            vec3 t = normalize(vTangent - n * dot(n, vTangent));
            vec3 b = normalize(cross(n, t));
            vec3 tangentNormal = texture2D(uNormalTexture, vTexCoord0).xyz * 2.0 - 1.0;
            tangentNormal.xy *= uNormalMapStrength;
            n = normalize(mat3(t, b, n) * normalize(tangentNormal));
            surfaceNormal = n;
        }
        vec3 viewDir = normalize(uCameraPosition - vWorldPos);
        vec3 light = uAmbientLight * uMaterialStrengths.x;
        vec3 dir = normalize(-uDirectionalLightDirection);
        float ndl = max(dot(n, dir), 0.0);
        light += ndl * uDirectionalLightColor * uMaterialStrengths.y;
        vec3 specular = vec3(0.0);
        if (uLightingEnabled > 1.5 && ndl > 0.0)
        {
            vec3 reflectDir = reflect(-dir, n);
            vec3 halfDir = normalize(dir + viewDir);
            float spec = uLightingEnabled > 2.5 ? pow(max(dot(n, halfDir), 0.0), uSpecularParams.y) : pow(max(dot(viewDir, reflectDir), 0.0), uSpecularParams.y);
            specular += spec * uDirectionalLightColor;
        }
        if (uPointLightColor.a > 0.5)
        {
            vec3 toPoint = uPointLightPosition.xyz - vWorldPos;
            float dist = length(toPoint);
            vec3 pointDir = normalize(toPoint);
            float att = clamp(1.0 - dist / max(uPointLightPosition.w, 0.01), 0.0, 1.0);
            float diff = max(dot(n, pointDir), 0.0) * att * att;
            light += diff * uPointLightColor.rgb * uMaterialStrengths.y;
            if (uLightingEnabled > 1.5 && diff > 0.0)
            {
                vec3 reflectDir = reflect(-pointDir, n);
                vec3 halfDir = normalize(pointDir + viewDir);
                float spec = uLightingEnabled > 2.5 ? pow(max(dot(n, halfDir), 0.0), uSpecularParams.y) : pow(max(dot(viewDir, reflectDir), 0.0), uSpecularParams.y);
                specular += spec * uPointLightColor.rgb * att * att;
            }
        }
        if (uSpotLightColor.a > 0.5)
        {
            vec3 toSpot = uSpotLightPosition.xyz - vWorldPos;
            float dist = length(toSpot);
            vec3 spotDir = normalize(toSpot);
            float angle = dot(spotDir, normalize(-uSpotLightDirection.xyz));
            float cone = clamp((angle - uSpotLightCone.y) / max(uSpotLightCone.x - uSpotLightCone.y, 0.0001), 0.0, 1.0);
            float att = clamp(1.0 - dist / max(uSpotLightPosition.w, 0.01), 0.0, 1.0) * cone;
            float diff = max(dot(n, spotDir), 0.0) * att * att;
            light += diff * uSpotLightColor.rgb * uMaterialStrengths.y;
            if (uLightingEnabled > 1.5 && diff > 0.0)
            {
                vec3 reflectDir = reflect(-spotDir, n);
                vec3 halfDir = normalize(spotDir + viewDir);
                float spec = uLightingEnabled > 2.5 ? pow(max(dot(n, halfDir), 0.0), uSpecularParams.y) : pow(max(dot(viewDir, reflectDir), 0.0), uSpecularParams.y);
                specular += spec * uSpotLightColor.rgb * att * att;
            }
        }
        float shadowFactor = ResolveDirectionalShadow();
        vec3 ambientBase = uAmbientLight * uMaterialStrengths.x;
        vec3 shadowedLight = ambientBase + (clamp(light, 0.0, 3.0) - ambientBase) * shadowFactor;
        float metallic = uSpecularParams.z;
        float roughness = uSpecularParams.w;
        if (uMetallicRoughnessTextureEnabled > 0.5)
        {
            vec4 mr = texture2D(uMetallicRoughnessTexture, vTexCoord0);
            roughness *= mr.g;
            metallic *= mr.b;
        }
        vec3 emissive = uEmissiveColor.rgb * uEmissiveColor.a;
        if (uEmissiveTextureEnabled > 0.5) emissive += texture2D(uEmissiveTexture, vTexCoord0).rgb;
        float specScale = mix(1.0, 0.35, clamp(roughness, 0.0, 1.0));
        outColor = outColor * shadowedLight + uSpecularColor * specular * uSpecularParams.x * specScale * shadowFactor + emissive;
    }
    if (uSsaoParams.x > 0.5)
    {
        float horizon = 1.0 - clamp(surfaceNormal.y * 0.5 + 0.5, 0.0, 1.0);
        float depthHint = clamp(1.0 - gl_FragCoord.z, 0.0, 1.0);
        float ao = clamp(horizon * uSsaoParams.y * 0.35 + depthHint * uSsaoParams.z * 0.025, 0.0, 0.85);
        outColor *= (1.0 - ao);
    }
    if (uPostProcessParams.z > 0.5)
    {
        float exposure = max(uPostProcessParams.x, 0.001);
        float gamma = max(uPostProcessParams.y, 0.1);
        if (uPostProcessParams.w < 1.5)
        {
            outColor = outColor / (vec3(1.0) + outColor);
        }
        else
        {
            outColor = vec3(1.0) - exp(-outColor * exposure);
        }
        outColor = pow(max(outColor, vec3(0.0)), vec3(1.0 / gamma));
    }
    gl_FragColor = vec4(outColor, materialColor.a);
}";

    private const string ShadowVertexSource = @"attribute vec3 aPosition;
attribute vec4 aInstanceModel0;
attribute vec4 aInstanceModel1;
attribute vec4 aInstanceModel2;
attribute vec4 aInstanceModel3;
uniform mat4 uModel;
uniform mat4 uLightViewProj;
uniform float uUseInstancing;
void main()
{
    mat4 instanceModel = mat4(aInstanceModel0, aInstanceModel1, aInstanceModel2, aInstanceModel3);
    mat4 model = uUseInstancing > 0.5 ? instanceModel : uModel;
    gl_Position = uLightViewProj * model * vec4(aPosition, 1.0);
}";

    private const string ShadowFragmentSource = @"#ifdef GL_ES
precision mediump float;
#endif
void main()
{
    gl_FragColor = vec4(1.0);
}";

    private const string SkyboxVertexSource = @"attribute vec2 aPosition;
varying vec2 vUv;
void main()
{
    vUv = aPosition * 0.5 + 0.5;
    gl_Position = vec4(aPosition, 1.0, 1.0);
}";

    private const string SkyboxFragmentSource = @"#ifdef GL_ES
precision mediump float;
#endif
uniform vec3 uTopColor;
uniform vec3 uHorizonColor;
uniform vec3 uBottomColor;
uniform float uIntensity;
uniform int uSkyboxMode;
uniform vec3 uCameraRight;
uniform vec3 uCameraUp;
uniform vec3 uCameraForward;
uniform sampler2D uSkyboxTexture;
uniform float uSkyboxTextureEnabled;
uniform sampler2D uSkyboxPX;
uniform sampler2D uSkyboxNX;
uniform sampler2D uSkyboxPY;
uniform sampler2D uSkyboxNY;
uniform sampler2D uSkyboxPZ;
uniform sampler2D uSkyboxNZ;
uniform float uSkyboxCubemapEnabled;
varying vec2 vUv;
float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}
void main()
{
    if (uSkyboxMode == 1)
    {
        gl_FragColor = vec4(uHorizonColor, 1.0);
        return;
    }
    vec2 screen = vUv * 2.0 - 1.0;
    vec3 dir = normalize(uCameraForward + uCameraRight * screen.x * 1.35 + uCameraUp * screen.y * 0.78);
    const float PI = 3.14159265359;
    vec2 uv = vec2(atan(dir.x, dir.z) / (2.0 * PI) + 0.5, asin(clamp(dir.y, -1.0, 1.0)) / PI + 0.5);
    if (uSkyboxMode == 5 && uSkyboxTextureEnabled > 0.5)
    {
        gl_FragColor = vec4(texture2D(uSkyboxTexture, uv).rgb * max(uIntensity, 0.0), 1.0);
        return;
    }
    if (uSkyboxMode == 3 && uSkyboxCubemapEnabled > 0.5)
    {
        vec3 ad = abs(dir);
        vec2 cuv;
        if (ad.x >= ad.y && ad.x >= ad.z)
        {
            if (dir.x > 0.0) { cuv = vec2(-dir.z, dir.y) / ad.x * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxPX, cuv).rgb * max(uIntensity, 0.0), 1.0); return; }
            cuv = vec2(dir.z, dir.y) / ad.x * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxNX, cuv).rgb * max(uIntensity, 0.0), 1.0); return;
        }
        if (ad.y >= ad.x && ad.y >= ad.z)
        {
            if (dir.y > 0.0) { cuv = vec2(dir.x, -dir.z) / ad.y * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxPY, cuv).rgb * max(uIntensity, 0.0), 1.0); return; }
            cuv = vec2(dir.x, dir.z) / ad.y * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxNY, cuv).rgb * max(uIntensity, 0.0), 1.0); return;
        }
        if (dir.z > 0.0) { cuv = vec2(dir.x, dir.y) / ad.z * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxPZ, cuv).rgb * max(uIntensity, 0.0), 1.0); return; }
        cuv = vec2(-dir.x, dir.y) / ad.z * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxNZ, cuv).rgb * max(uIntensity, 0.0), 1.0); return;
    }
    if (uSkyboxMode == 4)
    {
        vec3 baseColor = mix(uBottomColor, uTopColor, smoothstep(0.0, 1.0, uv.y));
        vec2 cell = floor(uv * vec2(280.0, 140.0));
        vec2 local = fract(uv * vec2(280.0, 140.0)) - 0.5;
        float rnd = hash(cell);
        float starMask = step(0.988, rnd);
        float core = smoothstep(0.030, 0.0, length(local));
        float twinkle = 0.55 + 0.45 * hash(cell + 19.37);
        vec3 star = vec3(1.0, 0.94, 0.82) * starMask * core * twinkle * 2.35;
        gl_FragColor = vec4((baseColor + star) * max(uIntensity, 0.0), 1.0);
        return;
    }
    float t = clamp(vUv.y, 0.0, 1.0);
    vec3 lower = mix(uBottomColor, uHorizonColor, smoothstep(0.0, 0.55, t));
    vec3 upper = mix(uHorizonColor, uTopColor, smoothstep(0.45, 1.0, t));
    vec3 color = t < 0.5 ? lower : upper;
    gl_FragColor = vec4(color * max(uIntensity, 0.0), 1.0);
}";

    private const string TexturedVertexSource = @"attribute vec3 aPosition;
attribute vec2 aTexCoord;
uniform mat4 uViewProj;
varying vec2 vTexCoord;
void main()
{
    vTexCoord = aTexCoord;
    gl_Position = uViewProj * vec4(aPosition, 1.0);
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
