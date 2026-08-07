#!/usr/bin/env node

import { createHash } from 'node:crypto';
import { mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { dirname, relative, resolve, sep } from 'node:path';
import process from 'node:process';

const root = resolve(import.meta.dirname, '..');
const args = process.argv.slice(2);
const checkOnly = args[0] === '--check';
const explicitPath = args[checkOnly ? 1 : 0];
const outputPath = resolve(explicitPath ?? resolve(root, 'Artifacts', 'source-manifest.sha256'));
const excludedDirectories = new Set(['Artifacts', 'bin', 'obj', '.vs', '.git', '.idea']);
const files = [];

async function visit(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  entries.sort((left, right) => left.name.localeCompare(right.name, 'en'));
  for (const entry of entries) {
    const fullPath = resolve(directory, entry.name);
    if (entry.isDirectory()) {
      if (!excludedDirectories.has(entry.name)) await visit(fullPath);
      continue;
    }
    if (entry.isFile()) files.push(fullPath);
  }
}

await visit(root);
const lines = [];
for (const file of files) {
  if (file === outputPath) continue;
  const digest = createHash('sha256').update(await readFile(file)).digest('hex');
  const path = relative(root, file).split(sep).join('/');
  lines.push(`${digest}  ${path}`);
}
lines.sort((left, right) => left.localeCompare(right, 'en'));
const content = `${lines.join('\n')}\n`;

if (checkOnly) {
  let existing;
  try {
    existing = await readFile(outputPath, 'utf8');
  } catch (error) {
    console.error(`Source manifest is missing or unreadable: ${outputPath}`);
    process.exitCode = 1;
    throw error;
  }
  if (existing !== content) {
    console.error(`Source manifest is stale: ${outputPath}. Regenerate it with 'node eng/source-manifest.mjs'.`);
    process.exitCode = 1;
  } else {
    console.log(`Source manifest verified: ${outputPath} (${lines.length} files).`);
  }
} else {
  await mkdir(dirname(outputPath), { recursive: true });
  await writeFile(outputPath, content, 'utf8');
  console.log(`Source manifest: ${outputPath} (${lines.length} files).`);
}
