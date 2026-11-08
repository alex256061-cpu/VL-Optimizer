# GDZ 1.1.27 — motor nativo aprofundado

Análise estática de `Lossless.dll`, realizada em 08/11/2026. Nenhum export do alvo foi chamado; a DLL foi tratada como bytes. A interface VL Optimizer, o Windows, os perfis e o produto não foram modificados.

## O que foi extraído nesta etapa

| Evidência | Quantidade | Interpretação correta |
|---|---:|---|
| Exports com corpo desmontado e contrato descrito | 11 | Fronteira pública da DLL |
| Funções reconhecidas pelo analisador | 473 | Resultado heurístico do radare2; inclui bibliotecas e infraestrutura |
| Intervalos da tabela de unwind PE | 705 | 214.716 bytes examinados; não são 705 recursos de produto |
| Parâmetros de ApplySettings | 33 | 32 efetivamente lidos, gerando 33 escritas globais |
| Recursos PE | 302 | 300 RCDATA de shaders + versão + manifesto |
| DXBC | 202 | Compute shaders SM5.0, todos desmontados |
| SPIR-V | 98 | Compute shaders 1.0, todos com listagem por gramática |
| Identidades únicas de shaders por SHA256 | 297 | 202 DXBC únicos + 95 SPIR-V únicos |
| Entradas de binding recuperadas | 2.141 | 1.453 RDEF/DXBC + 688 SPIR-V; repetições entre shaders contam |
| Variáveis de constant buffers DXBC | 1.069 | Nome, offset, tamanho e flags |
| Bytes de shader incorporados | 7.179.876 | 5.121.772 DXBC + 2.058.104 SPIR-V |
| Bytes de listagens privadas | 13.055.029 | Assembly de shader e metadados; não HLSL original |
| Verificações de consistência | 19 | Estruturais/estáticas, sem validar resultado gráfico em execução |

Os 300 shaders são recursos compilados, **não 300 funções novas do otimizador**. SPIR-V presente no arquivo não prova que a versão em uso executa esses módulos. As referências diretas recuperadas do carregador D3D11 apontam para recursos DXBC.

## Identificação e evidências

- Alvo: `%LOCALAPPDATA%\Programs\GDZ Optimizer\resources\app.asar.unpacked\bin\Lossless.dll`.
- Tamanho: 7.521.280 bytes; x64; ImageBase `0x180000000`.
- SHA256: `626b196d799606cd4250b7b29e04228692ab70cf56a5d1bbb56d748c8219f0eb`.
- `.text`: 223.671 bytes virtuais; `.rsrc`: 7.196.080 bytes virtuais. A maior parte da DLL corresponde aos recursos GPU, não ao código de controle.
- Ferramenta: radare2 6.2.6, release oficial; ZIP SHA256 `c648e354dd584b14ba31d0244feb338c7506eb5b55b8cb4f09b06ee0507de54f`.
- Os pseudocódigos `pdc` são auxiliares. Os contratos abaixo foram confrontados com instruções x64 e com o contrato gerenciado já extraído. O `pdc` atribuiu tipos incorretos e nomes errados a ordinais OLEAUT32; esses nomes não foram adotados.
- A tabela de exports de `C:\Windows\System32\oleaut32.dll`, lida como bytes, confirmou ordinal4=`SysAllocStringLen`, 6=`SysFreeString`, 7=`SysStringLen`.

Os endereços abaixo são RVAs. Para obter VA estático, somar ImageBase. ASLR pode alterar o endereço real durante execução.

## Contratos dos onze exports

### GetForegroundWindowEx — Consulta de janela em primeiro plano

RVA `0x2b9d0`; corpo reconhecido de 7 bytes; evidência privada `GetForegroundWindowEx.asm.json`.

Faz tail-call pelo ponteiro global `RVA0x505f0`. A preparação do mecanismo de captura/hook pode substituir esse destino. Não é simplesmente um getter de HWND em cache. O destino indireto não foi resolvido em execução.

### GetAdapterNames — Lista de GPUs

RVA `0x2b9e0`; corpo reconhecido de 36 bytes; evidência privada `GetAdapterNames.asm.json`.

Obtém texto UTF-16 de `0x4d458`, comprimento em `0x4d468`; usa buffer inline para capacidade≤7 e ponteiro para textos maiores. Chama `SysAllocStringLen` para devolver uma cópia BSTR. O helper `0x2b610` preenche a lista por DXGI; o export não enumera a cada chamada.

### GetDisplayNames — Lista de monitores

RVA `0x2ba10`; corpo reconhecido de 36 bytes; evidência privada `GetDisplayNames.asm.json`.

Mesmo padrão de BSTR sobre `0x4d438`, comprimento `0x4d448`. O helper DXGI ignora adaptadores de software, enumera saídas anexadas ao desktop, registra HMONITOR e compõe descrições com dimensões e nome da GPU. Os dispositivos reais não foram consultados nesta análise.

### GetDwmRefreshRate — Taxa de atualização do compositor

RVA `0x2ba80`; corpo reconhecido de 116 bytes; evidência privada `GetDwmRefreshRate.asm.json`.

Zera `DWM_TIMING_INFO`, define `cbSize=292`, chama `DwmGetCompositionTimingInfo(NULL,...)`, converte numerador e denominador de `rateRefresh` para Single e divide. Não foi encontrado teste de HRESULT nem proteção contra denominador zero neste export.

### IsWindowsBuildAtLeast — Teste de versão do Windows

RVA `0x2ba70`; corpo reconhecido de 5 bytes; evidência privada `IsWindowsBuildAtLeast.asm.json`.

Encaminha para `0x289b0`: prepara Windows major10/minor0 e build solicitado, aplica condição maior-ou-igual aos três campos via `VerSetConditionMask`, chama `VerifyVersionInfoW` com máscara7 e retorna se o resultado é diferente de zero. A semântica depende também da compatibilidade/manifesto do processo.

### SetWindowsSettings — Preferência de GPU para viabilizar captura

RVA `0x2bb00`; corpo reconhecido de 896 bytes; evidência privada `SetWindowsSettings.asm.json`.

Só continua em build≥19041. Enumera adaptadores e saídas DXGI, cria device D3D11 com suporte BGRA e feature levels11.1/11.0; consulta `IDXGIOutput5`, IID `80a07424-ab52-42eb-833c-0c42fd282d98`; tenta `DuplicateOutput1(device,0,4,[87,28,24,10],...)`. Os formatos são BGRA8_UNORM, RGBA8_UNORM, R10G10B10A2_UNORM e RGBA16_FLOAT. Se alguma duplicação puder ser criada, libera-a e retorna falso. Se nenhuma saída produzir objeto de duplicação, cria `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`; nome do valor é o caminho do executável atual e dado REG_SZ é `GpuPreference=<argumento>;`, usando 40 bytes. Retorna verdadeiro apenas após criação, escrita e fechamento bem-sucedidos. Não salva valor anterior nem possui reversão local. O argumento não é validado neste export. Algumas chamadas COM têm checagens incompletas.

### SetDriverSettings — Perfil de desempenho NVIDIA

RVA `0x2ba40`; corpo reconhecido de 39 bytes; evidência privada `SetDriverSettings.asm.json`.

Inicializa NVAPI, abre sessão DRS, carrega configurações e procura perfil `LosslessScaling`. Somente se receber `PROFILE_NOT_FOUND` (-163) cria o perfil, associa basename do executável atual com nome amigável `LosslessScaling.exe`, define `PREFERRED_PSTATE_ID=0x1057EB71` com DWORD1 (`PREFER_MAX`), salva e destrói a sessão. Perfil já existente não recebe essa atualização e o retorno fica falso. O wrapper tenta descarregar NVAPI ao final. O levantamento gerenciado anterior não encontrou chamador para este export; a existência do corpo não comprova sua utilização pela interface principal.

### ApplySettings — Transferência de configurações para o motor

RVA `0x2be80`; corpo reconhecido de 402 bytes; evidência privada `ApplySettings.asm.json`.

O corpo de 402 bytes copia os parâmetros para variáveis globais. Lê 32 argumentos; duplica `scaleFactor` em dois destinos; reduz `resizeBeforeScale` a `scalingMode==1 && resizeBeforeScale`. O 33º argumento, `setupPhase`, não é lido por este export. O corpo não inicia captura, não dispara shader, não grava XML/Registro e não valida intervalos. O uso efetivo ocorre em consumidores posteriores dessas variáveis.

### Init — Inicialização nativa

RVA `0x2c020`; corpo reconhecido de 651 bytes; evidência privada `Init.asm.json`.

Armazena callback gerenciado em `0x505d8`, registra classes de janela `LosslessScaling` e `LosslessScalingBlk`, inicializa COM com `CoInitializeEx(NULL,2)`, verifica suporte a DispatcherQueueController e cria/controla dispatcher quando disponível. Chama helper `0x2b610` para preencher GPUs/monitores. No caminho normal retorna1. Falta de suporte ao dispatcher é registrada, mas esse caminho ainda pode terminar com1; alguns HRESULTs negativos são convertidos em exceção WinRT.

