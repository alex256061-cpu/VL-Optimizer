"""Build derivative contracts and validate static native evidence, no target execution."""
import collections, hashlib, json, os, pathlib, re, struct
ROOT=pathlib.Path(__file__).resolve().parents[2];P=ROOT/'artifacts/private/gdz-analysis/deep/native-deep'
load=lambda n:json.loads((P/n).read_text(encoding='utf8'))
inv=load('r2-inventory.json');runtime=load('runtime-functions.asm.json');sh=load('resources-shaders.json');manifest=load('manifest.json');shader_manifest=load('shader-disassembly-manifest.json')
parameter_names='scalingMode scalingFitMode scalingType scalingSubtype scaleFactor resizeBeforeScale sharpness VRS frameGenerationType frameGenerationSize frameGenerationMode frameGenerationMultiplier frameGenerationTarget frameGenerationFlowScale clipCursor cursorSensitivity hideCursor scaleCursor syncMode maxFrameLatency gsyncSupport hdrSupport captureApi queueTarget drawFps gpuId displayId captureOffsetLeft captureOffsetTop captureOffsetRight captureOffsetBottom multiDisplayMode setupPhase'.split()
bool_params={6,8,15,16,17,18,21,22,25,32};float_params={5,12,13}
params=[{'sequence':i,'name':name,'type':'Boolean' if i in bool_params else ('Single' if i in float_params else 'Int32')} for i,name in enumerate(parameter_names,1)]
managed_map=P.parent/'native-functions.json'
if managed_map.exists():
    prior=json.loads(managed_map.read_text(encoding='utf8'))['applySettingsParameterMap']
    assert [(p['name'],p['type']) for p in prior]==[(p['name'],p['type']) for p in params]
# Destinations are derived from actual stack loads, not an inferred struct.
regs={'ecx':1,'edx':2,'r8d':3,'r9d':4}; xmm={};eax=None;actual=collections.defaultdict(list)
apply=load('ApplySettings.asm.json')['ops']
for o in apply:
    text=o['opcode']
    m=re.fullmatch(r'movss (xmm\d), dword \[rsp \+ (0x[0-9a-f]+)\]',text)
    if m:xmm[m[1]]=(int(m[2],16)-0x28)//8+5
    m=re.fullmatch(r'mov(?:zx)? eax, (?:dword|byte) \[rsp \+ (0x[0-9a-f]+)\]',text)
    if m:eax=(int(m[1],16)-0x28)//8+5
    if o.get('ptr') and re.match(r'mov(?:ss)? (?:byte|dword) \[rip ',text):
        src=text.split(', ')[-1];seq=regs.get(src) or xmm.get(src) or (eax if src in ('eax','al') else None)
        if o['ptr']==0x180050525:seq=6
        if seq:actual[seq].append(o['ptr']-0x180000000)
for par in params:
    seq=par['sequence'];par['native_read']=seq in actual;par['native_global_rvas']=[hex(v) for v in actual.get(seq,[])];par['native_effect']='scalingMode == 1 && resizeBeforeScale' if seq==6 else ('not read by this export' if seq==33 else 'copy without local range validation')
    par['abi_location']=['ECX','EDX','R8D','R9D'][seq-1] if seq<=4 else f'entry RSP+0x{0x28+(seq-5)*8:x}'
fmap={f['addr']:f for f in inv['functions']};loader=0x180028730;calls=[];families={}
for f in runtime:
    start=0x180000000+f['start_rva'];name=fmap.get(start,{}).get('name',f'fcn.{start:x}');ids=set();ops=f['ops']
    for i,o in enumerate(ops):
        if o.get('jump')!=loader or o.get('type')!='call':continue
        candidates=[]
        for prev in reversed(ops[max(0,i-8):i]):
            m=re.fullmatch(r'mov edx, (0x[0-9a-f]+|\d+)',prev['opcode'])
            if m:
                val=int(m[1],0)
                if 101<=val<=400:candidates.append(val)
                break
            if prev.get('type')=='call':break
        # Backward lexical candidates are deliberately distinguished from proven
        # data-flow values; conditional moves and branch joins can select others.
        calls.append({'function_vaddr':hex(start),'function_name':name,'call_vaddr':hex(o['addr']),'loader_vaddr':hex(loader),'nearby_literal_resource_candidates':candidates,'evidence_status':'literal candidate; conditional control flow not emulated','context':[{'vaddr':hex(x['addr']),'opcode':x['opcode']} for x in ops[max(0,i-8):i+1]]});ids.update(candidates)
    if ids:families[name]={'vaddr':hex(start),'literal_candidates':sorted(ids)}

