#!/usr/bin/env node

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ignoredDirectories = new Set(['Artifacts', 'bin', 'obj', '.git', '.vs']);
const errors = [];

function walk(directory) {
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (entry.isDirectory() && ignoredDirectories.has(entry.name)) continue;
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...walk(fullPath));
    else if (entry.isFile() && entry.name.endsWith('.cs')) files.push(fullPath);
  }
  return files;
}

function stripNonCode(source) {
  const output = source.split('');
  let index = 0;
  let state = 'code';
  let rawQuoteCount = 0;
  const blank = position => { if (output[position] !== '\n' && output[position] !== '\r') output[position] = ' '; };

  while (index < source.length) {
    const current = source[index];
    const next = source[index + 1] ?? '';
    if (state === 'code') {
      if (current === '/' && next === '/') { blank(index); blank(index + 1); index += 2; state = 'line-comment'; continue; }
      if (current === '/' && next === '*') { blank(index); blank(index + 1); index += 2; state = 'block-comment'; continue; }
      if (current === '@' && next === '"') { blank(index); blank(index + 1); index += 2; state = 'verbatim-string'; continue; }
      if ((current === '$' && next === '@' && source[index + 2] === '"') ||
          (current === '@' && next === '$' && source[index + 2] === '"')) {
        blank(index); blank(index + 1); blank(index + 2); index += 3; state = 'verbatim-string'; continue;
      }
      if (current === '$' && next === '"') { blank(index); blank(index + 1); index += 2; state = 'regular-string'; continue; }
      if (current === '"') {
        let count = 1;
        while (source[index + count] === '"') count++;
        if (count >= 3) {
          rawQuoteCount = count;
          for (let i = 0; i < count; i++) blank(index + i);
          index += count;
          state = 'raw-string';
          continue;
        }
        blank(index++); state = 'regular-string'; continue;
      }
      if (current === '\'') { blank(index++); state = 'character'; continue; }
      index++;
      continue;
    }

    if (state === 'line-comment') {
      if (current === '\n' || current === '\r') state = 'code'; else blank(index);
      index++;
      continue;
    }
    if (state === 'block-comment') {
      if (current === '*' && next === '/') { blank(index); blank(index + 1); index += 2; state = 'code'; }
      else { blank(index); index++; }
      continue;
    }
    if (state === 'regular-string' || state === 'character') {
      const terminator = state === 'regular-string' ? '"' : '\'';
      if (current === '\\') { blank(index); if (index + 1 < source.length) blank(index + 1); index += 2; continue; }
      blank(index);
      index++;
      if (current === terminator) state = 'code';
      continue;
    }
    if (state === 'verbatim-string') {
      if (current === '"' && next === '"') { blank(index); blank(index + 1); index += 2; continue; }
      blank(index);
      index++;
      if (current === '"') state = 'code';
      continue;
    }
    if (state === 'raw-string') {
      if (source.startsWith('"'.repeat(rawQuoteCount), index)) {
        for (let i = 0; i < rawQuoteCount; i++) blank(index + i);
        index += rawQuoteCount;
        state = 'code';
      } else {
        blank(index++);
      }
    }
  }

  if (state !== 'code' && state !== 'line-comment') return { code: output.join(''), unterminated: state };
  return { code: output.join(''), unterminated: null };
}

function lineAt(source, index) {
  let line = 1;
  for (let position = 0; position < index; position++) if (source[position] === '\n') line++;
  return line;
}

function isSystemQualified(source, identifierIndex) {
  const prefix = source.slice(Math.max(0, identifierIndex - 24), identifierIndex);
  return prefix.endsWith('System.') || prefix.endsWith('global::System.');
}

function isMemberOrNamespaceQualified(source, identifierIndex) {
  if (identifierIndex <= 0) return false;
  const previous = source[identifierIndex - 1];
  return previous === '.' || previous === ':' || /[A-Za-z0-9_]/.test(previous);
}

