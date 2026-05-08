const hosts = new Map();
let nextHostId = 1;

function createShader(gl, type, source) {
  const shader = gl.createShader(type);
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    const info = gl.getShaderInfoLog(shader) || 'Unknown shader error';
    gl.deleteShader(shader);
    throw new Error(info);
  }
  return shader;
}

function createProgram(gl, vertexSource, fragmentSource) {
  const program = gl.createProgram();
  const vs = createShader(gl, gl.VERTEX_SHADER, vertexSource);
  const fs = createShader(gl, gl.FRAGMENT_SHADER, fragmentSource);
  gl.attachShader(program, vs);
  gl.attachShader(program, fs);
  gl.bindAttribLocation(program, 0, 'aPosition');
  gl.bindAttribLocation(program, 1, 'aNormal');
  gl.bindAttribLocation(program, 2, 'aInstanceModel0');
  gl.bindAttribLocation(program, 3, 'aInstanceModel1');
  gl.bindAttribLocation(program, 4, 'aInstanceModel2');
  gl.bindAttribLocation(program, 5, 'aInstanceModel3');
  gl.bindAttribLocation(program, 6, 'aInstanceColor');
  gl.bindAttribLocation(program, 7, 'aMaterialSlot');
  gl.bindAttribLocation(program, 8, 'aTexCoord0');
  gl.bindAttribLocation(program, 9, 'aTangent');
  gl.linkProgram(program);
  gl.deleteShader(vs);
  gl.deleteShader(fs);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    const info = gl.getProgramInfoLog(program) || 'Unknown program link error';
    gl.deleteProgram(program);
    throw new Error(info);
  }
  return program;
}