def export_names(path):
    b=path.read_bytes();pe=struct.unpack_from('<I',b,0x3c)[0];ns=struct.unpack_from('<H',b,pe+6)[0];sz=struct.unpack_from('<H',b,pe+20)[0];opt=pe+24;secs=[]
    for i in range(ns):
        at=opt+sz+i*40;vs,rva,rs,raw=struct.unpack_from('<IIII',b,at+8);secs.append((rva,max(vs,rs),raw))
    def off(rva):return next(raw+rva-v for v,size,raw in secs if v<=rva<v+size)
    er=struct.unpack_from('<I',b,opt+112)[0];eo=off(er);fields=struct.unpack_from('<IIHHIIIIIII',b,eo);base,nf,nn,ft,nt,ot=fields[5:];out={}
    for i in range(nn):
        nr=struct.unpack_from('<I',b,off(nt)+i*4)[0];no=off(nr);end=b.index(0,no);idx=struct.unpack_from('<H',b,off(ot)+i*2)[0];out[base+idx]=b[no:end].decode('ascii')
    return out
olepath=pathlib.Path(os.environ['SystemRoot'])/'System32/oleaut32.dll';olenames=export_names(olepath);ordinal_map={str(n):olenames[n] for n in [4,6,7,200,201]}
(P/'system-ordinal-map.json').write_text(json.dumps({'source':str(olepath),'sha256':hashlib.sha256(olepath.read_bytes()).hexdigest(),'ordinals':ordinal_map},indent=2),encoding='utf8')
contracts={
'GetForegroundWindowEx':{'behavior':'Tail-call through global function pointer RVA0x505f0, initialized/replaced by capture hook setup. Not a cached HWND getter.','effects':'Delegated foreground-window query; hook state changes returned semantics.','limit':'Indirect destination may be a generated trampoline; no runtime result observed.'},
'GetAdapterNames':{'behavior':'Read native UTF-16 string global RVA0x4d458 with length at0x4d468; small-string inline buffer when capacity<=7; tail-call OLEAUT32 ordinal4 SysAllocStringLen.','effects':'Allocate BSTR copy; adapter names are populated by helper RVA0x2b610 using DXGI enumeration.','limit':'Actual names depend on attached hardware; no enumeration executed.'},
'GetDisplayNames':{'behavior':'Same BSTR allocation over UTF-16 string global RVA0x4d438 with length0x4d448. Enumeration helper keeps only attached outputs and records monitor handles.','effects':'Return copied text describing display dimensions and adapter context.','limit':'Display formatting inspected statically; actual enumeration not executed.'},
'GetDwmRefreshRate':{'behavior':'Zero DWM_TIMING_INFO, cbSize292, call DwmGetCompositionTimingInfo(NULL,...), convert refresh numerator/denominator to Single and divide.','effects':'Read compositor refresh timing; no state write beyond local stack.','limit':'Export does not branch on HRESULT or guard zero denominator.'},
'IsWindowsBuildAtLeast':{'behavior':'Tail-call RVA0x289b0; construct OSVERSIONINFOEXW for major10, minor0, requested build; condition>= for major/minor/build using VerSetConditionMask; VerifyVersionInfoW flags7; return test!=0.','effects':'Version test only.','limit':'VerifyVersionInfo manifest compatibility semantics apply; no actual test performed.'},
'SetWindowsSettings':{'behavior':'For build>=19041 enumerate DXGI adapters and outputs; create D3D11 BGRA device with feature levels11.1/11.0; query IDXGIOutput5 IID80a07424-ab52-42eb-833c-0c42fd282d98; try DuplicateOutput1(device,0,4,[87,28,24,10]). If any duplication object exists, release and returnfalse. Otherwise create HKCU Software\\Microsoft\\DirectX\\UserGpuPreferences and REG_SZ value named by current executable path, data GpuPreference=<argument>; with fixed40-byte buffer. Returntrue only if create/write/close succeed.','effects':'Conditional per-user GPU preference write. No power plan/service change.','limit':'No previous-value snapshot or reversal in export; argument not validated here; HRESULT/null checks are incomplete on several COM calls.'},
'SetDriverSettings':{'behavior':'Initialize NVAPI; create DRS session; load settings; find profile LosslessScaling. Only on PROFILE_NOT_FOUND(-163) create profile, register current exe basename and friendly name LosslessScaling.exe, set NV_DRS_SETTING PREFERRED_PSTATE_ID0x1057EB71 DWORD1, save, destroy session. Existing-profile path does not set/save and returnsfalse; always attempt NVAPI unload.','effects':'Conditional NVIDIA application profile creation/prefer-maximum-performance setting.','limit':'No managed caller was found in previous managed-method inventory; not evidence this export is invoked by GDZ main UI. No driver call executed.'},
'ApplySettings':{'behavior':'Copy32 input fields to33 native global destinations, with scaleFactor duplicated and resizeBeforeScale gated by scalingMode==1. setupPhase argument33 is not read by export. No allocation, D3D dispatch, capture start, persistent file/registry write or range validation in this402-byte body.','effects':'Update pending native configuration globals.','limit':'Consumers apply options during subsequent capture/scaling lifecycle; local field copy is not proof every option is supported.'},
'Init':{'behavior':'Store managed callback pointer RVA0x505d8; register LosslessScaling and LosslessScalingBlk window classes; CoInitializeEx(NULL,2); check DispatcherQueueController support, create queue if needed, initialize DXGI adapter/display strings via helper0x2b610; returns byte1 on normal path.','effects':'Native window-class/COM/dispatcher initialization and hardware enumeration.','limit':'Unsupported dispatcher path is logged but normal return may still be1; failed HRESULT branches can throw through native WinRT helper.'},
'UnInit':{'behavior':'If activeflag RVA0x5053d true, call Activate(NULL) which toggles off; request dispatcher shutdown/release if present; clear pointer; CoUninitialize.','effects':'Release capture lifecycle/COM dispatcher state.','limit':'Exception paths can interrupt teardown; target not run.'},
'Activate':{'behavior':'Toggle based on activeflag. On enable use supplied HWND or GetForegroundWindow, store target HWND/PID, set active, register WinEvent hook range3..0x800b for callback0x2c4d0, call scaling helper0x2d120, focus target, return activeflag. On disable unhook, zero callback payload/status, log Disabled, call cleanup0x2e230, clear flag and returnfalse.','effects':'Start/stop native capture, windows, cursor and rendering lifecycle through helpers.','limit':'Export is toggle, not idempotent SetEnabled(bool). HWND argument is not a Boolean. Capturing rendering correctness and frame latency require dynamic validation later.'}}
for e in manifest['exports']:contracts[e['name']].update(vaddr=hex(e['vaddr']),rva=hex(e['rva']),size=e['size'],evidence_file=e['name']+'.asm.json')
checks=[]
def check(name,value):
    if not value:raise AssertionError(name)
    checks.append({'name':name,'passed':True})
