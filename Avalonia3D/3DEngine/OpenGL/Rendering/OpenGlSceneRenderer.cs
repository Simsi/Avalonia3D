using System;
using System.Buffers;
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
    private const int GlUnsignedShort = 0x1403;
    private const int GlArrayBuffer = 0x8892;
    private const int GlElementArrayBuffer = 0x8893;
    private const int GlStaticDraw = 0x88E4;
    private const int GlDynamicDraw = 0x88E8;
    private const int GlDepthTest = 0x0B71;
    private const int GlCullFace = 0x0B44;
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
    private const int MeshVertexFloatStride = 25;
    private const int MeshVertexByteStride = MeshVertexFloatStride * sizeof(float);
    private const int MeshPositionOffsetBytes = 0;
    private const int MeshNormalOffsetBytes = 3 * sizeof(float);
    private const int MeshTexCoordOffsetBytes = 6 * sizeof(float);
    private const int MeshTangentOffsetBytes = 8 * sizeof(float);
    private const int MeshVertexColorOffsetBytes = 12 * sizeof(float);
    private const int MeshMaterialSlotOffsetBytes = 16 * sizeof(float);
    private const int MeshBoneIndexOffsetBytes = 17 * sizeof(float);
    private const int MeshBoneWeightOffsetBytes = 21 * sizeof(float);
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

    // Desktop OpenGL backends used by Avalonia may expose VAO support while keeping
    // divisor/attribute state in a way that is not stable across context rebinds. The
    // engine binds vertex attributes explicitly per draw; this is a little more CPU
    // work, but it removes a deterministic "draws once then disappears" failure mode.
    private const bool DisableOpenGlVertexArraysForStability = true;

    // Small/medium high-scale scenes are safer as explicit draws on Desktop GL. Large
    // digital-twin layers still use the instanced retained path when it validates.
    private const int StableHighScaleLegacyInstanceThreshold = 2048;
    private const int InstancedDrawValidationBudgetInitial = 256;
    private static readonly HighScaleChunkKey3D AggregateChunkKey = new(int.MinValue, int.MinValue, int.MinValue);

    private readonly Dictionary<string, MeshGpuResource> _meshResources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControlTextureResource> _controlTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MaterialTextureResource> _materialTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HighScalePaletteTextureResource> _highScalePaletteTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<MeshBatchKey, MeshBatchData> _meshBatches = new();
    private readonly Dictionary<MeshBatchKey, ParticleBatchData> _particleBatches = new();
    private readonly Dictionary<HighScaleBatchKey, HighScaleGpuBatchData> _highScaleGpuBatches = new();
    private readonly float[] _matrixUploadBuffer = new float[16];
    private readonly float[] _controlVertexData = new float[20];
    private readonly List<ControlPlaneRenderItem3D> _controlPlaneScratch = new(16);
    private readonly Vector3[] _controlCornerScratch = new Vector3[4];
    private readonly HashSet<string> _liveMeshSweepScratch = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveControlPlaneSweepScratch = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveMaterialTextureSweepScratch = new(StringComparer.Ordinal);
    private readonly List<string> _meshSweepScratch = new();
    private readonly List<string> _textureSweepScratch = new();
    private readonly List<MeshBatchKey> _batchRemovalScratch = new();
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private int _lastSweptRegistryVersion = -1;
    private int _lastSweptBatchContentVersion = -1;
    private int _lastBuiltOrdinarySceneChangeVersion = -1;
    private int _lastBuiltOrdinaryTransformVersion = -1;
    private int _lastBuiltOrdinaryParticleVersion = -1;
    private int _lastBuiltOrdinaryRegistryVersion = -1;
    private int _lastBuiltOrdinaryInterpolationVersion = -1;
    private int _lastBuiltOrdinaryCameraVersion = -1;
    private bool _hasTransparentOrdinaryBatches;
    private bool _hasCameraDependentParticleBatches;
    private readonly OrdinaryBatchStatsCache _ordinaryBatchStatsCache = new();
    private readonly HighScaleLodSelectionPlan3D _highScaleLodPlanScratch = new();
    private readonly List<int> _highScaleShadowInstanceScratch = new(1024);
    private readonly RenderStats _shadowHighScalePlanningStats = new();
    private readonly List<CachedOpenGlDrawCommand> _cachedDrawCommands = new(384);
    private readonly List<CachedOpenGlDrawCommand> _frameDrawCommandScratch = new(384);
    private readonly Dictionary<string, RetainedOrdinarySlotRef> _ordinarySlotByObjectId = new(StringComparer.Ordinal);
    private readonly List<Object3D> _ordinaryTransformDirtyScratch = new(256);
    private int[] _particleSortOrderScratch = Array.Empty<int>();
    private float[] _particleSortKeyScratch = Array.Empty<float>();
    private int _highScaleTransformBatchUploadsThisFrame;

    private int _meshProgram;
    private int _texturedProgram;
    private int _shadowProgram;
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
    private int _shadowPositionLocation;
    private int _shadowInstanceModel0Location;
    private int _shadowInstanceModel1Location;
    private int _shadowInstanceModel2Location;
    private int _shadowInstanceModel3Location;
    private int _shadowBoneIndicesLocation;
    private int _shadowBoneWeightsLocation;
    private int _shadowModelLocation;
    private int _shadowPartLocalLocation;
    private int _shadowUsePartLocalLocation;
    private int _shadowLightViewProjLocation;
    private int _shadowParticleBillboardLocation;
    private int _shadowParticleCameraRightLocation;
    private int _shadowParticleCameraUpLocation;
    private int _shadowUseInstancingLocation;
    private int _shadowSkinningEnabledLocation;
    private int _shadowBoneTextureLocation;
    private int _shadowBoneTextureHeightLocation;
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
    private int _skyboxVertexArray;
    private int _skyboxVertexBuffer;
    private int _skyboxIndexBuffer;
    private DirectionalShadowMapResource? _directionalShadowMap;
    private int _controlVertexArray;
    private int _controlVertexBuffer;
    private int _controlIndexBuffer;
    private MeshGpuResource? _lastMeshAttributeResource;
    private MeshGpuResource? _lastShadowAttributeResource;
    private int _paletteTexture;
    private byte[] _paletteUploadBuffer = Array.Empty<byte>();
    private byte[] _controlBgraUploadBuffer = Array.Empty<byte>();
    private byte[] _controlRgbaUploadBuffer = Array.Empty<byte>();
    private GlInterface? _lastGl;
    private bool _initialized;
    private bool _supportsInstancing;
    private bool _instancedDrawPathBroken;
    private int _instancedDrawValidationBudget = InstancedDrawValidationBudgetInitial;
    private int _instancedDrawFailureCount;
    private bool _supportsBoneTextureSkinning;
    private int _gpuSkinTextureBoneLimit = 0;
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
    private GlGenVertexArraysDelegate? _genVertexArrays;
    private GlBindVertexArrayDelegate? _bindVertexArray;
    private GlDeleteVertexArraysDelegate? _deleteVertexArrays;
    private GlDisableVertexAttribArrayDelegate? _disableVertexAttribArray;
    private GlGetIntegervDelegate? _getIntegerv;
    private GlGetErrorDelegate? _getError;
    private bool _supportsVertexArrays;
    private int _boundVertexArray;

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

        _blendFunc = LoadDelegate<GlBlendFuncDelegate>(gl, "glBlendFunc");
        _depthMask = LoadDelegate<GlDepthMaskDelegate>(gl, "glDepthMask");
        _disable = LoadDelegate<GlDisableDelegate>(gl, "glDisable");
        _uniform1i = LoadDelegate<GlUniform1iDelegate>(gl, "glUniform1i");
        _uniform1f = LoadDelegate<GlUniform1fDelegate>(gl, "glUniform1f");
        _uniform4f = LoadDelegate<GlUniform4fDelegate>(gl, "glUniform4f");
        _uniform3f = LoadDelegate<GlUniform3fDelegate>(gl, "glUniform3f");
        _uniformMatrix4fv = LoadDelegate<GlUniformMatrix4fvDelegate>(gl, "glUniformMatrix4fv");
        _vertexAttribDivisor = LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisor")
                                ?? LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisorARB")
                                ?? LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisorEXT")
                                ?? LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisorANGLE")
                                ?? LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisorOES");
        _drawElementsInstanced = LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstanced")
                                 ?? LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstancedARB")
                                 ?? LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstancedEXT")
                                 ?? LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstancedANGLE")
                                 ?? LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstancedOES");
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
        _genVertexArrays = LoadDelegate<GlGenVertexArraysDelegate>(gl, "glGenVertexArrays")
                           ?? LoadDelegate<GlGenVertexArraysDelegate>(gl, "glGenVertexArraysOES")
                           ?? LoadDelegate<GlGenVertexArraysDelegate>(gl, "glGenVertexArraysAPPLE");
        _bindVertexArray = LoadDelegate<GlBindVertexArrayDelegate>(gl, "glBindVertexArray")
                           ?? LoadDelegate<GlBindVertexArrayDelegate>(gl, "glBindVertexArrayOES")
                           ?? LoadDelegate<GlBindVertexArrayDelegate>(gl, "glBindVertexArrayAPPLE");
        _deleteVertexArrays = LoadDelegate<GlDeleteVertexArraysDelegate>(gl, "glDeleteVertexArrays")
                              ?? LoadDelegate<GlDeleteVertexArraysDelegate>(gl, "glDeleteVertexArraysOES")
                              ?? LoadDelegate<GlDeleteVertexArraysDelegate>(gl, "glDeleteVertexArraysAPPLE");
        _disableVertexAttribArray = LoadDelegate<GlDisableVertexAttribArrayDelegate>(gl, "glDisableVertexAttribArray");
        _getIntegerv = LoadDelegate<GlGetIntegervDelegate>(gl, "glGetIntegerv");
        _getError = LoadDelegate<GlGetErrorDelegate>(gl, "glGetError");
        var nativeVertexArraysAvailable = _genVertexArrays is not null && _bindVertexArray is not null && _deleteVertexArrays is not null;
        _supportsVertexArrays = nativeVertexArraysAvailable && !DisableOpenGlVertexArraysForStability;
        _supportsInstancing = _vertexAttribDivisor is not null && _drawElementsInstanced is not null;
        _instancedDrawPathBroken = false;
        _instancedDrawValidationBudget = InstancedDrawValidationBudgetInitial;
        _instancedDrawFailureCount = 0;

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
        _meshShadowEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uShadowEnabled");
        _meshShadowMapLocation = gl.GetUniformLocationString(_meshProgram, "uShadowMap");
        _meshLightViewProjLocation = gl.GetUniformLocationString(_meshProgram, "uLightViewProj");
        _meshShadowParamsLocation = gl.GetUniformLocationString(_meshProgram, "uShadowParams");

        _shadowProgram = CreateProgram(gl, ShadowVertexSource, ShadowFragmentSource,
            (0, "aPosition"), (2, "aInstanceModel0"), (3, "aInstanceModel1"), (4, "aInstanceModel2"), (5, "aInstanceModel3"),
            (12, "aBoneIndices"), (13, "aBoneWeights"));
        _shadowPositionLocation = gl.GetAttribLocationString(_shadowProgram, "aPosition");
        _shadowInstanceModel0Location = gl.GetAttribLocationString(_shadowProgram, "aInstanceModel0");
        _shadowInstanceModel1Location = gl.GetAttribLocationString(_shadowProgram, "aInstanceModel1");
        _shadowInstanceModel2Location = gl.GetAttribLocationString(_shadowProgram, "aInstanceModel2");
        _shadowInstanceModel3Location = gl.GetAttribLocationString(_shadowProgram, "aInstanceModel3");
        _shadowBoneIndicesLocation = gl.GetAttribLocationString(_shadowProgram, "aBoneIndices");
        _shadowBoneWeightsLocation = gl.GetAttribLocationString(_shadowProgram, "aBoneWeights");
        _shadowModelLocation = gl.GetUniformLocationString(_shadowProgram, "uModel");
        _shadowPartLocalLocation = gl.GetUniformLocationString(_shadowProgram, "uPartLocal");
        _shadowUsePartLocalLocation = gl.GetUniformLocationString(_shadowProgram, "uUsePartLocal");
        _shadowLightViewProjLocation = gl.GetUniformLocationString(_shadowProgram, "uLightViewProj");
        _shadowUseInstancingLocation = gl.GetUniformLocationString(_shadowProgram, "uUseInstancing");
        _shadowSkinningEnabledLocation = gl.GetUniformLocationString(_shadowProgram, "uSkinningEnabled");
        _shadowBoneTextureLocation = gl.GetUniformLocationString(_shadowProgram, "uBoneTexture");
        _shadowBoneTextureHeightLocation = gl.GetUniformLocationString(_shadowProgram, "uBoneTextureHeight");
        _shadowParticleBillboardLocation = gl.GetUniformLocationString(_shadowProgram, "uParticleBillboard");
        _shadowParticleCameraRightLocation = gl.GetUniformLocationString(_shadowProgram, "uParticleCameraRight");
        _shadowParticleCameraUpLocation = gl.GetUniformLocationString(_shadowProgram, "uParticleCameraUp");

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

        _paletteTexture = gl.GenTexture();
        _controlVertexBuffer = gl.GenBuffer();
        _controlIndexBuffer = gl.GenBuffer();
        _skyboxVertexBuffer = gl.GenBuffer();
        _skyboxIndexBuffer = gl.GenBuffer();
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
        _disable?.Invoke(GlCullFace);
        gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);

        var frame = SceneRenderFrameContext3D.Build(scene, width, height, BackendKind.OpenGlDesktop);
        var viewProjection = frame.ViewProjection;
        var pipeline = frame.Pipeline;

        SweepUnusedResources(gl, scene, frame.Snapshot);
        var stats = frame.CreateBaseStats();
        var batchPlanNeeded = RequiresOrdinaryBatchPlan(frame);
        var overlayPlanNeeded = scene.Debug.ShowWireframeOverlay || scene.Debug.ShowSilhouetteOverlay;
        var plan = SceneRenderPlanBuilder3D.Build(
            frame,
            RequiresCpuSkinFallback,
            batchPlanNeeded ? stats : null,
            includeOrdinary: batchPlanNeeded || overlayPlanNeeded,
            includeParticles: batchPlanNeeded,
            includeHighScale: true,
            frustumCullParticles: false);
        if (scene.Debug.ShowPerformanceMetrics)
        {
            ApplyAnimationStats(stats, scene, gpuSkinningActive: _supportsBoneTextureSkinning, fallbackReason: _supportsBoneTextureSkinning ? string.Empty : "OpenGL bone-texture skinning unavailable; CPU skinned fallback mesh used");
        }
        SceneRenderStats3D.ApplyPipelineStats(stats, scene, pipeline);

        BuildBatches(gl, plan, stats);
        var shadow = plan.Shadow;
        RenderDirectionalShadowMap(gl, plan, stats);

        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);
        gl.Clear(GlColorBufferBit | GlDepthBufferBit);
        DrawSkybox(gl, scene, stats);
        DrawMeshes(gl, plan, stats, shadow, pipeline);
        DrawSurfaceOverlays(gl, plan, stats);
        DrawControlPlanes(gl, plan, stats);

        BindVertexArray(0);
        gl.BindBuffer(GlArrayBuffer, 0);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        gl.BindTexture(GlTexture2D, 0);
        gl.UseProgram(0);
        return stats;
    }

    public void Deinitialize(GlInterface gl)
    {
        foreach (var resource in _meshResources.Values) DisposeMeshResource(gl, resource);
        foreach (var texture in _controlTextures.Values) texture.Dispose(gl);
        foreach (var texture in _materialTextures.Values) texture.Dispose(gl);
        foreach (var texture in _highScalePaletteTextures.Values) texture.Dispose(gl);
        foreach (var batch in _meshBatches.Values) batch.Dispose(gl);
        foreach (var batch in _particleBatches.Values) batch.Dispose(gl);
        foreach (var batch in _highScaleGpuBatches.Values) batch.Dispose(gl);
        _directionalShadowMap?.Dispose(gl, _deleteFramebuffers);
        _directionalShadowMap = null;
        _meshResources.Clear();
        _controlTextures.Clear();
        _materialTextures.Clear();
        _highScalePaletteTextures.Clear();
        _meshBatches.Clear();
        _particleBatches.Clear();
        _highScaleGpuBatches.Clear();
        _cachedDrawCommands.Clear();
        _frameDrawCommandScratch.Clear();
        _ordinarySlotByObjectId.Clear();
        _ordinaryTransformDirtyScratch.Clear();
        if (_paletteTexture != 0) gl.DeleteTexture(_paletteTexture);
        DeleteVertexArray(_controlVertexArray);
        DeleteVertexArray(_skyboxVertexArray);
        if (_controlVertexBuffer != 0) gl.DeleteBuffer(_controlVertexBuffer);
        if (_controlIndexBuffer != 0) gl.DeleteBuffer(_controlIndexBuffer);
        if (_skyboxVertexBuffer != 0) gl.DeleteBuffer(_skyboxVertexBuffer);
        if (_skyboxIndexBuffer != 0) gl.DeleteBuffer(_skyboxIndexBuffer);
        if (_meshProgram != 0) gl.DeleteProgram(_meshProgram);
        if (_texturedProgram != 0) gl.DeleteProgram(_texturedProgram);
        if (_shadowProgram != 0) gl.DeleteProgram(_shadowProgram);
        if (_skyboxProgram != 0) gl.DeleteProgram(_skyboxProgram);
        _controlVertexArray = _skyboxVertexArray = 0;
        _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = _shadowProgram = _skyboxProgram = 0;
        _skyboxVertexBuffer = _skyboxIndexBuffer = 0;
        _paletteTexture = 0;
        _lastMeshAttributeResource = null;
        _lastShadowAttributeResource = null;
        _boundVertexArray = 0;
        _lastBuiltOrdinarySceneChangeVersion = -1;
        _lastBuiltOrdinaryTransformVersion = -1;
        _lastBuiltOrdinaryParticleVersion = -1;
        _lastBuiltOrdinaryRegistryVersion = -1;
        _lastBuiltOrdinaryInterpolationVersion = -1;
        _lastBuiltOrdinaryCameraVersion = -1;
        _hasTransparentOrdinaryBatches = false;
        _hasCameraDependentParticleBatches = false;
        _ordinaryBatchStatsCache.Reset();
        _supportsBoneTextureSkinning = false;
        _gpuSkinTextureBoneLimit = 0;
        _instancedDrawPathBroken = false;
        _instancedDrawValidationBudget = InstancedDrawValidationBudgetInitial;
        _instancedDrawFailureCount = 0;
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
        _initialized = false;
        _meshResources.Clear();
        _controlTextures.Clear();
        _materialTextures.Clear();
        _highScalePaletteTextures.Clear();
        _meshBatches.Clear();
        _particleBatches.Clear();
        _highScaleGpuBatches.Clear();
        _cachedDrawCommands.Clear();
        _frameDrawCommandScratch.Clear();
        _ordinarySlotByObjectId.Clear();
        _ordinaryTransformDirtyScratch.Clear();
        _directionalShadowMap = null;
        _lastMeshAttributeResource = null;
        _lastShadowAttributeResource = null;
        _boundVertexArray = 0;
        _controlVertexArray = _skyboxVertexArray = 0;
        _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = _shadowProgram = _skyboxProgram = 0;
        _skyboxVertexBuffer = _skyboxIndexBuffer = 0;
        _paletteTexture = 0;
        _lastSweptRegistryVersion = -1;
        _lastSweptBatchContentVersion = -1;
        _lastBuiltOrdinarySceneChangeVersion = -1;
        _lastBuiltOrdinaryTransformVersion = -1;
        _lastBuiltOrdinaryParticleVersion = -1;
        _lastBuiltOrdinaryRegistryVersion = -1;
        _lastBuiltOrdinaryInterpolationVersion = -1;
        _lastBuiltOrdinaryCameraVersion = -1;
        _hasTransparentOrdinaryBatches = false;
        _hasCameraDependentParticleBatches = false;
        _ordinaryBatchStatsCache.Reset();
        _supportsInstancing = false;
        _instancedDrawPathBroken = false;
        _instancedDrawValidationBudget = InstancedDrawValidationBudgetInitial;
        _instancedDrawFailureCount = 0;
        _supportsVertexArrays = false;
        _supportsBoneTextureSkinning = false;
        _gpuSkinTextureBoneLimit = 0;
        _vertexAttribDivisor = null;
        _drawElementsInstanced = null;
        _bufferSubData = null;
        _genVertexArrays = null;
        _bindVertexArray = null;
        _deleteVertexArrays = null;
        _disableVertexAttribArray = null;
        _getIntegerv = null;
        _getError = null;
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

        foreach (var obj in snapshot.Renderables)
        {
            if (obj is ParticleSystem3D liveParticles)
            {
                liveParticles.SetBillboardBasis(scene.Camera.Right, scene.Camera.SafeUp, scene.Camera.Forward);
            }
        }
        SceneRenderResourceCollector3D.CollectLiveMeshesAndTextures(scene, snapshot, liveMeshes, liveMaterialTextures, RequiresCpuSkinFallback);
        foreach (var obj in snapshot.AllObjects)
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
            DisposeMeshResource(gl, _meshResources[key]);
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
        _lastSweptBatchContentVersion = batchContentVersion;
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
        if (_skyboxVertexArray != 0)
        {
            BindVertexArray(_skyboxVertexArray);
        }
        else
        {
            BindVertexArray(0);
            gl.BindBuffer(GlArrayBuffer, _skyboxVertexBuffer);
            gl.EnableVertexAttribArray(_skyboxPositionLocation);
            gl.VertexAttribPointer(_skyboxPositionLocation, 2, GlFloat, 0, sizeof(float) * 2, IntPtr.Zero);
            gl.BindBuffer(GlElementArrayBuffer, _skyboxIndexBuffer);
        }
        gl.DrawElements(GlTriangles, 6, GlUnsignedShort, IntPtr.Zero);
        _depthMask?.Invoke(1);
        gl.Enable(GlDepthTest);

        stats.SkyboxEnabled = true;
        stats.SkyboxMode = (int)skybox.Mode;
        stats.SkyboxDrawCalls++;
        stats.DrawCallCount++;
    }


    private void RenderDirectionalShadowMap(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        var scene = plan.Frame.Scene;
        var shadow = plan.Shadow;
        stats.DirectionalShadowEnabled = shadow.IsEnabled;
        stats.ShadowMapReason = shadow.Reason;
        if (!shadow.IsEnabled || _shadowProgram == 0) return;

        RebuildFrameShadowCommandScratch(plan);
        if (!HasShadowCasterCommands())
        {
            stats.ShadowMapReason = "no-shadow-casters";
            return;
        }

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
        _lastShadowAttributeResource = null;
        _highScaleTransformBatchUploadsThisFrame = 0;
        UploadMatrix(_uniformMatrix4fv, _shadowLightViewProjLocation, shadow.LightViewProjection, _matrixUploadBuffer);
        UploadMatrix(_uniformMatrix4fv, _shadowPartLocalLocation, Matrix4x4.Identity, _matrixUploadBuffer);
        UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _shadowParticleBillboardLocation, 0f);
        UploadVector3(_uniform3f, _shadowParticleCameraRightLocation, scene.Camera.Right);
        UploadVector3(_uniform3f, _shadowParticleCameraUpLocation, scene.Camera.SafeUp);

        UploadFloat(_uniform1f, _shadowUseInstancingLocation, CanUseInstancedDrawPath ? 1f : 0f);
        DrawInstancedShadowCommandStream(gl, scene, plan.Frame.ViewProjection, stats);
        ResetShadowAttributeDivisors();

        UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _shadowParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _shadowSkinningEnabledLocation, 0f);

        watch.Stop();
        stats.ShadowMapCount = 1;
        stats.ShadowMapResolution = resource.Resolution;
        stats.ShadowMapMilliseconds = watch.Elapsed.TotalMilliseconds;
        stats.ShadowMapReason = shadow.Reason;
    }

    private bool HasShadowCasterCommands()
    {
        for (var i = 0; i < _frameDrawCommandScratch.Count; i++)
        {
            var command = _frameDrawCommandScratch[i];
            if (command.MeshBatch is { InstanceCount: > 0 }) return true;
            if (command.ParticleBatch is { InstanceCount: > 0 }) return true;
            if (command.Kind == SceneRenderCommandKind3D.HighScaleLayer && command.HighScaleLayer is not null) return true;
        }

        return false;
    }

    private void DrawInstancedShadowCommandStream(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        for (var i = 0; i < _frameDrawCommandScratch.Count; i++)
        {
            var command = _frameDrawCommandScratch[i];
            switch (command.Kind)
            {
                case SceneRenderCommandKind3D.OrdinaryBatch:
                case SceneRenderCommandKind3D.TransparentOrdinaryItem:
                case SceneRenderCommandKind3D.TransparentOrdinaryBatch:
                    if (command.MeshBatch is not null)
                    {
                        DrawShadowMeshBatchInstanced(gl, command.MeshBatch, stats);
                    }
                    break;
                case SceneRenderCommandKind3D.ParticleSystem:
                    if (command.ParticleBatch is not null)
                    {
                        DrawShadowParticleBatchInstanced(gl, command.ParticleBatch, stats);
                    }
                    break;
                case SceneRenderCommandKind3D.HighScaleLayer:
                    if (command.HighScaleLayer is not null)
                    {
                        DrawShadowHighScaleLayer(gl, scene, viewProjection, command.HighScaleLayer, scene.Camera.Position, stats);
                    }
                    break;
            }
        }
    }

    private void DrawLegacyShadowCommandStream(GlInterface gl, RenderStats stats)
    {
        for (var i = 0; i < _frameDrawCommandScratch.Count; i++)
        {
            var command = _frameDrawCommandScratch[i];
            switch (command.Kind)
            {
                case SceneRenderCommandKind3D.OrdinaryBatch:
                case SceneRenderCommandKind3D.TransparentOrdinaryItem:
                case SceneRenderCommandKind3D.TransparentOrdinaryBatch:
                    if (command.MeshBatch is not null)
                    {
                        DrawShadowMeshBatchLegacy(gl, command.MeshBatch, stats);
                    }
                    break;
                case SceneRenderCommandKind3D.ParticleSystem:
                    if (command.ParticleBatch is not null)
                    {
                        DrawShadowParticleBatchLegacy(gl, command.ParticleBatch, stats);
                    }
                    break;
                case SceneRenderCommandKind3D.HighScaleLayer:
                    // The legacy non-instanced fallback intentionally does not explode high-scale
                    // retained layers into per-object shadow draws. Instanced OpenGL covers them.
                    break;
            }
        }
    }

    private void DrawShadowMeshBatchInstanced(GlInterface gl, MeshBatchData batch, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;
        if (!CanUseInstancedDrawPath)
        {
            UploadFloat(_uniform1f, _shadowUseInstancingLocation, 0f);
            DrawShadowMeshBatchLegacy(gl, batch, stats);
            return;
        }

        var mesh = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        BindShadowAttributes(gl, mesh);
        UploadShadowBatchSkinning(gl, batch);
        EnsureBatchInstanceBuffer(gl, batch, stats);
        UploadFloat(_uniform1f, _shadowParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 0f);
        gl.BindBuffer(GlArrayBuffer, batch.InstanceBuffer);
        EnableShadowInstanceAttributes(gl);
        if (!TryDrawElementsInstanced(gl, mesh.IndexCount, mesh.IndexType, batch.InstanceCount, batch.MeshKey, "shadow-mesh"))
        {
            DisableShadowInstanceAttributes();
            UploadFloat(_uniform1f, _shadowUseInstancingLocation, 0f);
            DrawShadowMeshBatchLegacy(gl, batch, stats);
            return;
        }

        DisableShadowInstanceAttributes();
        stats.ShadowCasterCount += batch.InstanceCount;
    }

    private void DrawShadowParticleBatchInstanced(GlInterface gl, ParticleBatchData batch, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;
        if (!CanUseInstancedDrawPath)
        {
            UploadFloat(_uniform1f, _shadowUseInstancingLocation, 0f);
            DrawShadowParticleBatchLegacy(gl, batch, stats);
            return;
        }

        var mesh = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        UploadFloat(_uniform1f, _shadowSkinningEnabledLocation, 0f);
        UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _shadowParticleBillboardLocation, batch.Billboard ? 1f : 0f);
        BindShadowAttributes(gl, mesh);
        EnsureBatchInstanceBuffer(gl, batch, stats);
        gl.BindBuffer(GlArrayBuffer, batch.InstanceBuffer);
        if (batch.Billboard) EnableShadowParticleBillboardInstanceAttributes(gl);
        else EnableShadowInstanceAttributes(gl);
        if (!TryDrawElementsInstanced(gl, mesh.IndexCount, mesh.IndexType, batch.InstanceCount, batch.MeshKey, "shadow-particle"))
        {
            DisableShadowInstanceAttributes();
            UploadFloat(_uniform1f, _shadowUseInstancingLocation, 0f);
            DrawShadowParticleBatchLegacy(gl, batch, stats);
            return;
        }

        DisableShadowInstanceAttributes();
        stats.ShadowCasterCount += batch.InstanceCount;
    }

    private void DrawShadowMeshBatchLegacy(GlInterface gl, MeshBatchData batch, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;
        var mesh = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        BindShadowAttributes(gl, mesh);
        UploadShadowBatchSkinning(gl, batch);
        UploadFloat(_uniform1f, _shadowParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 0f);
        var data = batch.Data;
        for (var i = 0; i < batch.InstanceCount; i++)
        {
            var offset = i * batch.FloatStride;
            UploadMatrixFromInstanceData(_uniformMatrix4fv, _shadowModelLocation, data, offset, _matrixUploadBuffer);
            gl.DrawElements(GlTriangles, mesh.IndexCount, mesh.IndexType, IntPtr.Zero);
            stats.ShadowCasterCount++;
        }
    }

    private void DrawShadowParticleBatchLegacy(GlInterface gl, ParticleBatchData batch, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;
        var mesh = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        UploadFloat(_uniform1f, _shadowSkinningEnabledLocation, 0f);
        UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _shadowParticleBillboardLocation, batch.Billboard ? 1f : 0f);
        BindShadowAttributes(gl, mesh);
        var data = batch.Data;
        for (var i = 0; i < batch.InstanceCount; i++)
        {
            var offset = i * batch.FloatStride;
            if (batch.Billboard) UploadBillboardParticleMatrix(_uniformMatrix4fv, _shadowModelLocation, data, offset, _matrixUploadBuffer);
            else UploadMatrixFromInstanceData(_uniformMatrix4fv, _shadowModelLocation, data, offset, _matrixUploadBuffer);
            gl.DrawElements(GlTriangles, mesh.IndexCount, mesh.IndexType, IntPtr.Zero);
            stats.ShadowCasterCount++;
        }
    }

    private void DrawShadowHighScaleLayer(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, HighScaleInstanceLayer3D layer, Vector3 cameraPosition, RenderStats stats)
    {
        SceneHighScaleRenderPlanner3D.EnsureChunks(layer);
        var performance = scene.Performance;
        if (SceneHighScaleRenderPlanner3D.ShouldUseAggregateLayerBatches(layer, performance))
        {
            var shadowPlanningStats = ResetShadowHighScalePlanningStats();
            var lodPlan = SceneHighScaleRenderPlanner3D.BuildLayerLodPlan(layer, cameraPosition, performance, shadowPlanningStats, _highScaleLodPlanScratch);
            DrawShadowHighScaleLod(gl, layer, AggregateChunkKey, HighScaleLodLevel3D.Detailed, lodPlan.Detailed, cameraPosition, performance, stats);
            DrawShadowHighScaleLod(gl, layer, AggregateChunkKey, HighScaleLodLevel3D.Simplified, lodPlan.Simplified, cameraPosition, performance, stats);
            DrawShadowHighScaleLod(gl, layer, AggregateChunkKey, HighScaleLodLevel3D.Proxy, lodPlan.Proxy, cameraPosition, performance, stats);
            DrawShadowHighScaleLod(gl, layer, AggregateChunkKey, HighScaleLodLevel3D.Billboard, lodPlan.Billboard, cameraPosition, performance, stats);
            return;
        }

        var visibleChunks = layer.Chunks.QueryVisible(viewProjection);
        var visibleChunkLimit = SceneHighScaleRenderPlanner3D.ResolveVisibleChunkLimit(performance, visibleChunks.Count);
        for (var visibleChunkIndex = 0; visibleChunkIndex < visibleChunkLimit; visibleChunkIndex++)
        {
            var chunk = visibleChunks[visibleChunkIndex];
            var shadowPlanningStats = ResetShadowHighScalePlanningStats();
            var lodPlan = SceneHighScaleRenderPlanner3D.BuildChunkLodPlan(layer, chunk, cameraPosition, performance, shadowPlanningStats, _highScaleLodPlanScratch);
            DrawShadowHighScaleLod(gl, layer, chunk.Key, HighScaleLodLevel3D.Detailed, lodPlan.Detailed, cameraPosition, performance, stats, chunk.IsDirty);
            DrawShadowHighScaleLod(gl, layer, chunk.Key, HighScaleLodLevel3D.Simplified, lodPlan.Simplified, cameraPosition, performance, stats, chunk.IsDirty);
            DrawShadowHighScaleLod(gl, layer, chunk.Key, HighScaleLodLevel3D.Proxy, lodPlan.Proxy, cameraPosition, performance, stats, chunk.IsDirty);
            DrawShadowHighScaleLod(gl, layer, chunk.Key, HighScaleLodLevel3D.Billboard, lodPlan.Billboard, cameraPosition, performance, stats, chunk.IsDirty);
        }
    }

    private RenderStats ResetShadowHighScalePlanningStats()
    {
        var stats = _shadowHighScalePlanningStats;
        stats.HighScaleInstanceCount = 0;
        stats.LodDetailedCount = 0;
        stats.LodSimplifiedCount = 0;
        stats.LodProxyCount = 0;
        stats.LodBillboardCount = 0;
        stats.LodCulledCount = 0;
        stats.CulledObjectCount = 0;
        return stats;
    }

    private void DrawShadowHighScaleLod(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        HighScaleChunkKey3D chunkKey,
        HighScaleLodLevel3D lod,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats,
        bool structuralDirty = false)
    {
        if (instanceIndices.Count == 0) return;

        _highScaleShadowInstanceScratch.Clear();
        for (var i = 0; i < instanceIndices.Count; i++)
        {
            var instanceIndex = instanceIndices[i];
            if ((uint)instanceIndex < (uint)layer.Instances.Count && IsHighScaleVisible(layer.Instances[instanceIndex]))
            {
                _highScaleShadowInstanceScratch.Add(instanceIndex);
            }
        }

        if (_highScaleShadowInstanceScratch.Count == 0) return;

        var parts = layer.Template.ResolveParts(lod);
        if (!CanUseInstancedDrawPath || ShouldUseHighScaleLegacySafePath(_highScaleShadowInstanceScratch.Count))
        {
            DrawShadowHighScaleLegacyLod(gl, layer, parts, _highScaleShadowInstanceScratch, stats, startPartIndex: 0);
            UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 0f);
            return;
        }

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var buildStart = Stopwatch.GetTimestamp();
            var key = new HighScaleBatchKey(layer.Id, chunkKey, lod, statePartIndex: -2);
            var batch = EnsureHighScaleGpuBatch(gl, layer, key, structuralDirty, lod, _highScaleShadowInstanceScratch, cameraPosition, performance, stats, part, directStateColor: false);
            stats.HighScaleBufferBuildMilliseconds += GetElapsedMilliseconds(buildStart);
            if (batch.InstanceCount == 0) continue;

            var mesh = EnsureMeshResource(gl, part.Mesh.ResourceKey, part.Mesh.GeometryVersion, part.Mesh, stats);
            BindShadowAttributes(gl, mesh);
            UploadFloat(_uniform1f, _shadowSkinningEnabledLocation, 0f);
            UploadFloat(_uniform1f, _shadowParticleBillboardLocation, 0f);
            UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 1f);
            UploadMatrix(_uniformMatrix4fv, _shadowPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            gl.BindBuffer(GlArrayBuffer, batch.TransformBuffer);
            EnableShadowHighScaleInstanceAttributes(gl);
            var shadowHighScaleDrawn = TryDrawElementsInstanced(gl, mesh.IndexCount, mesh.IndexType, batch.InstanceCount, part.Mesh.ResourceKey, "shadow-highscale");
            DisableShadowInstanceAttributes();
            if (!shadowHighScaleDrawn)
            {
                DrawShadowHighScaleLegacyLod(gl, layer, parts, _highScaleShadowInstanceScratch, stats, partIndex);
                break;
            }

            stats.ShadowCasterCount += batch.InstanceCount;
        }

        UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 0f);
    }

    private void DrawShadowHighScaleLegacyLod(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        IReadOnlyList<CompositePartTemplate3D> parts,
        List<int> instanceIndices,
        RenderStats stats,
        int startPartIndex)
    {
        UploadFloat(_uniform1f, _shadowUseInstancingLocation, 0f);
        UploadFloat(_uniform1f, _shadowUsePartLocalLocation, 1f);
        UploadFloat(_uniform1f, _shadowParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _shadowSkinningEnabledLocation, 0f);
        DisableShadowInstanceAttributes();

        for (var partIndex = System.Math.Max(0, startPartIndex); partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var mesh = EnsureMeshResource(gl, part.Mesh.ResourceKey, part.Mesh.GeometryVersion, part.Mesh, stats);
            BindShadowAttributes(gl, mesh);
            UploadMatrix(_uniformMatrix4fv, _shadowPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);

            for (var i = 0; i < instanceIndices.Count; i++)
            {
                var instanceIndex = instanceIndices[i];
                if ((uint)instanceIndex >= (uint)layer.Instances.Count) continue;
                var record = layer.Instances[instanceIndex];
                if (!IsHighScaleVisible(record)) continue;

                UploadMatrix(_uniformMatrix4fv, _shadowModelLocation, record.Transform, _matrixUploadBuffer);
                gl.DrawElements(GlTriangles, mesh.IndexCount, mesh.IndexType, IntPtr.Zero);
                stats.ShadowCasterCount++;
            }
        }
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
        if (_supportsVertexArrays && resource.VertexArray != 0)
        {
            BindVertexArray(resource.VertexArray);
            _lastShadowAttributeResource = resource;
            _lastMeshAttributeResource = resource;
            return;
        }

        BindVertexArray(0);
        if (ReferenceEquals(_lastShadowAttributeResource, resource))
        {
            gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
            return;
        }

        _lastShadowAttributeResource = resource;
        BindMeshStaticAttributesFallback(gl, resource, meshProgram: false);
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

    private void DrawMeshes(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats, DirectionalShadowSnapshot3D shadow, RenderPipelinePlan3D pipeline)
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
        ConfigureShadowSampling(gl, shadow);
        stats.WebGlClientGpuTransformAnimation = scene.Performance.EnableWebGlClientGpuTransformAnimation;
        UploadClientTransformAnimation(scene, enabled: false);

        UploadFloat(_uniform1f, _meshUseInstancingLocation, CanUseInstancedDrawPath ? 1f : 0f);
        UploadClientTransformAnimation(scene, enabled: false);
        DrawInstancedRenderCommandStream(gl, plan, stats);
        UploadClientTransformAnimation(scene, enabled: false);
    }

    private bool RequiresOrdinaryBatchPlan(SceneRenderFrameContext3D frame)
    {
        var scene = frame.Scene;
        var interpolationVersion = scene.FrameInterpolator.RenderVersion;

        // Stability first: ordinary batch membership and draw commands are rebuilt when
        // transforms/interpolation change. The previous retained slot-only path could keep
        // stale draw commands for objects whose effective bucket changed after animation,
        // physics or selection updates, which matched the "same objects disappear after
        // the first frame" symptom. Retained slot updates remain available only for
        // frames where the ordinary plan is provably unchanged.
        return _lastBuiltOrdinarySceneChangeVersion != scene.BatchContentVersion ||
               _lastBuiltOrdinaryTransformVersion != scene.BatchTransformVersion ||
               _lastBuiltOrdinaryInterpolationVersion != interpolationVersion ||
               _lastBuiltOrdinaryParticleVersion != scene.ParticleContentVersion ||
               (_hasTransparentOrdinaryBatches && _lastBuiltOrdinaryCameraVersion != scene.CameraVersion) ||
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

            // The retained slot map could not prove that this transform-only change is
            // safe (for example the bounded dirty log was trimmed). Fall back to a full
            // ordinary/particle plan rather than silently drawing stale transforms.
            plan = SceneRenderPlanBuilder3D.Build(
                frame,
                RequiresCpuSkinFallback,
                stats,
                includeOrdinary: true,
                includeParticles: true,
                includeHighScale: false,
                frustumCullParticles: false);
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

        _hasTransparentOrdinaryBatches = plan.TransparentOrdinaryItems.Count > 0 || plan.TransparentOrdinaryBatches.Count > 0;
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
            foreach (var slot in _ordinarySlotByObjectId.Values)
            {
                if (!TryUpdateRetainedOrdinarySlot(frame, slot.Owner, slot, interpolationVersion))
                {
                    return false;
                }

                updated++;
            }
        }
        else
        {
            if (!scene.TryCopyBatchTransformChangesSince(_lastBuiltOrdinaryTransformVersion, _ordinaryTransformDirtyScratch))
            {
                return false;
            }

            for (var i = 0; i < _ordinaryTransformDirtyScratch.Count; i++)
            {
                var obj = _ordinaryTransformDirtyScratch[i];
                if (!_ordinarySlotByObjectId.TryGetValue(obj.Id, out var slot))
                {
                    if (ShouldHaveOrdinaryRetainedSlot(obj))
                    {
                        return false;
                    }

                    continue;
                }

                if (!TryUpdateRetainedOrdinarySlot(frame, obj, slot, interpolationVersion))
                {
                    return false;
                }

                updated++;
            }
        }

        if (updated > 0)
        {
            stats.RetainedTransformSlotUpdateCount += updated;
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

    private bool TryUpdateRetainedOrdinarySlot(SceneRenderFrameContext3D frame, Object3D obj, RetainedOrdinarySlotRef slot, int interpolationVersion)
    {
        var batch = slot.Batch;
        if (!batch.TryGetSlot(obj.Id, out var currentSlot) || currentSlot != slot.Slot)
        {
            return false;
        }

        var skinnedPart = obj as ModelPart3D;
        if (RequiresCpuSkinFallback(skinnedPart))
        {
            // CPU-skinned fallback geometry can change with animation.  Do not patch an
            // existing mesh slot in place unless a full ordinary plan rebuilt the fallback mesh.
            return false;
        }

        var mesh = obj.GetMesh();
        if (!ReferenceEquals(mesh, batch.Mesh) && !string.Equals(mesh.ResourceKey, batch.Mesh.ResourceKey, StringComparison.Ordinal))
        {
            return false;
        }

        var material = MaterialBinding3D.FromMaterial(obj.Material);
        if (!string.Equals(material.Key, batch.Material.Key, StringComparison.Ordinal))
        {
            return false;
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

    private bool RequiresCpuSkinFallback(ModelPart3D? part)
    {
        return part is not null &&
               part.IsSkinned &&
               part.CurrentGpuSkinMatrices.Length > 0 &&
               (!_supportsBoneTextureSkinning || part.CurrentGpuSkinMatrices.Length > _gpuSkinTextureBoneLimit);
    }

    private static void ConfigureBatchSkinning(MeshBatchData batch, OrdinaryRenderItem3D item)
    {
        var skinnedPart = item.SkinnedPart;
        if (item.UsesGpuSkinning &&
            skinnedPart is not null &&
            skinnedPart.CurrentGpuSkinMatrices.Length > 0)
        {
            batch.SetSkinning(skinnedPart.CurrentGpuSkinMatrices, skinnedPart.SkinningVersion);
        }
        else
        {
            batch.SetSkinning(Array.Empty<Matrix4x4>(), -1);
        }
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


    private void DrawLegacyRenderCommandStream(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        var scene = plan.Frame.Scene;
        if (!plan.HasVisibleOrdinary && !plan.HasVisibleParticles)
        {
            // Keep the retained command order even on partial plans.  The legacy path still
            // cannot execute high-scale, but it must not reorder cached transparent work.
            DrawLegacyCachedRenderCommandStream(gl, plan, stats);
            return;
        }

        var ignoredHighScale = false;
        for (var commandIndex = 0; commandIndex < plan.DrawCommands.Count; commandIndex++)
        {
            var command = plan.DrawCommands[commandIndex];
            switch (command.Kind)
            {
                case SceneRenderCommandKind3D.OrdinaryBatch when command.OrdinaryBatch is not null:
                {
                    var batch = GetBatch(command.OrdinaryBatch.BatchId, command.OrdinaryBatch.LogicalMeshBatchKey, command.OrdinaryBatch.Mesh, command.OrdinaryBatch.Material);
                    DrawMeshBatchLegacy(gl, batch, plan.Frame.ViewProjection, stats);
                    break;
                }
                case SceneRenderCommandKind3D.TransparentOrdinaryItem when command.TransparentOrdinary is { } transparent:
                {
                    var item = transparent.Item;
                    var batch = GetBatch(transparent.DrawId, item.LogicalMeshBatchKey, item.Mesh, item.Material);
                    DrawMeshBatchLegacy(gl, batch, plan.Frame.ViewProjection, stats);
                    break;
                }
                case SceneRenderCommandKind3D.TransparentOrdinaryBatch when command.TransparentOrdinaryBatch is { } transparentBatch:
                {
                    var batch = GetBatch(transparentBatch.BatchId, transparentBatch.LogicalMeshBatchKey, transparentBatch.Mesh, transparentBatch.Material);
                    DrawMeshBatchLegacy(gl, batch, plan.Frame.ViewProjection, stats);
                    break;
                }
                case SceneRenderCommandKind3D.ParticleSystem when command.Particle is { } particle:
                {
                    var batch = GetParticleBatch(particle.RetainedBatchId, particle.Mesh.ResourceKey, particle.Mesh, particle.Material, particle.Billboard, particle.Transparent);
                    DrawParticleBatchLegacy(gl, batch, stats);
                    break;
                }
                case SceneRenderCommandKind3D.HighScaleLayer:
                    // The non-instanced OpenGL fallback cannot execute high-scale retained
                    // chunks without exploding them into per-object draws. Keep the command
                    // visible to Core, but do not run a divergent legacy implementation here.
                    ignoredHighScale = true;
                    break;
            }
        }

        if (ignoredHighScale)
        {
            stats.HighScaleInstanceCount = 0;
        }

        ConfigureForwardBlendState(gl, false);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
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

    private void DrawLegacyCachedRenderCommandStream(GlInterface gl, SceneRenderPlan3D plan, RenderStats stats)
    {
        RebuildFrameDrawCommandScratch(plan);
        var ignoredHighScale = false;
        for (var i = 0; i < _frameDrawCommandScratch.Count; i++)
        {
            var command = _frameDrawCommandScratch[i];
            switch (command.Kind)
            {
                case SceneRenderCommandKind3D.OrdinaryBatch:
                case SceneRenderCommandKind3D.TransparentOrdinaryItem:
                case SceneRenderCommandKind3D.TransparentOrdinaryBatch:
                    if (command.MeshBatch is not null)
                    {
                        DrawMeshBatchLegacy(gl, command.MeshBatch, plan.Frame.ViewProjection, stats);
                    }
                    break;
                case SceneRenderCommandKind3D.ParticleSystem:
                    if (command.ParticleBatch is not null)
                    {
                        DrawParticleBatchLegacy(gl, command.ParticleBatch, stats);
                    }
                    break;
                case SceneRenderCommandKind3D.HighScaleLayer:
                    ignoredHighScale = true;
                    break;
            }
        }

        if (ignoredHighScale)
        {
            stats.HighScaleInstanceCount = 0;
        }

        ConfigureForwardBlendState(gl, false);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
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

    private void RebuildFrameShadowCommandScratch(SceneRenderPlan3D plan)
    {
        _frameDrawCommandScratch.Clear();
        var sourceOrder = 0;
        var cameraPosition = plan.Frame.Scene.Camera.Position;

        for (var i = 0; i < plan.ShadowCommands.Count; i++)
        {
            var command = plan.ShadowCommands[i];
            switch (command.Kind)
            {
                case SceneRenderCommandKind3D.OrdinaryBatch:
                case SceneRenderCommandKind3D.TransparentOrdinaryItem:
                case SceneRenderCommandKind3D.TransparentOrdinaryBatch:
                {
                    var cached = FindCachedMeshCommand(command.Id);
                    if (cached.MeshBatch is not null)
                    {
                        _frameDrawCommandScratch.Add(cached.WithSourceOrder(sourceOrder++).RefreshSortDistance(cameraPosition));
                    }
                    break;
                }
                case SceneRenderCommandKind3D.ParticleSystem:
                {
                    var cached = FindCachedParticleCommand(command.Id);
                    if (cached.ParticleBatch is not null)
                    {
                        _frameDrawCommandScratch.Add(cached.WithSourceOrder(sourceOrder++));
                    }
                    break;
                }
                case SceneRenderCommandKind3D.HighScaleLayer:
                    if (command.HighScaleLayer is not null)
                    {
                        _frameDrawCommandScratch.Add(CachedOpenGlDrawCommand.ForHighScale(command, sourceOrder++));
                    }
                    break;
            }
        }

        // Partial plans intentionally omit ordinary/particle Core extraction. Retained
        // shadow casting must still include cached ordinary and particle commands.
        if (!plan.IncludesOrdinary || !plan.IncludesParticles)
        {
            for (var i = 0; i < _cachedDrawCommands.Count; i++)
            {
                var cached = _cachedDrawCommands[i];
                if (ContainsShadowCommand(cached.Id, cached.Kind)) continue;
                _frameDrawCommandScratch.Add(cached.WithSourceOrder(sourceOrder++).RefreshSortDistance(cameraPosition));
            }
        }
    }

    private CachedOpenGlDrawCommand FindCachedMeshCommand(string id)
    {
        for (var i = 0; i < _cachedDrawCommands.Count; i++)
        {
            var command = _cachedDrawCommands[i];
            if ((command.Kind == SceneRenderCommandKind3D.OrdinaryBatch || command.Kind == SceneRenderCommandKind3D.TransparentOrdinaryItem || command.Kind == SceneRenderCommandKind3D.TransparentOrdinaryBatch) &&
                string.Equals(command.Id, id, StringComparison.Ordinal))
            {
                return command;
            }
        }

        return default;
    }

    private CachedOpenGlDrawCommand FindCachedParticleCommand(string id)
    {
        for (var i = 0; i < _cachedDrawCommands.Count; i++)
        {
            var command = _cachedDrawCommands[i];
            if (command.Kind == SceneRenderCommandKind3D.ParticleSystem &&
                string.Equals(command.Id, id, StringComparison.Ordinal))
            {
                return command;
            }
        }

        return default;
    }

    private bool ContainsShadowCommand(string id, SceneRenderCommandKind3D kind)
    {
        for (var i = 0; i < _frameDrawCommandScratch.Count; i++)
        {
            var command = _frameDrawCommandScratch[i];
            if (command.Kind == kind && string.Equals(command.Id, id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanUseInstancedDrawPath => _supportsInstancing && !_instancedDrawPathBroken && _drawElementsInstanced is not null && _vertexAttribDivisor is not null;

    private bool ShouldValidateInstancedDraw => _getError is not null && _instancedDrawValidationBudget > 0;

    private bool TryDrawElementsInstanced(GlInterface gl, int indexCount, int indexType, int instanceCount, string meshKey, string scope)
    {
        if (!CanUseInstancedDrawPath || indexCount <= 0 || instanceCount <= 0)
        {
            return false;
        }

        var validate = ShouldValidateInstancedDraw;
        if (validate)
        {
            var preError = DrainGlErrors();
            if (preError != 0)
            {
                MarkInstancedDrawPathBroken($"pre-draw GL error 0x{preError:X} scope={scope} mesh={meshKey}");
                return false;
            }
        }

        _drawElementsInstanced!(GlTriangles, indexCount, indexType, IntPtr.Zero, instanceCount);

        if (!validate)
        {
            return true;
        }

        var drawError = DrainGlErrors();
        if (drawError != 0)
        {
            MarkInstancedDrawPathBroken($"draw GL error 0x{drawError:X} scope={scope} mesh={meshKey}");
            return false;
        }

        _instancedDrawValidationBudget--;
        return true;
    }

    private int DrainGlErrors()
    {
        if (_getError is null) return 0;
        var first = 0;
        for (var i = 0; i < 16; i++)
        {
            var error = _getError();
            if (error == 0) break;
            if (first == 0) first = error;
        }

        return first;
    }

    private void MarkInstancedDrawPathBroken(string reason)
    {
        if (_instancedDrawPathBroken) return;
        _instancedDrawFailureCount++;
        _instancedDrawPathBroken = true;
        _instancedDrawValidationBudget = 0;
        Debug.WriteLine($"Avalonia3D OpenGL: instanced draw path disabled after validation failure: {reason}");
    }

    private void DrawMeshBatchInstanced(GlInterface gl, MeshBatchData batch, Matrix4x4 viewProjection, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;
        if (!CanUseInstancedDrawPath)
        {
            UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
            DrawMeshBatchLegacy(gl, batch, viewProjection, stats);
            return;
        }

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

        gl.BindBuffer(GlArrayBuffer, instanceBuffer);
        EnableInstanceAttributes(gl);
        UploadClassicMaterial(gl, batch.Material, stats);
        UploadBatchSkinning(gl, batch);
        if (!TryDrawElementsInstanced(gl, resource.IndexCount, resource.IndexType, visibleCount, batch.MeshKey, "mesh"))
        {
            DisableInstanceAttributes();
            UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
            DrawMeshBatchLegacy(gl, batch, viewProjection, stats);
            return;
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
            batch.CulledInstanceBuffer = gl.GenBuffer();
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
        if (!CanUseInstancedDrawPath)
        {
            UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
            DrawParticleBatchLegacy(gl, batch, stats);
            return;
        }

        ConfigureForwardBlendState(gl, batch.Transparent);
        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
        var resource = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, batch.Billboard ? 1f : 0f);
        BindMeshAttributes(gl, resource);
        EnsureBatchInstanceBuffer(gl, batch, stats);
        gl.BindBuffer(GlArrayBuffer, batch.InstanceBuffer);
        if (batch.Billboard) EnableParticleBillboardInstanceAttributes(gl);
        else EnableInstanceAttributes(gl);
        UploadClassicMaterial(gl, batch.Material, stats);
        if (!TryDrawElementsInstanced(gl, resource.IndexCount, resource.IndexType, batch.InstanceCount, batch.MeshKey, "particle"))
        {
            DisableInstanceAttributes();
            UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
            DrawParticleBatchLegacy(gl, batch, stats);
            return;
        }

        DisableInstanceAttributes();
        stats.DrawCallCount++;
        stats.EstimatedDrawCallCount++;
        stats.InstancedBatchCount++;
    }

    private void DrawMeshBatchLegacy(GlInterface gl, MeshBatchData batch, Matrix4x4 viewProjection, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;

        ConfigureForwardBlendState(gl, IsTransparent(batch.Material));
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
        var resource = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        BindMeshAttributes(gl, resource);
        DisableInstanceAttributes();
        UploadClassicMaterial(gl, batch.Material, stats);
        UploadBatchSkinning(gl, batch);

        var frustum = FrustumCuller3D.ExtractClipFrustum(viewProjection);
        var canCull = !batch.HasSkinning && batch.Mesh.LocalBounds.IsValid;
        var data = batch.Data;
        for (var i = 0; i < batch.InstanceCount; i++)
        {
            var offset = i * batch.FloatStride;
            if (canCull && !FrustumCuller3D.IntersectsLocalBounds(batch.Mesh.LocalBounds, data, offset, frustum))
            {
                stats.CulledObjectCount++;
                continue;
            }

            UploadMatrixFromInstanceData(_uniformMatrix4fv, _meshModelLocation, data, offset, _matrixUploadBuffer);
            UploadColorFromInstanceData(_uniform4f, _meshColorLocation, data, offset + 16);
            gl.DrawElements(GlTriangles, resource.IndexCount, resource.IndexType, IntPtr.Zero);
            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
        }

        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
    }

    private void DrawParticleBatchLegacy(GlInterface gl, ParticleBatchData batch, RenderStats stats)
    {
        if (batch.InstanceCount == 0) return;

        ConfigureForwardBlendState(gl, batch.Transparent);
        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, batch.Billboard ? 1f : 0f);
        var resource = EnsureMeshResource(gl, batch.Mesh.ResourceKey, batch.Mesh.GeometryVersion, batch.Mesh, stats);
        BindMeshAttributes(gl, resource);
        DisableInstanceAttributes();
        UploadClassicMaterial(gl, batch.Material, stats);

        var data = batch.Data;
        for (var i = 0; i < batch.InstanceCount; i++)
        {
            var offset = i * batch.FloatStride;
            if (batch.Billboard)
            {
                UploadBillboardParticleMatrix(_uniformMatrix4fv, _meshModelLocation, data, offset, _matrixUploadBuffer);
                UploadColorFromInstanceData(_uniform4f, _meshColorLocation, data, offset + 4);
            }
            else
            {
                UploadMatrixFromInstanceData(_uniformMatrix4fv, _meshModelLocation, data, offset, _matrixUploadBuffer);
                UploadColorFromInstanceData(_uniform4f, _meshColorLocation, data, offset + 16);
            }

            gl.DrawElements(GlTriangles, resource.IndexCount, resource.IndexType, IntPtr.Zero);
            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
        }

        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
    }

    private void EnsureBatchInstanceBuffer(GlInterface gl, InstanceBatchData batch, RenderStats stats)
    {
        if (batch.InstanceBuffer == 0)
        {
            batch.InstanceBuffer = gl.GenBuffer();
            batch.UploadedVersion = -1;
            batch.UploadedCapacityFloats = 0;
        }

        if (batch.UploadedVersion == batch.DataVersion && batch.UploadedCapacityFloats >= batch.FloatCount)
        {
            return;
        }

        gl.BindBuffer(GlArrayBuffer, batch.InstanceBuffer);
        if (_bufferSubData is not null &&
            batch.CanUploadDirtyInstanceRanges &&
            batch.UploadedCapacityFloats >= batch.FloatCount &&
            batch.FloatCount > 0)
        {
            UploadDirtyInstanceRanges(batch, stats);
        }
        else if (_bufferSubData is not null && batch.UploadedCapacityFloats >= batch.FloatCount && batch.FloatCount > 0)
        {
            UploadFloatsSubData(GlArrayBuffer, 0, batch.Data, 0, batch.FloatCount);
            stats.InstanceBufferUploads++;
            stats.InstanceUploadBytes += batch.FloatCount * sizeof(float);
        }
        else
        {
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
        dirty.Sort();

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

    private void BindMeshAttributes(GlInterface gl, MeshGpuResource resource)
    {
        if (_supportsVertexArrays && resource.VertexArray != 0)
        {
            BindVertexArray(resource.VertexArray);
            _lastMeshAttributeResource = resource;
            _lastShadowAttributeResource = resource;
            return;
        }

        BindVertexArray(0);
        if (ReferenceEquals(_lastMeshAttributeResource, resource))
        {
            gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
            return;
        }

        _lastMeshAttributeResource = resource;
        BindMeshStaticAttributesFallback(gl, resource, meshProgram: true);
    }

    private void BindMeshStaticAttributesFallback(GlInterface gl, MeshGpuResource resource, bool meshProgram)
    {
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        var positionLocation = meshProgram ? _meshPositionLocation : _shadowPositionLocation;
        gl.EnableVertexAttribArray(positionLocation);
        gl.VertexAttribPointer(positionLocation, 3, GlFloat, 0, MeshVertexByteStride, new IntPtr(MeshPositionOffsetBytes));

        if (meshProgram)
        {
            gl.EnableVertexAttribArray(_meshNormalLocation);
            gl.VertexAttribPointer(_meshNormalLocation, 3, GlFloat, 0, MeshVertexByteStride, new IntPtr(MeshNormalOffsetBytes));
            if (_meshTexCoordLocation >= 0)
            {
                gl.EnableVertexAttribArray(_meshTexCoordLocation);
                gl.VertexAttribPointer(_meshTexCoordLocation, 2, GlFloat, 0, MeshVertexByteStride, new IntPtr(MeshTexCoordOffsetBytes));
            }
            if (_meshTangentLocation >= 0)
            {
                gl.EnableVertexAttribArray(_meshTangentLocation);
                gl.VertexAttribPointer(_meshTangentLocation, 4, GlFloat, 0, MeshVertexByteStride, new IntPtr(MeshTangentOffsetBytes));
            }
            if (_meshVertexColorLocation >= 0)
            {
                gl.EnableVertexAttribArray(_meshVertexColorLocation);
                gl.VertexAttribPointer(_meshVertexColorLocation, 4, GlFloat, 0, MeshVertexByteStride, new IntPtr(MeshVertexColorOffsetBytes));
            }
            if (_meshMaterialSlotLocation >= 0)
            {
                gl.EnableVertexAttribArray(_meshMaterialSlotLocation);
                gl.VertexAttribPointer(_meshMaterialSlotLocation, 1, GlFloat, 0, MeshVertexByteStride, new IntPtr(MeshMaterialSlotOffsetBytes));
                _vertexAttribDivisor?.Invoke(_meshMaterialSlotLocation, 0);
            }
        }

        var boneIndexLocation = meshProgram ? _meshBoneIndicesLocation : _shadowBoneIndicesLocation;
        if (boneIndexLocation >= 0)
        {
            gl.EnableVertexAttribArray(boneIndexLocation);
            gl.VertexAttribPointer(boneIndexLocation, 4, GlFloat, 0, MeshVertexByteStride, new IntPtr(MeshBoneIndexOffsetBytes));
        }
        var boneWeightLocation = meshProgram ? _meshBoneWeightsLocation : _shadowBoneWeightsLocation;
        if (boneWeightLocation >= 0)
        {
            gl.EnableVertexAttribArray(boneWeightLocation);
            gl.VertexAttribPointer(boneWeightLocation, 4, GlFloat, 0, MeshVertexByteStride, new IntPtr(MeshBoneWeightOffsetBytes));
        }

        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
    }

    private void ConfigureMeshVertexArray(GlInterface gl, MeshGpuResource resource)
    {
        if (!_supportsVertexArrays || resource.VertexArray == 0) return;
        BindVertexArray(resource.VertexArray);
        BindMeshStaticAttributesFallback(gl, resource, meshProgram: true);
        BindVertexArray(0);
        _lastMeshAttributeResource = null;
        _lastShadowAttributeResource = null;
    }

    private void ConfigureStaticUtilityVertexArrays(GlInterface gl)
    {
        if (!_supportsVertexArrays) return;

        _skyboxVertexArray = CreateVertexArray();
        if (_skyboxVertexArray != 0)
        {
            BindVertexArray(_skyboxVertexArray);
            gl.BindBuffer(GlArrayBuffer, _skyboxVertexBuffer);
            gl.EnableVertexAttribArray(_skyboxPositionLocation);
            gl.VertexAttribPointer(_skyboxPositionLocation, 2, GlFloat, 0, sizeof(float) * 2, IntPtr.Zero);
            gl.BindBuffer(GlElementArrayBuffer, _skyboxIndexBuffer);
        }

        _controlVertexArray = CreateVertexArray();
        if (_controlVertexArray != 0)
        {
            BindVertexArray(_controlVertexArray);
            gl.BindBuffer(GlArrayBuffer, _controlVertexBuffer);
            gl.EnableVertexAttribArray(_texturePositionLocation);
            gl.VertexAttribPointer(_texturePositionLocation, 3, GlFloat, 0, sizeof(float) * 5, IntPtr.Zero);
            gl.EnableVertexAttribArray(_textureUvLocation);
            gl.VertexAttribPointer(_textureUvLocation, 2, GlFloat, 0, sizeof(float) * 5, new IntPtr(sizeof(float) * 3));
            gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
        }

        BindVertexArray(0);
    }

    private void BindVertexArray(int vertexArray)
    {
        if (!_supportsVertexArrays || _bindVertexArray is null) return;
        if (_boundVertexArray == vertexArray) return;
        _bindVertexArray(vertexArray);
        _boundVertexArray = vertexArray;
        if (vertexArray == 0)
        {
            _lastMeshAttributeResource = null;
            _lastShadowAttributeResource = null;
        }
    }


    private bool ProbeBoneTextureSkinningSupport(GlInterface gl)
    {
        _gpuSkinTextureBoneLimit = 0;
        var uniformsAvailable = _meshSkinningEnabledLocation >= 0 &&
                                _meshBoneTextureLocation >= 0 &&
                                _meshBoneTextureHeightLocation >= 0 &&
                                _shadowSkinningEnabledLocation >= 0 &&
                                _shadowBoneTextureLocation >= 0 &&
                                _shadowBoneTextureHeightLocation >= 0;
        if (!uniformsAvailable || _getIntegerv is null) return false;

        var vertexTextureUnits = GetInteger(0x8B4C); // GL_MAX_VERTEX_TEXTURE_IMAGE_UNITS
        var maxTextureSize = GetInteger(0x0D33); // GL_MAX_TEXTURE_SIZE
        if (vertexTextureUnits <= 0 || maxTextureSize < 1) return false;

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

            var error = _getError?.Invoke() ?? 0;
            if (error != 0) return false;
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

    private int CreateVertexArray()
    {
        if (!_supportsVertexArrays || _genVertexArrays is null) return 0;
        var arrays = new int[1];
        _genVertexArrays(1, arrays);
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

    private void EnableShadowParticleBillboardInstanceAttributes(GlInterface gl)
    {
        EnableInstanceAttribute(gl, _shadowInstanceModel0Location, 4, ParticleBillboardByteStride, 0);
        EnableInstanceAttribute(gl, _shadowInstanceModel1Location, 4, ParticleBillboardByteStride, 0);
        EnableInstanceAttribute(gl, _shadowInstanceModel2Location, 4, ParticleBillboardByteStride, 0);
        EnableInstanceAttribute(gl, _shadowInstanceModel3Location, 4, ParticleBillboardByteStride, 0);
    }

    private void EnableShadowHighScaleInstanceAttributes(GlInterface gl)
    {
        EnableInstanceAttribute(gl, _shadowInstanceModel0Location, 4, HighScaleTransformByteStride, 0);
        EnableInstanceAttribute(gl, _shadowInstanceModel1Location, 4, HighScaleTransformByteStride, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _shadowInstanceModel2Location, 4, HighScaleTransformByteStride, sizeof(float) * 8);
        EnableInstanceAttribute(gl, _shadowInstanceModel3Location, 4, HighScaleTransformByteStride, sizeof(float) * 12);
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

    private void DisableShadowInstanceAttributes()
    {
        DisableAttribute(_shadowInstanceModel0Location);
        DisableAttribute(_shadowInstanceModel1Location);
        DisableAttribute(_shadowInstanceModel2Location);
        DisableAttribute(_shadowInstanceModel3Location);
        ResetShadowAttributeDivisors();
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
        UploadFloat(_uniform1f, _meshShadowEnabledLocation, 0f);
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

        var visibleChunks = layer.Chunks.QueryVisible(viewProjection);
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

    private static bool ShouldUseHighScaleLegacySafePath(int instanceCount)
        => instanceCount > 0 && instanceCount <= StableHighScaleLegacyInstanceThreshold;

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
        if (!CanUseInstancedDrawPath || ShouldUseHighScaleLegacySafePath(instanceIndices.Count))
        {
            DrawHighScaleLegacyLod(gl, layer, parts, instanceIndices, cameraPosition, performance, stats, startPartIndex: 0);
            return;
        }

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
            EnableHighScaleInstanceAttributes(gl, batch);
            UploadHighScalePalette(gl, layer, part, performance, directStateColor, stats);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadHighScaleMaterial(part.LightingMode);
            var highScaleDrawn = TryDrawElementsInstanced(gl, meshResource.IndexCount, meshResource.IndexType, batch.InstanceCount, part.Mesh.ResourceKey, "highscale-aggregate");
            DisableInstanceAttributes();
            if (!highScaleDrawn)
            {
                DrawHighScaleLegacyLod(gl, layer, parts, instanceIndices, cameraPosition, performance, stats, partIndex);
                return;
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
        if (!CanUseInstancedDrawPath || ShouldUseHighScaleLegacySafePath(instanceIndices.Count))
        {
            DrawHighScaleLegacyLod(gl, layer, parts, instanceIndices, cameraPosition, performance, stats, startPartIndex: 0);
            return;
        }

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
            EnableHighScaleInstanceAttributes(gl, batch);
            UploadHighScalePalette(gl, layer, part, performance, directStateColor, stats);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadHighScaleMaterial(part.LightingMode);
            var highScaleDrawn = TryDrawElementsInstanced(gl, meshResource.IndexCount, meshResource.IndexType, batch.InstanceCount, part.Mesh.ResourceKey, "highscale");
            DisableInstanceAttributes();
            if (!highScaleDrawn)
            {
                DrawHighScaleLegacyLod(gl, layer, parts, instanceIndices, cameraPosition, performance, stats, partIndex);
                return;
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

    private void DrawHighScaleLegacyLod(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        IReadOnlyList<CompositePartTemplate3D> parts,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats,
        int startPartIndex)
    {
        UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        UploadFloat(_uniform1f, _meshUseDirectStateColorLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 1f);
        UploadFloat(_uniform1f, _meshParticleBillboardLocation, 0f);
        UploadFloat(_uniform1f, _meshSkinningEnabledLocation, 0f);
        DisableInstanceAttributes();

        var dynamicFadeState = performance.EnableHighScaleDynamicFadeState;
        for (var partIndex = System.Math.Max(0, startPartIndex); partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var meshResource = EnsureMeshResource(gl, part.Mesh.ResourceKey, part.Mesh.GeometryVersion, part.Mesh, stats);
            BindMeshAttributes(gl, meshResource);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadHighScaleMaterial(part.LightingMode);

            for (var i = 0; i < instanceIndices.Count; i++)
            {
                var instanceIndex = instanceIndices[i];
                if ((uint)instanceIndex >= (uint)layer.Instances.Count) continue;
                var record = layer.Instances[instanceIndex];
                if (!IsHighScaleVisible(record)) continue;

                var alpha = ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState);
                if (alpha <= 0f) continue;

                var color = layer.ResolveColor(part, record);
                UploadMatrix(_uniformMatrix4fv, _meshModelLocation, record.Transform, _matrixUploadBuffer);
                UploadColor(_uniform4f, _meshColorLocation, new ColorRgba(color.R, color.G, color.B, color.A * alpha));
                gl.DrawElements(GlTriangles, meshResource.IndexCount, meshResource.IndexType, IntPtr.Zero);

                stats.DrawCallCount++;
                stats.EstimatedDrawCallCount++;
                stats.VisibleMeshCount++;
                stats.HighScaleVisiblePartInstanceCount++;
                stats.TriangleCount += part.Mesh.Indices.Length / 3;
                if (part.UsesVertexMaterialSlots) stats.BakedHighScalePartDraws++;
            }
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

    private void UploadHighScalePalette(GlInterface gl, HighScaleInstanceLayer3D layer, CompositePartTemplate3D part, ScenePerformanceOptions performance, bool directStateColor, RenderStats stats)
    {
        UploadFloat(_uniform1f, _meshUseDirectStateColorLocation, directStateColor ? 1f : 0f);
        if (!directStateColor && performance.EnableHighScalePaletteTexture && part.UsesVertexMaterialSlots && layer.ColorResolver is null)
        {
            var palette = EnsureHighScalePaletteTexture(gl, layer, part);
            UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 1f);
            gl.ActiveTexture(GlTexture1);
            gl.BindTexture(GlTexture2D, palette.TextureId);
            if (_meshPaletteTextureLocation >= 0) _uniform1i?.Invoke(_meshPaletteTextureLocation, 1);
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
            cached = new HighScalePaletteTextureResource { TextureId = gl.GenTexture() };
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
            gl.ActiveTexture(GlTexture1);
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

        var viewProjection = plan.Frame.ViewProjection;
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

        if (_controlVertexArray != 0) BindVertexArray(_controlVertexArray);
        else BindVertexArray(0);
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
            if (_controlVertexArray == 0)
            {
                gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
                gl.EnableVertexAttribArray(_texturePositionLocation);
                gl.VertexAttribPointer(_texturePositionLocation, 3, GlFloat, 0, sizeof(float) * 5, IntPtr.Zero);
                gl.EnableVertexAttribArray(_textureUvLocation);
                gl.VertexAttribPointer(_textureUvLocation, 2, GlFloat, 0, sizeof(float) * 5, new IntPtr(sizeof(float) * 3));
            }
            gl.DrawElements(GlTriangles, 6, GlUnsignedShort, IntPtr.Zero);
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
                ConfigureMeshVertexArray(gl, resource);
                UpdateMeshResourceCounters(resource, geometry, geometryVersion);
                AddMeshUploadStats(stats, geometry);
                return resource;
            }

            DisposeMeshResource(gl, resource);
        }

        resource = new MeshGpuResource
        {
            GeometryVersion = geometryVersion,
            VertexArray = CreateVertexArray(),
            VertexBuffer = gl.GenBuffer(),
            IndexBuffer = gl.GenBuffer(),
            WireframeIndexBuffer = gl.GenBuffer()
        };
        UploadMeshResourceData(gl, resource, mesh, uploadUsage);
        ConfigureMeshVertexArray(gl, resource);
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
        resource.VertexUploadBytes = EstimateInterleavedVertexUploadBytes(geometry);
        resource.IndexUploadBytes = geometry.EstimatedIndexUploadBytes;
    }

    private static void AddMeshUploadStats(RenderStats stats, RenderGeometry3D geometry)
    {
        stats.DirtyMeshUploads++;
        stats.RenderGeometryCount++;
        stats.VertexBufferUploadCount += 1;
        stats.IndexBufferUploadCount += 2;
        var vertexUploadBytes = EstimateInterleavedVertexUploadBytes(geometry);
        stats.VertexBufferUploadBytes += vertexUploadBytes;
        stats.IndexBufferUploadBytes += geometry.EstimatedIndexUploadBytes;
        stats.MeshUploadBytes += vertexUploadBytes + geometry.EstimatedIndexUploadBytes;
        stats.TangentUploadBytes += geometry.HasTangents ? geometry.Tangents.LongLength * sizeof(float) * 4L : 0L;
        stats.WireframeIndexUploadBytes += geometry.EstimatedWireframeIndexUploadBytes;
        if (geometry.HasTangentSpace) stats.TangentSpaceMeshCount++;
    }

    private static void UploadMeshResourceData(GlInterface gl, MeshGpuResource resource, Mesh3D mesh, int usage)
    {
        var geometry = mesh.RenderGeometry;
        var vertexFloatCount = geometry.Positions.Length * MeshVertexFloatStride;
        var interleaved = ArrayPool<float>.Shared.Rent(vertexFloatCount);
        try
        {
            BuildInterleavedVertexData(mesh, interleaved);
            gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
            UploadFloats(gl, GlArrayBuffer, interleaved, vertexFloatCount, usage);
            gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
            UploadIndices(gl, GlElementArrayBuffer, geometry.Indices, usage, out var indexType);
            resource.IndexType = indexType;
            gl.BindBuffer(GlElementArrayBuffer, resource.WireframeIndexBuffer);
            UploadIndices(gl, GlElementArrayBuffer, geometry.WireframeIndices, usage, out var wireframeIndexType);
            resource.WireframeIndexType = wireframeIndexType;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(interleaved);
        }
    }

    private static long EstimateInterleavedVertexUploadBytes(RenderGeometry3D geometry)
        => geometry.Positions.LongLength * MeshVertexFloatStride * sizeof(float);

    private static void BuildInterleavedVertexData(Mesh3D mesh, float[] data)
    {
        var geometry = mesh.RenderGeometry;
        var vertexCount = geometry.Positions.Length;
        var normals = geometry.HasNormals ? geometry.Normals : GetNormalsOrDefault(mesh);
        var texCoords = geometry.HasTexCoords0 ? geometry.TexCoords0 : null;
        var tangents = geometry.HasTangents ? geometry.Tangents : null;
        var colors = geometry.HasColors0 ? geometry.Colors0 : null;
        var materialSlots = geometry.HasMaterialSlots ? geometry.MaterialSlots : null;
        var boneIndices = geometry.HasSkinWeights ? geometry.BoneIndices0 : null;
        var boneWeights = geometry.HasSkinWeights ? geometry.BoneWeights0 : null;

        for (var i = 0; i < vertexCount; i++)
        {
            var offset = i * MeshVertexFloatStride;
            var position = geometry.Positions[i];
            var normal = normals[i];
            var texCoord = texCoords is not null ? texCoords[i] : Vector2.Zero;
            var tangent = tangents is not null ? tangents[i] : new Vector4(1f, 0f, 0f, 1f);
            var color = colors is not null ? colors[i] : ColorRgba.White;
            var materialSlot = materialSlots is not null ? materialSlots[i] : 0f;
            var boneIndex = boneIndices is not null ? boneIndices[i] : Vector4.Zero;
            var boneWeight = boneWeights is not null ? boneWeights[i] : new Vector4(1f, 0f, 0f, 0f);

            data[offset] = position.X;
            data[offset + 1] = position.Y;
            data[offset + 2] = position.Z;
            data[offset + 3] = normal.X;
            data[offset + 4] = normal.Y;
            data[offset + 5] = normal.Z;
            data[offset + 6] = texCoord.X;
            data[offset + 7] = texCoord.Y;
            data[offset + 8] = tangent.X;
            data[offset + 9] = tangent.Y;
            data[offset + 10] = tangent.Z;
            data[offset + 11] = tangent.W;
            data[offset + 12] = color.R;
            data[offset + 13] = color.G;
            data[offset + 14] = color.B;
            data[offset + 15] = color.A;
            data[offset + 16] = materialSlot;
            data[offset + 17] = boneIndex.X;
            data[offset + 18] = boneIndex.Y;
            data[offset + 19] = boneIndex.Z;
            data[offset + 20] = boneIndex.W;
            data[offset + 21] = boneWeight.X;
            data[offset + 22] = boneWeight.Y;
            data[offset + 23] = boneWeight.Z;
            data[offset + 24] = boneWeight.W;
        }

    }

    private unsafe MaterialTextureResource? EnsureMaterialTexture(GlInterface gl, string? key, byte[]? data, int version, int textureUnit, RenderStats stats)
    {
        if (string.IsNullOrWhiteSpace(key) || data is not { Length: > 0 }) return null;
        if (_materialTextures.TryGetValue(key, out var resource) && resource.Version == version) return resource;

        if (!TextureDecodeHelper3D.TryDecodeRgba(data, out var decoded, out _)) return null;
        if (resource is null)
        {
            resource = new MaterialTextureResource { TextureId = gl.GenTexture(), Version = -1 };
            _materialTextures[key] = resource;
        }

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
            resource.Version = version;
            resource.Width = decoded.Width;
            resource.Height = decoded.Height;
            stats.DirtyTextureUploads++;
            stats.TextureUploadBytes += decoded.ByteLength;
        }

        return resource;
    }

    private unsafe ControlTextureResource? EnsureControlTexture(GlInterface gl, ControlPlane3D plane, RenderStats stats)
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
        if (_controlBgraUploadBuffer.Length < bufferSize) _controlBgraUploadBuffer = new byte[bufferSize];
        if (_controlRgbaUploadBuffer.Length < bufferSize) _controlRgbaUploadBuffer = new byte[bufferSize];
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
            resource.SnapshotVersion = plane.SnapshotVersion;
            resource.Width = pixelWidth;
            resource.Height = pixelHeight;
            stats.DirtyTextureUploads++;
            stats.TextureUploadBytes += bufferSize;
        }
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

    private static unsafe void UploadFloats(GlInterface gl, int target, float[] data, int count, int usage)
    {
        if (count <= 0) return;
        fixed (float* ptr = data)
        {
            gl.BufferData(target, new IntPtr(count * sizeof(float)), (IntPtr)ptr, usage);
        }
    }

    private unsafe void UploadFloatsSubData(int target, int byteOffset, float[] data, int floatOffset, int count)
    {
        if (count <= 0 || _bufferSubData is null) return;
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

    private static void UploadIndices(GlInterface gl, int target, int[] data, int usage, out int indexType)
    {
        if (CanUseUShortIndices(data))
        {
            var packed = ArrayPool<ushort>.Shared.Rent(data.Length);
            try
            {
                for (var i = 0; i < data.Length; i++) packed[i] = (ushort)data[i];
                UploadUShorts(gl, target, packed, data.Length, usage);
                indexType = GlUnsignedShort;
            }
            finally
            {
                ArrayPool<ushort>.Shared.Return(packed);
            }
            return;
        }

        UploadInts(gl, target, data, usage);
        indexType = GlUnsignedInt;
    }

    private static bool CanUseUShortIndices(int[] data)
    {
        if (data is null || data.Length == 0) return false;
        for (var i = 0; i < data.Length; i++)
        {
            var value = data[i];
            if ((uint)value > ushort.MaxValue) return false;
        }
        return true;
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


    private static void ApplyAnimationStats(RenderStats stats, Scene3D scene, bool gpuSkinningActive, string fallbackReason)
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
        stats.SkinningFallbackReason = stats.GpuSkinningActive || skinned == 0 ? string.Empty : fallbackReason;
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

    private static ColorRgba[] GetVertexColorsOrDefault(Mesh3D mesh)
    {
        if (mesh.VertexColors0.Length == mesh.Positions.Length) return mesh.VertexColors0;
        var colors = new ColorRgba[mesh.Positions.Length];
        for (var i = 0; i < colors.Length; i++) colors[i] = ColorRgba.White;
        return colors;
    }

    private static Vector4[] GetBoneIndicesOrDefault(Mesh3D mesh)
    {
        if (mesh.BoneIndices0.Length == mesh.Positions.Length) return mesh.BoneIndices0;
        return new Vector4[mesh.Positions.Length];
    }

    private static Vector4[] GetBoneWeightsOrDefault(Mesh3D mesh)
    {
        if (mesh.BoneWeights0.Length == mesh.Positions.Length) return mesh.BoneWeights0;
        var weights = new Vector4[mesh.Positions.Length];
        for (var i = 0; i < weights.Length; i++) weights[i] = new Vector4(1f, 0f, 0f, 0f);
        return weights;
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

    private static void UploadBillboardParticleMatrix(GlUniformMatrix4fvDelegate? uniformMatrix4fv, int location, float[] data, int offset, float[] buffer)
    {
        if (location < 0 || uniformMatrix4fv is null) return;
        var size = MathF.Max(data[offset + 3], 0.0001f);
        var matrix = Matrix4x4.Identity;
        matrix.M11 = size;
        matrix.M22 = size;
        matrix.M33 = size;
        matrix.M41 = data[offset];
        matrix.M42 = data[offset + 1];
        matrix.M43 = data[offset + 2];
        UploadMatrix(uniformMatrix4fv, location, matrix, buffer);
    }

    private void UploadBatchSkinning(GlInterface gl, MeshBatchData batch)
    {
        UploadSkinning(gl, batch, _meshSkinningEnabledLocation, _meshBoneTextureLocation, _meshBoneTextureHeightLocation);
    }

    private void UploadShadowBatchSkinning(GlInterface gl, MeshBatchData batch)
    {
        UploadSkinning(gl, batch, _shadowSkinningEnabledLocation, _shadowBoneTextureLocation, _shadowBoneTextureHeightLocation);
    }

    private unsafe void UploadSkinning(GlInterface gl, MeshBatchData batch, int enabledLocation, int textureLocation, int heightLocation)
    {
        if (!_supportsBoneTextureSkinning || !batch.HasSkinning || batch.SkinMatrices.Length == 0)
        {
            UploadFloat(_uniform1f, enabledLocation, 0f);
            return;
        }

        var matrices = batch.SkinMatrices;
        if (matrices.Length > _gpuSkinTextureBoneLimit)
        {
            UploadFloat(_uniform1f, enabledLocation, 0f);
            return;
        }

        if (batch.BoneTexture == 0)
        {
            batch.BoneTexture = gl.GenTexture();
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGenVertexArraysDelegate(int n, int[] arrays);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlBindVertexArrayDelegate(int array);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDeleteVertexArraysDelegate(int n, int[] arrays);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDisableVertexAttribArrayDelegate(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlGetIntegervDelegate(int pname, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GlGetErrorDelegate();




    private void DisposeMeshResource(GlInterface gl, MeshGpuResource resource)
    {
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
        public int GeometryVersion { get; set; }
        public int VertexArray { get; init; }
        public int VertexBuffer { get; init; }
        public int NormalBuffer { get; init; }
        public int TexCoordBuffer { get; init; }
        public int TangentBuffer { get; init; }
        public int VertexColorBuffer { get; init; }
        public int BoneIndexBuffer { get; init; }
        public int BoneWeightBuffer { get; init; }
        public int MaterialSlotBuffer { get; init; }
        public int IndexBuffer { get; init; }
        public int WireframeIndexBuffer { get; init; }
        public int VertexCount { get; set; }
        public int IndexCount { get; set; }
        public int WireframeIndexCount { get; set; }
        public int IndexType { get; set; } = GlUnsignedInt;
        public int WireframeIndexType { get; set; } = GlUnsignedInt;
        public long VertexUploadBytes { get; set; }
        public long IndexUploadBytes { get; set; }
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
                var fallbackMax = 0f;
                for (var i = 0; i < InstanceCount; i++)
                {
                    var offset = i * FloatStride;
                    var position = new Vector3(Data[offset + 12], Data[offset + 13], Data[offset + 14]);
                    var distanceSquared = Vector3.DistanceSquared(cameraPosition, position);
                    if (distanceSquared > fallbackMax) fallbackMax = distanceSquared;
                }

                return fallbackMax;
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
            _skinMatrices = matrices.Length == 0 ? Array.Empty<Matrix4x4>() : (Matrix4x4[])matrices.Clone();
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
uniform mat4 uLightViewProj;
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
varying vec4 vLightSpace;
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
    vLightSpace = uLightViewProj * world;
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
varying vec4 vVertexColor;
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
attribute vec4 aBoneIndices;
attribute vec4 aBoneWeights;
attribute vec4 aInstanceModel0;
attribute vec4 aInstanceModel1;
attribute vec4 aInstanceModel2;
attribute vec4 aInstanceModel3;
uniform mat4 uModel;
uniform mat4 uPartLocal;
uniform mat4 uLightViewProj;
uniform float uUseInstancing;
uniform float uUsePartLocal;
uniform float uSkinningEnabled;
uniform sampler2D uBoneTexture;
uniform float uBoneTextureHeight;
uniform float uParticleBillboard;
uniform vec3 uParticleCameraRight;
uniform vec3 uParticleCameraUp;
mat4 readShadowBoneMatrix(float boneIndex)
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
    if (uUsePartLocal > 0.5) model = model * uPartLocal;
    vec4 world;
    if (uParticleBillboard > 0.5)
    {
        vec4 particleState = uUseInstancing > 0.5 ? aInstanceModel0 : vec4(model[3].xyz, abs(model[0].x));
        float particleSize = max(abs(particleState.w), 0.0001);
        vec3 center = particleState.xyz;
        vec3 billboardOffset = (uParticleCameraRight * aPosition.x + uParticleCameraUp * aPosition.y) * particleSize;
        world = vec4(center + billboardOffset, 1.0);
    }
    else
    {
        vec4 localPosition = vec4(aPosition, 1.0);
        if (uSkinningEnabled > 0.5)
        {
            float maxBone = max(uBoneTextureHeight - 1.0, 0.0);
            mat4 skin = readShadowBoneMatrix(clamp(aBoneIndices.x, 0.0, maxBone)) * aBoneWeights.x +
                        readShadowBoneMatrix(clamp(aBoneIndices.y, 0.0, maxBone)) * aBoneWeights.y +
                        readShadowBoneMatrix(clamp(aBoneIndices.z, 0.0, maxBone)) * aBoneWeights.z +
                        readShadowBoneMatrix(clamp(aBoneIndices.w, 0.0, maxBone)) * aBoneWeights.w;
            localPosition = skin * localPosition;
        }
        world = model * localPosition;
    }
    gl_Position = uLightViewProj * world;
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
