#!/usr/bin/env node
'use strict';
/**
 * Static data recovery for the local GDZ sample. This tool parses the input as
 * data. It never loads, evals, imports, runs or invokes the input program.
 * Only the deliberately small expression language below is interpreted.
 */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const pkg = path.resolve(__dirname, '../.tools/gdz-static/node_modules');
const parser = require(path.join(pkg, '@babel/parser'));
const traverse = require(path.join(pkg, '@babel/traverse')).default;
const generate = require(path.join(pkg, '@babel/generator')).default;
const t = require(path.join(pkg, '@babel/types'));
const sourcePath = path.resolve(process.argv[2] || 'artifacts/private/gdz-analysis/extracted/app-main.js');
const outDir = path.resolve(process.argv[3] || 'artifacts/private/gdz-analysis/deep');
const source = fs.readFileSync(sourcePath, 'utf8');
const ast = parser.parse(source, { sourceType: 'unambiguous', errorRecovery: false });
const paths = new WeakMap();
traverse(ast, { enter(p) { paths.set(p.node, p); } });
const unknown = why => { throw new Error(why); };
const fnValue = p => ({ __staticFunction: p });
const primitive = x => x === null || ['string', 'number', 'boolean', 'undefined'].includes(typeof x);
const dictionarySafety = new WeakMap();
const blockedObjectBindings = new Set();
function isImmutableObfuscatorDictionary(binding) {
  if (dictionarySafety.has(binding)) return dictionarySafety.get(binding);
  const bp = binding.path;
  let safe = binding.constant && bp.isVariableDeclarator() && t.isIdentifier(bp.node.id) && /^_0x[\da-f]+$/i.test(bp.node.id.name) && t.isObjectExpression(bp.node.init);
  if (safe) {
    safe = bp.node.init.properties.every(prop => {
      if (!t.isObjectProperty(prop) || prop.computed) return false;
      const key = prop.key.name ?? prop.key.value;
      if (!/^_0x[\da-f]+$/i.test(String(key)) && !/^[a-z]{5}$/i.test(String(key))) return false;
      // Offsets/helpers are flat dictionaries. A nested object can escape via a
      // property read; do not treat it as immutable.
      if (t.isObjectExpression(prop.value) || t.isArrayExpression(prop.value)) return false;
      if (t.isFunctionExpression(prop.value) || t.isArrowFunctionExpression(prop.value)) {
        let hasThis = false;
        paths.get(prop.value).traverse({ ThisExpression() { hasThis = true; } });
        if (hasThis) return false;
      }
      return true;
    });
  }
  if (safe) {
    safe = binding.referencePaths.every(ref => {
      // A bare reference escapes the object to arbitrary application code.
      let access = ref.parentPath;
      if (!access.isMemberExpression() || access.node.object !== ref.node) return false;
      while (access.parentPath.isMemberExpression() && access.parentPath.node.object === access.node) access = access.parentPath;
      const use = access.parentPath;
      if (use.isAssignmentExpression() && use.node.left === access.node || use.isUpdateExpression() || use.isUnaryExpression({ operator: 'delete' })) return false;
      return true;
    });
  }
  dictionarySafety.set(binding, safe);
  if (!safe) blockedObjectBindings.add(bp.node.id?.name || '<binding>');
  return safe;
}
let rotation = 0;
let decoderCalls = 0;
const decodeCache = new Map();
const stringFunction = ast.program.body.find(n => t.isFunctionDeclaration(n) && n.id.name === '_0x281f');
if (!stringFunction) throw new Error('Expected known string-array function, refusing unknown format');
const stringArray = stringFunction.body.body.find(t.isVariableDeclaration).declarations[0].init.elements.map(n => {
  if (!t.isStringLiteral(n)) throw new Error('Nonliteral encrypted array entry');
  return n.value;
});
const decoderFunction = ast.program.body.find(n => t.isFunctionDeclaration(n) && n.id.name === '_0x2908');
const decoderOffsetNode = decoderFunction.body.body[0].expression.right.right;