function createHostState(canvas, gl, metricsElement, centerCursorElement) {
  const meshProgram = createProgram(gl, `
attribute vec3 aPosition;
attribute vec3 aNormal;
attribute vec2 aTexCoord0;
attribute vec4 aTangent;
attribute vec4 aInstanceModel0;
attribute vec4 aInstanceModel1;
attribute vec4 aInstanceModel2;
attribute vec4 aInstanceModel3;
attribute vec4 aInstanceColor;
attribute float aMaterialSlot;
uniform mat4 uViewProj;
uniform mat4 uModel;
uniform vec4 uColor;
uniform float uUseInstancing;
uniform float uUsePalette;
uniform float uClientAnimationEnabled;
uniform float uClientAnimationTime;
uniform float uClientAnimationAmplitude;
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec3 vTangent;
varying vec2 vTexCoord0;
varying vec4 vColor;
varying float vMaterialSlot;
void main() {
  mat4 instanceModel = mat4(aInstanceModel0, aInstanceModel1, aInstanceModel2, aInstanceModel3);
  mat4 model = uUseInstancing > 0.5 ? instanceModel : uModel;
  vec4 world = model * vec4(aPosition, 1.0);
  if (uClientAnimationEnabled > 0.5) {
    float phase = world.x * 0.033 + world.z * 0.047;
    world.x += sin(uClientAnimationTime + phase) * uClientAnimationAmplitude;
    world.z += cos(uClientAnimationTime * 0.7 + phase * 1.7) * uClientAnimationAmplitude;
  }
  vWorldPos = world.xyz;
  mat3 normalMatrix = mat3(model);
  vNormal = normalize(normalMatrix * aNormal);
  vTangent = normalize(normalMatrix * aTangent.xyz);
  vTexCoord0 = aTexCoord0;
  vColor = uUseInstancing > 0.5 ? aInstanceColor : uColor;
  vMaterialSlot = aMaterialSlot;
  gl_Position = uViewProj * world;
}
`, `
precision mediump float;
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
uniform float uNormalMapStrength;
uniform vec4 uPostProcessParams;
uniform vec4 uSsaoParams;
uniform float uUsePalette;
uniform sampler2D uPalette;
uniform vec2 uPaletteSize;
uniform sampler2D uBaseColorTexture;
uniform float uBaseColorTextureEnabled;
uniform sampler2D uNormalTexture;
uniform float uNormalTextureEnabled;
uniform sampler2D uMetallicRoughnessTexture;
uniform float uMetallicRoughnessTextureEnabled;
uniform sampler2D uEmissiveTexture;
uniform float uEmissiveTextureEnabled;
uniform vec4 uMaterialParams;
uniform vec4 uEmissiveColor;
uniform vec4 uAlphaParams;
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec3 vTangent;
varying vec2 vTexCoord0;
varying vec4 vColor;
varying float vMaterialSlot;
void main() {
  vec4 color = vColor;
  vec3 surfaceNormal = normalize(vNormal);
  if (uUsePalette > 0.5) {
    float sx = (floor(vMaterialSlot + 0.5) + 0.5) / max(uPaletteSize.x, 1.0);
    float sy = (floor(vColor.r + 0.5) + 0.5) / max(uPaletteSize.y, 1.0);
    color = texture2D(uPalette, vec2(sx, sy));
    color.a *= vColor.g * vColor.b;
  }
  if (uBaseColorTextureEnabled > 0.5) {
    vec4 texel = texture2D(uBaseColorTexture, vTexCoord0);
    color = vec4(color.rgb * texel.rgb, color.a * texel.a);
  }
  if (color.a <= 0.001) discard;
  if (uAlphaParams.x > 0.0001 && color.a < uAlphaParams.x) discard;
  if (color.a < 0.999) {
    float threshold = mod(floor(gl_FragCoord.x) + floor(gl_FragCoord.y), 4.0) * 0.25;
    if (threshold > color.a) discard;
  }
  vec3 outColor = color.rgb;
  if (uLightingEnabled > 0.5) {
    vec3 n = surfaceNormal;
    if (uNormalTextureEnabled > 0.5 && uNormalMapStrength > 0.0001) {
      vec3 t = normalize(vTangent - n * dot(n, vTangent));
      vec3 b = normalize(cross(n, t));
      vec3 tangentNormal = texture2D(uNormalTexture, vTexCoord0).xyz * 2.0 - 1.0;
      tangentNormal.xy *= uNormalMapStrength;
      tangentNormal = normalize(tangentNormal);
      n = normalize(mat3(t, b, n) * tangentNormal);
      surfaceNormal = n;
    }
    vec3 viewDir = normalize(uCameraPosition - vWorldPos);
    float metallic = clamp(uMaterialParams.x, 0.0, 1.0);
    float roughness = clamp(uMaterialParams.y, 0.04, 1.0);
    if (uMetallicRoughnessTextureEnabled > 0.5) {
      vec4 mr = texture2D(uMetallicRoughnessTexture, vTexCoord0);
      roughness *= clamp(mr.g, 0.04, 1.0);
      metallic *= clamp(mr.b, 0.0, 1.0);
    }
    vec3 light = uAmbientLight;
    vec3 specular = vec3(0.0);
    vec3 dir = normalize(-uDirectionalLightDirection);
    float ndl = max(dot(n, dir), 0.0);
    light += ndl * uDirectionalLightColor;
    if (uLightingEnabled > 1.5 && ndl > 0.0) {
      vec3 halfDir = normalize(dir + viewDir);
      specular += pow(max(dot(n, halfDir), 0.0), mix(96.0, 12.0, roughness)) * uDirectionalLightColor * mix(0.25, 1.0, metallic);
    }
    if (uPointLightColor.a > 0.5) {
      vec3 toPoint = uPointLightPosition.xyz - vWorldPos;
      float dist = length(toPoint);
      vec3 pointDir = normalize(toPoint);
      float att = clamp(1.0 - dist / max(uPointLightPosition.w, 0.01), 0.0, 1.0);
      float diff = max(dot(n, pointDir), 0.0) * att * att;
      light += diff * uPointLightColor.rgb;
      if (uLightingEnabled > 1.5 && diff > 0.0) {
        vec3 halfDir = normalize(pointDir + viewDir);
        specular += pow(max(dot(n, halfDir), 0.0), mix(96.0, 12.0, roughness)) * uPointLightColor.rgb * mix(0.25, 1.0, metallic) * att * att;
      }
    }
    if (uSpotLightColor.a > 0.5) {
      vec3 toSpot = uSpotLightPosition.xyz - vWorldPos;
      float dist = length(toSpot);
      vec3 spotDir = normalize(toSpot);
      float angle = dot(spotDir, normalize(-uSpotLightDirection.xyz));
      float cone = clamp((angle - uSpotLightCone.y) / max(uSpotLightCone.x - uSpotLightCone.y, 0.0001), 0.0, 1.0);
      float att = clamp(1.0 - dist / max(uSpotLightPosition.w, 0.01), 0.0, 1.0) * cone;
      float diff = max(dot(n, spotDir), 0.0) * att * att;
      light += diff * uSpotLightColor.rgb;
      if (uLightingEnabled > 1.5 && diff > 0.0) {
        vec3 halfDir = normalize(spotDir + viewDir);
        specular += pow(max(dot(n, halfDir), 0.0), mix(96.0, 12.0, roughness)) * uSpotLightColor.rgb * mix(0.25, 1.0, metallic) * att * att;
      }
    }
    outColor = outColor * clamp(light, 0.0, 3.0) + specular * 0.25;
  }
  if (uSsaoParams.x > 0.5) {
    float horizon = 1.0 - clamp(surfaceNormal.y * 0.5 + 0.5, 0.0, 1.0);
    float depthHint = clamp(1.0 - gl_FragCoord.z, 0.0, 1.0);
    float ao = clamp(horizon * uSsaoParams.y * 0.35 + depthHint * uSsaoParams.z * 0.025, 0.0, 0.85);
    outColor *= (1.0 - ao);
  }
  vec3 emissive = uEmissiveColor.rgb * uEmissiveColor.a;
  if (uEmissiveTextureEnabled > 0.5) emissive += texture2D(uEmissiveTexture, vTexCoord0).rgb;
  outColor += emissive;
  if (uPostProcessParams.z > 0.5) {
    float exposure = max(uPostProcessParams.x, 0.001);
    float gamma = max(uPostProcessParams.y, 0.1);
    if (uPostProcessParams.w < 1.5) {
      outColor = outColor / (vec3(1.0) + outColor);
    } else {
      outColor = vec3(1.0) - exp(-outColor * exposure);
    }
    outColor = pow(max(outColor, vec3(0.0)), vec3(1.0 / gamma));
  }
  gl_FragColor = vec4(outColor, color.a);
}
`);

  const skyboxProgram = createProgram(gl, `
attribute vec2 aPosition;
varying vec2 vUv;
void main() {
  vUv = aPosition * 0.5 + 0.5;
  gl_Position = vec4(aPosition, 1.0, 1.0);
}
`, `
precision mediump float;
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
float hash(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}
void main() {
  if (uSkyboxMode == 1) { gl_FragColor = vec4(uHorizonColor, 1.0); return; }
  vec2 screen = vUv * 2.0 - 1.0;
  vec3 dir = normalize(uCameraForward + uCameraRight * screen.x * 1.35 + uCameraUp * screen.y * 0.78);
  const float PI = 3.14159265359;
  vec2 uv = vec2(atan(dir.x, dir.z) / (2.0 * PI) + 0.5, asin(clamp(dir.y, -1.0, 1.0)) / PI + 0.5);
  if (uSkyboxMode == 5 && uSkyboxTextureEnabled > 0.5) {
    gl_FragColor = vec4(texture2D(uSkyboxTexture, uv).rgb * max(uIntensity, 0.0), 1.0);
    return;
  }
  if (uSkyboxMode == 3 && uSkyboxCubemapEnabled > 0.5) {
    vec3 ad = abs(dir);
    vec2 cuv;
    if (ad.x >= ad.y && ad.x >= ad.z) {
      if (dir.x > 0.0) { cuv = vec2(-dir.z, dir.y) / ad.x * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxPX, cuv).rgb * max(uIntensity, 0.0), 1.0); return; }
      cuv = vec2(dir.z, dir.y) / ad.x * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxNX, cuv).rgb * max(uIntensity, 0.0), 1.0); return;
    }
    if (ad.y >= ad.x && ad.y >= ad.z) {
      if (dir.y > 0.0) { cuv = vec2(dir.x, -dir.z) / ad.y * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxPY, cuv).rgb * max(uIntensity, 0.0), 1.0); return; }
      cuv = vec2(dir.x, dir.z) / ad.y * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxNY, cuv).rgb * max(uIntensity, 0.0), 1.0); return;
    }
    if (dir.z > 0.0) { cuv = vec2(dir.x, dir.y) / ad.z * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxPZ, cuv).rgb * max(uIntensity, 0.0), 1.0); return; }
    cuv = vec2(-dir.x, dir.y) / ad.z * 0.5 + 0.5; gl_FragColor = vec4(texture2D(uSkyboxNZ, cuv).rgb * max(uIntensity, 0.0), 1.0); return;
  }
  if (uSkyboxMode == 4) {
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
}
`);

  const texturedProgram = createProgram(gl, `
attribute vec3 aPosition;
attribute vec2 aTexCoord;
uniform mat4 uViewProj;
varying vec2 vTexCoord;
void main() {
  vTexCoord = aTexCoord;
  gl_Position = uViewProj * vec4(aPosition, 1.0);
}
`, `
precision mediump float;
uniform sampler2D uTexture;
varying vec2 vTexCoord;
void main() {
  gl_FragColor = texture2D(uTexture, vTexCoord);
}
`);

  const skyboxVertexBuffer = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, skyboxVertexBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, 1, 1, -1, 1]), gl.STATIC_DRAW);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);

  const quadIndexBuffer = gl.createBuffer();
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, quadIndexBuffer);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array([0, 1, 2, 0, 2, 3]), gl.STATIC_DRAW);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);

  return {
    canvas,
    metricsElement,
    centerCursorElement,
    gl,
    isWebGl2: !!gl.drawElementsInstanced,
    instancing: gl.drawElementsInstanced ? {
      drawElementsInstancedANGLE: (mode, count, type, offset, instanceCount) => gl.drawElementsInstanced(mode, count, type, offset, instanceCount),
      vertexAttribDivisorANGLE: (location, divisor) => gl.vertexAttribDivisor(location, divisor)
    } : gl.getExtension('ANGLE_instanced_arrays'),
    meshProgram,
    skyboxProgram,
    skyboxPositionLocation: gl.getAttribLocation(skyboxProgram, 'aPosition'),
    skyboxTopColorLocation: gl.getUniformLocation(skyboxProgram, 'uTopColor'),
    skyboxHorizonColorLocation: gl.getUniformLocation(skyboxProgram, 'uHorizonColor'),
    skyboxBottomColorLocation: gl.getUniformLocation(skyboxProgram, 'uBottomColor'),
    skyboxIntensityLocation: gl.getUniformLocation(skyboxProgram, 'uIntensity'),
    skyboxModeLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxMode'),
    skyboxCameraRightLocation: gl.getUniformLocation(skyboxProgram, 'uCameraRight'),
    skyboxCameraUpLocation: gl.getUniformLocation(skyboxProgram, 'uCameraUp'),
    skyboxCameraForwardLocation: gl.getUniformLocation(skyboxProgram, 'uCameraForward'),
    skyboxTextureLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxTexture'),
    skyboxTextureEnabledLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxTextureEnabled'),
    skyboxPXLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxPX'),
    skyboxNXLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxNX'),
    skyboxPYLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxPY'),
    skyboxNYLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxNY'),
    skyboxPZLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxPZ'),
    skyboxNZLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxNZ'),
    skyboxCubemapEnabledLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxCubemapEnabled'),
    skyboxVertexBuffer,
    texturedProgram,
    meshPositionLocation: gl.getAttribLocation(meshProgram, 'aPosition'),
    meshNormalLocation: gl.getAttribLocation(meshProgram, 'aNormal'),
    meshInstanceModel0Location: gl.getAttribLocation(meshProgram, 'aInstanceModel0'),
    meshInstanceModel1Location: gl.getAttribLocation(meshProgram, 'aInstanceModel1'),
    meshInstanceModel2Location: gl.getAttribLocation(meshProgram, 'aInstanceModel2'),
    meshInstanceModel3Location: gl.getAttribLocation(meshProgram, 'aInstanceModel3'),
    meshInstanceColorLocation: gl.getAttribLocation(meshProgram, 'aInstanceColor'),
    meshMaterialSlotLocation: gl.getAttribLocation(meshProgram, 'aMaterialSlot'),
    meshTexCoordLocation: gl.getAttribLocation(meshProgram, 'aTexCoord0'),
    meshTangentLocation: gl.getAttribLocation(meshProgram, 'aTangent'),
    meshViewProjLocation: gl.getUniformLocation(meshProgram, 'uViewProj'),
    meshModelLocation: gl.getUniformLocation(meshProgram, 'uModel'),
    meshColorLocation: gl.getUniformLocation(meshProgram, 'uColor'),
    meshUseInstancingLocation: gl.getUniformLocation(meshProgram, 'uUseInstancing'),
    meshUsePaletteLocation: gl.getUniformLocation(meshProgram, 'uUsePalette'),
    meshClientAnimationEnabledLocation: gl.getUniformLocation(meshProgram, 'uClientAnimationEnabled'),
    meshClientAnimationTimeLocation: gl.getUniformLocation(meshProgram, 'uClientAnimationTime'),
    meshClientAnimationAmplitudeLocation: gl.getUniformLocation(meshProgram, 'uClientAnimationAmplitude'),
    meshPaletteLocation: gl.getUniformLocation(meshProgram, 'uPalette'),
    meshPaletteSizeLocation: gl.getUniformLocation(meshProgram, 'uPaletteSize'),
    meshBaseColorTextureLocation: gl.getUniformLocation(meshProgram, 'uBaseColorTexture'),
    meshBaseColorTextureEnabledLocation: gl.getUniformLocation(meshProgram, 'uBaseColorTextureEnabled'),
    meshNormalTextureLocation: gl.getUniformLocation(meshProgram, 'uNormalTexture'),
    meshNormalTextureEnabledLocation: gl.getUniformLocation(meshProgram, 'uNormalTextureEnabled'),
    meshMetallicRoughnessTextureLocation: gl.getUniformLocation(meshProgram, 'uMetallicRoughnessTexture'),
    meshMetallicRoughnessTextureEnabledLocation: gl.getUniformLocation(meshProgram, 'uMetallicRoughnessTextureEnabled'),
    meshEmissiveTextureLocation: gl.getUniformLocation(meshProgram, 'uEmissiveTexture'),
    meshEmissiveTextureEnabledLocation: gl.getUniformLocation(meshProgram, 'uEmissiveTextureEnabled'),
    meshMaterialParamsLocation: gl.getUniformLocation(meshProgram, 'uMaterialParams'),
    meshEmissiveColorLocation: gl.getUniformLocation(meshProgram, 'uEmissiveColor'),
    meshAlphaParamsLocation: gl.getUniformLocation(meshProgram, 'uAlphaParams'),
    meshLightingEnabledLocation: gl.getUniformLocation(meshProgram, 'uLightingEnabled'),
    meshAmbientLightLocation: gl.getUniformLocation(meshProgram, 'uAmbientLight'),
    meshDirectionalLightDirectionLocation: gl.getUniformLocation(meshProgram, 'uDirectionalLightDirection'),
    meshDirectionalLightColorLocation: gl.getUniformLocation(meshProgram, 'uDirectionalLightColor'),
    meshPointLightPositionLocation: gl.getUniformLocation(meshProgram, 'uPointLightPosition'),
    meshPointLightColorLocation: gl.getUniformLocation(meshProgram, 'uPointLightColor'),
    meshSpotLightPositionLocation: gl.getUniformLocation(meshProgram, 'uSpotLightPosition'),
    meshSpotLightDirectionLocation: gl.getUniformLocation(meshProgram, 'uSpotLightDirection'),
    meshSpotLightColorLocation: gl.getUniformLocation(meshProgram, 'uSpotLightColor'),
    meshSpotLightConeLocation: gl.getUniformLocation(meshProgram, 'uSpotLightCone'),
    meshCameraPositionLocation: gl.getUniformLocation(meshProgram, 'uCameraPosition'),
    meshNormalMapStrengthLocation: gl.getUniformLocation(meshProgram, 'uNormalMapStrength'),
    meshPostProcessParamsLocation: gl.getUniformLocation(meshProgram, 'uPostProcessParams'),
    meshSsaoParamsLocation: gl.getUniformLocation(meshProgram, 'uSsaoParams'),
    texturedPositionLocation: gl.getAttribLocation(texturedProgram, 'aPosition'),
    texturedUvLocation: gl.getAttribLocation(texturedProgram, 'aTexCoord'),
    texturedViewProjLocation: gl.getUniformLocation(texturedProgram, 'uViewProj'),
    texturedSamplerLocation: gl.getUniformLocation(texturedProgram, 'uTexture'),
    quadIndexBuffer,
    meshResources: new Map(),
    meshResourceList: [],
    meshIdToIndex: new Map(),
    instanceBuffers: new Map(),
    retainedBatches: new Map(),
    retainedBatchList: [],
    retainedBatchIdToIndex: new Map(),
    highScaleLayers: new Map(),
    highScaleDrawList: [],
    frameViewProjection: new Float32Array(16),
    frameId: 0,
    texturePayloadErrors: 0,
    palettePayloadErrors: 0,
    animationUploadBytes: 0,
    animationUploadBatches: 0,
    textureResources: new Map(),
    controlVertexBuffers: new Map(),
    elementIndexUintExt: gl.drawElementsInstanced ? true : gl.getExtension('OES_element_index_uint'),
    width: 0,
    height: 0,
    centerCursorVisible: false,
    pointerDeltaX: 0,
    pointerDeltaY: 0,
    pointerLocked: false,
    pointerMoveHandler: null,
    pointerLockChangeHandler: null
  };
}

