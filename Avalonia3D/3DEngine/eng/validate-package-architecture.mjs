import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const requiredProjects = [
  'Avalonia3D.Core.csproj',
  'Avalonia3D.Assets.Gltf.csproj',
  'Avalonia3D.Physics.Jitter2.csproj',
  'Avalonia3D.Avalonia.csproj',
  'Avalonia3D.OpenGL.csproj',
  'Avalonia3D.WebGL.csproj',
  'Avalonia3D.Editor.csproj',
  'Avalonia3D.Engine.csproj'
];
for (const project of requiredProjects) {
  if (!fs.existsSync(path.join(root, project))) throw new Error(`Missing package project: ${project}`);
}

const buildProperties = read('Directory.Build.props');
for (const requiredProperty of ['BaseOutputPath', 'BaseIntermediateOutputPath', 'NuGetLockFilePath']) {
  if (!buildProperties.includes(`<${requiredProperty}>`) || !buildProperties.includes('$(MSBuildProjectName)')) {
    throw new Error(`Directory.Build.props must isolate ${requiredProperty} per project.`);
  }
}

const coreProject = read('Avalonia3D.Core.csproj');
if (coreProject.includes('<PackageReference Include="Avalonia"') || coreProject.includes('<PackageReference Include="Jitter2"')) {
  throw new Error('Avalonia3D.Core must not depend on Avalonia or Jitter2 packages.');
}

for (const testOnlySource of [
  'Core/Diagnostics/Avalonia3DSelfTestRunner.cs',
  'Core/Diagnostics/Avalonia3DSelfTestResult.cs'
]) {
  if (!coreProject.includes(testOnlySource)) {
    throw new Error(`${testOnlySource} must be excluded from Avalonia3D.Core.`);
  }
}
const diagnosticReport = read('Core/Diagnostics/EngineDiagnosticReport3D.cs');
if (diagnosticReport.includes('Avalonia3DSelfTest')) {
  throw new Error('Production diagnostic reports must not depend on the test-only self-test host.');
}

const aggregate = read('Avalonia3D.Engine.csproj');
if (!aggregate.includes('Compatibility/**/*.cs') || aggregate.includes('Core/**/*.cs') || aggregate.includes('Avalonia/**/*.cs')) {
  throw new Error('The aggregate project must compile only compatibility/facade source.');
}
const coreAssemblyInfo = read('Core/Properties/AssemblyInfo.cs');
if (!coreAssemblyInfo.includes('InternalsVisibleTo("Avalonia3D.Engine")')) {
  throw new Error('Avalonia3D.Core must expose the internal default-stack registration hook to the aggregate facade.');
}

const forbiddenCoreReferences = [
  ['ThreeDEngine.Avalonia', 'Avalonia runtime namespace'],
  ['using Avalonia', 'Avalonia package'],
  ['global::Jitter2', 'Jitter2 package']
];
const excludedCorePrefixes = [
  'Core/Importers/Gltf/',
  'Core/Physics/Jitter2/',
  'Core/Preview/',
  'Core/Demos/'
];
const ignoredWalkDirectories = new Set(['Artifacts', 'bin', 'obj', '.git', '.vs']);
function walk(directory) {
  const out = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (!ignoredWalkDirectories.has(entry.name)) out.push(...walk(full));
    } else {
      out.push(full);
    }
  }
  return out;
}
for (const file of walk(path.join(root, 'Core')).filter(file => file.endsWith('.cs'))) {
  const relative = path.relative(root, file).replaceAll('\\', '/');
  if (excludedCorePrefixes.some(prefix => relative.startsWith(prefix)) ||
      relative === 'Core/Assets/Models/ModelAssetCache3D.cs' ||
      relative === 'Core/Diagnostics/Avalonia3DSelfTestRunner.cs' ||
      relative === 'Core/Diagnostics/Avalonia3DSelfTestResult.cs') continue;
  const text = fs.readFileSync(file, 'utf8');
  for (const [needle, description] of forbiddenCoreReferences) {
    if (text.includes(needle)) throw new Error(`${relative} reintroduced a ${description} dependency into Core.`);
  }
}

