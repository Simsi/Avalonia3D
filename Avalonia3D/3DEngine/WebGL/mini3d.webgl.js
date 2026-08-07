const hosts = new Map();
let nextHostId = 1;
let documentVisibilityVersion = 0;
const uniformCacheByContext = new WeakMap();

function finiteOr(value, fallback) {
  return Number.isFinite(value) ? value : fallback;
}

if (typeof document !== 'undefined' && document.addEventListener) {
  document.addEventListener('visibilitychange', () => {
    documentVisibilityVersion++;
    for (const host of hosts.values()) {
      host.pointerDeltaX = 0;
      host.pointerDeltaY = 0;
    }
  }, true);
}

export function isDocumentHidden() {
  return typeof document !== 'undefined' && !!document.hidden;
}

export function getDocumentVisibilityVersion() {
  return documentVisibilityVersion;
}

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
  gl.bindAttribLocation(program, 10, 'aVertexColor');
  gl.bindAttribLocation(program, 11, 'aBoneIndices');
  gl.bindAttribLocation(program, 12, 'aBoneWeights');
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

function createRequiredBuffer(gl, label) {
  const buffer = gl.createBuffer();
  if (!buffer) throw new Error(`WebGL 2 failed to allocate required buffer '${label}'.`);
  return buffer;
}

function createRequiredTexture(gl, label) {
  const texture = gl.createTexture();
  if (!texture) throw new Error(`WebGL 2 failed to allocate required texture '${label}'.`);
  return texture;
}

