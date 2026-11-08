# Extração estática do motor GDZ

Ferramentas próprias para ler a DLL instalada como dados e gerar evidência privada. Não constroem aplicativo, não chamam exports de Lossless.dll, não criam dispositivos gráficos e não aplicam otimizações ao Windows.

O alvo esperado é GDZ 1.1.27, `Lossless.dll` SHA256 `626b196d799606cd4250b7b29e04228692ab70cf56a5d1bbb56d748c8219f0eb`. O caminho padrão está explícito nos scripts; ajuste-o se a instalação estiver em outra conta/pasta.

Requisitos: Windows x64, Python 3 e radare2 6.2.6 oficial extraído em `.tools/radare2-6.2.6/radare2-6.2.6-w64/`. O radare2 recebe apenas abertura estática, sem opções de debugger ou escrita. Para DXBC, o script usa a função de desmontagem de `System32/d3dcompiler_47.dll`; ela recebe os bytes de shader e devolve texto. Nenhum shader é executado.

## Dependências de análise

| Arquivo | Origem | SHA256 observado |
|---|---|---|
| radare2-6.2.6-w64.zip | [release oficial](https://github.com/radareorg/radare2/releases/tag/6.2.6) | `c648e354dd584b14ba31d0244feb338c7506eb5b55b8cb4f09b06ee0507de54f` |
| d3d12TokenizedProgramFormat.official.hpp | [Microsoft](https://raw.githubusercontent.com/microsoft/DirectXShaderCompiler/main/include/dxc/Support/d3d12TokenizedProgramFormat.hpp) | `cf808b363406fce9387d7ec8daec19940175af966589c0d3e084a4c41c3f5060` |
| spirv.core.grammar.official.json | [Khronos](https://raw.githubusercontent.com/KhronosGroup/SPIRV-Headers/main/include/spirv/unified1/spirv.core.grammar.json) | `8f20f6ca4c2411391d14125cac69999f1c8248ef656e00d9d534e5e2b7a4f52c` |

Coloque o header Microsoft e a gramática Khronos em `artifacts/private/gdz-analysis/deep/native-deep/`, com os nomes da tabela. São entradas estáticas, não código importado/executado. Os URLs `main` podem mudar; compare os hashes para reproduzir exatamente esta análise. O manifesto gerado registra os hashes realmente usados.

Os headers NVIDIA usados apenas para identificar os números de NVAPI/DRS foram conferidos nas fontes primárias [nvapi_interface.h](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h) e [NvApiDriverSettings.h](https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h). Não são necessários para rodar os quatro scripts.

## Ordem de execução

Execute a partir da raiz do projeto:

```powershell
python scripts/gdz-native/analyze_native.py
python scripts/gdz-native/inventory_resources.py
python scripts/gdz-native/disassemble_shaders.py
python scripts/gdz-native/build_contracts.py
```

1. `analyze_native.py`: cria inventário radare2, corpos dos 11 exports, referências, funções reconhecidas e os 705 intervalos de unwind PE, limitando a desmontagem aos bytes de cada intervalo. O pseudocódigo automático permanece auxiliar.
2. `inventory_resources.py`: percorre recursos PE, valida os containers DXBC/SPIR-V, recupera grupos de threads, bindings e constant buffers. Não carrega bibliotecas do alvo.
3. `disassemble_shaders.py`: desmonta 202 DXBC pelo desassemblador do Windows e gera 98 listagens SPIR-V por gramática. A listagem SPIR-V conserva literais dependentes de contexto como números; não substitui um decompilador HLSL.
4. `build_contracts.py`: cruza assinatura/ABI, identifica escritas globais, resolve ordinais OLEAUT32 pela tabela de exports da DLL do Windows lida como bytes, valida a cobertura e gera o relatório e JSON de contratos. Se o inventário gerenciado anterior existir, também confere os 33 tipos/nomes de parâmetros.

Saídas em `artifacts/private/gdz-analysis/deep/native-deep/`; relatório em `docs/GDZ-MOTOR-NATIVO-APROFUNDADO.md`. Os scripts substituem somente suas próprias saídas de análise e relatório. As fontes instaladas e o produto VL não são alterados. As listagens dos shaders e a desmontagem do alvo ficam privadas e não devem ser incorporadas por engano ao pacote público dos relatórios.

Os 19 checks são de integridade e consistência estática. Eles não comprovam resultado gráfico, FPS, latência ou equivalência dinâmica. Há 300 entradas de recursos GPU, com 297 hashes únicos, e não 300 funcionalidades de produto.