const forbiddenExports = [
  ['OpenGL/OpenGlScenePresenterFactory.cs', 'public sealed class OpenGlScenePresenterFactory'],
  ['OpenGL/Controls/OpenGlScenePresenter.cs', 'public sealed class OpenGlScenePresenter'],
  ['WebGL/WebGlScenePresenterFactory.cs', 'public sealed class WebGlScenePresenterFactory'],
  ['WebGL/Controls/WebGlScenePresenter.cs', 'public sealed class WebGlScenePresenter'],
  ['Core/Rendering/SceneRenderPlan3D.cs', 'public sealed class SceneRenderPlan3D'],
  ['Core/Rendering/Rhi/RhiDevice3D.cs', 'public sealed class RhiDevice3D'],
  ['Core/Rendering/Rhi/RhiResourceRegistry3D.cs', 'public sealed class RhiResourceRegistry3D'],
  ['Core/Rendering/MaterialBinding3D.cs', 'public readonly struct MaterialBinding3D'],
  ['Core/Rendering/OrdinaryRenderItem3D.cs', 'public readonly struct OrdinaryRenderItem3D'],
  ['Core/Rendering/ParticleRenderItem3D.cs', 'public readonly struct ParticleRenderItem3D'],
  ['Core/Rendering/RenderBatchKey.cs', 'public readonly struct RenderBatchKey'],
  ['Core/Rendering/RenderTextureResource3D.cs', 'public readonly struct RenderTextureResource3D'],
  ['Core/Rendering/RendererResourceKey.cs', 'public readonly struct RendererResourceKey'],
  ['Core/Rendering/ShaderProgramDescriptor3D.cs', 'public readonly struct ShaderProgramDescriptor3D'],
  ['Core/Rendering/TransparentOrdinaryRenderItem3D.cs', 'public readonly struct TransparentOrdinaryRenderItem3D']
];
for (const [file, declaration] of forbiddenExports) {
  if (read(file).includes(declaration)) throw new Error(`${file} exports implementation type '${declaration}'.`);
}


const normalizePath = value => value.replaceAll('\\', '/').replace(/^\.\//, '');
const expectedSolutionProjects = [
  ...requiredProjects,
  'Tests/Avalonia3D.Engine.Tests.csproj',
  'Benchmarks/Avalonia3D.Engine.Benchmarks.csproj',
  'Tools/ApiSnapshot/Avalonia3D.Engine.ApiSnapshot.csproj'
];
const solution = read('Avalonia3D.Engine.sln');
const solutionProjects = [...solution.matchAll(/"([^"]+\.csproj)"/g)]
  .map(match => normalizePath(match[1]))
  .sort();
const expectedSorted = [...expectedSolutionProjects].sort();
if (JSON.stringify(solutionProjects) !== JSON.stringify(expectedSorted)) {
  throw new Error(`Solution project set mismatch. Expected ${expectedSorted.join(', ')}; actual ${solutionProjects.join(', ')}.`);
}

const projectGraph = new Map();
for (const relativeProject of expectedSolutionProjects) {
  const projectPath = path.join(root, relativeProject);
  if (!fs.existsSync(projectPath)) throw new Error(`Missing solution project: ${relativeProject}`);
  const projectDirectory = path.dirname(projectPath);
  const references = [...fs.readFileSync(projectPath, 'utf8').matchAll(/<ProjectReference\s+Include="([^"]+)"/g)]
    .map(match => normalizePath(path.relative(root, path.resolve(projectDirectory, match[1]))));
  projectGraph.set(relativeProject, references);
}
const visitState = new Map();
function visitProject(project, chain = []) {
  const state = visitState.get(project) ?? 0;
  if (state === 1) throw new Error(`Project-reference cycle: ${[...chain, project].join(' -> ')}`);
  if (state === 2) return;
  visitState.set(project, 1);
  for (const dependency of projectGraph.get(project) ?? []) {
    if (!projectGraph.has(dependency)) throw new Error(`${project} references missing project ${dependency}.`);
    visitProject(dependency, [...chain, project]);
  }
  visitState.set(project, 2);
}
for (const project of projectGraph.keys()) visitProject(project);

function globExpression(pattern) {
  pattern = normalizePath(pattern);
  let expression = '^';
  for (let index = 0; index < pattern.length;) {
    if (pattern.startsWith('**/', index)) {
      expression += '(?:.*/)?';
      index += 3;
    } else if (pattern.startsWith('**', index)) {
      expression += '.*';
      index += 2;
    } else if (pattern[index] === '*') {
      expression += '[^/]*';
      index += 1;
    } else if (pattern[index] === '?') {
      expression += '[^/]';
      index += 1;
    } else {
      expression += pattern[index].replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      index += 1;
    }
  }
  return new RegExp(expression + '$');
}
function compilePatterns(project) {
  const projectSource = read(project);
  const patterns = [];
  for (const match of projectSource.matchAll(/<Compile\s+([^>]*?)(?:\/?>)/gs)) {
    const attributes = match[1];
    const include = /\bInclude="([^"]+)"/.exec(attributes)?.[1];
    if (!include) continue;
    const excludes = (/\bExclude="([^"]+)"/.exec(attributes)?.[1] ?? '')
      .split(';').filter(Boolean).map(globExpression);
    patterns.push({ include: globExpression(include), excludes });
  }
  return patterns;
}
const packageCompilePatterns = new Map(requiredProjects.map(project => [project, compilePatterns(project)]));
const runtimePrefixes = ['Core/', 'Avalonia/', 'OpenGL/', 'WebGL/', 'Compatibility/', 'TestControls/'];
const testOnlyRuntimeSources = new Set([
  'Core/Diagnostics/Avalonia3DSelfTestRunner.cs',
  'Core/Diagnostics/Avalonia3DSelfTestResult.cs'
]);
const runtimeSources = walk(root)
  .filter(file => file.endsWith('.cs'))
  .map(file => normalizePath(path.relative(root, file)))
  .filter(file => runtimePrefixes.some(prefix => file.startsWith(prefix)));
