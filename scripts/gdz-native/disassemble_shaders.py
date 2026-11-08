"""Disassemble shader bytecode, never execute it or load Lossless.dll.

DXBC uses only the signed Windows system disassembler. SPIR-V is decoded in
Python with the Khronos instruction grammar. Output is private analysis data.
"""
import ctypes as C, hashlib, json, pathlib, struct, os
ROOT=pathlib.Path(__file__).resolve().parents[2]
P=ROOT/'artifacts/private/gdz-analysis/deep/native-deep'
inv=json.loads((P/'resources-shaders.json').read_text(encoding='utf8'))
target=pathlib.Path(os.environ['LOCALAPPDATA'])/'Programs/GDZ Optimizer/resources/app.asar.unpacked/bin/Lossless.dll'; data=target.read_bytes()
assert hashlib.sha256(data).hexdigest()==inv['target_sha256']
compiler=pathlib.Path(os.environ['SystemRoot'])/'System32/d3dcompiler_47.dll'
system=C.WinDLL(str(compiler),winmode=0x00000800)
dis=system.D3DDisassemble;dis.argtypes=[C.c_void_p,C.c_size_t,C.c_uint,C.c_char_p,C.POINTER(C.c_void_p)];dis.restype=C.c_long
O=P/'shader-asm';O.mkdir(exist_ok=True); manifest=[]
for s in inv['shaders']:
    blob=data[s['file_offset']:s['file_offset']+s['size']];src=C.create_string_buffer(blob);result=C.c_void_p()
    hr=dis(src,len(blob),0,None,C.byref(result))
    if hr<0 or not result.value:raise RuntimeError(f'D3DDisassemble failed {hr} resource {s["resource_path"]}')
    table=C.cast(result,C.POINTER(C.POINTER(C.c_void_p))).contents
    ptr=C.WINFUNCTYPE(C.c_void_p,C.c_void_p)(table[3])(result)
    size=C.WINFUNCTYPE(C.c_size_t,C.c_void_p)(table[4])(result)
    txt=C.string_at(ptr,size).rstrip(b'\0');C.WINFUNCTYPE(C.c_ulong,C.c_void_p)(table[2])(result)
    name=f'dxbc-{s["resource_path"][1]}.asm';(O/name).write_bytes(txt)
    manifest.append({'resource':s['resource_path'],'source_sha256':s['sha256'],'format':'DXBC','file':name,'output_sha256':hashlib.sha256(txt).hexdigest(),'bytes':len(txt)})

grammar_bytes=(P/'spirv.core.grammar.official.json').read_bytes()
assert hashlib.sha256(grammar_bytes).hexdigest()=='8f20f6ca4c2411391d14125cac69999f1c8248ef656e00d9d534e5e2b7a4f52c', 'Unexpected Khronos grammar revision'
grammar=json.loads(grammar_bytes); instructions={i['opcode']:i for i in grammar['instructions']}; kinds={k['kind']:k for k in grammar['operand_kinds']}
def decode_operands(words,spec):
    out=[];at=0
    for op in spec:
        if at>=len(words):break
        kind=op['kind'];quantity=op.get('quantifier'); repeats=1 if quantity!='*' else len(words)-at
        for _ in range(repeats):
            if at>=len(words):break
            k=kinds.get(kind,{});w=words[at]
            if kind=='LiteralString':
                end=at
                while end<len(words):
                    raw=struct.pack('<I',words[end]);end+=1
                    if b'\0' in raw:break
                raw=struct.pack('<'+'I'*(end-at),*words[at:end]);out.append(json.dumps(raw.split(b'\0',1)[0].decode('utf8','replace')));at=end
            elif kind.startswith('Id'):
                out.append('%'+str(w));at+=1
            elif k.get('category') in ('ValueEnum','BitEnum'):
                found=next((e for e in k.get('enumerants',[]) if int(str(e['value']),0)==w),None)
                out.append(found['enumerant'] if found else hex(w));at+=1
                # Enum-dependent operands are kept as numeric words below rather
                # than guessed when their meaning depends on a prior result ID.
                if found and found.get('parameters'):
                    extra=found['parameters'];decoded,used=decode_operands(words[at:],extra);out.extend(decoded);at+=used
            elif kind.startswith('Pair'):
                bases=k.get('bases',[]);decoded,used=decode_operands(words[at:],[{'kind':x} for x in bases]);out.extend(decoded);at+=used
            else:out.append(str(w));at+=1
    return out,at
for s in inv['spirv']:
    blob=data[s['file_offset']:s['file_offset']+s['size']];words=struct.unpack('<'+'I'*(len(blob)//4),blob);lines=[f'; SPIR-V {s["version"]}; resource {s["resource_path"][1]}; bound {words[3]}', '; Grammar-based listing. Context-dependent literal words remain numeric.'];at=5
    while at<len(words):
        wc=words[at]>>16;op=words[at]&65535;info=instructions.get(op,{'opname':f'Op{op}','operands':[]});vals=list(words[at+1:at+wc]);parts,used=decode_operands(vals,info.get('operands',[]));parts.extend(str(x) for x in vals[used:]);lines.append(f'{at:05d}: '+info['opname']+' '+' '.join(parts));at+=wc
    txt=('\n'.join(lines)+'\n').encode();name=f'spirv-{s["resource_path"][1]}.asm';(O/name).write_bytes(txt)
    manifest.append({'resource':s['resource_path'],'source_sha256':s['sha256'],'format':'SPIR-V','file':name,'output_sha256':hashlib.sha256(txt).hexdigest(),'bytes':len(txt)})
summary={'disassembler':str(compiler),'disassembler_sha256':hashlib.sha256(compiler.read_bytes()).hexdigest(),'grammar_source':'https://github.com/KhronosGroup/SPIRV-Headers/blob/main/include/spirv/unified1/spirv.core.grammar.json','grammar_sha256':hashlib.sha256((P/'spirv.core.grammar.official.json').read_bytes()).hexdigest(),'scope':'static bytecode disassembly; no D3D device, GPU execution or target loading','outputs':manifest}
(P/'shader-disassembly-manifest.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'outputs':len(manifest),'dxbc':sum(x['format']=='DXBC' for x in manifest),'spirv':sum(x['format']=='SPIR-V' for x in manifest),'output_bytes':sum(x['bytes'] for x in manifest)}))