function binary(op, a, b) {
  if (!primitive(a) || !primitive(b)) return unknown('nonprimitive binary operands');
  switch (op) {
    case '+': return a + b; case '-': return a - b; case '*': return a * b; case '/': return a / b;
    case '%': return a % b; case '**': return a ** b; case '>>': return a >> b; case '>>>': return a >>> b;
    case '<<': return a << b; case '&': return a & b; case '|': return a | b; case '^': return a ^ b;
    case '===': return a === b; case '!==': return a !== b; case '==': return a == b; case '!=': return a != b;
    case '<': return a < b; case '<=': return a <= b; case '>': return a > b; case '>=': return a >= b;
    default: return unknown('unsupported binary ' + op);
  }
}
function checkPureInitializer(node, env, depth) {
  if (t.isObjectExpression(node)) {
    for (const prop of node.properties) {
      if (!t.isObjectProperty(prop)) return unknown('nonliteral initializer property');
      if (prop.computed) evaluate(prop.key, env, depth + 1);
      checkPureInitializer(prop.value, env, depth + 1);
    }
    return;
  }
  if (t.isArrayExpression(node)) {
    for (const item of node.elements) if (item) checkPureInitializer(item, env, depth + 1);
    return;
  }
  // Creating a function value does not execute its body. Every other expression
  // must itself be accepted by the restricted evaluator, even when unused.
  evaluate(node, env, depth + 1);
}
function evaluate(node, env = new Map(), depth = 0) {
  if (!node || depth > 90) return unknown('missing node or recursion limit');
  const p = paths.get(node);
  const sub = n => evaluate(n, env, depth + 1);
  if (t.isStringLiteral(node) || t.isNumericLiteral(node) || t.isBooleanLiteral(node)) return node.value;
  if (t.isNullLiteral(node)) return null;
  if (t.isIdentifier(node)) {
    if (node.name === 'undefined') return undefined;
    if (node.name === 'NaN') return NaN;
    if (node.name === 'Infinity') return Infinity;
    const binding = p.scope.getBinding(node.name);
    if (!binding) return unknown('unbound ' + node.name);
    if (env.has(binding)) return env.get(binding);
    if (!binding.constant) return unknown('mutable binding ' + node.name);
    if (binding.path.isFunctionDeclaration()) return fnValue(binding.path);
    if (binding.path.isVariableDeclarator() && binding.path.node.init) {
      const init = binding.path.node.init;
      if ((t.isObjectExpression(init) || t.isArrayExpression(init)) && !isImmutableObfuscatorDictionary(binding)) return unknown('mutable or non-dictionary object ' + node.name);
      return evaluate(init, env, depth + 1);
    }
    return unknown('unsupported binding ' + node.name);
  }
  if (t.isFunctionExpression(node) || t.isArrowFunctionExpression(node) || t.isFunctionDeclaration(node)) return fnValue(p);
  if (t.isUnaryExpression(node)) {
    const a = sub(node.argument);
    switch (node.operator) {
      case '+': return +a; case '-': return -a; case '!': return !a; case '~': return ~a;
      case 'void': return undefined; case 'typeof': return a?.__staticFunction ? 'function' : typeof a;
      default: return unknown('unsupported unary');
    }
  }
  if (t.isBinaryExpression(node)) return binary(node.operator, sub(node.left), sub(node.right));
  if (t.isLogicalExpression(node)) {
    const a = sub(node.left);
    if (node.operator === '&&') return a && sub(node.right);
    if (node.operator === '||') return a || sub(node.right);
    if (node.operator === '??') return a ?? sub(node.right);
  }
  if (t.isConditionalExpression(node)) return sub(node.test) ? sub(node.consequent) : sub(node.alternate);
  if (t.isArrayExpression(node)) return node.elements.map(n => n ? sub(n) : undefined);
  if (t.isObjectExpression(node)) {
    const obj = Object.create(null);
    for (const prop of node.properties) {
      if (!t.isObjectProperty(prop)) return unknown('unsupported object property');
      const key = prop.computed ? sub(prop.key) : prop.key.name ?? prop.key.value;
      if (['__proto__', 'prototype', 'constructor'].includes(String(key))) return unknown('reserved object key');
      checkPureInitializer(prop.value, env, depth + 1);
      // Lazy fields avoid interpreting unrelated dynamic members of a string dictionary.
      obj[key] = { __staticLazy: prop.value, env };
    }
    return obj;
  }
  if (t.isMemberExpression(node)) {
    const obj = sub(node.object);
    const key = node.computed ? sub(node.property) : node.property.name;
    if (['__proto__', 'prototype', 'constructor'].includes(String(key))) return unknown('reserved member');
    if (obj === null || obj === undefined || !Object.hasOwn(Object(obj), key)) return unknown('nonliteral member ' + key);
    const value = obj[key];
    return value?.__staticLazy ? evaluate(value.__staticLazy, value.env, depth + 1) : value;
  }
  if (t.isTemplateLiteral(node)) {
    let str = node.quasis[0].value.cooked;
    for (let i = 0; i < node.expressions.length; i++) {
      const v = sub(node.expressions[i]);
      if (!primitive(v)) return unknown('nonprimitive interpolation');
      str += String(v) + node.quasis[i + 1].value.cooked;
    }
    return str;
  }
  if (t.isCallExpression(node)) {
    if (t.isIdentifier(node.callee, { name: '_0x2908' })) {
      const n = sub(node.arguments[0]), key = sub(node.arguments[1]);
      if (typeof n !== 'number' || typeof key !== 'string') return unknown('dynamic decoder arguments');
      return decode(n, key);
    }
    if (t.isIdentifier(node.callee, { name: 'parseInt' }) && !p.scope.getBinding('parseInt')) {
      const args = node.arguments.map(sub);
      return parseInt(args[0], args[1]);
    }
    const fn = sub(node.callee);
    if (!fn?.__staticFunction) return unknown('non-allowlisted call');
    const fp = fn.__staticFunction;
    const f = fp.node;
    const statements = t.isBlockStatement(f.body) ? f.body.body : [t.returnStatement(f.body)];
    // Only a single return, optionally preceded by local immutable declarations.
    if (statements.some((s, i) => !(t.isVariableDeclaration(s, { kind: 'const' }) || (t.isReturnStatement(s) && i === statements.length - 1)))) return unknown('nonpure function body');
    if (!t.isReturnStatement(statements.at(-1))) return unknown('missing pure return');
    const callEnv = new Map(env);
    // Evaluate all supplied arguments, including unused/extra ones: discarding
    // an argument is allowed only when it is statically pure. This preserves
    // effects and errors that would occur before entering the helper.
    const suppliedArgs = node.arguments.map(sub);
    for (let i = 0; i < f.params.length; i++) {
      if (!t.isIdentifier(f.params[i])) return unknown('nonidentifier parameter');
      const b = fp.scope.getBinding(f.params[i].name);
      if (b) callEnv.set(b, suppliedArgs[i]);
    }
    for (const statement of statements.slice(0, -1)) {
      for (const declaration of statement.declarations) {
        if (!t.isIdentifier(declaration.id) || !declaration.init) return unknown('nonliteral local declaration');
        checkPureInitializer(declaration.init, callEnv, depth + 1);
      }
    }
    return evaluate(statements.at(-1).argument, callEnv, depth + 1);
  }
  return unknown('unsupported syntax ' + node.type);
}
const offset = evaluate(decoderOffsetNode);
// Regression fixtures for the material distinction between a constant binding
// and mutable object state. These fixtures are parsed as data, never executed.
const fixtures = [
  { name: 'named const object state must remain dynamic', code: 'const state={bytes:null};state.bytes=10;state.bytes;', allowed: false },
  { name: 'obfuscated object property write must remain dynamic', code: 'const _0xaa={abcde:1};_0xaa.abcde=2;_0xaa.abcde;', allowed: false },
  { name: 'object escape must remain dynamic', code: 'const _0xbb={abcde:1};external(_0xbb);_0xbb.abcde;', allowed: false },
  { name: 'array mutation must remain dynamic', code: 'const _0xcc=[1];_0xcc.push(2);_0xcc[0];', allowed: false },
  { name: 'read-only offset dictionary may fold', code: 'const _0xdd={_0xabc:28};_0xdd._0xabc;', allowed: true, expected: 28 },
  { name: 'read-only operator dictionary may fold', code: 'const _0xee={abcde:function(a,b){return a+b;}};_0xee.abcde(3,4);', allowed: true, expected: 7 },
  { name: 'unused side-effect initializer must not disappear', code: 'function helper(){const value=external();return 1;}helper();', allowed: false },
  { name: 'side-effect initializer inside object must not disappear', code: 'function helper(){const value={field:external()};return 1;}helper();', allowed: false },
  { name: 'unused side-effect argument must not disappear', code: 'function helper(unused){return 1;}helper(external());', allowed: false },
  { name: 'extra side-effect argument must not disappear', code: 'function helper(){return 1;}helper(external());', allowed: false },
  { name: 'side effect in unused object argument must remain', code: 'function helper(unused){return 1;}helper({field:external()});', allowed: false },
  { name: 'side effect in object truthiness must remain', code: '!{field:external()};', allowed: false },
  { name: 'function typeof matches JavaScript semantics', code: 'function helper(){}typeof helper;', allowed: true, expected: 'function' },
];
const fixtureResults = fixtures.map(f => {
  const tree = parser.parse(f.code);
  traverse(tree, { enter(p) { paths.set(p.node, p); } });
  let value, allowed = false;
  try { value = evaluate(tree.program.body.at(-1).expression); allowed = true; } catch {}
  const passed = allowed === f.allowed && (!f.allowed || value === f.expected);
  if (!passed) throw new Error('Static interpreter regression: ' + f.name);
  return { name: f.name, passed };
});
function decode(index, key) {
  const arrIndex = index - offset;
  if (!Number.isInteger(arrIndex) || arrIndex < 0 || arrIndex >= stringArray.length) return unknown('decoder index range');
  const actual = (arrIndex + rotation) % stringArray.length;
  const cacheKey = actual + ':' + key;
  if (decodeCache.has(cacheKey)) return decodeCache.get(cacheKey);
  const input = stringArray[actual];
  const alphabet = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+/';
  const normal = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
  let b64 = '';
  for (const ch of input) b64 += ch === '=' ? '=' : normal[alphabet.indexOf(ch)];
  const decoded = new TextDecoder('utf-8', { fatal: true }).decode(Buffer.from(b64, 'base64'));
  const S = Array.from({ length: 256 }, (_, i) => i);
  let j = 0;
  for (let i = 0; i < 256; i++) {
    j = (j + S[i] + key.charCodeAt(i % key.length)) & 255;
    [S[i], S[j]] = [S[j], S[i]];
  }
  let i = 0, out = ''; j = 0;
  for (let p = 0; p < decoded.length; p++) {
    i = (i + 1) & 255; j = (j + S[i]) & 255;
    [S[i], S[j]] = [S[j], S[i]];
    out += String.fromCharCode(decoded.charCodeAt(p) ^ S[(S[i] + S[j]) & 255]);
  }
  decoderCalls++;
  decodeCache.set(cacheKey, out);
  return out;
}

