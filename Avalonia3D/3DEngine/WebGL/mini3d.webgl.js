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
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec4 vColor;
varying float vMaterialSlot;
void main() {
  mat4 instanceModel = mat4(aInstanceModel0, aInstanceModel1, aInstanceModel2, aInstanceModel3);
  mat4 model = uUseInstancing > 0.5 ? instanceModel : uModel;
  vec4 world = model * vec4(aPosition, 1.0);
  vWorldPos = world.xyz;
  vNormal = normalize(mat3(model) * aNormal);
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
uniform float uUsePalette;
uniform sampler2D uPalette;
uniform vec2 uPaletteSize;
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec4 vColor;
varying float vMaterialSlot;
void main() {
  vec4 color = vColor;
  if (uUsePalette > 0.5) {
    float sx = (floor(vMaterialSlot + 0.5) + 0.5) / max(uPaletteSize.x, 1.0);
    float sy = (floor(vColor.r + 0.5) + 0.5) / max(uPaletteSize.y, 1.0);
    color = texture2D(uPalette, vec2(sx, sy));
    color.a *= vColor.g * vColor.b;
  }
  if (color.a <= 0.001) discard;
  if (color.a < 0.999) {
    float threshold = mod(floor(gl_FragCoord.x) + floor(gl_FragCoord.y), 4.0) * 0.25;
    if (threshold > color.a) discard;
  }
  vec3 outColor = color.rgb;
  if (uLightingEnabled > 0.5) {
    vec3 n = normalize(vNormal);
    vec3 light = uAmbientLight;
    vec3 dir = normalize(-uDirectionalLightDirection);
    light += max(dot(n, dir), 0.0) * uDirectionalLightColor;
    if (uPointLightColor.a > 0.5) {
      vec3 toPoint = uPointLightPosition.xyz - vWorldPos;
      float dist = length(toPoint);
      float att = clamp(1.0 - dist / max(uPointLightPosition.w, 0.01), 0.0, 1.0);
      light += max(dot(n, normalize(toPoint)), 0.0) * uPointLightColor.rgb * att * att;
    }
    outColor *= clamp(light, 0.0, 2.0);
  }
  gl_FragColor = vec4(outColor, color.a);
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

  const quadIndexBuffer = gl.createBuffer();
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, quadIndexBuffer);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array([0, 1, 2, 0, 2, 3]), gl.STATIC_DRAW);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);

  return {
    canvas,
    metricsElement,
    centerCursorElement,
    gl,
    instancing: gl.getExtension('ANGLE_instanced_arrays'),
    meshProgram,
    texturedProgram,
    meshPositionLocation: gl.getAttribLocation(meshProgram, 'aPosition'),
    meshNormalLocation: gl.getAttribLocation(meshProgram, 'aNormal'),
    meshInstanceModel0Location: gl.getAttribLocation(meshProgram, 'aInstanceModel0'),
    meshInstanceModel1Location: gl.getAttribLocation(meshProgram, 'aInstanceModel1'),
    meshInstanceModel2Location: gl.getAttribLocation(meshProgram, 'aInstanceModel2'),
    meshInstanceModel3Location: gl.getAttribLocation(meshProgram, 'aInstanceModel3'),
    meshInstanceColorLocation: gl.getAttribLocation(meshProgram, 'aInstanceColor'),
    meshMaterialSlotLocation: gl.getAttribLocation(meshProgram, 'aMaterialSlot'),
    meshViewProjLocation: gl.getUniformLocation(meshProgram, 'uViewProj'),
    meshModelLocation: gl.getUniformLocation(meshProgram, 'uModel'),
    meshColorLocation: gl.getUniformLocation(meshProgram, 'uColor'),
    meshUseInstancingLocation: gl.getUniformLocation(meshProgram, 'uUseInstancing'),
    meshUsePaletteLocation: gl.getUniformLocation(meshProgram, 'uUsePalette'),
    meshPaletteLocation: gl.getUniformLocation(meshProgram, 'uPalette'),
    meshPaletteSizeLocation: gl.getUniformLocation(meshProgram, 'uPaletteSize'),
    meshLightingEnabledLocation: gl.getUniformLocation(meshProgram, 'uLightingEnabled'),
    meshAmbientLightLocation: gl.getUniformLocation(meshProgram, 'uAmbientLight'),
    meshDirectionalLightDirectionLocation: gl.getUniformLocation(meshProgram, 'uDirectionalLightDirection'),
    meshDirectionalLightColorLocation: gl.getUniformLocation(meshProgram, 'uDirectionalLightColor'),
    meshPointLightPositionLocation: gl.getUniformLocation(meshProgram, 'uPointLightPosition'),
    meshPointLightColorLocation: gl.getUniformLocation(meshProgram, 'uPointLightColor'),
    texturedPositionLocation: gl.getAttribLocation(texturedProgram, 'aPosition'),
    texturedUvLocation: gl.getAttribLocation(texturedProgram, 'aTexCoord'),
    texturedViewProjLocation: gl.getUniformLocation(texturedProgram, 'uViewProj'),
    texturedSamplerLocation: gl.getUniformLocation(texturedProgram, 'uTexture'),
    quadIndexBuffer,
    meshResources: new Map(),
    instanceBuffers: new Map(),
    retainedBatches: new Map(),
    textureResources: new Map(),
    controlVertexBuffers: new Map(),
    elementIndexUintExt: gl.getExtension('OES_element_index_uint'),
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

  const gl = canvas.getContext('webgl', {
    alpha: true,
    antialias: true,
    premultipliedAlpha: false,
    preserveDrawingBuffer: false
  });
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
  for (const r of host.meshResources.values()) {
    gl.deleteBuffer(r.vertexBuffer); gl.deleteBuffer(r.normalBuffer); gl.deleteBuffer(r.materialSlotBuffer); gl.deleteBuffer(r.indexBuffer);
  }
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

export function uploadTexture(hostId, textureId, width, height, rgbaBytesBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  let resource = host.textureResources.get(textureId);
  if (!resource) {
    resource = { texture: gl.createTexture(), width: 0, height: 0 };
    host.textureResources.set(textureId, resource);
  }
  gl.bindTexture(gl.TEXTURE_2D, resource.texture);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  const binary = atob(rgbaBytesBase64);
  const rgbaBytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) rgbaBytes[i] = binary.charCodeAt(i);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, rgbaBytes);
  gl.bindTexture(gl.TEXTURE_2D, null);
  resource.width = width;
  resource.height = height;
}

