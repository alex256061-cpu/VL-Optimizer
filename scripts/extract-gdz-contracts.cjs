#!/usr/bin/env node
'use strict';
// Recover literal data only. No eval, vm, target imports, PowerShell or native loading.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const parser = require(path.resolve(__dirname, '../.tools/gdz-static/node_modules/@babel/parser'));
const root = path.resolve(__dirname, '..');
const sourcePath = path.join(root, 'artifacts/private/gdz-analysis/deep/backend-focused.js');
const outputPath = path.join(root, 'artifacts/private/gdz-analysis/deep/embedded-contracts.json');
const source = fs.readFileSync(sourcePath, 'utf8');
const ast = parser.parse(source, {sourceType: 'unambiguous'});
const hash = value => crypto.createHash('sha256').update(value).digest('hex');
const definitions = new Map();
const named = new Set(['MEM_LAYOUT_PS', 'STANDBY_PS', 'RES_DLL', 'BOTTLENECK_PS', 'BN_FIND_PS',
  'GAME_PS_HEAD', 'GAMES_SCAN_PS', 'CLEAN_TAIL', 'SHADER_CLEAN_PS', 'RAM_CLEAN_PS',
  'STEAM_VDF_WRITE', 'DRS_PS', 'SNAPSHOT_PS', 'RESTORE_PS', 'DISK_CS', 'CLEAN_PS_HEAD',
  'SENSOR_PS', 'PCINFO_PS', 'PCLIVE_PS', 'FIVEM_SCALING', 'FIVEM_PRESETS', 'FIVEM_KEYS',
  'RAM_INTERVALS', 'AUTO_APPS_LOW', 'AUTO_APPS_CLOSE', 'FPS_REC_SECS', 'OPTI_DLLS',
  'OPTI_OPTS_DEFAULT', 'OPTI_MENU_KEYS', 'OPTI_QUIRKS', 'MEM_GEN', 'MEM_MAKER_ID']);
