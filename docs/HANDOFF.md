# Handoff — o que outro assistente precisa saber sobre o VL Optimizer

Este arquivo existe para passar o contexto do projeto a outra ferramenta (ChatGPT, outro
desenvolvedor, ou uma sessão futura) sem que seja necessário reconstruir a investigação.

## O que é o projeto

**VL Optimizer** é um aplicativo WPF (.NET 10, `net10.0-windows`, x64) para Windows 10/11 que faz
duas coisas além da otimização de sistema que já existia:

1. **Escala de imagem e geração de quadros em jogos**, sem o usuário precisar instalar o Lossless
   Scaling separadamente.
2. **Medição de FPS ao vivo** com médias, 1% low e 0,1% low.

Repositório: `https://github.com/alex256061-cpu/VL-Optimizer`
Documentação de recuperação: `docs/RECUPERACAO.md`
Instruções para usuário final: `docs/INSTALACAO.md`

## Estrutura

```
src/VLOptimizer.Core/     regras e serviços (testável, sem WPF)
src/VLOptimizer.App/      WPF, páginas e código de UI
tests/VLOptimizer.Tests/  runner próprio, 240 testes
scripts/                  bootstrap-sdk.ps1, preparar-binarios.ps1, publish.ps1
installer/                Install.ps1, Uninstall.ps1 (embutidos pelo IExpress)
docs/                     documentação
```

## Arquivos centrais da funcionalidade nova

| Arquivo | Papel |
|---|---|
| `Core/EnginePayloadResolver.cs` | Acha e valida o `Lossless.dll` por SHA-256 |
| `Core/ScalingEngineSession.cs` | `ScalingEngineHost`: carrega, inicia, ativa, aplica, para |
| `Core/ScalingEngineArguments.cs` | Os 32 argumentos da ABI provada |
| `Core/ScalingEngineNative.cs` | Chamada nativa e projeção `ScalingProfile` → argumentos |
| `Core/ScalingEngineGlobals.cs` | Mapa argumento → global, para auditoria |
| `Core/ToolkitFpsStreamReader.cs` | PresentMon em fluxo contínuo por stdout |
| `Core/LiveFrameStatistics.cs` | Janela de 10 s, ignore list, cálculo de lows |
| `Core/LiveFpsService.cs` | Orquestra o medidor, limpa ETW antes e depois |
| `Core/EtwSessionCleaner.cs` | Remove sessões de rastreamento órfãs do próprio VL |
| `Core/GlobalHotkey.cs` | Ctrl+Alt+B |
| `App/app.manifest` | `requireAdministrator` |

## As descobertas que não podem ser perdidas

1. **O motor é um compositor externo, não algo injetado no jogo.**
   Ele captura a janela em primeiro plano por DXGI Desktop Duplication e apresenta o resultado na
   própria janela. Injeção de processo foi tentada, compilada e **removida** — era o modelo errado.

2. **`Init` precisa de thread STA com pilha grande.**
   Chamado da thread principal de um console .NET, falha com `0xc0000005` em `0x0`. Em thread STA
   com 16 MB de pilha, inicializa.

3. **`Init` recebe um callback que `Activate` chama no teardown.**
   Passar `NULL` deixa um ponteiro nulo que o engine invoca. O delegate fica vivo por toda a sessão.

4. **Ordem obrigatória:** `Init` → criar janela e swapchain → `Activate(hwnd)`. O engine instala
   os hooks de DXGI durante o `Init`.

5. **`ApplySettings` tem 32 argumentos, não um ponteiro de struct.**
   4 em registrador (`ECX`, `EDX`, `R8D`, `R9D`) e 28 na pilha a partir de `RSP+0x28`, slots de 8
   bytes. Derivado do disassembly completo: 68 instruções, 402 bytes, sem prólogo.

6. **Tela cheia exclusiva não pode ser capturada.** O jogo precisa estar em janela ou sem bordas.

7. **`PresentMon --output_file` falha em silêncio** quando há muitas sessões ETW ativas: imprime
   `Started/Stopped recording`, retorna 0 e não cria o arquivo. `--output_stdout` continua
   funcionando — o código usa stdout.

8. **Sessões ETW ficam presas** quando o PresentMon é morto antes de fechar. Depois de algumas, o
   medidor para até reiniciar o PC. `EtwSessionCleaner` resolve.

9. **A medição de FPS exige privilégio** (Administrador ou grupo "Usuários de log de desempenho",
   SID `S-1-5-32-559`). É a razão do `requireAdministrator` no manifesto.

## Binários de terceiros (não versionados)

| Arquivo | SHA-256 | Origem |
|---|---|---|
| `Lossless.dll` (v3.2.2, 7,5 MB) | `626B196D799606CD4250B7B29E04228692AB70CF56A5D1BBB56D748C8219F0EB` | Steam: `steamapps\common\Lossless Scaling` ou GDZ Optimizer |
| `PresentMon.exe` (v2.5.1, 957 KB) | `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191` | GDZ Optimizer ou `GameTechDev/PresentMon` |

`scripts/preparar-binarios.ps1` procura nos caminhos conhecidos e valida o hash.

## Como buildar

```powershell
.\scripts\bootstrap-sdk.ps1        # .NET 10 SDK em .tools/dotnet
.\scripts\preparar-binarios.ps1    # valida Lossless.dll e PresentMon.exe
.\scripts\publish.ps1              # compila, testa, gera exe/zip/instalador
```

`publish.ps1` **aborta** se o hash de um binário não bater, em vez de empacotar versão errada.

## Números medidos (GTA V, sem bordas, PresentMon, janela de 10 s)

| Configuração | FPS médio | 1% low |
|---|---|---|
| Jogo sozinho | 63,8 | 62,0 |
| Motor + FSR 1,5x | 138,7 | 61,4 |
| Motor + FSR + geração de quadros | 168,7 | 143,0 |

## Limitações conhecidas

- **Sem assinatura de código.** SmartScreen avisa em qualquer download. O hash do instalador é
  publicado nas instruções para conferência.
- **O `Lossless.dll` é redistribuído**, como outras ferramentas do gênero fazem. É decisão de
  licenciamento do dono do projeto, não técnica.
- **Nunca testado numa máquina limpa.** Toda validação rodou no ambiente de desenvolvimento.
- **Motor e medidor não funcionam ao mesmo tempo**: com o motor ativo, o PresentMon vê os quadros
  da janela do motor, e o filtro ignora processos que não são jogos. É comportamento correto.
- **PresentMon pode perder eventos** em carga muito pesada; o medidor então não retorna dados.

## Regras que o código segue

- Verificação SHA-256 antes de carregar ou empacotar qualquer binário de terceiros.
- Nenhuma exceção de manutenção (limpeza de ETW, hotkey, overlay) pode derrubar o app.
- `App.xaml.cs` relança elevado; se o UAC for recusado, o app abre mesmo assim e a escala continua
  funcionando.
- Ao fechar, o medidor e o hotkey são liberados — sem isso o PresentMon fica órfão e a sessão ETW
  trava as próximas medições.
