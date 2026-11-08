"""Inspect PE resources and Direct3D containers as bytes; no shader execution."""
import struct, hashlib, json, pathlib, re, os
ROOT=pathlib.Path(__file__).resolve().parents[2]
P=ROOT/'artifacts/private/gdz-analysis/deep/native-deep'
P.mkdir(parents=True,exist_ok=True)
target=pathlib.Path(os.environ['LOCALAPPDATA'])/'Programs/GDZ Optimizer/resources/app.asar.unpacked/bin/Lossless.dll'
b=target.read_bytes(); U=lambda at:struct.unpack_from('<I',b,at)[0]
assert hashlib.sha256(b).hexdigest()=='626b196d799606cd4250b7b29e04228692ab70cf56a5d1bbb56d748c8219f0eb', 'Unexpected target revision'
pe=U(0x3c);opt=pe+24; n=struct.unpack_from('<H',b,pe+6)[0];sz=struct.unpack_from('<H',b,pe+20)[0]
secs=[]
for i in range(n):
    at=opt+sz+i*40; name,vs,rva,rs,raw=struct.unpack_from('<8sIIII',b,at);secs.append((name.rstrip(b'\0').decode(),rva,max(vs,rs),raw))
def off(rva):
    for _,v,s,r in secs:
        if v<=rva<v+s:return r+rva-v
    raise ValueError(rva)
base=off(U(opt+112+16)); resources=[]
def resname(x):
    if x&0x80000000:
        a=base+(x&0x7fffffff);l=struct.unpack_from('<H',b,a)[0];return b[a+2:a+2+l*2].decode('utf-16-le')
    return x
def walk(rel,parts=()):
    at=base+rel;n1,n2=struct.unpack_from('<HH',b,at+12)
    for i in range(n1+n2):
        name,d=struct.unpack_from('<II',b,at+16+i*8);path=parts+(resname(name),)
        if d&0x80000000:walk(d&0x7fffffff,path)
        else:
            rva,size,cp,_=struct.unpack_from('<IIII',b,base+d);raw=off(rva);blob=b[raw:raw+size]
            resources.append({'path':path,'rva':rva,'vaddr':0x180000000+rva,'offset':raw,'size':size,'codepage':cp,'sha256':hashlib.sha256(blob).hexdigest(),'magic':blob[:16].hex(),'dxbc':[]})
walk(0)
def u32(d,a):return struct.unpack_from('<I',d,a)[0]
def cstr(d,a):
    if a>=len(d):return None
    return d[a:d.find(b'\0',a) if b'\0' in d[a:] else len(d)].decode('utf8','replace')
stages={0:'pixel',1:'vertex',2:'geometry',3:'hull',4:'domain',5:'compute',6:'library',7:'ray_generation',8:'intersection',9:'any_hit',10:'closest_hit',11:'miss',12:'callable',13:'mesh',14:'amplification'}
header_bytes=(P/'d3d12TokenizedProgramFormat.official.hpp').read_bytes()
assert hashlib.sha256(header_bytes).hexdigest()=='cf808b363406fce9387d7ec8daec19940175af966589c0d3e084a4c41c3f5060', 'Unexpected Microsoft token grammar revision'
header=header_bytes.decode('utf8')
enum=header[header.index('typedef enum D3D10_SB_OPCODE_TYPE'):];enum=enum[:enum.index('} D3D10_SB_OPCODE_TYPE')]
opcode_names={};enum_value=-1
for match in re.finditer(r'\b((?:D3D10|D3D11|D3D12)[A-Z0-9_]*_OPCODE_[A-Z0-9_]+)\s*(?:=\s*(0x[0-9a-fA-F]+|\d+))?\s*,',enum):
    enum_value=int(match[2],0) if match[2] else enum_value+1;opcode_names[enum_value]=match[1]