export function createHost() {
  const canvas = document.createElement('canvas');
  canvas.style.position = 'fixed';
  canvas.style.left = '0px';
  canvas.style.top = '0px';
  canvas.style.pointerEvents = 'none';
  canvas.style.zIndex = '999';
  canvas.style.display = 'none';

  const metricsElement = document.createElement('div');
  metricsElement.style.position = 'fixed';
  metricsElement.style.pointerEvents = 'none';
  metricsElement.style.zIndex = '1000';
  metricsElement.style.display = 'none';
  metricsElement.style.padding = '5px 8px';
  metricsElement.style.borderRadius = '4px';
  metricsElement.style.background = 'rgba(0, 0, 0, 0.67)';
  metricsElement.style.color = 'white';
  metricsElement.style.font = '12px Consolas, monospace';
  metricsElement.style.whiteSpace = 'pre';
  metricsElement.style.lineHeight = '16px';
  metricsElement.style.userSelect = 'none';

  const centerCursorElement = document.createElement('div');
  centerCursorElement.style.position = 'fixed';
  centerCursorElement.style.pointerEvents = 'none';
  centerCursorElement.style.zIndex = '1001';
  centerCursorElement.style.display = 'none';
  centerCursorElement.style.width = '24px';
  centerCursorElement.style.height = '24px';
  centerCursorElement.style.userSelect = 'none';

  function addCrosshairLine(left, top, width, height) {
    const line = document.createElement('div');
    line.style.position = 'absolute';
    line.style.left = `${left}px`;
    line.style.top = `${top}px`;
    line.style.width = `${width}px`;
    line.style.height = `${height}px`;
    line.style.background = 'white';
    line.style.boxShadow = '0 0 2px rgba(0,0,0,0.85)';
    centerCursorElement.appendChild(line);
  }

  addCrosshairLine(11, 0, 2, 7);
  addCrosshairLine(11, 17, 2, 7);
  addCrosshairLine(0, 11, 7, 2);
  addCrosshairLine(17, 11, 7, 2);

  const contextOptions = {
    alpha: true,
    antialias: false,
    premultipliedAlpha: false,
    preserveDrawingBuffer: false,
    powerPreference: 'high-performance'
  };
  const gl2 = canvas.getContext('webgl2', contextOptions);
  const gl = gl2 || canvas.getContext('webgl', contextOptions);
  if (!gl) throw new Error('WebGL is not available.');

  document.body.appendChild(canvas);
  document.body.appendChild(metricsElement);
  document.body.appendChild(centerCursorElement);
  const id = nextHostId++;
  const host = createHostState(canvas, gl, metricsElement, centerCursorElement);
  host.pointerMoveHandler = (event) => {
    if (document.pointerLockElement !== canvas) return;
    host.pointerDeltaX += event.movementX || 0;
    host.pointerDeltaY += event.movementY || 0;
    event.preventDefault();
  };
  host.pointerLockChangeHandler = () => {
    host.pointerLocked = document.pointerLockElement === canvas;
    if (!host.pointerLocked) {
      host.pointerDeltaX = 0;
      host.pointerDeltaY = 0;
    }
  };
  document.addEventListener('mousemove', host.pointerMoveHandler, true);
  document.addEventListener('pointerlockchange', host.pointerLockChangeHandler, true);
  document.addEventListener('mozpointerlockchange', host.pointerLockChangeHandler, true);
  document.addEventListener('webkitpointerlockchange', host.pointerLockChangeHandler, true);
  hosts.set(id, host);
  return id;
}

export function destroyHost(hostId) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  for (const r of host.meshResources.values()) disposeMeshResource(gl, r);
  for (const b of host.instanceBuffers.values()) gl.deleteBuffer(b);
  for (const b of host.retainedBatches.values()) { gl.deleteBuffer(b.transformBuffer); gl.deleteBuffer(b.stateBuffer); if (b.paletteTexture) gl.deleteTexture(b.paletteTexture); }
  for (const t of host.textureResources.values()) gl.deleteTexture(t.texture);
  for (const b of host.controlVertexBuffers.values()) gl.deleteBuffer(b);
  gl.deleteBuffer(host.quadIndexBuffer);
  gl.deleteProgram(host.meshProgram);
  gl.deleteProgram(host.texturedProgram);
  if (host.pointerMoveHandler) document.removeEventListener('mousemove', host.pointerMoveHandler, true);
  if (host.pointerLockChangeHandler) {
    document.removeEventListener('pointerlockchange', host.pointerLockChangeHandler, true);
    document.removeEventListener('mozpointerlockchange', host.pointerLockChangeHandler, true);
    document.removeEventListener('webkitpointerlockchange', host.pointerLockChangeHandler, true);
  }
  if (document.pointerLockElement === host.canvas) document.exitPointerLock?.();
  host.canvas.remove();
  host.metricsElement.remove();
  host.centerCursorElement.remove();
  hosts.delete(hostId);
}

export function updateHost(hostId, x, y, width, height, visible) {
  const host = hosts.get(hostId);
  if (!host) return;
  const canvas = host.canvas;
  if (!visible || width <= 0 || height <= 0) {
    canvas.style.display = 'none';
    host.metricsElement.style.display = 'none';
    host.centerCursorElement.style.display = 'none';
    return;
  }
  canvas.style.display = 'block';
  canvas.style.left = `${x}px`;
  canvas.style.top = `${y}px`;
  canvas.style.width = `${width}px`;
  canvas.style.height = `${height}px`;
  const dpr = window.devicePixelRatio || 1;
  const pixelWidth = Math.max(1, Math.round(width * dpr));
  const pixelHeight = Math.max(1, Math.round(height * dpr));
  if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
    canvas.width = pixelWidth;
    canvas.height = pixelHeight;
    host.width = pixelWidth;
    host.height = pixelHeight;
  }
  host.metricsElement.style.left = `${x + width - host.metricsElement.offsetWidth - 8}px`;
  host.metricsElement.style.top = `${y + 8}px`;
  updateCenterCursor(hostId, host.centerCursorVisible);
}

function disposeMeshResource(gl, r) {
  if (!r) return;
  gl.deleteBuffer(r.vertexBuffer);
  gl.deleteBuffer(r.normalBuffer);
  gl.deleteBuffer(r.texCoordBuffer);
  gl.deleteBuffer(r.tangentBuffer);
  gl.deleteBuffer(r.materialSlotBuffer);
  gl.deleteBuffer(r.indexBuffer);
  gl.deleteBuffer(r.wireframeIndexBuffer);
}

function rebuildMeshIndex(host) {
  host.meshIdToIndex.clear();
  for (let i = 0; i < host.meshResourceList.length; i++) {
    const r = host.meshResourceList[i];
    r.meshIndex = i;
    host.meshIdToIndex.set(r.meshId, i);
  }
}

export function destroyMeshGeometry(hostId, meshId) {
  const host = hosts.get(hostId);
  if (!host) return;
  const resource = host.meshResources.get(meshId);
  if (!resource) return;
  disposeMeshResource(host.gl, resource);
  host.meshResources.delete(meshId);
  const index = host.meshResourceList.indexOf(resource);
  if (index >= 0) host.meshResourceList.splice(index, 1);
  rebuildMeshIndex(host);
}

export function destroyTexture(hostId, textureId) {
  const host = hosts.get(hostId);
  if (!host) return;
  const resource = host.textureResources.get(textureId);
  if (!resource) return;
  host.gl.deleteTexture(resource.texture);
  host.textureResources.delete(textureId);
}

export function uploadTexture(hostId, textureId, width, height, rgbaBytesBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const safeWidth = Math.max(1, width | 0);
  const safeHeight = Math.max(1, height | 0);
  let resource = host.textureResources.get(textureId);
  if (!resource) {
    resource = { texture: gl.createTexture(), width: 0, height: 0 };
    host.textureResources.set(textureId, resource);
  }
  const rgbaBytes = coerceRgbaPayload(host, rgbaBytesBase64, safeWidth, safeHeight, 'texture');
  gl.bindTexture(gl.TEXTURE_2D, resource.texture);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, safeWidth, safeHeight, 0, gl.RGBA, gl.UNSIGNED_BYTE, rgbaBytes);
  gl.bindTexture(gl.TEXTURE_2D, null);
  resource.width = safeWidth;
  resource.height = safeHeight;
}

