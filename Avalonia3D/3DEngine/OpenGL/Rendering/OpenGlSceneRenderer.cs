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
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Instancing;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Rhi;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Resources;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.OpenGL.Rendering;

internal sealed partial class OpenGlSceneRenderer : IRhiCommandExecutor3D
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
    private const int GlShort = 0x1402;
    private const int GlHalfFloat = 0x140B;
    private const int GlUnsignedInt = 0x1405;
    private const int GlUnsignedShort = 0x1403;
    private const int GlArrayBuffer = 0x8892;
    private const int GlElementArrayBuffer = 0x8893;
    private const int GlStaticDraw = 0x88E4;
    private const int GlDynamicDraw = 0x88E8;
    private const int GlDepthTest = 0x0B71;
    private const int GlLess = 0x0201;
    private const int GlCullFace = 0x0B44;
    private const int GlFront = 0x0404;
    private const int GlBack = 0x0405;
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
    private const int GlVendor = 0x1F00;
    private const int GlRenderer = 0x1F01;
    private const int GlVersion = 0x1F02;
    private const int GlExtensions = 0x1F03;
    private const int GlNoError = 0;
    private const int GlInvalidEnum = 0x0500;
    private const int GlInvalidValue = 0x0501;
    private const int GlInvalidOperation = 0x0502;
    private const int GlOutOfMemory = 0x0505;
    private const int GlInvalidFramebufferOperation = 0x0506;
    private const int GlContextLost = 0x0507;
    private const int GlTimeElapsed = 0x88BF;
    private const int GlQueryResult = 0x8866;
    private const int GlQueryResultAvailable = 0x8867;
    private const int InstanceFloatStride = 20;
    private const int InstanceByteStride = InstanceFloatStride * sizeof(float);
    private const int ParticleBillboardFloatStride = 8; // center.xyz + size + color.rgba
    private const int ParticleBillboardByteStride = ParticleBillboardFloatStride * sizeof(float);
    private const int MaxGpuSkinTextureBones = 4096;
    private const int HighScaleTransformFloatStride = 16;
    private const int HighScaleTransformByteStride = HighScaleTransformFloatStride * sizeof(float);
    private const int HighScaleStateFloatStride = 4;
    private const int HighScaleStateByteStride = HighScaleStateFloatStride * sizeof(float);
    private const int MaxHighScaleMaterialVariants = 32;
    private const int RetainedOrdinaryCullMinInstances = 32;
    private const float RetainedOrdinaryCullMinCulledRatio = 0.15f;
    private const float SparseInstanceUploadMaxDirtyRatio = 0.25f;
    private const int MaxSparseInstanceUploadRanges = 64;
    private const int MaxPartialHighScaleUploadRanges = 64;

    // Capability setup is validated on a short startup sample. Hundreds of synchronous
    // glGetError boundaries made the first complex Desktop frames visibly stall without
    // improving coverage: every ordinary/particle/high-scale path uses the same entry points.
    private const int InstancedDrawValidationBudgetInitial = 8;
    private static readonly HighScaleChunkKey3D AggregateChunkKey = new(int.MinValue, int.MinValue, int.MinValue);

    private readonly Dictionary<string, MeshGpuResource> _meshResources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControlTextureResource> _controlTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MaterialTextureResource> _materialTextures = new(StringComparer.Ordinal);
    private readonly GpuDeferredReleaseQueue3D<MaterialTextureResource> _deferredMaterialTextureReleases = new();
    private readonly GpuDeferredReleaseQueue3D<ControlTextureResource> _deferredControlTextureReleases = new();
    private readonly string _rhiResourceOwnerId = "opengl-renderer:" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, HighScalePaletteTextureResource> _highScalePaletteTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<MeshBatchKey, MeshBatchData> _meshBatches = new();
    private readonly Dictionary<MeshBatchKey, ParticleBatchData> _particleBatches = new();
    private readonly Dictionary<HighScaleBatchKey, HighScaleGpuBatchData> _highScaleGpuBatches = new();
    private readonly float[] _matrixUploadBuffer = new float[16];
    private readonly float[] _controlVertexData = new float[20];
    private readonly List<ControlPlaneRenderItem3D> _controlPlaneScratch = new(16);
    private readonly Vector3[] _controlCornerScratch = new Vector3[4];
    private readonly SceneRenderFrameScratch3D _renderFrameScratch = new();
    private readonly SceneRenderPlanScratch3D _renderPlanScratch = new();
    private readonly SceneRenderPlanScratch3D _fullRenderPlanScratch = new();
    private readonly HashSet<string> _liveMeshSweepScratch = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveControlPlaneSweepScratch = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveMaterialTextureSweepScratch = new(StringComparer.Ordinal);
    private readonly List<string> _meshSweepScratch = new();
    private readonly List<string> _textureSweepScratch = new();
    private readonly List<MeshBatchKey> _batchRemovalScratch = new();
    private long _lastSweptRegistryVersion = -1;
    private long _lastSweptBatchContentVersion = -1;
    private long _lastBuiltOrdinarySceneChangeVersion = -1;
    private long _lastBuiltOrdinaryTransformVersion = -1;
    private long _lastBuiltOrdinaryParticleVersion = -1;
    private long _lastBuiltOrdinaryRegistryVersion = -1;
    private int _lastBuiltOrdinaryInterpolationVersion = -1;
    private long _lastBuiltOrdinaryCameraVersion = -1;
    private bool _hasAdaptiveTransparentOrdinaryBatches;
    private bool _hasCameraDependentParticleBatches;
    private readonly OrdinaryBatchStatsCache _ordinaryBatchStatsCache = new();
    private readonly HighScaleLodSelectionPlan3D _highScaleLodPlanScratch = new();
    private readonly List<CachedOpenGlDrawCommand> _cachedDrawCommands = new(384);
    private readonly List<CachedOpenGlDrawCommand> _frameDrawCommandScratch = new(384);
    private readonly Dictionary<string, RetainedOrdinarySlotRef> _ordinarySlotByObjectId = new(StringComparer.Ordinal);
    private readonly List<Object3D> _ordinaryTransformDirtyScratch = new(256);
    private readonly List<Object3D> _ordinaryInterpolationDirtyScratch = new(256);
    private readonly HashSet<Object3D> _ordinaryDirtySeen = new(ObjectReferenceComparer3D<Object3D>.Instance);
    private long _lastRetainedPlanRebuildWarningTicks;
    private int _suppressedRetainedPlanRebuildWarnings;
    private long _retainedOrdinaryPlanRebuildCount;
    private long _retainedOrdinaryCursorRecoveryCount;
    private long _retainedSkinningBatchUpdateCount;
    private string _lastRetainedOrdinaryFailureReason = string.Empty;
    private int[] _particleSortOrderScratch = Array.Empty<int>();
    private float[] _particleSortKeyScratch = Array.Empty<float>();
    private int _highScaleTransformBatchUploadsThisFrame;
    private readonly int[] _gpuTimerQueries = new int[4];
    private readonly bool[] _gpuTimerPending = new bool[4];
    private int _gpuTimerActiveSlot = -1;
    private int _gpuTimerNextSlot;
    private double _lastGpuFrameMilliseconds = double.NaN;
    private RhiDevice3D? _rhiDevice;
    private EngineResourceConfiguration3D? _resourceConfiguration;

    private int _meshProgram;
    private int _texturedProgram;
    private int _skyboxProgram;
    private int _meshPositionLocation;
    private int _meshNormalLocation;
    private int _meshTexCoordLocation;
    private int _meshTangentLocation;
    private int _meshVertexColorLocation;
    private int _meshBoneIndicesLocation;
    private int _meshBoneWeightsLocation;
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
    private int _meshDistanceFadeParamsLocation;
    private int _meshColorLocation;
    private int _meshUseInstancingLocation;
    private int _meshLightingEnabledLocation;
    private int _meshModelLocation;
    private int _meshViewProjLocation;
    private int _meshPartLocalLocation;
    private int _meshUsePartLocalLocation;
    private int _meshUseHighScaleStateLocation;
    private int _meshUsePaletteTextureLocation;
    private int _meshUseDirectStateColorLocation;
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
    private int _meshParticleBillboardLocation;
    private int _meshParticleCameraRightLocation;
    private int _meshParticleCameraUpLocation;
    private int _meshParticleCameraForwardLocation;
    private int _meshClientAnimationEnabledLocation;
    private int _meshClientAnimationTimeLocation;
    private int _meshClientAnimationAmplitudeLocation;
    private int _meshSkinningEnabledLocation;
    private int _meshBoneTextureLocation;
    private int _meshBoneTextureHeightLocation;
    private readonly int[] _meshVariantColorLocations = new int[MaxHighScaleMaterialVariants];
    private int _texturePositionLocation;
    private int _textureUvLocation;
    private int _textureSamplerLocation;
    private int _textureViewProjLocation;
    private int _skyboxPositionLocation;
    private int _skyboxTopColorLocation;
    private int _skyboxHorizonColorLocation;
    private int _skyboxBottomColorLocation;
    private int _skyboxIntensityLocation;
    private int _skyboxModeLocation;
    private int _skyboxCameraRightLocation;
    private int _skyboxCameraUpLocation;
    private int _skyboxCameraForwardLocation;
    private int _skyboxProjectionScaleXLocation;
    private int _skyboxProjectionScaleYLocation;
    private int _skyboxTextureLocation;
    private int _skyboxTextureEnabledLocation;
    private int _skyboxPXLocation;
    private int _skyboxNXLocation;
    private int _skyboxPYLocation;
    private int _skyboxNYLocation;
    private int _skyboxPZLocation;
    private int _skyboxNZLocation;
    private int _skyboxCubemapEnabledLocation;
    private int _skyboxVertexArray;
    private int _skyboxVertexBuffer;
    private int _skyboxIndexBuffer;
    private int _controlVertexArray;
    private int _controlVertexBuffer;
    private int _controlIndexBuffer;
    private MeshGpuResource? _lastMeshAttributeResource;
    private byte[] _paletteUploadBuffer = Array.Empty<byte>();
    private byte[] _controlBgraUploadBuffer = Array.Empty<byte>();
    private byte[] _controlRgbaUploadBuffer = Array.Empty<byte>();
    private GlInterface? _lastGl;
    private bool _initialized;
    private bool _supportsInstancing;
    private string _instancingApi = "unresolved";
    private bool _instancedDrawPathBroken;
    private int _instancedDrawValidationBudget = InstancedDrawValidationBudgetInitial;
    private int _instancedDrawFailureCount;
    private string _lastInstancedDrawFailureReason = "none";
    private bool _supportsBoneTextureSkinning;
    private int _gpuSkinTextureBoneLimit = 0;
    private GlBlendFuncDelegate? _blendFunc;
    private GlDepthMaskDelegate? _depthMask;
    private GlDepthFuncDelegate? _depthFunc;
    private GlDisableDelegate? _disable;
    private GlCullFaceDelegate? _cullFace;
    private GlUniform1iDelegate? _uniform1i;
    private GlUniform1fDelegate? _uniform1f;
    private GlUniform4fDelegate? _uniform4f;
    private GlUniform3fDelegate? _uniform3f;
    private GlUniformMatrix4fvDelegate? _uniformMatrix4fv;
    private GlVertexAttribDivisorDelegate? _vertexAttribDivisor;
    private GlVertexAttrib4fDelegate? _vertexAttrib4f;
    private GlDrawElementsInstancedDelegate? _drawElementsInstanced;
    private GlBufferSubDataDelegate? _bufferSubData;
    private GlGenFramebuffersDelegate? _genFramebuffers;
    private GlDeleteFramebuffersDelegate? _deleteFramebuffers;
    private GlFramebufferTexture2DDelegate? _framebufferTexture2D;
    private GlCheckFramebufferStatusDelegate? _checkFramebufferStatus;
    private GlDrawBufferDelegate? _drawBuffer;
    private GlReadBufferDelegate? _readBuffer;
    private GlGenVertexArraysDelegate? _genVertexArrays;
    private GlBindVertexArrayDelegate? _bindVertexArray;
    private GlDeleteVertexArraysDelegate? _deleteVertexArrays;
    private GlDisableVertexAttribArrayDelegate? _disableVertexAttribArray;
    private GlGetIntegervDelegate? _getIntegerv;
    private GlGetStringDelegate? _getString;
    private GlGetErrorDelegate? _getError;
    private GlGenQueriesDelegate? _genQueries;
    private GlDeleteQueriesDelegate? _deleteQueries;
    private GlBeginQueryDelegate? _beginQuery;
    private GlEndQueryDelegate? _endQuery;
    private GlGetQueryObjectivDelegate? _getQueryObjectiv;
    private GlGetQueryObjectui64vDelegate? _getQueryObjectui64v;
    private bool _supportsVertexArrays;
    private string _vertexArrayApi = "unresolved";
    private int _boundVertexArray;
    private long _graphicsCallbackSerial;
    private CullMode _appliedCullMode = (CullMode)(-1);

    public RhiDevice3D? Device => _rhiDevice;

    public void ConfigureResources(EngineResourceConfiguration3D configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (_initialized && !Equals(_resourceConfiguration, configuration))
            throw new InvalidOperationException("An initialized OpenGL renderer cannot switch to a different engine resource policy without recreating its presenter.");
        _resourceConfiguration = configuration;
    }

    public void Initialize(GlInterface gl)
    {
        if (_initialized)
        {
            if (ReferenceEquals(_lastGl, gl)) return;
            if (_lastGl is not null)
            {
                try
                {
                    Deinitialize(_lastGl);
                }
                catch
                {
                    // The old context may already be lost. Continue with a clean managed state.
                    ResetManagedGlState();
                }
            }
            else
            {
                ResetManagedGlState();
            }
        }

        _lastGl = gl;

        // Resolve context identity/error reporting before extension entry points.
        // EGL/ANGLE may return non-null addresses for core names that are illegal
        // in the active ES2 context, so pointer presence is not a capability test.
        _getIntegerv = LoadDelegate<GlGetIntegervDelegate>(gl, "glGetIntegerv");
        _getString = LoadDelegate<GlGetStringDelegate>(gl, "glGetString");
        _getError = LoadDelegate<GlGetErrorDelegate>(gl, "glGetError");
        var glVersion = GetString(GlVersion);
        var contextVersion = ParseGlContextVersion(glVersion);
        var extensions = contextVersion.IsKnown && contextVersion.Major < 3
            ? GetString(GlExtensions)
            : string.Empty;

        _blendFunc = LoadDelegate<GlBlendFuncDelegate>(gl, "glBlendFunc");
        _depthMask = LoadDelegate<GlDepthMaskDelegate>(gl, "glDepthMask");
        _depthFunc = LoadDelegate<GlDepthFuncDelegate>(gl, "glDepthFunc");
        _disable = LoadDelegate<GlDisableDelegate>(gl, "glDisable");
        _cullFace = LoadDelegate<GlCullFaceDelegate>(gl, "glCullFace");
        _uniform1i = LoadDelegate<GlUniform1iDelegate>(gl, "glUniform1i");
        _uniform1f = LoadDelegate<GlUniform1fDelegate>(gl, "glUniform1f");
        _uniform4f = LoadDelegate<GlUniform4fDelegate>(gl, "glUniform4f");
        _uniform3f = LoadDelegate<GlUniform3fDelegate>(gl, "glUniform3f");
        _uniformMatrix4fv = LoadDelegate<GlUniformMatrix4fvDelegate>(gl, "glUniformMatrix4fv");
        _vertexAttrib4f = LoadDelegate<GlVertexAttrib4fDelegate>(gl, "glVertexAttrib4f");
        ResolveInstancingEntryPoints(gl, contextVersion, extensions);
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
        ResolveVertexArrayEntryPoints(gl, contextVersion, extensions);
        _disableVertexAttribArray = LoadDelegate<GlDisableVertexAttribArrayDelegate>(gl, "glDisableVertexAttribArray");
        _genQueries = LoadDelegate<GlGenQueriesDelegate>(gl, "glGenQueries")
                      ?? LoadDelegate<GlGenQueriesDelegate>(gl, "glGenQueriesARB");
        _deleteQueries = LoadDelegate<GlDeleteQueriesDelegate>(gl, "glDeleteQueries")
                         ?? LoadDelegate<GlDeleteQueriesDelegate>(gl, "glDeleteQueriesARB");
        _beginQuery = LoadDelegate<GlBeginQueryDelegate>(gl, "glBeginQuery")
                      ?? LoadDelegate<GlBeginQueryDelegate>(gl, "glBeginQueryARB");
        _endQuery = LoadDelegate<GlEndQueryDelegate>(gl, "glEndQuery")
                    ?? LoadDelegate<GlEndQueryDelegate>(gl, "glEndQueryARB");
        _getQueryObjectiv = LoadDelegate<GlGetQueryObjectivDelegate>(gl, "glGetQueryObjectiv")
                            ?? LoadDelegate<GlGetQueryObjectivDelegate>(gl, "glGetQueryObjectivARB");
        _getQueryObjectui64v = LoadDelegate<GlGetQueryObjectui64vDelegate>(gl, "glGetQueryObjectui64v")
                               ?? LoadDelegate<GlGetQueryObjectui64vDelegate>(gl, "glGetQueryObjectui64vEXT");
        var nativeVertexArraysAvailable = _genVertexArrays is not null && _bindVertexArray is not null && _deleteVertexArrays is not null;
        _supportsVertexArrays = nativeVertexArraysAvailable;
        _supportsInstancing = _vertexAttribDivisor is not null && _drawElementsInstanced is not null;
        _instancedDrawPathBroken = false;
        _instancedDrawValidationBudget = InstancedDrawValidationBudgetInitial;
        _instancedDrawFailureCount = 0;
        _lastInstancedDrawFailureReason = "none";
        EngineLog3D.Information(
            "OpenGL.Capabilities",
            $"Resolved context '{glVersion}': VAO API={_vertexArrayApi}, instancing API={_instancingApi}.");

        var requiredEntryPoints = new List<string>();
        if (!_supportsVertexArrays) requiredEntryPoints.Add("vertex array objects");
        if (!_supportsInstancing) requiredEntryPoints.Add("instanced drawing");
        if (_vertexAttrib4f is null) requiredEntryPoints.Add("generic vertex attribute constants");
        if (_bufferSubData is null) requiredEntryPoints.Add("buffer sub-data updates");
        if (_genFramebuffers is null || _deleteFramebuffers is null || _framebufferTexture2D is null || _checkFramebufferStatus is null) requiredEntryPoints.Add("framebuffer objects");
        if (_getIntegerv is null) requiredEntryPoints.Add("GPU limit queries");
        if (_blendFunc is null || _depthMask is null || _depthFunc is null || _disable is null || _cullFace is null) requiredEntryPoints.Add("fixed-function render state");
        if (_uniform1i is null || _uniform1f is null || _uniform3f is null || _uniform4f is null || _uniformMatrix4fv is null) requiredEntryPoints.Add("shader uniform uploads");
        if (requiredEntryPoints.Count != 0)
        {
            throw new InvalidOperationException($"OpenGL RHI initialization failed; required native GPU entry points are unavailable: {string.Join(", ", requiredEntryPoints)}. Legacy rendering paths are disabled.");
        }

        _meshProgram = CreateProgram(gl, MeshVertexSource, MeshFragmentSource,
            (0, "aPosition"), (1, "aNormal"), (2, "aInstanceModel0"), (3, "aInstanceModel1"),
            (4, "aInstanceModel2"), (5, "aInstanceModel3"), (6, "aInstanceColor"), (7, "aInstanceState"),
            (8, "aMaterialSlot"), (9, "aTexCoord0"), (10, "aTangent"), (11, "aVertexColor"),
            (12, "aBoneIndices"), (13, "aBoneWeights"));
        _meshPositionLocation = gl.GetAttribLocationString(_meshProgram, "aPosition");
        _meshNormalLocation = gl.GetAttribLocationString(_meshProgram, "aNormal");
        _meshTexCoordLocation = gl.GetAttribLocationString(_meshProgram, "aTexCoord0");
        _meshTangentLocation = gl.GetAttribLocationString(_meshProgram, "aTangent");
        _meshVertexColorLocation = gl.GetAttribLocationString(_meshProgram, "aVertexColor");
        _meshBoneIndicesLocation = gl.GetAttribLocationString(_meshProgram, "aBoneIndices");
        _meshBoneWeightsLocation = gl.GetAttribLocationString(_meshProgram, "aBoneWeights");
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
        _meshUseDirectStateColorLocation = gl.GetUniformLocationString(_meshProgram, "uUseDirectStateColor");
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
        _meshParticleBillboardLocation = gl.GetUniformLocationString(_meshProgram, "uParticleBillboard");
        _meshParticleCameraRightLocation = gl.GetUniformLocationString(_meshProgram, "uParticleCameraRight");
        _meshParticleCameraUpLocation = gl.GetUniformLocationString(_meshProgram, "uParticleCameraUp");
        _meshParticleCameraForwardLocation = gl.GetUniformLocationString(_meshProgram, "uParticleCameraForward");
        _meshClientAnimationEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uClientAnimationEnabled");
        _meshClientAnimationTimeLocation = gl.GetUniformLocationString(_meshProgram, "uClientAnimationTime");
        _meshClientAnimationAmplitudeLocation = gl.GetUniformLocationString(_meshProgram, "uClientAnimationAmplitude");
        _meshSkinningEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uSkinningEnabled");
        _meshBoneTextureLocation = gl.GetUniformLocationString(_meshProgram, "uBoneTexture");
        _meshBoneTextureHeightLocation = gl.GetUniformLocationString(_meshProgram, "uBoneTextureHeight");
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
        _meshDistanceFadeParamsLocation = gl.GetUniformLocationString(_meshProgram, "uDistanceFadeParams");
        _supportsBoneTextureSkinning = ProbeBoneTextureSkinningSupport(gl);

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
        _skyboxProjectionScaleXLocation = gl.GetUniformLocationString(_skyboxProgram, "uProjectionScaleX");
        _skyboxProjectionScaleYLocation = gl.GetUniformLocationString(_skyboxProgram, "uProjectionScaleY");
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

        _controlVertexBuffer = GenRequiredBuffer(gl, "utility:control:vertices");
        _controlIndexBuffer = GenRequiredBuffer(gl, "utility:control:indices");
        _skyboxVertexBuffer = GenRequiredBuffer(gl, "utility:skybox:vertices");
        _skyboxIndexBuffer = GenRequiredBuffer(gl, "utility:skybox:indices");
        gl.BindBuffer(GlArrayBuffer, _skyboxVertexBuffer);
        UploadFloats(gl, GlArrayBuffer, new[] { -1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f }, 8, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, _skyboxIndexBuffer);
        UploadUShorts(gl, GlElementArrayBuffer, new ushort[] { 0, 1, 2, 0, 2, 3 }, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        gl.BindBuffer(GlArrayBuffer, 0);
        gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
        UploadUShorts(gl, GlElementArrayBuffer, new ushort[] { 0, 1, 2, 0, 2, 3 }, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        ConfigureStaticUtilityVertexArrays(gl);
        InitializeGpuTimerQueries();
        var resourceConfiguration = _resourceConfiguration
            ?? throw new InvalidOperationException("OpenGL renderer resource policy was not configured from the assigned scene engine before initialization.");
        _rhiDevice ??= new RhiDevice3D(BuildRhiCapabilities(), resourceConfiguration);
        RegisterStaticRhiResources();
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
        // Avalonia owns the GL context outside this callback. Native drivers are allowed to
        // retain a different VAO between callbacks, so a renderer-side binding cache cannot
        // cross the host boundary. Every mesh draw below rebinds its exact VAO/EBO and the
        // first use of each mesh restores its full static layout for this callback.
        unchecked
        {
            _graphicsCallbackSerial++;
            if (_graphicsCallbackSerial == 0) _graphicsCallbackSerial = 1;
        }
        _boundVertexArray = -1;
        _lastMeshAttributeResource = null;
        DrainDeferredMaterialTextureReleases(gl, _rhiDevice?.FrameIndex ?? 0);
        DrainDeferredControlTextureReleases(gl, _rhiDevice?.FrameIndex ?? 0);
        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.Enable(GlDepthTest);
        _depthFunc!(GlLess);
        _depthMask!(1);
        _disable!(GlBlend);
        _appliedCullMode = (CullMode)(-1);
        ApplyCullMode(gl, CullMode.None);
        gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);

        using var frame = _renderFrameScratch.Begin(scene, width, height, BackendKind.OpenGlDesktop);
        var viewProjection = frame.ViewProjection;
        var pipeline = frame.Pipeline;

        SweepUnusedResources(gl, scene, frame.Snapshot);
        var stats = frame.CreateBaseStats();
        stats.RetainedOrdinaryPlanRebuildCount = _retainedOrdinaryPlanRebuildCount;
        stats.RetainedOrdinaryCursorRecoveryCount = _retainedOrdinaryCursorRecoveryCount;
        stats.RetainedSkinningBatchUpdateCount = _retainedSkinningBatchUpdateCount;
        stats.RetainedOrdinaryLastFailureReason = _lastRetainedOrdinaryFailureReason;
        EnsureGpuSkinningAvailable(frame.Snapshot);
        var batchPlanNeeded = RequiresOrdinaryBatchPlan(frame);
        var overlayPlanNeeded = scene.Debug.ShowWireframeOverlay || scene.Debug.ShowSilhouetteOverlay;
        var plan = SceneRenderPlanBuilder3D.Build(
            frame,
            _renderPlanScratch,
            batchPlanNeeded ? stats : null,
            includeOrdinary: batchPlanNeeded || overlayPlanNeeded,
            includeParticles: batchPlanNeeded,
            includeHighScale: true,
            frustumCullParticles: false);
        if (scene.Debug.ShowPerformanceMetrics)
        {
            ApplyAnimationStats(stats, scene, gpuSkinningActive: _supportsBoneTextureSkinning);
        }
        SceneRenderStats3D.ApplyPipelineStats(stats, scene, pipeline);
        var device = _rhiDevice ?? throw new InvalidOperationException("OpenGL RHI device is not initialized.");
        device.BeginFrame(plan.RhiSubmission);
        ExecuteRhiFrame(gl, framebuffer, width, height, scene, frame, plan, stats, pipeline, device);
        device.ApplyStats(stats);
        return stats;
    }

    public void Deinitialize(GlInterface gl)
    {
        foreach (var pair in _meshResources) DisposeMeshResource(gl, pair.Key, pair.Value);
        _deferredControlTextureReleases.DrainAll(texture => ReleaseControlTextureNow(gl, texture));
        foreach (var texture in _controlTextures.Values) ReleaseControlTextureNow(gl, texture);
        _deferredMaterialTextureReleases.DrainAll(texture => ReleaseMaterialTextureNow(gl, texture));
        foreach (var texture in _materialTextures.Values) ReleaseMaterialTextureNow(gl, texture);
        foreach (var texture in _highScalePaletteTextures.Values) texture.Dispose(gl);
        foreach (var batch in _meshBatches.Values) batch.Dispose(gl);
        foreach (var batch in _particleBatches.Values) batch.Dispose(gl);
        foreach (var batch in _highScaleGpuBatches.Values) batch.Dispose(gl);
        _meshResources.Clear();
        _controlTextures.Clear();
        _materialTextures.Clear();
        _deferredMaterialTextureReleases.ClearWithoutRelease();
        _deferredControlTextureReleases.ClearWithoutRelease();
        _highScalePaletteTextures.Clear();
        _meshBatches.Clear();
        _particleBatches.Clear();
        _highScaleGpuBatches.Clear();
        _cachedDrawCommands.Clear();
        _frameDrawCommandScratch.Clear();
        _ordinarySlotByObjectId.Clear();
        _ordinaryTransformDirtyScratch.Clear();
        _ordinaryInterpolationDirtyScratch.Clear();
        _ordinaryDirtySeen.Clear();
        DeleteVertexArray(_controlVertexArray);
        DeleteVertexArray(_skyboxVertexArray);
        if (_controlVertexBuffer != 0) gl.DeleteBuffer(_controlVertexBuffer);
        if (_controlIndexBuffer != 0) gl.DeleteBuffer(_controlIndexBuffer);
        if (_skyboxVertexBuffer != 0) gl.DeleteBuffer(_skyboxVertexBuffer);
        if (_skyboxIndexBuffer != 0) gl.DeleteBuffer(_skyboxIndexBuffer);
        if (_meshProgram != 0) gl.DeleteProgram(_meshProgram);
        if (_texturedProgram != 0) gl.DeleteProgram(_texturedProgram);
        if (_skyboxProgram != 0) gl.DeleteProgram(_skyboxProgram);
        DeleteGpuTimerQueries();
        _controlVertexArray = _skyboxVertexArray = 0;
        _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = _skyboxProgram = 0;
        _skyboxVertexBuffer = _skyboxIndexBuffer = 0;
        _lastMeshAttributeResource = null;
        _boundVertexArray = 0;
        _lastBuiltOrdinarySceneChangeVersion = -1;
        _lastBuiltOrdinaryTransformVersion = -1;
        _lastBuiltOrdinaryParticleVersion = -1;
        _lastBuiltOrdinaryRegistryVersion = -1;
        _lastBuiltOrdinaryInterpolationVersion = -1;
        _lastBuiltOrdinaryCameraVersion = -1;
        _hasAdaptiveTransparentOrdinaryBatches = false;
        _hasCameraDependentParticleBatches = false;
        _ordinaryBatchStatsCache.Reset();
        _supportsBoneTextureSkinning = false;
        _gpuSkinTextureBoneLimit = 0;
        _instancedDrawPathBroken = false;
        _instancedDrawValidationBudget = InstancedDrawValidationBudgetInitial;
        _instancedDrawFailureCount = 0;
        _lastInstancedDrawFailureReason = "none";
        _instancingApi = "unresolved";
        _vertexArrayApi = "unresolved";
        if (_rhiDevice is not null)
        {
            _rhiDevice.InvalidateContext("OpenGL native resources deinitialized");
            _rhiDevice.Dispose();
        }
        _rhiDevice = null;
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
                return;
            }
            catch
            {
                // Context may already be lost; fall through to managed-state reset.
            }
        }

        ResetManagedGlState();
    }

    private void ResetManagedGlState()
    {
        if (_rhiDevice is not null && !_rhiDevice.IsDisposed)
        {
            _rhiDevice.InvalidateContext("OpenGL context lost before native resource teardown");
            _rhiDevice.Dispose();
            _rhiDevice = null;
        }
        _initialized = false;
        _meshResources.Clear();
        _controlTextures.Clear();
        _materialTextures.Clear();
        _deferredMaterialTextureReleases.ClearWithoutRelease();
        _deferredControlTextureReleases.ClearWithoutRelease();
        _highScalePaletteTextures.Clear();
        _meshBatches.Clear();
        _particleBatches.Clear();
        _highScaleGpuBatches.Clear();
        _cachedDrawCommands.Clear();
        _frameDrawCommandScratch.Clear();
        _ordinarySlotByObjectId.Clear();
        _ordinaryTransformDirtyScratch.Clear();
        _ordinaryInterpolationDirtyScratch.Clear();
        _ordinaryDirtySeen.Clear();
        _lastMeshAttributeResource = null;
        _boundVertexArray = 0;
        _controlVertexArray = _skyboxVertexArray = 0;
        _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = _skyboxProgram = 0;
        _skyboxVertexBuffer = _skyboxIndexBuffer = 0;
        _lastSweptRegistryVersion = -1;
        _lastSweptBatchContentVersion = -1;
        _lastBuiltOrdinarySceneChangeVersion = -1;
        _lastBuiltOrdinaryTransformVersion = -1;
        _lastBuiltOrdinaryParticleVersion = -1;
        _lastBuiltOrdinaryRegistryVersion = -1;
        _lastBuiltOrdinaryInterpolationVersion = -1;
        _lastBuiltOrdinaryCameraVersion = -1;
        _hasAdaptiveTransparentOrdinaryBatches = false;
        _hasCameraDependentParticleBatches = false;
        _ordinaryBatchStatsCache.Reset();
        _supportsInstancing = false;
        _instancedDrawPathBroken = false;
        _instancedDrawValidationBudget = InstancedDrawValidationBudgetInitial;
        _instancedDrawFailureCount = 0;
        _lastInstancedDrawFailureReason = "none";
        _instancingApi = "unresolved";
        _vertexArrayApi = "unresolved";
        _supportsVertexArrays = false;
        _supportsBoneTextureSkinning = false;
        _gpuSkinTextureBoneLimit = 0;
        _vertexAttribDivisor = null;
        _vertexAttrib4f = null;
        _drawElementsInstanced = null;
        _bufferSubData = null;
        _genVertexArrays = null;
        _bindVertexArray = null;
        _deleteVertexArrays = null;
        _disableVertexAttribArray = null;
        _getIntegerv = null;
        _getString = null;
        _getError = null;
        _genQueries = null;
        _deleteQueries = null;
        _beginQuery = null;
        _endQuery = null;
        _getQueryObjectiv = null;
        _getQueryObjectui64v = null;
        Array.Clear(_gpuTimerQueries, 0, _gpuTimerQueries.Length);
        Array.Clear(_gpuTimerPending, 0, _gpuTimerPending.Length);
        _gpuTimerActiveSlot = -1;
        _gpuTimerNextSlot = 0;
        _lastGpuFrameMilliseconds = double.NaN;
        _cullFace = null;
        _depthFunc = null;
        _appliedCullMode = (CullMode)(-1);
    }

    private void SweepUnusedResources(GlInterface gl, Scene3D scene, SceneFrameSnapshot3D snapshot)
    {
        var registryVersion = scene.Registry.Version;
        var batchContentVersion = scene.BatchContentVersion;
        if (_lastSweptRegistryVersion == registryVersion && _lastSweptBatchContentVersion == batchContentVersion) return;

        var liveMeshes = _liveMeshSweepScratch;
        var liveControlPlanes = _liveControlPlaneSweepScratch;
        var liveMaterialTextures = _liveMaterialTextureSweepScratch;
        liveMeshes.Clear();
        liveControlPlanes.Clear();
        liveMaterialTextures.Clear();

        SceneRenderResourceCollector3D.CollectLiveMeshesAndTextures(scene, snapshot, liveMeshes, liveMaterialTextures);
        foreach (var obj in snapshot.AllObjectsInternal)
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
            DisposeMeshResource(gl, key, _meshResources[key]);
            _meshResources.Remove(key);
        }

        _textureSweepScratch.Clear();
        foreach (var pair in _controlTextures)
        {
            if (!liveControlPlanes.Contains(pair.Key)) _textureSweepScratch.Add(pair.Key);
        }
        foreach (var key in _textureSweepScratch)
        {
            var texture = _controlTextures[key];
            _controlTextures.Remove(key);
            _deferredControlTextureReleases.Enqueue(texture, _rhiDevice?.FrameIndex ?? 0, _resourceConfiguration?.DeferredReleaseFrames ?? 0);
        }

        _textureSweepScratch.Clear();
        foreach (var pair in _materialTextures)
        {
            if (!liveMaterialTextures.Contains(pair.Key)) _textureSweepScratch.Add(pair.Key);
        }
        foreach (var key in _textureSweepScratch)
        {
            var texture = _materialTextures[key];
            _materialTextures.Remove(key);
            _deferredMaterialTextureReleases.Enqueue(texture, _rhiDevice?.FrameIndex ?? 0, _resourceConfiguration?.DeferredReleaseFrames ?? 0);
        }
        _lastSweptRegistryVersion = registryVersion;
        _lastSweptBatchContentVersion = batchContentVersion;
    }

    private void DrawSkybox(GlInterface gl, SceneRenderFrameContext3D frame, RenderStats stats)
    {
        var scene = frame.Scene;
        var skybox = scene.Environment.Skybox;
        if (skybox.Mode == SkyboxMode3D.None || _skyboxProgram == 0) return;

        gl.UseProgram(_skyboxProgram);
        UploadColor3(_uniform3f, _skyboxTopColorLocation, skybox.TopColor);
        UploadColor3(_uniform3f, _skyboxHorizonColorLocation, skybox.HorizonColor);
        UploadColor3(_uniform3f, _skyboxBottomColorLocation, skybox.BottomColor);
        UploadFloat(_uniform1f, _skyboxIntensityLocation, skybox.Intensity);
        _uniform1i?.Invoke(_skyboxModeLocation, (int)skybox.Mode);
        UploadVector3(_uniform3f, _skyboxCameraRightLocation, scene.Camera.Right);
        UploadVector3(_uniform3f, _skyboxCameraUpLocation, scene.Camera.SafeUp);
        UploadVector3(_uniform3f, _skyboxCameraForwardLocation, scene.Camera.Forward);
        var verticalProjectionScale = MathF.Tan(scene.Camera.FieldOfViewDegrees * (MathF.PI / 360f));
        UploadFloat(_uniform1f, _skyboxProjectionScaleXLocation, verticalProjectionScale * frame.Aspect);
        UploadFloat(_uniform1f, _skyboxProjectionScaleYLocation, verticalProjectionScale);
        UploadSkyboxTexture(gl, skybox, stats);
        UploadSkyboxCubemapTextures(gl, skybox, stats);

        _disable!(GlDepthTest);
        _depthMask!(0);
        BindSkyboxGeometry(gl);
        gl.DrawElements(GlTriangles, 6, GlUnsignedShort, IntPtr.Zero);
        _depthMask!(1);
        gl.Enable(GlDepthTest);

        stats.SkyboxEnabled = true;
        stats.SkyboxMode = (int)skybox.Mode;
        stats.SkyboxDrawCalls++;
        stats.DrawCallCount++;
    }

    private void BindSkyboxGeometry(GlInterface gl)
    {
        ForceBindVertexArray(_skyboxVertexArray);
        gl.BindBuffer(GlArrayBuffer, _skyboxVertexBuffer);
        gl.EnableVertexAttribArray(_skyboxPositionLocation);
        gl.VertexAttribPointer(_skyboxPositionLocation, 2, GlFloat, 0, sizeof(float) * 2, IntPtr.Zero);
        ResetDivisor(_skyboxPositionLocation);
        gl.BindBuffer(GlElementArrayBuffer, _skyboxIndexBuffer);
    }


    private void ConfigureForwardBlendState(GlInterface gl, bool transparent)
    {
        if (transparent)
        {
            gl.Enable(GlBlend);
            _blendFunc?.Invoke(GlSrcAlpha, GlOneMinusSrcAlpha);
            _depthMask?.Invoke(0);
        }
        else
        {
            _depthMask?.Invoke(1);
            _disable?.Invoke(GlBlend);
        }
    }

    private void ApplyCullMode(GlInterface gl, CullMode cullMode)
    {
        if (_appliedCullMode == cullMode)
        {
            return;
        }

        switch (cullMode)
        {
            case CullMode.None:
                (_disable ?? throw new InvalidOperationException("OpenGL glDisable is required for material culling state."))(GlCullFace);
                break;
            case CullMode.Back:
                gl.Enable(GlCullFace);
                (_cullFace ?? throw new InvalidOperationException("OpenGL glCullFace is required for CullMode.Back."))(GlBack);
                break;
            case CullMode.Front:
                gl.Enable(GlCullFace);
                (_cullFace ?? throw new InvalidOperationException("OpenGL glCullFace is required for CullMode.Front."))(GlFront);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cullMode), cullMode, "Unsupported material culling mode.");
        }

        _appliedCullMode = cullMode;
    }

    private static bool IsTransparent(MaterialBinding3D material)
        => material.Surface == SurfaceMode.Transparent || material.BaseColor.A < 0.999f;

    private void UploadPostProcessing(Scene3D scene, RenderPipelinePlan3D pipeline)
    {
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

    private void UploadDistanceFade(Scene3D scene)
    {
        var drawDistance = scene.Performance.DrawDistance;
        if (drawDistance <= 0f || float.IsPositiveInfinity(drawDistance))
        {
            UploadVector4(_uniform4f, _meshDistanceFadeParamsLocation, Vector4.Zero);
            return;
        }

        var fadeBand = scene.Performance.EnableDistanceFade ? MathF.Max(scene.Performance.DistanceFadeBand, 0f) : 0f;
        var fadeStart = MathF.Max(0f, drawDistance - fadeBand);
        UploadVector4(_uniform4f, _meshDistanceFadeParamsLocation, new Vector4(
            drawDistance,
            fadeStart,
            fadeBand,
            fadeBand > 0.001f ? 1f : 0f));
    }

    private void DrawMeshes(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats, RenderPipelinePlan3D pipeline)
    {
        var frame = plan.Frame;
        var scene = frame.Scene;
        var viewProjection = frame.ViewProjection;
        var hasHighScale = plan.HasVisibleHighScale;
        if (!HasActiveMeshOrParticleBatches() && !hasHighScale) return;

        gl.UseProgram(_meshProgram);
        _lastMeshAttributeResource = null;
        UploadLighting(scene);
        UploadVector3(_uniform3f, _meshCameraPositionLocation, scene.Camera.Position);
        UploadPostProcessing(scene, pipeline);
        UploadDistanceFade(scene);
        UploadMatrix(_uniformMatrix4fv, _meshViewProjLocation, viewProjection, _matrixUploadBuffer);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        UploadFloat(_uniform1f, _meshBaseColorTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshNormalTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshMetallicRoughnessTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshEmissiveTextureEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
        UploadVector3(_uniform3f, _meshParticleCameraRightLocation, scene.Camera.Right);
        UploadVector3(_uniform3f, _meshParticleCameraUpLocation, scene.Camera.SafeUp);
        UploadVector3(_uniform3f, _meshParticleCameraForwardLocation, scene.Camera.Forward);
        stats.WebGlClientGpuTransformAnimation = scene.Performance.EnableWebGlClientGpuTransformAnimation;
        UploadFloat(_uniform1f, _meshUseInstancingLocation, CanUseInstancedDrawPath ? 1f : 0f);
        UploadClientTransformAnimation(scene, enabled: false);
        DrawInstancedRenderCommandStream(gl, plan, stats);
    }

    private bool RequiresOrdinaryBatchPlan(SceneRenderFrameContext3D frame)
    {
        var scene = frame.Scene;
        var interpolationVersion = scene.FrameInterpolator.RenderVersion;
        var transformChanged = _lastBuiltOrdinaryTransformVersion != scene.BatchTransformVersion;
        var interpolationChanged = _lastBuiltOrdinaryInterpolationVersion != interpolationVersion;

        // Opaque and exact-transparent transform/interpolation changes keep batch identity;
        // the journal patches retained slots and cached commands refresh exact distances.
        // Only adaptive depth bins need a new Core plan when camera-relative order changes.
        return _lastBuiltOrdinarySceneChangeVersion != scene.BatchContentVersion ||
               _lastBuiltOrdinaryParticleVersion != scene.ParticleContentVersion ||
               (_hasAdaptiveTransparentOrdinaryBatches &&
                (transformChanged || interpolationChanged || _lastBuiltOrdinaryCameraVersion != scene.CameraVersion)) ||
               (_hasCameraDependentParticleBatches && _lastBuiltOrdinaryCameraVersion != scene.CameraVersion);
    }

    private void BuildBatches(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        var frame = plan.Frame;
        var scene = frame.Scene;
        var registryVersion = scene.Registry.Version;
        var interpolationVersion = scene.FrameInterpolator.RenderVersion;
        var transformVersion = scene.BatchTransformVersion;
        var particleVersion = scene.ParticleContentVersion;

        // Camera movement is not batch content. Only transparent ordinary batches depend on
        // camera order; draw-distance/fade is resolved in the mesh shader from uniforms.
        if (!RequiresOrdinaryBatchPlan(frame))
        {
            if (TryApplyRetainedOrdinarySlotUpdates(frame, stats))
            {
                _ordinaryBatchStatsCache.ApplyTo(stats);
                _lastBuiltOrdinaryTransformVersion = transformVersion;
                _lastBuiltOrdinaryInterpolationVersion = interpolationVersion;
                return;
            }

            // Rebuild only when retained slot identity/mesh/material invariants truly changed.
            // Cursor expiry alone is recovered inside TryApplyRetainedOrdinarySlotUpdates by
            // refreshing all existing slots, which is substantially cheaper than rebuilding
            // the render plan on every animated frame.
            _retainedOrdinaryPlanRebuildCount++;
            stats.RetainedOrdinaryPlanRebuildCount = _retainedOrdinaryPlanRebuildCount;
            stats.RetainedOrdinaryLastFailureReason = _lastRetainedOrdinaryFailureReason;
            LogRetainedPlanRebuildWarning();
            plan = SceneRenderPlanBuilder3D.Build(
                frame,
                _fullRenderPlanScratch,
                stats,
                includeOrdinary: true,
                includeParticles: true,
                includeHighScale: false,
                frustumCullParticles: false);
            (_rhiDevice ?? throw new InvalidOperationException("OpenGL RHI device is not initialized."))
                .ValidateSubmission(plan.RhiSubmission);
        }

        _ordinaryBatchStatsCache.Reset();
        stats.SceneTraversalCount++;
        foreach (var batch in _meshBatches.Values) batch.BeginBuild();
        foreach (var batch in _particleBatches.Values) batch.BeginBuild();
        _ordinarySlotByObjectId.Clear();
        _cachedDrawCommands.Clear();

        var hasCameraDependentParticleBatches = false;
        for (var i = 0; i < plan.ParticleItems.Count; i++)
        {
            var item = plan.ParticleItems[i];
            hasCameraDependentParticleBatches |= item.CameraDependentOrder;
            BuildParticleBatch(item, scene.Camera.Position);
        }

        _hasAdaptiveTransparentOrdinaryBatches = plan.TransparentOrdinaryBatches.Count > 0;
        _hasCameraDependentParticleBatches = hasCameraDependentParticleBatches;
        for (var batchIndex = 0; batchIndex < plan.OrdinaryBatches.Count; batchIndex++)
        {
            var plannedBatch = plan.OrdinaryBatches[batchIndex];
            var batch = GetBatch(plannedBatch.BatchId, plannedBatch.LogicalMeshBatchKey, plannedBatch.Mesh, plannedBatch.Material);
            ConfigureBatchSkinning(batch, plannedBatch.Items.Count > 0 ? plannedBatch.Items[0] : default);

            for (var itemIndex = 0; itemIndex < plannedBatch.Items.Count; itemIndex++)
            {
                var item = plannedBatch.Items[itemIndex];
                var slot = batch.AddTracked(item.Owner.Id, item.Owner.TransformVersion ^ interpolationVersion, item.Owner.MaterialVersion, item.Model, item.Color);
                _ordinarySlotByObjectId[item.Owner.Id] = new RetainedOrdinarySlotRef(batch, slot, item.Owner);
            }
        }

        for (var itemIndex = 0; itemIndex < plan.TransparentOrdinaryItems.Count; itemIndex++)
        {
            var transparent = plan.TransparentOrdinaryItems[itemIndex];
            var item = transparent.Item;
            var batch = GetBatch(transparent.DrawId, item.LogicalMeshBatchKey, item.Mesh, item.Material);
            ConfigureBatchSkinning(batch, item);
            var slot = batch.AddTracked(item.Owner.Id, item.Owner.TransformVersion ^ interpolationVersion, item.Owner.MaterialVersion, item.Model, item.Color);
            _ordinarySlotByObjectId[item.Owner.Id] = new RetainedOrdinarySlotRef(batch, slot, item.Owner);
        }

        for (var batchIndex = 0; batchIndex < plan.TransparentOrdinaryBatches.Count; batchIndex++)
        {
            var plannedBatch = plan.TransparentOrdinaryBatches[batchIndex];
            var batch = GetBatch(plannedBatch.BatchId, plannedBatch.LogicalMeshBatchKey, plannedBatch.Mesh, plannedBatch.Material);
            ConfigureBatchSkinning(batch, plannedBatch.Items.Count > 0 ? plannedBatch.Items[0] : default);
            for (var itemIndex = 0; itemIndex < plannedBatch.Items.Count; itemIndex++)
            {
                var item = plannedBatch.Items[itemIndex];
                var slot = batch.AddTracked(item.Owner.Id, item.Owner.TransformVersion ^ interpolationVersion, item.Owner.MaterialVersion, item.Model, item.Color);
                _ordinarySlotByObjectId[item.Owner.Id] = new RetainedOrdinarySlotRef(batch, slot, item.Owner);
            }
        }

        for (var commandIndex = 0; commandIndex < plan.DrawCommands.Count; commandIndex++)
        {
            var command = plan.DrawCommands[commandIndex];
            if (command.Kind == SceneRenderCommandKind3D.OrdinaryBatch && command.OrdinaryBatch is not null)
            {
                var batch = GetBatch(command.OrdinaryBatch.BatchId, command.OrdinaryBatch.LogicalMeshBatchKey, command.OrdinaryBatch.Mesh, command.OrdinaryBatch.Material);
                _cachedDrawCommands.Add(CachedOpenGlDrawCommand.ForMesh(command, batch));
            }
            else if (command.Kind == SceneRenderCommandKind3D.TransparentOrdinaryItem && command.TransparentOrdinary is { } transparent)
            {
                var item = transparent.Item;
                var batch = GetBatch(transparent.DrawId, item.LogicalMeshBatchKey, item.Mesh, item.Material);
                _cachedDrawCommands.Add(CachedOpenGlDrawCommand.ForMesh(command, batch));
            }
            else if (command.Kind == SceneRenderCommandKind3D.TransparentOrdinaryBatch && command.TransparentOrdinaryBatch is { } transparentBatch)
            {
                var batch = GetBatch(transparentBatch.BatchId, transparentBatch.LogicalMeshBatchKey, transparentBatch.Mesh, transparentBatch.Material);
                _cachedDrawCommands.Add(CachedOpenGlDrawCommand.ForMesh(command, batch));
            }
            else if (command.Kind == SceneRenderCommandKind3D.ParticleSystem && command.Particle is { } particle)
            {
                var batch = GetParticleBatch(particle.RetainedBatchId, particle.Mesh.ResourceKey, particle.Mesh, particle.Material, particle.Billboard, particle.Transparent);
                _cachedDrawCommands.Add(CachedOpenGlDrawCommand.ForParticle(command, batch));
            }
        }

        foreach (var batch in _meshBatches.Values) batch.EndBuild();
        foreach (var batch in _particleBatches.Values) batch.EndBuild();
        SweepInactiveInstanceBatches(gl);
        _ordinaryBatchStatsCache.Capture(stats);
        _lastBuiltOrdinarySceneChangeVersion = scene.BatchContentVersion;
        _lastBuiltOrdinaryTransformVersion = transformVersion;
        _lastBuiltOrdinaryParticleVersion = particleVersion;
        _lastBuiltOrdinaryRegistryVersion = registryVersion;
        _lastBuiltOrdinaryInterpolationVersion = interpolationVersion;
        _lastBuiltOrdinaryCameraVersion = scene.CameraVersion;

        // HighScaleInstanceLayer3D is intentionally not expanded into the normal per-frame mesh batch.
        // It is rendered by DrawHighScaleLayers using retained chunk/part instance buffers.
    }

    private bool TryApplyRetainedOrdinarySlotUpdates(SceneRenderFrameContext3D frame, RenderStats stats)
    {
        var scene = frame.Scene;
        var interpolationVersion = scene.FrameInterpolator.RenderVersion;
        var interpolationChanged = _lastBuiltOrdinaryInterpolationVersion != interpolationVersion;
        var transformChanged = _lastBuiltOrdinaryTransformVersion != scene.BatchTransformVersion;
        if (!interpolationChanged && !transformChanged)
        {
            return true;
        }

        if (_ordinarySlotByObjectId.Count == 0)
        {
            return true;
        }

        foreach (var batch in _meshBatches.Values)
        {
            batch.BeginSlotUpdates();
        }

        var updated = 0;
        if (interpolationChanged)
        {
            scene.FrameInterpolator.CopyActiveObjects(_ordinaryTransformDirtyScratch);
            _ordinaryDirtySeen.Clear();
            for (var i = 0; i < _ordinaryTransformDirtyScratch.Count; i++)
            {
                _ordinaryDirtySeen.Add(_ordinaryTransformDirtyScratch[i]);
            }

            if (transformChanged)
            {
                if (!scene.TryCopyBatchTransformChangesSince(_lastBuiltOrdinaryTransformVersion, _ordinaryInterpolationDirtyScratch))
                {
                    _ordinaryTransformDirtyScratch.Clear();
                    _ordinaryInterpolationDirtyScratch.Clear();
                    _ordinaryDirtySeen.Clear();
                    _lastRetainedOrdinaryFailureReason = $"transform-journal-cursor-unavailable; retainedVersion={_lastBuiltOrdinaryTransformVersion}; currentVersion={scene.BatchTransformVersion}; interpolationChanged=true";
                    stats.RetainedOrdinaryLastFailureReason = _lastRetainedOrdinaryFailureReason;
                    if (!TryRefreshAllRetainedOrdinarySlots(frame, stats, interpolationVersion, ref updated)) return false;
                    _retainedOrdinaryCursorRecoveryCount++;
                    stats.RetainedOrdinaryCursorRecoveryCount = _retainedOrdinaryCursorRecoveryCount;
                    goto Completed;
                }
                for (var i = 0; i < _ordinaryInterpolationDirtyScratch.Count; i++)
                {
                    var obj = _ordinaryInterpolationDirtyScratch[i];
                    if (_ordinaryDirtySeen.Add(obj)) _ordinaryTransformDirtyScratch.Add(obj);
                }
            }

            if (_ordinaryTransformDirtyScratch.Count == 0)
            {
                foreach (var slot in _ordinarySlotByObjectId.Values)
                {
                    if (!TryUpdateRetainedOrdinarySlot(frame, slot.Owner, slot, interpolationVersion, stats))
                    {
                        _ordinaryTransformDirtyScratch.Clear();
                        _ordinaryInterpolationDirtyScratch.Clear();
                        _ordinaryDirtySeen.Clear();
                        return false;
                    }
                    updated++;
                }
            }
            else
            {
                for (var i = 0; i < _ordinaryTransformDirtyScratch.Count; i++)
                {
                    var obj = _ordinaryTransformDirtyScratch[i];
                    if (!_ordinarySlotByObjectId.TryGetValue(obj.Id, out var slot)) continue;
                    if (!TryUpdateRetainedOrdinarySlot(frame, obj, slot, interpolationVersion, stats))
                    {
                        _ordinaryTransformDirtyScratch.Clear();
                        _ordinaryInterpolationDirtyScratch.Clear();
                        _ordinaryDirtySeen.Clear();
                        return false;
                    }
                    updated++;
                }
            }

            _ordinaryTransformDirtyScratch.Clear();
            _ordinaryInterpolationDirtyScratch.Clear();
            _ordinaryDirtySeen.Clear();
        }
        else
        {
            if (!scene.TryCopyBatchTransformChangesSince(_lastBuiltOrdinaryTransformVersion, _ordinaryTransformDirtyScratch))
            {
                _lastRetainedOrdinaryFailureReason = $"transform-journal-cursor-unavailable; retainedVersion={_lastBuiltOrdinaryTransformVersion}; currentVersion={scene.BatchTransformVersion}; interpolationChanged=false";
                stats.RetainedOrdinaryLastFailureReason = _lastRetainedOrdinaryFailureReason;
                if (!TryRefreshAllRetainedOrdinarySlots(frame, stats, interpolationVersion, ref updated)) return false;
                _retainedOrdinaryCursorRecoveryCount++;
                stats.RetainedOrdinaryCursorRecoveryCount = _retainedOrdinaryCursorRecoveryCount;
                goto Completed;
            }

            for (var i = 0; i < _ordinaryTransformDirtyScratch.Count; i++)
            {
                var obj = _ordinaryTransformDirtyScratch[i];
                if (!_ordinarySlotByObjectId.TryGetValue(obj.Id, out var slot))
                {
                    if (ShouldHaveOrdinaryRetainedSlot(obj))
                    {
                        _lastRetainedOrdinaryFailureReason = $"missing-slot; object={obj.Id}; type={obj.GetType().Name}";
                        return false;
                    }

                    continue;
                }

                if (!TryUpdateRetainedOrdinarySlot(frame, obj, slot, interpolationVersion, stats))
                {
                    return false;
                }

                updated++;
            }
        }

Completed:
        if (updated > 0)
        {
            stats.RetainedTransformSlotUpdateCount += updated;
        }

        return true;
    }

    private bool TryRefreshAllRetainedOrdinarySlots(
        SceneRenderFrameContext3D frame,
        RenderStats stats,
        int interpolationVersion,
        ref int updated)
    {
        foreach (var slot in _ordinarySlotByObjectId.Values)
        {
            if (!TryUpdateRetainedOrdinarySlot(frame, slot.Owner, slot, interpolationVersion, stats)) return false;
            updated++;
        }
        return true;
    }

    private static bool ShouldHaveOrdinaryRetainedSlot(Object3D obj)
    {
        if (obj is null || !obj.IsVisible || !obj.UseMeshRendering || obj is ParticleSystem3D)
        {
            return false;
        }

        var mesh = obj.GetMesh();
        return mesh.Positions.Length != 0 && mesh.Indices.Length != 0;
    }

    private bool TryUpdateRetainedOrdinarySlot(SceneRenderFrameContext3D frame, Object3D obj, RetainedOrdinarySlotRef slot, int interpolationVersion, RenderStats stats)
    {
        var batch = slot.Batch;
        if (!batch.TryGetSlot(obj.Id, out var currentSlot) || currentSlot != slot.Slot)
        {
            _lastRetainedOrdinaryFailureReason = $"slot-mismatch; object={obj.Id}; expectedSlot={slot.Slot}; actualSlot={currentSlot}";
            return false;
        }

        var skinnedPart = obj as ModelPart3D;
        ValidateGpuSkinning(skinnedPart);

        var mesh = obj.GetMesh();
        if (!ReferenceEquals(mesh, batch.Mesh) && !string.Equals(mesh.ResourceKey, batch.Mesh.ResourceKey, StringComparison.Ordinal))
        {
            _lastRetainedOrdinaryFailureReason = $"mesh-changed; object={obj.Id}; retained={batch.Mesh.ResourceKey}; current={mesh.ResourceKey}";
            return false;
        }

        var material = MaterialBinding3D.FromMaterial(obj.Material);
        if (!string.Equals(material.Key, batch.Material.Key, StringComparison.Ordinal))
        {
            _lastRetainedOrdinaryFailureReason = $"material-changed; object={obj.Id}; retained={batch.Material.Key}; current={material.Key}";
            return false;
        }

        var previousSkinningVersion = batch.SkinningVersion;
        ConfigureBatchSkinning(batch, skinnedPart);
        if (batch.SkinningVersion != previousSkinningVersion)
        {
            _retainedSkinningBatchUpdateCount++;
            stats.RetainedSkinningBatchUpdateCount = _retainedSkinningBatchUpdateCount;
        }

        var model = frame.Scene.FrameInterpolator.TryGetInterpolatedModel(obj.Id, out var interpolatedModel)
            ? interpolatedModel
            : obj.GetModelMatrix();
        var color = SceneOrdinaryRenderItemBuilder3D.ResolveColor(obj);
        return batch.UpdateTrackedSlot(
            currentSlot,
            obj.Id,
            obj.TransformVersion ^ interpolationVersion,
            obj.MaterialVersion,
            model,
            color,
            frame.Scene.Camera.Position);
    }

    private void SweepInactiveInstanceBatches(GlInterface gl)
    {
        _batchRemovalScratch.Clear();
        foreach (var pair in _meshBatches)
        {
            if (pair.Value.InstanceCount == 0)
            {
                _batchRemovalScratch.Add(pair.Key);
            }
        }

        for (var i = 0; i < _batchRemovalScratch.Count; i++)
        {
            if (_meshBatches.TryGetValue(_batchRemovalScratch[i], out var batch))
            {
                batch.Dispose(gl);
                _meshBatches.Remove(_batchRemovalScratch[i]);
            }
        }

        _batchRemovalScratch.Clear();
        foreach (var pair in _particleBatches)
        {
            if (pair.Value.InstanceCount == 0)
            {
                _batchRemovalScratch.Add(pair.Key);
            }
        }

        for (var i = 0; i < _batchRemovalScratch.Count; i++)
        {
            if (_particleBatches.TryGetValue(_batchRemovalScratch[i], out var batch))
            {
                batch.Dispose(gl);
                _particleBatches.Remove(_batchRemovalScratch[i]);
            }
        }

        _batchRemovalScratch.Clear();
    }

    private void ValidateGpuSkinning(ModelPart3D? part)
    {
        if (part is null || !part.IsSkinned) return;
        if (part.CurrentGpuSkinMatricesInternal.Length == 0)
            throw new InvalidOperationException($"Skinned model part '{part.Name}' has no GPU bone matrices; rendering an undeformed bind pose is forbidden.");

        if (!_supportsBoneTextureSkinning || part.CurrentGpuSkinMatricesInternal.Length > _gpuSkinTextureBoneLimit)
        {
            throw new InvalidOperationException(
                $"GPU skinning is required for model part '{part.Name}', but the OpenGL bone-texture path is unavailable " +
                $"or its {part.CurrentGpuSkinMatricesInternal.Length} bones exceed the GPU limit {_gpuSkinTextureBoneLimit}. CPU skinning fallback is disabled.");
        }

    }

    private void EnsureGpuSkinningAvailable(SceneFrameSnapshot3D snapshot)
    {
        foreach (var obj in snapshot.AllObjectsInternal)
        {
            if (obj is ModelPart3D part) ValidateGpuSkinning(part);
        }
    }

    private static void ConfigureBatchSkinning(MeshBatchData batch, OrdinaryRenderItem3D item)
        => ConfigureBatchSkinning(batch, item.UsesGpuSkinning ? item.SkinnedPart : null);

    private static void ConfigureBatchSkinning(MeshBatchData batch, ModelPart3D? skinnedPart)
    {
        if (skinnedPart is not null &&
            skinnedPart.IsSkinned &&
            skinnedPart.CurrentGpuSkinMatricesInternal.Length > 0)
        {
            batch.SetSkinning(skinnedPart.CurrentGpuSkinMatricesInternal, skinnedPart.SkinningVersion);
        }
        else
        {
            batch.SetSkinning(Array.Empty<Matrix4x4>(), -1);
        }
    }

    private void LogRetainedPlanRebuildWarning()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedMs = _lastRetainedPlanRebuildWarningTicks == 0
            ? double.PositiveInfinity
            : (now - _lastRetainedPlanRebuildWarningTicks) * 1000d / Stopwatch.Frequency;
        if (elapsedMs < 5000d)
        {
            _suppressedRetainedPlanRebuildWarnings++;
            return;
        }

        var suppressed = _suppressedRetainedPlanRebuildWarnings;
        _suppressedRetainedPlanRebuildWarnings = 0;
        _lastRetainedPlanRebuildWarningTicks = now;
        EngineLog3D.Warning(
            "OpenGL",
            $"Retained ordinary invariants changed; rebuilding the ordinary render plan. Reason={_lastRetainedOrdinaryFailureReason}." +
            (suppressed > 0 ? $" Suppressed equivalent rebuild warnings: {suppressed}." : string.Empty));
    }

    private void BuildParticleBatch(ParticleRenderItem3D item, Vector3 cameraPosition)
    {
        var particles = item.System;
        var count = particles.AliveCount;
        if (count <= 0) return;

        var batch = GetParticleBatch(item.RetainedBatchId, item.Mesh.ResourceKey, item.Mesh, item.Material, item.Billboard, item.Transparent);
        int[]? order = null;
        if (ParticleInstanceStream3D.ShouldSortBackToFront(item))
        {
            ParticleInstanceStream3D.EnsureSortScratch(ref _particleSortOrderScratch, ref _particleSortKeyScratch, count);
            ParticleInstanceStream3D.BuildBackToFrontOrder(item, cameraPosition, _particleSortOrderScratch, _particleSortKeyScratch);
            order = _particleSortOrderScratch;
        }

        var particleList = particles.Particles;
        for (var outputIndex = 0; outputIndex < count; outputIndex++)
        {
            var sourceIndex = order is null ? outputIndex : order[outputIndex];
            var instance = ParticleInstanceStream3D.ResolveInstance(item, particleList[sourceIndex], cameraPosition);
            if (item.Billboard)
            {
                batch.AddBillboardParticle(instance.Center, instance.Size, instance.Color);
            }
            else
            {
                batch.Add(instance.Model, instance.Color);
            }
        }
    }

    private MeshBatchData GetBatch(string batchId, string meshKey, Mesh3D mesh, MaterialBinding3D material)
    {
        var key = new MeshBatchKey(batchId, meshKey, material.Key, particleBillboard: false);
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

    private ParticleBatchData GetParticleBatch(string batchId, string meshKey, Mesh3D mesh, MaterialBinding3D material, bool billboard, bool transparent)
    {
        var key = new MeshBatchKey(batchId, meshKey, material.Key, billboard);
        if (!_particleBatches.TryGetValue(key, out var batch))
        {
            batch = new ParticleBatchData(meshKey, mesh, material, billboard, transparent);
            _particleBatches[key] = batch;
        }
        else
        {
            batch.Mesh = mesh;
            batch.Material = material;
            batch.Transparent = transparent;
        }
        return batch;
    }


    private bool HasActiveMeshOrParticleBatches()
    {
        foreach (var batch in _meshBatches.Values)
            if (batch.InstanceCount > 0)
                return true;
        foreach (var batch in _particleBatches.Values)
            if (batch.InstanceCount > 0)
                return true;
        return false;
    }

    private void DrawInstancedRenderCommandStream(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        var scene = plan.Frame.Scene;
        if (!plan.HasVisibleOrdinary && !plan.HasVisibleParticles)
        {
            // Partial plans skip ordinary/particle extraction, but still need the canonical
            // Core order: opaque ordinary -> high-scale -> particles/transparent.  Rebuild a
            // lightweight command stream from retained batch refs and the freshly planned
            // high-scale commands instead of drawing cached dictionaries in backend order.
            DrawInstancedCachedRenderCommandStream(gl, plan, stats);
            return;
        }

        var highScaleStateActive = false;
        _highScaleTransformBatchUploadsThisFrame = 0;

        for (var commandIndex = 0; commandIndex < plan.DrawCommands.Count; commandIndex++)
        {
            var command = plan.DrawCommands[commandIndex];
            switch (command.Kind)
            {
                case SceneRenderCommandKind3D.OrdinaryBatch when command.OrdinaryBatch is not null:
                {
                    if (highScaleStateActive)
                    {
                        EndHighScaleDrawState();
                        UploadClientTransformAnimation(scene, enabled: false);
                        highScaleStateActive = false;
                    }

                    var batch = GetBatch(command.OrdinaryBatch.BatchId, command.OrdinaryBatch.LogicalMeshBatchKey, command.OrdinaryBatch.Mesh, command.OrdinaryBatch.Material);
                    DrawMeshBatchInstanced(gl, batch, plan.Frame.ViewProjection, stats);
                    break;
                }
                case SceneRenderCommandKind3D.TransparentOrdinaryItem when command.TransparentOrdinary is { } transparent:
                {
                    if (highScaleStateActive)
                    {
                        EndHighScaleDrawState();
                        UploadClientTransformAnimation(scene, enabled: false);
                        highScaleStateActive = false;
                    }

                    var item = transparent.Item;
                    var batch = GetBatch(transparent.DrawId, item.LogicalMeshBatchKey, item.Mesh, item.Material);
                    DrawMeshBatchInstanced(gl, batch, plan.Frame.ViewProjection, stats);
                    break;
                }
                case SceneRenderCommandKind3D.TransparentOrdinaryBatch when command.TransparentOrdinaryBatch is { } transparentBatch:
                {
                    if (highScaleStateActive)
                    {
                        EndHighScaleDrawState();
                        UploadClientTransformAnimation(scene, enabled: false);
                        highScaleStateActive = false;
                    }

                    var batch = GetBatch(transparentBatch.BatchId, transparentBatch.LogicalMeshBatchKey, transparentBatch.Mesh, transparentBatch.Material);
                    DrawMeshBatchInstanced(gl, batch, plan.Frame.ViewProjection, stats);
                    break;
                }
                case SceneRenderCommandKind3D.ParticleSystem when command.Particle is { } particle:
                {
                    if (highScaleStateActive)
                    {
                        EndHighScaleDrawState();
                        UploadClientTransformAnimation(scene, enabled: false);
                        highScaleStateActive = false;
                    }

                    var batch = GetParticleBatch(particle.RetainedBatchId, particle.Mesh.ResourceKey, particle.Mesh, particle.Material, particle.Billboard, particle.Transparent);
                    DrawParticleBatchInstanced(gl, batch, stats);
                    break;
                }
                case SceneRenderCommandKind3D.HighScaleLayer when command.HighScaleLayer is not null:
                {
                    if (!highScaleStateActive)
                    {
                        UploadClientTransformAnimation(scene, scene.Performance.EnableWebGlClientGpuTransformAnimation);
                        BeginHighScaleDrawState(gl);
                        highScaleStateActive = true;
                    }

                    DrawHighScaleLayer(gl, scene, plan.Frame.ViewProjection, command.HighScaleLayer, scene.Camera.Position, stats);
                    break;
                }
            }
        }

        if (highScaleStateActive)
        {
            EndHighScaleDrawState();
            UploadClientTransformAnimation(scene, enabled: false);
        }

        ConfigureForwardBlendState(gl, false);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
        ResetInstanceAttributeDivisors();
    }


    private void DrawInstancedCachedRenderCommandStream(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        var scene = plan.Frame.Scene;
        RebuildFrameDrawCommandScratch(plan);
        var highScaleStateActive = false;
        _highScaleTransformBatchUploadsThisFrame = 0;

        for (var i = 0; i < _frameDrawCommandScratch.Count; i++)
        {
            var command = _frameDrawCommandScratch[i];
            switch (command.Kind)
            {
                case SceneRenderCommandKind3D.OrdinaryBatch:
                case SceneRenderCommandKind3D.TransparentOrdinaryItem:
                case SceneRenderCommandKind3D.TransparentOrdinaryBatch:
                {
                    if (highScaleStateActive)
                    {
                        EndHighScaleDrawState();
                        UploadClientTransformAnimation(scene, enabled: false);
                        highScaleStateActive = false;
                    }

                    if (command.MeshBatch is not null)
                    {
                        DrawMeshBatchInstanced(gl, command.MeshBatch, plan.Frame.ViewProjection, stats);
                    }
                    break;
                }
                case SceneRenderCommandKind3D.ParticleSystem:
                {
                    if (highScaleStateActive)
                    {
                        EndHighScaleDrawState();
                        UploadClientTransformAnimation(scene, enabled: false);
                        highScaleStateActive = false;
                    }

                    if (command.ParticleBatch is not null)
                    {
                        DrawParticleBatchInstanced(gl, command.ParticleBatch, stats);
                    }
                    break;
                }
                case SceneRenderCommandKind3D.HighScaleLayer:
                {
                    if (command.HighScaleLayer is null)
                    {
                        break;
                    }

                    if (!highScaleStateActive)
                    {
                        UploadClientTransformAnimation(scene, scene.Performance.EnableWebGlClientGpuTransformAnimation);
                        BeginHighScaleDrawState(gl);
                        highScaleStateActive = true;
                    }

                    DrawHighScaleLayer(gl, scene, plan.Frame.ViewProjection, command.HighScaleLayer, scene.Camera.Position, stats);
                    break;
                }
            }
        }

        if (highScaleStateActive)
        {
            EndHighScaleDrawState();
            UploadClientTransformAnimation(scene, enabled: false);
        }

        ConfigureForwardBlendState(gl, false);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
        ResetInstanceAttributeDivisors();
    }

    private void RebuildFrameDrawCommandScratch(SceneRenderPlan3D plan)
    {
        _frameDrawCommandScratch.Clear();
        var sourceOrder = 0;
        var cameraPosition = plan.Frame.Scene.Camera.Position;

        for (var i = 0; i < _cachedDrawCommands.Count; i++)
        {
            var command = _cachedDrawCommands[i];
            if (command.Kind == SceneRenderCommandKind3D.OrdinaryBatch && !command.Transparent)
            {
                _frameDrawCommandScratch.Add(command.WithSourceOrder(sourceOrder++));
            }
        }

        for (var i = 0; i < plan.DrawCommands.Count; i++)
        {
            var command = plan.DrawCommands[i];
            if (command.Kind == SceneRenderCommandKind3D.HighScaleLayer && command.HighScaleLayer is not null)
            {
                _frameDrawCommandScratch.Add(CachedOpenGlDrawCommand.ForHighScale(command, sourceOrder++));
            }
        }

        for (var i = 0; i < _cachedDrawCommands.Count; i++)
        {
            var command = _cachedDrawCommands[i];
            if (command.Kind == SceneRenderCommandKind3D.ParticleSystem && !command.Transparent)
            {
                _frameDrawCommandScratch.Add(command.WithSourceOrder(sourceOrder++));
            }
        }

        for (var i = 0; i < _cachedDrawCommands.Count; i++)
        {
            var command = _cachedDrawCommands[i];
            if (command.Transparent)
            {
                _frameDrawCommandScratch.Add(command.WithSourceOrder(sourceOrder++).RefreshSortDistance(cameraPosition));
            }
        }

        _frameDrawCommandScratch.Sort(CachedOpenGlDrawCommand.CompareForDraw);
    }

    private bool CanUseInstancedDrawPath => _supportsInstancing && !_instancedDrawPathBroken && _drawElementsInstanced is not null && _vertexAttribDivisor is not null;

    private void RequireInstancedDrawPath(string scope)
    {
        if (!CanUseInstancedDrawPath)
        {
            throw CreateInstancingFailure(scope);
        }
    }

    private InvalidOperationException CreateInstancingFailure(string scope)
        => new($"OpenGL instanced rendering is required for {scope}; the GPU path is unavailable or failed validation. " +
               $"supportsInstancing={_supportsInstancing}, validationBroken={_instancedDrawPathBroken}, failures={_instancedDrawFailureCount}, " +
               $"vaoApi={_vertexArrayApi}, instancingApi={_instancingApi}, reason={_lastInstancedDrawFailureReason}.");

    private bool ShouldValidateInstancedDraw => _getError is not null && _instancedDrawValidationBudget > 0;

    private bool BeginInstancedDrawValidation(string meshKey, string scope)
    {
        if (!ShouldValidateInstancedDraw) return false;
        var queuedError = DrainGlErrors();
        if (queuedError != GlNoError)
        {
            // glGetError is a context-global queue. Establish a clean boundary
            // immediately before instance-buffer/attribute setup so only setup and
            // draw errors can invalidate the mandatory instancing path.
            EngineLog3D.Error(
                "OpenGL.Validation",
                $"Queued GL error {DescribeGlError(queuedError)} was isolated before instancing setup; " +
                $"it is not evidence that instancing failed. scope={scope}, mesh={meshKey}.");
        }
        return true;
    }

    private bool PrepareValidatedInstancedDrawState(
        GlInterface gl,
        MeshGpuResource resource,
        int program,
        string meshKey,
        string scope,
        bool validate)
    {
        // This is a permanent draw boundary, not only a startup validation aid. Avalonia
        // and several Windows GL drivers do not preserve every VAO/divisor binding exactly
        // as our managed cache expects across context rebinds. Reasserting the native VAO,
        // EBO and program immediately before instance pointers/draw prevents stale index or
        // vertex state without introducing a non-instanced/CPU fallback.
        ForceBindVertexArray(resource.VertexArray);
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
        gl.UseProgram(program);
        return ValidateInstancedOperation("VAO/index/program bind", meshKey, scope, validate);
    }

    private bool ValidateInstancedOperation(string operation, string meshKey, string scope, bool validate)
    {
        if (!validate) return true;
        var error = DrainGlErrors();
        if (error == GlNoError) return true;
        MarkInstancedDrawPathBroken(
            $"{operation} returned {DescribeGlError(error)} scope={scope} mesh={meshKey} " +
            $"vaoApi={_vertexArrayApi} instancingApi={_instancingApi}");
        return false;
    }

    private bool TryDrawElementsInstanced(int indexCount, int indexType, int instanceCount, string meshKey, string scope, bool validate)
    {
        if (!CanUseInstancedDrawPath || indexCount <= 0 || instanceCount <= 0)
        {
            return false;
        }

        _drawElementsInstanced!(GlTriangles, indexCount, indexType, IntPtr.Zero, instanceCount);

        if (!validate)
        {
            return true;
        }

        var operationError = DrainGlErrors();
        if (operationError != GlNoError)
        {
            MarkInstancedDrawPathBroken(
                $"glDrawElementsInstanced returned {DescribeGlError(operationError)} scope={scope} mesh={meshKey} " +
                $"vaoApi={_vertexArrayApi} instancingApi={_instancingApi}");
            return false;
        }

        _instancedDrawValidationBudget--;
        return true;
    }

    private int DrainGlErrors()
    {
        if (_getError is null) return GlNoError;
        var first = GlNoError;
        for (var i = 0; i < 16; i++)
        {
            var error = _getError();
            if (error == GlNoError) break;
            if (first == GlNoError) first = error;
        }

        return first;
    }

    private void MarkInstancedDrawPathBroken(string reason)
    {
        if (_instancedDrawPathBroken) return;
        _instancedDrawFailureCount++;
        _instancedDrawPathBroken = true;
        _instancedDrawValidationBudget = 0;
        _lastInstancedDrawFailureReason = reason;
        EngineLog3D.Error("OpenGL.Instancing", $"Instanced draw path was disabled after validation failure: {reason}");
    }

    private static string DescribeGlError(int error)
        => error switch
        {
            GlNoError => "GL_NO_ERROR (0x0000)",
            GlInvalidEnum => "GL_INVALID_ENUM (0x0500)",
            GlInvalidValue => "GL_INVALID_VALUE (0x0501)",
            GlInvalidOperation => "GL_INVALID_OPERATION (0x0502)",
            GlOutOfMemory => "GL_OUT_OF_MEMORY (0x0505)",
            GlInvalidFramebufferOperation => "GL_INVALID_FRAMEBUFFER_OPERATION (0x0506)",
            GlContextLost => "GL_CONTEXT_LOST (0x0507)",
            _ => $"unknown GL error 0x{error:X4}"
        };

    private void DrawMeshBatchInstanced(GlInterface gl, MeshBatchData batch, Matrix4x4 viewProjection, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;
        RequireInstancedDrawPath("ordinary meshes");

        var visibleCount = PrepareVisibleMeshBatch(gl, batch, viewProjection, stats, out var instanceBuffer);
        if (visibleCount == 0) return;

        ConfigureForwardBlendState(gl, IsTransparent(batch.Material));
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
        var resource = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        BindMeshAttributes(gl, resource);
        if (instanceBuffer == batch.InstanceBuffer)
        {
            EnsureBatchInstanceBuffer(gl, batch, stats);
            instanceBuffer = batch.InstanceBuffer;
        }

        UploadClassicMaterial(gl, batch.Material, stats);
        UploadBatchSkinning(gl, batch);
        var validate = BeginInstancedDrawValidation(batch.MeshKey, "mesh");
        if (!PrepareValidatedInstancedDrawState(gl, resource, _meshProgram, batch.MeshKey, "mesh", validate))
            throw CreateInstancingFailure("ordinary meshes");
        gl.BindBuffer(GlArrayBuffer, instanceBuffer);
        EnableInstanceAttributes(gl);
        if (!ValidateInstancedOperation("instance attribute setup", batch.MeshKey, "mesh", validate))
        {
            DisableInstanceAttributes();
            throw CreateInstancingFailure("ordinary meshes");
        }
        if (!TryDrawElementsInstanced(resource.IndexCount, resource.IndexType, visibleCount, batch.MeshKey, "mesh", validate))
        {
            DisableInstanceAttributes();
            throw CreateInstancingFailure("ordinary meshes");
        }

        DisableInstanceAttributes();
        stats.DrawCallCount++;
        stats.EstimatedDrawCallCount++;
        stats.InstancedBatchCount++;
    }

    private int PrepareVisibleMeshBatch(GlInterface gl, MeshBatchData batch, Matrix4x4 viewProjection, RenderStats stats, out int instanceBuffer)
    {
        instanceBuffer = batch.InstanceBuffer;
        var count = batch.InstanceCount;
        if (count <= 0) return 0;

        // GPU-skinned bounds are rest-pose bounds and can be too tight for animated poses.
        // Keep them conservative until the engine has animated/skinned world bounds.
        if (batch.HasSkinning || !batch.Mesh.LocalBounds.IsValid)
        {
            return count;
        }

        var frustum = FrustumCuller3D.ExtractClipFrustum(viewProjection);
        var data = batch.Data;
        var stride = batch.FloatStride;
        var visible = 0;
        var compacted = false;
        for (var i = 0; i < count; i++)
        {
            var sourceOffset = i * stride;
            if (!FrustumCuller3D.IntersectsLocalBounds(batch.Mesh.LocalBounds, data, sourceOffset, frustum))
            {
                continue;
            }

            if (visible != i)
            {
                var targetOffset = visible * stride;
                var required = targetOffset + stride;
                batch.EnsureVisibleDataCapacity(required);
                if (!compacted && targetOffset > 0)
                {
                    Array.Copy(data, 0, batch.VisibleData, 0, targetOffset);
                }

                compacted = true;
                Array.Copy(data, sourceOffset, batch.VisibleData, targetOffset, stride);
            }

            visible++;
        }

        if (visible == count)
        {
            return count;
        }

        var culled = count - visible;
        if (visible == 0)
        {
            stats.CulledObjectCount += culled;
            return 0;
        }

        if (count < RetainedOrdinaryCullMinInstances || culled / (float)System.Math.Max(1, count) < RetainedOrdinaryCullMinCulledRatio)
        {
            return count;
        }

        var floatCount = visible * stride;
        if (!compacted)
        {
            batch.EnsureVisibleDataCapacity(floatCount);
            Array.Copy(data, 0, batch.VisibleData, 0, floatCount);
        }

        if (batch.CulledInstanceBuffer == 0)
        {
            batch.CulledInstanceBuffer = GenRequiredBuffer(gl, $"batch:{batch.MeshKey}:culled-instances");
        }

        gl.BindBuffer(GlArrayBuffer, batch.CulledInstanceBuffer);
        UploadFloats(gl, GlArrayBuffer, batch.VisibleData, floatCount, GlDynamicDraw);
        stats.CulledObjectCount += culled;
        stats.InstanceBufferUploads++;
        stats.InstanceUploadBytes += floatCount * sizeof(float);
        instanceBuffer = batch.CulledInstanceBuffer;
        return visible;
    }

    private void DrawParticleBatchInstanced(GlInterface gl, ParticleBatchData batch, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;
        RequireInstancedDrawPath("particles");

        ConfigureForwardBlendState(gl, batch.Transparent);
        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
        var resource = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, batch.Billboard ? 1f : 0f);
        BindMeshAttributes(gl, resource);
        EnsureBatchInstanceBuffer(gl, batch, stats);
        UploadClassicMaterial(gl, batch.Material, stats);
        var validate = BeginInstancedDrawValidation(batch.MeshKey, "particle");
        if (!PrepareValidatedInstancedDrawState(gl, resource, _meshProgram, batch.MeshKey, "particle", validate))
            throw CreateInstancingFailure("particles");
        gl.BindBuffer(GlArrayBuffer, batch.InstanceBuffer);
        if (batch.Billboard) EnableParticleBillboardInstanceAttributes(gl);
        else EnableInstanceAttributes(gl);
        if (!ValidateInstancedOperation("instance attribute setup", batch.MeshKey, "particle", validate))
        {
            DisableInstanceAttributes();
            throw CreateInstancingFailure("particles");
        }
        if (!TryDrawElementsInstanced(resource.IndexCount, resource.IndexType, batch.InstanceCount, batch.MeshKey, "particle", validate))
        {
            DisableInstanceAttributes();
            throw CreateInstancingFailure("particles");
        }

        DisableInstanceAttributes();
        stats.DrawCallCount++;
        stats.EstimatedDrawCallCount++;
        stats.InstancedBatchCount++;
    }

    private void EnsureBatchInstanceBuffer(GlInterface gl, InstanceBatchData batch, RenderStats stats)
    {
        if (batch.InstanceBuffer == 0)
        {
            batch.InstanceBuffer = GenRequiredBuffer(gl, $"batch:{batch.MeshKey}:instances");
            batch.UploadedVersion = -1;
            batch.UploadedCapacityFloats = 0;
        }

        if (batch.UploadedVersion == batch.DataVersion && batch.UploadedCapacityFloats >= batch.FloatCount)
        {
            return;
        }

        gl.BindBuffer(GlArrayBuffer, batch.InstanceBuffer);
        var dirtyRatio = batch.InstanceCount == 0
            ? 1f
            : batch.DirtyInstanceOffsets.Count / (float)batch.InstanceCount;
        var sparseRangeCount = batch.CanUploadDirtyInstanceRanges
            ? PrepareDirtyInstanceRanges(batch.DirtyInstanceOffsets)
            : int.MaxValue;
        if (_bufferSubData is not null &&
            batch.CanUploadDirtyInstanceRanges &&
            dirtyRatio <= SparseInstanceUploadMaxDirtyRatio &&
            sparseRangeCount <= MaxSparseInstanceUploadRanges &&
            batch.UploadedCapacityFloats >= batch.FloatCount &&
            batch.FloatCount > 0)
        {
            UploadDirtyInstanceRanges(batch, stats);
        }
        else
        {
            // glBufferData replaces (orphans) the store. Rewriting most/all of a buffer with
            // glBufferSubData forces the CPU to wait for the previous frame's GPU reads on
            // native drivers and was the source of periodic 20 FPS hitches under animation.
            UploadFloats(gl, GlArrayBuffer, batch.Data, batch.FloatCount, GlDynamicDraw);
            batch.UploadedCapacityFloats = batch.FloatCount;
            stats.InstanceBufferUploads++;
            stats.InstanceUploadBytes += batch.FloatCount * sizeof(float);
        }

        batch.UploadedVersion = batch.DataVersion;
    }

    private void UploadDirtyInstanceRanges(InstanceBatchData batch, RenderStats stats)
    {
        var dirty = batch.DirtyInstanceOffsets;
        if (dirty.Count == 0) return;

        var uploadedFloats = 0;
        var rangeStart = dirty[0];
        var previous = rangeStart;
        for (var i = 1; i <= dirty.Count; i++)
        {
            if (i < dirty.Count && dirty[i] == previous + 1)
            {
                previous = dirty[i];
                continue;
            }

            var floatOffset = rangeStart * batch.FloatStride;
            var floatCount = (previous - rangeStart + 1) * batch.FloatStride;
            UploadFloatsSubData(GlArrayBuffer, floatOffset * sizeof(float), batch.Data, floatOffset, floatCount);
            uploadedFloats += floatCount;

            if (i < dirty.Count)
            {
                rangeStart = dirty[i];
                previous = rangeStart;
            }
        }

        stats.InstanceBufferUploads++;
        stats.InstanceBufferSubDataUploads++;
        stats.InstanceUploadBytes += uploadedFloats * sizeof(float);
        stats.TransformUploadBytes += uploadedFloats * sizeof(float);
    }

    private static int PrepareDirtyInstanceRanges(List<int> dirty)
    {
        if (dirty.Count == 0) return 0;
        dirty.Sort();
        var ranges = 1;
        var previous = dirty[0];
        for (var i = 1; i < dirty.Count; i++)
        {
            var current = dirty[i];
            if (current == previous) continue;
            if (current != previous + 1 && ++ranges > MaxSparseInstanceUploadRanges) return ranges;
            previous = current;
        }
        return ranges;
    }

    private void BindMeshAttributes(GlInterface gl, MeshGpuResource resource)
    {
        if (resource.VertexArray == 0) throw new InvalidOperationException("OpenGL mesh has no RHI vertex-array resource.");
        ForceBindVertexArray(resource.VertexArray);
        // VAO remains mandatory, but no state inside it is trusted after the host may have
        // rebound the context. Rebuild the compact static layout once per mesh per graphics
        // callback. Generic constants are not VAO state, so missing-stream defaults are
        // restored on every bind.
        if (resource.StaticLayoutCallbackSerial != _graphicsCallbackSerial)
        {
            ConfigureMeshStaticAttributes(gl, resource);
            resource.StaticLayoutCallbackSerial = _graphicsCallbackSerial;
        }
        else
        {
            gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
        }
        ApplyMissingMeshAttributeDefaults(resource.Layout);
        _lastMeshAttributeResource = resource;
    }

    private void ConfigureMeshStaticAttributes(GlInterface gl, MeshGpuResource resource)
    {
        var layout = resource.Layout ?? throw new InvalidOperationException($"OpenGL mesh '{resource.LogicalKey}' has no vertex layout.");
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        ConfigureMeshAttribute(gl, layout, VertexAttributeKind3D.Position, _meshPositionLocation, required: true, defaultValue: Vector4.Zero);
        ConfigureMeshAttribute(gl, layout, VertexAttributeKind3D.Normal, _meshNormalLocation, required: true, defaultValue: new Vector4(0f, 1f, 0f, 0f));
        ConfigureMeshAttribute(gl, layout, VertexAttributeKind3D.TexCoord0, _meshTexCoordLocation, required: false, defaultValue: new Vector4(0f, 0f, 0f, 1f));
        ConfigureMeshAttribute(gl, layout, VertexAttributeKind3D.Tangent, _meshTangentLocation, required: false, defaultValue: new Vector4(1f, 0f, 0f, 1f));
        ConfigureMeshAttribute(gl, layout, VertexAttributeKind3D.Color0, _meshVertexColorLocation, required: false, defaultValue: Vector4.One);
        ConfigureMeshAttribute(gl, layout, VertexAttributeKind3D.MaterialSlot, _meshMaterialSlotLocation, required: false, defaultValue: new Vector4(0f, 0f, 0f, 1f));
        ConfigureMeshAttribute(gl, layout, VertexAttributeKind3D.BoneIndices, _meshBoneIndicesLocation, required: false, defaultValue: Vector4.Zero);
        ConfigureMeshAttribute(gl, layout, VertexAttributeKind3D.BoneWeights, _meshBoneWeightsLocation, required: false, defaultValue: new Vector4(1f, 0f, 0f, 0f));
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
    }

    private void ConfigureMeshAttribute(
        GlInterface gl,
        VertexLayout3D layout,
        VertexAttributeKind3D kind,
        int location,
        bool required,
        Vector4 defaultValue)
    {
        if (location < 0) return;
        var descriptor = layout.Find(kind);
        if (descriptor.HasValue)
        {
            var attribute = descriptor.Value;
            gl.EnableVertexAttribArray(location);
            gl.VertexAttribPointer(
                location,
                attribute.ComponentCount,
                ResolveGlVertexType(attribute.Format),
                attribute.Normalized ? 1 : 0,
                layout.StrideBytes,
                new IntPtr(attribute.OffsetBytes));
            ResetDivisor(location);
            return;
        }
        if (required) throw new InvalidOperationException($"Vertex layout '{layout}' is missing required attribute '{kind}'.");
        _disableVertexAttribArray?.Invoke(location);
        ResetDivisor(location);
        SetVertexAttributeConstant(location, defaultValue);
    }

    private void ApplyMissingMeshAttributeDefaults(VertexLayout3D? layout)
    {
        if (layout is null) return;
        Apply(VertexAttributeKind3D.TexCoord0, _meshTexCoordLocation, new Vector4(0f, 0f, 0f, 1f));
        Apply(VertexAttributeKind3D.Tangent, _meshTangentLocation, new Vector4(1f, 0f, 0f, 1f));
        Apply(VertexAttributeKind3D.Color0, _meshVertexColorLocation, Vector4.One);
        Apply(VertexAttributeKind3D.MaterialSlot, _meshMaterialSlotLocation, new Vector4(0f, 0f, 0f, 1f));
        Apply(VertexAttributeKind3D.BoneIndices, _meshBoneIndicesLocation, Vector4.Zero);
        Apply(VertexAttributeKind3D.BoneWeights, _meshBoneWeightsLocation, new Vector4(1f, 0f, 0f, 0f));

        void Apply(VertexAttributeKind3D kind, int location, Vector4 value)
        {
            if (location >= 0 && !layout.Has(kind)) SetVertexAttributeConstant(location, value);
        }
    }

    private void SetVertexAttributeConstant(int location, Vector4 value)
    {
        if (_vertexAttrib4f is null)
            throw new InvalidOperationException("OpenGL glVertexAttrib4f is required for compact layouts with omitted optional streams.");
        _vertexAttrib4f(location, value.X, value.Y, value.Z, value.W);
    }

    private static int ResolveGlVertexType(VertexAttributeFormat3D format)
        => format switch
        {
            VertexAttributeFormat3D.Float1 or VertexAttributeFormat3D.Float2 or VertexAttributeFormat3D.Float3 or VertexAttributeFormat3D.Float4 => GlFloat,
            VertexAttributeFormat3D.Half2 => GlHalfFloat,
            VertexAttributeFormat3D.SNorm16x4 => GlShort,
            VertexAttributeFormat3D.UNorm8x4 => GlUnsignedByte,
            VertexAttributeFormat3D.UInt16x1 or VertexAttributeFormat3D.UInt16x4 or VertexAttributeFormat3D.UNorm16x4 => GlUnsignedShort,
            _ => throw new NotSupportedException($"OpenGL vertex format '{format}' is not supported by the mesh pipeline.")
        };

    private void ConfigureMeshVertexArray(GlInterface gl, MeshGpuResource resource)
    {
        if (resource.VertexArray == 0)
            throw new InvalidOperationException("OpenGL failed to create the required mesh vertex-array resource.");
        BindVertexArray(resource.VertexArray);
        ConfigureMeshStaticAttributes(gl, resource);
        BindVertexArray(0);
        _lastMeshAttributeResource = null;
    }

    private void ConfigureStaticUtilityVertexArrays(GlInterface gl)
    {
        _skyboxVertexArray = CreateVertexArray();
        BindVertexArray(_skyboxVertexArray);
        gl.BindBuffer(GlArrayBuffer, _skyboxVertexBuffer);
        gl.EnableVertexAttribArray(_skyboxPositionLocation);
        gl.VertexAttribPointer(_skyboxPositionLocation, 2, GlFloat, 0, sizeof(float) * 2, IntPtr.Zero);
        gl.BindBuffer(GlElementArrayBuffer, _skyboxIndexBuffer);

        _controlVertexArray = CreateVertexArray();
        BindVertexArray(_controlVertexArray);
        gl.BindBuffer(GlArrayBuffer, _controlVertexBuffer);
        gl.EnableVertexAttribArray(_texturePositionLocation);
        gl.VertexAttribPointer(_texturePositionLocation, 3, GlFloat, 0, sizeof(float) * 5, IntPtr.Zero);
        gl.EnableVertexAttribArray(_textureUvLocation);
        gl.VertexAttribPointer(_textureUvLocation, 2, GlFloat, 0, sizeof(float) * 5, new IntPtr(sizeof(float) * 3));
        gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);

        BindVertexArray(0);
    }

    private void BindVertexArray(int vertexArray)
    {
        if (!_supportsVertexArrays || _bindVertexArray is null) throw new InvalidOperationException("Required OpenGL vertex-array support is unavailable.");
        if (_boundVertexArray == vertexArray) return;
        _bindVertexArray(vertexArray);
        _boundVertexArray = vertexArray;
        if (vertexArray == 0)
        {
            _lastMeshAttributeResource = null;
        }
    }

    private void ForceBindVertexArray(int vertexArray)
    {
        if (!_supportsVertexArrays || _bindVertexArray is null) throw new InvalidOperationException("Required OpenGL vertex-array support is unavailable.");
        _bindVertexArray(vertexArray);
        _boundVertexArray = vertexArray;
        if (vertexArray == 0)
        {
            _lastMeshAttributeResource = null;
        }
    }

    private void ResolveInstancingEntryPoints(GlInterface gl, GlContextVersionInfo context, string extensions)
    {
        _vertexAttribDivisor = null;
        _drawElementsInstanced = null;
        _instancingApi = "unavailable";

        var coreSupported = context.IsKnown &&
            (context.IsOpenGlEs
                ? context.Major >= 3
                : context.Major > 3 || context.Major == 3 && context.Minor >= 3);
        if (coreSupported && TrySelectInstancingApi(gl, "core", "glVertexAttribDivisor", "glDrawElementsInstanced")) return;

        if (HasExtension(extensions, "GL_ANGLE_instanced_arrays") &&
            TrySelectInstancingApi(gl, "ANGLE", "glVertexAttribDivisorANGLE", "glDrawElementsInstancedANGLE")) return;
        if (HasExtension(extensions, "GL_EXT_instanced_arrays") &&
            TrySelectInstancingApi(gl, "EXT", "glVertexAttribDivisorEXT", "glDrawElementsInstancedEXT")) return;
        if (HasExtension(extensions, "GL_NV_instanced_arrays") && HasExtension(extensions, "GL_NV_draw_instanced") &&
            TrySelectInstancingApi(gl, "NV", "glVertexAttribDivisorNV", "glDrawElementsInstancedNV")) return;
        if (HasExtension(extensions, "GL_ARB_instanced_arrays") && HasExtension(extensions, "GL_ARB_draw_instanced") &&
            TrySelectInstancingApi(gl, "ARB", "glVertexAttribDivisorARB", "glDrawElementsInstancedARB")) return;
        if (HasExtension(extensions, "GL_EXT_instanced_arrays") && HasExtension(extensions, "GL_EXT_draw_instanced") &&
            TrySelectInstancingApi(gl, "EXT", "glVertexAttribDivisorEXT", "glDrawElementsInstancedEXT")) return;

        // Desktop OpenGL 3.1/3.2 exposes instancing through the paired ARB
        // extensions. Core-profile extension enumeration requires glGetStringi,
        // so the coherent pair itself is the authoritative capability here.
        if (!context.IsOpenGlEs && context.IsKnown && context.Major >= 3 &&
            TrySelectInstancingApi(gl, "ARB", "glVertexAttribDivisorARB", "glDrawElementsInstancedARB")) return;

        if (!context.IsKnown)
        {
            TrySelectInstancingApi(gl, "core-unversioned", "glVertexAttribDivisor", "glDrawElementsInstanced");
        }
    }

    private bool TrySelectInstancingApi(GlInterface gl, string label, string divisorName, string drawName)
    {
        var divisor = LoadDelegate<GlVertexAttribDivisorDelegate>(gl, divisorName);
        var draw = LoadDelegate<GlDrawElementsInstancedDelegate>(gl, drawName);
        if (divisor is null || draw is null) return false;
        _vertexAttribDivisor = divisor;
        _drawElementsInstanced = draw;
        _instancingApi = label;
        return true;
    }

    private void ResolveVertexArrayEntryPoints(GlInterface gl, GlContextVersionInfo context, string extensions)
    {
        _genVertexArrays = null;
        _bindVertexArray = null;
        _deleteVertexArrays = null;
        _vertexArrayApi = "unavailable";

        var coreSupported = context.IsKnown &&
            (context.IsOpenGlEs ? context.Major >= 3 : context.Major >= 3);
        if (coreSupported && TrySelectVertexArrayApi(gl, "core", "glGenVertexArrays", "glBindVertexArray", "glDeleteVertexArrays")) return;

        if (HasExtension(extensions, "GL_OES_vertex_array_object") &&
            TrySelectVertexArrayApi(gl, "OES", "glGenVertexArraysOES", "glBindVertexArrayOES", "glDeleteVertexArraysOES")) return;
        if (HasExtension(extensions, "GL_APPLE_vertex_array_object") &&
            TrySelectVertexArrayApi(gl, "APPLE", "glGenVertexArraysAPPLE", "glBindVertexArrayAPPLE", "glDeleteVertexArraysAPPLE")) return;
        if (HasExtension(extensions, "GL_ARB_vertex_array_object") &&
            TrySelectVertexArrayApi(gl, "ARB", "glGenVertexArrays", "glBindVertexArray", "glDeleteVertexArrays")) return;

        if (!context.IsKnown)
        {
            TrySelectVertexArrayApi(gl, "core-unversioned", "glGenVertexArrays", "glBindVertexArray", "glDeleteVertexArrays");
        }
    }

    private bool TrySelectVertexArrayApi(GlInterface gl, string label, string genName, string bindName, string deleteName)
    {
        var gen = LoadDelegate<GlGenVertexArraysDelegate>(gl, genName);
        var bind = LoadDelegate<GlBindVertexArrayDelegate>(gl, bindName);
        var delete = LoadDelegate<GlDeleteVertexArraysDelegate>(gl, deleteName);
        if (gen is null || bind is null || delete is null) return false;

        var arrays = new int[1];
        DrainGlErrors();
        try
        {
            gen(1, arrays);
            var generateError = DrainGlErrors();
            if (generateError != GlNoError || arrays[0] == 0)
            {
                EngineLog3D.Warning(
                    "OpenGL.Capabilities",
                    $"Rejected VAO API {label}: generation returned {DescribeGlError(generateError)}, handle={arrays[0]}.");
                return false;
            }

            bind(arrays[0]);
            var bindError = DrainGlErrors();
            bind(0);
            var unbindError = DrainGlErrors();
            if (bindError != GlNoError || unbindError != GlNoError)
            {
                EngineLog3D.Warning(
                    "OpenGL.Capabilities",
                    $"Rejected VAO API {label}: bind={DescribeGlError(bindError)}, unbind={DescribeGlError(unbindError)}.");
                return false;
            }

            _genVertexArrays = gen;
            _bindVertexArray = bind;
            _deleteVertexArrays = delete;
            _vertexArrayApi = label;
            _boundVertexArray = 0;
            return true;
        }
        finally
        {
            if (arrays[0] != 0)
            {
                try { delete(1, arrays); }
                catch { /* The candidate is rejected by the capability result above. */ }
                DrainGlErrors();
            }
        }
    }

    private static GlContextVersionInfo ParseGlContextVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return default;
        var esIndex = version.IndexOf("OpenGL ES", StringComparison.OrdinalIgnoreCase);
        var isOpenGlEs = esIndex >= 0;
        var start = isOpenGlEs ? esIndex + "OpenGL ES".Length : 0;
        while (start < version.Length && !char.IsDigit(version[start])) start++;
        if (start >= version.Length) return new GlContextVersionInfo(isOpenGlEs, 0, 0, false);

        var major = 0;
        while (start < version.Length && char.IsDigit(version[start])) major = checked(major * 10 + version[start++] - '0');
        if (start >= version.Length || version[start] != '.') return new GlContextVersionInfo(isOpenGlEs, major, 0, true);
        start++;
        var minor = 0;
        while (start < version.Length && char.IsDigit(version[start])) minor = checked(minor * 10 + version[start++] - '0');
        return new GlContextVersionInfo(isOpenGlEs, major, minor, true);
    }

    private static bool HasExtension(string extensions, string extension)
    {
        if (string.IsNullOrWhiteSpace(extensions) || string.IsNullOrWhiteSpace(extension)) return false;
        return (" " + extensions + " ").Contains(" " + extension + " ", StringComparison.Ordinal);
    }


    private bool ProbeBoneTextureSkinningSupport(GlInterface gl)
    {
        _gpuSkinTextureBoneLimit = 0;
        var uniformsAvailable = _meshSkinningEnabledLocation >= 0 &&
                                _meshBoneTextureLocation >= 0 &&
                                _meshBoneTextureHeightLocation >= 0;
        if (!uniformsAvailable || _getIntegerv is null) return false;

        var vertexTextureUnits = GetInteger(0x8B4C); // GL_MAX_VERTEX_TEXTURE_IMAGE_UNITS
        var maxTextureSize = GetInteger(0x0D33); // GL_MAX_TEXTURE_SIZE
        if (vertexTextureUnits <= 0 || maxTextureSize < 1) return false;

        var queuedError = DrainGlErrors();
        if (queuedError != GlNoError)
        {
            EngineLog3D.Warning(
                "OpenGL.Skinning",
                $"Queued GL error {DescribeGlError(queuedError)} was isolated before the float-texture capability probe.");
        }

        var texture = 0;
        try
        {
            texture = gl.GenTexture();
            gl.BindTexture(GlTexture2D, texture);
            gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlNearest);
            gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlNearest);
            gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
            gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
            unsafe
            {
                float* probe = stackalloc float[16];
                for (var i = 0; i < 16; i++) probe[i] = 0f;
                probe[0] = probe[5] = probe[10] = probe[15] = 1f;
                gl.TexImage2D(GlTexture2D, 0, GlRgba, 4, 1, 0, GlRgba, GlFloat, (IntPtr)probe);
            }

            var error = DrainGlErrors();
            if (error != GlNoError)
            {
                EngineLog3D.Information(
                    "OpenGL.Skinning",
                    $"GPU bone texture probe returned {DescribeGlError(error)}; float-texture skinning is unavailable for this context.");
                return false;
            }
            _gpuSkinTextureBoneLimit = Math.Min(MaxGpuSkinTextureBones, maxTextureSize);
            return _gpuSkinTextureBoneLimit > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (texture != 0) gl.DeleteTexture(texture);
            gl.BindTexture(GlTexture2D, 0);
        }
    }

    private int GetInteger(int pname)
    {
        if (_getIntegerv is null) return 0;
        unsafe
        {
            var value = 0;
            _getIntegerv(pname, (IntPtr)(&value));
            return value;
        }
    }

    private RhiDeviceCapabilities3D BuildRhiCapabilities()
    {
        var features = RhiDeviceCapabilities3D.RequiredRasterFeatures;
        if (_supportsBoneTextureSkinning)
        {
            features |= RhiFeature3D.VertexTextureFetch | RhiFeature3D.FloatTextures;
        }
        if (GpuTimerQueriesInitialized) features |= RhiFeature3D.TimerQueries;

        var vendor = GetString(GlVendor);
        var renderer = GetString(GlRenderer);
        var adapter = string.IsNullOrWhiteSpace(vendor) ? renderer : string.IsNullOrWhiteSpace(renderer) ? vendor : vendor + " " + renderer;
        return new RhiDeviceCapabilities3D(
            RhiBackendApi3D.OpenGl,
            adapter,
            GetString(GlVersion),
            features,
            new RhiDeviceLimits3D(
                GetInteger(0x0D33), // GL_MAX_TEXTURE_SIZE
                GetInteger(0x8B4D), // GL_MAX_COMBINED_TEXTURE_IMAGE_UNITS
                GetInteger(0x8B4C), // GL_MAX_VERTEX_TEXTURE_IMAGE_UNITS
                GetInteger(0x8869), // GL_MAX_VERTEX_ATTRIBS
                GetInteger(0x84E8), // GL_MAX_RENDERBUFFER_SIZE
                GetInteger(0x8D57))); // GL_MAX_SAMPLES
    }

    private void RegisterStaticRhiResources()
    {
        var resources = _rhiDevice?.Resources ?? throw new InvalidOperationException("OpenGL RHI device is not initialized.");
        resources.RegisterBuffer("utility:skybox:vertices", new RhiBufferDescriptor3D(8L * sizeof(float), RhiBufferUsage3D.Vertex, sizeof(float) * 2), 1);
        resources.RegisterBuffer("utility:skybox:indices", new RhiBufferDescriptor3D(6L * sizeof(ushort), RhiBufferUsage3D.Index, sizeof(ushort)), 1);
        resources.RegisterBuffer("utility:control:indices", new RhiBufferDescriptor3D(6L * sizeof(ushort), RhiBufferUsage3D.Index, sizeof(ushort)), 1);
        resources.RegisterAllocation("utility:skybox:vao", RhiResourceKind3D.VertexArray, 0, 1);
        resources.RegisterAllocation("utility:control:vao", RhiResourceKind3D.VertexArray, 0, 1);
        resources.RegisterAllocation("pipeline:mesh", RhiResourceKind3D.Pipeline, 0, 1);
        resources.RegisterAllocation("pipeline:skybox", RhiResourceKind3D.Pipeline, 0, 1);
        resources.RegisterAllocation("pipeline:textured", RhiResourceKind3D.Pipeline, 0, 1);
    }

    private string GetString(int name)
    {
        if (_getString is null) return "unknown";
        var pointer = _getString(name);
        return pointer == IntPtr.Zero ? "unknown" : Marshal.PtrToStringAnsi(pointer) ?? "unknown";
    }

    private bool GpuTimerQueriesInitialized
    {
        get
        {
            for (var i = 0; i < _gpuTimerQueries.Length; i++) if (_gpuTimerQueries[i] == 0) return false;
            return true;
        }
    }

    private void InitializeGpuTimerQueries()
    {
        Array.Clear(_gpuTimerQueries, 0, _gpuTimerQueries.Length);
        Array.Clear(_gpuTimerPending, 0, _gpuTimerPending.Length);
        _gpuTimerActiveSlot = -1;
        _gpuTimerNextSlot = 0;
        _lastGpuFrameMilliseconds = double.NaN;
        if (_genQueries is null || _deleteQueries is null || _beginQuery is null || _endQuery is null ||
            _getQueryObjectiv is null || _getQueryObjectui64v is null || _getError is null)
        {
            EngineLog3D.Information("OpenGL", "GPU timer queries are unavailable; RHI timing telemetry is disabled for this context.");
            return;
        }

        try
        {
            _genQueries(_gpuTimerQueries.Length, _gpuTimerQueries);
            if (!GpuTimerQueriesInitialized)
            {
                DeleteGpuTimerQueries();
                EngineLog3D.Warning("OpenGL", "GPU timer query allocation returned an invalid native handle; timing telemetry is disabled.");
            }
            else if (!ValidateGpuTimerQueryTarget())
            {
                DeleteGpuTimerQueries();
            }
        }
        catch (Exception exception)
        {
            DeleteGpuTimerQueries();
            EngineLog3D.Warning("OpenGL", "GPU timer query initialization failed; timing telemetry is disabled.", exception);
        }
    }

    private bool ValidateGpuTimerQueryTarget()
    {
        if (!GpuTimerQueriesInitialized || _beginQuery is null || _endQuery is null || _getError is null)
        {
            return false;
        }

        var queuedError = DrainGlErrors();
        if (queuedError != GlNoError)
        {
            EngineLog3D.Warning(
                "OpenGL.TimerQuery",
                $"Queued GL error {DescribeGlError(queuedError)} was isolated before the timer-query capability probe.");
        }

        var query = _gpuTimerQueries[0];
        _beginQuery(GlTimeElapsed, query);
        var beginError = DrainGlErrors();
        if (beginError != GlNoError)
        {
            EngineLog3D.Information(
                "OpenGL.TimerQuery",
                $"GL_TIME_ELAPSED is not supported by this context ({DescribeGlError(beginError)} at glBeginQuery); GPU timing telemetry is disabled.");
            return false;
        }

        _endQuery(GlTimeElapsed);
        var endError = DrainGlErrors();
        if (endError != GlNoError)
        {
            EngineLog3D.Warning(
                "OpenGL.TimerQuery",
                $"GL_TIME_ELAPSED probe failed at glEndQuery with {DescribeGlError(endError)}; GPU timing telemetry is disabled.");
            return false;
        }

        _gpuTimerPending[0] = true;
        _gpuTimerNextSlot = 1;
        EngineLog3D.Information("OpenGL.TimerQuery", "GL_TIME_ELAPSED capability probe succeeded; asynchronous GPU timing is enabled.");
        return true;
    }

    private void BeginGpuFrameTimer()
    {
        ResolveGpuFrameTimers();
        if (!GpuTimerQueriesInitialized || _beginQuery is null || _gpuTimerActiveSlot >= 0) return;
        for (var attempt = 0; attempt < _gpuTimerQueries.Length; attempt++)
        {
            var slot = (_gpuTimerNextSlot + attempt) % _gpuTimerQueries.Length;
            if (_gpuTimerPending[slot]) continue;
            try
            {
                _beginQuery(GlTimeElapsed, _gpuTimerQueries[slot]);
                _gpuTimerActiveSlot = slot;
                _gpuTimerNextSlot = (slot + 1) % _gpuTimerQueries.Length;
            }
            catch (Exception exception)
            {
                DisableGpuTimerQueries("begin query failed", exception);
            }
            return;
        }
    }

    private void EndGpuFrameTimer()
    {
        var slot = _gpuTimerActiveSlot;
        if (slot < 0) return;
        _gpuTimerActiveSlot = -1;
        try
        {
            _endQuery?.Invoke(GlTimeElapsed);
            _gpuTimerPending[slot] = true;
        }
        catch (Exception exception)
        {
            DisableGpuTimerQueries("end query failed", exception);
        }
    }

    private void ResolveGpuFrameTimers()
    {
        if (!GpuTimerQueriesInitialized || _getQueryObjectiv is null || _getQueryObjectui64v is null) return;
        for (var slot = 0; slot < _gpuTimerQueries.Length; slot++)
        {
            if (!_gpuTimerPending[slot]) continue;
            try
            {
                _getQueryObjectiv(_gpuTimerQueries[slot], GlQueryResultAvailable, out var available);
                if (available == 0) continue;
                _getQueryObjectui64v(_gpuTimerQueries[slot], GlQueryResult, out var nanoseconds);
                _gpuTimerPending[slot] = false;
                _lastGpuFrameMilliseconds = nanoseconds / 1_000_000d;
            }
            catch (Exception exception)
            {
                DisableGpuTimerQueries("query result read failed", exception);
                return;
            }
        }
    }

    private void DisableGpuTimerQueries(string reason, Exception? exception = null)
    {
        DeleteGpuTimerQueries();
        EngineLog3D.Warning("OpenGL", $"GPU timer telemetry disabled because {reason}.", exception);
    }

    private void DeleteGpuTimerQueries()
    {
        var hasAllocatedQuery = false;
        for (var i = 0; i < _gpuTimerQueries.Length; i++)
        {
            if (_gpuTimerQueries[i] != 0) { hasAllocatedQuery = true; break; }
        }
        if (hasAllocatedQuery && _deleteQueries is not null)
        {
            try
            {
                _deleteQueries(_gpuTimerQueries.Length, _gpuTimerQueries);
            }
            catch (Exception exception)
            {
                EngineLog3D.Warning("OpenGL", "GPU timer-query deletion failed during telemetry shutdown.", exception);
            }
        }
        Array.Clear(_gpuTimerQueries, 0, _gpuTimerQueries.Length);
        Array.Clear(_gpuTimerPending, 0, _gpuTimerPending.Length);
        _gpuTimerActiveSlot = -1;
        _gpuTimerNextSlot = 0;
        _lastGpuFrameMilliseconds = double.NaN;
    }

    private int CreateVertexArray()
    {
        if (!_supportsVertexArrays || _genVertexArrays is null) throw new InvalidOperationException("Required OpenGL vertex-array support is unavailable.");
        var arrays = new int[1];
        _genVertexArrays(1, arrays);
        if (arrays[0] == 0) throw new InvalidOperationException("OpenGL failed to allocate a required vertex array.");
        return arrays[0];
    }

    private void DeleteVertexArray(int vertexArray)
    {
        if (vertexArray == 0 || _deleteVertexArrays is null) return;
        if (_boundVertexArray == vertexArray) BindVertexArray(0);
        _deleteVertexArrays(1, new[] { vertexArray });
    }

    private void EnableInstanceAttributes(GlInterface gl)
    {
        EnableInstanceAttribute(gl, _meshInstanceModel0Location, 4, InstanceByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel1Location, 4, InstanceByteStride, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _meshInstanceModel2Location, 4, InstanceByteStride, sizeof(float) * 8);
        EnableInstanceAttribute(gl, _meshInstanceModel3Location, 4, InstanceByteStride, sizeof(float) * 12);
        EnableInstanceAttribute(gl, _meshInstanceColorLocation, 4, InstanceByteStride, sizeof(float) * 16);
    }

    private void EnableParticleBillboardInstanceAttributes(GlInterface gl)
    {
        // Keep every active instance attribute backed by the compact particle buffer.
        // Leaving matrix columns 1-3 pointed at a previous batch buffer is legal on some
        // drivers, but it can render undefined data or trip validation on stricter GL ES
        // implementations because the shader still declares those attributes.
        EnableInstanceAttribute(gl, _meshInstanceModel0Location, 4, ParticleBillboardByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel1Location, 4, ParticleBillboardByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel2Location, 4, ParticleBillboardByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel3Location, 4, ParticleBillboardByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceColorLocation, 4, ParticleBillboardByteStride, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _meshInstanceStateColorLocation, 4, ParticleBillboardByteStride, sizeof(float) * 4);
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

    private void DisableInstanceAttributes()
    {
        DisableAttribute(_meshInstanceModel0Location);
        DisableAttribute(_meshInstanceModel1Location);
        DisableAttribute(_meshInstanceModel2Location);
        DisableAttribute(_meshInstanceModel3Location);
        DisableAttribute(_meshInstanceColorLocation);
        DisableAttribute(_meshInstanceStateColorLocation);
        ResetInstanceAttributeDivisors();
    }

    private void DisableAttribute(int location)
    {
        if (location < 0 || _disableVertexAttribArray is null) return;
        _disableVertexAttribArray(location);
    }

    private void DrawHighScaleLayers(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        BeginHighScaleDrawState(gl);
        var frame = plan.Frame;
        var scene = frame.Scene;
        var cameraPosition = scene.Camera.Position;
        _highScaleTransformBatchUploadsThisFrame = 0;
        for (var i = 0; i < plan.HighScaleLayers.Count; i++)
        {
            DrawHighScaleLayer(gl, scene, frame.ViewProjection, plan.HighScaleLayers[i], cameraPosition, stats);
        }
        EndHighScaleDrawState();
    }

    private void BeginHighScaleDrawState(GlInterface gl)
    {
        ConfigureForwardBlendState(gl, false);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 1f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 1f);
    }

    private void EndHighScaleDrawState()
    {
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        UploadFloat(_uniform1f, _meshUseDirectStateColorLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        ResetInstanceAttributeDivisors();
    }

    private void DrawHighScaleLayer(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, HighScaleInstanceLayer3D layer, Vector3 cameraPosition, RenderStats stats)
    {
        SceneHighScaleRenderPlanner3D.EnsureChunks(layer);

        if (SceneHighScaleRenderPlanner3D.ShouldUseAggregateLayerBatches(layer, scene.Performance))
        {
            DrawHighScaleAggregateLayer(gl, scene, layer, cameraPosition, scene.Performance, stats);
            layer.StateBuffer.ClearDirty();
            return;
        }

        var visibleChunks = layer.QueryVisibleChunks(viewProjection);
        stats.TotalChunkCount += layer.Chunks.Chunks.Count;
        var visibleChunkLimit = SceneHighScaleRenderPlanner3D.ResolveVisibleChunkLimit(scene.Performance, visibleChunks.Count);
        stats.VisibleChunkCount += visibleChunkLimit;

        for (var visibleChunkIndex = 0; visibleChunkIndex < visibleChunkLimit; visibleChunkIndex++)
        {
            var chunk = visibleChunks[visibleChunkIndex];
            var planStart = Stopwatch.GetTimestamp();
            var lodPlan = SceneHighScaleRenderPlanner3D.BuildChunkLodPlan(layer, chunk, cameraPosition, scene.Performance, stats, _highScaleLodPlanScratch);
            stats.HighScalePlanMilliseconds += GetElapsedMilliseconds(planStart);

            DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Detailed, lodPlan.Detailed, cameraPosition, scene.Performance, stats);
            DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Simplified, lodPlan.Simplified, cameraPosition, scene.Performance, stats);
            DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Proxy, lodPlan.Proxy, cameraPosition, scene.Performance, stats);
            DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Billboard, lodPlan.Billboard, cameraPosition, scene.Performance, stats);
            chunk.MarkClean();
        }

        layer.StateBuffer.ClearDirty();
    }


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
        var plan = SceneHighScaleRenderPlanner3D.BuildLayerLodPlan(layer, cameraPosition, performance, stats, _highScaleLodPlanScratch);
        stats.HighScalePlanMilliseconds += GetElapsedMilliseconds(planStart);

        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Detailed, plan.Detailed, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Simplified, plan.Simplified, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Proxy, plan.Proxy, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Billboard, plan.Billboard, cameraPosition, performance, stats);
    }

    private static bool UsesDirectHighScaleStateColor(HighScaleInstanceLayer3D layer, CompositePartTemplate3D part, ScenePerformanceOptions performance)
        => !(performance.EnableHighScalePaletteTexture && part.UsesVertexMaterialSlots && layer.ColorResolver is null);

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

        var parts = layer.Template.ResolveParts(lod);
        RequireInstancedDrawPath("aggregate high-scale meshes");

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var directStateColor = UsesDirectHighScaleStateColor(layer, part, performance);
            var buildStart = Stopwatch.GetTimestamp();
            var key = new HighScaleBatchKey(layer.Id, AggregateChunkKey, lod, directStateColor ? partIndex : -1);
            var batch = EnsureHighScaleGpuBatch(gl, layer, key, false, lod, instanceIndices, cameraPosition, performance, stats, part, directStateColor);
            stats.HighScaleBufferBuildMilliseconds += GetElapsedMilliseconds(buildStart);
            if (batch.InstanceCount == 0) continue;

            var meshResource = EnsureMeshResource(gl, part.Mesh.ResourceKey, part.Mesh.GeometryVersion, part.Mesh, stats);
            BindMeshAttributes(gl, meshResource);
            UploadHighScalePalette(gl, layer, part, performance, directStateColor, stats);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadHighScaleMaterial(gl, part.LightingMode);
            var validate = BeginInstancedDrawValidation(part.Mesh.ResourceKey, "highscale-aggregate");
            if (!PrepareValidatedInstancedDrawState(gl, meshResource, _meshProgram, part.Mesh.ResourceKey, "highscale-aggregate", validate))
                throw CreateInstancingFailure("aggregate high-scale meshes");
            EnableHighScaleInstanceAttributes(gl, batch);
            if (!ValidateInstancedOperation("instance attribute setup", part.Mesh.ResourceKey, "highscale-aggregate", validate))
            {
                DisableInstanceAttributes();
                throw CreateInstancingFailure("aggregate high-scale meshes");
            }
            var highScaleDrawn = TryDrawElementsInstanced(meshResource.IndexCount, meshResource.IndexType, batch.InstanceCount, part.Mesh.ResourceKey, "highscale-aggregate", validate);
            DisableInstanceAttributes();
            if (!highScaleDrawn)
            {
                throw CreateInstancingFailure("aggregate high-scale meshes");
            }

            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
            stats.InstancedBatchCount++;
            stats.VisibleMeshCount += batch.InstanceCount;
            stats.HighScaleVisiblePartInstanceCount += batch.InstanceCount;
            if (part.UsesVertexMaterialSlots) stats.BakedHighScalePartDraws++;
            stats.TriangleCount += (part.Mesh.Indices.Length / 3) * batch.InstanceCount;
        }
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

        var parts = layer.Template.ResolveParts(lod);
        RequireInstancedDrawPath("high-scale meshes");

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var directStateColor = UsesDirectHighScaleStateColor(layer, part, performance);
            var buildStart = Stopwatch.GetTimestamp();
            var key = new HighScaleBatchKey(layer.Id, chunk.Key, lod, directStateColor ? partIndex : -1);
            var batch = EnsureHighScaleGpuBatch(gl, layer, key, chunk.IsDirty, lod, instanceIndices, cameraPosition, performance, stats, part, directStateColor);
            stats.HighScaleBufferBuildMilliseconds += GetElapsedMilliseconds(buildStart);
            if (batch.InstanceCount == 0) continue;

            var meshResource = EnsureMeshResource(gl, part.Mesh.ResourceKey, part.Mesh.GeometryVersion, part.Mesh, stats);
            BindMeshAttributes(gl, meshResource);
            UploadHighScalePalette(gl, layer, part, performance, directStateColor, stats);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadHighScaleMaterial(gl, part.LightingMode);
            var validate = BeginInstancedDrawValidation(part.Mesh.ResourceKey, "highscale");
            if (!PrepareValidatedInstancedDrawState(gl, meshResource, _meshProgram, part.Mesh.ResourceKey, "highscale", validate))
                throw CreateInstancingFailure("high-scale meshes");
            EnableHighScaleInstanceAttributes(gl, batch);
            if (!ValidateInstancedOperation("instance attribute setup", part.Mesh.ResourceKey, "highscale", validate))
            {
                DisableInstanceAttributes();
                throw CreateInstancingFailure("high-scale meshes");
            }
            var highScaleDrawn = TryDrawElementsInstanced(meshResource.IndexCount, meshResource.IndexType, batch.InstanceCount, part.Mesh.ResourceKey, "highscale", validate);
            DisableInstanceAttributes();
            if (!highScaleDrawn)
            {
                throw CreateInstancingFailure("high-scale meshes");
            }

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
        RenderStats stats,
        CompositePartTemplate3D part,
        bool directStateColor)
    {
        if (!_highScaleGpuBatches.TryGetValue(key, out var batch))
        {
            batch = new HighScaleGpuBatchData
            {
                TransformBuffer = GenRequiredBuffer(gl, $"high-scale:{layer.Id}:transforms"),
                StateBuffer = GenRequiredBuffer(gl, $"high-scale:{layer.Id}:state")
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

            RebuildHighScaleGpuBatch(gl, layer, instanceIndices, cameraPosition, batch, fadeVersion, dynamicFadeState, stats, part, directStateColor);
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
                // Every transform in this retained batch was rewritten. Orphaning the store
                // avoids waiting for native GPU reads from the previous frame.
                UploadFloats(gl, GlArrayBuffer, batch.TransformData, batch.TransformFloatCount, GlDynamicDraw);
                batch.TransformBufferCapacityBytes = batch.TransformFloatCount * sizeof(float);
                stats.InstanceBufferUploads++;

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
            UpdateHighScaleStateBuffer(gl, layer, cameraPosition, batch, fadeVersion, dynamicFadeState, performance, stats, part, directStateColor);
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
        RenderStats stats,
        CompositePartTemplate3D part,
        bool directStateColor)
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
                ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState),
                directStateColor,
                directStateColor ? layer.ResolveColor(part, record) : default);
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
        RenderStats stats,
        CompositePartTemplate3D part,
        bool directStateColor)
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
                batch.WriteState(offset, record.MaterialVariantId, IsHighScaleVisible(record), ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState), directStateColor, directStateColor ? layer.ResolveColor(part, record) : default);
                batch.AddDirtyOffset(offset);
            }

            // Decide full-vs-partial per batch after we know how many dirty instances
            // actually belong to this batch. This is the core fix for 10k/50k telemetry.
            forceFullUpdate = batch.DirtyOffsetCount > System.Math.Max(32, batch.InstanceCount / 3);
            if (!forceFullUpdate && batch.DirtyOffsetCount > 0)
            {
                batch.SortDirtyOffsets();
                var mergeGap = System.Math.Max(0, performance.HighScalePartialStateMergeGap);
                forceFullUpdate = CountHighScaleDirtyRanges(batch, mergeGap) > MaxPartialHighScaleUploadRanges;
            }
        }

        if (forceFullUpdate)
        {
            for (var offset = 0; offset < batch.InstanceCount; offset++)
            {
                var instanceIndex = batch.GetInstanceIndexAt(offset);
                var record = layer.Instances[instanceIndex];
                batch.WriteState(offset, record.MaterialVariantId, IsHighScaleVisible(record), ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState), directStateColor, directStateColor ? layer.ResolveColor(part, record) : default);
            }

            gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
            UploadFloats(gl, GlArrayBuffer, batch.StateData, batch.StateFloatCount, GlDynamicDraw);
            batch.StateBufferCapacityBytes = batch.StateFloatCount * sizeof(float);
            stats.StateBufferUploads++;

            stats.StateUploadBytes += batch.StateFloatCount * sizeof(float);
        }
        else if (batch.DirtyOffsetCount > 0)
        {
            gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);

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

    private static int CountHighScaleDirtyRanges(HighScaleGpuBatchData batch, int mergeGap)
    {
        if (batch.DirtyOffsetCount == 0) return 0;
        var rangeCount = 1;
        var previous = batch.GetDirtyOffsetAt(0);
        for (var i = 1; i < batch.DirtyOffsetCount; i++)
        {
            var current = batch.GetDirtyOffsetAt(i);
            if (current <= previous + 1 + mergeGap)
            {
                previous = current;
                continue;
            }
            if (++rangeCount > MaxPartialHighScaleUploadRanges) return rangeCount;
            previous = current;
        }
        return rangeCount;
    }

    private void UploadHighScalePalette(GlInterface gl, HighScaleInstanceLayer3D layer, CompositePartTemplate3D part, ScenePerformanceOptions performance, bool directStateColor, RenderStats stats)
    {
        UploadFloat(_uniform1f, _meshUseDirectStateColorLocation, directStateColor ? 1f : 0f);
        if (!directStateColor && performance.EnableHighScalePaletteTexture && part.UsesVertexMaterialSlots && layer.ColorResolver is null)
        {
            var palette = EnsureHighScalePaletteTexture(gl, layer, part);
            UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 1f);
            gl.ActiveTexture(GlTexture0);
            gl.BindTexture(GlTexture2D, palette.TextureId);
            if (_meshPaletteTextureLocation >= 0) _uniform1i?.Invoke(_meshPaletteTextureLocation, 0);
            UploadFloat(_uniform1f, _meshPaletteWidthLocation, palette.Width);
            UploadFloat(_uniform1f, _meshPaletteHeightLocation, palette.Height);
            gl.ActiveTexture(GlTexture0);
            return;
        }

        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        if (directStateColor) return;
        var count = ResolveActiveVariantSlotCount(layer);
        for (var i = 0; i < count; i++)
        {
            UploadColor(_uniform4f, _meshVariantColorLocations[i], layer.Template.ResolveColor(part, i));
        }
    }

    private unsafe HighScalePaletteTextureResource EnsureHighScalePaletteTexture(GlInterface gl, HighScaleInstanceLayer3D layer, CompositePartTemplate3D part)
    {
        var variantCount = ResolveActiveVariantSlotCount(layer);
        var slotCount = System.Math.Clamp(part.MaterialSlotBaseColors.Count, 1, 64);
        var paletteVersion = ComputePaletteVersion(layer, part, variantCount, slotCount);
        var key = layer.Id + ":" + layer.Template.Id + ":" + part.Mesh.ResourceKey + ":" + part.Name;

        if (_highScalePaletteTextures.TryGetValue(key, out var cached) &&
            cached.Version == paletteVersion &&
            cached.Width == slotCount &&
            cached.Height == variantCount)
        {
            return cached;
        }

        if (cached is null)
        {
            cached = new HighScalePaletteTextureResource { TextureId = GenRequiredTexture(gl, $"high-scale:{layer.Id}:palette") };
            _highScalePaletteTextures[key] = cached;
        }

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

        fixed (byte* ptr = _paletteUploadBuffer)
        {
            gl.ActiveTexture(GlTexture0);
            gl.BindTexture(GlTexture2D, cached.TextureId);
            gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlNearest);
            gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlNearest);
            gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
            gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
            gl.TexImage2D(GlTexture2D, 0, GlRgba, slotCount, variantCount, 0, GlRgba, GlUnsignedByte, (IntPtr)ptr);
            gl.ActiveTexture(GlTexture0);
        }

        cached.Version = paletteVersion;
        cached.Width = slotCount;
        cached.Height = variantCount;
        return cached;
    }

    private static int ComputePaletteVersion(HighScaleInstanceLayer3D layer, CompositePartTemplate3D part, int variantCount, int slotCount)
    {
        unchecked
        {
            var hash = HashCode.Combine(layer.MaterialResolverVersion, layer.Instances.MaterialVersion, variantCount, slotCount, part.MaterialSlotBaseColors.Count);
            for (var variant = 0; variant < variantCount; variant++)
            {
                for (var slot = 0; slot < slotCount; slot++)
                {
                    var baseColor = slot < part.MaterialSlotBaseColors.Count ? part.MaterialSlotBaseColors[slot] : part.BaseColor;
                    var color = layer.Template.ResolveColor(slot, baseColor, variant);
                    hash = hash * 31 + color.GetHashCode();
                }
            }
            return hash == 0 ? 1 : hash;
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

    private void DrawSurfaceOverlays(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        var scene = plan.Frame.Scene;
        if (!scene.Debug.ShowWireframeOverlay && !scene.Debug.ShowSilhouetteOverlay) return;

        ApplyCullMode(gl, CullMode.None);

        var viewProjection = plan.Frame.ViewProjection;
        gl.UseProgram(_meshProgram);
        UploadMatrix(_uniformMatrix4fv, _meshViewProjLocation, viewProjection, _matrixUploadBuffer);
        UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        UploadFloat(_uniform1f, _meshLightingEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshNormalMapStrengthLocation, 0f);
        UploadVector4(_uniform4f, _meshPostProcessParamsLocation, Vector4.Zero);
        UploadVector4(_uniform4f, _meshSsaoParamsLocation, Vector4.Zero);

        for (var batchIndex = 0; batchIndex < plan.OrdinaryBatches.Count; batchIndex++)
        {
            var items = plan.OrdinaryBatches[batchIndex].Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var obj = item.Owner;
                var mesh = item.Mesh;
                if (mesh.RenderGeometry.WireframeIndexCount == 0) continue;
                if (!FrustumCuller3D.IntersectsLocalBounds(mesh.LocalBounds, item.Model, viewProjection)) continue;

                var resource = EnsureMeshResource(gl, mesh.ResourceKey, mesh.GeometryVersion, mesh, stats);
                BindMeshAttributes(gl, resource);
                EnsureWireframeResource(gl, resource, mesh.RenderGeometry, mesh.GeometryVersion, stats);
                gl.BindBuffer(GlElementArrayBuffer, resource.WireframeIndexBuffer);
                UploadMatrix(_uniformMatrix4fv, _meshModelLocation, item.Model, _matrixUploadBuffer);
                if (scene.Debug.ShowWireframeOverlay)
                {
                    UploadColor(_uniform4f, _meshColorLocation, new ColorRgba(0.02f, 0.02f, 0.02f, 0.95f));
                    gl.DrawElements(GlLines, resource.WireframeIndexCount, resource.WireframeIndexType, IntPtr.Zero);
                    stats.WireframeOverlayDrawCalls++;
                    stats.DrawCallCount++;
                }

                if (scene.Debug.ShowSilhouetteOverlay && (obj.IsEffectivelyHovered || obj.IsEffectivelySelected))
                {
                    UploadColor(_uniform4f, _meshColorLocation, obj.IsEffectivelySelected ? ColorRgba.White : new ColorRgba(1f, 0.85f, 0.25f, 1f));
                    gl.DrawElements(GlLines, resource.WireframeIndexCount, resource.WireframeIndexType, IntPtr.Zero);
                    stats.SilhouetteOverlayDrawCalls++;
                    stats.DrawCallCount++;
                }

                // Element-array binding is VAO state. Restore the triangle buffer so enabling
                // an overlay cannot make the next retained frame draw line indices as triangles.
                gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
            }
        }
    }

    private void DrawControlPlanes(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        var scene = plan.Frame.Scene;
        var viewProjection = plan.Frame.ViewProjection;
        var planes = _controlPlaneScratch;
        ControlPlaneRenderPlanner3D.Build(plan.Frame.Snapshot, scene.Camera, planes);
        if (planes.Count == 0) return;

        ApplyCullMode(gl, CullMode.None);

        BindVertexArray(_controlVertexArray);
        gl.Enable(GlBlend);
        _blendFunc?.Invoke(GlSrcAlpha, GlOneMinusSrcAlpha);
        _depthMask?.Invoke(0);
        gl.UseProgram(_texturedProgram);
        gl.ActiveTexture(GlTexture0);
        if (_textureSamplerLocation >= 0) _uniform1i?.Invoke(_textureSamplerLocation, 0);
        UploadMatrix(_uniformMatrix4fv, _textureViewProjLocation, viewProjection, _matrixUploadBuffer);

        for (var i = 0; i < planes.Count; i++)
        {
            var item = planes[i];
            var plane = item.Plane;
            var texture = EnsureControlTexture(gl, plane, stats);
            if (texture is null) continue;
            item.CopyCorners(_controlCornerScratch);
            BuildWorldControlVertices(_controlCornerScratch, _controlVertexData);
            gl.BindTexture(GlTexture2D, texture.TextureId);
            gl.BindBuffer(GlArrayBuffer, _controlVertexBuffer);
            UploadFloats(gl, GlArrayBuffer, _controlVertexData, _controlVertexData.Length, GlDynamicDraw);
            _rhiDevice?.Resources.RegisterBuffer(
                "utility:control:vertices",
                new RhiBufferDescriptor3D(_controlVertexData.LongLength * sizeof(float), RhiBufferUsage3D.Vertex | RhiBufferUsage3D.Dynamic, sizeof(float) * 5),
                _rhiDevice.FrameIndex);
            gl.DrawElements(GlTriangles, 6, GlUnsignedShort, IntPtr.Zero);
            stats.ControlPlaneCount++;
            stats.DrawCallCount++;
        }
        _depthMask?.Invoke(1);
        _disable?.Invoke(GlBlend);
    }

    private MeshGpuResource EnsureMeshResource(GlInterface gl, string id, long geometryVersion, Mesh3D mesh, RenderStats stats)
    {
        var geometry = mesh.RenderGeometry;
        const int uploadUsage = GlStaticDraw;
        if (_meshResources.TryGetValue(id, out var resource))
        {
            if (resource.GeometryVersion == geometryVersion) return resource;
            if (resource.VertexCount == geometry.VertexCount &&
                resource.IndexCount == geometry.IndexCount)
            {
                if (resource.WireframeIndexBuffer != 0)
                {
                    gl.DeleteBuffer(resource.WireframeIndexBuffer);
                    resource.WireframeIndexBuffer = 0;
                }
                UploadMeshResourceData(gl, resource, mesh, uploadUsage);
                ConfigureMeshVertexArray(gl, resource);
                UpdateMeshResourceCounters(resource, geometry, geometryVersion);
                RegisterMeshResources(id, geometry);
                AddMeshUploadStats(stats, geometry);
                return resource;
            }

            DisposeMeshResource(gl, id, resource);
        }

        resource = new MeshGpuResource
        {
            LogicalKey = id,
            GeometryVersion = geometryVersion,
            VertexArray = CreateVertexArray(),
            VertexBuffer = GenRequiredBuffer(gl, $"mesh:{id}:vertices"),
            IndexBuffer = GenRequiredBuffer(gl, $"mesh:{id}:indices")
        };
        UploadMeshResourceData(gl, resource, mesh, uploadUsage);
        ConfigureMeshVertexArray(gl, resource);
        UpdateMeshResourceCounters(resource, geometry, geometryVersion);
        RegisterMeshResources(id, geometry);
        _meshResources[id] = resource;
        AddMeshUploadStats(stats, geometry);
        return resource;
    }

    private void RegisterMeshResources(string id, RenderGeometry3D geometry)
    {
        var resources = _rhiDevice?.Resources ?? throw new InvalidOperationException("OpenGL RHI device is not initialized.");
        var vertexBytes = EstimateInterleavedVertexUploadBytes(geometry);
        resources.RegisterBuffer(
            $"mesh:{id}:vertex",
            new RhiBufferDescriptor3D(vertexBytes, RhiBufferUsage3D.Vertex, geometry.Layout.StrideBytes),
            geometry.GeometryVersion);
        resources.RegisterBuffer(
            $"mesh:{id}:index",
            new RhiBufferDescriptor3D(geometry.EstimatedIndexUploadBytes, RhiBufferUsage3D.Index, geometry.Indices.ElementSizeBytes),
            geometry.GeometryVersion);
        resources.Release($"mesh:{id}:wireframe-index", RhiResourceKind3D.Buffer);
        resources.RegisterAllocation($"mesh:{id}:vao-main", RhiResourceKind3D.VertexArray, 0, geometry.GeometryVersion);
    }

    private static void UpdateMeshResourceCounters(MeshGpuResource resource, RenderGeometry3D geometry, long geometryVersion)
    {
        resource.GeometryVersion = geometryVersion;
        resource.VertexCount = geometry.VertexCount;
        resource.IndexCount = geometry.IndexCount;
        resource.WireframeGeometryVersion = 0;
        resource.WireframeIndexCount = 0;
        resource.VertexUploadBytes = EstimateInterleavedVertexUploadBytes(geometry);
        resource.IndexUploadBytes = geometry.EstimatedIndexUploadBytes;
    }

    private static void AddMeshUploadStats(RenderStats stats, RenderGeometry3D geometry)
    {
        stats.DirtyMeshUploads++;
        stats.RenderGeometryCount++;
        stats.VertexBufferUploadCount += 1;
        stats.IndexBufferUploadCount += 1;
        var vertexUploadBytes = EstimateInterleavedVertexUploadBytes(geometry);
        stats.VertexBufferUploadBytes += vertexUploadBytes;
        stats.IndexBufferUploadBytes += geometry.EstimatedIndexUploadBytes;
        stats.MeshUploadBytes += vertexUploadBytes + geometry.EstimatedIndexUploadBytes;
        var tangentAttribute = geometry.Layout.Find(VertexAttributeKind3D.Tangent);
        stats.TangentUploadBytes += tangentAttribute.HasValue ? (long)geometry.VertexCount * tangentAttribute.Value.ByteCount : 0L;
        if (geometry.HasTangentSpace) stats.TangentSpaceMeshCount++;
    }

    private static void UploadMeshResourceData(GlInterface gl, MeshGpuResource resource, Mesh3D mesh, int usage)
    {
        var geometry = mesh.RenderGeometry;
        var interleaved = geometry.GetInterleavedVertexBuffer();
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        UploadBytes(gl, GlArrayBuffer, interleaved.Storage, usage);
        resource.Layout = interleaved.Layout;
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
        UploadIndices(gl, GlElementArrayBuffer, geometry.Indices, usage, out var indexType);
        resource.IndexType = indexType;
    }

    private static long EstimateInterleavedVertexUploadBytes(RenderGeometry3D geometry)
        => geometry.GetInterleavedVertexBuffer().ByteCount;

    private void EnsureWireframeResource(GlInterface gl, MeshGpuResource resource, RenderGeometry3D geometry, long geometryVersion, RenderStats stats)
    {
        if (resource.WireframeGeometryVersion == geometryVersion && resource.WireframeIndexCount > 0) return;
        if (resource.WireframeIndexBuffer == 0) resource.WireframeIndexBuffer = GenRequiredBuffer(gl, $"mesh:{resource.LogicalKey}:wireframe-indices");
        var wireframe = geometry.WireframeIndices;
        gl.BindBuffer(GlElementArrayBuffer, resource.WireframeIndexBuffer);
        UploadIndices(gl, GlElementArrayBuffer, wireframe, GlStaticDraw, out var indexType);
        resource.WireframeGeometryVersion = geometryVersion;
        resource.WireframeIndexCount = wireframe.Count;
        resource.WireframeIndexType = indexType;
        _rhiDevice?.Resources.RegisterBuffer(
            $"mesh:{resource.LogicalKey}:wireframe-index",
            new RhiBufferDescriptor3D(wireframe.ByteCount, RhiBufferUsage3D.Index, wireframe.ElementSizeBytes),
            geometryVersion);
        stats.IndexBufferUploadCount++;
        stats.WireframeIndexUploadBytes += wireframe.ByteCount;
    }

    private unsafe MaterialTextureResource? EnsureMaterialTexture(GlInterface gl, TextureResource3D? texture, int textureUnit, RenderStats stats)
    {
        if (texture is null) return null;
        var key = texture.ResourceKey;
        if (_materialTextures.TryGetValue(key, out var resource))
        {
            if (resource.ContentVersion != texture.ContentVersion)
                throw new InvalidOperationException($"Immutable texture identity collision for '{key}'.");
            return resource;
        }
        if (_deferredMaterialTextureReleases.TryCancel(candidate => string.Equals(candidate.PhysicalKey, key, StringComparison.Ordinal), out resource))
        {
            if (resource.ContentVersion != texture.ContentVersion)
                throw new InvalidOperationException($"Deferred immutable texture identity collision for '{key}'.");
            _materialTextures.Add(key, resource);
            return resource;
        }

        if (!TextureDecodeHelper3D.TryDecodeRgba(texture.EncodedDataInternal, out var decoded, out var error))
            throw new InvalidOperationException($"Texture '{texture.LogicalKey}' ({texture.ContentHash}) could not be decoded: {error}. Missing GPU texture data is not rendered through a fallback material.");
        var descriptor = new RhiTextureDescriptor3D(decoded.Width, decoded.Height, RhiTextureFormat3D.Rgba8Unorm, RhiTextureUsage3D.Sampled);
        var device = _rhiDevice ?? throw new InvalidOperationException("OpenGL RHI device is not initialized.");
        device.ValidateTexture(descriptor, $"texture '{texture.LogicalKey}'");
        device.Resources.ValidateTextureRegistration(key, descriptor, texture.ContentVersion);
        resource = new MaterialTextureResource
        {
            ContentVersion = texture.ContentVersion,
            PhysicalKey = key
        };
        RhiResourceHandle3D handle = default;

        try
        {
            resource.TextureId = GenRequiredTexture(gl, key);
            fixed (byte* ptr = decoded.RgbaPixels)
            {
                gl.ActiveTexture(textureUnit);
                gl.BindTexture(GlTexture2D, resource.TextureId);
                gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
                gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
                gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
                gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
                gl.TexImage2D(GlTexture2D, 0, GlRgba, decoded.Width, decoded.Height, 0, GlRgba, GlUnsignedByte, (IntPtr)ptr);
                gl.ActiveTexture(GlTexture0);
                resource.Width = decoded.Width;
                resource.Height = decoded.Height;
                stats.DirtyTextureUploads++;
                stats.TextureUploadBytes += decoded.ByteLength;
            }
            handle = device.Resources.RegisterTexture(key, descriptor, texture.ContentVersion, _rhiResourceOwnerId);
            resource.RhiHandle = handle;
            _materialTextures.Add(key, resource);
            return resource;
        }
        catch
        {
            if (resource.TextureId != 0) resource.Dispose(gl);
            if (handle.IsValid) device.Resources.ReleaseOwner(handle, _rhiResourceOwnerId);
            throw;
        }
    }

    private void DrainDeferredMaterialTextureReleases(GlInterface gl, long completedFrame)
        => _deferredMaterialTextureReleases.DrainReady(completedFrame, texture => ReleaseMaterialTextureNow(gl, texture));

    private void ReleaseMaterialTextureNow(GlInterface gl, MaterialTextureResource texture)
    {
        texture.Dispose(gl);
        if (texture.RhiHandle.IsValid) _rhiDevice?.Resources.ReleaseOwner(texture.RhiHandle, _rhiResourceOwnerId);
    }

    private void DrainDeferredControlTextureReleases(GlInterface gl, long completedFrame)
        => _deferredControlTextureReleases.DrainReady(completedFrame, texture => ReleaseControlTextureNow(gl, texture));

    private void ReleaseControlTextureNow(GlInterface gl, ControlTextureResource texture)
    {
        texture.Dispose(gl);
        if (texture.RhiHandle.IsValid) _rhiDevice?.Resources.ReleaseOwner(texture.RhiHandle, _rhiResourceOwnerId);
    }

    private unsafe ControlTextureResource? EnsureControlTexture(GlInterface gl, ControlPlane3D plane, RenderStats stats)
    {
        var snapshot = plane.Snapshot;
        if (snapshot is null) return null;

        var restoredFromDeferred = false;
        var created = false;
        if (!_controlTextures.TryGetValue(plane.Id, out var resource))
        {
            if (_deferredControlTextureReleases.TryCancel(candidate => string.Equals(candidate.LogicalKey, plane.Id, StringComparison.Ordinal), out resource))
            {
                restoredFromDeferred = true;
                _controlTextures.Add(plane.Id, resource);
            }
        }
        if (resource is not null && resource.SnapshotVersion == plane.SnapshotVersion) return resource;

        var pixelWidth = System.Math.Max(plane.RenderPixelWidth, 1);
        var pixelHeight = System.Math.Max(plane.RenderPixelHeight, 1);
        var stride = checked(pixelWidth * 4);
        var bufferSize = checked(stride * pixelHeight);
        var resourceKey = $"control:{plane.Id}";
        var descriptor = new RhiTextureDescriptor3D(pixelWidth, pixelHeight, RhiTextureFormat3D.Rgba8Unorm, RhiTextureUsage3D.Sampled);
        var device = _rhiDevice ?? throw new InvalidOperationException("OpenGL RHI device is not initialized.");
        device.ValidateTexture(descriptor, $"control-plane texture '{plane.Id}'");
        device.Resources.ValidateTextureRegistration(resourceKey, descriptor, plane.SnapshotVersion);

        if (resource is null)
        {
            resource = new ControlTextureResource
            {
                LogicalKey = plane.Id,
                TextureId = GenRequiredTexture(gl, resourceKey),
                SnapshotVersion = -1
            };
            created = true;
        }

        if (_controlBgraUploadBuffer.Length < bufferSize) _controlBgraUploadBuffer = new byte[bufferSize];
        if (_controlRgbaUploadBuffer.Length < bufferSize) _controlRgbaUploadBuffer = new byte[bufferSize];

        RhiResourceHandle3D handle = default;
        try
        {
            fixed (byte* bgraPtr = _controlBgraUploadBuffer)
            {
                snapshot.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), (IntPtr)bgraPtr, bufferSize, stride);
            }

            for (var i = 0; i < bufferSize; i += 4)
            {
                _controlRgbaUploadBuffer[i] = _controlBgraUploadBuffer[i + 2];
                _controlRgbaUploadBuffer[i + 1] = _controlBgraUploadBuffer[i + 1];
                _controlRgbaUploadBuffer[i + 2] = _controlBgraUploadBuffer[i];
                _controlRgbaUploadBuffer[i + 3] = _controlBgraUploadBuffer[i + 3];
            }
            fixed (byte* rgbaPtr = _controlRgbaUploadBuffer)
            {
                gl.BindTexture(GlTexture2D, resource.TextureId);
                gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
                gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
                gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
                gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
                gl.TexImage2D(GlTexture2D, 0, GlRgba, pixelWidth, pixelHeight, 0, GlRgba, GlUnsignedByte, (IntPtr)rgbaPtr);
            }

            handle = device.Resources.RegisterTexture(
                resourceKey,
                descriptor,
                plane.SnapshotVersion,
                _rhiResourceOwnerId);
            resource.SnapshotVersion = plane.SnapshotVersion;
            resource.Width = pixelWidth;
            resource.Height = pixelHeight;
            resource.RhiHandle = handle;
            if (created) _controlTextures.Add(plane.Id, resource);
            stats.DirtyTextureUploads++;
            stats.TextureUploadBytes += bufferSize;
            return resource;
        }
        catch
        {
            if (created)
            {
                resource.Dispose(gl);
                if (handle.IsValid) device.Resources.ReleaseOwner(handle, _rhiResourceOwnerId);
            }
            else if (restoredFromDeferred)
            {
                _controlTextures.Remove(plane.Id);
                _deferredControlTextureReleases.Enqueue(
                    resource,
                    _rhiDevice?.FrameIndex ?? 0,
                    _resourceConfiguration?.DeferredReleaseFrames ?? 0);
            }
            throw;
        }
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

    private static unsafe void UploadBytes(GlInterface gl, int target, byte[] data, int usage)
    {
        if (data is null || data.Length == 0) return;
        fixed (byte* ptr = data)
        {
            gl.BufferData(target, new IntPtr(data.Length), (IntPtr)ptr, usage);
        }
    }

    private static unsafe void UploadFloats(GlInterface gl, int target, float[] data, int count, int usage)
    {
        if (count <= 0) return;
        fixed (float* ptr = data)
        {
            gl.BufferData(target, new IntPtr(count * sizeof(float)), (IntPtr)ptr, usage);
        }
    }

    private static int GenRequiredBuffer(GlInterface gl, string label)
    {
        ArgumentNullException.ThrowIfNull(gl);
        label = string.IsNullOrWhiteSpace(label) ? "unnamed" : label;

        int buffer;
        try
        {
            buffer = gl.GenBuffer();
        }
        catch (Exception exception)
        {
            var message = $"OpenGL failed to allocate required GPU buffer '{label}'. No CPU or legacy rendering fallback is permitted.";
            EngineLog3D.Error("OpenGL.Resources", message, exception);
            throw new InvalidOperationException(message, exception);
        }

        if (buffer != 0) return buffer;
        var zeroHandleMessage = $"OpenGL returned buffer handle 0 for required GPU resource '{label}'. No CPU or legacy rendering fallback is permitted.";
        EngineLog3D.Error("OpenGL.Resources", zeroHandleMessage);
        throw new InvalidOperationException(zeroHandleMessage);
    }

    private static int GenRequiredTexture(GlInterface gl, string label)
    {
        ArgumentNullException.ThrowIfNull(gl);
        label = string.IsNullOrWhiteSpace(label) ? "unnamed" : label;

        int texture;
        try
        {
            texture = gl.GenTexture();
        }
        catch (Exception exception)
        {
            var message = $"OpenGL failed to allocate required GPU texture '{label}'. No CPU or legacy rendering fallback is permitted.";
            EngineLog3D.Error("OpenGL.Resources", message, exception);
            throw new InvalidOperationException(message, exception);
        }

        if (texture != 0) return texture;
        var zeroHandleMessage = $"OpenGL returned texture handle 0 for required GPU resource '{label}'. No CPU or legacy rendering fallback is permitted.";
        EngineLog3D.Error("OpenGL.Resources", zeroHandleMessage);
        throw new InvalidOperationException(zeroHandleMessage);
    }

    private unsafe void UploadFloatsSubData(int target, int byteOffset, float[] data, int floatOffset, int count)
    {
        if (count <= 0) return;
        if (_bufferSubData is null) throw new InvalidOperationException("Required OpenGL buffer sub-data support is unavailable.");
        fixed (float* ptr = &data[floatOffset])
        {
            _bufferSubData(target, new IntPtr(byteOffset), new IntPtr(count * sizeof(float)), (IntPtr)ptr);
        }
    }

    private static unsafe void UploadVector3(GlInterface gl, int target, Vector3[] data, int usage)
    {
        if (data.Length == 0) return;
        fixed (Vector3* ptr = data)
        {
            gl.BufferData(target, new IntPtr(data.Length * sizeof(float) * 3), (IntPtr)ptr, usage);
        }
    }

    private static unsafe void UploadVector2(GlInterface gl, int target, Vector2[] data, int usage)
    {
        if (data.Length == 0) return;
        fixed (Vector2* ptr = data)
        {
            gl.BufferData(target, new IntPtr(data.Length * sizeof(float) * 2), (IntPtr)ptr, usage);
        }
    }

    private static unsafe void UploadVector4(GlInterface gl, int target, Vector4[] data, int usage)
    {
        if (data.Length == 0) return;
        fixed (Vector4* ptr = data)
        {
            gl.BufferData(target, new IntPtr(data.Length * sizeof(float) * 4), (IntPtr)ptr, usage);
        }
    }

    private static unsafe void UploadColors(GlInterface gl, int target, ColorRgba[] data, int usage)
    {
        if (data.Length == 0) return;
        fixed (ColorRgba* ptr = data)
        {
            gl.BufferData(target, new IntPtr(data.Length * sizeof(float) * 4), (IntPtr)ptr, usage);
        }
    }

    private static void UploadIndices(GlInterface gl, int target, GeometryIndexBuffer3D data, int usage, out int indexType)
    {
        if (data.Format == IndexFormat3D.UInt16)
        {
            UploadUShorts(gl, target, data.UInt16Storage!, usage);
            indexType = GlUnsignedShort;
            return;
        }

        UploadInts(gl, target, data.UInt32Storage!, usage);
        indexType = GlUnsignedInt;
    }

    private static unsafe void UploadUShorts(GlInterface gl, int target, ushort[] data, int usage)
        => UploadUShorts(gl, target, data, data?.Length ?? 0, usage);

    private static unsafe void UploadUShorts(GlInterface gl, int target, ushort[] data, int count, int usage)
    {
        if (data is null || count <= 0) return;
        fixed (ushort* ptr = data)
        {
            gl.BufferData(target, new IntPtr(count * sizeof(ushort)), (IntPtr)ptr, usage);
        }
    }

    private static unsafe void UploadInts(GlInterface gl, int target, int[] data, int usage)
    {
        if (data is null || data.Length == 0) return;
        fixed (int* ptr = data)
        {
            gl.BufferData(target, new IntPtr(data.Length * sizeof(int)), (IntPtr)ptr, usage);
        }
    }


    private static void ApplyAnimationStats(RenderStats stats, Scene3D scene, bool gpuSkinningActive)
    {
        var imported = 0;
        var skinned = 0;
        var animated = 0;
        var skinMatrices = 0;
        var skinnedPrimitives = 0;
        long skinPayloadBytes = 0;
        var objects = scene.Registry.SnapshotAllObjects();
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
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
        stats.GpuSkinningActive = gpuSkinningActive && skinned > 0;
    }

    private void UploadClientTransformAnimation(Scene3D scene, bool enabled)
    {
        var active = enabled && scene.Performance.EnableWebGlClientGpuTransformAnimation;
        UploadFloat(_uniform1f, _meshClientAnimationEnabledLocation, active ? 1f : 0f);
        UploadFloat(_uniform1f, _meshClientAnimationTimeLocation, active ? (float)scene.UpdateLoop.RenderTimeSeconds : 0f);
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
        ApplyCullMode(gl, material.CullMode);
        UploadFloat(_uniform1f, _meshLightingEnabledLocation, ToLightingUniform(material.Lighting));
        UploadVector3(_uniform3f, _meshSpecularColorLocation, new Vector3(material.SpecularColor.R, material.SpecularColor.G, material.SpecularColor.B));
        UploadVector4(_uniform4f, _meshSpecularParamsLocation, new Vector4(material.SpecularStrength, material.Shininess, material.Metallic, material.Roughness));
        UploadVector4(_uniform4f, _meshMaterialStrengthsLocation, new Vector4(material.AmbientStrength, material.DiffuseStrength, material.NormalMapStrength, material.HasNormalMap ? 1f : 0f));
        UploadFloat(_uniform1f, _meshNormalMapStrengthLocation, material.HasNormalMap ? material.NormalMapStrength : 0f);
        UploadVector4(_uniform4f, _meshAlphaParamsLocation, new Vector4(material.AlphaCutoff, material.Surface == SurfaceMode.Transparent ? 1f : 0f, 0f, 0f));
        UploadVector4(_uniform4f, _meshEmissiveColorLocation, new Vector4(material.EmissiveColor.R, material.EmissiveColor.G, material.EmissiveColor.B, material.EmissiveColor.A));
        UploadMaterialTexture(gl, material.HasBaseColorTexture ? material.BaseColorTextureResource : null, GlTexture2, 2, _meshBaseColorTextureLocation, _meshBaseColorTextureEnabledLocation, stats);
        UploadMaterialTexture(gl, material.HasNormalMap ? material.NormalMapTextureResource : null, GlTexture3, 3, _meshNormalTextureLocation, _meshNormalTextureEnabledLocation, stats);
        UploadMaterialTexture(gl, material.HasMetallicRoughnessTexture ? material.MetallicRoughnessTextureResource : null, GlTexture4, 4, _meshMetallicRoughnessTextureLocation, _meshMetallicRoughnessTextureEnabledLocation, stats);
        UploadMaterialTexture(gl, material.HasEmissiveTexture ? material.EmissiveTextureResource : null, GlTexture5, 5, _meshEmissiveTextureLocation, _meshEmissiveTextureEnabledLocation, stats);
    }

    private void UploadMaterialTexture(GlInterface gl, TextureResource3D? texture, int glTextureUnit, int samplerSlot, int samplerLocation, int enabledLocation, RenderStats stats)
    {
        if (texture is null)
        {
            UploadFloat(_uniform1f, enabledLocation, 0f);
            return;
        }

        var resource = EnsureMaterialTexture(gl, texture, glTextureUnit, stats);
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

    private void UploadHighScaleMaterial(GlInterface gl, LightingMode lightingMode)
    {
        ApplyCullMode(gl, CullMode.None);
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

    private static void UploadColorFromInstanceData(GlUniform4fDelegate? uniform4f, int location, float[] data, int offset)
    {
        if (location < 0 || uniform4f is null || data is null || offset < 0 || offset + 3 >= data.Length) return;
        uniform4f(location, data[offset], data[offset + 1], data[offset + 2], data[offset + 3]);
    }

    private static void UploadColor3(GlUniform3fDelegate? uniform3f, int location, ColorRgba color, float intensity = 1f)
    {
        if (location >= 0) uniform3f?.Invoke(location, color.R * intensity, color.G * intensity, color.B * intensity);
    }

    private static unsafe void UploadMatrix(GlUniformMatrix4fvDelegate? uniformMatrix4fv, int location, Matrix4x4 matrix, float[] buffer)
    {
        if (location < 0 || uniformMatrix4fv is null) return;
        WriteMatrix(buffer, 0, matrix);
        fixed (float* ptr = buffer)
        {
            uniformMatrix4fv(location, 1, 0, (IntPtr)ptr);
        }
    }

    private static unsafe void UploadMatrixFromInstanceData(GlUniformMatrix4fvDelegate? uniformMatrix4fv, int location, float[] data, int offset, float[] buffer)
    {
        if (location < 0 || uniformMatrix4fv is null) return;
        fixed (float* ptr = &data[offset])
        {
            uniformMatrix4fv(location, 1, 0, (IntPtr)ptr);
        }
    }

    private void UploadBatchSkinning(GlInterface gl, MeshBatchData batch)
    {
        UploadSkinning(gl, batch, _meshSkinningEnabledLocation, _meshBoneTextureLocation, _meshBoneTextureHeightLocation);
    }

    private unsafe void UploadSkinning(GlInterface gl, MeshBatchData batch, int enabledLocation, int textureLocation, int heightLocation)
    {
        if (!batch.HasSkinning)
        {
            UploadFloat(_uniform1f, enabledLocation, 0f);
            return;
        }

        var rhiDevice = _rhiDevice ?? throw new InvalidOperationException("OpenGL RHI device is not initialized.");
        if (!_supportsBoneTextureSkinning)
            throw new RhiCapabilityException3D(RhiBackendApi3D.OpenGl, "GPU skinning upload", RhiFeature3D.VertexTextureFetch | RhiFeature3D.FloatTextures, rhiDevice.Capabilities);

        var matrices = batch.SkinMatrices;
        if (matrices.Length == 0)
            throw new InvalidOperationException("A skinned OpenGL batch has no bone matrices; rendering the bind pose as a fallback is forbidden.");
        if (matrices.Length > _gpuSkinTextureBoneLimit)
            throw new RhiDeviceLimitException3D(RhiBackendApi3D.OpenGl, "GPU skinning upload", $"bone count <= {_gpuSkinTextureBoneLimit}", rhiDevice.Capabilities);

        if (batch.BoneTexture == 0)
        {
            batch.BoneTexture = GenRequiredTexture(gl, "skinning:bone-matrices");
            batch.BoneTextureVersion = -1;
        }

        if (batch.BoneTextureVersion != batch.SkinningVersion || batch.BoneTextureHeight != matrices.Length)
        {
            var required = matrices.Length * 16;
            if (batch.BoneTextureData.Length < required)
            {
                batch.BoneTextureData = new float[required];
            }

            for (var i = 0; i < matrices.Length; i++)
            {
                WriteMatrix(batch.BoneTextureData, i * 16, matrices[i]);
            }

            fixed (float* ptr = batch.BoneTextureData)
            {
                gl.ActiveTexture(GlTexture6);
                gl.BindTexture(GlTexture2D, batch.BoneTexture);
                gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlNearest);
                gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlNearest);
                gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
                gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
                gl.TexImage2D(GlTexture2D, 0, GlRgba, 4, matrices.Length, 0, GlRgba, GlFloat, (IntPtr)ptr);
                gl.ActiveTexture(GlTexture0);
            }

            batch.BoneTextureVersion = batch.SkinningVersion;
            batch.BoneTextureHeight = matrices.Length;
        }

        gl.ActiveTexture(GlTexture6);
        gl.BindTexture(GlTexture2D, batch.BoneTexture);
        _uniform1i?.Invoke(textureLocation, 6);
        UploadFloat(_uniform1f, heightLocation, matrices.Length);
        UploadFloat(_uniform1f, enabledLocation, 1f);
        gl.ActiveTexture(GlTexture0);
    }

    private static Vector3 TransformPointFromInstanceData(float[] data, int offset, Vector3 point)
    {
        return new Vector3(
            point.X * data[offset] + point.Y * data[offset + 4] + point.Z * data[offset + 8] + data[offset + 12],
            point.X * data[offset + 1] + point.Y * data[offset + 5] + point.Z * data[offset + 9] + data[offset + 13],
            point.X * data[offset + 2] + point.Y * data[offset + 6] + point.Z * data[offset + 10] + data[offset + 14]);
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDepthFuncDelegate(int func);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDisableDelegate(int cap);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlCullFaceDelegate(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform1iDelegate(int location, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform1fDelegate(int location, float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform3fDelegate(int location, float x, float y, float z);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform4fDelegate(int location, float x, float y, float z, float w);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniformMatrix4fvDelegate(int location, int count, byte transpose, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlVertexAttribDivisorDelegate(int index, int divisor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlVertexAttrib4fDelegate(int index, float x, float y, float z, float w);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDrawElementsInstancedDelegate(int mode, int count, int type, IntPtr indices, int instanceCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlBufferSubDataDelegate(int target, IntPtr offset, IntPtr size, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGenFramebuffersDelegate(int n, int[] framebuffers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDeleteFramebuffersDelegate(int n, int[] framebuffers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlFramebufferTexture2DDelegate(int target, int attachment, int textarget, int texture, int level);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GlCheckFramebufferStatusDelegate(int target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDrawBufferDelegate(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlReadBufferDelegate(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGenVertexArraysDelegate(int n, int[] arrays);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlBindVertexArrayDelegate(int array);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDeleteVertexArraysDelegate(int n, int[] arrays);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDisableVertexAttribArrayDelegate(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGetIntegervDelegate(int pname, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GlGetStringDelegate(int name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GlGetErrorDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGenQueriesDelegate(int count, [Out] int[] queries);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDeleteQueriesDelegate(int count, int[] queries);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlBeginQueryDelegate(int target, int query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlEndQueryDelegate(int target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGetQueryObjectivDelegate(int query, int parameter, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGetQueryObjectui64vDelegate(int query, int parameter, out ulong value);

    private readonly record struct GlContextVersionInfo(bool IsOpenGlEs, int Major, int Minor, bool IsKnown);




    private void DisposeMeshResource(GlInterface gl, string key, MeshGpuResource resource)
    {
        var resources = _rhiDevice?.Resources;
        resources?.Release($"mesh:{key}:vertex", RhiResourceKind3D.Buffer);
        resources?.Release($"mesh:{key}:index", RhiResourceKind3D.Buffer);
        resources?.Release($"mesh:{key}:wireframe-index", RhiResourceKind3D.Buffer);
        resources?.Release($"mesh:{key}:vao-main", RhiResourceKind3D.VertexArray);
        DeleteVertexArray(resource.VertexArray);
        resource.Dispose(gl);
    }

    private sealed class OrdinaryBatchStatsCache
    {
        private int _visibleMeshCount;
        private int _culledObjectCount;
        private int _triangleCount;
        private int _normalMappedMeshCount;
        private int _particleSystemCount;
        private int _particleCount;
        private int _particleVertexCount;
        private int _instancedMeshLayerCount;
        private int _instancedMeshInstanceCount;

        public void Reset()
        {
            _visibleMeshCount = 0;
            _culledObjectCount = 0;
            _triangleCount = 0;
            _normalMappedMeshCount = 0;
            _particleSystemCount = 0;
            _particleCount = 0;
            _particleVertexCount = 0;
            _instancedMeshLayerCount = 0;
            _instancedMeshInstanceCount = 0;
        }

        public void Capture(RenderStats stats)
        {
            _visibleMeshCount = stats.VisibleMeshCount;
            _culledObjectCount = stats.CulledObjectCount;
            _triangleCount = stats.TriangleCount;
            _normalMappedMeshCount = stats.NormalMappedMeshCount;
            _particleSystemCount = stats.ParticleSystemCount;
            _particleCount = stats.ParticleCount;
            _particleVertexCount = stats.ParticleVertexCount;
            _instancedMeshLayerCount = stats.InstancedMeshLayerCount;
            _instancedMeshInstanceCount = stats.InstancedMeshInstanceCount;
        }

        public void ApplyTo(RenderStats stats)
        {
            stats.VisibleMeshCount += _visibleMeshCount;
            stats.CulledObjectCount += _culledObjectCount;
            stats.TriangleCount += _triangleCount;
            stats.NormalMappedMeshCount += _normalMappedMeshCount;
            stats.ParticleSystemCount += _particleSystemCount;
            stats.ParticleCount += _particleCount;
            stats.ParticleVertexCount += _particleVertexCount;
            stats.InstancedMeshLayerCount += _instancedMeshLayerCount;
            stats.InstancedMeshInstanceCount += _instancedMeshInstanceCount;
        }
    }

    private sealed class MeshGpuResource
    {
        public string LogicalKey { get; init; } = string.Empty;
        public long GeometryVersion { get; set; }
        public int VertexArray { get; init; }
        public int VertexBuffer { get; init; }
        public VertexLayout3D? Layout { get; set; }
        public int NormalBuffer { get; init; }
        public int TexCoordBuffer { get; init; }
        public int TangentBuffer { get; init; }
        public int VertexColorBuffer { get; init; }
        public int BoneIndexBuffer { get; init; }
        public int BoneWeightBuffer { get; init; }
        public int MaterialSlotBuffer { get; init; }
        public int IndexBuffer { get; init; }
        public int WireframeIndexBuffer { get; set; }
        public int VertexCount { get; set; }
        public int IndexCount { get; set; }
        public int WireframeIndexCount { get; set; }
        public long WireframeGeometryVersion { get; set; }
        public int IndexType { get; set; } = GlUnsignedInt;
        public int WireframeIndexType { get; set; } = GlUnsignedInt;
        public long VertexUploadBytes { get; set; }
        public long IndexUploadBytes { get; set; }
        public long StaticLayoutCallbackSerial { get; set; } = -1;
        public void Dispose(GlInterface gl)
        {
            if (NormalBuffer != 0) gl.DeleteBuffer(NormalBuffer);
            if (TexCoordBuffer != 0) gl.DeleteBuffer(TexCoordBuffer);
            if (TangentBuffer != 0) gl.DeleteBuffer(TangentBuffer);
            if (VertexColorBuffer != 0) gl.DeleteBuffer(VertexColorBuffer);
            if (BoneIndexBuffer != 0) gl.DeleteBuffer(BoneIndexBuffer);
            if (BoneWeightBuffer != 0) gl.DeleteBuffer(BoneWeightBuffer);
            if (MaterialSlotBuffer != 0) gl.DeleteBuffer(MaterialSlotBuffer);
            if (VertexBuffer != 0) gl.DeleteBuffer(VertexBuffer);
            if (IndexBuffer != 0) gl.DeleteBuffer(IndexBuffer);
            if (WireframeIndexBuffer != 0) gl.DeleteBuffer(WireframeIndexBuffer);
        }
    }

    private readonly struct MeshBatchKey : IEquatable<MeshBatchKey>
    {
        private readonly string _batchId;
        private readonly string _meshKey;
        private readonly string _materialKey;
        private readonly bool _particleBillboard;

        public MeshBatchKey(string batchId, string meshKey, string materialKey, bool particleBillboard)
        {
            _batchId = batchId ?? string.Empty;
            _meshKey = meshKey ?? string.Empty;
            _materialKey = materialKey ?? string.Empty;
            _particleBillboard = particleBillboard;
        }

        public bool Equals(MeshBatchKey other)
            => _particleBillboard == other._particleBillboard &&
               string.Equals(_batchId, other._batchId, StringComparison.Ordinal) &&
               string.Equals(_meshKey, other._meshKey, StringComparison.Ordinal) &&
               string.Equals(_materialKey, other._materialKey, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is MeshBatchKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_batchId, _meshKey, _materialKey, _particleBillboard);
    }

    private readonly struct RetainedOrdinarySlotRef
    {
        public RetainedOrdinarySlotRef(MeshBatchData batch, int slot, Object3D owner)
        {
            Batch = batch;
            Slot = slot;
            Owner = owner;
        }

        public MeshBatchData Batch { get; }
        public int Slot { get; }
        public Object3D Owner { get; }
    }

    private readonly struct CachedOpenGlDrawCommand
    {
        private CachedOpenGlDrawCommand(
            SceneRenderCommandKind3D kind,
            string id,
            bool transparent,
            float sortDistanceSquared,
            int sourceOrder,
            MeshBatchData? meshBatch,
            ParticleBatchData? particleBatch,
            HighScaleInstanceLayer3D? highScaleLayer)
        {
            Kind = kind;
            Id = id ?? string.Empty;
            Transparent = transparent;
            SortDistanceSquared = sortDistanceSquared;
            SourceOrder = sourceOrder;
            MeshBatch = meshBatch;
            ParticleBatch = particleBatch;
            HighScaleLayer = highScaleLayer;
        }

        public SceneRenderCommandKind3D Kind { get; }
        public string Id { get; }
        public bool Transparent { get; }
        public float SortDistanceSquared { get; }
        public int SourceOrder { get; }
        public MeshBatchData? MeshBatch { get; }
        public ParticleBatchData? ParticleBatch { get; }
        public HighScaleInstanceLayer3D? HighScaleLayer { get; }

        public CachedOpenGlDrawCommand WithSourceOrder(int sourceOrder)
            => new(Kind, Id, Transparent, SortDistanceSquared, sourceOrder, MeshBatch, ParticleBatch, HighScaleLayer);

        public CachedOpenGlDrawCommand RefreshSortDistance(Vector3 cameraPosition)
        {
            if (!Transparent || MeshBatch is null)
            {
                return this;
            }

            return new CachedOpenGlDrawCommand(Kind, Id, Transparent, MeshBatch.ComputeSortDistanceSquared(cameraPosition), SourceOrder, MeshBatch, ParticleBatch, HighScaleLayer);
        }

        public static CachedOpenGlDrawCommand ForMesh(SceneRenderCommand3D command, MeshBatchData batch)
            => new(command.Kind, command.Id, command.Transparent, command.SortDistanceSquared, command.SourceOrder, batch, null, null);

        public static CachedOpenGlDrawCommand ForParticle(SceneRenderCommand3D command, ParticleBatchData batch)
            => new(command.Kind, command.Id, command.Transparent, command.SortDistanceSquared, command.SourceOrder, null, batch, null);

        public static CachedOpenGlDrawCommand ForHighScale(SceneRenderCommand3D command, int sourceOrder)
            => new(command.Kind, command.Id, false, command.SortDistanceSquared, sourceOrder, null, null, command.HighScaleLayer);

        public static int CompareForDraw(CachedOpenGlDrawCommand a, CachedOpenGlDrawCommand b)
            => SceneRenderDrawOrder3D.Compare(
                a.Transparent,
                a.SortDistanceSquared,
                a.SourceOrder,
                a.Id,
                b.Transparent,
                b.SortDistanceSquared,
                b.SourceOrder,
                b.Id);
    }

    private abstract class InstanceBatchData
    {
        private float[] _data;
        private int _buildHash;
        private int _lastHash;
        private int _lastCount;
        private readonly List<int> _dirtyInstanceOffsets = new(64);
        private readonly Dictionary<string, int> _slotByObjectId = new(StringComparer.Ordinal);
        private string[] _trackedIds = Array.Empty<string>();
        private int[] _trackedTransformVersions = Array.Empty<int>();
        private int[] _trackedMaterialVersions = Array.Empty<int>();
        private bool _rangeTrackingActive;
        private bool _structuralDirty;

        protected InstanceBatchData(string meshKey, Mesh3D mesh, MaterialBinding3D material, int floatStride)
        {
            MeshKey = meshKey;
            Mesh = mesh;
            Material = material;
            FloatStride = Math.Max(1, floatStride);
            _data = new float[FloatStride * 64];
        }

        public string MeshKey { get; }
        public Mesh3D Mesh { get; set; }
        public MaterialBinding3D Material { get; set; }
        public int InstanceCount { get; private set; }
        public int FloatStride { get; }
        public int FloatCount => InstanceCount * FloatStride;
        public float[] Data => _data;
        public int InstanceBuffer { get; set; }
        public int UploadedVersion { get; set; } = -1;
        public int UploadedCapacityFloats { get; set; }
        public int DataVersion { get; private set; }
        public bool CanUploadDirtyInstanceRanges { get; private set; }
        public List<int> DirtyInstanceOffsets => _dirtyInstanceOffsets;

        public void BeginBuild()
        {
            InstanceCount = 0;
            _buildHash = 17;
            _dirtyInstanceOffsets.Clear();
            _slotByObjectId.Clear();
            _rangeTrackingActive = false;
            _structuralDirty = false;
            CanUploadDirtyInstanceRanges = false;
        }

        public void EndBuild()
        {
            var previousCount = _lastCount;
            var changed = InstanceCount != previousCount || _buildHash != _lastHash;
            CanUploadDirtyInstanceRanges = changed &&
                                           _rangeTrackingActive &&
                                           !_structuralDirty &&
                                           InstanceCount == previousCount &&
                                           _dirtyInstanceOffsets.Count > 0 &&
                                           _dirtyInstanceOffsets.Count < InstanceCount;
            if (changed)
            {
                DataVersion++;
                _lastCount = InstanceCount;
                _lastHash = _buildHash;
            }
        }

        public void BeginSlotUpdates()
        {
            _dirtyInstanceOffsets.Clear();
            CanUploadDirtyInstanceRanges = false;
        }

        public bool TryGetSlot(string objectId, out int slot)
            => _slotByObjectId.TryGetValue(objectId ?? string.Empty, out slot);

        public void Add(Matrix4x4 model, ColorRgba color)
        {
            if (FloatStride < InstanceFloatStride) throw new InvalidOperationException("Matrix instance batches require the 20-float instance layout.");
            var offset = AllocateInstance();
            WriteMatrix(_data, offset, model);
            _data[offset + 16] = color.R; _data[offset + 17] = color.G; _data[offset + 18] = color.B; _data[offset + 19] = color.A;
            HashInstance(offset);
        }

        public int AddTracked(string objectId, int transformVersion, int materialVersion, Matrix4x4 model, ColorRgba color)
        {
            if (FloatStride < InstanceFloatStride) throw new InvalidOperationException("Tracked matrix instance batches require the 20-float instance layout.");
            _rangeTrackingActive = true;
            var normalizedId = objectId ?? string.Empty;
            var slot = InstanceCount;
            EnsureTrackingCapacity(slot + 1);
            var structuralDirty = slot >= _lastCount || !string.Equals(_trackedIds[slot], normalizedId, StringComparison.Ordinal);
            if (structuralDirty)
            {
                _structuralDirty = true;
            }

            if (structuralDirty ||
                _trackedTransformVersions[slot] != transformVersion ||
                _trackedMaterialVersions[slot] != materialVersion)
            {
                _dirtyInstanceOffsets.Add(slot);
            }

            _trackedIds[slot] = normalizedId;
            _trackedTransformVersions[slot] = transformVersion;
            _trackedMaterialVersions[slot] = materialVersion;
            _slotByObjectId[normalizedId] = slot;
            Add(model, color);
            return slot;
        }

        public bool UpdateTrackedSlot(int slot, string objectId, int transformVersion, int materialVersion, Matrix4x4 model, ColorRgba color, Vector3 cameraPosition)
        {
            if (FloatStride < InstanceFloatStride) throw new InvalidOperationException("Tracked matrix instance batches require the 20-float instance layout.");
            if ((uint)slot >= (uint)InstanceCount) return false;
            var normalizedId = objectId ?? string.Empty;
            if (!string.Equals(_trackedIds[slot], normalizedId, StringComparison.Ordinal)) return false;
            if (_trackedMaterialVersions[slot] != materialVersion) return false;

            var offset = slot * FloatStride;
            WriteMatrix(_data, offset, model);
            _data[offset + 16] = color.R;
            _data[offset + 17] = color.G;
            _data[offset + 18] = color.B;
            _data[offset + 19] = color.A;
            _trackedTransformVersions[slot] = transformVersion;
            _dirtyInstanceOffsets.Add(slot);
            DataVersion++;
            CanUploadDirtyInstanceRanges = _dirtyInstanceOffsets.Count > 0 && _dirtyInstanceOffsets.Count < InstanceCount;
            UpdateSortDistanceFromSlot(slot, cameraPosition);
            return true;
        }

        protected virtual void UpdateSortDistanceFromSlot(int slot, Vector3 cameraPosition)
        {
        }

        protected int AllocateInstance()
        {
            EnsureCapacity((InstanceCount + 1) * FloatStride);
            var offset = InstanceCount * FloatStride;
            InstanceCount++;
            return offset;
        }

        protected void HashInstance(int offset)
        {
            unchecked
            {
                for (var i = 0; i < FloatStride; i++)
                {
                    _buildHash = (_buildHash * 31) + _data[offset + i].GetHashCode();
                }
            }
        }

        public virtual void Dispose(GlInterface gl)
        {
            if (InstanceBuffer != 0) gl.DeleteBuffer(InstanceBuffer);
            InstanceBuffer = 0;
            UploadedVersion = -1;
            UploadedCapacityFloats = 0;
        }

        private void EnsureCapacity(int required)
        {
            if (_data.Length >= required) return;
            var next = _data.Length;
            while (next < required) next *= 2;
            Array.Resize(ref _data, next);
            UploadedCapacityFloats = 0;
            UploadedVersion = -1;
        }

        private void EnsureTrackingCapacity(int required)
        {
            if (_trackedIds.Length >= required) return;
            var next = _trackedIds.Length == 0 ? 64 : _trackedIds.Length;
            while (next < required) next *= 2;
            Array.Resize(ref _trackedIds, next);
            Array.Resize(ref _trackedTransformVersions, next);
            Array.Resize(ref _trackedMaterialVersions, next);
        }
    }

    private sealed class MeshBatchData : InstanceBatchData
    {
        private Matrix4x4[] _skinMatrices = Array.Empty<Matrix4x4>();
        private float[] _visibleData = Array.Empty<float>();

        public MeshBatchData(string meshKey, Mesh3D mesh, MaterialBinding3D material) : base(meshKey, mesh, material, InstanceFloatStride) { }

        public Matrix4x4[] SkinMatrices => _skinMatrices;
        public int SkinningVersion { get; private set; } = -1;
        public bool HasSkinning => _skinMatrices.Length > 0;
        public int BoneTexture { get; set; }
        public int BoneTextureVersion { get; set; } = -1;
        public int BoneTextureHeight { get; set; }
        public float[] BoneTextureData { get; set; } = Array.Empty<float>();
        public int CulledInstanceBuffer { get; set; }
        public float[] VisibleData => _visibleData;

        public void EnsureVisibleDataCapacity(int required)
        {
            if (_visibleData.Length >= required) return;
            var next = _visibleData.Length == 0 ? InstanceFloatStride * 64 : _visibleData.Length;
            while (next < required) next *= 2;
            Array.Resize(ref _visibleData, next);
        }

        public float ComputeSortDistanceSquared(Vector3 cameraPosition)
        {
            if (InstanceCount == 0) return 0f;
            if (!Mesh.LocalBounds.IsValid)
            {
                var conservativeMax = 0f;
                for (var i = 0; i < InstanceCount; i++)
                {
                    var offset = i * FloatStride;
                    var position = new Vector3(Data[offset + 12], Data[offset + 13], Data[offset + 14]);
                    var distanceSquared = Vector3.DistanceSquared(cameraPosition, position);
                    if (distanceSquared > conservativeMax) conservativeMax = distanceSquared;
                }

                return conservativeMax;
            }

            var center = Mesh.LocalBounds.Center;
            var maxDistanceSquared = 0f;
            for (var i = 0; i < InstanceCount; i++)
            {
                var offset = i * FloatStride;
                var worldCenter = TransformPointFromInstanceData(Data, offset, center);
                var distanceSquared = Vector3.DistanceSquared(cameraPosition, worldCenter);
                if (distanceSquared > maxDistanceSquared) maxDistanceSquared = distanceSquared;
            }

            return maxDistanceSquared;
        }

        protected override void UpdateSortDistanceFromSlot(int slot, Vector3 cameraPosition)
        {
            // Sort distance is intentionally computed on demand from current slot data
            // so camera-only frames do not dirty CPU instance payloads.
        }

        public void SetSkinning(Matrix4x4[] matrices, int version)
        {
            if (SkinningVersion == version && _skinMatrices.Length == matrices.Length) return;
            if (matrices.Length == 0)
            {
                _skinMatrices = Array.Empty<Matrix4x4>();
            }
            else
            {
                if (_skinMatrices.Length != matrices.Length) _skinMatrices = new Matrix4x4[matrices.Length];
                Array.Copy(matrices, _skinMatrices, matrices.Length);
            }
            SkinningVersion = version;
        }

        public override void Dispose(GlInterface gl)
        {
            base.Dispose(gl);
            if (BoneTexture != 0) gl.DeleteTexture(BoneTexture);
            if (CulledInstanceBuffer != 0) gl.DeleteBuffer(CulledInstanceBuffer);
            BoneTexture = 0;
            BoneTextureVersion = -1;
            BoneTextureHeight = 0;
            CulledInstanceBuffer = 0;
        }
    }

    private sealed class ParticleBatchData : InstanceBatchData
    {
        public ParticleBatchData(string meshKey, Mesh3D mesh, MaterialBinding3D material, bool billboard, bool transparent)
            : base(meshKey, mesh, material, billboard ? ParticleBillboardFloatStride : InstanceFloatStride)
        {
            Billboard = billboard;
            Transparent = transparent;
        }

        public bool Billboard { get; }
        public bool Transparent { get; set; }

        public void AddBillboardParticle(Vector3 center, float size, ColorRgba color)
        {
            var offset = AllocateInstance();
            var data = Data;
            data[offset] = center.X;
            data[offset + 1] = center.Y;
            data[offset + 2] = center.Z;
            data[offset + 3] = size;
            data[offset + 4] = color.R;
            data[offset + 5] = color.G;
            data[offset + 6] = color.B;
            data[offset + 7] = color.A;
            HashInstance(offset);
        }
    }

    private readonly struct HighScaleBatchKey : IEquatable<HighScaleBatchKey>
    {
        private readonly string _layerId;
        private readonly HighScaleChunkKey3D _chunkKey;
        private readonly HighScaleLodLevel3D _lod;
        private readonly int _statePartIndex;

        public HighScaleBatchKey(string layerId, HighScaleChunkKey3D chunkKey, HighScaleLodLevel3D lod, int statePartIndex = -1)
        {
            _layerId = layerId;
            _chunkKey = chunkKey;
            _lod = lod;
            _statePartIndex = statePartIndex;
        }

        public bool Equals(HighScaleBatchKey other)
            => string.Equals(_layerId, other._layerId, StringComparison.Ordinal) &&
               _chunkKey.Equals(other._chunkKey) &&
               _lod == other._lod &&
               _statePartIndex == other._statePartIndex;

        public override bool Equals(object? obj) => obj is HighScaleBatchKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_layerId, _chunkKey, _lod, _statePartIndex);
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

        public void Add(int instanceIndex, Matrix4x4 model, int transformVersion, int materialVariantId, bool visible, float fadeAlpha, bool directStateColor, ColorRgba directColor)
        {
            EnsureCapacity(InstanceCount + 1);
            _instanceIndices[InstanceCount] = instanceIndex;
            _transformVersions[InstanceCount] = transformVersion;
            _offsetByInstanceIndex[instanceIndex] = InstanceCount;

            var transformOffset = InstanceCount * HighScaleTransformFloatStride;
            WriteMatrix(_transformData, transformOffset, model);
            WriteState(InstanceCount, materialVariantId, visible, fadeAlpha, directStateColor, directColor);
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

        public void WriteState(int offset, int materialVariantId, bool visible, float fadeAlpha, bool directStateColor, ColorRgba directColor)
        {
            var stateOffset = offset * HighScaleStateFloatStride;
            var alpha = System.Math.Clamp(fadeAlpha, 0f, 1f) * (visible ? 1f : 0f);
            if (directStateColor)
            {
                _stateData[stateOffset] = directColor.R;
                _stateData[stateOffset + 1] = directColor.G;
                _stateData[stateOffset + 2] = directColor.B;
                _stateData[stateOffset + 3] = directColor.A * alpha;
                return;
            }

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

    private sealed class ControlTextureResource
    {
        public string LogicalKey { get; init; } = string.Empty;
        public int TextureId { get; init; }
        public RhiResourceHandle3D RhiHandle { get; set; }
        public int SnapshotVersion { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public void Dispose(GlInterface gl) { if (TextureId != 0) gl.DeleteTexture(TextureId); }
    }

    private sealed class MaterialTextureResource
    {
        public int TextureId { get; set; }
        public string PhysicalKey { get; init; } = string.Empty;
        public long ContentVersion { get; init; }
        public RhiResourceHandle3D RhiHandle { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public void Dispose(GlInterface gl) { if (TextureId != 0) gl.DeleteTexture(TextureId); }
    }

    private sealed class HighScalePaletteTextureResource
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
attribute vec4 aVertexColor;
attribute vec4 aBoneIndices;
attribute vec4 aBoneWeights;
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
uniform vec4 uColor;
uniform float uUseInstancing;
uniform float uUsePartLocal;
uniform float uUseHighScaleState;
uniform float uUsePaletteTexture;
uniform float uUseDirectStateColor;
uniform float uParticleBillboard;
uniform vec3 uParticleCameraRight;
uniform vec3 uParticleCameraUp;
uniform vec3 uParticleCameraForward;
uniform float uClientAnimationEnabled;
uniform float uClientAnimationTime;
uniform float uClientAnimationAmplitude;
uniform float uSkinningEnabled;
uniform sampler2D uBoneTexture;
uniform float uBoneTextureHeight;
uniform vec4 uVariantColors[32];
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec3 vTangent;
varying vec2 vTexCoord0;
varying vec4 vColor;
varying vec4 vVertexColor;
varying float vVariantIndex;
varying float vMaterialSlot;
varying float vUsePaletteTexture;
mat4 readBoneMatrix(float boneIndex)
{
    float y = (floor(boneIndex + 0.5) + 0.5) / max(uBoneTextureHeight, 1.0);
    vec4 c0 = texture2D(uBoneTexture, vec2(0.125, y));
    vec4 c1 = texture2D(uBoneTexture, vec2(0.375, y));
    vec4 c2 = texture2D(uBoneTexture, vec2(0.625, y));
    vec4 c3 = texture2D(uBoneTexture, vec2(0.875, y));
    return mat4(c0, c1, c2, c3);
}
void main()
{
    mat4 instanceModel = mat4(aInstanceModel0, aInstanceModel1, aInstanceModel2, aInstanceModel3);
    mat4 model = uUseInstancing > 0.5 ? instanceModel : uModel;
    // CPU-side authoring uses System.Numerics row-vector composition: local * partLocal * instance/world.
    // GLSL multiplies column vectors, and we upload the same matrix memory without transposing, so the
    // correct shader order here is model * uPartLocal. The previous order (uPartLocal * model) effectively
    // applied the part-local offset in world space, causing high-scale rack parts to stretch into long strips
    // and only appear from certain camera angles.
    if (uUsePartLocal > 0.5) model = model * uPartLocal;
    vec4 world;
    vec3 normalWorld;
    vec3 tangentWorld;
    if (uParticleBillboard > 0.5)
    {
        vec4 particleState = uUseInstancing > 0.5 ? aInstanceModel0 : vec4(model[3].xyz, abs(model[0].x));
        float particleSize = max(abs(particleState.w), 0.0001);
        vec3 center = particleState.xyz;
        vec3 billboardOffset = (uParticleCameraRight * aPosition.x + uParticleCameraUp * aPosition.y) * particleSize;
        world = vec4(center + billboardOffset, 1.0);
        normalWorld = normalize(uParticleCameraForward);
        tangentWorld = normalize(uParticleCameraRight);
    }
    else
    {
        vec4 localPosition = vec4(aPosition, 1.0);
        vec3 localNormal = aNormal;
        vec3 localTangent = aTangent.xyz;
        if (uSkinningEnabled > 0.5)
        {
            float maxBone = max(uBoneTextureHeight - 1.0, 0.0);
            mat4 skin = readBoneMatrix(clamp(aBoneIndices.x, 0.0, maxBone)) * aBoneWeights.x +
                        readBoneMatrix(clamp(aBoneIndices.y, 0.0, maxBone)) * aBoneWeights.y +
                        readBoneMatrix(clamp(aBoneIndices.z, 0.0, maxBone)) * aBoneWeights.z +
                        readBoneMatrix(clamp(aBoneIndices.w, 0.0, maxBone)) * aBoneWeights.w;
            localPosition = skin * localPosition;
            localNormal = normalize(mat3(skin) * localNormal);
            localTangent = normalize(mat3(skin) * localTangent);
        }
        world = model * localPosition;
        mat3 normalMatrix = mat3(model);
        normalWorld = normalize(normalMatrix * localNormal);
        tangentWorld = normalize(normalMatrix * localTangent);
    }
    if (uClientAnimationEnabled > 0.5)
    {
        float phase = world.x * 0.033 + world.z * 0.047;
        world.x += sin(uClientAnimationTime + phase) * uClientAnimationAmplitude;
        world.z += cos(uClientAnimationTime * 0.7 + phase * 1.7) * uClientAnimationAmplitude;
    }
    vWorldPos = world.xyz;
    vNormal = normalWorld;
    vTangent = tangentWorld;
    vTexCoord0 = aTexCoord0;
    vVertexColor = aVertexColor;
    vVariantIndex = 0.0;
    vMaterialSlot = aMaterialSlot;
    vUsePaletteTexture = 0.0;
    if (uUseHighScaleState > 0.5)
    {
        if (uUseDirectStateColor > 0.5)
        {
            vVariantIndex = 0.0;
            vUsePaletteTexture = 0.0;
            vColor = aInstanceState;
        }
        else
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
uniform vec4 uDistanceFadeParams;
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
varying vec3 vNormal;
varying vec3 vTangent;
varying vec2 vTexCoord0;
varying vec4 vColor;
varying vec4 vVertexColor;
varying float vVariantIndex;
varying float vMaterialSlot;
varying float vUsePaletteTexture;
void main()
{
    vec4 materialColor = vColor * vVertexColor;
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
    if (uDistanceFadeParams.x > 0.0)
    {
        float distanceToCamera = distance(uCameraPosition, vWorldPos);
        if (distanceToCamera > uDistanceFadeParams.x) discard;
        if (uDistanceFadeParams.w > 0.5 && uDistanceFadeParams.z > 0.001 && distanceToCamera > uDistanceFadeParams.y)
        {
            materialColor.a *= clamp(1.0 - ((distanceToCamera - uDistanceFadeParams.y) / max(uDistanceFadeParams.z, 0.001)), 0.0, 1.0);
        }
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
        outColor = outColor * clamp(light, 0.0, 3.0) + uSpecularColor * specular * uSpecularParams.x * specScale + emissive;
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
        vec3 mapped = max(outColor * exposure, vec3(0.0));
        if (uPostProcessParams.w < 1.5)
        {
            outColor = mapped / (vec3(1.0) + mapped);
        }
        else if (uPostProcessParams.w < 2.5)
        {
            outColor = vec3(1.0) - exp(-mapped);
        }
        else
        {
            outColor = clamp(
                (mapped * (2.51 * mapped + vec3(0.03))) /
                (mapped * (2.43 * mapped + vec3(0.59)) + vec3(0.14)),
                0.0,
                1.0);
        }
        outColor = pow(max(outColor, vec3(0.0)), vec3(1.0 / gamma));
    }
    gl_FragColor = vec4(outColor, materialColor.a);
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
uniform float uProjectionScaleX;
uniform float uProjectionScaleY;
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
        gl_FragColor = vec4(uHorizonColor * max(uIntensity, 0.0), 1.0);
        return;
    }
    vec2 screen = vUv * 2.0 - 1.0;
    vec3 dir = normalize(uCameraForward + uCameraRight * screen.x * uProjectionScaleX + uCameraUp * screen.y * uProjectionScaleY);
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
    float t = clamp(uv.y, 0.0, 1.0);
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
