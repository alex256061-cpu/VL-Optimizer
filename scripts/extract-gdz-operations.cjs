#!/usr/bin/env node
'use strict';
// Parse-only declarative extraction. No target module is imported, evaluated,
// loaded or spawned. The bounded interpreter accepts only the expression forms
// and pure collection operations below; unsupported expressions remain explicit.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const parser = require(path.resolve(__dirname, '../.tools/gdz-static/node_modules/@babel/parser'));
const repositoryRoot = path.resolve(__dirname, '..');
const sourceFile = process.argv[2] ? path.resolve(process.argv[2]) : path.join(repositoryRoot, 'artifacts/private/gdz-analysis/deep/backend-focused.js');
const outputFile = process.argv[3] ? path.resolve(process.argv[3]) : path.join(repositoryRoot, 'artifacts/private/gdz-analysis/deep/operations-declarative.json');
const source = fs.readFileSync(sourceFile, 'utf8');
const ast = parser.parse(source, { sourceType: 'unambiguous' });
const declarations = new Map();
for (const statement of ast.program.body) {
  if (statement.type === 'VariableDeclaration') for (const d of statement.declarations) if (d.id.type === 'Identifier') declarations.set(d.id.name, d.init);
  if (statement.type === 'FunctionDeclaration') declarations.set(statement.id.name, statement);
}
const text = n => source.slice(n.start, n.end);
const evidence = n => ({ file: 'deep/backend-focused.js', line: n.loc.start.line, endLine: n.loc.end.line, start: n.start, end: n.end });
const unresolved = (n, reason) => ({ unresolved: true, reason, expression: text(n), evidence: evidence(n) });
const fn = (n, env) => ({ staticFunction: true, node: n, env });
const isFn = v => v && v.staticFunction === true;
let budget = 0;
function bind(pattern, value, env) {
  if (pattern.type === 'Identifier') env[pattern.name] = value;
  else if (pattern.type === 'ArrayPattern') pattern.elements.forEach((p, i) => p && bind(p, value?.[i], env));
  else if (pattern.type === 'AssignmentPattern') bind(pattern.left, value === undefined ? evaluate(pattern.right, env) : value, env);
  else throw new Error('unsupported binding ' + pattern.type);
}
function invoke(f, args) {
  if (!isFn(f)) throw new Error('only parsed pure function expressions permitted');
  const env = Object.assign(Object.create(null), f.env);
  f.node.params.forEach((p, i) => bind(p, args[i], env));
  if (f.node.body.type !== 'BlockStatement') return evaluate(f.node.body, env);
  const step = statements => {
    for (const s of statements) {
      if (s.type === 'FunctionDeclaration' && /^_0x/.test(s.id.name)) continue;
      if (s.type === 'ReturnStatement') return { returned: true, value: evaluate(s.argument, env) };
      if (s.type === 'VariableDeclaration') { for (const d of s.declarations) bind(d.id, evaluate(d.init, env), env); continue; }
      if (s.type === 'BlockStatement') { const r = step(s.body); if (r) return r; continue; }
      if (s.type === 'IfStatement') { const b = evaluate(s.test, env) ? s.consequent : s.alternate; if (b) { const r = step(b.type === 'BlockStatement' ? b.body : [b]); if (r) return r; } continue; }
      throw new Error('statement outside static subset: ' + s.type);
    }
  };
  return step(f.node.body.body)?.value;
}
function evaluate(n, env = Object.create(null)) {
  if (++budget > 300000) throw new Error('expression budget exceeded');
  if (!n) return undefined;
  switch (n.type) {
    case 'StringLiteral': case 'NumericLiteral': case 'BooleanLiteral': return n.value;
    case 'NullLiteral': return null;
    case 'RegExpLiteral': return { staticRegex: true, pattern: n.pattern, flags: n.flags };
    case 'NewExpression':
      if (n.callee.type === 'Identifier' && n.callee.name === 'Set' && n.arguments.length === 1) return [...new Set(evaluate(n.arguments[0], env))];
      throw new Error('construction outside static subset');
    case 'Identifier':
      if (Object.hasOwn(env, n.name)) return env[n.name];
      if (n.name === 'undefined') return undefined;
      if (!declarations.has(n.name)) throw new Error('unknown identifier ' + n.name);
      return evaluate(declarations.get(n.name), env);
    case 'ArrowFunctionExpression': case 'FunctionExpression': case 'FunctionDeclaration': return fn(n, env);
    case 'ArrayExpression': {
      const a = [];
      for (const e of n.elements) e?.type === 'SpreadElement' ? a.push(...evaluate(e.argument, env)) : a.push(e ? evaluate(e, env) : undefined);
      return a;
    }
    case 'ObjectExpression': {
      const o = Object.create(null);
      for (const p of n.properties) {
        if (p.type === 'SpreadElement') { Object.assign(o, evaluate(p.argument, env)); continue; }
        const k = p.computed ? evaluate(p.key, env) : p.key.name ?? p.key.value;
        o[k] = evaluate(p.value, env);
      }
      return o;
    }
    case 'MemberExpression': {
      const o = evaluate(n.object, env), k = n.computed ? evaluate(n.property, env) : n.property.name;
      if (['__proto__', 'constructor', 'prototype'].includes(k)) throw new Error('prototype access forbidden');
      if (o == null) return undefined;
      if (k === 'length' && (Array.isArray(o) || typeof o === 'string')) return o.length;
      if (Object.hasOwn(Object(o), k)) return o[k];
      // Pure declarative records have normal missing-property semantics; never
      // walk their prototype or expose callable host methods through a read.
      if (typeof o === 'object' && !isFn(o)) return undefined;
      throw new Error('unresolved member ' + k);
    }
    case 'UnaryExpression': {
      const v = evaluate(n.argument, env);
      if (n.operator === '!') return !v;
      if (n.operator === '-') return -v;
      if (n.operator === '+') return +v;
      if (n.operator === 'typeof') return isFn(v) ? 'function' : typeof v;
      throw new Error('unary ' + n.operator);
    }
    case 'LogicalExpression': {
      const l = evaluate(n.left, env);
      return n.operator === '&&' ? l && evaluate(n.right, env) : n.operator === '||' ? l || evaluate(n.right, env) : l ?? evaluate(n.right, env);
    }
    case 'ConditionalExpression': return evaluate(evaluate(n.test, env) ? n.consequent : n.alternate, env);
    case 'BinaryExpression': {
      const l = evaluate(n.left, env), r = evaluate(n.right, env);
      switch (n.operator) {
        case '+': return l + r; case '-': return l - r; case '*': return l * r; case '/': return l / r;
        case '>': return l > r; case '>=': return l >= r; case '<': return l < r; case '<=': return l <= r;
        case '===': return l === r; case '!==': return l !== r; case '==': return l == r; case '!=': return l != r; case '&': return l & r;
        default: throw new Error('binary ' + n.operator);
      }
    }
    case 'TemplateLiteral': return n.quasis.map((q, i) => q.value.cooked + (i < n.expressions.length ? evaluate(n.expressions[i], env) : '')).join('');
    case 'TaggedTemplateExpression':
      if (n.tag.type === 'MemberExpression' && n.tag.object.name === 'String' && (n.tag.property.value ?? n.tag.property.name) === 'raw')
        return n.quasi.quasis.map((q, i) => q.value.raw + (i < n.quasi.expressions.length ? evaluate(n.quasi.expressions[i], env) : '')).join('');
      throw new Error('tag outside static subset');
    case 'CallExpression': {
      const args = n.arguments.map(a => evaluate(a, env));
      if (n.callee.type === 'Identifier' && ['String', 'Number', 'Boolean'].includes(n.callee.name)) {
        return n.callee.name === 'String' ? String(args[0]) : n.callee.name === 'Number' ? Number(args[0]) : Boolean(args[0]);
      }
      if (n.callee.type !== 'MemberExpression') return invoke(evaluate(n.callee, env), args);
      const o = evaluate(n.callee.object, env), k = n.callee.computed ? evaluate(n.callee.property, env) : n.callee.property.name;
      if (Array.isArray(o)) {
        if (k === 'map') return o.map((v, i) => invoke(args[0], [v, i]));
        if (k === 'flatMap') return o.flatMap((v, i) => invoke(args[0], [v, i]));
        if (k === 'filter') return o.filter((v, i) => invoke(args[0], [v, i]));
        if (k === 'some') return o.some((v, i) => invoke(args[0], [v, i]));
        if (k === 'find') return o.find((v, i) => invoke(args[0], [v, i]));
        if (k === 'join') return o.join(args[0]);
        if (k === 'concat') return o.concat(...args);
        if (k === 'includes') return o.includes(args[0]);
      }
      if (typeof o === 'string') {
        if (k === 'replace') {
          const r = args[0]?.staticRegex ? new RegExp(args[0].pattern, args[0].flags) : args[0];
          if (typeof args[1] !== 'string') throw new Error('replacement callback forbidden');
          return o.replace(r, args[1]);
        }
        if (k === 'includes') return o.includes(args[0]);
        if (k === 'toLowerCase') return o.toLowerCase();
        if (k === 'toUpperCase') return o.toUpperCase();
        if (k === 'trim') return o.trim();
      }
      if (o?.staticRegex && k === 'test') return new RegExp(o.pattern, o.flags).test(String(args[0]));
      throw new Error('call outside static subset: ' + k);
    }
    default: throw new Error('node outside static subset: ' + n.type);
  }
}
function serial(v) {
  if (isFn(v)) return { function: text(v.node), evidence: evidence(v.node) };
  if (Array.isArray(v)) return v.map(serial);
  if (v && typeof v === 'object') return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, serial(x)]));
  return v;
}
function recover(n) { budget = 0; try { return serial(evaluate(n)); } catch (e) { return unresolved(n, e.message); } }
function recoverObject(n) {
  return n.properties.map(p => ({ key: p.key.name ?? p.key.value, value: recover(p.value), evidence: evidence(p) }));
}
function recoverArray(n) {
  return n.elements.map((e, i) => ({ index: i, evidence: evidence(e), fields: recoverObject(e) }));
}
const catalogs = {};
for (const name of ['ACTIONS', 'SPECS', 'REVERT', 'REVERT_EXTRA', 'DETECT', 'CHECKS', 'TWEAK_TOOLS', 'DNS_PROVIDERS', 'FIVEM_PRESETS', 'OPTI_QUIRKS']) catalogs[name] = { evidence: evidence(declarations.get(name)), entries: recoverObject(declarations.get(name)) };
for (const name of ['TWEAKS', 'CLEAN_CATS', 'GAMES']) catalogs[name] = { evidence: evidence(declarations.get(name)), entries: recoverArray(declarations.get(name)) };
const constants = {};
for (const name of ['K', 'PLAN', 'PWR', 'NIC_CLASS', 'NIC_EEE', 'NIC_INTMOD', 'STARTUP_APPS', 'UWP_APPS', 'AUTO_APPS_LOW', 'AUTO_APPS_CLOSE', 'FIVEM_SCALING', 'FIVEM_KEYS', 'OPTI_DLLS', 'OPTI_SKIP', 'OPTI_LOGS', 'OPTI_AC_RE', 'OPTI_UPS', 'OPTI_OPTS_DEFAULT', 'OPTI_MENU_KEYS', 'RAM_INTERVALS', 'ACTION_TIMEOUT', 'CLEAN_PS_HEAD', 'SNAPSHOT_PS', 'RESTORE_PS', 'CLEAN_TAIL', 'GAME_PS_HEAD', 'GENERIC_GAME_OPT', 'GENERIC_GAME_SPEC', 'GPU_ACTION_RE', 'GPU_ACTION_RE', 'gpuMsiReg', 'nicLsoReg', 'nicOptReg', 'nicOptRestart', 'gpuSpec', 'gpuScript', 'STEAM_VDF_WRITE', 'DETECT_HEAD', 'SHADER_CLEAN_PS', 'PS_UTF8']) constants[name] = { value: recover(declarations.get(name)), evidence: evidence(declarations.get(name)) };
const scenario = { build: 26100, laptop: false, x3dDual: false, obsExe: 'C:\\Example\\OBS\\bin\\64bit\\obs64.exe', steamPath: 'C:\\Example\\Steam', gpuIrq: 'HKLM:\\SYSTEM\\CurrentControlSet\\Enum\\PCI\\EXAMPLE\\0\\Device Parameters\\Interrupt Management', rtx: true, rtxOld: false, nvapi: true, nvidia: true, amdGpu: false, discord: true, nicLso: [{ k: '0001', v6: true }], nicOpt: [{ k: '0001', n: '*EEE', d: '1' }, { k: '0001', n: '*InterruptModeration', d: '1' }] };
const expanded = {};
for (const name of ['ACTIONS', 'SPECS', 'REVERT_EXTRA', 'DETECT']) expanded[name] = declarations.get(name).properties.map(p => {
  budget = 0;
  try { const v = evaluate(p.value); return { key: p.key.name ?? p.key.value, value: serial(isFn(v) ? invoke(v, [scenario]) : v), evidence: evidence(p) }; }
  catch (e) { return { key: p.key.name ?? p.key.value, value: unresolved(p.value, e.message), evidence: evidence(p) }; }
});
expanded.TWEAKS = declarations.get('TWEAKS').elements.map(e => ({ evidence: evidence(e), fields: e.properties.map(p => {
  budget = 0;
  try { const v = evaluate(p.value); return { key: p.key.name ?? p.key.value, value: serial(isFn(v) ? invoke(v, [scenario]) : v), evidence: evidence(p) }; }
  catch (error) { return { key: p.key.name ?? p.key.value, value: unresolved(p.value, error.message), evidence: evidence(p) }; }
}) }));
const mutations = ast.program.body.filter(n => n.type === 'ExpressionStatement' && /^(REVERT|ACTIONS|SPECS|DETECT)\[/.test(text(n))).map(n => ({ source: text(n), evidence: evidence(n) }));
const derivedTweaks = declarations.get('TWEAKS').elements.map(e => {
  const values = evaluate(e);
  const result = { id: values.id, evidence: evidence(e), observations: {} };
  for (const [name, args] of [['tweakSpec', [values, scenario]], ['tweakProbeExpr', [values, scenario]], ['tweakScriptOn', [values, true, scenario]], ['tweakScriptOff', [values, false, scenario]]]) {
    budget = 0;
    try { result.observations[name] = serial(invoke(evaluate(declarations.get(name.startsWith('tweakScript') ? 'tweakScript' : name)), args)); }
    catch (error) { result.observations[name] = unresolved(e, error.message); }
  }
  return result;
});
const scenarios = {
  desktopRtxWin11: scenario,
  notebookWin10NoSupportedExtras: { ...scenario, build: 19045, laptop: true, x3dDual: false, obsExe: null, steamPath: null, gpuIrq: null, rtx: false, nvapi: false, nvidia: false, discord: false, nicLso: [], nicOpt: [] },
  dualX3DOldRtxWin11: { ...scenario, x3dDual: true, rtxOld: true },
  win1122H2: { ...scenario, build: 22621 },
  win1123H2: { ...scenario, build: 22631 },
  missingWindowsBuild: { ...scenario, build: 0 },
};
const capabilityMatrix = Object.entries(scenarios).map(([name, capabilities]) => ({
  name, capabilities,
  tweaks: declarations.get('TWEAKS').elements.map(e => {
    budget = 0;
    const record = evaluate(e);
    return { id: record.id, recommended: Boolean(isFn(record.rec) ? invoke(record.rec, [capabilities]) : record.rec), blocked: record.block ? invoke(record.block, [capabilities]) : null, registry: serial(isFn(record.reg) ? invoke(record.reg, [capabilities]) : record.reg || []), service: serial(isFn(record.svc) ? invoke(record.svc, [capabilities]) : record.svc || []), drs: serial(isFn(record.drs) ? invoke(record.drs, [capabilities]) : record.drs || []) };
  }),
}));
const finalRevert = evaluate(declarations.get('REVERT'));
for (const statement of ast.program.body) {
  if (statement.type !== 'ExpressionStatement') continue;
  const expressions = statement.expression.type === 'SequenceExpression' ? statement.expression.expressions : [statement.expression];
  for (const expression of expressions) if (expression.type === 'AssignmentExpression' && expression.operator === '=' && expression.left.type === 'MemberExpression' && expression.left.object.name === 'REVERT') {
    const key = expression.left.computed ? evaluate(expression.left.property) : expression.left.property.name;
    finalRevert[key] = evaluate(expression.right, { REVERT: finalRevert });
  }
}
const data = { schema: 1, generatedBy: 'scripts/extract-gdz-operations.cjs', scope: 'Static declarations only, not runtime evaluation or observed effects. Dynamic expansion uses synthetic example capabilities, never this machine.', source: { file: sourceFile, sha256: crypto.createHash('sha256').update(source).digest('hex') }, catalogs, constants, syntheticScenario: scenario, expanded, derivedTweaks, capabilityMatrixSynthetic: capabilityMatrix, finalRevert: serial(finalRevert), topLevelCatalogMutations: mutations };
fs.mkdirSync(path.dirname(outputFile), { recursive: true });
fs.writeFileSync(outputFile, JSON.stringify(data, null, 2));
console.log(JSON.stringify({ outputFile, counts: Object.fromEntries(Object.entries(catalogs).map(([k, v]) => [k, v.entries.length])), unresolved: JSON.stringify(data).match(/"unresolved":true/g)?.length || 0 }));
