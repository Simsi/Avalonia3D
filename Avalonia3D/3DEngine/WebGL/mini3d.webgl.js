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

function createHostState(canvas, gl) {
  const meshProgram = createProgram(gl, `
attribute vec3 aPosition;
uniform mat4 uViewProj;
uniform mat4 uModel;
void main() {
  gl_Position = uViewProj * uModel * vec4(aPosition, 1.0);
}
`, `
precision mediump float;
uniform vec4 uColor;
void main() {
  gl_FragColor = uColor;
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
    gl,
    meshProgram,
    texturedProgram,
    meshPositionLocation: gl.getAttribLocation(meshProgram, 'aPosition'),
    meshViewProjLocation: gl.getUniformLocation(meshProgram, 'uViewProj'),
    meshModelLocation: gl.getUniformLocation(meshProgram, 'uModel'),
    meshColorLocation: gl.getUniformLocation(meshProgram, 'uColor'),
    texturedPositionLocation: gl.getAttribLocation(texturedProgram, 'aPosition'),
    texturedUvLocation: gl.getAttribLocation(texturedProgram, 'aTexCoord'),
    texturedViewProjLocation: gl.getUniformLocation(texturedProgram, 'uViewProj'),
    texturedSamplerLocation: gl.getUniformLocation(texturedProgram, 'uTexture'),
    quadIndexBuffer,
    meshResources: new Map(),
    textureResources: new Map(),
    controlVertexBuffers: new Map(),
    elementIndexUintExt: gl.getExtension('OES_element_index_uint'),
    width: 0,
    height: 0
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

  const gl = canvas.getContext('webgl', {
    alpha: true,
    antialias: true,
    premultipliedAlpha: false,
    preserveDrawingBuffer: false
  });

  if (!gl) {
    throw new Error('WebGL is not available.');
  }

  document.body.appendChild(canvas);

  const id = nextHostId++;
  hosts.set(id, createHostState(canvas, gl));
  return id;
}

export function destroyHost(hostId) {
  const host = hosts.get(hostId);
  if (!host) {
    return;
  }

  const { gl } = host;
  const geometry = JSON.parse(geometryJson);
  const positions = geometry.positions;
  const indices = geometry.indices;
  for (const resource of host.meshResources.values()) {
    gl.deleteBuffer(resource.vertexBuffer);
    gl.deleteBuffer(resource.indexBuffer);
  }
  for (const texture of host.textureResources.values()) {
    gl.deleteTexture(texture.texture);
  }
  for (const buffer of host.controlVertexBuffers.values()) {
    gl.deleteBuffer(buffer);
  }

  gl.deleteBuffer(host.quadIndexBuffer);
  gl.deleteProgram(host.meshProgram);
  gl.deleteProgram(host.texturedProgram);

  host.canvas.remove();
  hosts.delete(hostId);
}

export function updateHost(hostId, x, y, width, height, visible) {
  const host = hosts.get(hostId);
  if (!host) {
    return;
  }

  const canvas = host.canvas;
  if (!visible || width <= 0 || height <= 0) {
    canvas.style.display = 'none';
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
    host.gl.viewport(0, 0, pixelWidth, pixelHeight);
  }
}

export function uploadTexture(hostId, textureId, width, height, rgbaBytesBase64) {
  const host = hosts.get(hostId);
  if (!host) {
    return;
  }

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
  for (let i = 0; i < binary.length; i++) {
    rgbaBytes[i] = binary.charCodeAt(i);
  }
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, rgbaBytes);
  gl.bindTexture(gl.TEXTURE_2D, null);
  resource.width = width;
  resource.height = height;
}

export function uploadMeshGeometry(hostId, meshId, geometryJson) {
  const host = hosts.get(hostId);
  if (!host) {
    return;
  }

  const { gl } = host;
  const geometry = JSON.parse(geometryJson);
  const positions = geometry.positions;
  const indices = geometry.indices;
  let resource = host.meshResources.get(meshId);
  if (!resource) {
    resource = {
      vertexBuffer: gl.createBuffer(),
      indexBuffer: gl.createBuffer(),
      indexCount: 0,
      indexType: gl.UNSIGNED_SHORT
    };
    host.meshResources.set(meshId, resource);
  }

  gl.bindBuffer(gl.ARRAY_BUFFER, resource.vertexBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(positions), gl.STATIC_DRAW);

  const maxIndex = indices.length === 0 ? 0 : Math.max.apply(null, indices);
  let indexArray;
  if (maxIndex > 65535 && host.elementIndexUintExt) {
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

function getOrCreateControlBuffer(host, id) {
  let buffer = host.controlVertexBuffers.get(id);
  if (!buffer) {
    buffer = host.gl.createBuffer();
    host.controlVertexBuffers.set(id, buffer);
  }
  return buffer;
}

export function renderScene(hostId, packetJson) {
  const host = hosts.get(hostId);
  if (!host) {
    return;
  }

  const packet = JSON.parse(packetJson);
  const { gl } = host;
  const viewProj = new Float32Array(packet.viewProjection);

  gl.viewport(0, 0, host.width || 1, host.height || 1);
  gl.enable(gl.DEPTH_TEST);
  gl.depthFunc(gl.LEQUAL);
  gl.clearColor(packet.clearColor[0], packet.clearColor[1], packet.clearColor[2], packet.clearColor[3]);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

  gl.disable(gl.BLEND);
  gl.useProgram(host.meshProgram);
  gl.uniformMatrix4fv(host.meshViewProjLocation, false, viewProj);

  for (const mesh of packet.meshes) {
    const resource = host.meshResources.get(mesh.id);
    if (!resource || resource.indexCount === 0) {
      continue;
    }

    gl.bindBuffer(gl.ARRAY_BUFFER, resource.vertexBuffer);
    gl.enableVertexAttribArray(host.meshPositionLocation);
    gl.vertexAttribPointer(host.meshPositionLocation, 3, gl.FLOAT, false, 0, 0);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, resource.indexBuffer);

    gl.uniformMatrix4fv(host.meshModelLocation, false, new Float32Array(mesh.model));
    gl.uniform4fv(host.meshColorLocation, new Float32Array(mesh.color));
    gl.drawElements(gl.TRIANGLES, resource.indexCount, resource.indexType, 0);
  }

  if (packet.controlPlanes.length > 0) {
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthMask(false);
    gl.useProgram(host.texturedProgram);
    gl.uniformMatrix4fv(host.texturedViewProjLocation, false, viewProj);
    gl.activeTexture(gl.TEXTURE0);
    gl.uniform1i(host.texturedSamplerLocation, 0);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, host.quadIndexBuffer);

    for (const plane of packet.controlPlanes) {
      const textureResource = host.textureResources.get(plane.textureId);
      if (!textureResource) {
        continue;
      }

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

  gl.bindBuffer(gl.ARRAY_BUFFER, null);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);
  gl.bindTexture(gl.TEXTURE_2D, null);
  gl.useProgram(null);
}