export function uploadMeshGeometry(hostId, meshId, geometryJson) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const geometry = JSON.parse(geometryJson);
  const positions = geometry.positions;
  const normals = geometry.normals || [];
  const texCoords0 = geometry.texCoords0 || [];
  const tangents = geometry.tangents || [];
  const indices = geometry.indices;
  const wireframeIndices = geometry.wireframeIndices || [];
  const materialSlots = geometry.materialSlots || [];
  let resource = host.meshResources.get(meshId);
  if (!resource) {
    resource = { vertexBuffer: gl.createBuffer(), normalBuffer: gl.createBuffer(), texCoordBuffer: gl.createBuffer(), tangentBuffer: gl.createBuffer(), materialSlotBuffer: gl.createBuffer(), indexBuffer: gl.createBuffer(), wireframeIndexBuffer: gl.createBuffer(), indexCount: 0, wireframeIndexCount: 0, indexType: gl.UNSIGNED_SHORT, meshId, meshIndex: host.meshResourceList.length };
    host.meshResources.set(meshId, resource);
    host.meshIdToIndex.set(meshId, resource.meshIndex);
    host.meshResourceList.push(resource);
  }
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.vertexBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(positions), gl.STATIC_DRAW);
  const vertexCount = positions.length / 3;
  const normalData = normals.length === positions.length ? normals : createDefaultNormals(vertexCount);
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.normalBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(normalData), gl.STATIC_DRAW);
  const texCoordData = texCoords0.length === vertexCount * 2 ? texCoords0 : createDefaultTexCoords(vertexCount);
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.texCoordBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(texCoordData), gl.STATIC_DRAW);
  const tangentData = tangents.length === vertexCount * 4 ? tangents : createDefaultTangents(vertexCount);
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.tangentBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(tangentData), gl.STATIC_DRAW);
  const slotData = materialSlots.length === vertexCount ? materialSlots : createDefaultMaterialSlots(vertexCount);
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.materialSlotBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(slotData), gl.STATIC_DRAW);
  let maxIndex = 0;
  for (let i = 0; i < indices.length; i++) if (indices[i] > maxIndex) maxIndex = indices[i];
  let indexArray;
  if (maxIndex > 65535) {
    if (!host.elementIndexUintExt) throw new Error('Mesh ' + meshId + ' requires 32-bit indices, but OES_element_index_uint is unavailable.');
    indexArray = new Uint32Array(indices);
    resource.indexType = gl.UNSIGNED_INT;
  } else {
    indexArray = new Uint16Array(indices);
    resource.indexType = gl.UNSIGNED_SHORT;
  }
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, resource.indexBuffer);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indexArray, gl.STATIC_DRAW);
  resource.indexCount = indices.length;
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, resource.wireframeIndexBuffer);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array(wireframeIndices.filter(i => i <= 65535)), gl.STATIC_DRAW);
  resource.wireframeIndexCount = wireframeIndices.length;
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);
}

function createDefaultNormals(vertexCount) {
  const normals = new Array(Math.max(0, vertexCount) * 3);
  for (let i = 0; i < normals.length; i += 3) { normals[i] = 0; normals[i + 1] = 0; normals[i + 2] = 1; }
  return normals;
}

function createDefaultTexCoords(vertexCount) {
  const tex = new Array(Math.max(0, vertexCount) * 2);
  for (let i = 0; i < tex.length; i++) tex[i] = 0;
  return tex;
}

function createDefaultTangents(vertexCount) {
  const tangents = new Array(Math.max(0, vertexCount) * 4);
  for (let i = 0; i < tangents.length; i += 4) { tangents[i] = 1; tangents[i + 1] = 0; tangents[i + 2] = 0; tangents[i + 3] = 1; }
  return tangents;
}

function createDefaultMaterialSlots(vertexCount) {
  const slots = new Array(Math.max(0, vertexCount));
  for (let i = 0; i < slots.length; i++) slots[i] = 0;
  return slots;
}

function getOrCreateControlBuffer(host, id) {
  let buffer = host.controlVertexBuffers.get(id);
  if (!buffer) { buffer = host.gl.createBuffer(); host.controlVertexBuffers.set(id, buffer); }
  return buffer;
}

function getOrCreateInstanceBuffer(host, id) {
  let buffer = host.instanceBuffers.get(id);
  if (!buffer) { buffer = host.gl.createBuffer(); host.instanceBuffers.set(id, buffer); }
  return buffer;
}