function createHostState(canvas, gl, metricsElement, centerCursorElement) {
  if (!gl || typeof gl.createVertexArray !== 'function' || typeof gl.drawElementsInstanced !== 'function') throw new Error('Avalonia3D RHI requires WebGL 2. WebGL 1 and extension-based legacy paths are not supported.');
  if (typeof BigInt === 'undefined') throw new Error('Avalonia3D retained RHI protocol requires browser BigInt support for stable 64-bit resource handles.');
  const meshProgram = createProgram(gl, `
attribute vec3 aPosition;
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
attribute float aMaterialSlot;
uniform mat4 uViewProj;
uniform mat4 uModel;
uniform vec4 uColor;
uniform float uUseInstancing;
uniform float uUsePalette;
uniform float uClientAnimationEnabled;
uniform float uClientAnimationTime;
uniform float uClientAnimationAmplitude;
uniform float uParticleMode;
uniform vec3 uCameraRight;
uniform vec3 uCameraUp;
uniform float uSkinningEnabled;
uniform sampler2D uBoneTexture;
uniform float uBoneTextureHeight;
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec3 vTangent;
varying vec2 vTexCoord0;
varying vec4 vColor;
varying vec4 vVertexColor;
varying float vMaterialSlot;
mat4 readBoneMatrix(float boneIndex) {
  float y = (floor(boneIndex + 0.5) + 0.5) / max(uBoneTextureHeight, 1.0);
  vec4 c0 = texture2D(uBoneTexture, vec2(0.125, y));
  vec4 c1 = texture2D(uBoneTexture, vec2(0.375, y));
  vec4 c2 = texture2D(uBoneTexture, vec2(0.625, y));
  vec4 c3 = texture2D(uBoneTexture, vec2(0.875, y));
  return mat4(c0, c1, c2, c3);
}
void main() {
  mat4 instanceModel = mat4(aInstanceModel0, aInstanceModel1, aInstanceModel2, aInstanceModel3);
  mat4 model = uUseInstancing > 0.5 ? instanceModel : uModel;
  vec4 world;
  vec3 normal;
  vec3 tangent;
  if (uParticleMode > 0.5) {
    vec3 center = aInstanceModel0.xyz;
    float size = max(aInstanceModel0.w, 0.0001);
    if (uParticleMode < 1.5) {
      world = vec4(center + uCameraRight * (aPosition.x * size) + uCameraUp * (aPosition.y * size), 1.0);
      normal = normalize(-cross(uCameraRight, uCameraUp));
      tangent = normalize(uCameraRight);
    } else {
      world = instanceModel * vec4(aPosition, 1.0);
      normal = normalize(mat3(instanceModel) * aNormal);
      tangent = normalize(mat3(instanceModel) * aTangent.xyz);
    }
  } else {
    vec4 localPosition = vec4(aPosition, 1.0);
    vec3 localNormal = aNormal;
    vec3 localTangent = aTangent.xyz;
    if (uSkinningEnabled > 0.5) {
      mat4 skin = readBoneMatrix(aBoneIndices.x) * aBoneWeights.x;
      skin += readBoneMatrix(aBoneIndices.y) * aBoneWeights.y;
      skin += readBoneMatrix(aBoneIndices.z) * aBoneWeights.z;
      skin += readBoneMatrix(aBoneIndices.w) * aBoneWeights.w;
      localPosition = skin * localPosition;
      localNormal = normalize(mat3(skin) * localNormal);
      localTangent = normalize(mat3(skin) * localTangent);
    }
    world = model * localPosition;
    if (uClientAnimationEnabled > 0.5) {
      float phase = world.x * 0.033 + world.z * 0.047;
      world.x += sin(uClientAnimationTime + phase) * uClientAnimationAmplitude;
      world.z += cos(uClientAnimationTime * 0.7 + phase * 1.7) * uClientAnimationAmplitude;
    }
    mat3 normalMatrix = mat3(model);
    normal = normalize(normalMatrix * localNormal);
    tangent = normalize(normalMatrix * localTangent);
  }
  vWorldPos = world.xyz;
  vNormal = normal;
  vTangent = tangent;
  vTexCoord0 = aTexCoord0;
  vColor = uUseInstancing > 0.5 ? aInstanceColor : uColor;
  vVertexColor = aVertexColor;
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
varying vec4 vVertexColor;
varying float vMaterialSlot;
void main() {
  vec4 color = vec4(vColor.rgb * vVertexColor.rgb, vColor.a * vVertexColor.a);
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
    vec3 mapped = max(outColor * exposure, vec3(0.0));
    if (uPostProcessParams.w < 1.5) {
      outColor = mapped / (vec3(1.0) + mapped);
    } else if (uPostProcessParams.w < 2.5) {
      outColor = vec3(1.0) - exp(-mapped);
    } else {
      outColor = clamp(
        (mapped * (2.51 * mapped + vec3(0.03))) /
        (mapped * (2.43 * mapped + vec3(0.59)) + vec3(0.14)),
        0.0,
        1.0);
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
float hash(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}
void main() {
  if (uSkyboxMode == 1) { gl_FragColor = vec4(uHorizonColor * max(uIntensity, 0.0), 1.0); return; }
  vec2 screen = vUv * 2.0 - 1.0;
  vec3 dir = normalize(uCameraForward + uCameraRight * screen.x * uProjectionScaleX + uCameraUp * screen.y * uProjectionScaleY);
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
  float t = clamp(uv.y, 0.0, 1.0);
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

  const skyboxPositionLocation = gl.getAttribLocation(skyboxProgram, 'aPosition');
  const texturedPositionLocation = gl.getAttribLocation(texturedProgram, 'aPosition');
  const texturedUvLocation = gl.getAttribLocation(texturedProgram, 'aTexCoord');
  const skyboxVertexBuffer = createRequiredBuffer(gl, 'utility:skybox:vertices');
  gl.bindBuffer(gl.ARRAY_BUFFER, skyboxVertexBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, 1, 1, -1, 1]), gl.STATIC_DRAW);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);

  const quadIndexBuffer = createRequiredBuffer(gl, 'utility:quad:indices');
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, quadIndexBuffer);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array([0, 1, 2, 0, 2, 3]), gl.STATIC_DRAW);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);
  const skyboxVertexArray = gl.createVertexArray();
  if (!skyboxVertexArray) throw new Error('WebGL 2 failed to allocate the required skybox vertex array.');
  gl.bindVertexArray(skyboxVertexArray);
  gl.bindBuffer(gl.ARRAY_BUFFER, skyboxVertexBuffer);
  gl.enableVertexAttribArray(skyboxPositionLocation);
  gl.vertexAttribPointer(skyboxPositionLocation, 2, gl.FLOAT, false, 8, 0);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, quadIndexBuffer);
  gl.bindVertexArray(null);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
  const gpuTimerExtension = gl.getExtension('EXT_disjoint_timer_query_webgl2');
  const debugRendererInfo = gl.getExtension('WEBGL_debug_renderer_info');

  return {
    canvas,
    metricsElement,
    centerCursorElement,
    gl,
    meshProgram,
    skyboxProgram,
    skyboxPositionLocation,
    skyboxTopColorLocation: gl.getUniformLocation(skyboxProgram, 'uTopColor'),
    skyboxHorizonColorLocation: gl.getUniformLocation(skyboxProgram, 'uHorizonColor'),
    skyboxBottomColorLocation: gl.getUniformLocation(skyboxProgram, 'uBottomColor'),
    skyboxIntensityLocation: gl.getUniformLocation(skyboxProgram, 'uIntensity'),
    skyboxModeLocation: gl.getUniformLocation(skyboxProgram, 'uSkyboxMode'),
    skyboxCameraRightLocation: gl.getUniformLocation(skyboxProgram, 'uCameraRight'),
    skyboxCameraUpLocation: gl.getUniformLocation(skyboxProgram, 'uCameraUp'),
    skyboxCameraForwardLocation: gl.getUniformLocation(skyboxProgram, 'uCameraForward'),
    skyboxProjectionScaleXLocation: gl.getUniformLocation(skyboxProgram, 'uProjectionScaleX'),
    skyboxProjectionScaleYLocation: gl.getUniformLocation(skyboxProgram, 'uProjectionScaleY'),
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
    skyboxVertexArray,
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
    meshVertexColorLocation: gl.getAttribLocation(meshProgram, 'aVertexColor'),
    meshBoneIndicesLocation: gl.getAttribLocation(meshProgram, 'aBoneIndices'),
    meshBoneWeightsLocation: gl.getAttribLocation(meshProgram, 'aBoneWeights'),
    meshViewProjLocation: gl.getUniformLocation(meshProgram, 'uViewProj'),
    meshModelLocation: gl.getUniformLocation(meshProgram, 'uModel'),
    meshColorLocation: gl.getUniformLocation(meshProgram, 'uColor'),
    meshUseInstancingLocation: gl.getUniformLocation(meshProgram, 'uUseInstancing'),
    meshUsePaletteLocation: gl.getUniformLocation(meshProgram, 'uUsePalette'),
    meshClientAnimationEnabledLocation: gl.getUniformLocation(meshProgram, 'uClientAnimationEnabled'),
    meshClientAnimationTimeLocation: gl.getUniformLocation(meshProgram, 'uClientAnimationTime'),
    meshClientAnimationAmplitudeLocation: gl.getUniformLocation(meshProgram, 'uClientAnimationAmplitude'),
    meshParticleModeLocation: gl.getUniformLocation(meshProgram, 'uParticleMode'),
    meshCameraRightUniformLocation: gl.getUniformLocation(meshProgram, 'uCameraRight'),
    meshCameraUpUniformLocation: gl.getUniformLocation(meshProgram, 'uCameraUp'),
    meshSkinningEnabledLocation: gl.getUniformLocation(meshProgram, 'uSkinningEnabled'),
    meshBoneTextureLocation: gl.getUniformLocation(meshProgram, 'uBoneTexture'),
    meshBoneTextureHeightLocation: gl.getUniformLocation(meshProgram, 'uBoneTextureHeight'),
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
    texturedPositionLocation,
    texturedUvLocation,
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
    retainedHandleRefs: new Map(),
    highScaleLayerHandleRefs: new Map(),
    retainedDrawOrder: [],
    controlPlanes: [],
    controlPlaneDrawList: [],
    emptyBatches: [],
    highScaleLayers: new Map(),
    highScaleDrawList: [],
    highScaleFramePacket: null,
    retainedDirectFramePacket: null,
    retainedCubemapIdsKey: null,
    retainedCubemapIds: [],
    forceAlphaDitherOpaque: false,
    frameViewProjection: new Float32Array(16),
    scratch3: new Float32Array(3),
    scratch4: new Float32Array(4),
    scratch16: new Float32Array(16),
    glState: {
      activeTextureUnit: -1,
      texture2D: new Array(16).fill(null),
      program: null,
      arrayBuffer: null,
      elementArrayBuffer: null,
      vao: null,
      blend: null,
      depthTest: null,
      depthMask: null,
      cullMode: null,
      cullFace: null,
      blendSrc: null,
      blendDst: null,
      depthFunc: null,
      materialKey: '',
      stateChanges: 0,
      uniformUpdates: 0,
      textureBinds: 0,
      bufferBinds: 0,
      vaoBinds: 0,
      legacyDrawPathCalls: 0,
      legacyDrawPathBlockedCalls: 0,
      legacyStringProtocolCalls: 0,
      bufferDataCalls: 0,
      dynamicBufferDataCalls: 0
    },
    maxDevicePixelRatio: 1.0,
    gpuSkinningSupported: (gl.getParameter(gl.MAX_VERTEX_TEXTURE_IMAGE_UNITS) || 0) > 0,
    gpuTimerExtension,
    gpuTimerActive: null,
    gpuTimerPending: [],
    gpuTimerLastMilliseconds: -1,
    gpuTimerValid: false,
    adapterName: debugRendererInfo ? String(gl.getParameter(debugRendererInfo.UNMASKED_RENDERER_WEBGL) || 'Unknown GPU') : String(gl.getParameter(gl.RENDERER) || 'Unknown GPU'),
    apiVersion: String(gl.getParameter(gl.VERSION) || 'WebGL 2'),
    lastCssLeft: null,
    lastCssTop: null,
    lastCssWidth: null,
    lastCssHeight: null,
    lastDisplay: null,
    lastMetricsText: null,
    lastMetricsVisible: false,
    lastCenterCursorVisible: false,
    frameId: 0,
    texturePayloadErrors: 0,
    palettePayloadErrors: 0,
    animationUploadBytes: 0,
    animationUploadBatches: 0,
    textureResources: new Map(),
    controlVertexBuffers: new Map(),
    vertexTextureUnits: gl.getParameter(gl.MAX_VERTEX_TEXTURE_IMAGE_UNITS) || 0,
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
  const gl = canvas.getContext('webgl2', contextOptions);
  if (!gl) throw new Error('WebGL 2 is required by the Avalonia3D RHI. WebGL 1 fallback is disabled.');

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
  host.contextMenuHandler = (event) => {
    const rect = canvas.getBoundingClientRect();
    const inside = event.clientX >= rect.left && event.clientX <= rect.right &&
      event.clientY >= rect.top && event.clientY <= rect.bottom;
    if (inside && canvas.style.display !== 'none') {
      event.preventDefault();
    }
  };
  host.contextLostHandler = (event) => {
    event.preventDefault();
    host.contextLost = true;
    host.cachedScenePacket = null;
  };
  host.contextRestoredHandler = () => {
    // WebGL invalidates every program/buffer/texture on context loss. Rebuild the JS-side
    // GPU runtime immediately and ask C# to clear its upload-version caches on the next frame.
    const pointerMoveHandler = host.pointerMoveHandler;
    const pointerLockChangeHandler = host.pointerLockChangeHandler;
    const contextMenuHandler = host.contextMenuHandler;
    const contextLostHandler = host.contextLostHandler;
    const contextRestoredHandler = host.contextRestoredHandler;
    const fresh = createHostState(canvas, gl, metricsElement, centerCursorElement);
    Object.assign(host, fresh);
    host.pointerMoveHandler = pointerMoveHandler;
    host.pointerLockChangeHandler = pointerLockChangeHandler;
    host.contextMenuHandler = contextMenuHandler;
    host.contextLostHandler = contextLostHandler;
    host.contextRestoredHandler = contextRestoredHandler;
    host.contextLost = false;
    host.contextResetPending = true;
    host.cachedScenePacket = null;
  };
  canvas.addEventListener('webglcontextlost', host.contextLostHandler, false);
  canvas.addEventListener('webglcontextrestored', host.contextRestoredHandler, false);
  document.addEventListener('mousemove', host.pointerMoveHandler, true);
  document.addEventListener('contextmenu', host.contextMenuHandler, true);
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
  if (host.gpuTimerActive) gl.deleteQuery(host.gpuTimerActive);
  for (const query of host.gpuTimerPending || []) gl.deleteQuery(query);
  for (const r of host.meshResources.values()) disposeMeshResource(gl, r);
  for (const b of host.instanceBuffers.values()) gl.deleteBuffer(b);
  for (const b of host.retainedBatches.values()) { gl.deleteBuffer(b.transformBuffer); gl.deleteBuffer(b.stateBuffer); if (b.particleBuffer) gl.deleteBuffer(b.particleBuffer); if (b.paletteTexture) gl.deleteTexture(b.paletteTexture); if (b.boneTexture) gl.deleteTexture(b.boneTexture); }
  for (const t of host.textureResources.values()) gl.deleteTexture(t.texture);
  for (const resource of host.controlVertexBuffers.values()) {
    gl.deleteVertexArray(resource.vao);
    gl.deleteBuffer(resource.buffer);
  }
  gl.deleteVertexArray(host.skyboxVertexArray);
  gl.deleteBuffer(host.skyboxVertexBuffer);
  gl.deleteBuffer(host.quadIndexBuffer);
  gl.deleteProgram(host.meshProgram);
  gl.deleteProgram(host.skyboxProgram);
  gl.deleteProgram(host.texturedProgram);
  if (host.contextLostHandler) host.canvas.removeEventListener('webglcontextlost', host.contextLostHandler, false);
  if (host.contextRestoredHandler) host.canvas.removeEventListener('webglcontextrestored', host.contextRestoredHandler, false);
  if (host.pointerMoveHandler) document.removeEventListener('mousemove', host.pointerMoveHandler, true);
  if (host.contextMenuHandler) document.removeEventListener('contextmenu', host.contextMenuHandler, true);
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
  const show = !!visible && width > 0 && height > 0;
  const display = show ? 'block' : 'none';
  if (host.lastDisplay !== display) {
    canvas.style.display = display;
    host.lastDisplay = display;
  }
  if (!show) {
    if (host.lastMetricsVisible) updateMetrics(hostId, '', false);
    if (host.lastCenterCursorVisible) updateCenterCursor(hostId, false);
    return;
  }

  const left = `${x}px`;
  const top = `${y}px`;
  const cssWidth = `${width}px`;
  const cssHeight = `${height}px`;
  if (host.lastCssLeft !== left) { canvas.style.left = left; host.lastCssLeft = left; }
  if (host.lastCssTop !== top) { canvas.style.top = top; host.lastCssTop = top; }
  if (host.lastCssWidth !== cssWidth) { canvas.style.width = cssWidth; host.lastCssWidth = cssWidth; }
  if (host.lastCssHeight !== cssHeight) { canvas.style.height = cssHeight; host.lastCssHeight = cssHeight; }

  // Hard-cap browser DPR. Retina/HiDPI displays otherwise multiply fragment cost by 4x
  // and make small demos look CPU/GPU-bound even when the scene itself is simple.
  const deviceDpr = window.devicePixelRatio || 1;
  const dpr = Math.max(1, Math.min(host.maxDevicePixelRatio || 1.25, deviceDpr));
  const pixelWidth = Math.max(1, Math.round(width * dpr));
  const pixelHeight = Math.max(1, Math.round(height * dpr));
  if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
    canvas.width = pixelWidth;
    canvas.height = pixelHeight;
    host.width = pixelWidth;
    host.height = pixelHeight;
  }
}

function disposeMeshResource(gl, r) {
  if (!r) return;
  if (r.vao) {
    gl.deleteVertexArray(r.vao);
    r.vao = null;
  }
  if (r.vertexBuffer) gl.deleteBuffer(r.vertexBuffer);
  if (r.normalBuffer) gl.deleteBuffer(r.normalBuffer);
  if (r.texCoordBuffer) gl.deleteBuffer(r.texCoordBuffer);
  if (r.tangentBuffer) gl.deleteBuffer(r.tangentBuffer);
  if (r.colorBuffer) gl.deleteBuffer(r.colorBuffer);
  if (r.materialSlotBuffer) gl.deleteBuffer(r.materialSlotBuffer);
  if (r.boneIndexBuffer) gl.deleteBuffer(r.boneIndexBuffer);
  if (r.boneWeightBuffer) gl.deleteBuffer(r.boneWeightBuffer);
  if (r.indexBuffer) gl.deleteBuffer(r.indexBuffer);
  if (r.wireframeIndexBuffer) gl.deleteBuffer(r.wireframeIndexBuffer);
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

function uploadTexture(hostId, textureId, width, height, rgbaBytesBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const safeWidth = Math.max(1, width | 0);
  const safeHeight = Math.max(1, height | 0);
  let resource = host.textureResources.get(textureId);
  if (!resource) {
    resource = { texture: createRequiredTexture(gl, `texture:${textureId}`), width: 0, height: 0 };
    host.textureResources.set(textureId, resource);
  }
  const rgbaBytes = coerceRgbaPayload(host, rgbaBytesBase64, safeWidth, safeHeight, 'texture');
  const canMipmap = isPowerOfTwo(safeWidth) && isPowerOfTwo(safeHeight);
  bindTexture2DCached(host, 0, resource.texture);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, canMipmap ? gl.LINEAR_MIPMAP_LINEAR : gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, canMipmap ? gl.REPEAT : gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, safeWidth, safeHeight, 0, gl.RGBA, gl.UNSIGNED_BYTE, rgbaBytes);
  if (canMipmap) gl.generateMipmap(gl.TEXTURE_2D);
  bindTexture2DCached(host, 0, null);
  resetTextureBindCache(host);
  resource.width = safeWidth;
  resource.height = safeHeight;
}


export function uploadTextureBytes(hostId, textureId, width, height, rgbaBytes) {
  uploadTexture(hostId, textureId, width, height, rgbaBytes);
}

export function uploadMeshGeometryBytes(hostId, meshId, vertexCount, indexCount, positionBytes, normalBytes, texCoordBytes, tangentBytes, colorBytes, materialSlotBytes, boneIndexBytes, boneWeightBytes, indexBytes, indexElementSize, wireframeIndexBytes, wireframeIndexElementSize, hasTexCoords, hasTangents, hasColors, hasMaterialSlots, hasSkinWeights, vertexLayout) {
  const positions = decodeFloat32Payload(positionBytes);
  const normals = decodeFloat32Payload(normalBytes);
  const texCoords = hasTexCoords ? decodeFloat32Payload(texCoordBytes) : new Float32Array(0);
  const tangents = hasTangents ? decodeFloat32Payload(tangentBytes) : new Float32Array(0);
  const colors0 = hasColors ? decodeFloat32Payload(colorBytes) : new Float32Array(0);
  const materialSlots = hasMaterialSlots ? decodeFloat32Payload(materialSlotBytes) : new Float32Array(0);
  const boneIndices = hasSkinWeights ? decodeFloat32Payload(boneIndexBytes) : new Float32Array(0);
  const boneWeights = hasSkinWeights ? decodeFloat32Payload(boneWeightBytes) : new Float32Array(0);
  const indices = decodeIndexPayload(indexBytes, indexElementSize);
  const wireframeIndices = decodeIndexPayload(wireframeIndexBytes, wireframeIndexElementSize || 2);
  uploadMeshGeometryTyped(hostId, meshId, vertexCount | 0, indexCount | 0, positions, normals, texCoords, tangents, colors0, materialSlots, boneIndices, boneWeights, indices, wireframeIndices, !!hasTexCoords, !!hasTangents, !!hasColors, !!hasMaterialSlots, !!hasSkinWeights, vertexLayout || 'PositionNormal');
}

function computeLocalBounds3(positions) {
  if (!positions || positions.length < 3) return { center: [0, 0, 0], extents: [0, 0, 0], valid: false };
  let minX = Number.POSITIVE_INFINITY, minY = Number.POSITIVE_INFINITY, minZ = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY, maxY = Number.NEGATIVE_INFINITY, maxZ = Number.NEGATIVE_INFINITY;
  for (let i = 0; i + 2 < positions.length; i += 3) {
    const x = positions[i] || 0, y = positions[i + 1] || 0, z = positions[i + 2] || 0;
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (y < minY) minY = y; if (y > maxY) maxY = y;
    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
  }
  if (!Number.isFinite(minX) || !Number.isFinite(maxX)) return { center: [0, 0, 0], extents: [0, 0, 0], valid: false };
  return {
    center: [(minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5],
    extents: [(maxX - minX) * 0.5, (maxY - minY) * 0.5, (maxZ - minZ) * 0.5],
    valid: true
  };
}

function uploadMeshGeometryTyped(hostId, meshId, vertexCount, indexCount, positions, normals, texCoords0, tangents, colors0, materialSlots, boneIndices, boneWeights, indices, wireframeIndices, hasTexCoords, hasTangents, hasColors, hasMaterialSlots, hasSkinWeights, vertexLayout) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const safeVertexCount = vertexCount | 0;
  const safeIndexCount = indexCount | 0;
  if (safeVertexCount <= 0 || safeIndexCount <= 0) throw new Error(`Mesh '${meshId}' has an empty GPU geometry submission.`);
  requireFloatStream('positions', positions, safeVertexCount * 3, true);
  requireFloatStream('normals', normals, safeVertexCount * 3, true);
  requireFloatStream('texCoords0', texCoords0, safeVertexCount * 2, !!hasTexCoords);
  requireFloatStream('tangents', tangents, safeVertexCount * 4, !!hasTangents);
  requireFloatStream('colors0', colors0, safeVertexCount * 4, !!hasColors);
  requireFloatStream('materialSlots', materialSlots, safeVertexCount, !!hasMaterialSlots);
  requireFloatStream('boneIndices', boneIndices, safeVertexCount * 4, !!hasSkinWeights);
  requireFloatStream('boneWeights', boneWeights, safeVertexCount * 4, !!hasSkinWeights);
  if (!indices || indices.length !== safeIndexCount) throw new Error(`Mesh '${meshId}' index payload has ${indices ? indices.length : 0} elements; exactly ${safeIndexCount} are required.`);
  let resource = host.meshResources.get(meshId);
  if (!resource) {
    resource = {
      vertexBuffer: createRequiredBuffer(gl, `mesh:${meshId}:position`),
      normalBuffer: createRequiredBuffer(gl, `mesh:${meshId}:normal`),
      texCoordBuffer: null,
      tangentBuffer: null,
      colorBuffer: null,
      materialSlotBuffer: null,
      boneIndexBuffer: null,
      boneWeightBuffer: null,
      indexBuffer: createRequiredBuffer(gl, `mesh:${meshId}:index`),
      wireframeIndexBuffer: null,
      vao: null,
      indexCount: 0,
      wireframeIndexCount: 0,
      wireframeIndexType: gl.UNSIGNED_SHORT,
      indexType: gl.UNSIGNED_SHORT,
      hasTexCoords: false,
      hasTangents: false,
      hasColors: false,
      hasMaterialSlots: false,
      hasSkinWeights: false,
      meshId,
      meshIndex: host.meshResourceList.length
    };
    host.meshResources.set(meshId, resource);
    host.meshIdToIndex.set(meshId, resource.meshIndex);
    host.meshResourceList.push(resource);
  }

  const localBounds = computeLocalBounds3(positions);
  resource.localCenter = localBounds.center;
  resource.localExtents = localBounds.extents;
  resource.localBoundsValid = localBounds.valid;
  bindArrayBufferCached(host, resource.vertexBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, positions, gl.STATIC_DRAW);
  bindArrayBufferCached(host, resource.normalBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, normals, gl.STATIC_DRAW);

  resource.hasTexCoords = !!hasTexCoords;
  if (resource.hasTexCoords) {
    if (!resource.texCoordBuffer) resource.texCoordBuffer = createRequiredBuffer(gl, `mesh:${meshId}:uv0`);
    bindArrayBufferCached(host, resource.texCoordBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, texCoords0, gl.STATIC_DRAW);
  } else if (resource.texCoordBuffer) {
    gl.deleteBuffer(resource.texCoordBuffer);
    resource.texCoordBuffer = null;
  }

  resource.hasTangents = !!hasTangents;
  if (resource.hasTangents) {
    if (!resource.tangentBuffer) resource.tangentBuffer = createRequiredBuffer(gl, `mesh:${meshId}:tangent`);
    bindArrayBufferCached(host, resource.tangentBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, tangents, gl.STATIC_DRAW);
  } else if (resource.tangentBuffer) {
    gl.deleteBuffer(resource.tangentBuffer);
    resource.tangentBuffer = null;
  }

  resource.hasColors = !!hasColors;
  if (resource.hasColors) {
    if (!resource.colorBuffer) resource.colorBuffer = createRequiredBuffer(gl, `mesh:${meshId}:color0`);
    bindArrayBufferCached(host, resource.colorBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, colors0, gl.STATIC_DRAW);
  } else if (resource.colorBuffer) {
    gl.deleteBuffer(resource.colorBuffer);
    resource.colorBuffer = null;
  }

  resource.hasMaterialSlots = !!hasMaterialSlots;
  if (resource.hasMaterialSlots) {
    if (!resource.materialSlotBuffer) resource.materialSlotBuffer = createRequiredBuffer(gl, `mesh:${meshId}:material-slot`);
    bindArrayBufferCached(host, resource.materialSlotBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, materialSlots, gl.STATIC_DRAW);
  } else if (resource.materialSlotBuffer) {
    gl.deleteBuffer(resource.materialSlotBuffer);
    resource.materialSlotBuffer = null;
  }

  resource.hasSkinWeights = !!hasSkinWeights;
  if (resource.hasSkinWeights) {
    if (!resource.boneIndexBuffer) resource.boneIndexBuffer = createRequiredBuffer(gl, `mesh:${meshId}:bone-index`);
    if (!resource.boneWeightBuffer) resource.boneWeightBuffer = createRequiredBuffer(gl, `mesh:${meshId}:bone-weight`);
    bindArrayBufferCached(host, resource.boneIndexBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, boneIndices, gl.STATIC_DRAW);
    bindArrayBufferCached(host, resource.boneWeightBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, boneWeights, gl.STATIC_DRAW);
  } else {
    if (resource.boneIndexBuffer) gl.deleteBuffer(resource.boneIndexBuffer);
    if (resource.boneWeightBuffer) gl.deleteBuffer(resource.boneWeightBuffer);
    resource.boneIndexBuffer = null;
    resource.boneWeightBuffer = null;
  }

  if (indices instanceof Uint32Array) {
    resource.indexType = gl.UNSIGNED_INT;
  } else {
    resource.indexType = gl.UNSIGNED_SHORT;
  }
  bindElementArrayBufferCached(host, resource.indexBuffer);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);
  resource.indexCount = safeIndexCount;

  if (wireframeIndices && wireframeIndices.length > 0) {
    if (!resource.wireframeIndexBuffer) resource.wireframeIndexBuffer = createRequiredBuffer(gl, `mesh:${meshId}:wireframe-index`);
    let safeWireframe = wireframeIndices;
    if (wireframeIndices instanceof Uint32Array) {
      resource.wireframeIndexType = gl.UNSIGNED_INT;
    } else {
      resource.wireframeIndexType = gl.UNSIGNED_SHORT;
      if (!(wireframeIndices instanceof Uint16Array)) safeWireframe = new Uint16Array(wireframeIndices);
    }
    bindElementArrayBufferCached(host, resource.wireframeIndexBuffer);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, safeWireframe, gl.STATIC_DRAW);
    resource.wireframeIndexCount = safeWireframe.length;
  } else {
    if (resource.wireframeIndexBuffer) gl.deleteBuffer(resource.wireframeIndexBuffer);
    resource.wireframeIndexBuffer = null;
    resource.wireframeIndexCount = 0;
    resource.wireframeIndexType = gl.UNSIGNED_SHORT;
  }

  rebuildMeshVao(host, resource);
  bindArrayBufferCached(host, null);
  bindElementArrayBufferCached(host, null);
}

function requireFloatStream(name, stream, expectedLength, present) {
  const actualLength = stream ? stream.length : 0;
  const requiredLength = present ? expectedLength : 0;
  if (actualLength !== requiredLength) {
    throw new Error(`Mesh ${name} payload has ${actualLength} floats; exactly ${requiredLength} are required by its declared layout.`);
  }
}

function decodeIndexPayload(payload, elementSize) {
  const bytes = toUint8Array(payload);
  if (bytes.byteLength === 0) return new Uint16Array(0);
  if ((elementSize | 0) === 4) {
    if ((bytes.byteOffset & 3) === 0 && (bytes.byteLength & 3) === 0) return new Uint32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
    const copy = new Uint8Array(bytes.byteLength); copy.set(bytes); return new Uint32Array(copy.buffer);
  }
  if ((bytes.byteOffset & 1) === 0 && (bytes.byteLength & 1) === 0) return new Uint16Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 2);
  const copy = new Uint8Array(bytes.byteLength); copy.set(bytes); return new Uint16Array(copy.buffer);
}

function maxArrayValue(values) {
  let max = 0;
  for (let i = 0; i < values.length; i++) if (values[i] > max) max = values[i];
  return max;
}

function getOrCreateControlResource(host, id) {
  let resource = host.controlVertexBuffers.get(id);
  if (resource) return resource;
  const { gl } = host;
  const buffer = gl.createBuffer();
  const vao = gl.createVertexArray();
  if (!buffer || !vao) {
    if (buffer) gl.deleteBuffer(buffer);
    if (vao) gl.deleteVertexArray(vao);
    throw new Error(`WebGL 2 failed to allocate the required control-plane GPU resource '${id}'.`);
  }
  resource = { buffer, vao, initialized: false };
  bindVertexArrayCached(host, vao);
  bindArrayBufferCached(host, buffer);
  gl.enableVertexAttribArray(host.texturedPositionLocation);
  gl.vertexAttribPointer(host.texturedPositionLocation, 3, gl.FLOAT, false, 20, 0);
  gl.enableVertexAttribArray(host.texturedUvLocation);
  gl.vertexAttribPointer(host.texturedUvLocation, 2, gl.FLOAT, false, 20, 12);
  bindElementArrayBufferCached(host, host.quadIndexBuffer);
  bindVertexArrayCached(host, null);
  host.controlVertexBuffers.set(id, resource);
  return resource;
}

function getOrCreateInstanceBuffer(host, id) {
  let buffer = host.instanceBuffers.get(id);
  if (!buffer) { buffer = createRequiredBuffer(host.gl, `instance:${id}`); host.instanceBuffers.set(id, buffer); }
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

function requireExactFloatPayload(label, payload, expectedLength) {
  const values = decodeFloat32Payload(payload);
  if (values.length !== expectedLength) {
    throw new Error(`${label} payload has ${values.length} floats; exactly ${expectedLength} are required.`);
  }
  return values;
}

function expectedRgbaByteCount(width, height) {
  const w = Math.max(1, width | 0);
  const h = Math.max(1, height | 0);
  return w * h * 4;
}

function isPowerOfTwo(value) {
  const v = value | 0;
  return v > 0 && (v & (v - 1)) === 0;
}

function coerceRgbaPayload(host, payload, width, height, kind) {
  const expected = expectedRgbaByteCount(width, height);
  const source = typeof payload === 'string' ? decodeBase64Bytes(payload) : toUint8Array(payload);
  if (source.byteLength === expected) return source;

  if (kind === 'palette') host.palettePayloadErrors = (host.palettePayloadErrors || 0) + 1;
  else host.texturePayloadErrors = (host.texturePayloadErrors || 0) + 1;

  throw new Error(`${kind || 'texture'} RGBA payload has ${source.byteLength} bytes; exactly ${expected} are required.`);
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


const fnv64Offset = typeof BigInt !== 'undefined' ? BigInt('0xcbf29ce484222325') : null;
const fnv64Prime = typeof BigInt !== 'undefined' ? BigInt('0x100000001b3') : null;
const uint64Mask = typeof BigInt !== 'undefined' ? BigInt('0xffffffffffffffff') : null;

function stableHash64Utf16(value) {
  if (typeof BigInt === 'undefined') return null;
  const s = value === null || value === undefined ? '' : String(value);
  let hash = fnv64Offset;
  for (let i = 0; i < s.length; i++) {
    hash ^= BigInt(s.charCodeAt(i));
    hash = (hash * fnv64Prime) & uint64Mask;
  }
  return hash;
}

function handleKey(lo, hi) {
  return ((lo >>> 0).toString(16)) + ':' + ((hi >>> 0).toString(16));
}

function handleKeyFromString(value) {
  const hash = stableHash64Utf16(value);
  if (hash === null) return null;
  const lo = Number(hash & BigInt(0xffffffff));
  const hi = Number((hash >> BigInt(32)) & BigInt(0xffffffff));
  return handleKey(lo, hi);
}

function registerRetainedBatchHandle(host, batch) {
  if (!host || !batch) return;
  const key = handleKeyFromString(batch.batchId);
  if (!key) return;
  batch.handleKey = key;
  host.retainedHandleRefs.set(key, { id: batch.batchId, batchIndex: batch.batchIndex, transparent: false });
}

function registerHighScaleLayerHandle(host, layer) {
  if (!host || !layer) return;
  const key = handleKeyFromString(layer.id);
  if (!key) return;
  layer.handleKey = key;
  host.highScaleLayerHandleRefs.set(key, { id: layer.id, kind: 'highScaleLayer', layerId: layer.id, transparent: false });
}

function getOrCreateRetainedBatch(host, batchId) {
  let batch = host.retainedBatches.get(batchId);
  if (!batch) {
    const gl = host.gl;
    const transformBuffer = createRequiredBuffer(gl, `retained:${batchId}:transform`);
    let stateBuffer;
    try {
      stateBuffer = createRequiredBuffer(gl, `retained:${batchId}:state`);
    } catch (error) {
      gl.deleteBuffer(transformBuffer);
      throw error;
    }
    batch = {
      batchId,
      batchIndex: host.retainedBatchList.length,
      meshId: '',
      meshIndex: -1,
      lightingEnabled: 0,
      usePalette: false,
      instanceCount: 0,
      transformBuffer,
      stateBuffer,
      baseTransformData: new Float32Array(0),
      baseStateData: new Float32Array(0),
      visibleTransformData: new Float32Array(0),
      visibleStateData: new Float32Array(0),
      culledTransformBuffer: null,
      culledStateBuffer: null,
      culledTransformCapacityFloats: 0,
      culledStateCapacityFloats: 0,
      animatedTransformData: new Float32Array(0),
      animationFrameId: -1,
      animationActive: false,
      paletteTexture: null,
      paletteWidth: 1,
      paletteHeight: 1,
      normalMapStrength: 0,
      baseColorTextureId: '',
      normalTextureId: '',
      metallicRoughnessTextureId: '',
      emissiveTextureId: '',
      metallic: 0,
      roughness: 1,
      alphaCutoff: 0,
      transparent: false,
      cullMode: 0,
      emissiveColor: [0, 0, 0, 0],
      materialTextureKey: ''
    };
    host.retainedBatches.set(batchId, batch);
    host.retainedBatchIdToIndex.set(batchId, batch.batchIndex);
    host.retainedBatchList.push(batch);
    registerRetainedBatchHandle(host, batch);
  }
  return batch;
}

function uploadRetainedBatchTransforms(hostId, batchId, meshId, lightingEnabled, usePalette, instanceCount, transformFloatsBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const batch = getOrCreateRetainedBatch(host, batchId);
  batch.meshId = meshId;
  batch.meshIndex = host.meshIdToIndex.has(meshId) ? host.meshIdToIndex.get(meshId) : -1;
  batch.lightingEnabled = lightingEnabled || 0;
  batch.usePalette = !!usePalette;
  batch.instanceCount = instanceCount | 0;
  if (batch.instanceCount < 0) throw new Error(`Retained batch '${batchId}' has a negative instance count.`);
  const transforms = decodeFloat32Payload(transformFloatsBase64);
  const expectedTransformFloats = batch.instanceCount * 16;
  if (transforms.length !== expectedTransformFloats) throw new Error(`Retained batch '${batchId}' has ${transforms.length} transform floats; exactly ${expectedTransformFloats} are required.`);
  batch.baseTransformData = new Float32Array(transforms);
  batch.animatedTransformData = new Float32Array(batch.baseTransformData.length);
  batch.animationFrameId = -1;
  batch.animationActive = false;
  bindArrayBufferCached(host, batch.transformBuffer);
  trackedBufferData(host, gl.ARRAY_BUFFER, batch.baseTransformData, gl.DYNAMIC_DRAW);
  bindArrayBufferCached(host, null);
}



export function uploadRetainedBatchTransformsBytes(hostId, batchId, meshId, lightingEnabled, usePalette, instanceCount, transformBytes) {
  uploadRetainedBatchTransforms(hostId, batchId, meshId, lightingEnabled, usePalette, instanceCount, transformBytes);
}

function uploadRetainedBatchTransformsRange(hostId, batchId, startInstance, transformFloatsBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const batch = host.retainedBatches.get(batchId);
  if (!batch || !batch.transformBuffer) throw new Error(`Transform patch references unknown retained batch '${batchId}'.`);
  const transforms = decodeFloat32Payload(transformFloatsBase64);
  if (transforms.length === 0) return;
  const start = startInstance | 0;
  if (start < 0 || (transforms.length % 16) !== 0) throw new Error(`Retained batch '${batchId}' has an invalid transform patch range.`);
  const offsetFloats = start * 16;
  if (!batch.baseTransformData || offsetFloats + transforms.length > batch.baseTransformData.length) throw new Error(`Retained batch '${batchId}' transform patch exceeds its allocated instance range.`);
  batch.baseTransformData.set(transforms, offsetFloats);
  const { gl } = host;
  if (!batch.animationActive) {
    bindArrayBufferCached(host, batch.transformBuffer);
    gl.bufferSubData(gl.ARRAY_BUFFER, offsetFloats * 4, transforms);
    bindArrayBufferCached(host, null);
  }
}

function uploadRetainedBatchState(hostId, batchId, usePalette, paletteWidth, paletteHeight, stateFloatsBase64, paletteRgbaBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const batch = getOrCreateRetainedBatch(host, batchId);
  batch.usePalette = !!usePalette;
  const states = decodeFloat32Payload(stateFloatsBase64);
  const expectedStateFloats = batch.instanceCount * 4;
  if (states.length !== expectedStateFloats) throw new Error(`Retained batch '${batchId}' has ${states.length} state floats; exactly ${expectedStateFloats} are required.`);
  batch.baseStateData = new Float32Array(states);
  bindArrayBufferCached(host, batch.stateBuffer);
  trackedBufferData(host, gl.ARRAY_BUFFER, batch.baseStateData, gl.DYNAMIC_DRAW);
  bindArrayBufferCached(host, null);
  if (batch.usePalette && hasNonEmptyPayload(paletteRgbaBase64)) {
    if (!batch.paletteTexture) batch.paletteTexture = createRequiredTexture(gl, `retained:${batchId}:palette`);
    batch.paletteWidth = Math.max(1, paletteWidth || 1);
    batch.paletteHeight = Math.max(1, paletteHeight || 1);
    const rgbaBytes = coerceRgbaPayload(host, paletteRgbaBase64, batch.paletteWidth, batch.paletteHeight, 'palette');
    bindTexture2DCached(host, 0, batch.paletteTexture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, batch.paletteWidth, batch.paletteHeight, 0, gl.RGBA, gl.UNSIGNED_BYTE, rgbaBytes);
    bindTexture2DCached(host, 0, null);
  resetTextureBindCache(host);
  }
}


export function uploadRetainedBatchStateBytes(hostId, batchId, usePalette, paletteWidth, paletteHeight, stateBytes, paletteRgbaBytes) {
  uploadRetainedBatchState(hostId, batchId, usePalette, paletteWidth, paletteHeight, stateBytes, paletteRgbaBytes);
}

export function uploadRetainedBatchMaterial(
  hostId,
  batchId,
  normalMapStrength,
  baseColorTextureId,
  normalTextureId,
  metallicRoughnessTextureId,
  emissiveTextureId,
  metallic,
  roughness,
  alphaCutoff,
  transparent,
  emissiveR,
  emissiveG,
  emissiveB,
  emissiveA,
  cullMode) {
  const host = hosts.get(hostId);
  if (!host) return;
  const batch = getOrCreateRetainedBatch(host, batchId);
  batch.normalMapStrength = normalMapStrength || 0;
  batch.baseColorTextureId = baseColorTextureId || '';
  batch.normalTextureId = normalTextureId || '';
  batch.metallicRoughnessTextureId = metallicRoughnessTextureId || '';
  batch.emissiveTextureId = emissiveTextureId || '';
  batch.materialTextureKey = (batch.baseColorTextureId || '') + '|' + (batch.normalTextureId || '') + '|' + (batch.metallicRoughnessTextureId || '') + '|' + (batch.emissiveTextureId || '');
  batch.metallic = finiteOr(metallic, 0);
  batch.roughness = finiteOr(roughness, 1);
  batch.alphaCutoff = finiteOr(alphaCutoff, 0);
  batch.transparent = !!transparent;
  batch.emissiveColor = [emissiveR || 0, emissiveG || 0, emissiveB || 0, emissiveA || 0];
  const resolvedCullMode = Number(cullMode || 0) | 0;
  if (resolvedCullMode < 0 || resolvedCullMode > 2) throw new Error(`Unsupported CullMode ${resolvedCullMode}.`);
  batch.cullMode = resolvedCullMode;
}

export function uploadRetainedBatchTransformsRangeBytes(hostId, batchId, startInstance, transformBytes) {
  uploadRetainedBatchTransformsRange(hostId, batchId, startInstance, transformBytes);
}

function uploadRetainedBatchStateRange(hostId, batchId, startInstance, stateFloatsBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const batch = host.retainedBatches.get(batchId);
  if (!batch || !batch.stateBuffer) throw new Error(`State patch references unknown retained batch '${batchId}'.`);
  const states = decodeFloat32Payload(stateFloatsBase64);
  if (states.length === 0) return;
  const start = startInstance | 0;
  if (start < 0 || (states.length % 4) !== 0) throw new Error(`Retained batch '${batchId}' has an invalid state patch range.`);
  const offsetFloats = start * 4;
  if (!batch.baseStateData || offsetFloats + states.length > batch.baseStateData.length) throw new Error(`Retained batch '${batchId}' state patch exceeds its allocated instance range.`);
  batch.baseStateData.set(states, offsetFloats);
  const { gl } = host;
  bindArrayBufferCached(host, batch.stateBuffer);
  gl.bufferSubData(gl.ARRAY_BUFFER, offsetFloats * 4, states);
  bindArrayBufferCached(host, null);
}


export function uploadRetainedBatchStateRangeBytes(hostId, batchId, startInstance, stateBytes) {
  uploadRetainedBatchStateRange(hostId, batchId, startInstance, stateBytes);
}

function removeRetainedBatchFromIndexTables(host, batch) {
  if (!batch) return;
  host.retainedBatchIdToIndex.delete(batch.batchId);
  if (batch.handleKey) host.retainedHandleRefs.delete(batch.handleKey);
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
  if (batch.culledTransformBuffer) gl.deleteBuffer(batch.culledTransformBuffer);
  if (batch.culledStateBuffer) gl.deleteBuffer(batch.culledStateBuffer);
  if (batch.paletteTexture) gl.deleteTexture(batch.paletteTexture);
  if (batch.boneTexture) gl.deleteTexture(batch.boneTexture);
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
  useProgramCached(host, host.skyboxProgram);
  setDepthTestCached(host, false);
  setDepthMaskCached(host, false);
  uniform3fvFromArray(host, host.skyboxTopColorLocation, host.scratch3, packet.skyboxTopColor, [0.28, 0.45, 0.72]);
  uniform3fvFromArray(host, host.skyboxHorizonColorLocation, host.scratch3, packet.skyboxHorizonColor, [0.62, 0.76, 0.94]);
  uniform3fvFromArray(host, host.skyboxBottomColorLocation, host.scratch3, packet.skyboxBottomColor, [0.82, 0.86, 0.90]);
  const skyboxIntensity = Number.isFinite(packet.skyboxIntensity) ? Math.max(0, packet.skyboxIntensity) : 1.0;
  const defaultProjectionScaleY = Math.tan(55 * Math.PI / 360);
  const defaultProjectionScaleX = defaultProjectionScaleY * Math.max(1, packet.width || host.width || 1) / Math.max(1, packet.height || host.height || 1);
  const projectionScaleX = Number.isFinite(packet.skyboxProjectionScaleX) && packet.skyboxProjectionScaleX > 0
    ? packet.skyboxProjectionScaleX
    : defaultProjectionScaleX;
  const projectionScaleY = Number.isFinite(packet.skyboxProjectionScaleY) && packet.skyboxProjectionScaleY > 0
    ? packet.skyboxProjectionScaleY
    : defaultProjectionScaleY;
  uniform1fCached(host, host.skyboxIntensityLocation, skyboxIntensity);
  uniform1iCached(host, host.skyboxModeLocation, packet.skyboxMode | 0);
  uniform3fvFromArray(host, host.skyboxCameraRightLocation, host.scratch3, packet.cameraRight, [1, 0, 0]);
  uniform3fvFromArray(host, host.skyboxCameraUpLocation, host.scratch3, packet.cameraUp, [0, 1, 0]);
  uniform3fvFromArray(host, host.skyboxCameraForwardLocation, host.scratch3, packet.cameraForward, [0, 0, -1]);
  uniform1fCached(host, host.skyboxProjectionScaleXLocation, projectionScaleX);
  uniform1fCached(host, host.skyboxProjectionScaleYLocation, projectionScaleY);
  bindTextureSlot(host, packet.skyboxTextureId || null, host.skyboxTextureLocation, host.skyboxTextureEnabledLocation, gl.TEXTURE0, 0);
  if ((packet.skyboxMode | 0) === 3) bindSkyboxCubemapTextures(host, packet.skyboxCubemapTextureIds || []);
  else uniform1fCached(host, host.skyboxCubemapEnabledLocation, 0);
  bindVertexArrayCached(host, host.skyboxVertexArray);
  gl.drawElements(gl.TRIANGLES, 6, gl.UNSIGNED_SHORT, 0);
  setDepthMaskCached(host, true);
  setDepthTestCached(host, true);
}

function copyToScratch(target, source, count) {
  const src = source || [];
  for (let i = 0; i < count; i++) target[i] = src[i] || 0;
  return target;
}

function getUniformCache(gl) {
  let cache = uniformCacheByContext.get(gl);
  if (!cache) {
    cache = new Map();
    uniformCacheByContext.set(gl, cache);
  }
  return cache;
}

function uniform3fvFromArray(host, location, scratch, source, defaultValue) {
  const { gl } = host;
  if (location === null || location === undefined) return;
  const src = source || defaultValue;
  const x = src[0] || 0;
  const y = src[1] || 0;
  const z = src[2] || 0;
  const cache = getUniformCache(gl);
  const last = cache.get(location);
  if (last && last.length === 3 && last[0] === x && last[1] === y && last[2] === z) return;
  scratch[0] = x; scratch[1] = y; scratch[2] = z;
  gl.uniform3fv(location, scratch);
  if (host.glState) host.glState.uniformUpdates++;
  if (last) { last[0] = x; last[1] = y; last[2] = z; }
  else cache.set(location, [x, y, z]);
}

function uniform4fvFromArray(host, location, scratch, source, defaultValue) {
  const { gl } = host;
  if (location === null || location === undefined) return;
  const src = source || defaultValue;
  const x = src[0] || 0;
  const y = src[1] || 0;
  const z = src[2] || 0;
  const w = src[3] || 0;
  const cache = getUniformCache(gl);
  const last = cache.get(location);
  if (last && last.length === 4 && last[0] === x && last[1] === y && last[2] === z && last[3] === w) return;
  scratch[0] = x; scratch[1] = y; scratch[2] = z; scratch[3] = w;
  gl.uniform4fv(location, scratch);
  if (host.glState) host.glState.uniformUpdates++;
  if (last) { last[0] = x; last[1] = y; last[2] = z; last[3] = w; }
  else cache.set(location, [x, y, z, w]);
}

function resolveGpuTimers(host) {
  const ext = host && host.gpuTimerExtension;
  if (!ext || !host.gpuTimerPending || host.gpuTimerPending.length === 0) return;
  const { gl } = host;
  const disjoint = !!gl.getParameter(ext.GPU_DISJOINT_EXT);
  for (let i = 0; i < host.gpuTimerPending.length;) {
    const query = host.gpuTimerPending[i];
    const available = !!gl.getQueryParameter(query, gl.QUERY_RESULT_AVAILABLE);
    if (!available && !disjoint) { i++; continue; }
    if (!disjoint) {
      const nanoseconds = Number(gl.getQueryParameter(query, gl.QUERY_RESULT) || 0);
      if (Number.isFinite(nanoseconds) && nanoseconds >= 0) {
        host.gpuTimerLastMilliseconds = nanoseconds / 1000000;
        host.gpuTimerValid = true;
      }
    } else {
      host.gpuTimerLastMilliseconds = -1;
      host.gpuTimerValid = false;
    }
    gl.deleteQuery(query);
    host.gpuTimerPending.splice(i, 1);
  }
}

export function beginGpuFrameTimer(hostId) {
  const host = hosts.get(hostId);
  if (!host || !host.gpuTimerExtension || host.gpuTimerActive) return false;
  resolveGpuTimers(host);
  if (host.gpuTimerPending.length >= 4) return false;
  const query = host.gl.createQuery();
  if (!query) throw new Error('WebGL 2 failed to allocate a GPU timer query.');
  host.gl.beginQuery(host.gpuTimerExtension.TIME_ELAPSED_EXT, query);
  host.gpuTimerActive = query;
  return true;
}

export function endGpuFrameTimer(hostId) {
  const host = hosts.get(hostId);
  if (!host || !host.gpuTimerActive || !host.gpuTimerExtension) return;
  host.gl.endQuery(host.gpuTimerExtension.TIME_ELAPSED_EXT);
  host.gpuTimerPending.push(host.gpuTimerActive);
  host.gpuTimerActive = null;
}

export function getLastGpuFrameMilliseconds(hostId) {
  const host = hosts.get(hostId);
  if (!host) return -1;
  resolveGpuTimers(host);
  return host.gpuTimerValid ? host.gpuTimerLastMilliseconds : -1;
}

export function getRhiFeatureMask(hostId) {
  const host = hosts.get(hostId);
  if (!host) return 0;
  let features = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5) | (1 << 6);
  if (host.gpuSkinningSupported) features |= (1 << 7) | (1 << 8);
  if (host.gpuTimerExtension) features |= (1 << 9);
  return features;
}

export function getRhiLimit(hostId, index) {
  const host = hosts.get(hostId);
  if (!host) return 0;
  const { gl } = host;
  switch (index | 0) {
    case 0: return gl.getParameter(gl.MAX_TEXTURE_SIZE) || 0;
    case 1: return gl.getParameter(gl.MAX_COMBINED_TEXTURE_IMAGE_UNITS) || 0;
    case 2: return gl.getParameter(gl.MAX_VERTEX_TEXTURE_IMAGE_UNITS) || 0;
    case 3: return gl.getParameter(gl.MAX_VERTEX_ATTRIBS) || 0;
    case 4: return gl.getParameter(gl.MAX_RENDERBUFFER_SIZE) || 0;
    case 5: return gl.getParameter(gl.MAX_SAMPLES) || 0;
    case 6: return gl.getParameter(gl.MAX_UNIFORM_BLOCK_SIZE) || 0;
    case 7: return gl.getParameter(gl.MAX_TRANSFORM_FEEDBACK_SEPARATE_ATTRIBS) || 0;
    default: return 0;
  }
}

export function getRhiAdapterName(hostId) {
  return hosts.get(hostId)?.adapterName || 'Unknown GPU';
}

export function getRhiApiVersion(hostId) {
  return hosts.get(hostId)?.apiVersion || 'WebGL 2';
}

export function uploadRetainedBatchSkinningBytes(hostId, batchId, enabled, boneCount, boneMatrixBytes) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const batch = getOrCreateRetainedBatch(host, batchId);
  if (!enabled || boneCount <= 0) {
    batch.skinningEnabled = false;
    batch.boneCount = 0;
    return;
  }
  if (!host.gpuSkinningSupported) throw new Error('GPU skinning was submitted without required WebGL 2 vertex-texture capability.');

  const count = boneCount | 0;
  const expected = count * 16;
  const matrices = decodeFloat32Payload(boneMatrixBytes);
  if (matrices.length !== expected) {
    throw new Error(`GPU skinning payload for '${batchId}' has ${matrices.length} floats; exactly ${expected} are required.`);
  }

  const internalFormat = gl.RGBA32F;
  const needsCreate = !batch.boneTexture;
  if (needsCreate) {
    batch.boneTexture = createRequiredTexture(gl, `retained:${batchId}:bones`);
    batch.boneTextureWidth = 0;
    batch.boneTextureHeight = 0;
  }

  bindTexture2DCached(host, 0, batch.boneTexture);
  if (needsCreate) {
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  }
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);

  // Do not redefine the bone texture every animation frame. texImage2D may force
  // driver allocation/synchronization and can produce multi-second stalls on WASM/WebGL.
  // Allocate only when the bone count changes, then stream matrix rows with texSubImage2D.
  if (batch.boneTextureWidth !== 4 || batch.boneTextureHeight !== count) {
    gl.texImage2D(gl.TEXTURE_2D, 0, internalFormat, 4, count, 0, gl.RGBA, gl.FLOAT, null);
    batch.boneTextureWidth = 4;
    batch.boneTextureHeight = count;
  }
  gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 4, count, gl.RGBA, gl.FLOAT, matrices);

  bindTexture2DCached(host, 0, null);
  resetTextureBindCache(host);
  batch.skinningEnabled = true;
  batch.boneCount = count;
}

function isHighScaleCommandRef(ref) {
  return !!ref && ref.kind === 'highScaleLayer';
}

export function setRetainedDrawOrderBytes(hostId, count, orderBytes) {
  const host = hosts.get(hostId);
  if (!host) return;
  const safeCount = Math.max(0, count | 0);
  if (safeCount === 0) { host.retainedDrawOrder = []; return; }
  const bytes = toUint8Array(orderBytes);
  const available = Math.min(safeCount, Math.floor(bytes.byteLength / 12));
  if (available <= 0) { host.retainedDrawOrder = []; return; }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const refs = new Array(available);
  let used = 0;
  for (let i = 0; i < available; i++) {
    const offset = i * 12;
    const lo = view.getUint32(offset + 0, true);
    const hi = view.getUint32(offset + 4, true);
    const flags = view.getUint32(offset + 8, true);
    const transparent = (flags & 1) !== 0;
    const highScale = (flags & 2) !== 0;
    const key = handleKey(lo, hi);
    const source = highScale ? host.highScaleLayerHandleRefs.get(key) : host.retainedHandleRefs.get(key);
    if (!source) continue;
    if (highScale) refs[used++] = { id: source.id, kind: 'highScaleLayer', layerId: source.layerId, transparent };
    else refs[used++] = { id: source.id, batchIndex: source.batchIndex, transparent };
  }
  refs.length = used;
  host.retainedDrawOrder = refs;
}

function controlTextureKey(controlId) {
  return 'control-texture:' + controlId;
}

export function setRetainedControlPlanesDirect(hostId, controlPlaneIds, count, planeBytes) {
  const host = hosts.get(hostId);
  if (!host) return;
  const safeCount = Math.max(0, count | 0);
  if (safeCount === 0) {
    host.controlPlanes = [];
    host.controlPlaneDrawList = [];
    return;
  }
  const ids = controlPlaneIds ? String(controlPlaneIds).split('\n') : [];
  const records = decodeFloat32Payload(planeBytes);
  const planes = new Array(safeCount);
  for (let i = 0; i < safeCount; i++) {
    const id = ids[i] || ('control:' + i);
    const offset = i * 20;
    const source = new Float32Array(20);
    if (records.length >= offset + 20) source.set(records.subarray(offset, offset + 20));
    planes[i] = {
      id,
      textureId: controlTextureKey(id),
      source,
      alwaysFaceCamera: (source[0] || 0) > 0.5,
      vertices: new Float32Array(20),
      verticesDirty: true,
      averageDepth: 0
    };
  }
  host.controlPlanes = planes;
  host.controlPlaneDrawList = planes.slice();
}

function ensureRetainedDirectPacket(host) {
  let p = host.retainedDirectFramePacket;
  if (!p || !p.__direct) {
    p = {
      __direct: true,
      width: 1,
      height: 1,
      clearColor: new Float32Array(4),
      viewProjection: new Float32Array(16),
      cameraPosition: new Float32Array(3),
      cameraRight: new Float32Array(3),
      cameraUp: new Float32Array(3),
      cameraForward: new Float32Array(3),
      ambientLight: new Float32Array(3),
      directionalLightDirection: new Float32Array(3),
      directionalLightColor: new Float32Array(3),
      pointLightPosition: new Float32Array(4),
      pointLightColor: new Float32Array(4),
      spotLightPosition: new Float32Array(4),
      spotLightDirection: new Float32Array(4),
      spotLightColor: new Float32Array(4),
      spotLightCone: new Float32Array(4),
      skyboxTopColor: new Float32Array(3),
      skyboxHorizonColor: new Float32Array(3),
      skyboxBottomColor: new Float32Array(3),
      toneMappingParams: new Float32Array(4),
      ssaoParams: new Float32Array(4),
      batches: host.emptyBatches || [],
      retainedBatches: host.retainedDrawOrder || [],
      controlPlanes: host.controlPlanes || [],
      liveMeshIds: host.emptyBatches || [],
      liveTextureIds: host.emptyBatches || []
    };
    host.retainedDirectFramePacket = p;
  }
  return p;
}

function set3(dest, src, offset, x, y, z) {
  dest[0] = src.length > offset ? (src[offset] || 0) : x;
  dest[1] = src.length > offset + 1 ? (src[offset + 1] || 0) : y;
  dest[2] = src.length > offset + 2 ? (src[offset + 2] || 0) : z;
}

function set4(dest, src, offset, x, y, z, w) {
  dest[0] = src.length > offset ? (src[offset] || 0) : x;
  dest[1] = src.length > offset + 1 ? (src[offset + 1] || 0) : y;
  dest[2] = src.length > offset + 2 ? (src[offset + 2] || 0) : z;
  dest[3] = src.length > offset + 3 ? (src[offset + 3] || 0) : w;
}

export function renderRetainedSceneFrameDirect(
  hostId,
  width,
  height,
  flags,
  skyboxMode,
  toneMappingMode,
  skyboxTextureId,
  skyboxCubemapIds,
  viewProjectionBytes,
  cameraBytes,
  lightingBytes,
  styleBytes) {
  const host = hosts.get(hostId);
  if (!host || host.contextLost) return;
  const view = requireExactFloatPayload('retained view-projection', viewProjectionBytes, 16);
  const camera = requireExactFloatPayload('retained camera', cameraBytes, 12);
  const lighting = requireExactFloatPayload('retained lighting', lightingBytes, 33);
  const style = requireExactFloatPayload('retained style', styleBytes, 30);
  const packet = ensureRetainedDirectPacket(host);
  packet.width = width || host.width || 1;
  packet.height = height || host.height || 1;
  packet.viewProjection.set(view.length >= 16 ? view.subarray(0, 16) : view);
  set4(packet.clearColor, style, 0, 0, 0, 0, 1); if (packet.clearColor[3] === 0) packet.clearColor[3] = 1;
  set3(packet.cameraPosition, camera, 0, 0, 0, 6);
  set3(packet.cameraRight, camera, 3, 1, 0, 0);
  set3(packet.cameraUp, camera, 6, 0, 1, 0);
  set3(packet.cameraForward, camera, 9, 0, 0, -1);
  set3(packet.ambientLight, lighting, 0, 0, 0, 0);
  set3(packet.directionalLightDirection, lighting, 3, -0.35, -0.75, -0.55);
  set3(packet.directionalLightColor, lighting, 6, 0, 0, 0);
  set4(packet.pointLightPosition, lighting, 9, 0, 0, 0, 1);
  set4(packet.pointLightColor, lighting, 13, 0, 0, 0, 0);
  set4(packet.spotLightPosition, lighting, 17, 0, 0, 0, 1);
  set4(packet.spotLightDirection, lighting, 21, 0, -1, 0, 0);
  set4(packet.spotLightColor, lighting, 25, 0, 0, 0, 0);
  set4(packet.spotLightCone, lighting, 29, 0.95, 0.85, 1, 0);
  packet.skyboxEnabled = (flags & 1) !== 0;
  packet.skyboxMode = skyboxMode || 0;
  set3(packet.skyboxTopColor, style, 4, 0.28, 0.45, 0.72);
  set3(packet.skyboxHorizonColor, style, 7, 0.62, 0.76, 0.94);
  set3(packet.skyboxBottomColor, style, 10, 0.82, 0.86, 0.9);
  packet.skyboxIntensity = Number.isFinite(style[13]) ? Math.max(0, style[13]) : 1;
  packet.skyboxProjectionScaleX = style[22] > 0 ? style[22] : 0;
  packet.skyboxProjectionScaleY = style[23] > 0 ? style[23] : 0;
  packet.skyboxTextureId = skyboxTextureId || null;
  const cubeKey = skyboxCubemapIds || '';
  if (host.retainedCubemapIdsKey !== cubeKey) {
    host.retainedCubemapIdsKey = cubeKey;
    host.retainedCubemapIds = cubeKey ? String(cubeKey).split('\n') : [];
  }
  packet.skyboxCubemapTextureIds = host.retainedCubemapIds;
  packet.ssaoEnabled = (flags & 2) !== 0;
  packet.hdrEnabled = (flags & 4) !== 0;
  packet.showWireframeOverlay = (flags & 8) !== 0;
  packet.showSilhouetteOverlay = (flags & 16) !== 0;
  packet.toneMappingMode = finiteOr(toneMappingMode, 0);
  packet.toneMappingParams[0] = finiteOr(style[14], 1); packet.toneMappingParams[1] = finiteOr(style[15], 2.2); packet.toneMappingParams[2] = 0; packet.toneMappingParams[3] = 0;
  packet.ssaoParams[0] = finiteOr(style[16], 0); packet.ssaoParams[1] = finiteOr(style[17], 0.75); packet.ssaoParams[2] = finiteOr(style[18], 0.025); packet.ssaoParams[3] = finiteOr(style[19], 16);
  packet.retainedBatches = host.retainedDrawOrder || packet.retainedBatches;
  packet.controlPlanes = host.controlPlanes || packet.controlPlanes;
  renderScenePacket(host, packet, false);
}

function nextBufferCapacity(byteCount) {
  let capacity = 256;
  const required = Math.max(0, byteCount | 0);
  while (capacity < required) capacity <<= 1;
  return capacity;
}

export function uploadRetainedParticleBatchBytes(hostId, batchId, meshId, lightingEnabled, cubeMode, instanceCount, particleFloatCount, transparent, particleBytes) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const batch = getOrCreateRetainedBatch(host, batchId);
  batch.meshId = meshId;
  batch.meshIndex = host.meshIdToIndex.has(meshId) ? host.meshIdToIndex.get(meshId) : -1;
  batch.lightingEnabled = lightingEnabled || 0;
  batch.instanceCount = instanceCount || 0;
  batch.transparent = !!transparent;
  batch.usePalette = false;
  batch.particleMode = cubeMode ? 2 : 1;
  batch.particleStride = cubeMode ? 20 : 8;
  const data = decodeFloat32Payload(particleBytes);
  const floatCount = Math.max(0, particleFloatCount | 0);
  const expectedFloatCount = Math.max(0, batch.instanceCount | 0) * batch.particleStride;
  if (floatCount !== expectedFloatCount || data.length !== expectedFloatCount) {
    throw new Error(`Retained particle batch '${batchId}' has ${data.length}/${floatCount} payload floats; exactly ${expectedFloatCount} are required.`);
  }
  if (expectedFloatCount === 0) return;
  if (!batch.particleBuffer) batch.particleBuffer = createRequiredBuffer(gl, `retained:${batchId}:particles`);
  bindArrayBufferCached(host, batch.particleBuffer);
  const activeBytes = data.byteLength;
  if (!batch.particleBufferCapacityBytes || batch.particleBufferCapacityBytes < activeBytes) {
    batch.particleBufferCapacityBytes = nextBufferCapacity(activeBytes);
    trackedBufferData(host, gl.ARRAY_BUFFER, batch.particleBufferCapacityBytes, gl.DYNAMIC_DRAW);
  }
  gl.bufferSubData(gl.ARRAY_BUFFER, 0, data);
  bindArrayBufferCached(host, null);
}

export function consumeContextResetFlag(hostId) {
  const host = hosts.get(hostId);
  if (!host) return false;
  const pending = !!host.contextResetPending;
  host.contextResetPending = false;
  return pending;
}

function applyFramePacket(packet, frame) {
  packet.width = frame.width;
  packet.height = frame.height;
  packet.clearColor = frame.clearColor;
  packet.viewProjection = frame.viewProjection;
  packet.cameraPosition = frame.cameraPosition;
  packet.cameraRight = frame.cameraRight;
  packet.cameraUp = frame.cameraUp;
  packet.cameraForward = frame.cameraForward;
  packet.ambientLight = frame.ambientLight;
  packet.directionalLightDirection = frame.directionalLightDirection;
  packet.directionalLightColor = frame.directionalLightColor;
  packet.pointLightPosition = frame.pointLightPosition;
  packet.pointLightColor = frame.pointLightColor;
  packet.spotLightPosition = frame.spotLightPosition;
  packet.spotLightDirection = frame.spotLightDirection;
  packet.spotLightColor = frame.spotLightColor;
  packet.spotLightCone = frame.spotLightCone;
  packet.skyboxEnabled = frame.skyboxEnabled;
  packet.skyboxMode = frame.skyboxMode;
  packet.skyboxTopColor = frame.skyboxTopColor;
  packet.skyboxHorizonColor = frame.skyboxHorizonColor;
  packet.skyboxBottomColor = frame.skyboxBottomColor;
  packet.skyboxIntensity = frame.skyboxIntensity;
  packet.skyboxTextureId = frame.skyboxTextureId;
  packet.skyboxCubemapTextureIds = frame.skyboxCubemapTextureIds;
  packet.renderPipelineMode = frame.renderPipelineMode;
  packet.deferredRequested = frame.deferredRequested;
  packet.ssaoEnabled = frame.ssaoEnabled;
  packet.ssaoParams = frame.ssaoParams;
  packet.hdrEnabled = frame.hdrEnabled;
  packet.toneMappingMode = frame.toneMappingMode;
  packet.toneMappingParams = frame.toneMappingParams;
  packet.motionVectorMetadataEnabled = frame.motionVectorMetadataEnabled;
  packet.showWireframeOverlay = frame.showWireframeOverlay;
  packet.showSilhouetteOverlay = frame.showSilhouetteOverlay;
}

function renderScenePacket(host, packet, cleanupLiveResources) {
  resetWebGlFrameCounters(host);
  const { gl } = host;
  const batches = packet.batches || [];
  const retainedRefs = packet.retainedBatches || [];
  host.showWireframeOverlay = !!packet.showWireframeOverlay;
  host.showSilhouetteOverlay = !!packet.showSilhouetteOverlay;
  const viewProj = copyToScratch(host.frameViewProjection, packet.viewProjection, 16);
  if (cleanupLiveResources) {
    const liveMeshIds = Array.isArray(packet.liveMeshIds) ? new Set(packet.liveMeshIds) : new Set(batches.map(batch => batch.id));
    if (!Array.isArray(packet.liveMeshIds)) {
      for (const ref of retainedRefs) { const rb = host.retainedBatches.get(ref.id); if (rb && rb.meshId) liveMeshIds.add(rb.meshId); }
    }
    let removedMeshResource = false;
    for (const [id, resource] of Array.from(host.meshResources.entries())) {
      if (!liveMeshIds.has(id)) {
        disposeMeshResource(gl, resource);
        host.meshResources.delete(id);
        const index = host.meshResourceList.indexOf(resource);
        if (index >= 0) host.meshResourceList.splice(index, 1);
        removedMeshResource = true;
      }
    }
    if (removedMeshResource) rebuildMeshIndex(host);
    const liveControlIds = new Set(packet.controlPlanes.map(plane => plane.id));
    const liveTextureIds = Array.isArray(packet.liveTextureIds) ? new Set(packet.liveTextureIds) : new Set(packet.controlPlanes.map(plane => plane.textureId));
    for (const [id, resource] of host.controlVertexBuffers.entries()) {
      if (!liveControlIds.has(id)) {
        gl.deleteVertexArray(resource.vao);
        gl.deleteBuffer(resource.buffer);
        host.controlVertexBuffers.delete(id);
      }
    }
    for (const [id, texture] of host.textureResources.entries()) if (!liveTextureIds.has(id)) { gl.deleteTexture(texture.texture); host.textureResources.delete(id); }
  }


  gl.viewport(0, 0, host.width || 1, host.height || 1);
  setDepthTestCached(host, true);
  setDepthFuncCached(host, gl.LESS);
  setDepthMaskCached(host, true);
  setBlendCached(host, false);
  gl.clearColor(packet.clearColor[0], packet.clearColor[1], packet.clearColor[2], packet.clearColor[3]);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  drawSkybox(host, packet);

  setBlendCached(host, false);
  useProgramCached(host, host.meshProgram);
  uniform3fvFromArray(host, host.meshAmbientLightLocation, host.scratch3, packet.ambientLight, [0.28, 0.28, 0.28]);
  uniform3fvFromArray(host, host.meshDirectionalLightDirectionLocation, host.scratch3, packet.directionalLightDirection, [-0.35, -0.75, -0.55]);
  uniform3fvFromArray(host, host.meshDirectionalLightColorLocation, host.scratch3, packet.directionalLightColor, [0, 0, 0]);
  uniform4fvFromArray(host, host.meshPointLightPositionLocation, host.scratch4, packet.pointLightPosition, [0, 0, 0, 1]);
  uniform4fvFromArray(host, host.meshPointLightColorLocation, host.scratch4, packet.pointLightColor, [0, 0, 0, 0]);
  uniform4fvFromArray(host, host.meshSpotLightPositionLocation, host.scratch4, packet.spotLightPosition, [0, 0, 0, 1]);
  uniform4fvFromArray(host, host.meshSpotLightDirectionLocation, host.scratch4, packet.spotLightDirection, [0, -1, 0, 0]);
  uniform4fvFromArray(host, host.meshSpotLightColorLocation, host.scratch4, packet.spotLightColor, [0, 0, 0, 0]);
  uniform4fvFromArray(host, host.meshSpotLightConeLocation, host.scratch4, packet.spotLightCone, [0.95, 0.85, 1, 0]);
  uniform3fvFromArray(host, host.meshCameraPositionLocation, host.scratch3, packet.cameraPosition, [0, 0, 6]);
  if (host.meshPostProcessParamsLocation !== null) {
    const tone = packet.toneMappingParams || [1.0, 2.2, 0.0, 0.0];
    const mode = packet.toneMappingMode || 0;
    uniform4fCached(host, host.meshPostProcessParamsLocation, finiteOr(tone[0], 1.0), finiteOr(tone[1], 2.2), packet.hdrEnabled ? 1.0 : 0.0, mode);
  }
  if (host.meshSsaoParamsLocation !== null) {
    const ssao = packet.ssaoParams || [0.0, 0.75, 0.025, 16.0];
    uniform4fCached(host, host.meshSsaoParamsLocation, packet.ssaoEnabled ? 1.0 : 0.0, finiteOr(ssao[0], 0.0), finiteOr(ssao[1], 0.75), finiteOr(ssao[2], 0.025));
  }
  uniformMatrix4fvCached(host, host.meshViewProjLocation, viewProj);
  if (host.meshCameraRightUniformLocation !== null) uniform3fvFromArray(host, host.meshCameraRightUniformLocation, host.scratch3, packet.cameraRight, [1, 0, 0]);
  if (host.meshCameraUpUniformLocation !== null) uniform3fvFromArray(host, host.meshCameraUpUniformLocation, host.scratch3, packet.cameraUp, [0, 1, 0]);
  uniform1fCached(host, host.meshParticleModeLocation, 0);
  setClientAnimationUniforms(host, false, 0, 0);

  const hasHighScale = host.highScaleFramePacket && host.highScaleLayers && host.highScaleLayers.size > 0;
  if (hasHighScale) beginHighScaleCommandFrame(host);

  const legacyBatchCount = Array.isArray(batches) ? batches.length : 0;
  if (legacyBatchCount > 0) throw new Error(`Rejected ${legacyBatchCount} legacy per-object draw batches; the RHI requires retained GPU submissions.`);
  for (const ref of retainedRefs) if (!ref || !ref.transparent) drawRetainedCommandRef(host, ref, packet, viewProj);
  for (const ref of retainedRefs) if (ref && ref.transparent) drawRetainedCommandRef(host, ref, packet, viewProj);
  if (hasHighScale) publishHighScaleCommandMetrics(host);
  drawControlPlanes(host, packet, viewProj);

  bindVertexArrayCached(host, null);
  bindArrayBufferCached(host, null);
  bindElementArrayBufferCached(host, null);
  bindTexture2DCached(host, 0, null);
  resetTextureBindCache(host);
  useProgramCached(host, null);
}

function beginHighScaleCommandFrame(host) {
  host.frameId = (host.frameId || 0) + 1;
  host.animationUploadBytes = 0;
  host.animationUploadBatches = 0;
  const metrics = host.highScaleCommandMetricsScratch || (host.highScaleCommandMetricsScratch = new Float64Array(20));
  metrics.fill(0);
  host.highScaleCommandMetricsUsed = false;
}

function accumulateHighScaleCommandMetrics(host, metrics) {
  if (!metrics) return;
  const aggregate = host.highScaleCommandMetricsScratch || (host.highScaleCommandMetricsScratch = new Float64Array(20));
  for (let i = 0; i <= 14; i++) aggregate[i] += metrics[i] || 0;
  aggregate[15] = Math.max(aggregate[15] || 0, metrics[15] || 0);
  aggregate[18] = metrics[18] || 0;
  aggregate[19] = metrics[19] || 0;
  host.highScaleCommandMetricsUsed = true;
}

function publishHighScaleCommandMetrics(host) {
  if (!host.highScaleCommandMetricsUsed) return;
  const metrics = host.highScaleCommandMetricsScratch;
  metrics[16] = host.animationUploadBatches || 0;
  metrics[17] = host.animationUploadBytes || 0;
  metrics[18] = host.texturePayloadErrors || 0;
  metrics[19] = host.palettePayloadErrors || 0;
  host.lastHighScaleMetrics = metrics;
}

function drawRetainedCommandRef(host, ref, packet, viewProj) {
  if (!ref) return;
  if (isHighScaleCommandRef(ref)) {
    if (host.highScaleFramePacket && host.highScaleLayers && host.highScaleLayers.size > 0) {
      const metrics = drawHighScaleRuntime(host, host.highScaleFramePacket, false, 0, false, ref.layerId, false);
      accumulateHighScaleCommandMetrics(host, metrics);
      restoreMeshGlobalsAfterHighScale(host, packet, viewProj);
    }
    return;
  }

  if ((ref.batchIndex | 0) >= 0) {
    drawRetainedBatchByIndex(host, ref.batchIndex | 0);
  } else {
    drawRetainedBatch(host, ref.id || '');
  }
}

function restoreMeshGlobalsAfterHighScale(host, packet, viewProj) {
  const { gl } = host;
  useProgramCached(host, host.meshProgram);
  uniformMatrix4fvCached(host, host.meshViewProjLocation, viewProj);
  uniform3fvFromArray(host, host.meshCameraPositionLocation, host.scratch3, packet.cameraPosition, [0, 0, 6]);
  if (host.meshCameraRightUniformLocation !== null) uniform3fvFromArray(host, host.meshCameraRightUniformLocation, host.scratch3, packet.cameraRight, [1, 0, 0]);
  if (host.meshCameraUpUniformLocation !== null) uniform3fvFromArray(host, host.meshCameraUpUniformLocation, host.scratch3, packet.cameraUp, [0, 1, 0]);
  uniform1fCached(host, host.meshParticleModeLocation, 0);
  setClientAnimationUniforms(host, false, 0, 0);
}



function useProgramCached(host, program) {
  const { gl } = host;
  if (!host.glState) return gl.useProgram(program);
  if (host.glState.program !== program) {
    gl.useProgram(program);
    host.glState.program = program;
    host.glState.stateChanges++;
  }
}

function bindArrayBufferCached(host, buffer) {
  const { gl } = host;
  if (!host.glState) return gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  if (host.glState.arrayBuffer !== buffer) {
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    host.glState.arrayBuffer = buffer;
    host.glState.bufferBinds++;
  }
}

function bindElementArrayBufferCached(host, buffer) {
  const { gl } = host;
  if (!host.glState) return gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, buffer);
  if (host.glState.elementArrayBuffer !== buffer) {
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, buffer);
    host.glState.elementArrayBuffer = buffer;
    host.glState.bufferBinds++;
  }
}

function setBlendCached(host, enabled) {
  const { gl } = host;
  if (!host.glState || host.glState.blend !== enabled) {
    if (enabled) gl.enable(gl.BLEND); else gl.disable(gl.BLEND);
    if (host.glState) { host.glState.blend = enabled; host.glState.stateChanges++; }
  }
}

function setDepthTestCached(host, enabled) {
  const { gl } = host;
  if (!host.glState || host.glState.depthTest !== enabled) {
    if (enabled) gl.enable(gl.DEPTH_TEST); else gl.disable(gl.DEPTH_TEST);
    if (host.glState) { host.glState.depthTest = enabled; host.glState.stateChanges++; }
  }
}

function setDepthMaskCached(host, enabled) {
  const { gl } = host;
  if (!host.glState || host.glState.depthMask !== enabled) {
    gl.depthMask(!!enabled);
    if (host.glState) { host.glState.depthMask = !!enabled; host.glState.stateChanges++; }
  }
}

function setCullModeCached(host, mode) {
  const { gl } = host;
  const resolved = Number(mode || 0) | 0;
  if (resolved < 0 || resolved > 2) throw new Error(`Unsupported CullMode ${resolved}.`);
  if (!host.glState || host.glState.cullMode !== resolved) {
    if (resolved === 0) {
      gl.disable(gl.CULL_FACE);
    } else {
      gl.enable(gl.CULL_FACE);
      const face = resolved === 1 ? gl.BACK : gl.FRONT;
      if (!host.glState || host.glState.cullFace !== face) {
        gl.cullFace(face);
        if (host.glState) host.glState.cullFace = face;
      }
    }
    if (host.glState) { host.glState.cullMode = resolved; host.glState.stateChanges++; }
  }
}

function setBlendFuncCached(host, src, dst) {
  const { gl } = host;
  if (!host.glState || host.glState.blendSrc !== src || host.glState.blendDst !== dst) {
    gl.blendFunc(src, dst);
    if (host.glState) { host.glState.blendSrc = src; host.glState.blendDst = dst; host.glState.stateChanges++; }
  }
}

function setDepthFuncCached(host, func) {
  const { gl } = host;
  if (!host.glState || host.glState.depthFunc !== func) {
    gl.depthFunc(func);
    if (host.glState) { host.glState.depthFunc = func; host.glState.stateChanges++; }
  }
}

function uniform1fCached(host, location, value) {
  if (location === null || location === undefined) return;
  const { gl } = host;
  const key = location;
  const cache = getUniformCache(gl);
  const last = cache.get(key);
  const v = Number(value || 0);
  if (last === v) return;
  gl.uniform1f(location, v);
  cache.set(key, v);
  if (host.glState) host.glState.uniformUpdates++;
}

function uniform1iCached(host, location, value) {
  if (location === null || location === undefined) return;
  const { gl } = host;
  const key = location;
  const cache = getUniformCache(gl);
  const v = value | 0;
  const last = cache.get(key);
  if (last === v) return;
  gl.uniform1i(location, v);
  cache.set(key, v);
  if (host.glState) host.glState.uniformUpdates++;
}

function uniform2fCached(host, location, x, y) {
  if (location === null || location === undefined) return;
  const { gl } = host;
  const cache = getUniformCache(gl);
  const last = cache.get(location);
  const a = Number(x || 0), b = Number(y || 0);
  if (last && last.length === 2 && last[0] === a && last[1] === b) return;
  gl.uniform2f(location, a, b);
  if (last) { last[0] = a; last[1] = b; } else cache.set(location, [a, b]);
  if (host.glState) host.glState.uniformUpdates++;
}

function uniform4fCached(host, location, x, y, z, w) {
  if (location === null || location === undefined) return;
  const { gl } = host;
  const cache = getUniformCache(gl);
  const last = cache.get(location);
  const a = Number(x || 0), b = Number(y || 0), c = Number(z || 0), d = Number(w || 0);
  if (last && last.length === 4 && last[0] === a && last[1] === b && last[2] === c && last[3] === d) return;
  gl.uniform4f(location, a, b, c, d);
  if (last) { last[0] = a; last[1] = b; last[2] = c; last[3] = d; } else cache.set(location, [a, b, c, d]);
  if (host.glState) host.glState.uniformUpdates++;
}

function uniformMatrix4fvCached(host, location, value) {
  if (location === null || location === undefined) return;
  const { gl } = host;
  const cache = getUniformCache(gl);
  let last = cache.get(location);
  let same = !!last && last.length === 16;
  if (same) {
    for (let i = 0; i < 16; i++) { if (last[i] !== value[i]) { same = false; break; } }
  }
  if (same) return;
  gl.uniformMatrix4fv(location, false, value);
  if (!last || last.length !== 16) { last = new Float32Array(16); cache.set(location, last); }
  last.set(value);
  if (host.glState) host.glState.uniformUpdates++;
}

function bindVertexArrayCached(host, vao) {
  const { gl } = host;
  if (!host.glState) {
    gl.bindVertexArray(vao);
    return;
  }
  if (host.glState.vao === vao) return;
  gl.bindVertexArray(vao);
  host.glState.vao = vao;
  host.glState.arrayBuffer = null;
  host.glState.elementArrayBuffer = null;
  host.glState.vaoBinds++;
}

function resetGlStateCache(host) {
  if (!host || !host.glState) return;
  host.glState.activeTextureUnit = -1;
  host.glState.texture2D.fill(null);
  host.glState.program = null;
  host.glState.arrayBuffer = null;
  host.glState.elementArrayBuffer = null;
  host.glState.vao = null;
  host.glState.blend = null;
  host.glState.depthTest = null;
  host.glState.depthMask = null;
  host.glState.blendSrc = null;
  host.glState.blendDst = null;
  host.glState.depthFunc = null;
  host.glState.materialKey = '';
}

function resetWebGlFrameCounters(host) {
  if (!host || !host.glState) return;
  host.glState.stateChanges = 0;
  host.glState.uniformUpdates = 0;
  host.glState.textureBinds = 0;
  host.glState.bufferBinds = 0;
  host.glState.vaoBinds = 0;
  host.glState.legacyDrawPathCalls = 0;
  host.glState.legacyDrawPathBlockedCalls = 0;
  host.glState.legacyStringProtocolCalls = 0;
  host.glState.bufferDataCalls = 0;
  host.glState.dynamicBufferDataCalls = 0;
  host.retainedOrdinaryCulledInstances = 0;
}

function trackedBufferData(host, target, dataOrSize, usage) {
  if (host && host.glState) {
    host.glState.bufferDataCalls++;
    if (usage === host.gl.DYNAMIC_DRAW) host.glState.dynamicBufferDataCalls++;
  }
  host.gl.bufferData(target, dataOrSize, usage);
}

function createMeshVaoObject(host) {
  const { gl } = host;
  const vao = gl.createVertexArray();
  if (!vao) throw new Error('WebGL 2 failed to allocate a required vertex array.');
  return vao;
}

function bindMeshVaoRaw(host, vao) {
  host.gl.bindVertexArray(vao);
}

function rebuildMeshVao(host, resource) {
  const { gl } = host;
  if (resource.vao) {
    gl.deleteVertexArray(resource.vao);
    resource.vao = null;
  }
  resource.vao = createMeshVaoObject(host);
  bindMeshVaoRaw(host, resource.vao);
  resetGlStateCache(host);
  configureMeshGeometryAttributes(host, resource);
  bindMeshVaoRaw(host, null);
  resetGlStateCache(host);
}

function bindMeshGeometry(host, resource) {
  if (!resource || !resource.vao) throw new Error('A WebGL 2 mesh draw was submitted without a required vertex array.');
  bindVertexArrayCached(host, resource.vao);
}

function configureMeshGeometryAttributes(host, resource) {
  const { gl } = host;
  bindArrayBufferCached(host, resource.normalBuffer);
  gl.enableVertexAttribArray(host.meshNormalLocation);
  gl.vertexAttribPointer(host.meshNormalLocation, 3, gl.FLOAT, false, 0, 0);
  bindArrayBufferCached(host, resource.vertexBuffer);
  gl.enableVertexAttribArray(host.meshPositionLocation);
  gl.vertexAttribPointer(host.meshPositionLocation, 3, gl.FLOAT, false, 0, 0);
  if (host.meshTexCoordLocation >= 0) {
    if (resource.hasTexCoords && resource.texCoordBuffer) {
      bindArrayBufferCached(host, resource.texCoordBuffer);
      gl.enableVertexAttribArray(host.meshTexCoordLocation);
      gl.vertexAttribPointer(host.meshTexCoordLocation, 2, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(host.meshTexCoordLocation);
      gl.vertexAttrib2f(host.meshTexCoordLocation, 0, 0);
    }
  }
  if (host.meshTangentLocation >= 0) {
    if (resource.hasTangents && resource.tangentBuffer) {
      bindArrayBufferCached(host, resource.tangentBuffer);
      gl.enableVertexAttribArray(host.meshTangentLocation);
      gl.vertexAttribPointer(host.meshTangentLocation, 4, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(host.meshTangentLocation);
      gl.vertexAttrib4f(host.meshTangentLocation, 1, 0, 0, 1);
    }
  }
  if (host.meshVertexColorLocation >= 0) {
    if (resource.hasColors && resource.colorBuffer) {
      bindArrayBufferCached(host, resource.colorBuffer);
      gl.enableVertexAttribArray(host.meshVertexColorLocation);
      gl.vertexAttribPointer(host.meshVertexColorLocation, 4, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(host.meshVertexColorLocation);
      gl.vertexAttrib4f(host.meshVertexColorLocation, 1, 1, 1, 1);
    }
  }
  if (host.meshMaterialSlotLocation >= 0) {
    if (resource.hasMaterialSlots && resource.materialSlotBuffer) {
      bindArrayBufferCached(host, resource.materialSlotBuffer);
      gl.enableVertexAttribArray(host.meshMaterialSlotLocation);
      gl.vertexAttribPointer(host.meshMaterialSlotLocation, 1, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(host.meshMaterialSlotLocation);
      gl.vertexAttrib1f(host.meshMaterialSlotLocation, 0);
    }
  }
  if (host.meshBoneIndicesLocation >= 0) {
    if (resource.hasSkinWeights && resource.boneIndexBuffer) {
      bindArrayBufferCached(host, resource.boneIndexBuffer);
      gl.enableVertexAttribArray(host.meshBoneIndicesLocation);
      gl.vertexAttribPointer(host.meshBoneIndicesLocation, 4, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(host.meshBoneIndicesLocation);
      gl.vertexAttrib4f(host.meshBoneIndicesLocation, 0, 0, 0, 0);
    }
  }
  if (host.meshBoneWeightsLocation >= 0) {
    if (resource.hasSkinWeights && resource.boneWeightBuffer) {
      bindArrayBufferCached(host, resource.boneWeightBuffer);
      gl.enableVertexAttribArray(host.meshBoneWeightsLocation);
      gl.vertexAttribPointer(host.meshBoneWeightsLocation, 4, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(host.meshBoneWeightsLocation);
      gl.vertexAttrib4f(host.meshBoneWeightsLocation, 0, 0, 0, 0);
    }
  }
  bindElementArrayBufferCached(host, resource.indexBuffer);
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
    bindArrayBufferCached(host, batch.transformBuffer);
    gl.bufferSubData(gl.ARRAY_BUFFER, 0, batch.baseTransformData);
    bindArrayBufferCached(host, null);
    batch.animationFrameId = host.frameId;
    batch.animationActive = false;
    host.animationUploadBatches = (host.animationUploadBatches || 0) + 1;
    host.animationUploadBytes = (host.animationUploadBytes || 0) + batch.baseTransformData.byteLength;
    return batch.baseTransformData.byteLength;
  }

  return 0;
}

function worldAabbFromInstanceTransform(transformData, offset, localCenter, localExtents, outCenter, outExtents) {
  const cx = localCenter[0] || 0, cy = localCenter[1] || 0, cz = localCenter[2] || 0;
  const ex = localExtents[0] || 0, ey = localExtents[1] || 0, ez = localExtents[2] || 0;
  const m11 = transformData[offset],      m12 = transformData[offset + 1],  m13 = transformData[offset + 2];
  const m21 = transformData[offset + 4],  m22 = transformData[offset + 5],  m23 = transformData[offset + 6];
  const m31 = transformData[offset + 8],  m32 = transformData[offset + 9],  m33 = transformData[offset + 10];
  const m41 = transformData[offset + 12], m42 = transformData[offset + 13], m43 = transformData[offset + 14];
  outCenter[0] = m11 * cx + m21 * cy + m31 * cz + m41;
  outCenter[1] = m12 * cx + m22 * cy + m32 * cz + m42;
  outCenter[2] = m13 * cx + m23 * cy + m33 * cz + m43;
  outExtents[0] = Math.abs(m11) * ex + Math.abs(m21) * ey + Math.abs(m31) * ez;
  outExtents[1] = Math.abs(m12) * ex + Math.abs(m22) * ey + Math.abs(m32) * ez;
  outExtents[2] = Math.abs(m13) * ex + Math.abs(m23) * ey + Math.abs(m33) * ez;
}

function ensureFloatCapacity(current, required) {
  if (current && current.length >= required) return current;
  let next = 16;
  while (next < required) next <<= 1;
  return new Float32Array(next);
}

function prepareVisibleRetainedBatch(host, batch, resource) {
  if (!batch || batch.particleMode || (batch.batchId && String(batch.batchId).startsWith('hs:')) || !resource || !resource.localBoundsValid || !batch.baseTransformData || batch.instanceCount <= 0) {
    return { count: batch ? batch.instanceCount || 0 : 0, transformBuffer: batch ? batch.transformBuffer : null, stateBuffer: batch ? batch.stateBuffer : null };
  }
  const transforms = batch.baseTransformData;
  const states = batch.baseStateData;
  const count = Math.min(batch.instanceCount | 0, Math.floor(transforms.length / 16));
  if (count <= 0) return { count: 0, transformBuffer: batch.transformBuffer, stateBuffer: batch.stateBuffer };
  if (count < retainedOrdinaryCullMinInstances) {
    return { count, transformBuffer: batch.transformBuffer, stateBuffer: batch.stateBuffer };
  }
  const viewProj = host.frameViewProjection;
  const center = host.retainedCullCenter || (host.retainedCullCenter = [0, 0, 0]);
  const extents = host.retainedCullExtents || (host.retainedCullExtents = [0, 0, 0]);
  let visible = 0;
  let compacted = false;
  for (let i = 0; i < count; i++) {
    worldAabbFromInstanceTransform(transforms, i * 16, resource.localCenter, resource.localExtents, center, extents);
    if (!aabbIntersectsFrustum(viewProj, center, extents)) continue;
    if (visible !== i) {
      batch.visibleTransformData = ensureFloatCapacity(batch.visibleTransformData, (visible + 1) * 16);
      if (!compacted && visible > 0) {
        batch.visibleTransformData.set(transforms.subarray(0, visible * 16), 0);
        if (states && states.length >= visible * 4) {
          batch.visibleStateData = ensureFloatCapacity(batch.visibleStateData, visible * 4);
          batch.visibleStateData.set(states.subarray(0, visible * 4), 0);
        }
      }
      compacted = true;
      batch.visibleTransformData.set(transforms.subarray(i * 16, i * 16 + 16), visible * 16);
      if (states && states.length >= (i + 1) * 4) {
        batch.visibleStateData = ensureFloatCapacity(batch.visibleStateData, (visible + 1) * 4);
        batch.visibleStateData.set(states.subarray(i * 4, i * 4 + 4), visible * 4);
      }
    }
    visible++;
  }
  if (visible === count) {
    return { count, transformBuffer: batch.transformBuffer, stateBuffer: batch.stateBuffer };
  }
  const culled = count - visible;
  if (count < retainedOrdinaryCullMinInstances || culled / Math.max(1, count) < retainedOrdinaryCullMinCulledRatio) {
    host.retainedOrdinaryCullBypassInstances = (host.retainedOrdinaryCullBypassInstances || 0) + culled;
    return { count, transformBuffer: batch.transformBuffer, stateBuffer: batch.stateBuffer };
  }
  if (visible <= 0) {
    host.retainedOrdinaryCulledInstances = (host.retainedOrdinaryCulledInstances || 0) + count;
    return { count: 0, transformBuffer: batch.transformBuffer, stateBuffer: batch.stateBuffer };
  }
  const gl = host.gl;
  const createdTransformBuffer = !batch.culledTransformBuffer;
  if (createdTransformBuffer) batch.culledTransformBuffer = createRequiredBuffer(gl, `retained:${batch.batchId}:culled-transform`);
  if (!batch.culledStateBuffer) {
    try {
      batch.culledStateBuffer = createRequiredBuffer(gl, `retained:${batch.batchId}:culled-state`);
    } catch (error) {
      if (createdTransformBuffer) {
        gl.deleteBuffer(batch.culledTransformBuffer);
        batch.culledTransformBuffer = null;
      }
      throw error;
    }
  }
  if (batch.visibleTransformData.length < visible * 16) batch.visibleTransformData = ensureFloatCapacity(batch.visibleTransformData, visible * 16);
  if (states && states.length >= count * 4 && batch.visibleStateData.length < visible * 4) batch.visibleStateData = ensureFloatCapacity(batch.visibleStateData, visible * 4);
  const transformFloats = visible * 16;
  const stateFloats = visible * 4;
  bindArrayBufferCached(host, batch.culledTransformBuffer);
  if ((batch.culledTransformCapacityFloats || 0) < transformFloats) {
    trackedBufferData(host, gl.ARRAY_BUFFER, transformFloats * 4, gl.DYNAMIC_DRAW);
    batch.culledTransformCapacityFloats = transformFloats;
  }
  gl.bufferSubData(gl.ARRAY_BUFFER, 0, batch.visibleTransformData.subarray(0, transformFloats));
  bindArrayBufferCached(host, batch.culledStateBuffer);
  if ((batch.culledStateCapacityFloats || 0) < stateFloats) {
    trackedBufferData(host, gl.ARRAY_BUFFER, stateFloats * 4, gl.DYNAMIC_DRAW);
    batch.culledStateCapacityFloats = stateFloats;
  }
  gl.bufferSubData(gl.ARRAY_BUFFER, 0, batch.visibleStateData.subarray(0, stateFloats));
  bindArrayBufferCached(host, null);
  host.retainedOrdinaryCulledInstances = (host.retainedOrdinaryCulledInstances || 0) + (count - visible);
  return { count: visible, transformBuffer: batch.culledTransformBuffer, stateBuffer: batch.culledStateBuffer };
}

function drawRetainedBatch(host, batchId) {
  const batch = host.retainedBatches.get(batchId);
  if (!batch) return;
  drawRetainedBatchObject(host, batch);
}

function drawRetainedBatchObject(host, batch) {
  const { gl } = host;
  if (!batch || batch.instanceCount <= 0) return;
  const resource = batch.meshIndex >= 0 ? host.meshResourceList[batch.meshIndex] : host.meshResources.get(batch.meshId);
  if (!resource || resource.indexCount === 0) return;
  const visible = prepareVisibleRetainedBatch(host, batch, resource);
  if (visible.count <= 0) return;
  setCullModeCached(host, batch.cullMode || 0);
  bindMeshGeometry(host, resource);
  if (batch.transparent && !host.forceAlphaDitherOpaque) {
    setBlendCached(host, true);
    setBlendFuncCached(host, gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    setDepthMaskCached(host, false);
  } else {
    setBlendCached(host, false);
    setDepthMaskCached(host, true);
  }
  uniform1fCached(host, host.meshLightingEnabledLocation, batch.lightingEnabled || 0);
  uniform1fCached(host, host.meshNormalMapStrengthLocation, batch.normalMapStrength || 0);
  uniform1fCached(host, host.meshUsePaletteLocation, 0);
  bindMaterialTextures(host, batch);
  uniform4fCached(host, host.meshMaterialParamsLocation, finiteOr(batch.metallic, 0), finiteOr(batch.roughness, 1), 0, 0);
  uniform4fCached(host, host.meshAlphaParamsLocation, finiteOr(batch.alphaCutoff, 0), (batch.transparent && !host.forceAlphaDitherOpaque) ? 1 : 0, 0, 0);
  const em = batch.emissiveColor || [0, 0, 0, 0];
  uniform4fCached(host, host.meshEmissiveColorLocation, em[0] || 0, em[1] || 0, em[2] || 0, em[3] || 0);
  uniform1fCached(host, host.meshUseInstancingLocation, 1);
  uniform1fCached(host, host.meshUsePaletteLocation, batch.usePalette ? 1 : 0);
  if (host.meshSkinningEnabledLocation !== null) {
    if (resource.hasSkinWeights && !batch.skinningEnabled) {
      throw new Error(`Retained batch '${batch.batchId}' contains skinned geometry but no active GPU bone-matrix texture.`);
    }
    if (batch.skinningEnabled && (!batch.boneTexture || !resource.hasSkinWeights)) {
      throw new Error(`Retained batch '${batch.batchId}' requested GPU skinning without a complete bone texture and skin-weight geometry.`);
    }
    const skinActive = !!batch.skinningEnabled;
    uniform1fCached(host, host.meshSkinningEnabledLocation, skinActive ? 1 : 0);
    if (skinActive) {
      bindTexture2DCached(host, 6, batch.boneTexture);
      uniform1iCached(host, host.meshBoneTextureLocation, 6);
      uniform1fCached(host, host.meshBoneTextureHeightLocation, batch.boneCount || 1);
      activateTextureUnitCached(host, 0);
    }
  }
  if (batch.usePalette && batch.paletteTexture) {
    bindTexture2DCached(host, 1, batch.paletteTexture);
    uniform1iCached(host, host.meshPaletteLocation, 1);
    uniform2fCached(host, host.meshPaletteSizeLocation, batch.paletteWidth || 1, batch.paletteHeight || 1);
    activateTextureUnitCached(host, 0);
  }
  if (batch.particleMode && batch.particleBuffer) {
    uniform1fCached(host, host.meshParticleModeLocation, batch.particleMode);
    bindArrayBufferCached(host, batch.particleBuffer);
    if (batch.particleMode > 1.5) {
      setInstanceAttributeWithStride(host, host.meshInstanceModel0Location, 4, 0, 80);
      setInstanceAttributeWithStride(host, host.meshInstanceModel1Location, 4, 16, 80);
      setInstanceAttributeWithStride(host, host.meshInstanceModel2Location, 4, 32, 80);
      setInstanceAttributeWithStride(host, host.meshInstanceModel3Location, 4, 48, 80);
      setInstanceAttributeWithStride(host, host.meshInstanceColorLocation, 4, 64, 80);
    } else {
      setInstanceAttributeWithStride(host, host.meshInstanceModel0Location, 4, 0, 32);
      setInstanceAttributeWithStride(host, host.meshInstanceColorLocation, 4, 16, 32);
      if (host.meshInstanceModel1Location >= 0) { gl.disableVertexAttribArray(host.meshInstanceModel1Location); gl.vertexAttrib4f(host.meshInstanceModel1Location, 0, 0, 0, 0); }
      if (host.meshInstanceModel2Location >= 0) { gl.disableVertexAttribArray(host.meshInstanceModel2Location); gl.vertexAttrib4f(host.meshInstanceModel2Location, 0, 0, 0, 0); }
      if (host.meshInstanceModel3Location >= 0) { gl.disableVertexAttribArray(host.meshInstanceModel3Location); gl.vertexAttrib4f(host.meshInstanceModel3Location, 0, 0, 0, 1); }
    }
  } else {
    uniform1fCached(host, host.meshParticleModeLocation, 0);
    bindArrayBufferCached(host, visible.transformBuffer);
    setInstanceAttributeWithStride(host, host.meshInstanceModel0Location, 4, 0, 64);
    setInstanceAttributeWithStride(host, host.meshInstanceModel1Location, 4, 16, 64);
    setInstanceAttributeWithStride(host, host.meshInstanceModel2Location, 4, 32, 64);
    setInstanceAttributeWithStride(host, host.meshInstanceModel3Location, 4, 48, 64);
    bindArrayBufferCached(host, visible.stateBuffer);
    setInstanceAttributeWithStride(host, host.meshInstanceColorLocation, 4, 0, 16);
  }
  gl.drawElementsInstanced(gl.TRIANGLES, resource.indexCount, resource.indexType, 0, visible.count);
  uniform1fCached(host, host.meshParticleModeLocation, 0);
  uniform1fCached(host, host.meshSkinningEnabledLocation, 0);
  drawWireframeOverlayForCurrentBatch(host, resource, visible.count);
  resetInstanceDivisors(host);
  setDepthMaskCached(host, true);
  uniform1fCached(host, host.meshUsePaletteLocation, 0);
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

function aabbIntersectsFrustum(viewProj, c, e) {
  // Conservative plane-vs-AABB test. The old corner-inside test culled large
  // chunks whenever the frustum crossed the box without containing a corner,
  // which made high-scale racks disappear at specific camera angles.
  const m = viewProj;
  const planes = [
    [m[3] + m[0],  m[7] + m[4],  m[11] + m[8],  m[15] + m[12]], // left
    [m[3] - m[0],  m[7] - m[4],  m[11] - m[8],  m[15] - m[12]], // right
    [m[3] + m[1],  m[7] + m[5],  m[11] + m[9],  m[15] + m[13]], // bottom
    [m[3] - m[1],  m[7] - m[5],  m[11] - m[9],  m[15] - m[13]], // top
    [m[3] - m[2],  m[7] - m[6],  m[11] - m[10], m[15] - m[14]]  // far
  ];

  for (let i = 0; i < planes.length; i++) {
    const p = planes[i];
    const r = Math.abs(p[0]) * e[0] + Math.abs(p[1]) * e[1] + Math.abs(p[2]) * e[2];
    const distance = p[0] * c[0] + p[1] * c[1] + p[2] * c[2] + p[3];
    if (distance + r < 0) return false;
  }

  return true;
}


const retainedOrdinaryCullMinInstances = 32;
const retainedOrdinaryCullMinCulledRatio = 0.15;

const highScaleSnapshotTextDecoder = typeof TextDecoder !== 'undefined' ? new TextDecoder('utf-8') : null;

function readSnapshotInt32(view, bytes, cursor) {
  if (cursor.offset + 4 > bytes.byteLength) { cursor.failed = true; return 0; }
  const value = view.getInt32(cursor.offset, true);
  cursor.offset += 4;
  return value;
}

function readSnapshotFloat32(view, bytes, cursor) {
  if (cursor.offset + 4 > bytes.byteLength) { cursor.failed = true; return 0; }
  const value = view.getFloat32(cursor.offset, true);
  cursor.offset += 4;
  return value;
}

function readSnapshotString(view, bytes, cursor) {
  const length = readSnapshotInt32(view, bytes, cursor);
  if (cursor.failed) return '';
  if (length < 0 || cursor.offset + length > bytes.byteLength) {
    cursor.failed = true;
    cursor.offset = bytes.byteLength;
    return '';
  }
  if (length === 0) return '';
  const end = cursor.offset + length;
  const slice = bytes.subarray(cursor.offset, end);
  cursor.offset = end;
  if (highScaleSnapshotTextDecoder) return highScaleSnapshotTextDecoder.decode(slice);
  let text = '';
  for (let i = 0; i < slice.length; i++) text += String.fromCharCode(slice[i]);
  try { return decodeURIComponent(escape(text)); } catch (_) { return text; }
}

function parseHighScaleSnapshotBytes(host, payload) {
  try {
    const bytes = typeof payload === 'string' ? decodeBase64Bytes(payload) : toUint8Array(payload);
    if (bytes.byteLength < 44) return null;
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const cursor = { offset: 0, failed: false };
    const magic = readSnapshotInt32(view, bytes, cursor);
    const version = readSnapshotInt32(view, bytes, cursor);
    if (cursor.failed || magic !== 0x314C5348 || version !== 1) return null;
    const layerId = readSnapshotString(view, bytes, cursor);
    const structuralVersion = readSnapshotInt32(view, bytes, cursor);
    const visible = readSnapshotInt32(view, bytes, cursor) !== 0;
    const detailedDistance = readSnapshotFloat32(view, bytes, cursor);
    const simplifiedDistance = readSnapshotFloat32(view, bytes, cursor);
    const proxyDistance = readSnapshotFloat32(view, bytes, cursor);
    const drawDistance = readSnapshotFloat32(view, bytes, cursor);
    const enableBillboardFallback = readSnapshotInt32(view, bytes, cursor) !== 0;
    const chunkCount = Math.max(0, readSnapshotInt32(view, bytes, cursor));
    if (cursor.failed) return null;
    const layer = {
      id: layerId,
      version: structuralVersion,
      visible,
      detailedDistance: detailedDistance || 24,
      simplifiedDistance: simplifiedDistance || 96,
      proxyDistance: proxyDistance || 320,
      drawDistance: drawDistance || 5000,
      enableBillboardFallback,
      chunks: []
    };

    for (let ci = 0; ci < chunkCount; ci++) {
      const id = readSnapshotString(view, bytes, cursor);
      const cx = readSnapshotFloat32(view, bytes, cursor);
      const cy = readSnapshotFloat32(view, bytes, cursor);
      const cz = readSnapshotFloat32(view, bytes, cursor);
      const ex = readSnapshotFloat32(view, bytes, cursor);
      const ey = readSnapshotFloat32(view, bytes, cursor);
      const ez = readSnapshotFloat32(view, bytes, cursor);
      const instanceCount = readSnapshotInt32(view, bytes, cursor);
      if (cursor.failed) return null;
      const chunk = {
        id,
        center: [cx || 0, cy || 0, cz || 0],
        extents: [ex || 0, ey || 0, ez || 0],
        instanceCount: instanceCount || 0,
        batchesByLod: [[], [], [], []]
      };
      for (let lod = 0; lod < 4; lod++) {
        const count = Math.max(0, readSnapshotInt32(view, bytes, cursor));
        if (cursor.failed) return null;
        for (let i = 0; i < count; i++) {
          const batchId = readSnapshotString(view, bytes, cursor);
          if (cursor.failed) return null;
          const idx = host.retainedBatchIdToIndex.get(batchId);
          if (idx !== undefined) chunk.batchesByLod[lod].push(idx);
        }
      }
      layer.chunks.push(chunk);
    }
    return cursor.failed || cursor.offset !== bytes.byteLength ? null : layer;
  } catch (_) {
    return null;
  }
}

export function uploadHighScaleLayerSnapshotBytes(hostId, layerId, snapshotBytes) {
  const host = hosts.get(hostId);
  if (!host) return;
  const layer = parseHighScaleSnapshotBytes(host, snapshotBytes);
  if (!layer) {
    throw new Error(`High-scale layer '${layerId}' has a malformed binary snapshot; the previous GPU state was preserved and the frame is rejected.`);
  }
  host.highScaleLayers.set(layerId || layer.id, layer);
  registerHighScaleLayerHandle(host, layer);
}


export function destroyHighScaleLayer(hostId, layerId) {
  const host = hosts.get(hostId);
  if (!host) return;
  const key = handleKeyFromString(layerId);
  if (key) host.highScaleLayerHandleRefs.delete(key);
  host.highScaleLayers.delete(layerId);
}

function ensureHighScaleDirectPacket(host) {
  let p = host.highScaleFramePacket;
  if (!p || !p.__direct) {
    p = {
      __direct: true,
      viewProjection: new Float32Array(16),
      clearColor: new Float32Array(4),
      cameraPosition: new Float32Array(3),
      cameraRight: new Float32Array(3),
      cameraUp: new Float32Array(3),
      cameraForward: new Float32Array(3),
      ambientLight: new Float32Array(3),
      directionalLightDirection: new Float32Array(3),
      directionalLightColor: new Float32Array(3),
      pointLightPosition: new Float32Array(4),
      pointLightColor: new Float32Array(4),
      spotLightPosition: new Float32Array(4),
      spotLightDirection: new Float32Array(4),
      spotLightColor: new Float32Array(4),
      spotLightCone: new Float32Array(4),
      skyboxTopColor: new Float32Array(3),
      skyboxHorizonColor: new Float32Array(3),
      skyboxBottomColor: new Float32Array(3),
      toneMappingParams: new Float32Array(4),
      ssaoParams: new Float32Array(4)
    };
    host.highScaleFramePacket = p;
  }
  return p;
}

function copy3(dest, src, offset) { dest[0] = src[offset] || 0; dest[1] = src[offset + 1] || 0; dest[2] = src[offset + 2] || 0; }
function copy4(dest, src, offset) { dest[0] = src[offset] || 0; dest[1] = src[offset + 1] || 0; dest[2] = src[offset + 2] || 0; dest[3] = src[offset + 3] || 0; }

export function syncHighScaleFrameDirect(hostId, width, height, flags, skyboxMode, viewProjectionBytes, cameraBytes, lightingBytes, styleBytes) {
  const host = hosts.get(hostId);
  if (!host) return;
  const view = requireExactFloatPayload('high-scale view-projection', viewProjectionBytes, 16);
  const camera = requireExactFloatPayload('high-scale camera', cameraBytes, 12);
  const lighting = requireExactFloatPayload('high-scale lighting', lightingBytes, 33);
  const style = requireExactFloatPayload('high-scale style', styleBytes, 24);
  const p = ensureHighScaleDirectPacket(host);
  p.width = width || host.width || 1;
  p.height = height || host.height || 1;
  p.viewProjection.set(view.length >= 16 ? view.subarray(0, 16) : view);
  copy4(p.clearColor, style, 0); if (p.clearColor[3] === 0) p.clearColor[3] = 1;
  copy3(p.cameraPosition, camera, 0);
  copy3(p.cameraRight, camera, 3); if (p.cameraRight[0] === 0 && p.cameraRight[1] === 0 && p.cameraRight[2] === 0) p.cameraRight[0] = 1;
  copy3(p.cameraUp, camera, 6); if (p.cameraUp[0] === 0 && p.cameraUp[1] === 0 && p.cameraUp[2] === 0) p.cameraUp[1] = 1;
  copy3(p.cameraForward, camera, 9);
  copy3(p.ambientLight, lighting, 0);
  copy3(p.directionalLightDirection, lighting, 3);
  copy3(p.directionalLightColor, lighting, 6);
  copy4(p.pointLightPosition, lighting, 9);
  copy4(p.pointLightColor, lighting, 13);
  copy4(p.spotLightPosition, lighting, 17);
  copy4(p.spotLightDirection, lighting, 21);
  copy4(p.spotLightColor, lighting, 25);
  copy4(p.spotLightCone, lighting, 29);
  p.skyboxEnabled = (flags & 1) !== 0;
  p.skyboxMode = skyboxMode || 0;
  copy3(p.skyboxTopColor, style, 4);
  copy3(p.skyboxHorizonColor, style, 7);
  copy3(p.skyboxBottomColor, style, 10);
  p.skyboxIntensity = Number.isFinite(style[13]) ? Math.max(0, style[13]) : 1;
  p.toneMappingParams[0] = finiteOr(style[14], 1); p.toneMappingParams[1] = finiteOr(style[15], 2.2); p.toneMappingParams[2] = 0; p.toneMappingParams[3] = 0;
  p.ssaoParams[0] = finiteOr(style[16], 0); p.ssaoParams[1] = finiteOr(style[17], 0.75); p.ssaoParams[2] = finiteOr(style[18], 0.025); p.ssaoParams[3] = finiteOr(style[19], 16);
  p.clientAnimationEnabled = (flags & 2) !== 0;
  p.clientAnimationTime = style[20] || 0;
  p.clientAnimationAmplitude = style[21] || 0;
  p.skyboxProjectionScaleX = style[22] > 0 ? style[22] : 0;
  p.skyboxProjectionScaleY = style[23] > 0 ? style[23] : 0;
  p.ssaoEnabled = (flags & 8) !== 0;
  p.hdrEnabled = (flags & 16) !== 0;
}

export function getLastHighScaleMetric(hostId, index) {
  const host = hosts.get(hostId);
  const metrics = host && host.lastHighScaleMetrics;
  if (!metrics) return 0;
  const i = index | 0;
  return i >= 0 && i < metrics.length ? metrics[i] || 0 : 0;
}

function drawHighScaleRuntime(host, packet, clearFrame, passMode = 0, recordMetrics = true, layerFilterId = null, advanceFrame = true) {
  if (!host || !packet) return '';
  const { gl } = host;
  setCullModeCached(host, 0);
  if (advanceFrame) {
    host.frameId = (host.frameId || 0) + 1;
    host.animationUploadBytes = 0;
    host.animationUploadBatches = 0;
  }
  const t0 = performance.now();
  const viewProj = host.frameViewProjection;
  const viewProjSource = packet.viewProjection || [];
  for (let i = 0; i < 16; i++) viewProj[i] = viewProjSource[i] || 0;
  const camera = packet.cameraPosition || [0, 0, 0];
  if (clearFrame) {
    gl.viewport(0, 0, host.width || 1, host.height || 1);
    setDepthTestCached(host, true);
    setDepthFuncCached(host, gl.LESS);
    setDepthMaskCached(host, true);
    setBlendCached(host, false);
    const clear = packet.clearColor || [0, 0, 0, 1];
    gl.clearColor(clear[0], clear[1], clear[2], clear[3]);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    drawSkybox(host, packet);
  }
  setBlendCached(host, false);
  useProgramCached(host, host.meshProgram);
  uniform3fvFromArray(host, host.meshAmbientLightLocation, host.scratch3, packet.ambientLight, [0.28, 0.28, 0.28]);
  uniform3fvFromArray(host, host.meshDirectionalLightDirectionLocation, host.scratch3, packet.directionalLightDirection, [-0.35, -0.75, -0.55]);
  uniform3fvFromArray(host, host.meshDirectionalLightColorLocation, host.scratch3, packet.directionalLightColor, [0, 0, 0]);
  uniform4fvFromArray(host, host.meshPointLightPositionLocation, host.scratch4, packet.pointLightPosition, [0, 0, 0, 1]);
  uniform4fvFromArray(host, host.meshPointLightColorLocation, host.scratch4, packet.pointLightColor, [0, 0, 0, 0]);
  uniform4fvFromArray(host, host.meshSpotLightPositionLocation, host.scratch4, packet.spotLightPosition, [0, 0, 0, 1]);
  uniform4fvFromArray(host, host.meshSpotLightDirectionLocation, host.scratch4, packet.spotLightDirection, [0, -1, 0, 0]);
  uniform4fvFromArray(host, host.meshSpotLightColorLocation, host.scratch4, packet.spotLightColor, [0, 0, 0, 0]);
  uniform4fvFromArray(host, host.meshSpotLightConeLocation, host.scratch4, packet.spotLightCone, [0.95, 0.85, 1, 0]);
  uniform3fvFromArray(host, host.meshCameraPositionLocation, host.scratch3, packet.cameraPosition, [0, 0, 6]);
  if (host.meshCameraRightUniformLocation !== null) uniform3fvFromArray(host, host.meshCameraRightUniformLocation, host.scratch3, packet.cameraRight, [1, 0, 0]);
  if (host.meshCameraUpUniformLocation !== null) uniform3fvFromArray(host, host.meshCameraUpUniformLocation, host.scratch3, packet.cameraUp, [0, 1, 0]);
  if (host.meshPostProcessParamsLocation !== null) {
    const tone = packet.toneMappingParams || [1.0, 2.2, 0.0, 0.0];
    const mode = packet.toneMappingMode || 0;
    uniform4fCached(host, host.meshPostProcessParamsLocation, finiteOr(tone[0], 1.0), finiteOr(tone[1], 2.2), packet.hdrEnabled ? 1.0 : 0.0, mode);
  }
  if (host.meshSsaoParamsLocation !== null) {
    const ssao = packet.ssaoParams || [0.0, 0.75, 0.025, 16.0];
    uniform4fCached(host, host.meshSsaoParamsLocation, packet.ssaoEnabled ? 1.0 : 0.0, finiteOr(ssao[0], 0.0), finiteOr(ssao[1], 0.75), finiteOr(ssao[2], 0.025));
  }
  uniformMatrix4fvCached(host, host.meshViewProjLocation, viewProj);
  uniform1fCached(host, host.meshParticleModeLocation, 0);
  const clientAnimationEnabled = !!packet.clientAnimationEnabled;
  const clientAnimationTime = Number(packet.clientAnimationTime || 0);
  const clientAnimationAmplitude = Number(packet.clientAnimationAmplitude || 0);
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
  const layerFilter = layerFilterId === null || layerFilterId === undefined ? null : String(layerFilterId);
  for (const layer of host.highScaleLayers.values()) {
    if (layerFilter !== null && layer.id !== layerFilter) continue;
    if (!layer.visible) continue;
    totalChunks += layer.chunks.length;
    for (const chunk of layer.chunks) {
      if (!aabbIntersectsFrustum(viewProj, chunk.center, chunk.extents)) { culled += chunk.instanceCount; continue; }
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
  const previousForceAlphaDitherOpaque = !!host.forceAlphaDitherOpaque;
  host.forceAlphaDitherOpaque = true;
  try {
    for (let i = 0; i < drawBatchCount; i++) {
      const batchIndex = drawBatchIndices[i] | 0;
      const batch = host.retainedBatchList[batchIndex];
      if (!batch) continue;
      const resource = batch.meshIndex >= 0 ? host.meshResourceList[batch.meshIndex] : host.meshResources.get(batch.meshId);
      if (!resource) continue;
      if (passMode === 1 && batch.transparent) continue;
      if (passMode === 2 && !batch.transparent) continue;
      prepareRetainedBatchTransformForFrame(host, batch, clientAnimationEnabled, clientAnimationTime, clientAnimationAmplitude);
      drawRetainedBatchByIndex(host, batchIndex);
      drawCalls++;
      batches++;
      partInstances += batch.instanceCount || 0;
      triangles += ((resource.indexCount || 0) / 3) * (batch.instanceCount || 0);
    }
  } finally {
    host.forceAlphaDitherOpaque = previousForceAlphaDitherOpaque;
  }
  const tDraw1 = performance.now();
  if (clearFrame) {
    bindArrayBufferCached(host, null);
    bindElementArrayBufferCached(host, null);
    bindTexture2DCached(host, 0, null);
    resetTextureBindCache(host);
    useProgramCached(host, null);
  }
  const metrics = host.highScaleMetricsScratch || (host.highScaleMetricsScratch = new Float64Array(20));
  metrics[0] = visibleChunks; metrics[1] = totalChunks; metrics[2] = culled; metrics[3] = lodD; metrics[4] = lodS; metrics[5] = lodP; metrics[6] = lodB; metrics[7] = lodC;
  metrics[8] = drawCalls; metrics[9] = batches; metrics[10] = Math.round(triangles); metrics[11] = partInstances;
  metrics[12] = tCull1 - tCull0; metrics[13] = tDraw1 - tDraw0; metrics[14] = performance.now() - t0; metrics[15] = 2;
  metrics[16] = host.animationUploadBatches || 0; metrics[17] = host.animationUploadBytes || 0; metrics[18] = host.texturePayloadErrors || 0; metrics[19] = host.palettePayloadErrors || 0;
  if (recordMetrics) host.lastHighScaleMetrics = metrics;
  return metrics;
}

function setClientAnimationUniforms(host, enabled, time, amplitude) {
  uniform1fCached(host, host.meshClientAnimationEnabledLocation, enabled ? 1 : 0);
  uniform1fCached(host, host.meshClientAnimationTimeLocation, time || 0);
  uniform1fCached(host, host.meshClientAnimationAmplitudeLocation, enabled ? Math.max(0, amplitude || 0) : 0);
}

function activateTextureUnitCached(host, textureUnitIndex) {
  const { gl } = host;
  if (!host.glState) host.glState = { activeTextureUnit: -1, texture2D: new Array(16).fill(null) };
  if (host.glState.activeTextureUnit !== textureUnitIndex) {
    gl.activeTexture(gl.TEXTURE0 + textureUnitIndex);
    host.glState.activeTextureUnit = textureUnitIndex;
  }
}

function bindTexture2DCached(host, textureUnitIndex, texture) {
  const { gl } = host;
  activateTextureUnitCached(host, textureUnitIndex);
  if (host.glState.texture2D[textureUnitIndex] !== texture) {
    gl.bindTexture(gl.TEXTURE_2D, texture);
    host.glState.texture2D[textureUnitIndex] = texture;
    host.glState.textureBinds++;
  }
}

function resetTextureBindCache(host) {
  if (!host || !host.glState) return;
  host.glState.activeTextureUnit = -1;
  host.glState.texture2D.fill(null);
  host.glState.materialKey = '';
}

function bindTextureSlot(host, textureId, samplerLocation, enabledLocation, textureUnit, textureUnitIndex) {
  const { gl } = host;
  if (enabledLocation === null || samplerLocation === null || !textureId) {
    uniform1fCached(host, enabledLocation, 0);
    return;
  }
  const textureResource = host.textureResources.get(textureId);
  if (!textureResource) {
    uniform1fCached(host, enabledLocation, 0);
    return;
  }
  bindTexture2DCached(host, textureUnitIndex, textureResource.texture);
  uniform1iCached(host, samplerLocation, textureUnitIndex);
  uniform1fCached(host, enabledLocation, 1);
  activateTextureUnitCached(host, 0);
}

function ensureMaterialTextureKey(batch) {
  const base = batch.baseColorTextureId || '';
  const normal = batch.normalTextureId || '';
  const mr = batch.metallicRoughnessTextureId || '';
  const emissive = batch.emissiveTextureId || '';
  if (batch._materialTextureKeyBase !== base ||
      batch._materialTextureKeyNormal !== normal ||
      batch._materialTextureKeyMr !== mr ||
      batch._materialTextureKeyEmissive !== emissive) {
    batch._materialTextureKeyBase = base;
    batch._materialTextureKeyNormal = normal;
    batch._materialTextureKeyMr = mr;
    batch._materialTextureKeyEmissive = emissive;
    batch.materialTextureKey = base + '|' + normal + '|' + mr + '|' + emissive;
  }
  return batch.materialTextureKey || '';
}

function bindMaterialTextures(host, batch) {
  const key = batch.materialTextureKey === undefined ? ensureMaterialTextureKey(batch) : batch.materialTextureKey;
  if (host.glState && host.glState.materialKey === key) return;
  bindTextureSlot(host, batch.baseColorTextureId || null, host.meshBaseColorTextureLocation, host.meshBaseColorTextureEnabledLocation, null, 2);
  bindTextureSlot(host, batch.normalTextureId || null, host.meshNormalTextureLocation, host.meshNormalTextureEnabledLocation, null, 3);
  bindTextureSlot(host, batch.metallicRoughnessTextureId || null, host.meshMetallicRoughnessTextureLocation, host.meshMetallicRoughnessTextureEnabledLocation, null, 4);
  bindTextureSlot(host, batch.emissiveTextureId || null, host.meshEmissiveTextureLocation, host.meshEmissiveTextureEnabledLocation, null, 5);
  if (host.glState) host.glState.materialKey = key;
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
    bindTexture2DCached(host, i, res.texture);
    uniform1iCached(host, locations[i], i);
  }
  uniform1fCached(host, host.skyboxCubemapEnabledLocation, complete ? 1 : 0);
  activateTextureUnitCached(host, 0);
}

function drawWireframeOverlayForCurrentBatch(host, resource, instanceCount) {
  if (!host.showWireframeOverlay || !resource || resource.wireframeIndexCount <= 0) return;
  const { gl } = host;
  uniform1fCached(host, host.meshLightingEnabledLocation, 0);
  uniform1fCached(host, host.meshNormalMapStrengthLocation, 0);
  bindElementArrayBufferCached(host, resource.wireframeIndexBuffer);
  gl.drawElementsInstanced(gl.LINES, resource.wireframeIndexCount, resource.wireframeIndexType || gl.UNSIGNED_SHORT, 0, instanceCount);
  bindElementArrayBufferCached(host, resource.indexBuffer);
}

function setInstanceAttribute(host, location, size, offset) {
  setInstanceAttributeWithStride(host, location, size, offset, 80);
}

function setInstanceAttributeWithStride(host, location, size, offset, stride) {
  if (location < 0) return;
  const { gl } = host;
  gl.enableVertexAttribArray(location);
  gl.vertexAttribPointer(location, size, gl.FLOAT, false, stride, offset);
  gl.vertexAttribDivisor(location, 1);
}

function resetInstanceDivisors(host) {
  const { gl } = host;
  for (const location of [host.meshInstanceModel0Location, host.meshInstanceModel1Location, host.meshInstanceModel2Location, host.meshInstanceModel3Location, host.meshInstanceColorLocation]) {
    if (location >= 0) gl.vertexAttribDivisor(location, 0);
  }
}

function compareControlPlaneDepth(a, b) { return (b.averageDepth || 0) - (a.averageDepth || 0); }

function writeControlVertex(out, index, x, y, z, u, v) {
  const o = index * 5;
  out[o + 0] = x; out[o + 1] = y; out[o + 2] = z; out[o + 3] = u; out[o + 4] = v;
}

function normalize3Into(out, x, y, z, fx, fy, fz) {
  const len = Math.hypot(x, y, z);
  if (len <= 0.000001) { out[0] = fx; out[1] = fy; out[2] = fz; return out; }
  const inv = 1 / len;
  out[0] = x * inv; out[1] = y * inv; out[2] = z * inv;
  return out;
}

function updateControlPlaneForFrame(host, plane, packet, viewProj) {
  const src = plane.source;
  const out = plane.vertices;
  const billboard = (src[0] || 0) > 0.5;
  if (billboard) {
    const cx = src[1] || 0, cy = src[2] || 0, cz = src[3] || 0;
    const ex = Math.max(src[4] || 0, 0.0001);
    const ey = Math.max(src[5] || 0, 0.0001);
    const roll = src[6] || 0;
    const cp = packet.cameraPosition || [0, 0, 0];
    const cup = packet.cameraUp || [0, 1, 0];
    const scratchA = host.scratch3;
    const scratchB = host.controlScratch3 || (host.controlScratch3 = new Float32Array(3));
    const scratchC = host.controlScratch3b || (host.controlScratch3b = new Float32Array(3));
    normalize3Into(scratchA, (cp[0] || 0) - cx, (cp[1] || 0) - cy, (cp[2] || 0) - cz, 0, 0, 1);
    normalize3Into(scratchB, cup[0] || 0, cup[1] || 1, cup[2] || 0, 0, 1, 0);
    // right = normalize(cross(up, frontToCamera))
    normalize3Into(
      scratchC,
      scratchB[1] * scratchA[2] - scratchB[2] * scratchA[1],
      scratchB[2] * scratchA[0] - scratchB[0] * scratchA[2],
      scratchB[0] * scratchA[1] - scratchB[1] * scratchA[0],
      1, 0, 0);
    // up = normalize(cross(frontToCamera, right))
    normalize3Into(
      scratchB,
      scratchA[1] * scratchC[2] - scratchA[2] * scratchC[1],
      scratchA[2] * scratchC[0] - scratchA[0] * scratchC[2],
      scratchA[0] * scratchC[1] - scratchA[1] * scratchC[0],
      0, 1, 0);
    if (Math.abs(roll) > 0.0001) {
      const cos = Math.cos(roll), sin = Math.sin(roll);
      const rx = scratchC[0] * cos + scratchB[0] * sin;
      const ry = scratchC[1] * cos + scratchB[1] * sin;
      const rz = scratchC[2] * cos + scratchB[2] * sin;
      const ux = -scratchC[0] * sin + scratchB[0] * cos;
      const uy = -scratchC[1] * sin + scratchB[1] * cos;
      const uz = -scratchC[2] * sin + scratchB[2] * cos;
      scratchC[0] = rx; scratchC[1] = ry; scratchC[2] = rz;
      scratchB[0] = ux; scratchB[1] = uy; scratchB[2] = uz;
    }
    const rx = scratchC[0] * ex, ry = scratchC[1] * ex, rz = scratchC[2] * ex;
    const ux = scratchB[0] * ey, uy = scratchB[1] * ey, uz = scratchB[2] * ey;
    writeControlVertex(out, 0, cx - rx + ux, cy - ry + uy, cz - rz + uz, 0, 0);
    writeControlVertex(out, 1, cx + rx + ux, cy + ry + uy, cz + rz + uz, 1, 0);
    writeControlVertex(out, 2, cx + rx - ux, cy + ry - uy, cz + rz - uz, 1, 1);
    writeControlVertex(out, 3, cx - rx - ux, cy - ry - uy, cz - rz - uz, 0, 1);
  } else {
    writeControlVertex(out, 0, src[8] || 0, src[9] || 0, src[10] || 0, 0, 0);
    writeControlVertex(out, 1, src[11] || 0, src[12] || 0, src[13] || 0, 1, 0);
    writeControlVertex(out, 2, src[14] || 0, src[15] || 0, src[16] || 0, 1, 1);
    writeControlVertex(out, 3, src[17] || 0, src[18] || 0, src[19] || 0, 0, 1);
  }
  let depth = 0;
  for (let i = 0; i < 4; i++) {
    const o = i * 5;
    const x = out[o], y = out[o + 1], z = out[o + 2];
    const clipZ = x * viewProj[2] + y * viewProj[6] + z * viewProj[10] + viewProj[14];
    const clipW = x * viewProj[3] + y * viewProj[7] + z * viewProj[11] + viewProj[15];
    depth += Math.abs(clipW) > 0.000001 ? clipZ / clipW : clipZ;
  }
  plane.averageDepth = depth * 0.25;
}

function drawControlPlanes(host, packet, viewProj) {
  const { gl } = host;
  const planes = packet.controlPlanes || host.controlPlanes || [];
  if (!planes || planes.length === 0) return;
  setCullModeCached(host, 0);
  const drawList = host.controlPlaneDrawList || planes;
  for (let i = 0; i < planes.length; i++) {
    updateControlPlaneForFrame(host, planes[i], packet, viewProj);
    drawList[i] = planes[i];
  }
  drawList.length = planes.length;
  drawList.sort(compareControlPlaneDepth);
  setBlendCached(host, true);
  setBlendFuncCached(host, gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  setDepthMaskCached(host, false);
  useProgramCached(host, host.texturedProgram);
  uniformMatrix4fvCached(host, host.texturedViewProjLocation, viewProj);
  activateTextureUnitCached(host, 0);
  uniform1iCached(host, host.texturedSamplerLocation, 0);
  for (let i = 0; i < drawList.length; i++) {
    const plane = drawList[i];
    const textureResource = host.textureResources.get(plane.textureId);
    if (!textureResource) continue;
    const vertexResource = getOrCreateControlResource(host, plane.id);
    bindVertexArrayCached(host, vertexResource.vao);
    bindArrayBufferCached(host, vertexResource.buffer);
    if (!vertexResource.initialized) {
      trackedBufferData(host, gl.ARRAY_BUFFER, plane.vertices.byteLength, gl.DYNAMIC_DRAW);
      vertexResource.initialized = true;
      plane.verticesDirty = true;
    }
    if (plane.alwaysFaceCamera || plane.verticesDirty) {
      gl.bufferSubData(gl.ARRAY_BUFFER, 0, plane.vertices);
      plane.verticesDirty = false;
    }
    bindTexture2DCached(host, 0, textureResource.texture);
    gl.drawElements(gl.TRIANGLES, 6, gl.UNSIGNED_SHORT, 0);
  }
  setDepthMaskCached(host, true);
  setBlendCached(host, false);
}

export function getWebGlStateMetric(hostId, index) {
  const host = hosts.get(hostId);
  const s = host && host.glState;
  if (!s) return 0;
  switch (index | 0) {
    case 0: return s.stateChanges || 0;
    case 1: return s.uniformUpdates || 0;
    case 2: return s.textureBinds || 0;
    case 3: return s.bufferBinds || 0;
    case 4: return s.vaoBinds || 0;
    case 5: return s.legacyDrawPathCalls || 0;
    case 6: return s.legacyDrawPathBlockedCalls || 0;
    case 7: return s.legacyStringProtocolCalls || 0;
    case 8: return s.bufferDataCalls || 0;
    case 9: return s.dynamicBufferDataCalls || 0;
    default: return 0;
  }
}

export function updateMetrics(hostId, text, visible) {
  const host = hosts.get(hostId);
  if (!host) return;
  const element = host.metricsElement;
  const show = !!visible && !!text;
  if (!show) {
    if (host.lastMetricsVisible) element.style.display = 'none';
    if (host.lastMetricsText) element.textContent = '';
    host.lastMetricsVisible = false;
    host.lastMetricsText = '';
    return;
  }
  if (host.lastMetricsText !== text) {
    element.textContent = text;
    host.lastMetricsText = text;
  }
  if (!host.lastMetricsVisible) {
    element.style.display = 'block';
    host.lastMetricsVisible = true;
  }
  const canvasLeft = parseFloat(host.canvas.style.left || '0') || 0;
  const canvasTop = parseFloat(host.canvas.style.top || '0') || 0;
  const canvasWidth = parseFloat(host.canvas.style.width || '0') || 0;
  // Avoid layout reads such as offsetWidth in the render hot path. The overlay is debug-only;
  // a fixed approximate width is sufficient and does not force reflow.
  const approximateWidth = Math.min(360, Math.max(140, (text.length > 0 ? Math.min(text.length, 46) * 7 : 160)));
  element.style.left = `${canvasLeft + canvasWidth - approximateWidth - 8}px`;
  element.style.top = `${canvasTop + 8}px`;
}

export function updateCenterCursor(hostId, visible) {
  const host = hosts.get(hostId);
  if (!host) return;
  const show = !!visible;
  host.centerCursorVisible = show;
  if (host.lastCenterCursorVisible === show && !show) return;
  const canvasLeft = parseFloat(host.canvas.style.left || '0') || 0;
  const canvasTop = parseFloat(host.canvas.style.top || '0') || 0;
  const canvasWidth = parseFloat(host.canvas.style.width || '0') || 0;
  const canvasHeight = parseFloat(host.canvas.style.height || '0') || 0;
  host.centerCursorElement.style.left = `${canvasLeft + canvasWidth * 0.5 - 12}px`;
  host.centerCursorElement.style.top = `${canvasTop + canvasHeight * 0.5 - 12}px`;
  host.centerCursorElement.style.display = show ? 'block' : 'none';
  host.lastCenterCursorVisible = show;
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

export function consumePointerDeltaX(hostId) {
  const host = hosts.get(hostId);
  if (!host) return 0;
  return host.pointerDeltaX || 0;
}

export function consumePointerDeltaY(hostId) {
  const host = hosts.get(hostId);
  if (!host) return 0;
  const y = host.pointerDeltaY || 0;
  host.pointerDeltaX = 0;
  host.pointerDeltaY = 0;
  return y;
}
export function downloadTextFile(fileName, text) {
  const safeName = (fileName || 'Avalonia3D-diagnostics.txt').replace(/[\\/:*?"<>|]+/g, '_');
  const blob = new Blob([text || ''], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = safeName;
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  try { anchor.click(); } finally { anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 0); }
}