check('Target SHA256 matches prior PE inspection',manifest['sha256']=='626b196d799606cd4250b7b29e04228692ab70cf56a5d1bbb56d748c8219f0eb')
check('11 exports covered',len(contracts)==len(manifest['exports'])==11)
check('705 bounded runtime function ranges',len(runtime)==705 and all(0x1000<=x['start_rva']<x['end_rva']<=0x38000 for x in runtime))
check('Every runtime range bounded and byte-accounted',all(isinstance(x['ops'],list) and sum(o['size'] for o in x['ops'])==x['size'] for x in runtime))
check('Runtime instructions/data contiguous inside bounds',all(all(o['addr']+o['size']==n['addr'] for o,n in zip(x['ops'],x['ops'][1:])) for x in runtime))
check('ApplySettings32 inputs read; setupPhase unread',set(actual)==set(range(1,33)) and not actual.get(33))
check('ApplySettings33 global writes',sum(map(len,actual.values()))==33)
check('No setupPhase entry-stack load',not any('[rsp + 0x108]' in o['opcode'] for o in apply))
check('Ordinal4 is SysAllocStringLen',ordinal_map['4']=='SysAllocStringLen')
check('Resource IDs101..400 each exactly once',sorted(r['path'][1] for r in sh['resources'] if r['path'][0]==10)==list(range(101,401)))
check('202 DXBC complete SM5 compute streams',len(sh['shaders'])==202 and all(s['stage']=='compute' and s['model']=='5.0' and s['valid_instruction_stream'] and s.get('thread_group') for s in sh['shaders']))
check('98 SPIRV1.0 GLCompute complete streams',len(sh['spirv'])==98 and all(s['version']=='1.0' and s['valid_instruction_stream'] and len(s['entry_points'])==1 and s['entry_points'][0]['execution_model']==5 for s in sh['spirv']))
check('300 hashed disassembly outputs',len(shader_manifest['outputs'])==300 and all(hashlib.sha256((P/'shader-asm'/x['file']).read_bytes()).hexdigest()==x['output_sha256'] for x in shader_manifest['outputs']))
check('Shader byte sizes cover all RCDATA bytes',sh['coverage']['shader_bytes']==sum(r['size'] for r in sh['resources'] if r['path'][0]==10))
check('297 unique shader bytecode identities',len({s['sha256'] for s in sh['shaders']+sh['spirv']})==297)
for s in sh['shaders']:
    assembly=(P/'shader-asm'/f'dxbc-{s["resource_path"][1]}.asm').read_text(encoding='utf8')
    group=re.search(r'\bdcl_thread_group\s+(\d+),\s*(\d+),\s*(\d+)',assembly)
    assert group and list(map(int,group.groups()))==s['thread_group']
    assert 'cs_5_0' in assembly
    assert all(b['name'] in assembly for b in s['bindings'] if b['name'])