shaders=[]; spirv=[]
for res in resources:
    blob=b[res['offset']:res['offset']+res['size']]; start=0
    if blob[:4]==b'\x03\x02\x23\x07' and len(blob)%4==0 and len(blob)>=20:
        words=struct.unpack('<'+'I'*(len(blob)//4),blob); names={}; decorations={}; entry=[]; modes=[]; caps=[]; histogram={}; at=5;valid=True
        def spstr(v):return struct.pack('<'+'I'*len(v),*v).split(b'\0',1)[0].decode('utf8','replace')
        while at<len(words):
            wc=words[at]>>16;op=words[at]&0xffff
            if not wc or at+wc>len(words):valid=False;break
            w=words[at+1:at+wc];histogram[op]=histogram.get(op,0)+1
            if op==5 and len(w)>1:names[w[0]]=spstr(w[1:])
            elif op==15 and len(w)>2:entry.append({'execution_model':w[0],'id':w[1],'name':spstr(w[2:])})
            elif op==16 and len(w)>=2:modes.append({'entry_id':w[0],'mode':w[1],'literals':list(w[2:])})
            elif op==17 and w:caps.append(w[0])
            elif op==71 and len(w)>=2:decorations.setdefault(w[0],[]).append({'decoration':w[1],'literals':list(w[2:])})
            at+=wc
        bindings=[]
        for ident,dec in decorations.items():
            ds=next((x['literals'][0] for x in dec if x['decoration']==34 and x['literals']),None)
            bn=next((x['literals'][0] for x in dec if x['decoration']==33 and x['literals']),None)
            if ds is not None or bn is not None:bindings.append({'id':ident,'name':names.get(ident),'set':ds,'binding':bn})
        spirv.append({'index':len(spirv)+1,'resource_path':res['path'],'file_offset':res['offset'],'rva':res['rva'],'size':len(blob),'sha256':res['sha256'],'version':f'{(words[1]>>16)&255}.{(words[1]>>8)&255}','generator':words[2],'id_bound':words[3],'valid_instruction_stream':valid,'entry_points':entry,'execution_modes':modes,'capabilities':caps,'names':names,'bindings':bindings,'instruction_histogram':histogram})
    while True:
        at=blob.find(b'DXBC',start)
        if at<0:break
        start=at+4
        if at+32>len(blob):continue
        d=blob[at:];size=u32(d,24);nc=u32(d,28)
        if size<32 or size>len(d) or nc>100 or 32+4*nc>size:continue
        d=d[:size];chunks=[];shader={'index':len(shaders)+1,'resource_path':res['path'],'offset_in_resource':at,'file_offset':res['offset']+at,'rva':res['rva']+at,'size':size,'sha256':hashlib.sha256(d).hexdigest(),'chunks':chunks,'stage':None,'model':None,'bindings':[]}
        for j in range(nc):
            q=u32(d,32+j*4)
            if q+8>len(d):continue
            tag=d[q:q+4].decode('ascii','replace');l=u32(d,q+4);payload=d[q+8:q+8+l]
            if len(payload)!=l:continue
            item={'fourcc':tag,'size':l};chunks.append(item)
            if tag in ('SHDR','SHEX') and len(payload)>=8:
                ver=u32(payload,0);shader.update(stage=stages.get(ver>>16,str(ver>>16)),model=f'{(ver>>4)&15}.{ver&15}',token_dwords=u32(payload,4))
                words=struct.unpack('<'+'I'*(len(payload)//4),payload);ip=2;hist={};valid=True
                while ip<len(words):
                    op=words[ip]&0x7ff;wc=(words[ip]>>24)&0x7f
                    if op==53 and ip+1<len(words):wc=words[ip+1]
                    if not wc or ip+wc>len(words):valid=False;break
                    label=opcode_names.get(op,str(op));hist[label]=hist.get(label,0)+1
                    if label=='D3D11_SB_OPCODE_DCL_THREAD_GROUP' and wc==4:shader['thread_group']=list(words[ip+1:ip+4])
                    ip+=wc
                shader.update(valid_instruction_stream=valid,instruction_histogram=hist)
            if tag=='RDEF' and len(payload)>=28:
                cbc,cbo,bc,bo,ver,flags,creator=struct.unpack_from('<7I',payload)
                item.update(constant_buffer_count=cbc,binding_count=bc,creator=cstr(payload,creator))
                stride=40 if (ver&0xffff)>=0x501 else 32
                # SM5.0 RDEF resource binding table has 32-byte rows.
                stride=32
                for k in range(min(bc,1000)):
                    z=bo+k*stride
                    if z+32>len(payload):break
                    no,typ,ret,dim,samples,bind,count,fl=struct.unpack_from('<8I',payload,z)
                    shader['bindings'].append({'name':cstr(payload,no),'type':typ,'return_type':ret,'dimension':dim,'samples':samples,'bind_point':bind,'bind_count':count,'flags':fl})
                shader['constant_buffers']=[]
                for k in range(min(cbc,1000)):
                    z=cbo+k*24
                    if z+24>len(payload):break
                    no,nv,vo,sz,fl,typ=struct.unpack_from('<6I',payload,z);cb={'name':cstr(payload,no),'size':sz,'flags':fl,'type':typ,'variables':[]}
                    for k2 in range(min(nv,1000)):
                        z2=vo+k2*40
                        if z2+40>len(payload):break
                        vno,vstart,vsz,vfl,vtype,vdefault,st,nt,ss,ns=struct.unpack_from('<10I',payload,z2)
                        cb['variables'].append({'name':cstr(payload,vno),'offset':vstart,'size':vsz,'flags':vfl,'type_offset':vtype,'default_offset':vdefault})
                    shader['constant_buffers'].append(cb)
        shaders.append(shader);res['dxbc'].append(shader['index'])
summary={'target_sha256':hashlib.sha256(b).hexdigest(),'resources':resources,'shaders':shaders,'spirv':spirv,'coverage':{'resource_count':len(resources),'resource_bytes':sum(r['size'] for r in resources),'dxbc_count':len(shaders),'dxbc_bytes':sum(s['size'] for s in shaders),'spirv_count':len(spirv),'spirv_bytes':sum(s['size'] for s in spirv),'shader_count':len(shaders)+len(spirv),'shader_bytes':sum(s['size'] for s in shaders)+sum(s['size'] for s in spirv),'stages':{x:sum(s['stage']==x for s in shaders) for x in sorted(set(s['stage'] or 'unknown' for s in shaders))}}}
(P/'resources-shaders.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(summary['coverage']))