### UnInit — Encerramento nativo

RVA `0x2c2b0`; corpo reconhecido de 160 bytes; evidência privada `UnInit.asm.json`.

Se a flag ativa `0x5053d` estiver ligada, chama `Activate(NULL)` para alternar para desligado. Solicita encerramento do dispatcher, libera interfaces, zera ponteiro e chama `CoUninitialize`. Exceções podem interromper a limpeza; não houve execução do alvo.

### Activate — Alternância da captura e escalonamento

RVA `0x2c350`; corpo reconhecido de 227 bytes; evidência privada `Activate.asm.json`.

É um toggle. Ao ligar, usa HWND recebido ou `GetForegroundWindow`, registra HWND/PID, marca ativo, instala `SetWinEventHook(3,0x800b,callback0x2c4d0,...)`, chama helper `0x2d120` e tenta focar a janela. Ao desligar, remove hook, envia callback/status zerados, registra `Disabled`, chama limpeza `0x2e230` e limpa flag. O argumento é HWND, não Boolean. Chamadas repetidas alternam o estado; não equivalem a uma operação idempotente SetEnabled. O helper de captura/renderização contém comportamento adicional que não foi integralmente convertido em especificação semântica nesta etapa.

## Mapa nativo dos 33 parâmetros de ApplySettings

Mapa extraído das instruções de carga e escrita, confrontado com a assinatura gerenciada. Booleanos têm marshaling de 1 byte. `Single` permanece float32. Esta tabela descreve o armazenamento, não afirma que todos os consumidores posteriores foram reconstruídos.

| # | Parâmetro | Tipo | Origem ABI | Destino global RVA | Efeito no export |
|---:|---|---|---|---|---|
| 1 | `scalingMode` | Int32 | `ECX` | `0x50570` | cópia |
| 2 | `scalingFitMode` | Int32 | `EDX` | `0x505cc` | cópia |
| 3 | `scalingType` | Int32 | `R8D` | `0x505c0` | cópia |
| 4 | `scalingSubtype` | Int32 | `R9D` | `0x5055c` | cópia |
| 5 | `scaleFactor` | Single | `entry RSP+0x28` | `0x4d384`, `0x505ec` | cópia |
| 6 | `resizeBeforeScale` | Boolean | `entry RSP+0x30` | `0x50525` | modo1 E entrada |
| 7 | `sharpness` | Int32 | `entry RSP+0x38` | `0x4d3c8` | cópia |
| 8 | `VRS` | Boolean | `entry RSP+0x40` | `0x5058c` | cópia |
| 9 | `frameGenerationType` | Int32 | `entry RSP+0x48` | `0x50578` | cópia |
| 10 | `frameGenerationSize` | Int32 | `entry RSP+0x50` | `0x50554` | cópia |
| 11 | `frameGenerationMode` | Int32 | `entry RSP+0x58` | `0x5059c` | cópia |
| 12 | `frameGenerationMultiplier` | Single | `entry RSP+0x60` | `0x505c4` | cópia |
| 13 | `frameGenerationTarget` | Single | `entry RSP+0x68` | `0x505a8` | cópia |
| 14 | `frameGenerationFlowScale` | Int32 | `entry RSP+0x70` | `0x505b0` | cópia |
| 15 | `clipCursor` | Boolean | `entry RSP+0x78` | `0x4d301` | cópia |
| 16 | `cursorSensitivity` | Boolean | `entry RSP+0x80` | `0x5053e` | cópia |
| 17 | `hideCursor` | Boolean | `entry RSP+0x88` | `0x50527` | cópia |
| 18 | `scaleCursor` | Boolean | `entry RSP+0x90` | `0x5057e` | cópia |
| 19 | `syncMode` | Int32 | `entry RSP+0x98` | `0x4d3c4` | cópia |
| 20 | `maxFrameLatency` | Int32 | `entry RSP+0xa0` | `0x4d3cc` | cópia |
| 21 | `gsyncSupport` | Boolean | `entry RSP+0xa8` | `0x5053c` | cópia |
| 22 | `hdrSupport` | Boolean | `entry RSP+0xb0` | `0x5057c` | cópia |
| 23 | `captureApi` | Int32 | `entry RSP+0xb8` | `0x50588` | cópia |
| 24 | `queueTarget` | Int32 | `entry RSP+0xc0` | `0x505d0` | cópia |
| 25 | `drawFps` | Boolean | `entry RSP+0xc8` | `0x5057d` | cópia |
| 26 | `gpuId` | Int32 | `entry RSP+0xd0` | `0x505c8` | cópia |
| 27 | `displayId` | Int32 | `entry RSP+0xd8` | `0x50558` | cópia |
| 28 | `captureOffsetLeft` | Int32 | `entry RSP+0xe0` | `0x505d4` | cópia |
| 29 | `captureOffsetTop` | Int32 | `entry RSP+0xe8` | `0x505a4` | cópia |
| 30 | `captureOffsetRight` | Int32 | `entry RSP+0xf0` | `0x50550` | cópia |
| 31 | `captureOffsetBottom` | Int32 | `entry RSP+0xf8` | `0x50598` | cópia |
| 32 | `multiDisplayMode` | Boolean | `entry RSP+0x100` | `0x50526` | cópia |
| 33 | `setupPhase` | Int32 | `entry RSP+0x108` | — | não lido |

O mapa demonstra que `ClipCursor`, `MaxFrameLatency` e `DrawFps` chegam ao motor pela interface WPF real. A ausência deles no fluxo do HTML empacotado não deve ser generalizada para WPF. O `SetupPhase` existe no lado gerenciado; a ausência de leitura aqui exige tratar separadamente a preparação/reinicialização feita pelo chamador.

## Bibliotecas gráficas e associação com recursos

O RTTI e as tabelas virtuais reconhecidos revelam 14 classes concretas de efeitos/escalonamento/geração de quadros, além da base `Effect`: `Anime4k_L`, `Anime4k_M`, `Anime4k_S`, `Anime4k_UL`, `Anime4k_VL`, `Bicubic_CAS`, `FSR`, `LS1`, `LS1_P`, `LSFG2`, `LSFG3`, `NIS`, `SharpBilinear` e `SinglePass`. Os nomes `virtual_8`, `virtual_16` etc. são rótulos de slots gerados pela ferramenta; não nomes originais de métodos.

Para Anime4K, a leitura das instruções indica slot8 preparando shaders, slot16 liberando interfaces COM, slot24 reconstruindo recursos e slot32 encadeando CSSetShader/recursos/Dispatch. O número de dispatches depende das condições e dos buffers; não foi convertido em uma especificação completa de todos os modelos.

O helper `RVA0x28730` recebe device D3D11, ID de recurso e destino; chama FindResourceW(RCDATA=10), LoadResource, SizeofResource, LockResource, ID3D11Device::CreateComputeShader e FreeResource, propagando erro/log quando necessário. Foram encontrados 212 pontos de chamada nessa rotina nos intervalos de unwind.

A tabela abaixo registra **literais candidatos próximos da chamada**. Não é uma prova de fluxo de dados para caminhos com cmov, aritmética de ID, joins ou chamadas compartilhadas. O JSON guarda contexto x64 de cada ponto. FSR, NIS e LS1 possuem seleções calculadas que não cabem nessa extração lexical simples. Os IDs ausentes da tabela continuam inventariados e desmontados no apêndice.

| Corpo / evidência | RVA | IDs literais candidatos |
|---|---|---|
| `method.Anime4k_L.virtual_8` | `0x5180` | 101, 102, 103, 104, 135, 251, 252 |
| `method.Anime4k_M.virtual_8` | `0x65c0` | 105, 106, 107, 108, 109, 110, 111, 112, 135, 251, 252 |
| `method.Anime4k_S.virtual_8` | `0x7880` | 113, 114, 115, 116, 135, 251, 252 |
| `method.Anime4k_UL.virtual_8` | `0x8bb0` | 117, 118, 119, 120, 121, 122, 123, 124, 135, 251, 252 |
| `method.Anime4k_VL.virtual_8` | `0x9d40` | 125, 126, 127, 128, 129, 130, 131, 132, 135, 251, 252 |
| `method.Bicubic_CAS.virtual_8` | `0xae60` | 133, 134 |
| `fcn.1800176bd` | `0x176bd` | 163, 164, 165, 166, 171, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 202, 203, 204, 205, 206, 207, 208, 209, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242 |
| `fcn.180017e9c` | `0x17e9c` | 167, 168, 169, 172, 173, 174, 175, 176, 177, 178, 179, 180 |
| `method.FSR.virtual_8` | `0x1cc20` | 137 |
| `method.LS1.virtual_8` | `0x1d610` | 146, 147, 148, 149, 150, 151, 153, 154, 156, 157, 159, 160 |
| `method.LS1_P.virtual_8` | `0x1e240` | 141, 146 |
| `fcn.18001fe10` | `0x1fe10` | 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301 |
| `method.SharpBilinear.virtual_8` | `0x279c0` | 249 |
| `method.SinglePass.virtual_8` | `0x28240` | 249 |