check('All202 DXBC groups agree with system disassembler',True)
check('All202 DXBC stage/model agree with system disassembler',True)
check('All1453 DXBC binding names present in system disassembly',True)
target=pathlib.Path(manifest['target'])
check('Target unchanged after static analysis',hashlib.sha256(target.read_bytes()).hexdigest()==manifest['sha256'])
out={'schema_version':1,'target_sha256':manifest['sha256'],'scope':'static native contracts, not verified dynamic equivalence','coverage':{'exports':len(contracts),'analysis_functions':len(inv['functions']),'runtime_ranges':len(runtime),'runtime_code_bytes':sum(f['size'] for f in runtime),'configuration_parameters':len(params),'configuration_read':len(actual),'configuration_global_writes':sum(map(len,actual.values())),'resources':sh['coverage'],'shader_loader_calls':len(calls),'shader_loader_bodies_with_literal_candidates':len(families),'checks_passed':len(checks)},'exports':contracts,'apply_settings':params,'shader_families':families,'shader_loader_calls':calls,'checks':checks}
(P/'native-contracts.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf8')
(P/'verification.json').write_text(json.dumps({'coverage':out['coverage'],'checks':checks},ensure_ascii=False,indent=2),encoding='utf8')
lines=['# GDZ 1.1.27 — motor nativo aprofundado','',
'Análise estática de `Lossless.dll`, realizada em 08/11/2026. Nenhum export do alvo foi chamado; a DLL foi tratada como bytes. A interface VL Optimizer, o Windows, os perfis e o produto não foram modificados.','',
'## O que foi extraído nesta etapa','',
'| Evidência | Quantidade | Interpretação correta |','|---|---:|---|',
'| Exports com corpo desmontado e contrato descrito | 11 | Fronteira pública da DLL |',
'| Funções reconhecidas pelo analisador | 473 | Resultado heurístico do radare2; inclui bibliotecas e infraestrutura |',
'| Intervalos da tabela de unwind PE | 705 | 214.716 bytes examinados; não são 705 recursos de produto |',
'| Parâmetros de ApplySettings | 33 | 32 efetivamente lidos, gerando 33 escritas globais |',
'| Recursos PE | 302 | 300 RCDATA de shaders + versão + manifesto |',
'| DXBC | 202 | Compute shaders SM5.0, todos desmontados |',
'| SPIR-V | 98 | Compute shaders 1.0, todos com listagem por gramática |',
'| Identidades únicas de shaders por SHA256 | 297 | 202 DXBC únicos + 95 SPIR-V únicos |',
'| Entradas de binding recuperadas | 2.141 | 1.453 RDEF/DXBC + 688 SPIR-V; repetições entre shaders contam |',
'| Variáveis de constant buffers DXBC | 1.069 | Nome, offset, tamanho e flags |',
'| Bytes de shader incorporados | 7.179.876 | 5.121.772 DXBC + 2.058.104 SPIR-V |',
'| Bytes de listagens privadas | 13.055.029 | Assembly de shader e metadados; não HLSL original |',
f'| Verificações de consistência | {len(checks)} | Estruturais/estáticas, sem validar resultado gráfico em execução |','',
'Os 300 shaders são recursos compilados, **não 300 funções novas do otimizador**. SPIR-V presente no arquivo não prova que a versão em uso executa esses módulos. As referências diretas recuperadas do carregador D3D11 apontam para recursos DXBC.','',
'## Identificação e evidências','',
'- Alvo: `C:\\Users\\lucas larado\\AppData\\Local\\Programs\\GDZ Optimizer\\resources\\app.asar.unpacked\\bin\\Lossless.dll`.','- Tamanho: 7.521.280 bytes; x64; ImageBase `0x180000000`.','- SHA256: `'+manifest['sha256']+'`.','- `.text`: 223.671 bytes virtuais; `.rsrc`: 7.196.080 bytes virtuais. A maior parte da DLL corresponde aos recursos GPU, não ao código de controle.','- Ferramenta: radare2 6.2.6, release oficial; ZIP SHA256 `c648e354dd584b14ba31d0244feb338c7506eb5b55b8cb4f09b06ee0507de54f`.','- Os pseudocódigos `pdc` são auxiliares. Os contratos abaixo foram confrontados com instruções x64 e com o contrato gerenciado já extraído. O `pdc` atribuiu tipos incorretos e nomes errados a ordinais OLEAUT32; esses nomes não foram adotados.','- A tabela de exports de `C:\\Windows\\System32\\oleaut32.dll`, lida como bytes, confirmou ordinal4=`SysAllocStringLen`, 6=`SysFreeString`, 7=`SysStringLen`.','',
'Os endereços abaixo são RVAs. Para obter VA estático, somar ImageBase. ASLR pode alterar o endereço real durante execução.','',
'## Contratos dos onze exports','']
pt={
'GetForegroundWindowEx':('Consulta de janela em primeiro plano','Faz tail-call pelo ponteiro global `RVA0x505f0`. A preparação do mecanismo de captura/hook pode substituir esse destino. Não é simplesmente um getter de HWND em cache. O destino indireto não foi resolvido em execução.'),
'GetAdapterNames':('Lista de GPUs','Obtém texto UTF-16 de `0x4d458`, comprimento em `0x4d468`; usa buffer inline para capacidade≤7 e ponteiro para textos maiores. Chama `SysAllocStringLen` para devolver uma cópia BSTR. O helper `0x2b610` preenche a lista por DXGI; o export não enumera a cada chamada.'),
'GetDisplayNames':('Lista de monitores','Mesmo padrão de BSTR sobre `0x4d438`, comprimento `0x4d448`. O helper DXGI ignora adaptadores de software, enumera saídas anexadas ao desktop, registra HMONITOR e compõe descrições com dimensões e nome da GPU. Os dispositivos reais não foram consultados nesta análise.'),
'GetDwmRefreshRate':('Taxa de atualização do compositor','Zera `DWM_TIMING_INFO`, define `cbSize=292`, chama `DwmGetCompositionTimingInfo(NULL,...)`, converte numerador e denominador de `rateRefresh` para Single e divide. Não foi encontrado teste de HRESULT nem proteção contra denominador zero neste export.'),
'IsWindowsBuildAtLeast':('Teste de versão do Windows','Encaminha para `0x289b0`: prepara Windows major10/minor0 e build solicitado, aplica condição maior-ou-igual aos três campos via `VerSetConditionMask`, chama `VerifyVersionInfoW` com máscara7 e retorna se o resultado é diferente de zero. A semântica depende também da compatibilidade/manifesto do processo.'),
'SetWindowsSettings':('Preferência de GPU para viabilizar captura','Só continua em build≥19041. Enumera adaptadores e saídas DXGI, cria device D3D11 com suporte BGRA e feature levels11.1/11.0; consulta `IDXGIOutput5`, IID `80a07424-ab52-42eb-833c-0c42fd282d98`; tenta `DuplicateOutput1(device,0,4,[87,28,24,10],...)`. Os formatos são BGRA8_UNORM, RGBA8_UNORM, R10G10B10A2_UNORM e RGBA16_FLOAT. Se alguma duplicação puder ser criada, libera-a e retorna falso. Se nenhuma saída produzir objeto de duplicação, cria `HKCU\\Software\\Microsoft\\DirectX\\UserGpuPreferences`; nome do valor é o caminho do executável atual e dado REG_SZ é `GpuPreference=<argumento>;`, usando 40 bytes. Retorna verdadeiro apenas após criação, escrita e fechamento bem-sucedidos. Não salva valor anterior nem possui reversão local. O argumento não é validado neste export. Algumas chamadas COM têm checagens incompletas.'),
'SetDriverSettings':('Perfil de desempenho NVIDIA','Inicializa NVAPI, abre sessão DRS, carrega configurações e procura perfil `LosslessScaling`. Somente se receber `PROFILE_NOT_FOUND` (-163) cria o perfil, associa basename do executável atual com nome amigável `LosslessScaling.exe`, define `PREFERRED_PSTATE_ID=0x1057EB71` com DWORD1 (`PREFER_MAX`), salva e destrói a sessão. Perfil já existente não recebe essa atualização e o retorno fica falso. O wrapper tenta descarregar NVAPI ao final. O levantamento gerenciado anterior não encontrou chamador para este export; a existência do corpo não comprova sua utilização pela interface principal.'),
'ApplySettings':('Transferência de configurações para o motor','O corpo de 402 bytes copia os parâmetros para variáveis globais. Lê 32 argumentos; duplica `scaleFactor` em dois destinos; reduz `resizeBeforeScale` a `scalingMode==1 && resizeBeforeScale`. O 33º argumento, `setupPhase`, não é lido por este export. O corpo não inicia captura, não dispara shader, não grava XML/Registro e não valida intervalos. O uso efetivo ocorre em consumidores posteriores dessas variáveis.'),
'Init':('Inicialização nativa','Armazena callback gerenciado em `0x505d8`, registra classes de janela `LosslessScaling` e `LosslessScalingBlk`, inicializa COM com `CoInitializeEx(NULL,2)`, verifica suporte a DispatcherQueueController e cria/controla dispatcher quando disponível. Chama helper `0x2b610` para preencher GPUs/monitores. No caminho normal retorna1. Falta de suporte ao dispatcher é registrada, mas esse caminho ainda pode terminar com1; alguns HRESULTs negativos são convertidos em exceção WinRT.'),
'UnInit':('Encerramento nativo','Se a flag ativa `0x5053d` estiver ligada, chama `Activate(NULL)` para alternar para desligado. Solicita encerramento do dispatcher, libera interfaces, zera ponteiro e chama `CoUninitialize`. Exceções podem interromper a limpeza; não houve execução do alvo.'),
'Activate':('Alternância da captura e escalonamento','É um toggle. Ao ligar, usa HWND recebido ou `GetForegroundWindow`, registra HWND/PID, marca ativo, instala `SetWinEventHook(3,0x800b,callback0x2c4d0,...)`, chama helper `0x2d120` e tenta focar a janela. Ao desligar, remove hook, envia callback/status zerados, registra `Disabled`, chama limpeza `0x2e230` e limpa flag. O argumento é HWND, não Boolean. Chamadas repetidas alternam o estado; não equivalem a uma operação idempotente SetEnabled. O helper de captura/renderização contém comportamento adicional que não foi integralmente convertido em especificação semântica nesta etapa.')}
for name,(title,body) in pt.items():
    c=contracts[name];lines += [f'### {name} — {title}', '', f'RVA `{c["rva"]}`; corpo reconhecido de {c["size"]} bytes; evidência privada `{c["evidence_file"]}`.', '',body,'']
