#!/usr/bin/env node
'use strict';

// Parse source as data. This tool never evaluates/imports the inspected code.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const babelRoot = path.resolve(__dirname, '../.tools/gdz-static/node_modules/@babel');
const parser = require(path.join(babelRoot, 'parser'));
const traverse = require(path.join(babelRoot, 'traverse')).default;

function argument(name, fallback) {
  const position = process.argv.indexOf(name);
  if (position < 0) return fallback;
  if (!process.argv[position + 1] || process.argv[position + 1].startsWith('--')) {
    throw new Error(`Missing value for ${name}`);
  }
  return path.resolve(process.argv[position + 1]);
}
const root = argument('--root', path.resolve(__dirname, '../artifacts/private/gdz-analysis/extracted'));
const output = argument('--output', path.resolve(__dirname, '../artifacts/private/gdz-analysis/deep/function-index.json'));
const readableMain = argument('--main', null);
if (fs.existsSync(output)) throw new Error(`Output already exists: ${output}`);

function sha256(value) { return crypto.createHash('sha256').update(value).digest('hex'); }
function unquoteMember(node) {
  if (!node) return null;
  if (node.type === 'Identifier') return node.name;
  if (node.type === 'ThisExpression') return 'this';
  if (node.type === 'StringLiteral' || node.type === 'NumericLiteral') return String(node.value);
  if (node.type === 'MemberExpression' || node.type === 'OptionalMemberExpression') {
    const object = unquoteMember(node.object);
    const property = unquoteMember(node.property);
    return object && property ? `${object}.${property}` : null;
  }
  return null;
}
function literal(node) {
  if (!node) return null;
  if (['StringLiteral', 'NumericLiteral', 'BooleanLiteral'].includes(node.type)) return node.value;
  if (node.type === 'NullLiteral') return null;
  if (node.type === 'ObjectExpression') return { objectKeys: node.properties.map(p => unquoteMember(p.key)).filter(Boolean) };
  if (node.type === 'ArrayExpression') return { arrayLength: node.elements.length };
  return { expressionType: node.type };
}
function inferredName(p) {
  const n = p.node;
  if (n.id?.name) return n.id.name;
  if (n.key) return unquoteMember(n.key) || '(computed method)';
  const parent = p.parentPath?.node;
  if (parent?.type === 'VariableDeclarator') return unquoteMember(parent.id) || '(destructured variable)';
  if (parent?.type === 'AssignmentExpression') return unquoteMember(parent.left) || '(assignment)';
  if (parent?.type === 'ObjectProperty') return unquoteMember(parent.key) || '(computed property)';
  if (parent?.type === 'CallExpression') {
    const callee = unquoteMember(parent.callee) || '(computed call)';
    const first = literal(parent.arguments[0]);
    return `callback:${callee}${typeof first === 'string' ? ':' + first : ''}`;
  }
  return '(anonymous)';
}
function listFiles(directory) {
  const result = [];
  for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
    if (item.name === 'node_modules' || item.isSymbolicLink()) continue;
    const full = path.join(directory, item.name);
    if (item.isDirectory()) result.push(...listFiles(full));
    else if (item.isFile() && /\.(?:js|cjs|mjs|html)$/i.test(item.name)) result.push(full);
  }
  return result.sort();
}
function sourceSegments(file, source) {
  if (!/\.html$/i.test(file)) return [{ content: source, base: 0, firstLine: 1 }];
  const result = [];
  const re = /<script\b([^>]*)>([\s\S]*?)<\/script\s*>/gi;
  for (const match of source.matchAll(re)) {
    if (/\bsrc\s*=/i.test(match[1])) continue;
    const type = /\btype\s*=\s*["']([^"']+)/i.exec(match[1])?.[1];
    if (type && !/^(?:module|(?:text|application)\/javascript)$/i.test(type)) continue;
    const base = match.index + match[0].indexOf('>') + 1;
    result.push({ content: match[2], base, firstLine: 1 + (source.slice(0, base).match(/\n/g) || []).length });
  }
  return result;
}

const entries = [];
const files = [];
const ipc = [];
const errors = [];
const inputs = listFiles(root).map(file => ({ file, label: path.relative(root, file).replaceAll('\\', '/') }));
if (readableMain) inputs.push({ file: readableMain, label: 'decoded/app-main.js' });
for (const input of inputs) {
  const source = fs.readFileSync(input.file, 'utf8');
  const fileRecord = { file: input.label, sha256: sha256(source), bytes: Buffer.byteLength(source), functions: 0, scriptBlocks: 0 };
  for (const segment of sourceSegments(input.file, source)) {
    fileRecord.scriptBlocks++;
    let ast;
    try {
      ast = parser.parse(segment.content, { sourceType: 'unambiguous', allowReturnOutsideFunction: true });
    } catch (error) {
      errors.push({ file: input.label, block: fileRecord.scriptBlocks, line: segment.firstLine + (error.loc?.line || 1) - 1, message: error.message });
      continue;
    }
    traverse(ast, {
      CallExpression(p) {
        const callee = unquoteMember(p.node.callee);
        if (!callee || !/(?:^|\.)(?:ipcMain|ipcRenderer)\.(?:handle|on|send|invoke)$/.test(callee) && !/^window\.electron\.(?:send|invoke|on)$/.test(callee)) return;
        ipc.push({ file: input.label, line: segment.firstLine + p.node.loc.start.line - 1, callee, channel: literal(p.node.arguments[0]), arguments: p.node.arguments.slice(1).map(literal) });
      },
      Function(p) {
        const node = p.node;
        const name = inferredName(p);
        const calls = new Map();
        const returns = new Set();
        const throws = new Set();
        const writes = new Set();
        p.traverse({
          Function(child) { child.skip(); },
          CallExpression(child) {
            const callee = unquoteMember(child.node.callee) || '(computed call)';
            calls.set(callee, (calls.get(callee) || 0) + 1);
          },
          ReturnStatement(child) { returns.add(child.node.argument?.type || 'undefined'); },
          ThrowStatement(child) { throws.add(child.node.argument?.type || 'unknown'); },
          AssignmentExpression(child) {
            const target = unquoteMember(child.node.left);
            if (target) writes.add(target);
          },
        });
        const body = node.body?.body || [];
        const simpleWrapper = body.length === 1 && body[0].type === 'ReturnStatement';
        entries.push({
          id: `${input.label}:${segment.base + node.start}`,
          file: input.label,
          name,
          explicitlyNamed: Boolean(node.id?.name),
          obfuscatedName: /^_0x[\da-f]+$/i.test(name),
          kind: node.type,
          async: Boolean(node.async), generator: Boolean(node.generator),
          parameters: node.params.map(n => segment.content.slice(n.start, n.end)),
          line: segment.firstLine + node.loc.start.line - 1,
          endLine: segment.firstLine + node.loc.end.line - 1,
          offset: segment.base + node.start,
          endOffset: segment.base + node.end,
          sourceSha256: sha256(segment.content.slice(node.start, node.end)),
          structure: simpleWrapper ? 'single_return_wrapper' : node.type === 'ArrowFunctionExpression' && node.body.type !== 'BlockStatement' ? 'expression_arrow' : 'block_function',
          directCalls: Array.from(calls, ([callee, count]) => ({ callee, count })),
          returnExpressionKinds: Array.from(returns),
          throwExpressionKinds: Array.from(throws),
          assignedTargets: Array.from(writes),
          interpretation: 'Structural inventory only; calls inside nested functions are indexed separately. No source was executed.',
        });
        fileRecord.functions++;
      },
    });
  }
  files.push(fileRecord);
}
const mainFunctions = entries.filter(e => e.file === 'app-main.js');
const summary = {
  sourceFiles: files.length,
  functions: entries.length,
  originalMainFunctions: mainFunctions.length,
  originalMainReadableNamedFunctions: mainFunctions.filter(e => e.explicitlyNamed && !e.obfuscatedName).length,
  originalMainObfuscatedNamedFunctions: mainFunctions.filter(e => e.explicitlyNamed && e.obfuscatedName).length,
  originalMainSingleReturnWrappers: mainFunctions.filter(e => e.structure === 'single_return_wrapper').length,
  ipcCallSites: ipc.length,
  parseErrors: errors.length,
};
const document = { schemaVersion: 1, createdAtUtc: new Date().toISOString(), root, readableMain, summary, files, errors, ipc, functions: entries };
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, JSON.stringify(document, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ output, ...summary }));
if (errors.length) process.exitCode = 1;