export function uploadMeshGeometry(hostId, meshId, geometryJson) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const geometry = JSON.parse(geometryJson);
  const positions = geometry.positions;
  const normals = geometry.normals || [];
  const indices = geometry.indices;
  const materialSlots = geometry.materialSlots || [];
  let resource = host.meshResources.get(meshId);
  if (!resource) {
    resource = { vertexBuffer: gl.createBuffer(), normalBuffer: gl.createBuffer(), materialSlotBuffer: gl.createBuffer(), indexBuffer: gl.createBuffer(), indexCount: 0, indexType: gl.UNSIGNED_SHORT };
    host.meshResources.set(meshId, resource);
  }
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.vertexBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(positions), gl.STATIC_DRAW);
  const normalData = normals.length === positions.length ? normals : createDefaultNormals(positions.length / 3);
  gl.bindBuffer(gl.ARRAY_BUFFER, resource.normalBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(normalData), gl.STATIC_DRAW);
  const slotData = materialSlots.length === positions.length / 3 ? materialSlots : createDefaultMaterialSlots(positions.length / 3);
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
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);
}

function createDefaultNormals(vertexCount) {
  const normals = new Array(Math.max(0, vertexCount) * 3);
  for (let i = 0; i < normals.length; i += 3) { normals[i] = 0; normals[i + 1] = 0; normals[i + 2] = 1; }
  return normals;
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

function decodeFloat32Base64(base64) {
  const bytes = decodeBase64Bytes(base64);
  if (bytes.byteLength === 0) return new Float32Array(0);
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  return new Float32Array(copy.buffer);
}

function getOrCreateRetainedBatch(host, batchId) {
  let batch = host.retainedBatches.get(batchId);
  if (!batch) {
    const gl = host.gl;
    batch = {
      meshId: '',
      lightingEnabled: 0,
      usePalette: false,
      instanceCount: 0,
      transformBuffer: gl.createBuffer(),
      stateBuffer: gl.createBuffer(),
      paletteTexture: null,
      paletteWidth: 1,
      paletteHeight: 1
    };
    host.retainedBatches.set(batchId, batch);
  }
  return batch;
}

export function uploadRetainedBatchTransforms(hostId, batchId, meshId, lightingEnabled, usePalette, instanceCount, transformFloatsBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const batch = getOrCreateRetainedBatch(host, batchId);
  batch.meshId = meshId;
  batch.lightingEnabled = lightingEnabled || 0;
  batch.usePalette = !!usePalette;
  batch.instanceCount = instanceCount || 0;
  const transforms = decodeFloat32Base64(transformFloatsBase64);
  gl.bindBuffer(gl.ARRAY_BUFFER, batch.transformBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, transforms, gl.STATIC_DRAW);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
}

export function uploadRetainedBatchState(hostId, batchId, usePalette, paletteWidth, paletteHeight, stateFloatsBase64, paletteRgbaBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const { gl } = host;
  const batch = getOrCreateRetainedBatch(host, batchId);
  batch.usePalette = !!usePalette;
  const states = decodeFloat32Base64(stateFloatsBase64);
  gl.bindBuffer(gl.ARRAY_BUFFER, batch.stateBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, states, gl.DYNAMIC_DRAW);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
  if (batch.usePalette && paletteRgbaBase64) {
    if (!batch.paletteTexture) batch.paletteTexture = gl.createTexture();
    batch.paletteWidth = Math.max(1, paletteWidth || 1);
    batch.paletteHeight = Math.max(1, paletteHeight || 1);
    const rgbaBytes = decodeBase64Bytes(paletteRgbaBase64);
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

export function uploadRetainedBatchStateRange(hostId, batchId, startInstance, stateFloatsBase64) {
  const host = hosts.get(hostId);
  if (!host) return;
  const batch = host.retainedBatches.get(batchId);
  if (!batch || !batch.stateBuffer) return;
  const states = decodeFloat32Base64(stateFloatsBase64);
  if (states.length === 0) return;
  const { gl } = host;
  gl.bindBuffer(gl.ARRAY_BUFFER, batch.stateBuffer);
  gl.bufferSubData(gl.ARRAY_BUFFER, Math.max(0, startInstance | 0) * 16, states);
  gl.bindBuffer(gl.ARRAY_BUFFER, null);
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
  host.retainedBatches.delete(batchId);
}

export function clearRetainedBatches(hostId) {
  const host = hosts.get(hostId);
  if (!host) return;
  for (const id of Array.from(host.retainedBatches.keys())) destroyRetainedBatch(hostId, id);
}

export function renderScene(hostId, packetJson) {
  const host = hosts.get(hostId);
  if (!host) return;
  const packet = JSON.parse(packetJson);
  const { gl } = host;
  const batches = packet.batches || [];
  const retainedRefs = packet.retainedBatches || [];
  const viewProj = new Float32Array(packet.viewProjection);
  const liveMeshIds = new Set(batches.map(batch => batch.id));
  for (const ref of retainedRefs) { const rb = host.retainedBatches.get(ref.id); if (rb && rb.meshId) liveMeshIds.add(rb.meshId); }
  for (const [id, resource] of host.meshResources.entries()) {
    if (!liveMeshIds.has(id)) { gl.deleteBuffer(resource.vertexBuffer); gl.deleteBuffer(resource.normalBuffer); gl.deleteBuffer(resource.materialSlotBuffer); gl.deleteBuffer(resource.indexBuffer); host.meshResources.delete(id); }
  }
  const liveControlIds = new Set(packet.controlPlanes.map(plane => plane.id));
  const liveTextureIds = new Set(packet.controlPlanes.map(plane => plane.textureId));
  for (const [id, buffer] of host.controlVertexBuffers.entries()) if (!liveControlIds.has(id)) { gl.deleteBuffer(buffer); host.controlVertexBuffers.delete(id); }
  for (const [id, texture] of host.textureResources.entries()) if (!liveTextureIds.has(id)) { gl.deleteTexture(texture.texture); host.textureResources.delete(id); }

  gl.viewport(0, 0, host.width || 1, host.height || 1);
  gl.enable(gl.DEPTH_TEST);
  gl.depthFunc(gl.LEQUAL);
  gl.clearColor(packet.clearColor[0], packet.clearColor[1], packet.clearColor[2], packet.clearColor[3]);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

  gl.disable(gl.BLEND);
  gl.useProgram(host.meshProgram);
  gl.uniform3fv(host.meshAmbientLightLocation, new Float32Array(packet.ambientLight || [0.28, 0.28, 0.28]));
  gl.uniform3fv(host.meshDirectionalLightDirectionLocation, new Float32Array(packet.directionalLightDirection || [-0.35, -0.75, -0.55]));
  gl.uniform3fv(host.meshDirectionalLightColorLocation, new Float32Array(packet.directionalLightColor || [0, 0, 0]));
  gl.uniform4fv(host.meshPointLightPositionLocation, new Float32Array(packet.pointLightPosition || [0, 0, 0, 1]));
  gl.uniform4fv(host.meshPointLightColorLocation, new Float32Array(packet.pointLightColor || [0, 0, 0, 0]));
  gl.uniformMatrix4fv(host.meshViewProjLocation, false, viewProj);

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
  if (host.meshMaterialSlotLocation >= 0) {
    gl.bindBuffer(gl.ARRAY_BUFFER, resource.materialSlotBuffer);
    gl.enableVertexAttribArray(host.meshMaterialSlotLocation);
    gl.vertexAttribPointer(host.meshMaterialSlotLocation, 1, gl.FLOAT, false, 0, 0);
  }
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, resource.indexBuffer);
}

function drawRetainedBatch(host, batchId) {
  const { gl } = host;
  const batch = host.retainedBatches.get(batchId);
  if (!batch || batch.instanceCount <= 0 || !host.instancing) return;
  const resource = host.meshResources.get(batch.meshId);
  if (!resource || resource.indexCount === 0) return;
  bindMeshGeometry(host, resource);
  gl.uniform1f(host.meshLightingEnabledLocation, batch.lightingEnabled || 0);
  gl.uniform1f(host.meshUsePaletteLocation, 0);
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
  resetInstanceDivisors(host);
  gl.uniform1f(host.meshUsePaletteLocation, 0);
}

function drawMeshBatch(host, batch) {
  const { gl } = host;
  const resource = host.meshResources.get(batch.id);
  if (!resource || resource.indexCount === 0 || !batch.instanceData || batch.instanceCount <= 0) return;
  bindMeshGeometry(host, resource);
  gl.uniform1f(host.meshLightingEnabledLocation, batch.lightingEnabled || 0);
  gl.uniform1f(host.meshUsePaletteLocation, 0);

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
    resetInstanceDivisors(host);
  } else {
    gl.uniform1f(host.meshUseInstancingLocation, 0);
    const data = batch.instanceData;
    for (let i = 0; i < batch.instanceCount; i++) {
      const o = i * 20;
      gl.uniformMatrix4fv(host.meshModelLocation, false, new Float32Array(data.slice(o, o + 16)));
      gl.uniform4fv(host.meshColorLocation, new Float32Array(data.slice(o + 16, o + 20)));
      gl.drawElements(gl.TRIANGLES, resource.indexCount, resource.indexType, 0);
    }
  }
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