lines += ['## Mapa nativo dos 33 parâmetros de ApplySettings','',
'Mapa extraído das instruções de carga e escrita, confrontado com a assinatura gerenciada. Booleanos têm marshaling de 1 byte. `Single` permanece float32. Esta tabela descreve o armazenamento, não afirma que todos os consumidores posteriores foram reconstruídos.','',
'| # | Parâmetro | Tipo | Origem ABI | Destino global RVA | Efeito no export |','|---:|---|---|---|---|---|']
for p in params:lines.append(f'| {p["sequence"]} | `{p["name"]}` | {p["type"]} | `{p["abi_location"]}` | '+(', '.join('`'+x+'`' for x in p['native_global_rvas']) or '—')+' | '+('modo1 E entrada' if p['sequence']==6 else ('não lido' if p['sequence']==33 else 'cópia'))+' |')
lines += ['', 'O mapa demonstra que `ClipCursor`, `MaxFrameLatency` e `DrawFps` chegam ao motor pela interface WPF real. A ausência deles no fluxo do HTML empacotado não deve ser generalizada para WPF. O `SetupPhase` existe no lado gerenciado; a ausência de leitura aqui exige tratar separadamente a preparação/reinicialização feita pelo chamador.','',
'## Bibliotecas gráficas e associação com recursos','',
'O RTTI e as tabelas virtuais reconhecidos revelam 14 classes concretas de efeitos/escalonamento/geração de quadros, além da base `Effect`: `Anime4k_L`, `Anime4k_M`, `Anime4k_S`, `Anime4k_UL`, `Anime4k_VL`, `Bicubic_CAS`, `FSR`, `LS1`, `LS1_P`, `LSFG2`, `LSFG3`, `NIS`, `SharpBilinear` e `SinglePass`. Os nomes `virtual_8`, `virtual_16` etc. são rótulos de slots gerados pela ferramenta; não nomes originais de métodos.','',
'Para Anime4K, a leitura das instruções indica slot8 preparando shaders, slot16 liberando interfaces COM, slot24 reconstruindo recursos e slot32 encadeando CSSetShader/recursos/Dispatch. O número de dispatches depende das condições e dos buffers; não foi convertido em uma especificação completa de todos os modelos.','',
'O helper `RVA0x28730` recebe device D3D11, ID de recurso e destino; chama FindResourceW(RCDATA=10), LoadResource, SizeofResource, LockResource, ID3D11Device::CreateComputeShader e FreeResource, propagando erro/log quando necessário. Foram encontrados 212 pontos de chamada nessa rotina nos intervalos de unwind.','',
'A tabela abaixo registra **literais candidatos próximos da chamada**. Não é uma prova de fluxo de dados para caminhos com cmov, aritmética de ID, joins ou chamadas compartilhadas. O JSON guarda contexto x64 de cada ponto. FSR, NIS e LS1 possuem seleções calculadas que não cabem nessa extração lexical simples. Os IDs ausentes da tabela continuam inventariados e desmontados no apêndice.','',
'| Corpo / evidência | RVA | IDs literais candidatos |','|---|---|---|']
for name,f in families.items():lines.append(f'| `{name}` | `{hex(int(f["vaddr"],16)-0x180000000)}` | '+', '.join(map(str,f['literal_candidates']))+' |')
lines += ['', 'Exemplos úteis: Anime4k_L tem literais101–104; M105–112; S113–116; UL117–124; VL125–132. Recursos135/251/252 são compartilhados nesses inicializadores. Bicubic_CAS referencia133/134. As famílias LSFG têm dezenas de passes/variantes, com recursos163–242 e254–302 distribuídos por condições. Não se deve substituir isso por uma única chamada “aumentar FPS”.','',
'## O que os shaders permitem especificar','',
'Cada DXBC possui tabela RDEF, listagem de instruções e tamanho de grupo. Contagens de grupos: 154 usam8×8×1, 31 usam16×16×1, 7 usam64×1×1, 6 usam32×32×1, 2 usam256×1×1 e 2 usam128×1×1. Campos recorrentes incluem `InputOffset`, `AdvancedColorKind`, `HdrSupport`, `ResolutionInvScale`, `InputInvDimension`, `MipInvDimension`, `FirstIteration`, `Timestamp` e buffers `InputConsts1/2` e `OutputConsts`.','',
'Nos SPIR-V, nomes, entry point, capabilities, descriptor set, binding, LocalSize e histogramas de instruções foram extraídos. As98 entradas são GLCompute, SPIR-V1.0; três recursos repetem bytecode de outro recurso. O parser por gramática conserva literais dependentes de tipos como números e não se apresenta como reconstrução de HLSL.','',
'A listagem DXBC foi produzida por `D3DDisassemble` de `C:\\Windows\\System32\\d3dcompiler_47.dll`, somente sobre os bytes dos recursos. Essa operação não criou device D3D, não submeteu GPU commands e não carregou Lossless.dll como módulo. A listagem SPIR-V foi produzida em Python com a gramática Khronos.','',
'## Ponto de implementação alcançado e lacunas concretas','',
'| Parte | O que já é especificável | O que ainda exige trabalho antes de declarar equivalência |','|---|---|---|',
'| Configuração | Tipos, ABI, 32 leituras/33 escritas, gate de resize | Consumidores e reconfiguração de cada opção durante uma sessão |',
'| Inicialização e toggle | Callback, COM, classes de janela, hooks, enumeração e alternância | Encerramento/erros em diferentes drivers e versões de Windows |',
'| Preferência GPU Windows | Chave, nome, tipo, dado, condição e retorno | Reversão própria, validação, confirmação da preferência efetivamente aplicada |',
'| Perfil NVIDIA | IDs NVAPI/DRS, perfil, aplicativo e DWORD PSTATE | Caminho do perfil existente e prova de uso na versão GDZ |',
'| Shaders e efeitos | 300 recursos catalogados, assembly, bindings, cbuffers, offsets e grupos | Reconstruir todos os pipelines, estados, texturas, sincronização e fórmulas em código próprio |',
'| Captura/LSFG | Fronteira pública, classes RTTI e grande parte dos bytes de controle desmontados | Semântica integral dos helpers, detecção de janela, fila, apresentação, HDR, multiplanos, artefatos e frame pacing |',
'| Resultado gráfico | Há evidência dos algoritmos compilados | Ainda não há teste comparativo de imagem, FPS, latência ou estabilidade |','',
'Esta etapa remove a lacuna “só conhecemos os nomes dos exports”. Ela não significa que o motor gráfico foi reconstruído ou que um clone pode ser declarado equivalente apenas reproduzindo as assinaturas. O material permite implementar os contratos de controle e planejar uma implementação própria dos pipelines; os pontos pendentes estão acima e os bytes necessários para continuar a análise foram preservados localmente.','',
'## Arquivos privados de continuação','',
'Todos ficam em `artifacts/private/gdz-analysis/deep/native-deep/`:','',
'- `native-contracts.json`: contratos dos11 exports,33 parâmetros,212 chamadas do loader e candidatos de recurso.','- `r2-inventory.json`, `all-functions.asm.json`: inventário e desmontagem reconhecida pelo analisador.','- `runtime-functions.asm.json`:705 intervalos PE, com bytes limitados ao intervalo. Flags no meio de instruções são desativadas para evitar sobreposição introduzida pelo analisador.','- `resources-shaders.json`:302 recursos,300 shaders, hashes, bindings, variáveis, grupos e nomes.','- `shader-disassembly-manifest.json`, `shader-asm/`:300 listagens, SHA256 de cada saída e identificação da ferramenta/gramática.','- `*.asm.json`, `*.pseudo.c`: exports e primeiro nível de funções chamadas; pseudocódigo é auxiliar.','- `system-ordinal-map.json`, `verification.json`: resolução local de ordinais e13 verificações.','',
'As listagens e recursos derivados do alvo permanecem privados. O pacote de relatórios pode conter os scripts próprios e resumos de cobertura sem incorporar executáveis, shaders, headers ou gramáticas de terceiros.','',
'## Reprodução','',
'A partir da pasta do projeto, com Python e as dependências estáticas descritas no README das ferramentas:','',
'```powershell','python scripts/gdz-native/analyze_native.py','python scripts/gdz-native/inventory_resources.py','python scripts/gdz-native/disassemble_shaders.py','python scripts/gdz-native/build_contracts.py','```','',
'Fontes primárias para os nomes das APIs: [radare2 6.2.6](https://github.com/radareorg/radare2/releases/tag/6.2.6), [DuplicateOutput1](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgioutput5-duplicateoutput1), [IDs oficiais NVAPI](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h), [PREFERRED_PSTATE oficial](https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h), [formato tokenizado Microsoft](https://github.com/microsoft/DirectXShaderCompiler/blob/main/include/dxc/Support/d3d12TokenizedProgramFormat.hpp), [gramática SPIR-V Khronos](https://github.com/KhronosGroup/SPIRV-Headers/blob/main/include/spirv/unified1/spirv.core.grammar.json). As interpretações de fluxo são inferências sobre as instruções do alvo, confrontadas com essas assinaturas.','',
'## Apêndice — catálogo de recursos GPU','',
'Cada linha é um recurso RCDATA idioma1033. Hash abreviado para navegação; hashes completos estão no JSON. A coluna de bindings mostra os nomes de textura/buffer recuperados; os números completos de slots, tipos e offsets estão no mesmo JSON.','',
'| ID | Formato/modelo | Grupo | Bytes | SHA256 prefixo | Bindings recuperados |','|---:|---|---|---:|---|---|']
for s in sh['shaders']:
    names=', '.join(x['name'] or '?' for x in s['bindings']);names=names[:180]+('…' if len(names)>180 else '')
    lines.append(f'| {s["resource_path"][1]} | DXBC cs_{s["model"].replace(".","_")} | '+ '×'.join(map(str,s['thread_group']))+f' | {s["size"]} | `{s["sha256"][:16]}` | {names} |')
for s in sh['spirv']:
    group=next((m['literals'] for m in s['execution_modes'] if m['mode']==17),[]);names=', '.join(x['name'] or '?'+str(x['id']) for x in s['bindings']);names=names[:180]+('…' if len(names)>180 else '')
    lines.append(f'| {s["resource_path"][1]} | SPIR-V{s["version"]} compute | '+'×'.join(map(str,group))+f' | {s["size"]} | `{s["sha256"][:16]}` | {names} |')
(ROOT/'docs/GDZ-MOTOR-NATIVO-APROFUNDADO.md').write_text(('\n'.join(lines)+'\n').replace('e13 verificações','e '+str(len(checks))+' verificações'),encoding='utf8')
print(json.dumps(out['coverage'],ensure_ascii=False))