Exemplos úteis: Anime4k_L tem literais101–104; M105–112; S113–116; UL117–124; VL125–132. Recursos135/251/252 são compartilhados nesses inicializadores. Bicubic_CAS referencia133/134. As famílias LSFG têm dezenas de passes/variantes, com recursos163–242 e254–302 distribuídos por condições. Não se deve substituir isso por uma única chamada “aumentar FPS”.

## O que os shaders permitem especificar

Cada DXBC possui tabela RDEF, listagem de instruções e tamanho de grupo. Contagens de grupos: 154 usam8×8×1, 31 usam16×16×1, 7 usam64×1×1, 6 usam32×32×1, 2 usam256×1×1 e 2 usam128×1×1. Campos recorrentes incluem `InputOffset`, `AdvancedColorKind`, `HdrSupport`, `ResolutionInvScale`, `InputInvDimension`, `MipInvDimension`, `FirstIteration`, `Timestamp` e buffers `InputConsts1/2` e `OutputConsts`.

Nos SPIR-V, nomes, entry point, capabilities, descriptor set, binding, LocalSize e histogramas de instruções foram extraídos. As98 entradas são GLCompute, SPIR-V1.0; três recursos repetem bytecode de outro recurso. O parser por gramática conserva literais dependentes de tipos como números e não se apresenta como reconstrução de HLSL.

A listagem DXBC foi produzida por `D3DDisassemble` de `C:\Windows\System32\d3dcompiler_47.dll`, somente sobre os bytes dos recursos. Essa operação não criou device D3D, não submeteu GPU commands e não carregou Lossless.dll como módulo. A listagem SPIR-V foi produzida em Python com a gramática Khronos.

## Ponto de implementação alcançado e lacunas concretas

| Parte | O que já é especificável | O que ainda exige trabalho antes de declarar equivalência |
|---|---|---|
| Configuração | Tipos, ABI, 32 leituras/33 escritas, gate de resize | Consumidores e reconfiguração de cada opção durante uma sessão |
| Inicialização e toggle | Callback, COM, classes de janela, hooks, enumeração e alternância | Encerramento/erros em diferentes drivers e versões de Windows |
| Preferência GPU Windows | Chave, nome, tipo, dado, condição e retorno | Reversão própria, validação, confirmação da preferência efetivamente aplicada |
| Perfil NVIDIA | IDs NVAPI/DRS, perfil, aplicativo e DWORD PSTATE | Caminho do perfil existente e prova de uso na versão GDZ |
| Shaders e efeitos | 300 recursos catalogados, assembly, bindings, cbuffers, offsets e grupos | Reconstruir todos os pipelines, estados, texturas, sincronização e fórmulas em código próprio |
| Captura/LSFG | Fronteira pública, classes RTTI e grande parte dos bytes de controle desmontados | Semântica integral dos helpers, detecção de janela, fila, apresentação, HDR, multiplanos, artefatos e frame pacing |
| Resultado gráfico | Há evidência dos algoritmos compilados | Ainda não há teste comparativo de imagem, FPS, latência ou estabilidade |

Esta etapa remove a lacuna “só conhecemos os nomes dos exports”. Ela não significa que o motor gráfico foi reconstruído ou que um clone pode ser declarado equivalente apenas reproduzindo as assinaturas. O material permite implementar os contratos de controle e planejar uma implementação própria dos pipelines; os pontos pendentes estão acima e os bytes necessários para continuar a análise foram preservados localmente.

## Arquivos privados de continuação

Todos ficam em `artifacts/private/gdz-analysis/deep/native-deep/`:

- `native-contracts.json`: contratos dos11 exports,33 parâmetros,212 chamadas do loader e candidatos de recurso.
- `r2-inventory.json`, `all-functions.asm.json`: inventário e desmontagem reconhecida pelo analisador.
- `runtime-functions.asm.json`:705 intervalos PE, com bytes limitados ao intervalo. Flags no meio de instruções são desativadas para evitar sobreposição introduzida pelo analisador.
- `resources-shaders.json`:302 recursos,300 shaders, hashes, bindings, variáveis, grupos e nomes.
- `shader-disassembly-manifest.json`, `shader-asm/`:300 listagens, SHA256 de cada saída e identificação da ferramenta/gramática.
- `*.asm.json`, `*.pseudo.c`: exports e primeiro nível de funções chamadas; pseudocódigo é auxiliar.
- `system-ordinal-map.json`, `verification.json`: resolução local de ordinais e 19 verificações.

As listagens e recursos derivados do alvo permanecem privados. O pacote de relatórios pode conter os scripts próprios e resumos de cobertura sem incorporar executáveis, shaders, headers ou gramáticas de terceiros.

## Reprodução

A partir da pasta do projeto, com Python e as dependências estáticas descritas no README das ferramentas:

```powershell
python scripts/gdz-native/analyze_native.py
python scripts/gdz-native/inventory_resources.py
python scripts/gdz-native/disassemble_shaders.py
python scripts/gdz-native/build_contracts.py
```