for (const source of runtimeSources) {
  const owners = [];
  for (const [project, patterns] of packageCompilePatterns) {
    if (patterns.some(pattern => pattern.include.test(source) && !pattern.excludes.some(exclude => exclude.test(source)))) {
      owners.push(project);
    }
  }
  if (testOnlyRuntimeSources.has(source)) {
    if (owners.length !== 0) throw new Error(`Test-only source ${source} is compiled by runtime package(s): ${owners.join(', ')}.`);
  } else if (owners.length !== 1) {
    throw new Error(`Runtime source ${source} must have exactly one package owner; found [${owners.join(', ')}].`);
  }
}

const sceneSource = read('Core/Scene/Scene3D.cs');
const defaultStackSource = read('Core/Hosting/Engine3DDefaultStack3D.cs');
const compatibilityBootstrapSource = read('Compatibility/Engine3DApplication3D.cs');
if (!sceneSource.includes('Engine3D.CreateDefault()') ||
    !defaultStackSource.includes('[DynamicDependency(') ||
    !defaultStackSource.includes('Register(Func<Engine3D> factory)') ||
    !defaultStackSource.includes('Volatile.Read(ref s_registeredFactory)')) {
  throw new Error('Legacy parameterless scene construction is not isolated behind the registered, trim-safe compatibility bridge.');
}
if (!compatibilityBootstrapSource.includes('[ModuleInitializer]') ||
    !compatibilityBootstrapSource.includes('Engine3DDefaultStack3D.Register(Engine3DApplication3D.CreateDefaultEngine)') ||
    !compatibilityBootstrapSource.includes('#if AVALONIA3D_ENGINE_AGGREGATE') ||
    !compatibilityBootstrapSource.includes('OperatingSystem.IsBrowser()') ||
    !compatibilityBootstrapSource.includes('builder.UseWebGl()') ||
    !compatibilityBootstrapSource.includes('builder.UseOpenGl()')) {
  throw new Error('The aggregate/source-drop compatibility module does not register and select the platform default stack correctly.');
}
const previewSource = read('Avalonia/Preview/Scene3DPreviewControl.cs');
if (!previewSource.includes('UserControl, IDisposable') || !previewSource.includes(': this(new Scene3DControl())') || !previewSource.includes('_viewport.Dispose();')) {
  throw new Error('The parameterless preview control does not transfer and release aggregate engine ownership through its viewport.');
}
const baselineControlSource = read('TestControls/PerformanceBaselineControl3D.cs');
if (!baselineControlSource.includes('Grid, IDisposable') || !baselineControlSource.includes('_sceneControl.Dispose();')) {
  throw new Error('The performance test control does not release its scene viewport and owned compatibility engine.');
}

const projectText = requiredProjects.map(project => read(project)).join('\n');
for (const document of ['PACKAGES.md', 'PUBLIC_API.md']) {
  if (!fs.existsSync(path.join(root, document))) throw new Error(`Missing architecture document: ${document}`);
}
if (!projectText.includes('Avalonia3D.Editor')) throw new Error('Editor package is absent from the project graph.');

const bashBuild = read('build.sh');
const powershellBuild = read('build.ps1');
for (const project of requiredProjects) {
  if (!bashBuild.includes(project)) throw new Error(`build.sh does not pack ${project}.`);
  if (!powershellBuild.includes(project)) throw new Error(`build.ps1 does not pack ${project}.`);
}
const lockRefresh = read('eng/update-lock-files.ps1');
if (!lockRefresh.includes('$projects = @(') || !lockRefresh.includes('$projects.Count')) {
  throw new Error('PowerShell lock refresh does not calculate the project count.');
}


const apiSnapshotSource = read('Tools/ApiSnapshot/Program.cs');
const apiSnapshotProject = read('Tools/ApiSnapshot/Avalonia3D.Engine.ApiSnapshot.csproj');
if (!apiSnapshotSource.startsWith('#if AVALONIA3D_API_SNAPSHOT_TOOL') ||
    !apiSnapshotSource.trimEnd().endsWith('#endif') ||
    !apiSnapshotProject.includes('AVALONIA3D_API_SNAPSHOT_TOOL')) {
  throw new Error('ApiSnapshot tool entry point must be isolated from host-project wildcard compilation.');
}
for (const presenter of ['OpenGL/Controls/OpenGlScenePresenter.cs', 'WebGL/Controls/WebGlScenePresenter.cs']) {
  if (read(presenter).includes('??= throw')) {
    throw new Error(`${presenter} uses an invalid null-coalescing throw assignment.`);
  }
}
const rigidbodySource = read('Core/Physics/Rigidbody3D.cs');
const jitterSource = read('Core/Physics/Jitter2/Jitter2PhysicsCore.cs');
for (const property of ['RollingFriction', 'RollingRadius', 'CollisionTorqueScale']) {
  if (!rigidbodySource.includes(`public float ${property}`) || !jitterSource.includes(`rb.${property}`)) {
    throw new Error(`${property} must be both public and consumed by the Jitter2 backend.`);
  }
}

console.log('Package architecture validation passed.');