function decodeBase64Bytes(base64) {
  if (!base64) return new Uint8Array(0);
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

function toUint8Array(payload) {
  if (!payload) return new Uint8Array(0);
  if (payload instanceof Uint8Array) return payload;
  if (payload instanceof ArrayBuffer) return new Uint8Array(payload);
  if (ArrayBuffer.isView(payload)) return new Uint8Array(payload.buffer, payload.byteOffset, payload.byteLength);
  if (Array.isArray(payload)) return new Uint8Array(payload);
  return new Uint8Array(0);
}

function decodeFloat32Payload(payload) {
  const bytes = typeof payload === 'string' ? decodeBase64Bytes(payload) : toUint8Array(payload);
  if (bytes.byteLength === 0) return new Float32Array(0);
  if ((bytes.byteOffset & 3) === 0 && (bytes.byteLength & 3) === 0) {
    return new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
  }
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  return new Float32Array(copy.buffer);
}

function expectedRgbaByteCount(width, height) {
  const w = Math.max(1, width | 0);
  const h = Math.max(1, height | 0);
  return w * h * 4;
}

function coerceRgbaPayload(host, payload, width, height, kind) {
  const expected = expectedRgbaByteCount(width, height);
  const source = typeof payload === 'string' ? decodeBase64Bytes(payload) : toUint8Array(payload);
  if (source.byteLength >= expected) {
    return source.byteLength === expected ? source : source.subarray(0, expected);
  }

  if (kind === 'palette') host.palettePayloadErrors = (host.palettePayloadErrors || 0) + 1;
  else host.texturePayloadErrors = (host.texturePayloadErrors || 0) + 1;

  const fallback = new Uint8Array(expected);
  if (source.byteLength > 0) fallback.set(source.subarray(0, Math.min(source.byteLength, expected)));
  for (let i = 0; i < expected; i += 4) {
    if (i + 3 >= source.byteLength) {
      fallback[i + 0] = fallback[i + 0] || 255;
      fallback[i + 1] = fallback[i + 1] || 255;
      fallback[i + 2] = fallback[i + 2] || 255;
      fallback[i + 3] = 255;
    }
  }
  return fallback;
}

function hasNonEmptyPayload(payload) {
  if (!payload) return false;
  if (typeof payload === 'string') return payload.length > 0;
  if (payload instanceof ArrayBuffer) return payload.byteLength > 0;
  if (ArrayBuffer.isView(payload)) return payload.byteLength > 0;
  if (Array.isArray(payload)) return payload.length > 0;
  return false;
}

function decodeFloat32Base64(base64) {
  return decodeFloat32Payload(base64);
}

function getOrCreateRetainedBatch(host, batchId) {
  let batch = host.retainedBatches.get(batchId);
  if (!batch) {
    const gl = host.gl;
    batch = {
      batchId,
      batchIndex: host.retainedBatchList.length,
      meshId: '',
      meshIndex: -1,
      lightingEnabled: 0,
      usePalette: false,
      instanceCount: 0,
      transformBuffer: gl.createBuffer(),
      stateBuffer: gl.createBuffer(),
      baseTransformData: new Float32Array(0),
      animatedTransformData: new Float32Array(0),
      animationFrameId: -1,
      animationActive: false,
      paletteTexture: null,
      paletteWidth: 1,
      paletteHeight: 1
    };
    host.retainedBatches.set(batchId, batch);
    host.retainedBatchIdToIndex.set(batchId, batch.batchIndex);
    host.retainedBatchList.push(batch);
  }
  return batch;
}

export function uploadRetainedBatchTransforms(hostId, batchId, meshId, lightingEnabled, usePalette, instanceCount, transformFloatsBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const batch = getOrCreateRetainedBatch(host, batchId);
  batch.meshId = meshId;
  batch.meshIndex = host.meshIdToIndex.has(meshId) ? host.meshIdToIndex.get(meshId) : -1;
  batch.lightingEnabled = lightingEnabled || 0;
  batch.usePalette = !!usePalette;
  batch.instanceCount = instanceCount || 0;
  const transforms = decodeFloat32Payload(transformFloatsBase64);
  batch.baseTransformData = new Float32Array(transforms);
  batch.animatedTransformData = new Float32Array(batch.baseTransformData.length);
  batch.animationFrameId = -1;
  batch.animationActive = false;
  gl.bindBuffer(gl.ARRAY_BUFFER, batch.transformBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, batch.baseTransformData, gl.DYNAMIC_DRAW);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
}



export function uploadRetainedBatchTransformsBytes(hostId, batchId, meshId, lightingEnabled, usePalette, instanceCount, transformBytes) {
  uploadRetainedBatchTransforms(hostId, batchId, meshId, lightingEnabled, usePalette, instanceCount, transformBytes);
}

export function uploadRetainedBatchTransformsRange(hostId, batchId, startInstance, transformFloatsBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const batch = host.retainedBatches.get(batchId);
  if (!batch || !batch.transformBuffer) return;
  const transforms = decodeFloat32Payload(transformFloatsBase64);
  if (transforms.length === 0) return;
  const offsetFloats = Math.max(0, startInstance | 0) * 16;
  if (batch.baseTransformData && batch.baseTransformData.length >= offsetFloats + transforms.length) {
    batch.baseTransformData.set(transforms, offsetFloats);
  }
  const { gl } = host;
  if (!batch.animationActive) {
    gl.bindBuffer(gl.ARRAY_BUFFER, batch.transformBuffer);
    gl.bufferSubData(gl.ARRAY_BUFFER, offsetFloats * 4, transforms);
    gl.bindBuffer(gl.ARRAY_BUFFER, null);
  }
}

export function uploadRetainedBatchState(hostId, batchId, usePalette, paletteWidth, paletteHeight, stateFloatsBase64, paletteRgbaBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const batch = getOrCreateRetainedBatch(host, batchId);
  batch.usePalette = !!usePalette;
  const states = decodeFloat32Payload(stateFloatsBase64);
  gl.bindBuffer(gl.ARRAY_BUFFER, batch.stateBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, states, gl.DYNAMIC_DRAW);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
  if (batch.usePalette && hasNonEmptyPayload(paletteRgbaBase64)) {
    if (!batch.paletteTexture) batch.paletteTexture = gl.createTexture();
    batch.paletteWidth = Math.max(1, paletteWidth || 1);
    batch.paletteHeight = Math.max(1, paletteHeight || 1);
    const rgbaBytes = coerceRgbaPayload(host, paletteRgbaBase64, batch.paletteWidth, batch.paletteHeight, 'palette');
    gl.bindTexture(gl.TEXTURE_2D, batch.paletteTexture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, batch.paletteWidth, batch.paletteHeight, 0, gl.RGBA, gl.UNSIGNED_BYTE, rgbaBytes);
    gl.bindTexture(gl.TEXTURE_2D, null);
  }
}


export function uploadRetainedBatchStateBytes(hostId, batchId, usePalette, paletteWidth, paletteHeight, stateBytes, paletteRgbaBytes) {
  uploadRetainedBatchState(hostId, batchId, usePalette, paletteWidth, paletteHeight, stateBytes, paletteRgbaBytes);
}

export function uploadRetainedBatchTransformsRangeBytes(hostId, batchId, startInstance, transformBytes) {
  uploadRetainedBatchTransformsRange(hostId, batchId, startInstance, transformBytes);
}

export function uploadRetainedBatchStateRange(hostId, batchId, startInstance, stateFloatsBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const batch = host.retainedBatches.get(batchId);
  if (!batch || !batch.stateBuffer) return;
  const states = decodeFloat32Payload(stateFloatsBase64);
  if (states.length === 0) return;
  const { gl } = host;
  gl.bindBuffer(gl.ARRAY_BUFFER, batch.stateBuffer);
  gl.bufferSubData(gl.ARRAY_BUFFER, Math.max(0, startInstance | 0) * 16, states);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
}


export function uploadRetainedBatchStateRangeBytes(hostId, batchId, startInstance, stateBytes) {
  uploadRetainedBatchStateRange(hostId, batchId, startInstance, stateBytes);
}

function removeRetainedBatchFromIndexTables(host, batch) {
  if (!batch) return;
  host.retainedBatchIdToIndex.delete(batch.batchId);
  if ((batch.batchIndex | 0) >= 0 && host.retainedBatchList[batch.batchIndex] === batch) {
    host.retainedBatchList[batch.batchIndex] = null;
  }
}

export function destroyRetainedBatch(hostId, batchId) {
  const host = hosts.get(hostId);
  if (!host) return;
  const batch = host.retainedBatches.get(batchId);
  if (!batch) return;
  const { gl } = host;
  gl.deleteBuffer(batch.transformBuffer);
  gl.deleteBuffer(batch.stateBuffer);
  if (batch.paletteTexture) gl.deleteTexture(batch.paletteTexture);
  removeRetainedBatchFromIndexTables(host, batch);
  host.retainedBatches.delete(batchId);
}

export function clearRetainedBatches(hostId) {
  const host = hosts.get(hostId);
  if (!host) return;
  for (const id of Array.from(host.retainedBatches.keys())) destroyRetainedBatch(hostId, id);
}

function drawSkybox(host, packet) {
  if (!packet.skyboxEnabled || !host.skyboxProgram) return;
  const { gl } = host;
  gl.useProgram(host.skyboxProgram);
  gl.disable(gl.DEPTH_TEST);
  gl.depthMask(false);
  gl.uniform3fv(host.skyboxTopColorLocation, new Float32Array(packet.skyboxTopColor || [0.28, 0.45, 0.72]));
  gl.uniform3fv(host.skyboxHorizonColorLocation, new Float32Array(packet.skyboxHorizonColor || [0.62, 0.76, 0.94]));
  gl.uniform3fv(host.skyboxBottomColorLocation, new Float32Array(packet.skyboxBottomColor || [0.82, 0.86, 0.90]));
  gl.uniform1f(host.skyboxIntensityLocation, packet.skyboxIntensity || 1.0);
  gl.uniform1i(host.skyboxModeLocation, packet.skyboxMode || 2);
  gl.uniform3fv(host.skyboxCameraRightLocation, new Float32Array(packet.cameraRight || [1, 0, 0]));
  gl.uniform3fv(host.skyboxCameraUpLocation, new Float32Array(packet.cameraUp || [0, 1, 0]));
  gl.uniform3fv(host.skyboxCameraForwardLocation, new Float32Array(packet.cameraForward || [0, 0, -1]));
  bindTextureSlot(host, packet.skyboxTextureId || null, host.skyboxTextureLocation, host.skyboxTextureEnabledLocation, gl.TEXTURE0, 0);
  if ((packet.skyboxMode | 0) === 3) bindSkyboxCubemapTextures(host, packet.skyboxCubemapTextureIds || []);
  else if (host.skyboxCubemapEnabledLocation !== null) gl.uniform1f(host.skyboxCubemapEnabledLocation, 0);
  gl.bindBuffer(gl.ARRAY_BUFFER, host.skyboxVertexBuffer);
  gl.enableVertexAttribArray(host.skyboxPositionLocation);
  gl.vertexAttribPointer(host.skyboxPositionLocation, 2, gl.FLOAT, false, 0, 0);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, host.quadIndexBuffer);
  gl.drawElements(gl.TRIANGLES, 6, gl.UNSIGNED_SHORT, 0);
  gl.depthMask(true);
  gl.enable(gl.DEPTH_TEST);
}

export function renderScene(hostId, packetJson) {
  const host = hosts.get(hostId);
  if (!host) return;
  const packet = JSON.parse(packetJson);
  const { gl } = host;
  const batches = packet.batches || [];
  const retainedRefs = packet.retainedBatches || [];
  host.showWireframeOverlay = !!packet.showWireframeOverlay;
  host.showSilhouetteOverlay = !!packet.showSilhouetteOverlay;
  const viewProj = new Float32Array(packet.viewProjection);
  const liveMeshIds = Array.isArray(packet.liveMeshIds) ? new Set(packet.liveMeshIds) : new Set(batches.map(batch => batch.id));
  if (!Array.isArray(packet.liveMeshIds)) {
    for (const ref of retainedRefs) { const rb = host.retainedBatches.get(ref.id); if (rb && rb.meshId) liveMeshIds.add(rb.meshId); }
  }
  for (const [id, resource] of host.meshResources.entries()) {
    if (!liveMeshIds.has(id)) { gl.deleteBuffer(resource.vertexBuffer); gl.deleteBuffer(resource.normalBuffer); gl.deleteBuffer(resource.texCoordBuffer); gl.deleteBuffer(resource.tangentBuffer); gl.deleteBuffer(resource.materialSlotBuffer); gl.deleteBuffer(resource.indexBuffer); gl.deleteBuffer(resource.wireframeIndexBuffer); host.meshResources.delete(id); }
  }
  const liveControlIds = new Set(packet.controlPlanes.map(plane => plane.id));
  const liveTextureIds = Array.isArray(packet.liveTextureIds) ? new Set(packet.liveTextureIds) : new Set(packet.controlPlanes.map(plane => plane.textureId));
  for (const [id, buffer] of host.controlVertexBuffers.entries()) if (!liveControlIds.has(id)) { gl.deleteBuffer(buffer); host.controlVertexBuffers.delete(id); }
  for (const [id, texture] of host.textureResources.entries()) if (!liveTextureIds.has(id)) { gl.deleteTexture(texture.texture); host.textureResources.delete(id); }

  gl.viewport(0, 0, host.width || 1, host.height || 1);
  gl.enable(gl.DEPTH_TEST);
  gl.depthFunc(gl.LEQUAL);
  gl.clearColor(packet.clearColor[0], packet.clearColor[1], packet.clearColor[2], packet.clearColor[3]);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  drawSkybox(host, packet);

  gl.disable(gl.BLEND);
  gl.useProgram(host.meshProgram);
  gl.uniform3fv(host.meshAmbientLightLocation, new Float32Array(packet.ambientLight || [0.28, 0.28, 0.28]));
  gl.uniform3fv(host.meshDirectionalLightDirectionLocation, new Float32Array(packet.directionalLightDirection || [-0.35, -0.75, -0.55]));
  gl.uniform3fv(host.meshDirectionalLightColorLocation, new Float32Array(packet.directionalLightColor || [0, 0, 0]));
  gl.uniform4fv(host.meshPointLightPositionLocation, new Float32Array(packet.pointLightPosition || [0, 0, 0, 1]));
  gl.uniform4fv(host.meshPointLightColorLocation, new Float32Array(packet.pointLightColor || [0, 0, 0, 0]));
  gl.uniform4fv(host.meshSpotLightPositionLocation, new Float32Array(packet.spotLightPosition || [0, 0, 0, 1]));
  gl.uniform4fv(host.meshSpotLightDirectionLocation, new Float32Array(packet.spotLightDirection || [0, -1, 0, 0]));
  gl.uniform4fv(host.meshSpotLightColorLocation, new Float32Array(packet.spotLightColor || [0, 0, 0, 0]));
  gl.uniform4fv(host.meshSpotLightConeLocation, new Float32Array(packet.spotLightCone || [0.95, 0.85, 1, 0]));
  gl.uniform3fv(host.meshCameraPositionLocation, new Float32Array(packet.cameraPosition || [0, 0, 6]));
  if (host.meshPostProcessParamsLocation !== null) {
    const tone = packet.toneMappingParams || [1.0, 2.2, 0.0, 0.0];
    const mode = packet.toneMappingMode || 0;
    gl.uniform4fv(host.meshPostProcessParamsLocation, new Float32Array([tone[0] || 1.0, tone[1] || 2.2, packet.hdrEnabled ? 1.0 : 0.0, mode]));
  }
  if (host.meshSsaoParamsLocation !== null) {
    const ssao = packet.ssaoParams || [0.0, 0.75, 0.025, 16.0];
    gl.uniform4fv(host.meshSsaoParamsLocation, new Float32Array([packet.ssaoEnabled ? 1.0 : 0.0, ssao[0] || 0.0, ssao[1] || 0.75, ssao[2] || 0.025]));
  }
  gl.uniformMatrix4fv(host.meshViewProjLocation, false, viewProj);
  setClientAnimationUniforms(host, false, 0, 0);

  for (const batch of batches) drawMeshBatch(host, batch);
  for (const ref of retainedRefs) drawRetainedBatch(host, ref.id);
  drawControlPlanes(host, packet, viewProj);

  gl.bindBuffer(gl.ARRAY_BUFFER, null);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);
  gl.bindTexture(gl.TEXTURE_2D, null);
  gl.useProgram(null);
}