for (const file of walk(root)) {
  const relative = path.relative(root, file).replaceAll('\\', '/');
  const source = fs.readFileSync(file, 'utf8');
  const { code, unterminated } = stripNonCode(source);
  if (unterminated) errors.push(`${relative}: unterminated ${unterminated}.`);

  const stack = [];
  const opening = new Set(['(', '[', '{']);
  const expected = new Map([[')', '('], [']', '['], ['}', '{']]);
  for (let index = 0; index < code.length; index++) {
    const character = code[index];
    if (opening.has(character)) stack.push({ character, index });
    else if (expected.has(character)) {
      const top = stack.pop();
      if (!top || top.character !== expected.get(character)) {
        errors.push(`${relative}:${lineAt(code, index)}: mismatched '${character}'.`);
        break;
      }
    }
  }
  if (stack.length > 0) {
    const top = stack.at(-1);
    errors.push(`${relative}:${lineAt(code, top.index)}: unclosed '${top.character}'.`);
  }

  if (/\?\?=\s*throw\b/.test(code)) errors.push(`${relative}: contains unsupported '??= throw' expression.`);

  // In an interpolated expression the ':' in global:: is parsed as a format
  // separator by the C# lexer. Bind the value to a local before interpolation.
  if (/\$(?:@)?"[^"\n]*\{\s*global::/.test(source) || /@\$"[^"\n]*\{\s*global::/.test(source)) {
    errors.push(`${relative}: global:: cannot appear directly inside an interpolated expression; bind it to a local first.`);
  }

  if (relative.startsWith('Core/')) {
    for (const match of code.matchAll(/\bMath\./g)) {
      if (!isSystemQualified(code, match.index) && !isMemberOrNamespaceQualified(code, match.index)) {
        errors.push(`${relative}:${lineAt(code, match.index)}: unqualified Math resolves to ThreeDEngine.Core.Math; use global::System.Math.`);
      }
    }
  }

  if (relative === 'Core/Scene/Scene3D.cs') {
    for (const match of code.matchAll(/\bEnvironment\.CurrentManagedThreadId\b/g)) {
      if (!isSystemQualified(code, match.index) && !isMemberOrNamespaceQualified(code, match.index)) {
        errors.push(`${relative}:${lineAt(code, match.index)}: Environment resolves to SceneEnvironment3D; use global::System.Environment.`);
      }
    }
  }
}

const simulationHostSource = fs.readFileSync(path.join(root, 'Avalonia/Hosting/SceneSimulationHost3D.cs'), 'utf8');
if (!simulationHostSource.includes('Join(WorkerShutdownTimeoutMilliseconds)') || /\.Join\s*\(\s*\)\s*;/.test(simulationHostSource)) {
  errors.push('Avalonia/Hosting/SceneSimulationHost3D.cs: worker shutdown must remain bounded.');
}

const sceneControlSource = fs.readFileSync(path.join(root, 'Avalonia/Controls/Scene3DControl.cs'), 'utf8');
const fixedUpdateStart = sceneControlSource.indexOf('private void OnSceneFixedUpdate');
const fixedUpdateEnd = sceneControlSource.indexOf('private void OnSceneChanged', fixedUpdateStart);
if (fixedUpdateStart < 0 || fixedUpdateEnd < 0) {
  errors.push('Avalonia/Controls/Scene3DControl.cs: fixed-update navigation region was not found.');
} else {
  const fixedUpdateRegion = sceneControlSource.slice(fixedUpdateStart, fixedUpdateEnd);
  for (const forbidden of ['GetValue(', 'this.NavigationMode', 'FreeFlySettings', 'PersonSettings', '_navigationStateSync', 'PressedKeyCount', '_pressedKeys']) {
    if (fixedUpdateRegion.includes(forbidden)) {
      errors.push(`Avalonia/Controls/Scene3DControl.cs: fixed-update worker region reads UI-owned state '${forbidden}'.`);
    }
  }
}

