#!/usr/bin/env node

import { access, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import process from 'node:process';

const root = resolve(import.meta.dirname, '..');
const errors = [];

async function text(path) {
  return readFile(resolve(root, path), 'utf8');
}

async function json(path) {
  try {
    return JSON.parse(await text(path));
  } catch (error) {
    errors.push(`${path}: invalid JSON (${error.message}).`);
    return null;
  }
}

const buildProps = await text('Directory.Build.props');
const version = /<Avalonia3DEngineVersion>([^<]+)<\/Avalonia3DEngineVersion>/.exec(buildProps)?.[1];
if (!version) errors.push('Directory.Build.props: Avalonia3DEngineVersion is missing.');

const globalJson = await json('global.json');
if (!globalJson?.sdk?.version || globalJson.sdk.rollForward !== 'disable') {
  errors.push('global.json must pin an exact SDK and disable roll-forward.');
}

const nodeVersion = (await text('.node-version')).trim();
const nvmVersion = (await text('.nvmrc')).trim();
if (!nodeVersion || nodeVersion !== nvmVersion) errors.push('.node-version and .nvmrc must contain the same exact version.');

const policy = await json('Baselines/baseline-policy.json');
const manifest = await json('Baselines/baseline-manifest.json');
const provenance = await json('Baselines/source-provenance.json');
await json('Baselines/runtime-baseline.schema.json');
if (policy && policy.engineVersion !== version) errors.push('Baseline policy engineVersion does not match Directory.Build.props.');
if (manifest && manifest.engineVersion !== version) errors.push('Baseline manifest engineVersion does not match Directory.Build.props.');
if (!provenance?.selectedBaseline?.sha256 || !/^[0-9a-f]{64}$/i.test(provenance.selectedBaseline.sha256)) {
  errors.push('Source provenance selected baseline SHA-256 is missing or invalid.');
}
if (!provenance?.comparisonBaseline?.sha256 || !/^[0-9a-f]{64}$/i.test(provenance.comparisonBaseline.sha256)) {
  errors.push('Source provenance comparison baseline SHA-256 is missing or invalid.');
}

const benchmarkSource = await text('Benchmarks/Program.cs');
const workloadIds = [...benchmarkSource.matchAll(/Measure\s*\(\s*"([^"]+)"/g)].map(match => match[1]);
const uniqueWorkloads = new Set(workloadIds);
if (uniqueWorkloads.size !== workloadIds.length) errors.push('Benchmarks/Program.cs contains duplicate workload IDs.');
const requiredWorkloads = policy?.requiredWorkloads ?? [];
const requiredSet = new Set(requiredWorkloads);
if (requiredSet.size !== requiredWorkloads.length) errors.push('Baseline policy contains duplicate requiredWorkloads.');
const missingInPolicy = [...uniqueWorkloads].filter(id => !requiredSet.has(id));
const missingInCode = [...requiredSet].filter(id => !uniqueWorkloads.has(id));
if (missingInPolicy.length || missingInCode.length) {
  errors.push(`Benchmark/policy workload mismatch. Missing in policy: [${missingInPolicy.join(', ')}]; missing in code: [${missingInCode.join(', ')}].`);
}

const solution = await text('Avalonia3D.Engine.sln');
const projectPaths = [...solution.matchAll(/Project\("\{[^}]+\}"\) = "[^"]+", "([^"]+)", "\{[^}]+\}"/g)]
  .map(match => match[1].replaceAll('\\', '/'));
if (projectPaths.length !== 11) errors.push(`Avalonia3D.Engine.sln must contain 11 modular projects; found ${projectPaths.length}.`);
for (const projectPath of projectPaths) {
  try {
    await access(resolve(root, projectPath));
  } catch {
    errors.push(`Solution project is missing: ${projectPath}.`);
  }
}

for (const documentationPath of ['README.md', 'CHANGELOG.md', 'DEVELOPMENT_STATE.md', 'PERFORMANCE.md', 'PACKAGES.md', 'PUBLIC_API.md']) {
  if (!(await text(documentationPath)).includes(version ?? '__missing_version__')) {
    errors.push(`${documentationPath} does not contain current engine version ${version}.`);
  }
}

if (manifest?.captures) {
  for (const capture of manifest.captures) {
    if (capture.status === 'pending-real-device-capture' && capture.path !== null) {
      errors.push(`Pending capture ${capture.platform}/${capture.backend} must have null path.`);
    }
  }
}

if (errors.length > 0) {
  console.error('Baseline contract validation failed:');
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log(`Baseline contract valid: engine ${version}, SDK ${globalJson.sdk.version}, Node.js ${nodeVersion}, ${workloadIds.length} workloads.`);