Fontes primárias para os nomes das APIs: [radare2 6.2.6](https://github.com/radareorg/radare2/releases/tag/6.2.6), [DuplicateOutput1](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgioutput5-duplicateoutput1), [IDs oficiais NVAPI](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h), [PREFERRED_PSTATE oficial](https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h), [formato tokenizado Microsoft](https://github.com/microsoft/DirectXShaderCompiler/blob/main/include/dxc/Support/d3d12TokenizedProgramFormat.hpp), [gramática SPIR-V Khronos](https://github.com/KhronosGroup/SPIRV-Headers/blob/main/include/spirv/unified1/spirv.core.grammar.json). As interpretações de fluxo são inferências sobre as instruções do alvo, confrontadas com essas assinaturas.

## Apêndice — catálogo de recursos GPU

Cada linha é um recurso RCDATA idioma1033. Hash abreviado para navegação; hashes completos estão no JSON. A coluna de bindings mostra os nomes de textura/buffer recuperados; os números completos de slots, tipos e offsets estão no mesmo JSON.

| ID | Formato/modelo | Grupo | Bytes | SHA256 prefixo | Bindings recuperados |
|---:|---|---|---:|---|---|
| 101 | DXBC cs_5_0 | 8×8×1 | 5040 | `09cbba179c6a6fa2` | InputTexture, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 102 | DXBC cs_5_0 | 8×8×1 | 17112 | `fefb8ae13771c844` | InputTexture1, InputTexture2, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 103 | DXBC cs_5_0 | 8×8×1 | 17112 | `df0900d18b17b38c` | InputTexture1, InputTexture2, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 104 | DXBC cs_5_0 | 8×8×1 | 25112 | `46600a5857c22d01` | Sampler, InputTexture1, InputTexture2, MainTexture, UpdateMapTexture, OutputTexture, cb |
| 105 | DXBC cs_5_0 | 8×8×1 | 3248 | `155ba5e09f13166a` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 106 | DXBC cs_5_0 | 8×8×1 | 5684 | `a8792e1ee521c9f6` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 107 | DXBC cs_5_0 | 8×8×1 | 5684 | `5d735f9e5892549f` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 108 | DXBC cs_5_0 | 8×8×1 | 5684 | `fa9f563a27ba6e27` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 109 | DXBC cs_5_0 | 8×8×1 | 5684 | `daab56fcbaa24ee5` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 110 | DXBC cs_5_0 | 8×8×1 | 5684 | `483921a542242dc8` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 111 | DXBC cs_5_0 | 8×8×1 | 5684 | `c27171dda2a61238` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 112 | DXBC cs_5_0 | 8×8×1 | 6040 | `82508179ff96e0a5` | Sampler, InputTexture1, InputTexture2, InputTexture3, InputTexture4, InputTexture5, InputTexture6, InputTexture7, MainTexture, UpdateMapTexture, OutputTexture, cb |
| 113 | DXBC cs_5_0 | 8×8×1 | 3248 | `98dc9491a1551e77` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 114 | DXBC cs_5_0 | 8×8×1 | 5684 | `66236cbdd04e4091` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 115 | DXBC cs_5_0 | 8×8×1 | 5684 | `af09a4db838c44da` | InputTexture, UpdateMapTexture, OutputTexture, cb |
| 116 | DXBC cs_5_0 | 8×8×1 | 6692 | `fa2ea3a3025a275e` | Sampler, InputTexture, MainTexture, UpdateMapTexture, OutputTexture, cb |
| 117 | DXBC cs_5_0 | 8×8×1 | 6832 | `ad3a64d378a9ec67` | InputTexture, UpdateMapTexture, OutputTexture1, OutputTexture2, OutputTexture3, cb |
| 118 | DXBC cs_5_0 | 8×8×1 | 35304 | `f23eee9af2391e6d` | InputTexture1, InputTexture2, InputTexture3, UpdateMapTexture, OutputTexture1, OutputTexture2, OutputTexture3, cb |
| 119 | DXBC cs_5_0 | 8×8×1 | 35304 | `1cc36291147f6a6b` | InputTexture1, InputTexture2, InputTexture3, UpdateMapTexture, OutputTexture1, OutputTexture2, OutputTexture3, cb |
| 120 | DXBC cs_5_0 | 8×8×1 | 35304 | `820a6fced5b8106e` | InputTexture1, InputTexture2, InputTexture3, UpdateMapTexture, OutputTexture1, OutputTexture2, OutputTexture3, cb |
| 121 | DXBC cs_5_0 | 8×8×1 | 35304 | `762f30d907833fdc` | InputTexture1, InputTexture2, InputTexture3, UpdateMapTexture, OutputTexture1, OutputTexture2, OutputTexture3, cb |
| 122 | DXBC cs_5_0 | 8×8×1 | 35304 | `c28a1b8cb5bbff34` | InputTexture1, InputTexture2, InputTexture3, UpdateMapTexture, OutputTexture1, OutputTexture2, OutputTexture3, cb |
| 123 | DXBC cs_5_0 | 8×8×1 | 35304 | `80fdadc18095e1e2` | InputTexture1, InputTexture2, InputTexture3, UpdateMapTexture, OutputTexture1, OutputTexture2, OutputTexture3, cb |
| 124 | DXBC cs_5_0 | 8×8×1 | 22116 | `5fcf5902ddc69c03` | Sampler, InputTexture1, InputTexture2, InputTexture3, InputTexture4, InputTexture5, InputTexture6, InputTexture7, InputTexture8, InputTexture9, InputTexture10, InputTexture11, Inpu… |
| 125 | DXBC cs_5_0 | 8×8×1 | 5040 | `fcda744c6136f399` | InputTexture, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 126 | DXBC cs_5_0 | 8×8×1 | 17112 | `0ead08773e89ea3f` | InputTexture1, InputTexture2, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 127 | DXBC cs_5_0 | 8×8×1 | 17112 | `904b8c37969fd3ab` | InputTexture1, InputTexture2, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 128 | DXBC cs_5_0 | 8×8×1 | 17112 | `e66782ba019f16fd` | InputTexture1, InputTexture2, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 129 | DXBC cs_5_0 | 8×8×1 | 17112 | `de662f1499216044` | InputTexture1, InputTexture2, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 130 | DXBC cs_5_0 | 8×8×1 | 17112 | `d4a883dfbf2a1671` | InputTexture1, InputTexture2, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 131 | DXBC cs_5_0 | 8×8×1 | 17112 | `71362394d4eb5112` | InputTexture1, InputTexture2, UpdateMapTexture, OutputTexture1, OutputTexture2, cb |
| 132 | DXBC cs_5_0 | 8×8×1 | 20804 | `89ea8ce6629fe4fe` | Sampler, InputTexture1, InputTexture2, InputTexture3, InputTexture4, InputTexture5, InputTexture6, InputTexture7, InputTexture8, InputTexture9, InputTexture10, InputTexture11, Inpu… |
| 133 | DXBC cs_5_0 | 8×8×1 | 2632 | `fcc902479cb58f83` | Sampler, InputTexture, OutputTexture, cb |
| 134 | DXBC cs_5_0 | 64×1×1 | 6456 | `88165c10d4261b75` | InputTexture, OutputTexture, cb, cb |
| 135 | DXBC cs_5_0 | 16×16×1 | 2580 | `20ed42c0b7c10fd5` | Sampler, InputTexture, OutputTexture, cb |
| 136 | DXBC cs_5_0 | 8×8×1 | 636 | `d1099b73a216bf9e` | InputTexture, OutputTexture |
| 137 | DXBC cs_5_0 | 64×1×1 | 38960 | `730119456cce0e2b` | samLinearClamp, InputTexture, OutputTexture, cb |
| 138 | DXBC cs_5_0 | 64×1×1 | 36172 | `5cd8d200f057aa7e` | samLinearClamp, InputTexture, OutputTexture, cb |
| 139 | DXBC cs_5_0 | 64×1×1 | 33188 | `b1de6a519f7ec5f0` | samLinearClamp, InputTexture, OutputTexture, cb |
| 140 | DXBC cs_5_0 | 64×1×1 | 33100 | `3f7858ad62f38939` | samLinearClamp, InputTexture, OutputTexture, cb |
| 141 | DXBC cs_5_0 | 16×16×1 | 8468 | `d82efb0cff85c01c` | InputTexture, OutputTexture, cb |
| 142 | DXBC cs_5_0 | 16×16×1 | 8468 | `827add1e6cd0e5bd` | InputTexture, OutputTexture, cb |
| 143 | DXBC cs_5_0 | 16×16×1 | 8468 | `001829fd14faca22` | InputTexture, OutputTexture, cb |
| 144 | DXBC cs_5_0 | 16×16×1 | 8468 | `922f7fe23c875ffe` | InputTexture, OutputTexture, cb |
| 145 | DXBC cs_5_0 | 16×16×1 | 8468 | `68e6527d3b34f39f` | InputTexture, OutputTexture, cb |
| 146 | DXBC cs_5_0 | 16×16×1 | 1720 | `f8fdce19dd6de617` | Sampler, EffectTexture, InputTexture, OutputTexture, cb |
| 147 | DXBC cs_5_0 | 16×16×1 | 5856 | `6fa23d9f36684b51` | InputTexture, OutputTexture, cb |
| 148 | DXBC cs_5_0 | 16×16×1 | 6228 | `1d9ec8ad4d1760a8` | InputTexture, OutputTexture |
| 149 | DXBC cs_5_0 | 16×16×1 | 7056 | `6abb738297b5e335` | InputTexture, OutputTexture, cb |
| 150 | DXBC cs_5_0 | 16×16×1 | 5856 | `d633300506dfcb62` | InputTexture, OutputTexture, cb |
| 151 | DXBC cs_5_0 | 16×16×1 | 6228 | `ad3766aad226c3a6` | InputTexture, OutputTexture |
| 152 | DXBC cs_5_0 | 16×16×1 | 7056 | `e1385c900e936584` | InputTexture, OutputTexture, cb |
| 153 | DXBC cs_5_0 | 16×16×1 | 5856 | `463a4212b2d13238` | InputTexture, OutputTexture, cb |
| 154 | DXBC cs_5_0 | 16×16×1 | 6228 | `2868d084135e5c07` | InputTexture, OutputTexture |
| 155 | DXBC cs_5_0 | 16×16×1 | 7056 | `8dbd1fc602b422ab` | InputTexture, OutputTexture, cb |
| 156 | DXBC cs_5_0 | 16×16×1 | 5856 | `b09799ffcd2ec886` | InputTexture, OutputTexture, cb |
| 157 | DXBC cs_5_0 | 16×16×1 | 6228 | `49d20439c3e2cdb4` | InputTexture, OutputTexture |
| 158 | DXBC cs_5_0 | 16×16×1 | 7056 | `234b0d523856ad33` | InputTexture, OutputTexture, cb |
| 159 | DXBC cs_5_0 | 16×16×1 | 5856 | `98c341dd650b578a` | InputTexture, OutputTexture, cb |
| 160 | DXBC cs_5_0 | 16×16×1 | 6228 | `66e2ef6a3411ea22` | InputTexture, OutputTexture |
| 161 | DXBC cs_5_0 | 16×16×1 | 7056 | `cc7acfd3b54eacd7` | InputTexture, OutputTexture, cb |
| 162 | DXBC cs_5_0 | 16×16×1 | 2276 | `e9566934e7e031a7` | Sampler, InputFrame1, InputFrame2, InputFlow, InputMask, OutputFrame, cb |
| 163 | DXBC cs_5_0 | 8×8×1 | 8416 | `25e746b28cc9730a` | Sampler, InputFrame1, InputFrame2, InputMask, Output1, Output2, cb |
| 164 | DXBC cs_5_0 | 8×8×1 | 12128 | `999af4b180cb5404` | Sampler, Input1, Input2, Output1, Output2, cb |
| 165 | DXBC cs_5_0 | 8×8×1 | 11192 | `5dc6d0884849d521` | Sampler, Input1, Input2, Output1, Output2, cb |
| 166 | DXBC cs_5_0 | 8×8×1 | 9844 | `9b681d2f11c6e583` | Sampler, Input1, Input2, InputFlow, InputMask, InputUiMask, OutputFlow, OutputMask, cb |
| 167 | DXBC cs_5_0 | 8×8×1 | 8692 | `e1ab37b2c9fb00f7` | Sampler, InputFrame1, InputFrame2, InputFrame3, InputFrame4, Output1, Output2, cb |
| 168 | DXBC cs_5_0 | 8×8×1 | 12128 | `c2d4b52fcb67ab77` | Sampler, Input1, Input2, Output1, Output2, cb |
| 169 | DXBC cs_5_0 | 8×8×1 | 11192 | `5c3f5a9b922fc674` | Sampler, Input1, Input2, Output1, Output2, cb |
| 170 | DXBC cs_5_0 | 32×32×1 | 7264 | `dd37754e1a39b16e` | Sampler, Input1, Input2, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, cb |
| 171 | DXBC cs_5_0 | 16×16×1 | 1860 | `1daea38fca8856f4` | Sampler, InputFrame1, InputFrame2, InputFlow, OutputFrame1, OutputFrame2, cb |
| 172 | DXBC cs_5_0 | 8×8×1 | 12292 | `011b71005d7baa54` | Sampler, InputFrame1, InputFrame2, InputFrame3, InputFrame4, Output1, Output2, cb |
| 173 | DXBC cs_5_0 | 8×8×1 | 15912 | `9adb5802cf76bdd0` | Sampler, Input1, Input2, Output1, Output2, cb |
| 174 | DXBC cs_5_0 | 8×8×1 | 28968 | `35a71ae150283245` | Sampler, Input1, Input2, Output1, Output2, Output3, Output4, cb |
| 175 | DXBC cs_5_0 | 8×8×1 | 55212 | `8395b3b1aad279e4` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4, cb |
| 176 | DXBC cs_5_0 | 8×8×1 | 55212 | `17bb8057800cdbf4` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4, cb |
| 177 | DXBC cs_5_0 | 8×8×1 | 55212 | `59407076fd9a3dba` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4, cb |
| 178 | DXBC cs_5_0 | 8×8×1 | 55212 | `afd7a9ecbe1c70f8` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4, cb |
| 179 | DXBC cs_5_0 | 8×8×1 | 55212 | `822aaaa2b20c8751` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4, cb |
| 180 | DXBC cs_5_0 | 32×32×1 | 14212 | `3f7b92c399296edc` | Sampler, Input1, Input2, Input3, Input4, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, cb |
| 181 | DXBC cs_5_0 | 16×16×1 | 2520 | `2b83bc492ef1c4a8` | Sampler, InputFrame1, InputFrame2, InputFlow, InputMask, OutputFrame, cb |
| 182 | DXBC cs_5_0 | 8×8×1 | 14664 | `7613d56be3f04ab1` | Sampler, InputFrame1, InputFrame2, InputMask, Output1, Output2, Output3, cb |
| 183 | DXBC cs_5_0 | 8×8×1 | 32464 | `7889818f1003e559` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 184 | DXBC cs_5_0 | 8×8×1 | 61304 | `46e86b1022427dd6` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 185 | DXBC cs_5_0 | 8×8×1 | 119284 | `ef9035ea81b8f796` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 186 | DXBC cs_5_0 | 8×8×1 | 119284 | `31f0a63f94b0ade6` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 187 | DXBC cs_5_0 | 8×8×1 | 119284 | `80f970d022b002fd` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 188 | DXBC cs_5_0 | 8×8×1 | 119284 | `38f1b36e2010a8c5` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 189 | DXBC cs_5_0 | 8×8×1 | 119284 | `38c8d2aa4e91c725` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 190 | DXBC cs_5_0 | 8×8×1 | 24712 | `262ef977e9b99b1f` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, InputFlow, InputMask, InputUiMask, OutputFlow, OutputMask, cb |
| 191 | DXBC cs_5_0 | 8×8×1 | 9944 | `24955524eb5172d5` | Sampler, InputFrame1, InputFrame2, InputMask, Output1, Output2, cb |
| 192 | DXBC cs_5_0 | 8×8×1 | 13728 | `197a16b3d8b01912` | Sampler, Input1, Input2, Output1, Output2, cb |
| 193 | DXBC cs_5_0 | 8×8×1 | 22976 | `e54a5e7935efa9c1` | Sampler, Input1, Input2, Output1, Output2, Output3, cb |
| 194 | DXBC cs_5_0 | 8×8×1 | 32464 | `fed9fe876074a271` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 195 | DXBC cs_5_0 | 8×8×1 | 32464 | `d303c4a67d207012` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 196 | DXBC cs_5_0 | 8×8×1 | 32464 | `13472d7e77c61150` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 197 | DXBC cs_5_0 | 8×8×1 | 32464 | `1baef8620af9405f` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 198 | DXBC cs_5_0 | 8×8×1 | 32464 | `1365d0530c90be0b` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 199 | DXBC cs_5_0 | 8×8×1 | 14084 | `01f5a5ba33ba7f50` | Sampler, Input1, Input2, Input3, InputFlow, InputMask, InputUiMask, OutputFlow, OutputMask, cb |
| 200 | DXBC cs_5_0 | 16×16×1 | 1944 | `6edcce53dca403ca` | Sampler, InputFrame1, InputFrame2, InputFlow, OutputFrame1, OutputFrame2, cb |
| 201 | DXBC cs_5_0 | 16×16×1 | 2540 | `37cb37a8276b2d8d` | Sampler, InputFrame1, InputFrame2, InputFlow, InputMask, OutputFrame, cb |
| 202 | DXBC cs_5_0 | 8×8×1 | 14664 | `fd81e4804e3b18b2` | Sampler, InputFrame1, InputFrame2, InputMask, Output1, Output2, Output3, cb |
| 203 | DXBC cs_5_0 | 8×8×1 | 32464 | `1003fc1b1be1f7fa` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 204 | DXBC cs_5_0 | 8×8×1 | 61304 | `ff99c477d396f4b2` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 205 | DXBC cs_5_0 | 8×8×1 | 119284 | `5b26a6784d62f000` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 206 | DXBC cs_5_0 | 8×8×1 | 119284 | `7d6a6211c964ee92` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 207 | DXBC cs_5_0 | 8×8×1 | 119284 | `2fa3faa33f24708d` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 208 | DXBC cs_5_0 | 8×8×1 | 119284 | `4428f92042c93c95` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 209 | DXBC cs_5_0 | 8×8×1 | 119284 | `de647e0c20ed9b61` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 210 | DXBC cs_5_0 | 8×8×1 | 24788 | `a917c34eb61eb6a1` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, InputFlow, InputMask, InputUiMask, OutputFlow, OutputMask, cb |
| 211 | DXBC cs_5_0 | 8×8×1 | 9944 | `c5856edf10e6964c` | Sampler, InputFrame1, InputFrame2, InputMask, Output1, Output2, cb |
| 212 | DXBC cs_5_0 | 8×8×1 | 13728 | `775abe0c85f7b2a3` | Sampler, Input1, Input2, Output1, Output2, cb |
| 213 | DXBC cs_5_0 | 8×8×1 | 22976 | `09674af4b4b10275` | Sampler, Input1, Input2, Output1, Output2, Output3, cb |
| 214 | DXBC cs_5_0 | 8×8×1 | 32464 | `741a56f84f7e6dc7` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 215 | DXBC cs_5_0 | 8×8×1 | 32464 | `0f69616a44c83aa2` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 216 | DXBC cs_5_0 | 8×8×1 | 32464 | `1d9078247d327d9b` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 217 | DXBC cs_5_0 | 8×8×1 | 32464 | `bbc533f1c00c1a16` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 218 | DXBC cs_5_0 | 8×8×1 | 32464 | `28dec469bed15a4d` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 219 | DXBC cs_5_0 | 8×8×1 | 14160 | `a4851379be36e48f` | Sampler, Input1, Input2, Input3, InputFlow, InputMask, InputUiMask, OutputFlow, OutputMask, cb |
| 220 | DXBC cs_5_0 | 16×16×1 | 1944 | `f8a8b519515dd5d8` | Sampler, InputFrame1, InputFrame2, InputFlow, OutputFrame1, OutputFrame2, cb |
| 221 | DXBC cs_5_0 | 16×16×1 | 2540 | `81d8cb94c4567b6d` | Sampler, InputFrame1, InputFrame2, InputFlow, InputMask, OutputFrame, cb |
| 222 | DXBC cs_5_0 | 8×8×1 | 14664 | `83df53363d70e977` | Sampler, InputFrame1, InputFrame2, InputMask, Output1, Output2, Output3, cb |
| 223 | DXBC cs_5_0 | 8×8×1 | 32464 | `4a9c3867786957d2` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 224 | DXBC cs_5_0 | 8×8×1 | 61304 | `d6f70b987c47dcf8` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 225 | DXBC cs_5_0 | 8×8×1 | 119284 | `a0092c84a8a59080` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 226 | DXBC cs_5_0 | 8×8×1 | 119284 | `ab3698acd0070f33` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 227 | DXBC cs_5_0 | 8×8×1 | 119284 | `f0dbff03701e1a73` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 228 | DXBC cs_5_0 | 8×8×1 | 119284 | `e64d040acf7bcc2e` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 229 | DXBC cs_5_0 | 8×8×1 | 119284 | `14f1a06472d9bf90` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 230 | DXBC cs_5_0 | 8×8×1 | 24788 | `8ac8b5d09ca7895c` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, InputFlow, InputMask, InputUiMask, OutputFlow, OutputMask, cb |
| 231 | DXBC cs_5_0 | 8×8×1 | 9944 | `ae7b84c8c8893563` | Sampler, InputFrame1, InputFrame2, InputMask, Output1, Output2, cb |
| 232 | DXBC cs_5_0 | 8×8×1 | 13728 | `09701a5029fe503b` | Sampler, Input1, Input2, Output1, Output2, cb |
| 233 | DXBC cs_5_0 | 8×8×1 | 22976 | `c215c8fe097fcaf0` | Sampler, Input1, Input2, Output1, Output2, Output3, cb |
| 234 | DXBC cs_5_0 | 8×8×1 | 32464 | `029ab9255adcf5d8` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 235 | DXBC cs_5_0 | 8×8×1 | 32464 | `b8bf3a01756b9cd9` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 236 | DXBC cs_5_0 | 8×8×1 | 32464 | `1ae8549e66b2fde9` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 237 | DXBC cs_5_0 | 8×8×1 | 32464 | `24138a256b459e85` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 238 | DXBC cs_5_0 | 8×8×1 | 32464 | `23a9db99977c3f0e` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, cb |
| 239 | DXBC cs_5_0 | 8×8×1 | 14160 | `3ff18f9c858f67c8` | Sampler, Input1, Input2, Input3, InputFlow, InputMask, InputUiMask, OutputFlow, OutputMask, cb |
| 240 | DXBC cs_5_0 | 16×16×1 | 1944 | `a47c6665612bc6e6` | Sampler, InputFrame1, InputFrame2, InputFlow, OutputFrame1, OutputFrame2, cb |
| 241 | DXBC cs_5_0 | 8×8×1 | 1376 | `fc006b1dd0f7daaa` | InputFrame, OutputFrame, cb |
| 242 | DXBC cs_5_0 | 32×32×1 | 6132 | `be96d599d626450f` | Sampler, InputFrame, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, OutputMip6, cb |
| 243 | DXBC cs_5_0 | 256×1×1 | 27184 | `90600dc728c3dae6` | samplerLinearClamp, in_texture, coef_scaler, coef_usm, out_texture, cb |
| 244 | DXBC cs_5_0 | 256×1×1 | 27488 | `aff36a75a5ad505d` | samplerLinearClamp, in_texture, coef_scaler, coef_usm, out_texture, cb |
| 245 | DXBC cs_5_0 | 128×1×1 | 27080 | `fc8f0300fec6f802` | samplerLinearClamp, in_texture, coef_scaler, coef_usm, out_texture, cb |
| 246 | DXBC cs_5_0 | 128×1×1 | 27384 | `706e76dcb5b725b0` | samplerLinearClamp, in_texture, coef_scaler, coef_usm, out_texture, cb |
| 247 | DXBC cs_5_0 | 64×1×1 | 7248 | `ff051512bf2fbb10` | InputTexture, OutputTexture, cb, cb1 |
| 248 | DXBC cs_5_0 | 64×1×1 | 9120 | `dde25866ca56b1e5` | InputTexture, OutputTexture, cb, cb1 |
| 249 | DXBC cs_5_0 | 8×8×1 | 1292 | `14e1e62485505bce` | Sampler, InputTexture, OutputTexture, cb |
| 250 | DXBC cs_5_0 | 8×8×1 | 6828 | `6b7b07698ed51cc6` | Sampler, InputTexture, OutputTexture, cb |
| 251 | DXBC cs_5_0 | 8×8×1 | 1152 | `dca259b54efa2dfe` | InputTexture, UpdateMapTexture, PreviousFrameTexture, cb |
| 252 | DXBC cs_5_0 | 8×8×1 | 1824 | `b70304e9426e68ef` | InputTexture, PreviousFrameTexture, UpdateMapTexture, cb |
| 253 | DXBC cs_5_0 | 8×8×1 | 5216 | `7c79c88669b7f9ac` | Sampler, InputTexture, OutputTexture, cb |
| 254 | DXBC cs_5_0 | 8×8×1 | 1216 | `214a8ad496c0ffff` | Input1, Output1, cb |
| 255 | DXBC cs_5_0 | 32×32×1 | 6012 | `17d069a52fb2b438` | Sampler, Input1, Output0, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 256 | DXBC cs_5_0 | 16×16×1 | 3132 | `3af0031e97f43a93` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Output1, cb |
| 257 | DXBC cs_5_0 | 8×8×1 | 10048 | `24eb1c9f7bcedce5` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Output1, Output2, Output3, cb |
| 258 | DXBC cs_5_0 | 8×8×1 | 34528 | `aed7dae084575539` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Input10, Output1, Output2, cb |
| 259 | DXBC cs_5_0 | 8×8×1 | 33752 | `36cd82fc333f5dd8` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4 |
| 260 | DXBC cs_5_0 | 8×8×1 | 55292 | `1489b26821aede2c` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 261 | DXBC cs_5_0 | 8×8×1 | 55292 | `8b39d49886f16274` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 262 | DXBC cs_5_0 | 8×8×1 | 13848 | `2617086cc03e57cc` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 263 | DXBC cs_5_0 | 8×8×1 | 33752 | `619fb0bfa8993bc9` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4 |
| 264 | DXBC cs_5_0 | 8×8×1 | 55292 | `0a0b283e7fd68f16` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 265 | DXBC cs_5_0 | 8×8×1 | 55292 | `19ba9ad9f02d1431` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 266 | DXBC cs_5_0 | 8×8×1 | 13848 | `089c3593b27412bd` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 267 | DXBC cs_5_0 | 8×8×1 | 6388 | `106cb82e5a5cc934` | Sampler, Input1, Output1, Output2 |
| 268 | DXBC cs_5_0 | 8×8×1 | 15592 | `9321575b723959b5` | Sampler, Input1, Input2, Output1, Output2 |
| 269 | DXBC cs_5_0 | 8×8×1 | 28688 | `ea6082740f5c810f` | Sampler, Input1, Input2, Output1, Output2, Output3, Output4 |
| 270 | DXBC cs_5_0 | 8×8×1 | 55292 | `9f4ce6b066fdecae` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 271 | DXBC cs_5_0 | 8×8×1 | 15592 | `2665a85a95a532d5` | Sampler, Input1, Input2, Output1, Output2 |
| 272 | DXBC cs_5_0 | 8×8×1 | 15592 | `6649745add98e939` | Sampler, Input1, Input2, Output1, Output2 |
| 273 | DXBC cs_5_0 | 8×8×1 | 15592 | `8bf9de1d607f19a1` | Sampler, Input1, Input2, Output1, Output2 |
| 274 | DXBC cs_5_0 | 8×8×1 | 9780 | `34ba265e1e368987` | Sampler, Sampler1, Input1, Input2, Input3, Output1, cb |
| 275 | DXBC cs_5_0 | 8×8×1 | 16904 | `0570693ada6c8db8` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Input10, Input11, Input12, Output1, Output2 |
| 276 | DXBC cs_5_0 | 8×8×1 | 15592 | `7440a7b57fd0ff71` | Sampler, Input1, Input2, Output1, Output2 |
| 277 | DXBC cs_5_0 | 8×8×1 | 15592 | `25930474e1ad0d0c` | Sampler, Input1, Input2, Output1, Output2 |
| 278 | DXBC cs_5_0 | 8×8×1 | 15592 | `64c0fffaef849c96` | Sampler, Input1, Input2, Output1, Output2 |
| 279 | DXBC cs_5_0 | 32×32×1 | 11076 | `aee86e86c2e1af6f` | Sampler, Input1, Input2, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, cb |
| 280 | DXBC cs_5_0 | 8×8×1 | 6404 | `3c9e532ded52cc53` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Output1, Output2, Output3, cb |
| 281 | DXBC cs_5_0 | 8×8×1 | 20656 | `643f9a6b78947581` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 282 | DXBC cs_5_0 | 8×8×1 | 18632 | `16b1da261a0b9bec` | Sampler, Input1, Input2, Input3, Output1, Output2 |
| 283 | DXBC cs_5_0 | 8×8×1 | 15592 | `06171025cb34381c` | Sampler, Input1, Input2, Output1, Output2 |
| 284 | DXBC cs_5_0 | 8×8×1 | 15592 | `8b1b30a10a615f82` | Sampler, Input1, Input2, Output1, Output2 |
| 285 | DXBC cs_5_0 | 8×8×1 | 8488 | `f0aaa7d8868d1b71` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Output1, cb |
| 286 | DXBC cs_5_0 | 8×8×1 | 18632 | `bba5f96f67a530be` | Sampler, Input1, Input2, Input3, Output1, Output2 |
| 287 | DXBC cs_5_0 | 8×8×1 | 15592 | `fc29f896a648db65` | Sampler, Input1, Input2, Output1, Output2 |
| 288 | DXBC cs_5_0 | 8×8×1 | 15592 | `a3a126f20da305f8` | Sampler, Input1, Input2, Output1, Output2 |
| 289 | DXBC cs_5_0 | 8×8×1 | 8488 | `6b53c6ddeb39db82` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Output1, cb |
| 290 | DXBC cs_5_0 | 8×8×1 | 5020 | `9f39d154433970f4` | Sampler, Input1, Output1 |
| 291 | DXBC cs_5_0 | 8×8×1 | 5028 | `ab9fcfdb3d8e427a` | Sampler, Input1, Output1 |
| 292 | DXBC cs_5_0 | 8×8×1 | 8512 | `bd5d1036e6428da9` | Sampler, Input1, Output1, Output2 |
| 293 | DXBC cs_5_0 | 8×8×1 | 15592 | `389d2046e738b34d` | Sampler, Input1, Input2, Output1, Output2 |
| 294 | DXBC cs_5_0 | 8×8×1 | 5028 | `1f67718a019f84ec` | Sampler, Input1, Output1 |
| 295 | DXBC cs_5_0 | 8×8×1 | 5028 | `e731c69b18058dc8` | Sampler, Input1, Output1 |
| 296 | DXBC cs_5_0 | 8×8×1 | 5028 | `662991eb7ea8da4d` | Sampler, Input1, Output1 |
| 297 | DXBC cs_5_0 | 8×8×1 | 5732 | `a756c6837718e286` | Sampler, Sampler1, Input1, Input2, Output1, cb |
| 298 | DXBC cs_5_0 | 8×8×1 | 11516 | `ef0d503518cd1c51` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2 |
| 299 | DXBC cs_5_0 | 8×8×1 | 15592 | `579fa4fedd2e3bf4` | Sampler, Input1, Input2, Output1, Output2 |
| 300 | DXBC cs_5_0 | 8×8×1 | 15592 | `4e5870bf27cf813d` | Sampler, Input1, Input2, Output1, Output2 |
| 301 | DXBC cs_5_0 | 8×8×1 | 15592 | `c3648a3e85f815f5` | Sampler, Input1, Input2, Output1, Output2 |
| 302 | DXBC cs_5_0 | 32×32×1 | 11076 | `1b07ee7c17cd94b9` | Sampler, Input1, Input2, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, cb |
| 303 | SPIR-V1.0 compute | 8×8×1 | 1424 | `9855750d95dfc4c8` | Input1, Output1, cb |
| 304 | SPIR-V1.0 compute | 32×32×1 | 8196 | `23d912541273254a` | Sampler, Input1, Output0, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 305 | SPIR-V1.0 compute | 16×16×1 | 4308 | `a48155860e7c2342` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Output1, cb |
| 306 | SPIR-V1.0 compute | 8×8×1 | 9572 | `75b0f8b042fc1a92` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Output1, Output2, Output3, cb |
| 307 | SPIR-V1.0 compute | 8×8×1 | 29056 | `62808e954274e0ad` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Input10, Output1, Output2, cb |
| 308 | SPIR-V1.0 compute | 8×8×1 | 40800 | `1ee4b5be5a0095bb` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4 |
| 309 | SPIR-V1.0 compute | 8×8×1 | 65180 | `f1ec361fedaa3633` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 310 | SPIR-V1.0 compute | 8×8×1 | 65020 | `6fc666d4c78b81bb` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 311 | SPIR-V1.0 compute | 8×8×1 | 17140 | `e8999d908172cfe7` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 312 | SPIR-V1.0 compute | 8×8×1 | 40816 | `fa0302dca4631e2a` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4 |
| 313 | SPIR-V1.0 compute | 8×8×1 | 65676 | `8698f94f22671d32` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 314 | SPIR-V1.0 compute | 8×8×1 | 65260 | `337e432dbcf78218` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 315 | SPIR-V1.0 compute | 8×8×1 | 17236 | `be6b7f1a71fca77c` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 316 | SPIR-V1.0 compute | 8×8×1 | 6204 | `c350b729bc2f4bc1` | Sampler, Input1, Output1, Output2 |
| 317 | SPIR-V1.0 compute | 8×8×1 | 19500 | `a09b917a84c46b2a` | Sampler, Input1, Input2, Output1, Output2 |
| 318 | SPIR-V1.0 compute | 8×8×1 | 35076 | `4e717af40bfb7b91` | Sampler, Input1, Input2, Output1, Output2, Output3, Output4 |
| 319 | SPIR-V1.0 compute | 8×8×1 | 66076 | `b3b42da0c77db110` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 320 | SPIR-V1.0 compute | 8×8×1 | 19420 | `723cbfd5a9c886c2` | Sampler, Input1, Input2, Output1, Output2 |
| 321 | SPIR-V1.0 compute | 8×8×1 | 19436 | `e4b1c5bddf13facd` | Sampler, Input1, Input2, Output1, Output2 |
| 322 | SPIR-V1.0 compute | 8×8×1 | 19420 | `3131217517efa946` | Sampler, Input1, Input2, Output1, Output2 |
| 323 | SPIR-V1.0 compute | 8×8×1 | 11980 | `bf1dadc03d573885` | Sampler, Sampler1, Input1, Input2, Input3, Output1, cb |
| 324 | SPIR-V1.0 compute | 8×8×1 | 15156 | `526440ee7350543e` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Input10, Input11, Input12, Output1, Output2 |
| 325 | SPIR-V1.0 compute | 8×8×1 | 19404 | `3171dc6b71c6f7f3` | Sampler, Input1, Input2, Output1, Output2 |
| 326 | SPIR-V1.0 compute | 8×8×1 | 19420 | `cdfeef31b7be21c9` | Sampler, Input1, Input2, Output1, Output2 |
| 327 | SPIR-V1.0 compute | 8×8×1 | 19340 | `8a4f90354af69d48` | Sampler, Input1, Input2, Output1, Output2 |
| 328 | SPIR-V1.0 compute | 32×32×1 | 10068 | `a7a224d75f053120` | Sampler, Input1, Input2, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, cb |
| 329 | SPIR-V1.0 compute | 8×8×1 | 6596 | `a5513399c27e3407` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Output1, Output2, Output3, cb |
| 330 | SPIR-V1.0 compute | 8×8×1 | 17184 | `dcb5e761152a4b0a` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 331 | SPIR-V1.0 compute | 8×8×1 | 22840 | `3df4c7787d45d4f7` | Sampler, Input1, Input2, Input3, Output1, Output2 |
| 332 | SPIR-V1.0 compute | 8×8×1 | 19500 | `729ea57e0ade903a` | Sampler, Input1, Input2, Output1, Output2 |
| 333 | SPIR-V1.0 compute | 8×8×1 | 19420 | `206d3de49584bc03` | Sampler, Input1, Input2, Output1, Output2 |
| 334 | SPIR-V1.0 compute | 8×8×1 | 10364 | `fa2cf6acedae5345` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Output1, cb |
| 335 | SPIR-V1.0 compute | 8×8×1 | 22904 | `2a0e19de724e1482` | Sampler, Input1, Input2, Input3, Output1, Output2 |
| 336 | SPIR-V1.0 compute | 8×8×1 | 19292 | `db3a838f91457624` | Sampler, Input1, Input2, Output1, Output2 |
| 337 | SPIR-V1.0 compute | 8×8×1 | 19388 | `04d7f68684e79f0f` | Sampler, Input1, Input2, Output1, Output2 |
| 338 | SPIR-V1.0 compute | 8×8×1 | 10300 | `3e565a0f50dd869e` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Output1, cb |
| 339 | SPIR-V1.0 compute | 8×8×1 | 4584 | `bdb034ee83eab351` | Sampler, Input1, Output1 |
| 340 | SPIR-V1.0 compute | 8×8×1 | 6496 | `c1bfae80c55d62f6` | Sampler, Input1, Output1 |
| 341 | SPIR-V1.0 compute | 8×8×1 | 10796 | `70f92150c48becc0` | Sampler, Input1, Output1, Output2 |
| 342 | SPIR-V1.0 compute | 8×8×1 | 19500 | `29cd2862388a173f` | Sampler, Input1, Input2, Output1, Output2 |
| 343 | SPIR-V1.0 compute | 8×8×1 | 6480 | `9b6929b6c9ef6f55` | Sampler, Input1, Output1 |
| 344 | SPIR-V1.0 compute | 8×8×1 | 6480 | `e1904ccbe13f4cda` | Sampler, Input1, Output1 |
| 345 | SPIR-V1.0 compute | 8×8×1 | 6512 | `3fba5c0e98055b7f` | Sampler, Input1, Output1 |
| 346 | SPIR-V1.0 compute | 8×8×1 | 7176 | `b8b082ff8617ac47` | Sampler, Sampler1, Input1, Input2, Output1, cb |
| 347 | SPIR-V1.0 compute | 8×8×1 | 11172 | `400a28d3dc29e4d0` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2 |
| 348 | SPIR-V1.0 compute | 8×8×1 | 19372 | `48d0b7326ce05315` | Sampler, Input1, Input2, Output1, Output2 |
| 349 | SPIR-V1.0 compute | 8×8×1 | 19468 | `1915099509368b1b` | Sampler, Input1, Input2, Output1, Output2 |
| 350 | SPIR-V1.0 compute | 8×8×1 | 19324 | `59431403f861ffb4` | Sampler, Input1, Input2, Output1, Output2 |
| 351 | SPIR-V1.0 compute | 32×32×1 | 10084 | `e8b47a97d479241f` | Sampler, Input1, Input2, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, cb |
| 352 | SPIR-V1.0 compute | 8×8×1 | 1424 | `9855750d95dfc4c8` | Input1, Output1, cb |
| 353 | SPIR-V1.0 compute | 32×32×1 | 8196 | `23d912541273254a` | Sampler, Input1, Output0, Output1, Output2, Output3, Output4, Output5, Output6, cb |
| 354 | SPIR-V1.0 compute | 16×16×1 | 4308 | `a48155860e7c2342` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Output1, cb |
| 355 | SPIR-V1.0 compute | 8×8×1 | 8736 | `f81a7aa8d4996550` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Output1, Output2, Output3, cb |
| 356 | SPIR-V1.0 compute | 8×8×1 | 27308 | `8cae7c23395550c2` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Input10, Output1, Output2, cb |
| 357 | SPIR-V1.0 compute | 8×8×1 | 41564 | `d15412f052516616` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4 |
| 358 | SPIR-V1.0 compute | 8×8×1 | 68872 | `56298b74ca1af842` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 359 | SPIR-V1.0 compute | 8×8×1 | 68792 | `a2e42a4bc8ed6385` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 360 | SPIR-V1.0 compute | 8×8×1 | 16520 | `e5be6fd4f39f89f6` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 361 | SPIR-V1.0 compute | 8×8×1 | 41580 | `98828ae86ae4b1f9` | Sampler, Input1, Input2, Input3, Output1, Output2, Output3, Output4 |
| 362 | SPIR-V1.0 compute | 8×8×1 | 68824 | `4fbe797dce965a4a` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 363 | SPIR-V1.0 compute | 8×8×1 | 68760 | `23a1f1bd56fd02b7` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 364 | SPIR-V1.0 compute | 8×8×1 | 16520 | `627d30c611f7cffd` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 365 | SPIR-V1.0 compute | 8×8×1 | 5992 | `2de895f4e0430836` | Sampler, Input1, Output1, Output2 |
| 366 | SPIR-V1.0 compute | 8×8×1 | 19320 | `d4653f24d5efc331` | Sampler, Input1, Input2, Output1, Output2 |
| 367 | SPIR-V1.0 compute | 8×8×1 | 35840 | `603e80ec4dcaf2c4` | Sampler, Input1, Input2, Output1, Output2, Output3, Output4 |
| 368 | SPIR-V1.0 compute | 8×8×1 | 68760 | `2d3cee9f80cd1ea3` | Sampler, Input1, Input2, Input3, Input4, Output1, Output2, Output3, Output4 |
| 369 | SPIR-V1.0 compute | 8×8×1 | 19304 | `61969234e9ab5e73` | Sampler, Input1, Input2, Output1, Output2 |
| 370 | SPIR-V1.0 compute | 8×8×1 | 19320 | `9dbadc36a6e7c296` | Sampler, Input1, Input2, Output1, Output2 |
| 371 | SPIR-V1.0 compute | 8×8×1 | 19304 | `116a07b39f8af875` | Sampler, Input1, Input2, Output1, Output2 |
| 372 | SPIR-V1.0 compute | 8×8×1 | 11736 | `f7e03b8bb5ea2bc6` | Sampler, Sampler1, Input1, Input2, Input3, Output1, cb |
| 373 | SPIR-V1.0 compute | 8×8×1 | 14400 | `6fb11eb57435ee13` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8, Input9, Input10, Input11, Input12, Output1, Output2 |
| 374 | SPIR-V1.0 compute | 8×8×1 | 19320 | `be15684ad68a3da1` | Sampler, Input1, Input2, Output1, Output2 |
| 375 | SPIR-V1.0 compute | 8×8×1 | 19320 | `80283e7718c56598` | Sampler, Input1, Input2, Output1, Output2 |
| 376 | SPIR-V1.0 compute | 8×8×1 | 19320 | `0bff45293eba2c9e` | Sampler, Input1, Input2, Output1, Output2 |
| 377 | SPIR-V1.0 compute | 32×32×1 | 9648 | `7e1ff6000e26259e` | Sampler, Input1, Input2, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, cb |
| 378 | SPIR-V1.0 compute | 8×8×1 | 6096 | `41491212604e64d7` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Output1, Output2, Output3, cb |
| 379 | SPIR-V1.0 compute | 8×8×1 | 16092 | `85ebd4f1300764ae` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Input5, Input6, Output1, cb |
| 380 | SPIR-V1.0 compute | 8×8×1 | 22692 | `822282b38de00746` | Sampler, Input1, Input2, Input3, Output1, Output2 |
| 381 | SPIR-V1.0 compute | 8×8×1 | 19320 | `ce433150a3785f5d` | Sampler, Input1, Input2, Output1, Output2 |
| 382 | SPIR-V1.0 compute | 8×8×1 | 19320 | `92fa463947457271` | Sampler, Input1, Input2, Output1, Output2 |
| 383 | SPIR-V1.0 compute | 8×8×1 | 9920 | `506c6dca1e09cb08` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Output1, cb |
| 384 | SPIR-V1.0 compute | 8×8×1 | 22692 | `29ebdc63bded9d8f` | Sampler, Input1, Input2, Input3, Output1, Output2 |
| 385 | SPIR-V1.0 compute | 8×8×1 | 19320 | `65abc99f34416364` | Sampler, Input1, Input2, Output1, Output2 |
| 386 | SPIR-V1.0 compute | 8×8×1 | 19320 | `9855a089e8ea1732` | Sampler, Input1, Input2, Output1, Output2 |
| 387 | SPIR-V1.0 compute | 8×8×1 | 9888 | `39e70d38e735fe52` | Sampler, Sampler1, Input1, Input2, Input3, Input4, Output1, cb |
| 388 | SPIR-V1.0 compute | 8×8×1 | 4388 | `f196e486395e1706` | Sampler, Input1, Output1 |
| 389 | SPIR-V1.0 compute | 8×8×1 | 6316 | `de861dd2781f2c2e` | Sampler, Input1, Output1 |
| 390 | SPIR-V1.0 compute | 8×8×1 | 10712 | `8cb5f1799de38559` | Sampler, Input1, Output1, Output2 |
| 391 | SPIR-V1.0 compute | 8×8×1 | 19320 | `acdcf2aaf10cd8b7` | Sampler, Input1, Input2, Output1, Output2 |
| 392 | SPIR-V1.0 compute | 8×8×1 | 6316 | `4891da0acc90b896` | Sampler, Input1, Output1 |
| 393 | SPIR-V1.0 compute | 8×8×1 | 6316 | `b99ed2fb66dd59a0` | Sampler, Input1, Output1 |
| 394 | SPIR-V1.0 compute | 8×8×1 | 6316 | `71a5a348ccc303a7` | Sampler, Input1, Output1 |
| 395 | SPIR-V1.0 compute | 8×8×1 | 6996 | `1f34782600a4e339` | Sampler, Sampler1, Input1, Input2, Output1, cb |
| 396 | SPIR-V1.0 compute | 8×8×1 | 10752 | `c321dc41a33941c8` | Sampler, Input1, Input2, Input3, Input4, Input5, Input6, Output1, Output2 |
| 397 | SPIR-V1.0 compute | 8×8×1 | 19320 | `1a84cb843e1c4b6d` | Sampler, Input1, Input2, Output1, Output2 |
| 398 | SPIR-V1.0 compute | 8×8×1 | 19320 | `384bc524f57541fb` | Sampler, Input1, Input2, Output1, Output2 |
| 399 | SPIR-V1.0 compute | 8×8×1 | 19320 | `6f8cbc9c79d23603` | Sampler, Input1, Input2, Output1, Output2 |
| 400 | SPIR-V1.0 compute | 32×32×1 | 9648 | `e01cb406da454659` | Sampler, Input1, Input2, OutputMip0, OutputMip1, OutputMip2, OutputMip3, OutputMip4, OutputMip5, cb |