function bindMeshGeometry(host, resource) {
  const { gl } = host;
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.normalBuffer);
  gl.enableVertexAttribArray(host.meshNormalLocation);
  gl.vertexAttribPointer(host.meshNormalLocation, 3, gl.FLOAT, false, 0, 0);
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.vertexBuffer);
  gl.enableVertexAttribArray(host.meshPositionLocation);
  gl.vertexAttribPointer(host.meshPositionLocation, 3, gl.FLOAT, false, 0, 0);
  if (host.meshTexCoordLocation >= 0) {
    gl.bindBuffer(gl.ARRAY_BUFFER, resource.texCoordBuffer);
    gl.enableVertexAttribArray(host.meshTexCoordLocation);
    gl.vertexAttribPointer(host.meshTexCoordLocation, 2, gl.FLOAT, false, 0, 0);
  }
  if (host.meshTangentLocation >= 0) {
    gl.bindBuffer(gl.ARRAY_BUFFER, resource.tangentBuffer);
    gl.enableVertexAttribArray(host.meshTangentLocation);
    gl.vertexAttribPointer(host.meshTangentLocation, 4, gl.FLOAT, false, 0, 0);
  }
  if (host.meshMaterialSlotLocation >= 0) {
    gl.bindBuffer(gl.ARRAY_BUFFER, resource.materialSlotBuffer);
    gl.enableVertexAttribArray(host.meshMaterialSlotLocation);
    gl.vertexAttribPointer(host.meshMaterialSlotLocation, 1, gl.FLOAT, false, 0, 0);
  }
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, resource.indexBuffer);
}

function prepareRetainedBatchTransformForFrame(host, batch, animationEnabled, time, amplitude) {
  // v60: animation is shader-owned. Do not rewrite retained transform buffers per frame.
  // The v59 JS-side matrix rewrite path uploaded every visible batch every frame and
  // dominated browser frame time for 10k racks. Transform buffers are restored only when
  // leaving older animation modes that may have mutated them.
  if (!batch || !batch.transformBuffer) return 0;
  if (animationEnabled) {
    batch.animationActive = false;
    batch.animationFrameId = host.frameId;
    return 0;
  }

  if (batch.animationActive && batch.baseTransformData && batch.baseTransformData.length > 0) {
    const { gl } = host;
    gl.bindBuffer(gl.ARRAY_BUFFER, batch.transformBuffer);
    gl.bufferSubData(gl.ARRAY_BUFFER, 0, batch.baseTransformData);
    gl.bindBuffer(gl.ARRAY_BUFFER, null);
    batch.animationFrameId = host.frameId;
    batch.animationActive = false;
    host.animationUploadBatches = (host.animationUploadBatches || 0) + 1;
    host.animationUploadBytes = (host.animationUploadBytes || 0) + batch.baseTransformData.byteLength;
    return batch.baseTransformData.byteLength;
  }

  return 0;
}

function drawRetainedBatch(host, batchId) {
  const batch = host.retainedBatches.get(batchId);
  if (!batch) return;
  drawRetainedBatchObject(host, batch);
}

function drawRetainedBatchObject(host, batch) {
  const { gl } = host;
  if (!batch || batch.instanceCount <= 0 || !host.instancing) return;
  const resource = batch.meshIndex >= 0 ? host.meshResourceList[batch.meshIndex] : host.meshResources.get(batch.meshId);
  if (!resource || resource.indexCount === 0) return;
  bindMeshGeometry(host, resource);
  gl.uniform1f(host.meshLightingEnabledLocation, batch.lightingEnabled || 0);
  if (host.meshNormalMapStrengthLocation !== null) gl.uniform1f(host.meshNormalMapStrengthLocation, batch.normalMapStrength || 0);
  gl.uniform1f(host.meshUsePaletteLocation, 0);
  if (host.meshBaseColorTextureEnabledLocation !== null) gl.uniform1f(host.meshBaseColorTextureEnabledLocation, 0);
  if (host.meshNormalTextureEnabledLocation !== null) gl.uniform1f(host.meshNormalTextureEnabledLocation, 0);
  if (host.meshMetallicRoughnessTextureEnabledLocation !== null) gl.uniform1f(host.meshMetallicRoughnessTextureEnabledLocation, 0);
  if (host.meshEmissiveTextureEnabledLocation !== null) gl.uniform1f(host.meshEmissiveTextureEnabledLocation, 0);
  if (host.meshMaterialParamsLocation !== null) gl.uniform4f(host.meshMaterialParamsLocation, 0, 1, 0, 0);
  if (host.meshAlphaParamsLocation !== null) gl.uniform4f(host.meshAlphaParamsLocation, 0, 0, 0, 0);
  if (host.meshEmissiveColorLocation !== null) gl.uniform4f(host.meshEmissiveColorLocation, 0, 0, 0, 0);
  gl.uniform1f(host.meshUseInstancingLocation, 1);
  gl.uniform1f(host.meshUsePaletteLocation, batch.usePalette ? 1 : 0);
  if (batch.usePalette && batch.paletteTexture) {
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, batch.paletteTexture);
    gl.uniform1i(host.meshPaletteLocation, 1);
    gl.uniform2f(host.meshPaletteSizeLocation, batch.paletteWidth || 1, batch.paletteHeight || 1);
    gl.activeTexture(gl.TEXTURE0);
  }
  gl.bindBuffer(gl.ARRAY_BUFFER, batch.transformBuffer);
  setInstanceAttributeWithStride(host, host.meshInstanceModel0Location, 4, 0, 64);
  setInstanceAttributeWithStride(host, host.meshInstanceModel1Location, 4, 16, 64);
  setInstanceAttributeWithStride(host, host.meshInstanceModel2Location, 4, 32, 64);
  setInstanceAttributeWithStride(host, host.meshInstanceModel3Location, 4, 48, 64);
  gl.bindBuffer(gl.ARRAY_BUFFER, batch.stateBuffer);
  setInstanceAttributeWithStride(host, host.meshInstanceColorLocation, 4, 0, 16);
  host.instancing.drawElementsInstancedANGLE(gl.TRIANGLES, resource.indexCount, resource.indexType, 0, batch.instanceCount);
  drawWireframeOverlayForCurrentBatch(host, resource, batch.instanceCount, true);
  resetInstanceDivisors(host);
  gl.uniform1f(host.meshUsePaletteLocation, 0);
}

function drawRetainedBatchByIndex(host, batchIndex) {
  const batch = host.retainedBatchList[batchIndex | 0];
  if (!batch) return;
  drawRetainedBatchObject(host, batch);
}

function resolveLodIndex(layer, cameraPosition, cx, cy, cz) {
  const dx = cx - cameraPosition[0];
  const dy = cy - cameraPosition[1];
  const dz = cz - cameraPosition[2];
  const d2 = dx * dx + dy * dy + dz * dz;
  const detailed = layer.detailedDistance || 24;
  const simplified = layer.simplifiedDistance || 96;
  const proxy = layer.proxyDistance || 320;
  const draw = layer.drawDistance || 5000;
  if (d2 > draw * draw) return 4;
  if (d2 <= detailed * detailed) return 0;
  if (d2 <= simplified * simplified) return 1;
  if (d2 <= proxy * proxy) return 2;
  return layer.enableBillboardFallback ? 3 : 2;
}

function aabbIntersectsClip(viewProj, c, e) {
  let anyInside = false;
  const sx = [-1, 1];
  const sy = [-1, 1];
  const sz = [-1, 1];
  for (let ix = 0; ix < 2; ix++) for (let iy = 0; iy < 2; iy++) for (let iz = 0; iz < 2; iz++) {
    const x = c[0] + e[0] * sx[ix];
    const y = c[1] + e[1] * sy[iy];
    const z = c[2] + e[2] * sz[iz];
    const cx = viewProj[0] * x + viewProj[4] * y + viewProj[8] * z + viewProj[12];
    const cy = viewProj[1] * x + viewProj[5] * y + viewProj[9] * z + viewProj[13];
    const cz = viewProj[2] * x + viewProj[6] * y + viewProj[10] * z + viewProj[14];
    const cw = viewProj[3] * x + viewProj[7] * y + viewProj[11] * z + viewProj[15];
    if (cw > 0 && cx >= -cw && cx <= cw && cy >= -cw && cy <= cw && cz >= -cw && cz <= cw) {
      anyInside = true;
      break;
    }
  }
  return anyInside;
}

function parseHighScaleSnapshot(host, snapshot) {
  const layer = {
    id: snapshot.layerId || '',
    version: snapshot.version || 0,
    visible: snapshot.visible !== false,
    detailedDistance: snapshot.detailedDistance || 24,
    simplifiedDistance: snapshot.simplifiedDistance || 96,
    proxyDistance: snapshot.proxyDistance || 320,
    drawDistance: snapshot.drawDistance || 5000,
    enableBillboardFallback: !!snapshot.enableBillboardFallback,
    chunks: []
  };
  const chunks = snapshot.chunks || [];
  for (const c of chunks) {
    const chunk = {
      id: c.id || '',
      center: [c.cx || 0, c.cy || 0, c.cz || 0],
      extents: [c.ex || 0, c.ey || 0, c.ez || 0],
      instanceCount: c.instanceCount || 0,
      batchesByLod: [[], [], [], []]
    };
    const src = c.batchesByLod || [];
    for (let lod = 0; lod < 4; lod++) {
      const ids = src[lod] || [];
      for (let i = 0; i < ids.length; i++) {
        const idx = host.retainedBatchIdToIndex.get(ids[i]);
        if (idx !== undefined) chunk.batchesByLod[lod].push(idx);
      }
    }
    layer.chunks.push(chunk);
  }
  return layer;
}