const frameRenderedStart = sceneControlSource.indexOf('private void OnPresenterFrameRendered');
const frameRenderedEnd = sceneControlSource.indexOf('private void SchedulePerformanceMetricsTextUpdate', frameRenderedStart);
if (frameRenderedStart < 0 || frameRenderedEnd < 0) {
  errors.push('Avalonia/Controls/Scene3DControl.cs: frame-rendered runtime statistics region was not found.');
} else {
  const frameRenderedRegion = sceneControlSource.slice(frameRenderedStart, frameRenderedEnd);
  const runtimeStatsCall = frameRenderedRegion.indexOf('UpdateRuntimeStats(');
  const prematureTimestampWrite = frameRenderedRegion.indexOf('Volatile.Write(ref _lastFrameRenderedTicks');
  if (prematureTimestampWrite >= 0 && (runtimeStatsCall < 0 || prematureTimestampWrite < runtimeStatsCall)) {
    errors.push('Avalonia/Controls/Scene3DControl.cs: presentation timestamp is overwritten before runtime FPS calculation.');
  }
}

const openGlRendererSource = fs.readFileSync(path.join(root, 'OpenGL/Rendering/OpenGlSceneRenderer.cs'), 'utf8');
const retainedSlotStart = openGlRendererSource.indexOf('private bool TryUpdateRetainedOrdinarySlot');
const retainedSlotEnd = openGlRendererSource.indexOf('private void SweepInactiveInstanceBatches', retainedSlotStart);
if (retainedSlotStart < 0 || retainedSlotEnd < 0 ||
    !openGlRendererSource.slice(retainedSlotStart, retainedSlotEnd).includes('ConfigureBatchSkinning(batch, skinnedPart)')) {
  errors.push('OpenGL/Rendering/OpenGlSceneRenderer.cs: retained ordinary slot updates must refresh GPU skinning matrices.');
}

const sphereSource = fs.readFileSync(path.join(root, 'Core/Scene/Sphere3D.cs'), 'utf8');
if (!sphereSource.includes('value >= 3') || !sphereSource.includes('value >= 2')) {
  errors.push('Core/Scene/Sphere3D.cs: low-poly sphere contract must support at least 3 segments and 2 rings.');
}

const animationSequenceSource = fs.readFileSync(path.join(root, 'Core/Assets/Models/Animation/ModelAnimationSequence3D.cs'), 'utf8');
for (const required of [
  '_model.Animation.PlaybackCompleted += OnPlaybackCompleted',
  '_model.OwnerScene is { UpdateLoop.AdvanceAnimations: true }',
  'private void OnPlaybackCompleted'
]) {
  if (!animationSequenceSource.includes(required)) {
    errors.push(`Core/Assets/Models/Animation/ModelAnimationSequence3D.cs: required scene-owned animation-clock contract '${required}' is missing.`);
  }
}

const animationControllerSource = fs.readFileSync(path.join(root, 'Core/Assets/Models/Animation/ModelAnimationController3D.cs'), 'utf8');
for (const required of ['internal event EventHandler? PlaybackCompleted', 'PlaybackCompleted?.Invoke(this, EventArgs.Empty)']) {
  if (!animationControllerSource.includes(required)) {
    errors.push(`Core/Assets/Models/Animation/ModelAnimationController3D.cs: required animation completion contract '${required}' is missing.`);
  }
}

const engineLogSource = fs.readFileSync(path.join(root, 'Core/Diagnostics/EngineLog3D.cs'), 'utf8');
for (const required of ['CurrentLogFilePath', 'AVALONIA3D_LOG_FILE_MAX_BYTES', 'UnhandledException', 'UnobservedTaskException', 'WriteDiagnosticBlock']) {
  if (!engineLogSource.includes(required)) errors.push(`Core/Diagnostics/EngineLog3D.cs: required diagnostic capability '${required}' is missing.`);
}


const worldSource = fs.readFileSync(path.join(root, 'Core/World/World3D.cs'), 'utf8');
if (!worldSource.includes('using RuntimeEnvironment = global::System.Environment;')) {
  errors.push('Core/World/World3D.cs: System.Environment alias is required to avoid collision with ThreeDEngine.Core.Environment.');
}
if (/(?<!global::System\.)\bEnvironment\.CurrentManagedThreadId\b/.test(worldSource.replace('using RuntimeEnvironment = global::System.Environment;', ''))) {
  errors.push('Core/World/World3D.cs: CurrentManagedThreadId must be accessed through RuntimeEnvironment/global::System.Environment.');
}
for (const required of [
  'StrictSimulationOwner',
  'BindPersistentOwner()',
  'EnterTransientOwnerScope()',
  'PublishSnapshot(bool force = false)',
  'CreateCommandBuffer()',
  'AcquireReadSnapshot()',
  'Replay'
]) {
  if (!worldSource.includes(required)) errors.push(`Core/World/World3D.cs: stage-2 world contract '${required}' is missing.`);
}