function walk(node, cb, ancestors = []) {
  if (!node || typeof node.type !== 'string') return;
  cb(node, ancestors);
  for (const [key, value] of Object.entries(node)) {
    if (['loc', 'start', 'end', 'extra', 'tokens', 'comments'].includes(key)) continue;
    if (Array.isArray(value)) value.forEach(v => walk(v, cb, [...ancestors, node]));
    else if (value && typeof value.type === 'string') walk(value, cb, [...ancestors, node]);
  }
}
walk(ast.program, node => {
  if (node.type === 'VariableDeclarator' && node.id.type === 'Identifier' && named.has(node.id.name)) {
    if (definitions.has(node.id.name)) throw new Error('Ambiguous name: ' + node.id.name);
    definitions.set(node.id.name, node);
  }
});
const cache = new Map();
function prop(node) { return node?.type === 'Identifier' ? node.name : node?.value; }
function literal(node, stack = new Set()) {
  if (!node) throw new Error('Empty expression');
  if (['StringLiteral', 'NumericLiteral', 'BooleanLiteral'].includes(node.type)) return node.value;
  if (node.type === 'NullLiteral') return null;
  if (node.type === 'Identifier' && definitions.has(node.name)) {
    if (stack.has(node.name)) throw new Error('Cycle');
    if (!cache.has(node.name)) cache.set(node.name, literal(definitions.get(node.name).init, new Set([...stack, node.name])));
    return cache.get(node.name);
  }
  if (node.type === 'ArrayExpression' && node.elements.every(Boolean)) return node.elements.map(n => literal(n, stack));
  if (node.type === 'ObjectExpression') {
    const result = Object.create(null);
    for (const p of node.properties) {
      if (p.type !== 'ObjectProperty' || p.computed) throw new Error('Not a literal object');
      result[prop(p.key)] = literal(p.value, stack);
    }
    return result;
  }
  if (node.type === 'TemplateLiteral' && node.expressions.length === 0) return node.quasis[0].value.cooked;
  if (node.type === 'TaggedTemplateExpression' && node.tag.type === 'MemberExpression' &&
      prop(node.tag.object) === 'String' && prop(node.tag.property) === 'raw' && node.quasi.expressions.length === 0) {
    return node.quasi.quasis[0].value.raw;
  }
  if (node.type === 'BinaryExpression' && node.operator === '+') {
    const l = literal(node.left, stack), r = literal(node.right, stack);
    if (!['string','number'].includes(typeof l) || !['string','number'].includes(typeof r)) throw new Error('Not a literal addition');
    return l + r;
  }
  if (node.type === 'CallExpression' && node.callee.type === 'MemberExpression' && prop(node.callee.property) === 'join' && node.arguments.length === 1) {
    const a = literal(node.callee.object, stack), sep = literal(node.arguments[0], stack);
    if (!Array.isArray(a) || typeof sep !== 'string' || !a.every(x => typeof x === 'string')) throw new Error('Not a literal join');
    return a.join(sep);
  }
  throw new Error('Unsupported expression: ' + node.type);
}
const constants = [], unresolved = [];
for (const [name, node] of definitions) {
  try {
    const value = literal(node.init);
    constants.push({name, line: node.loc.start.line, endLine: node.loc.end.line,
      sourceStart: node.start, sourceEnd: node.end, value,
      valueSha256: hash(typeof value === 'string' ? value : JSON.stringify(value))});
  } catch (error) { unresolved.push({name, line: node.loc.start.line, reason: error.message}); }
}
const payloadsByHash = new Map();
walk(ast.program, (node, ancestors) => {
  if (!['StringLiteral', 'TemplateLiteral', 'TaggedTemplateExpression'].includes(node.type)) return;
  if (node.type === 'TemplateLiteral' && ancestors.at(-1)?.type === 'TaggedTemplateExpression') return;
  let value;
  try { value = literal(node); } catch { return; }
  if (typeof value !== 'string' || value.length < 100 || !/(?:Get-CimInstance|Add-Type|DllImport|\$(?:env:|ErrorActionPreference|ProgressPreference)|public static|GDZ_(?:SNAPSHOT|BN|JSON))/.test(value)) return;
  const sha256 = hash(value);
  if (!payloadsByHash.has(sha256)) payloadsByHash.set(sha256, {sha256, bytes: Buffer.byteLength(value), value, occurrences: []});
  const fn = [...ancestors].reverse().find(n => /Function/.test(n.type));
  payloadsByHash.get(sha256).occurrences.push({line: node.loc.start.line, endLine: node.loc.end.line,
    sourceStart: node.start, sourceEnd: node.end, function: fn?.id?.name || null});
});
const csharp = [];
for (const c of constants) {
  if (typeof c.value !== 'string' || !/(?:using System;|DllImport)/.test(c.value)) continue;
  const classes = [...c.value.matchAll(/\b(?:public\s+)?(?:static\s+)?class\s+(\w+)/g)].map(x=>x[1]);
  const imports = [...c.value.matchAll(/\[DllImport\("([^"]+)"([^\]]*)\)\]\s*(?:(?:public|private|internal|static|extern)\s+)*([^;]+);/g)]
    .map(x=>({library:x[1],attributes:x[2].trim(),declaration:x[3].trim()}));
  // All examined C# classes are static wrappers. Requiring 'static' excludes
  // PowerShell control words and C# call expressions from this signature index.
  const methods = [...c.value.matchAll(/\b(?:(public|private|internal|protected)\s+)?static\s+(?:extern\s+)?([\w.\[\]<>]+)\s+(\w+)(?:<[^>]+>)?\s*\(([^)]*)\)\s*(?:where\s+[^{};]+)?(?:\{|;)/g)]
    .map(x=>({visibility:x[1]||'implicit',static:true,returns:x[2],name:x[3],parameters:x[4]}));
  csharp.push({constant:c.name,line:c.line,classes,imports,staticMethods:methods});
}
const result = {schema:1,analysisMode:'static-literal-extraction-no-target-execution',
  source:{file:sourcePath,sha256:hash(source)}, counts:{selectedNames:named.size,constants:constants.length,
    unresolved:unresolved.length,uniqueEmbeddedPayloads:payloadsByHash.size,csharpGroups:csharp.length},
  constants,unresolved,csharp,payloads:[...payloadsByHash.values()]};
fs.writeFileSync(outputPath, JSON.stringify(result, null, 2)+'\n', 'utf8');
console.log(JSON.stringify({output:outputPath,counts:result.counts,unresolved},null,2));