const firstCall = ast.program.body[0].expression;
const checksumFn = firstCall.callee;
const target = evaluate(firstCall.arguments[1]);
let checksumNode;
traverse(ast, { VariableDeclarator(p) {
  if (p.node.id.name === '_0x209bd7' && p.findParent(a => a.node === checksumFn)) checksumNode = p.node.init;
}});
let checksum;
let rotationFound = false;
for (rotation = 0; rotation < stringArray.length; rotation++) {
  try { checksum = evaluate(checksumNode); } catch { checksum = NaN; }
  if (checksum === target) { rotationFound = true; break; }
}
if (!rotationFound) throw new Error('Array rotation not recovered; refusing replacements');
console.log(JSON.stringify({ stage: 'rotation', length: stringArray.length, offset, rotation, target, checksum }));
fs.mkdirSync(outDir, { recursive: true });
const replacements = [];
const failures = new Map();
const wrapperMemo = new WeakMap();
function isDecoderWrapper(p, seen = new Set()) {
  if (!p?.node) return false;
  if (wrapperMemo.has(p.node)) return wrapperMemo.get(p.node);
  if (t.isFunctionDeclaration(p.node) && p.node.id.name === '_0x2908') return true;
  if (!p.isFunction() || !t.isBlockStatement(p.node.body)) return false;
  const statements = p.node.body.body;
  if (!statements.length || !statements.slice(0, -1).every(s => t.isVariableDeclaration(s, { kind: 'const' }))) return false;
  if (!t.isReturnStatement(statements.at(-1)) || !t.isCallExpression(statements.at(-1).argument)) return false;
  const call = statements.at(-1).argument;
  if (!t.isIdentifier(call.callee)) return false;
  if (call.callee.name === '_0x2908') return true;
  const b = paths.get(call).scope.getBinding(call.callee.name);
  if (!b || seen.has(b)) return false;
  seen.add(b);
  const answer = isDecoderWrapper(b.path, seen);
  wrapperMemo.set(p.node, answer);
  return answer;
}
const originalFunctionIndex = [];
traverse(ast, { Function(p) {
  const n = p.node;
  const name = n.id?.name || (p.parentPath.isVariableDeclarator() && p.parentPath.node.id.name) || null;
  originalFunctionIndex.push({ name, kind: n.type, originalLine: n.loc.start.line, originalStart: n.start, originalEnd: n.end, parameters: n.params.map(x => source.slice(x.start, x.end)), decoderWrapper: isDecoderWrapper(p) });
}});
const replace = (p, value, kind) => {
  if (!primitive(value) || value === undefined || typeof value === 'number' && !Number.isFinite(value)) return false;
  if (typeof value === 'string' && value.length > 300000) return false;
  replacements.push({ start: p.node.start, end: p.node.end, kind, value });
  p.replaceWith(t.valueToNode(value));
  p.skip();
  return true;
};
// Keep the original array/decoder/bootstrap untouched as provenance; consumers
// can inspect the replacement output without assuming it is runnable source.
const excluded = new Set([ast.program.body[0], stringFunction, decoderFunction]);
traverse(ast, {
  enter(p) { if (excluded.has(p.node)) p.skip(); },
  CallExpression: { exit(p) {
    try {
      const val = evaluate(p.node);
      replace(p, val, 'pure-call');
    } catch (e) {
      if (t.isIdentifier(p.node.callee) && /^_0x/.test(p.node.callee.name)) failures.set(p.node.callee.name, (failures.get(p.node.callee.name) || 0) + 1);
    }
  } },
  BinaryExpression: { exit(p) { try { replace(p, evaluate(p.node), 'constant'); } catch {} } },
  UnaryExpression: { exit(p) { try { replace(p, evaluate(p.node), 'constant'); } catch {} } },
  MemberExpression: { exit(p) {
    // Preserve assignment targets and delete operands.
    if (p.parentPath.isAssignmentExpression() && p.key === 'left' || p.parentPath.isUpdateExpression() || p.parentPath.isUnaryExpression({ operator: 'delete' })) return;
    try { replace(p, evaluate(p.node), 'constant-member'); } catch {}
  } },
});
const unresolvedDecoderCalls = [];
traverse(ast, { CallExpression(p) {
  if (p.findParent(a => excluded.has(a.node))) return;
  if (!t.isIdentifier(p.node.callee)) return;
  const b = p.scope.getBinding(p.node.callee.name);
  if (p.node.callee.name !== '_0x2908' && !isDecoderWrapper(b?.path)) return;
  // Calls within a decoder wrapper are definitions, not missed literal callsites.
  if (p.findParent(a => a.isFunction() && isDecoderWrapper(a))) return;
  let reason;
  try { evaluate(p.node); reason = 'resolvable after pass'; } catch (e) { reason = e.message; }
  unresolvedDecoderCalls.push({ name: p.node.callee.name, originalStart: p.node.start, originalEnd: p.node.end, reason, expression: generate(p.node).code });
}});
const readable = generate(ast, { comments: true, compact: false, retainLines: false, jsescOption: { minimal: true } }, source).code;
fs.writeFileSync(path.join(outDir, 'backend-readable.js'), '/* STATIC ANALYSIS ARTIFACT. Original code was not executed. See backend-static-metadata.json. */\n' + readable);
const meta = {
  sourcePath, sourceSha256: crypto.createHash('sha256').update(source).digest('hex'),
  generatedUtc: new Date().toISOString(), method: 'Babel parser plus restricted expression interpreter; original target never evaluated or loaded',
  stringArray: { entries: stringArray.length, offset, rotation, checksum, target },
  decoderCalls, replacementCount: replacements.length,
  immutableDictionaryPolicy: 'Only flat obfuscator-named dictionaries with no property writes, deletes, updates, bare-reference escapes or this-dependent helper methods may resolve through a const binding. Named application state and arrays remain dynamic.',
  blockedObjectBindings: [...blockedObjectBindings].sort(),
  regressionFixtures: fixtureResults,
  replacementKinds: replacements.reduce((a, r) => (a[r.kind] = (a[r.kind] || 0) + 1, a), {}),
  unresolvedObfuscatedCallNames: [...failures].sort((a, b) => b[1] - a[1]),
  unresolvedDecoderCalls,
  limitations: ['Variable names and control-flow obfuscation are preserved.', 'Only the restricted pure expression subset is folded.', 'Original bootstrap, string array and decoder are retained as provenance.', 'This is an analysis artifact, not a replacement installation.'],
};
fs.writeFileSync(path.join(outDir, 'backend-static-metadata.json'), JSON.stringify(meta, null, 2));
fs.writeFileSync(path.join(outDir, 'backend-replacements.json'), JSON.stringify(replacements, null, 2));
fs.writeFileSync(path.join(outDir, 'backend-function-index.json'), JSON.stringify(originalFunctionIndex, null, 2));
// A second view removes only statically false/true branches and proven decoder
// wrappers. The lossless structural view above remains available alongside it.
let removedConstantBranches = 0, removedWrappers = 0, removedJunkConstants = 0, inlinedOperatorHelpers = 0;
function operatorTemplate(fn, args) {
  const fp = fn.__staticFunction, f = fp.node;
  if (!f.params.every(t.isIdentifier) || f.params.length !== args.length || !args.every(t.isExpression) || f.async || f.generator) return null;
  const body = t.isBlockStatement(f.body) && f.body.body.length === 1 && t.isReturnStatement(f.body.body[0]) ? f.body.body[0].argument : null;
  if (!body) return null;
  const params = f.params.map(x => x.name), order = [];
  function admissible(n) {
    if (t.isLiteral(n) && !t.isRegExpLiteral(n)) return true;
    if (t.isIdentifier(n)) { if (!params.includes(n.name)) return false; order.push(n.name); return true; }
    if (t.isBinaryExpression(n)) return admissible(n.left) && admissible(n.right);
    if (t.isUnaryExpression(n) && ['!', '~', '+', '-', 'typeof', 'void'].includes(n.operator)) return admissible(n.argument);
    // Simple function application wrappers preserve callee/argument order.
    if (t.isCallExpression(n) && t.isIdentifier(n.callee) && params.includes(n.callee.name)) return admissible(n.callee) && n.arguments.every(admissible);
    return false;
  }
  // Each argument must be evaluated once in its original order, preserving
  // side effects. Short-circuit/conditional templates are intentionally omitted.
  if (!admissible(body) || order.length !== params.length || order.some((x, i) => x !== params[i])) return null;
  const substitutes = new Map(params.map((n, i) => [n, args[i]]));
  function clone(n) {
    if (t.isIdentifier(n) && substitutes.has(n.name)) return t.cloneNode(substitutes.get(n.name), true);
    const copy = t.cloneNode(n, false);
    for (const key of t.VISITOR_KEYS[n.type] || []) {
      const value = n[key];
      copy[key] = Array.isArray(value) ? value.map(x => x && clone(x)) : value && clone(value);
    }
    return copy;
  }
  return clone(body);
}
for (let pass = 0; pass < 3; pass++) {
  traverse(ast, { enter(p) { paths.set(p.node, p); } });
  let count = 0;
  traverse(ast, {
    enter(p) { if (excluded.has(p.node)) p.skip(); },
    CallExpression: { exit(p) {
      // Only dictionaries introduced by the obfuscator are candidates.
      if (!t.isMemberExpression(p.node.callee) || !t.isIdentifier(p.node.callee.object) || !/^_0x/.test(p.node.callee.object.name)) return;
      try {
        const fn = evaluate(p.node.callee);
        if (!fn?.__staticFunction) return;
        const expression = operatorTemplate(fn, p.node.arguments);
        if (expression) { p.replaceWith(expression); p.skip(); count++; }
      } catch {}
    } },
  });
  inlinedOperatorHelpers += count;
  if (!count) break;
}
traverse(ast, {
  enter(p) { if (excluded.has(p.node)) p.skip(); },
  IfStatement: { exit(p) {
    let value;
    try { value = evaluate(p.node.test); } catch { return; }
    if (!primitive(value)) return;
    const chosen = value ? p.node.consequent : p.node.alternate;
    if (chosen) p.replaceWith(t.isBlockStatement(chosen) ? chosen : t.blockStatement([chosen]));
    else p.remove();
    removedConstantBranches++;
  } },
  ConditionalExpression: { exit(p) {
    let value;
    try { value = evaluate(p.node.test); } catch { return; }
    if (!primitive(value)) return;
    p.replaceWith(value ? p.node.consequent : p.node.alternate);
    removedConstantBranches++;
  } },
});
// Wrapper definitions carry no application behavior; their literal callsites
// were resolved above. Keep any wrapper referenced by an unresolved callsite.
const neededWrapperNames = new Set(unresolvedDecoderCalls.map(x => x.name));
traverse(ast, { FunctionDeclaration(p) {
  if (p.findParent(a => excluded.has(a.node)) || excluded.has(p.node)) return;
  if (isDecoderWrapper(p) && !neededWrapperNames.has(p.node.id.name)) { p.remove(); removedWrappers++; }
}});
for (let pass = 0; pass < 5; pass++) {
  traverse(ast, { Program(p) { p.scope.crawl(); } });
  let count = 0;
  traverse(ast, { VariableDeclarator(p) {
    if (p.findParent(a => excluded.has(a.node))) return;
    if (!t.isIdentifier(p.node.id) || !/^_0x/.test(p.node.id.name)) return;
    const b = p.scope.getBinding(p.node.id.name);
    if (!b || b.referenced || !b.constant || !t.isObjectExpression(p.node.init)) return;
    // Constructing literal/function dictionaries has no runtime side effects.
    if (!p.node.init.properties.every(prop => t.isObjectProperty(prop) && !prop.computed && (t.isLiteral(prop.value) && !t.isRegExpLiteral(prop.value) || t.isFunctionExpression(prop.value)))) return;
    p.remove(); count++;
  }});
  removedJunkConstants += count;
  if (!count) break;
}
const focusedCode = '/* STATIC ANALYSIS VIEW: proven constant branches, decoder wrappers and unused offset tables omitted. */\n' + generate(ast, { comments: true, jsescOption: { minimal: true } }, source).code;
fs.writeFileSync(path.join(outDir, 'backend-focused.js'), focusedCode);
// Reparse both products as a syntax check and to provide exact final line refs.
parser.parse(readable, { sourceType: 'unambiguous' });
const focusedAst = parser.parse(focusedCode, { sourceType: 'unambiguous' });
const namedFunctions = [];
traverse(focusedAst, { Function(p) {
  const n = p.node;
  const name = n.id?.name || (p.parentPath.isVariableDeclarator() && p.parentPath.node.id.name) || null;
  if (!name || /^_0x/.test(name)) return;
  const original = originalFunctionIndex.find(f => f.name === name);
  const directCalls = new Set();
  p.traverse({ Function(inner) { if (inner.node !== n) inner.skip(); }, CallExpression(call) {
    if (t.isIdentifier(call.node.callee)) directCalls.add(call.node.callee.name);
    else if (t.isMemberExpression(call.node.callee)) directCalls.add(generate(call.node.callee).code);
  }});
  namedFunctions.push({
    name, parameters: n.params.map(x => focusedCode.slice(x.start, x.end)),
    originalStart: original?.originalStart ?? null, originalEnd: original?.originalEnd ?? null,
    focusedLine: n.loc.start.line, focusedEndLine: n.loc.end.line,
    focusedStart: n.start, focusedEnd: n.end, directCalls: [...directCalls],
    code: focusedCode.slice(n.start, n.end),
  });
}});
fs.writeFileSync(path.join(outDir, 'backend-functions-readable.json'), JSON.stringify(namedFunctions, null, 2));
meta.focusedView = { removedConstantBranches, removedWrappers, removedJunkConstants, inlinedOperatorHelpers };
meta.validation = { readableReparsed: true, focusedReparsed: true, namedReadableFunctions: namedFunctions.length, unresolvedDecoderCallsites: unresolvedDecoderCalls.length };
fs.writeFileSync(path.join(outDir, 'backend-static-metadata.json'), JSON.stringify(meta, null, 2));
console.log(JSON.stringify({ stage: 'done', replacementCount: meta.replacementCount, replacementKinds: meta.replacementKinds, unresolvedDecoderCallCount: unresolvedDecoderCalls.length, focusedView: meta.focusedView }));