const colliderSource = fs.readFileSync(path.join(root, 'Core/Collision/Collider3D.cs'), 'utf8');
if (!colliderSource.includes('private protected SceneAccessLease3D EnterMutationScope()')) {
  errors.push('Core/Collision/Collider3D.cs: EnterMutationScope must remain private protected because SceneAccessLease3D is internal.');
}
if (/\bpublic\s+SceneAccessLease3D\s+EnterMutationScope\s*\(|(?<!private\s)\bprotected\s+SceneAccessLease3D\s+EnterMutationScope\s*\(/.test(colliderSource)) {
  errors.push('Core/Collision/Collider3D.cs: EnterMutationScope exposes an internal lease through a wider accessibility.');
}

const ownerLeaseSource = fs.readFileSync(path.join(root, 'Core/World/WorldOwnerLease3D.cs'), 'utf8');
if (!ownerLeaseSource.includes('internal sealed class WorldOwnerLease3D') || ownerLeaseSource.includes('struct WorldOwnerLease3D')) {
  errors.push('Core/World/WorldOwnerLease3D.cs: owner token must remain a non-copyable sealed class.');
}

const interactionSource = fs.readFileSync(path.join(root, 'Avalonia/Interaction/SceneInteractionManager.cs'), 'utf8');
for (const required of [
  'Scene.World.Mutate(scene => scene.Camera.Orbit',
  'Scene.World.Mutate(scene => scene.Camera.Pan',
  'Scene.World.Mutate(scene => scene.Camera.Dolly',
  'Scene.World.Mutate(_ => oldHovered.IsHovered = false)',
  'Scene.World.Mutate(_ => newHovered.IsHovered = true)'
]) {
  if (!interactionSource.includes(required)) errors.push(`Avalonia/Interaction/SceneInteractionManager.cs: owner-routed interaction '${required}' is missing.`);
}

const runtimeOptionsStart = sceneControlSource.indexOf('private void UpdateRuntimeOptionsFromControl');
const runtimeOptionsEnd = sceneControlSource.indexOf('private static string OnOff', runtimeOptionsStart);
if (runtimeOptionsStart < 0 || runtimeOptionsEnd < 0) {
  errors.push('Avalonia/Controls/Scene3DControl.cs: runtime-options region was not found.');
} else {
  const runtimeOptionsRegion = sceneControlSource.slice(runtimeOptionsStart, runtimeOptionsEnd);
  for (const required of ['Scene.World.Mutate', '_simulationHost.PumpCommands()']) {
    if (!runtimeOptionsRegion.includes(required)) errors.push(`Avalonia/Controls/Scene3DControl.cs: strict runtime configuration must include '${required}'.`);
  }
}

const webGlPresenterSource = fs.readFileSync(path.join(root, 'WebGL/Controls/WebGlScenePresenter.cs'), 'utf8');
const releaseIndex = webGlPresenterSource.indexOf('frame.ReleaseSceneAccess();');
const submitIndex = webGlPresenterSource.indexOf('RenderRetainedFrameDirect(stats, in retainedFrameState)');
if (releaseIndex < 0 || submitIndex < 0 || releaseIndex > submitIndex) {
  errors.push('WebGL/Controls/WebGlScenePresenter.cs: mutable scene access must be released before retained JS/GPU submission.');
}

const stage3Contracts = new Map([
  ['Core/Rendering/Rhi/RhiCommands3D.cs', ['RhiCommandEncoder3D', 'RhiCommandBuffer3D', 'IRhiCommandExecutor3D', 'BeginComputePass', 'CopyBufferToTexture']],
  ['Core/Rendering/Rhi/RhiQueue3D.cs', ['single-submit', 'RhiFence3D Submit', 'RequireComplete', 'ValidateCommand']],
  ['Core/Rendering/Rhi/RhiFrameResources3D.cs', ['Triple-buffered', 'BeginFrame(RhiQueue3D queue)', 'No implicit blocking']],
  ['Core/Rendering/Rhi/RhiUploadRing3D.cs', ['No heap fallback', 'Allocate(int byteCount', 'maximumCapacity']],
  ['Core/Rendering/Rhi/RhiPipelineDescriptors3D.cs', ['RhiShaderReflection3D', 'RhiBindGroupDescriptor3D', 'RhiRenderPipelineDescriptor3D']],
  ['Core/Rendering/Rhi/RhiDeferredLifetime3D.cs', ['Fence-gated', 'Collect(RhiQueue3D queue)']],
  ['Core/Rendering/Rhi/WebGpuRhiContract3D.cs', ['WebGpuBaseline', 'No presenter is']],
  ['OpenGL/Rendering/OpenGlSceneRenderer.Rhi.cs', ['IRhiCommandExecutor3D', 'device.Submit(commands, this)', 'No CPU fallback']],
  ['WebGL/Controls/WebGlScenePresenter.Rhi.cs', ['IRhiCommandExecutor3D', 'device.Submit(commands, this)', 'No CPU fallback']]
]);
for (const [relative, requiredValues] of stage3Contracts) {
  const source = fs.readFileSync(path.join(root, relative), 'utf8');
  for (const required of requiredValues) {
    if (!source.includes(required)) errors.push(`${relative}: stage-3 RHI contract '${required}' is missing.`);
  }
}

const stage3OpenGlRendererSource = fs.readFileSync(path.join(root, 'OpenGL/Rendering/OpenGlSceneRenderer.cs'), 'utf8');
for (const [relative, source] of [
  ['OpenGL/Rendering/OpenGlSceneRenderer.cs', stage3OpenGlRendererSource],
  ['WebGL/Controls/WebGlScenePresenter.cs', webGlPresenterSource]
]) {
  if (!source.includes('device.BeginFrame(plan.RhiSubmission)') || !source.includes('ExecuteRhiFrame(')) {
    errors.push(`${relative}: live frames must enter the executable RHI command path.`);
  }
}

const stage4Contracts = new Map([
  ['Core/Rendering/Rhi/RhiCommands3D.cs', [
    'WriteBuffer', 'ClearBuffer', 'DrawIndirect', 'DrawIndexedIndirect',
    'MultiDrawIndexedIndirect', 'DispatchIndirect', 'ColorTarget', 'DepthTarget'
  ]],
  ['Core/Rendering/GpuDriven/GpuDrivenRenderer3D.cs', [
    'RhiCapabilityProfile3D.GpuDriven', 'clustered-light-assignment', 'meshlet-visibility-lod',
    'gpu-particle-simulation', 'MultiDrawIndexedIndirect', 'hdr-tone-map',
    'No legacy or CPU fallback was attempted'
  ]],
  ['Core/Rendering/GpuDriven/GpuParticlePipeline3D.cs', [
    'AdvanceParticles=false', 'CompleteFrame()', '_sourceIsA',
    'CPU particle rendering fallback is not permitted'
  ]],
  ['Core/Rendering/GpuDriven/RenderGraph3D.cs', [
    'Render graph', 'AliasedResourceCount', 'RhiResourceBarrier3D', 'IDisposable'
  ]],
  ['Core/Rendering/GpuDriven/GpuDrivenShaderCatalog3D.cs', [
    'cull-meshlets.wgsl', 'build-clusters.wgsl', 'simulate-particles.wgsl',
    'forward.vert.wgsl', 'tonemap.frag.wgsl', 'skinMatrices', 'DrawIndexedIndirect'
  ]],
  ['Core/Rendering/GpuDriven/GpuDrivenSceneDatabase3D.cs', [
    'Persistent GPU scene database', 'VisibleMeshlets', 'IndirectCommands', 'BuildExpandedMeshletIndices', 'BuildCanonicalVertices', 'AddSkinPalette'
  ]]
]);
for (const [relative, requiredValues] of stage4Contracts) {
  const source = fs.readFileSync(path.join(root, relative), 'utf8');
  for (const required of requiredValues) {
    if (!source.includes(required)) errors.push(`${relative}: stage-4 GPU-driven contract '${required}' is missing.`);
  }
}


const gpuShaderSource = fs.readFileSync(path.join(root, 'Core/Rendering/GpuDriven/GpuDrivenShaderCatalog3D.cs'), 'utf8');
for (const forbidden of [
  'Backend compiler expands the packed record helpers',
  'return vec4<f32>(0.0, 0.0, 0.0, 1.0);',
  '@fragment fn main() -> @location(0) vec4<f32> { return vec4<f32>(1.0); }'
]) {
  if (gpuShaderSource.includes(forbidden)) errors.push(`Core/Rendering/GpuDriven/GpuDrivenShaderCatalog3D.cs: placeholder shader body remains: '${forbidden}'.`);
}
const gpuRecordsSource = fs.readFileSync(path.join(root, 'Core/Rendering/GpuDriven/GpuDrivenSceneRecords3D.cs'), 'utf8');
for (const required of ['GpuDrivenVertex3D', 'LightCounts', 'Timing', 'Reserved2']) {
  if (!gpuRecordsSource.includes(required)) errors.push(`Core/Rendering/GpuDriven/GpuDrivenSceneRecords3D.cs: packed GPU record contract '${required}' is missing.`);
}
const rhiPipelineSource = fs.readFileSync(path.join(root, 'Core/Rendering/Rhi/RhiPipelineDescriptors3D.cs'), 'utf8');
for (const required of ['RhiVertexFormat3D', 'RhiVertexBufferLayout3D', 'VertexBuffers']) {
  if (!rhiPipelineSource.includes(required)) errors.push(`Core/Rendering/Rhi/RhiPipelineDescriptors3D.cs: vertex-input contract '${required}' is missing.`);
}

const testHostSource = fs.readFileSync(path.join(root, 'Tests/Program.cs'), 'utf8');
for (const required of [
  'void WriteBuffer(', 'void ClearBuffer(', 'void DrawIndirect(', 'void DrawIndexedIndirect(',
  'void MultiDrawIndexedIndirect(', 'void DispatchIndirect(', 'GPU-driven RHI commands and render-graph aliasing'
]) {
  if (!testHostSource.includes(required)) errors.push(`Tests/Program.cs: stage-4 RHI executor/test contract '${required}' is missing.`);
}


const stage5Contracts = new Map([
  ['Core/Assets/Streaming/AssetManager3D.cs', [
    'PriorityQueue<QueueItem, QueuePriority>', 'reserveLease: true', 'ReservedLeaseCount',
    'IAsyncModelAssetLoader3D', 'RejectSynchronousLoaderInBrowser', 'No synchronous load fallback is allowed'
  ]],
  ['Core/Assets/Streaming/ContentAddressedAssetCache3D.cs', [
    'SHA256.HashData', 'failed SHA-256 verification', 'File.Move(temporary, path, overwrite: false)'
  ]],
  ['Core/Assets/Streaming/TextureStreaming3D.cs', [
    'ITextureMipSource3D', 'coarse-to-fine', 'MipCompletions',
    'Quality reduction is not permitted', 'ThrowIfDisposed()'
  ]],
  ['Core/Spatial/SpatialHashGrid3D.cs', [
    'OverflowObjectCount', '_overflowObjects', 'MaximumOverflowObjects'
  ]],
  ['Core/Interaction/GpuPicking3D.cs', [
    'IGpuPickingBackend3D', 'CPU fallback is prohibited', 'returned out-of-order request id'
  ]],
  ['Core/Serialization/SceneSerializer3D.cs', [
    'SceneDocument3D.CurrentVersion', 'MaximumExtensionParameterBytes',
    'maximumDecodedLength', 'using var update = scene.BeginUpdate()'
  ]],
  ['Core/Diagnostics/EngineProfiler3D.cs', [
    'bounded flight recorder', 'P95FrameMilliseconds', 'P99FrameMilliseconds'
  ]],
  ['Core/Diagnostics/EngineFrameCapture3D.cs', [
    'Avalonia3D.FrameCapture', 'MaterialExtensionCount', 'GpuPicking'
  ]],
  ['Core/Diagnostics/ProductionAcceptance3D.cs', [
    'MinimumFrameCount', 'MaximumP99FrameMilliseconds', 'RequireGpuDriven'
  ]],
  ['Core/Rendering/Extensions/RenderExtensionBackend3D.cs', [
    'IRenderExtensionBackend3D', 'legacy callback emulation and CPU execution are prohibited'
  ]],
  ['Core/Materials/MaterialShaderExtensionRegistry3D.cs', [
    'MaterialShaderExtensionDefinition3D', 'Parameter byte size must be aligned'
  ]]
]);
for (const [relative, requiredValues] of stage5Contracts) {
  const source = fs.readFileSync(path.join(root, relative), 'utf8');
  for (const required of requiredValues) {
    if (!source.includes(required)) errors.push(`${relative}: stage-5 production contract '${required}' is missing.`);
  }
}

const performanceOptionsSource = fs.readFileSync(path.join(root, 'Core/Scene/ScenePerformanceOptions.cs'), 'utf8');
for (const forbidden of ['AllowPickingFullScanFallback', 'MaxPickingFullScanFallbackObjects']) {
  if (performanceOptionsSource.includes(forbidden)) errors.push(`Core/Scene/ScenePerformanceOptions.cs: removed full-scan option '${forbidden}' was restored.`);
}
for (const relative of ['Core/Interaction/Raycaster.cs', 'Core/Collision/CollisionWorld3D.cs']) {
  const source = fs.readFileSync(path.join(root, relative), 'utf8');
  for (const forbidden of ['AllowPickingFullScanFallback', 'MaxPickingFullScanFallbackObjects', 'foreach (var object3D in scene.Registry.Objects)']) {
    if (source.includes(forbidden)) errors.push(`${relative}: forbidden full-registry fallback '${forbidden}' is present.`);
  }
}

const stage5SerializerSource = fs.readFileSync(path.join(root, 'Core/Serialization/SceneSerializer3D.cs'), 'utf8');
const restoreStart = stage5SerializerSource.indexOf('public static async ValueTask<Scene3D> RestoreAsync');
const updateStart = stage5SerializerSource.indexOf('using var update = scene.BeginUpdate()', restoreStart);
const firstAwait = stage5SerializerSource.indexOf('await ', restoreStart);
if (restoreStart < 0 || updateStart < 0 || firstAwait < 0 || updateStart < firstAwait) {
  errors.push('Core/Serialization/SceneSerializer3D.cs: scene mutation transaction must begin only after asynchronous asset loading completes.');
}

const assetManagerSource = fs.readFileSync(path.join(root, 'Core/Assets/Streaming/AssetManager3D.cs'), 'utf8');
if (assetManagerSource.includes('existing.State = AssetResidencyState3D.Queued') && assetManagerSource.includes('existing.State == AssetResidencyState3D.Loading && priority > existing.Priority')) {
  errors.push('Core/Assets/Streaming/AssetManager3D.cs: a loading request can be re-queued during priority promotion.');
}
if (!assetManagerSource.includes('PinCount = checked(item.Entry.PinCount + item.Entry.ReservedLeaseCount)')) {
  errors.push('Core/Assets/Streaming/AssetManager3D.cs: reserved leases must become pins before load completion is published.');
}

for (const forbidden of ['if (Path.IsPathRooted(', '= Path.GetFullPath(']) {
  if (assetManagerSource.includes(forbidden)) {
    errors.push(`Core/Assets/Streaming/AssetManager3D.cs: '${forbidden}' must use global::System.IO.Path to avoid namespace/import regressions.`);
  }
}


const openGlRhiSource = fs.readFileSync(path.join(root, 'OpenGL/Rendering/OpenGlSceneRenderer.Rhi.cs'), 'utf8');
if (openGlRhiSource.includes('ApplyCullMode(gl, CullMode.None)')) {
  errors.push('OpenGL/Rendering/OpenGlSceneRenderer.Rhi.cs: CullMode must be namespace-qualified in the RHI partial.');
}

const gpuParticlePipelineSource = fs.readFileSync(path.join(root, 'Core/Rendering/GpuDriven/GpuParticlePipeline3D.cs'), 'utf8');
if (gpuParticlePipelineSource.includes('private static GpuParticleEmitterRecord3D BuildEmitter(')) {
  errors.push('Core/Rendering/GpuDriven/GpuParticlePipeline3D.cs: BuildEmitter reads instance layout state and must not be static.');
}

const selfTestSource = fs.readFileSync(path.join(root, 'Core/Diagnostics/Avalonia3DSelfTestRunner.cs'), 'utf8');
if (selfTestSource.includes('new Box3D { Size =')) {
  errors.push('Core/Diagnostics/Avalonia3DSelfTestRunner.cs: Box3D has Width/Height/Depth properties, not Size.');
}


const finalHardeningContracts = new Map([
  ['Core/Hosting/Engine3D.cs', ['IDisposable, IAsyncDisposable', 'public Task ShutdownCompletion', 'Task.WhenAll(', 'await ShutdownCompletion.ConfigureAwait(false)']],
  ['Core/Assets/Streaming/AssetManager3D.cs', ['internal Task ShutdownCompletion', '_workersCompletion', '_backgroundOperations == 0', 'Volatile.Read(ref _synchronizationDisposed) != 0']],
  ['Core/Assets/Streaming/TextureStreaming3D.cs', ['internal Task ShutdownCompletion', '_shutdownCompletion.TrySetResult(true)']],
  ['Core/Interaction/GpuPicking3D.cs', ['internal Task ShutdownCompletion', '_shutdownCompletion.TrySetResult(true)']],
  ['Core/Assets/Streaming/ContentAddressedAssetCache3D.cs', ['VerifyPersistentFileAsync', 'TryDeleteCorruptPersistentFile', 'exactly 64 hexadecimal characters']],
  ['Core/Materials/MaterialShaderExtension3D.cs', ['IncrementalHash.CreateHash', 'HashAlgorithmName.SHA256', 'Convert.ToHexString']],
  ['Core/Diagnostics/EngineProfiler3D.cs', ['bool GpuTimingAvailable', 'stats.GpuTimingAvailable ? stats.GpuFrameMilliseconds : 0d', 'CountInvalidMetrics']],
  ['Core/Spatial/SpatialHashGrid3D.cs', ['CellSize cannot change while objects are indexed', 'public int Version', 'IncrementVersion()']],
  ['Core/Serialization/SceneSerializer3D.cs', ['material extension id contains surrounding whitespace', 'extensionTextures: false', 'extensionTextures: true']]
]);
for (const [relative, requiredValues] of finalHardeningContracts) {
  const source = fs.readFileSync(path.join(root, relative), 'utf8');
  for (const required of requiredValues) {
    if (!source.includes(required)) errors.push(`${relative}: final hardening contract '${required}' is missing.`);
  }
}

for (const relative of ['Core/Materials/MaterialShaderExtension3D.cs', 'Core/Rendering/ShaderProgramDescriptor3D.cs']) {
  const source = fs.readFileSync(path.join(root, relative), 'utf8');
  if (/ResourceKey[\s\S]{0,600}GetHashCode\s*\(/.test(source) || /Identity[\s\S]{0,600}new\s+HashCode\s*\(/.test(source)) {
    errors.push(`${relative}: persistent shader/material identity must not depend on randomized process-local hash codes.`);
  }
}

const guardedPrograms = new Map([
  ['Tools/ApiSnapshot/Program.cs', 'AVALONIA3D_API_SNAPSHOT_TOOL'],
  ['Tests/Program.cs', 'AVALONIA3D_TEST_HOST'],
  ['Benchmarks/Program.cs', 'AVALONIA3D_BENCHMARK_HOST']
]);
for (const [relative, symbol] of guardedPrograms) {
  const source = fs.readFileSync(path.join(root, relative), 'utf8').trimStart();
  if (!source.startsWith(`#if ${symbol}`) || !source.trimEnd().endsWith('#endif')) {
    errors.push(`${relative}: source-drop guard ${symbol} is missing or incomplete.`);
  }
}

if (errors.length > 0) {
  console.error('C# source validation failed:');
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log(`C# source validation passed: ${walk(root).length} files.`);