export function createHighScaleLayer(hostId, layerId, snapshotJson) {
  uploadHighScaleLayerSnapshot(hostId, layerId, snapshotJson);
}

export function uploadHighScaleLayerSnapshot(hostId, layerId, snapshotJson) {
  const host = hosts.get(hostId);
  if (!host) return;
  const snapshot = typeof snapshotJson === 'string' ? JSON.parse(snapshotJson) : snapshotJson;
  const layer = parseHighScaleSnapshot(host, snapshot || {});
  host.highScaleLayers.set(layerId || layer.id, layer);
}


export function destroyHighScaleLayer(hostId, layerId) {
  const host = hosts.get(hostId);
  if (!host) return;
  host.highScaleLayers.delete(layerId);
}

export function applyHighScaleTelemetryPatch(hostId, layerId, patchJson) {
  // Patch data is applied by typed range entrypoints. This hook is intentionally kept as
  // the high-level v57 patch ingress for future single-call patch streams and metrics.
  const host = hosts.get(hostId);
  if (!host || !host.highScaleLayers.has(layerId)) return '0,0,0,0';
  return '0,0,0,0';
}

export function renderHighScaleFrame(hostId, frameJson) {
  const host = hosts.get(hostId);
  if (!host) return '';
  const packet = typeof frameJson === 'string' ? JSON.parse(frameJson) : frameJson;
  const { gl } = host;
  host.frameId = (host.frameId || 0) + 1;
  host.animationUploadBytes = 0;
  host.animationUploadBatches = 0;
  const t0 = performance.now();
  const viewProj = host.frameViewProjection;
  const viewProjSource = packet.viewProjection || [];
  for (let i = 0; i < 16; i++) viewProj[i] = viewProjSource[i] || 0;
  const camera = packet.cameraPosition || [0, 0, 0];
  gl.viewport(0, 0, host.width || 1, host.height || 1);
  gl.enable(gl.DEPTH_TEST);
  gl.depthFunc(gl.LEQUAL);
  const clear = packet.clearColor || [0, 0, 0, 1];
  gl.clearColor(clear[0], clear[1], clear[2], clear[3]);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  drawSkybox(host, packet);
  gl.disable(gl.BLEND);
  gl.useProgram(host.meshProgram);
  gl.uniform3fv(host.meshAmbientLightLocation, new Float32Array(packet.ambientLight || [0.28, 0.28, 0.28]));
  gl.uniform3fv(host.meshDirectionalLightDirectionLocation, new Float32Array(packet.directionalLightDirection || [-0.35, -0.75, -0.55]));
  gl.uniform3fv(host.meshDirectionalLightColorLocation, new Float32Array(packet.directionalLightColor || [0, 0, 0]));
  gl.uniform4fv(host.meshPointLightPositionLocation, new Float32Array(packet.pointLightPosition || [0, 0, 0, 1]));
  gl.uniform4fv(host.meshPointLightColorLocation, new Float32Array(packet.pointLightColor || [0, 0, 0, 0]));
  gl.uniform4fv(host.meshSpotLightPositionLocation, new Float32Array(packet.spotLightPosition || [0, 0, 0, 1]));
  gl.uniform4fv(host.meshSpotLightDirectionLocation, new Float32Array(packet.spotLightDirection || [0, -1, 0, 0]));
  gl.uniform4fv(host.meshSpotLightColorLocation, new Float32Array(packet.spotLightColor || [0, 0, 0, 0]));
  gl.uniform4fv(host.meshSpotLightConeLocation, new Float32Array(packet.spotLightCone || [0.95, 0.85, 1, 0]));
  gl.uniform3fv(host.meshCameraPositionLocation, new Float32Array(packet.cameraPosition || [0, 0, 6]));
  if (host.meshPostProcessParamsLocation !== null) {
    const tone = packet.toneMappingParams || [1.0, 2.2, 0.0, 0.0];
    const mode = packet.toneMappingMode || 0;
    gl.uniform4fv(host.meshPostProcessParamsLocation, new Float32Array([tone[0] || 1.0, tone[1] || 2.2, packet.hdrEnabled ? 1.0 : 0.0, mode]));
  }
  if (host.meshSsaoParamsLocation !== null) {
    const ssao = packet.ssaoParams || [0.0, 0.75, 0.025, 16.0];
    gl.uniform4fv(host.meshSsaoParamsLocation, new Float32Array([packet.ssaoEnabled ? 1.0 : 0.0, ssao[0] || 0.0, ssao[1] || 0.75, ssao[2] || 0.025]));
  }
  gl.uniformMatrix4fv(host.meshViewProjLocation, false, viewProj);
  const clientAnimationEnabled = !!packet.clientAnimationEnabled;
  const clientAnimationTime = Number(packet.clientAnimationTime || 0);
  const clientAnimationAmplitude = Number(packet.clientAnimationAmplitude || 0);
  // v60: keep animation entirely on the GPU. No C#/WASM transform diffs and no
  // per-frame JS bufferSubData for transform matrices.
  setClientAnimationUniforms(host, clientAnimationEnabled, clientAnimationTime, clientAnimationAmplitude);

  let visibleChunks = 0;
  let totalChunks = 0;
  let culled = 0;
  let lodD = 0, lodS = 0, lodP = 0, lodB = 0, lodC = 0;
  let drawCalls = 0;
  let batches = 0;
  let triangles = 0;
  let partInstances = 0;
  const tCull0 = performance.now();
  const drawBatchIndices = host.highScaleDrawList;
  let drawBatchCount = 0;
  for (const layer of host.highScaleLayers.values()) {
    if (!layer.visible) continue;
    totalChunks += layer.chunks.length;
    for (const chunk of layer.chunks) {
      if (!aabbIntersectsClip(viewProj, chunk.center, chunk.extents)) { culled += chunk.instanceCount; continue; }
      const lod = resolveLodIndex(layer, camera, chunk.center[0], chunk.center[1], chunk.center[2]);
      if (lod === 4) { lodC += chunk.instanceCount; culled += chunk.instanceCount; continue; }
      visibleChunks++;
      if (lod === 0) lodD += chunk.instanceCount;
      else if (lod === 1) lodS += chunk.instanceCount;
      else if (lod === 2) lodP += chunk.instanceCount;
      else lodB += chunk.instanceCount;
      const chunkBatches = chunk.batchesByLod[lod] || [];
      for (let i = 0; i < chunkBatches.length; i++) drawBatchIndices[drawBatchCount++] = chunkBatches[i];
    }
  }
  const tCull1 = performance.now();
  const tDraw0 = performance.now();
  for (let i = 0; i < drawBatchCount; i++) {
    const batch = host.retainedBatchList[drawBatchIndices[i] | 0];
    if (!batch) continue;
    const resource = batch.meshIndex >= 0 ? host.meshResourceList[batch.meshIndex] : host.meshResources.get(batch.meshId);
    if (!resource) continue;
    prepareRetainedBatchTransformForFrame(host, batch, clientAnimationEnabled, clientAnimationTime, clientAnimationAmplitude);
    drawRetainedBatchByIndex(host, drawBatchIndices[i]);
    drawCalls++;
    batches++;
    partInstances += batch.instanceCount || 0;
    triangles += ((resource.indexCount || 0) / 3) * (batch.instanceCount || 0);
  }
  const tDraw1 = performance.now();
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);
  gl.bindTexture(gl.TEXTURE_2D, null);
  gl.useProgram(null);
  return [
    visibleChunks, totalChunks, culled, lodD, lodS, lodP, lodB, lodC,
    drawCalls, batches, Math.round(triangles), partInstances,
    (tCull1 - tCull0).toFixed(3), (tDraw1 - tDraw0).toFixed(3), (performance.now() - t0).toFixed(3), host.isWebGl2 ? 2 : 1,
    host.animationUploadBatches || 0, host.animationUploadBytes || 0, host.texturePayloadErrors || 0, host.palettePayloadErrors || 0
  ].join(',');
}


function setClientAnimationUniforms(host, enabled, time, amplitude) {
  const { gl } = host;
  if (host.meshClientAnimationEnabledLocation) gl.uniform1f(host.meshClientAnimationEnabledLocation, enabled ? 1 : 0);
  if (host.meshClientAnimationTimeLocation) gl.uniform1f(host.meshClientAnimationTimeLocation, time || 0);
  if (host.meshClientAnimationAmplitudeLocation) gl.uniform1f(host.meshClientAnimationAmplitudeLocation, enabled ? Math.max(0, amplitude || 0) : 0);
}

function bindTextureSlot(host, textureId, samplerLocation, enabledLocation, textureUnit, textureUnitIndex) {
  const { gl } = host;
  if (enabledLocation === null || samplerLocation === null || !textureId) {
    if (enabledLocation !== null) gl.uniform1f(enabledLocation, 0);
    return;
  }
  const textureResource = host.textureResources.get(textureId);
  if (!textureResource) {
    gl.uniform1f(enabledLocation, 0);
    return;
  }
  gl.activeTexture(textureUnit);
  gl.bindTexture(gl.TEXTURE_2D, textureResource.texture);
  gl.uniform1i(samplerLocation, textureUnitIndex);
  gl.uniform1f(enabledLocation, 1);
  gl.activeTexture(gl.TEXTURE0);
}

