"""Read-only radare2 analysis. Never opens the PE through the OS loader."""
import hashlib, json, pathlib, subprocess, datetime, struct, os

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/private/gdz-analysis/deep/native-deep'
OUT.mkdir(parents=True,exist_ok=True)
TOOL = ROOT / '.tools/radare2-6.2.6/radare2-6.2.6-w64/bin/radare2.exe'
TARGET = pathlib.Path(os.environ['LOCALAPPDATA']) / 'Programs/GDZ Optimizer/resources/app.asar.unpacked/bin/Lossless.dll'
assert hashlib.sha256(TOOL.read_bytes()).hexdigest()=='56a1fa4d2d35bf4860bc404beec416c776c972dd1898aec7907922e28aaf154f', 'Unexpected static analyzer binary'
assert hashlib.sha256(TARGET.read_bytes()).hexdigest()=='626b196d799606cd4250b7b29e04228692ab70cf56a5d1bbb56d748c8219f0eb', 'Unexpected target revision'
class R2:
    def __init__(self):
        self.p = subprocess.Popen([str(TOOL), '-2', '-q0', '-e', 'scr.color=0', '-e', 'bin.cache=true', str(TARGET)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        self.read()
    def read(self):
        buf=bytearray()
        while True:
            b=self.p.stdout.read(1)
            if b in (b'',b'\0'): break
            buf.extend(b)
        return bytes(buf).decode('utf-8','replace').strip()
    def cmd(self,c):
        self.p.stdin.write((c+'\n').encode()); self.p.stdin.flush()
        return self.read()
    def js(self,c):
        s=self.cmd(c)
        try:return json.loads(s)
        except Exception:return {'parse_error':s}
    def close(self): self.p.stdin.write(b'q!\n'); self.p.stdin.flush(); self.p.wait(timeout=5)

r=R2(); r.cmd('aaa')
inventory={k:r.js(c) for k,c in [('info','ij'),('imports','iij'),('exports','iEj'),('strings','izzj'),('sections','iSj'),('classes','icj'),('functions','aflj')]}
(OUT/'r2-inventory.json').write_text(json.dumps(inventory,ensure_ascii=False,indent=2),encoding='utf8')
export_functions=[]; child_addrs=set()
for e in inventory['exports']:
    addr=e['vaddr']; name=e['name']; asm=r.js(f'pdfj @ {addr}'); pseudo=r.cmd(f'pdc @ {addr}'); xrefs=r.js(f'axtj @ {addr}')
    (OUT/f'{name}.asm.json').write_text(json.dumps(asm,ensure_ascii=False,indent=2),encoding='utf8')
    (OUT/f'{name}.pseudo.c').write_text(pseudo,encoding='utf8')
    calls=[]
    for op in asm.get('ops',[]):
        if op.get('type') in ('call','ucall','rcall','icall'):
            calls.append({k:op[k] for k in ('addr','opcode','jump','ptr','comment') if k in op})
            if 'jump' in op and 0x180001000 <= op['jump'] < 0x180038000: child_addrs.add(op['jump'])
    export_functions.append({'name':name,'vaddr':addr,'rva':addr-0x180000000,'size':asm.get('size'),'calls':calls,'xrefs':xrefs})
for addr in sorted(child_addrs):
    (OUT/f'callee-{addr:x}.pseudo.c').write_text(r.cmd(f'pdc @ {addr}'),encoding='utf8')
    (OUT/f'callee-{addr:x}.asm.json').write_text(json.dumps(r.js(f'pdfj @ {addr}'),ensure_ascii=False,indent=2),encoding='utf8')
all_asm=[]
for f in inventory['functions']:
    asm=r.js(f"pdfj @ {f['addr']}")
    if isinstance(asm,dict) and 'ops' in asm:all_asm.append(asm)
(OUT/'all-functions.asm.json').write_text(json.dumps(all_asm,ensure_ascii=False,indent=2),encoding='utf8')
interesting_strings=[s for s in inventory['strings'] if 0x180038000<=s.get('vaddr',0)<0x18004a000]
for s in interesting_strings:s['xrefs']=r.js(f"axtj @ {s['vaddr']}")
(OUT/'strings-with-xrefs.json').write_text(json.dumps(interesting_strings,ensure_ascii=False,indent=2),encoding='utf8')
global_refs={hex(addr):r.js(f'axtj @ {addr}') for addr in [0x1800505f0,0x18004d458,0x18004d438,0x18005053d,0x1800505d8]}
(OUT/'selected-global-xrefs.json').write_text(json.dumps(global_refs,ensure_ascii=False,indent=2),encoding='utf8')
data=TARGET.read_bytes(); pdata=next(s for s in inventory['sections'] if s['name']=='.pdata'); runtime=[]
r.cmd('e asm.bbmiddle=false');r.cmd('e asm.flags.middle=false');r.cmd('e asm.middle=false')
pe=struct.unpack_from('<I',data,0x3c)[0];runtime_rva,runtime_size=struct.unpack_from('<II',data,pe+24+112+3*8)
assert runtime_size%12==0 and runtime_rva==pdata['vaddr']-0x180000000
for at in range(pdata['paddr'],pdata['paddr']+runtime_size,12):
    start,end,unwind=struct.unpack_from('<III',data,at)
    assert 0x1000<=start<end<=0x38000
    asm=r.js(f'pDj {end-start} @ {0x180000000+start}')
    bounded=[];boundary_overread=[]
    for op in asm:
        remaining=0x180000000+end-op['addr']
        if remaining<=0:continue
        if op['size']>remaining:
            boundary_overread.append(op)
            raw=0x400+op['addr']-0x180001000
            tail=data[raw:raw+remaining]
            op={'addr':op['addr'],'size':remaining,'bytes':tail.hex(),'opcode':'.byte '+', '.join(hex(x) for x in tail),'type':'data','note':'radare data hint extended beyond unwind boundary; retained bounded bytes only, not claimed as recovered instruction'}
        bounded.append(op)
    runtime.append({'start_rva':start,'end_rva':end,'unwind_rva':unwind,'size':end-start,'ops':bounded,'boundary_overread_from_tool':boundary_overread})
(OUT/'runtime-functions.asm.json').write_text(json.dumps(runtime,ensure_ascii=False,indent=2),encoding='utf8')
manifest={'target':str(TARGET),'sha256':hashlib.sha256(TARGET.read_bytes()).hexdigest(),'tool':'radare2 6.2.6','tool_sha256':hashlib.sha256(TOOL.read_bytes()).hexdigest(),'tool_zip_sha256':'c648e354dd584b14ba31d0244feb338c7506eb5b55b8cb4f09b06ee0507de54f','tool_source':'https://github.com/radareorg/radare2/releases/tag/6.2.6','analysis_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'mode':'static read-only; no debugger, loader, target code, exports or GPU operations executed','exports':export_functions,'first_level_callees':sorted(child_addrs),'function_count':len(inventory['functions'])}
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8');r.close()
print(json.dumps({'exports':len(export_functions),'callees':len(child_addrs),'functions':manifest['function_count'],'out':str(OUT)}))
