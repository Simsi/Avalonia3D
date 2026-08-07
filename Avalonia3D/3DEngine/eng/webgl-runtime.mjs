#!/usr/bin/env node

import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import process from 'node:process';

const root = resolve(import.meta.dirname, '..');
const sourcePath = resolve(root, 'WebGL', 'mini3d.webgl.js');
const interopPath = resolve(root, 'WebGL', 'Interop', 'WebGlInterop.cs');
const mode = process.argv.includes('--write') ? 'write' : 'check';
const marker = /private const string EmbeddedModuleBase64\s*=\s*"([^"]*)";/;

const source = await readFile(sourcePath);
const interop = await readFile(interopPath, 'utf8');
const match = marker.exec(interop);
if (!match) {
  throw new Error(`Embedded WebGL runtime marker was not found in ${interopPath}.`);
}

const expected = source.toString('base64');
const actual = match[1];

if (actual === expected) {
  console.log('WebGL runtime source and embedded module are identical.');
  process.exit(0);
}

if (mode === 'check') {
  console.error('WebGL runtime source and embedded module differ. Run: node eng/webgl-runtime.mjs --write');
  process.exit(1);
}

const updated = `${interop.slice(0, match.index)}private const string EmbeddedModuleBase64 = "${expected}";${interop.slice(match.index + match[0].length)}`;
await writeFile(interopPath, updated, 'utf8');
console.log('Updated embedded WebGL runtime from WebGL/mini3d.webgl.js.');