function bindMaterialTextures(host, batch) {
  const { gl } = host;
  bindTextureSlot(host, batch.baseColorTextureId || null, host.meshBaseColorTextureLocation, host.meshBaseColorTextureEnabledLocation, gl.TEXTURE2, 2);
  bindTextureSlot(host, batch.normalTextureId || null, host.meshNormalTextureLocation, host.meshNormalTextureEnabledLocation, gl.TEXTURE3, 3);
  bindTextureSlot(host, batch.metallicRoughnessTextureId || null, host.meshMetallicRoughnessTextureLocation, host.meshMetallicRoughnessTextureEnabledLocation, gl.TEXTURE4, 4);
  bindTextureSlot(host, batch.emissiveTextureId || null, host.meshEmissiveTextureLocation, host.meshEmissiveTextureEnabledLocation, gl.TEXTURE5, 5);
}


function bindSkyboxCubemapTextures(host, ids) {
  const { gl } = host;
  const locations = [host.skyboxPXLocation, host.skyboxNXLocation, host.skyboxPYLocation, host.skyboxNYLocation, host.skyboxPZLocation, host.skyboxNZLocation];
  const units = [gl.TEXTURE0, gl.TEXTURE1, gl.TEXTURE2, gl.TEXTURE3, gl.TEXTURE4, gl.TEXTURE5];
  let complete = Array.isArray(ids) && ids.length >= 6;
  for (let i = 0; i < 6; i++) {
    const id = complete ? ids[i] : null;
    const res = id ? host.textureResources.get(id) : null;
    if (!res || locations[i] === null) { complete = false; continue; }
    gl.activeTexture(units[i]);
    gl.bindTexture(gl.TEXTURE_2D, res.texture);
    gl.uniform1i(locations[i], i);
  }
  if (host.skyboxCubemapEnabledLocation !== null) gl.uniform1f(host.skyboxCubemapEnabledLocation, complete ? 1 : 0);
  gl.activeTexture(gl.TEXTURE0);
}

function drawMeshBatch(host, batch) {
  const { gl } = host;
  const resource = host.meshResources.get(batch.id);
  if (!resource || resource.indexCount === 0 || !batch.instanceData || batch.instanceCount <= 0) return;
  bindMeshGeometry(host, resource);
  gl.uniform1f(host.meshLightingEnabledLocation, batch.lightingEnabled || 0);
  if (host.meshNormalMapStrengthLocation !== null) gl.uniform1f(host.meshNormalMapStrengthLocation, batch.normalMapStrength || 0);
  if (host.meshMaterialParamsLocation !== null) gl.uniform4f(host.meshMaterialParamsLocation, batch.metallic || 0, batch.roughness === undefined ? 1 : batch.roughness, 0, 0);
  if (host.meshAlphaParamsLocation !== null) gl.uniform4f(host.meshAlphaParamsLocation, batch.alphaCutoff || 0, 0, 0, 0);
  if (host.meshEmissiveColorLocation !== null) {
    const em = batch.emissiveColor || [0, 0, 0, 0];
    gl.uniform4f(host.meshEmissiveColorLocation, em[0] || 0, em[1] || 0, em[2] || 0, em[3] || 0);
  }
  gl.uniform1f(host.meshUsePaletteLocation, 0);
  bindMaterialTextures(host, batch);

  if (host.instancing) {
    const buffer = getOrCreateInstanceBuffer(host, batch.id + '|l:' + (batch.lightingEnabled || 0));
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(batch.instanceData), gl.DYNAMIC_DRAW);
    setInstanceAttribute(host, host.meshInstanceModel0Location, 4, 0);
    setInstanceAttribute(host, host.meshInstanceModel1Location, 4, 16);
    setInstanceAttribute(host, host.meshInstanceModel2Location, 4, 32);
    setInstanceAttribute(host, host.meshInstanceModel3Location, 4, 48);
    setInstanceAttribute(host, host.meshInstanceColorLocation, 4, 64);
    gl.uniform1f(host.meshUseInstancingLocation, 1);
    host.instancing.drawElementsInstancedANGLE(gl.TRIANGLES, resource.indexCount, resource.indexType, 0, batch.instanceCount);
    drawWireframeOverlayForCurrentBatch(host, resource, batch.instanceCount, true);
    resetInstanceDivisors(host);
  } else {
    gl.uniform1f(host.meshUseInstancingLocation, 0);
    const data = batch.instanceData;
    for (let i = 0; i < batch.instanceCount; i++) {
      const o = i * 20;
      gl.uniformMatrix4fv(host.meshModelLocation, false, new Float32Array(data.slice(o, o + 16)));
      gl.uniform4fv(host.meshColorLocation, new Float32Array(data.slice(o + 16, o + 20)));
      gl.drawElements(gl.TRIANGLES, resource.indexCount, resource.indexType, 0);
      drawWireframeOverlayForCurrentBatch(host, resource, 1, false);
    }
  }
}

function drawWireframeOverlayForCurrentBatch(host, resource, instanceCount, instanced) {
  if (!host.showWireframeOverlay || !resource || resource.wireframeIndexCount <= 0) return;
  const { gl } = host;
  gl.uniform1f(host.meshLightingEnabledLocation, 0);
  if (host.meshNormalMapStrengthLocation !== null) gl.uniform1f(host.meshNormalMapStrengthLocation, 0);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, resource.wireframeIndexBuffer);
  if (instanced && host.instancing) {
    host.instancing.drawElementsInstancedANGLE(gl.LINES, resource.wireframeIndexCount, gl.UNSIGNED_SHORT, 0, instanceCount);
  } else {
    gl.drawElements(gl.LINES, resource.wireframeIndexCount, gl.UNSIGNED_SHORT, 0);
  }
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, resource.indexBuffer);
}

function setInstanceAttribute(host, location, size, offset) {
  setInstanceAttributeWithStride(host, location, size, offset, 80);
}

function setInstanceAttributeWithStride(host, location, size, offset, stride) {
  if (location < 0) return;
  const { gl } = host;
  gl.enableVertexAttribArray(location);
  gl.vertexAttribPointer(location, size, gl.FLOAT, false, stride, offset);
  host.instancing.vertexAttribDivisorANGLE(location, 1);
}

function resetInstanceDivisors(host) {
  const inst = host.instancing;
  if (!inst) return;
  for (const location of [host.meshInstanceModel0Location, host.meshInstanceModel1Location, host.meshInstanceModel2Location, host.meshInstanceModel3Location, host.meshInstanceColorLocation]) {
    if (location >= 0) inst.vertexAttribDivisorANGLE(location, 0);
  }
}

function drawControlPlanes(host, packet, viewProj) {
  const { gl } = host;
  if (!packet.controlPlanes || packet.controlPlanes.length === 0) return;
  gl.enable(gl.BLEND);
  gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  gl.depthMask(false);
  gl.useProgram(host.texturedProgram);
  gl.uniformMatrix4fv(host.texturedViewProjLocation, false, viewProj);
  gl.activeTexture(gl.TEXTURE0);
  gl.uniform1i(host.texturedSamplerLocation, 0);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, host.quadIndexBuffer);
  for (const plane of packet.controlPlanes) {
    const textureResource = host.textureResources.get(plane.textureId);
    if (!textureResource) continue;
    const vertexBuffer = getOrCreateControlBuffer(host, plane.id);
    gl.bindBuffer(gl.ARRAY_BUFFER, vertexBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(plane.vertices), gl.DYNAMIC_DRAW);
    gl.enableVertexAttribArray(host.texturedPositionLocation);
    gl.vertexAttribPointer(host.texturedPositionLocation, 3, gl.FLOAT, false, 20, 0);
    gl.enableVertexAttribArray(host.texturedUvLocation);
    gl.vertexAttribPointer(host.texturedUvLocation, 2, gl.FLOAT, false, 20, 12);
    gl.bindTexture(gl.TEXTURE_2D, textureResource.texture);
    gl.drawElements(gl.TRIANGLES, 6, gl.UNSIGNED_SHORT, 0);
  }
  gl.depthMask(true);
  gl.disable(gl.BLEND);
}

export function updateMetrics(hostId, text, visible) {
  const host = hosts.get(hostId);
  if (!host) return;
  const element = host.metricsElement;
  if (!visible || !text) { element.style.display = 'none'; element.textContent = ''; return; }
  element.textContent = text;
  element.style.display = 'block';
  const canvasLeft = parseFloat(host.canvas.style.left || '0') || 0;
  const canvasTop = parseFloat(host.canvas.style.top || '0') || 0;
  const canvasWidth = parseFloat(host.canvas.style.width || '0') || 0;
  element.style.left = `${canvasLeft + canvasWidth - element.offsetWidth - 8}px`;
  element.style.top = `${canvasTop + 8}px`;
}

export function updateCenterCursor(hostId, visible) {
  const host = hosts.get(hostId);
  if (!host) return;
  host.centerCursorVisible = !!visible;
  const canvasLeft = parseFloat(host.canvas.style.left || '0') || 0;
  const canvasTop = parseFloat(host.canvas.style.top || '0') || 0;
  const canvasWidth = parseFloat(host.canvas.style.width || '0') || 0;
  const canvasHeight = parseFloat(host.canvas.style.height || '0') || 0;
  host.centerCursorElement.style.left = `${canvasLeft + canvasWidth * 0.5 - 12}px`;
  host.centerCursorElement.style.top = `${canvasTop + canvasHeight * 0.5 - 12}px`;
  host.centerCursorElement.style.display = visible ? 'block' : 'none';
}

export function requestPointerLock(hostId) {
  const host = hosts.get(hostId);
  if (!host || !host.canvas.requestPointerLock) return;
  try { host.canvas.requestPointerLock(); } catch { }
}

export function exitPointerLock(hostId) {
  const host = hosts.get(hostId);
  if (!host) return;
  try { if (document.pointerLockElement === host.canvas) document.exitPointerLock?.(); } catch { }
  host.pointerDeltaX = 0;
  host.pointerDeltaY = 0;
}

export function isPointerLockActive(hostId) {
  const host = hosts.get(hostId);
  return !!host && document.pointerLockElement === host.canvas;
}

export function consumePointerDelta(hostId) {
  const host = hosts.get(hostId);
  if (!host) return '0,0';
  const x = host.pointerDeltaX || 0;
  const y = host.pointerDeltaY || 0;
  host.pointerDeltaX = 0;
  host.pointerDeltaY = 0;
  return `${x},${y}`;
}
