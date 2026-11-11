# Recuperação — VL Optimizer

Este documento existe para que o projeto possa ser reconstruído **do zero** numa máquina limpa,
sem depender de memória de ninguém e sem depender desta máquina.

## O que já está protegido

O código-fonte completo está em `github.com/alex256061-cpu/VL-Optimizer`, branch `main`.
Clonar já devolve tudo que importa:

```powershell
git clone https://github.com/alex256061-cpu/VL-Optimizer.git
cd VL-Optimizer
```

O repositório contém: `src/` (Core e App), `tests/`, `scripts/`, `installer/`, documentação em `docs/`
e o `.sln`. São 173 arquivos versionados.

## O que NÃO está no repositório e precisa ser reobtido

### 1. SDK do .NET 10

O projeto usa um SDK local em `.tools/dotnet` (não versionado, ~300 MB). O script de bootstrap
baixa de novo:

```powershell
.\scripts\bootstrap-sdk.ps1
```

Alternativa: instalar o .NET 10 SDK pelo instalador oficial e usar `dotnet` do PATH. O projeto
tem `global.json` fixando `10.0.403` com `rollForward: latestPatch`.

### 2. Visual Studio Build Tools (C++)

Necessário apenas para os componentes nativos, que **foram removidos** na versão atual.
Se forem reintroduzidos, instalar "Desenvolvimento para desktop com C++".

Atenção: nesta máquina o instalador gravou em `C:\Program` (caminho truncado por causa do espaço
em "Program Files"). O `vswhere` reporta `installationPath = "C:\Program"` e `vcvars64.bat` fica em
`C:\Program\VC\Auxiliary\Build\vcvars64.bat`. Numa instalação normal o caminho é
`C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\`.

### 3. Lossless.dll — o motor de escala

Binário de terceiros, nunca versionado. O app aceita apenas a build cujo SHA-256 é:

```
626B196D799606CD4250B7B29E04228692AB70CF56A5D1BBB56D748C8219F0EB
```

Origens possíveis:
- Instalação do Lossless Scaling pela Steam, em
  `C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\Lossless.dll`
- O pacote do GDZ Optimizer, em
  `%LOCALAPPDATA%\Programs\GDZ Optimizer\resources\app.asar.unpacked\bin\Lossless.dll`

O `EnginePayloadResolver` verifica o hash antes de carregar e recusa qualquer outra versão, então
uma build diferente não quebra em silêncio — ela é reportada.

### 4. PresentMon.exe — o medidor de FPS

Também de terceiros. Origem conhecida nesta máquina:

```
%LOCALAPPDATA%\Programs\GDZ Optimizer\resources\app.asar.unpacked\bin\PresentMon\PresentMon.exe
```

SHA-256: `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191`

Alternativa: baixar do repositório oficial `GameTechDev/PresentMon` (versão 2.5.x).

## Reconstruir o pacote

Com o SDK no lugar e os dois binários presentes:

```powershell
.\scripts\publish.ps1
```

O script compila, roda os testes, copia `Lossless.dll` e `PresentMon.exe` verificando o hash de
cada um, e gera:

- `release\VL-Optimizer-0.4.0.exe`
- `release\VL-Optimizer-0.4.0-portatil.zip`
- `release\Instalar-VL-Optimizer-0.4.0.exe` (IExpress)

Se o hash do motor não bater, o script **aborta** em vez de empacotar a versão errada.

## Verificação rápida de que tudo está de pé

```powershell
# compilar e testar (deve dar 240/240)
.\.tools\dotnet\dotnet.exe build VLOptimizer.sln -c Release
.\.tools\dotnet\dotnet.exe run --project tests\VLOptimizer.Tests -c Release --no-build
```

## Contexto das decisões (para não repetir investigação)

O que foi descoberto sobre o motor, e que custou tempo real para achar:

1. **O motor não é injetado no jogo.** Ele é um compositor externo: captura a janela em primeiro
   plano por DXGI Desktop Duplication e apresenta o resultado na própria janela. Injeção de
   processo foi tentada e descartada — era o modelo errado.

2. **`Init` exige uma thread STA com pilha grande.** Chamado da thread principal de um console
   .NET, falha com proteção (`0xc0000005` em `0x0`). Numa thread STA com 16 MB de pilha,
   inicializa. `ScalingEngineHost` cria essa thread internamente.

3. **`Init` recebe um callback que o engine chama no teardown.** Passar `NULL` deixa um ponteiro
   nulo e o engine salta para ele. O host mantém o delegate vivo.

4. **A ordem é `Init` → criar janela/swapchain → `Activate`.** O engine instala os hooks de DXGI
   durante o `Init`.

5. **`ApplySettings` recebe 32 argumentos**, não um ponteiro de struct: 4 em registrador
   (`ECX`/`EDX`/`R8D`/`R9D`) e 28 na pilha a partir de `RSP+0x28`, em slots de 8 bytes. Derivado
   do disassembly completo (68 instruções, 402 bytes, sem prólogo).

6. **Jogo em tela cheia exclusiva não pode ser capturado.** Precisa ser janela ou sem bordas.
   Vale para o Lossless também — não é limitação nossa.

7. **`PresentMon --output_file` falha silenciosamente** nesta máquina: imprime
   `Started/Stopped recording`, retorna 0, e não cria o arquivo, quando há muitas sessões ETW
   ativas. `--output_stdout` continua funcionando. O código usa stdout e cai para ele.

8. **Sessões ETW ficam presas** quando o PresentMon é morto antes de fechar. Depois de algumas, o
   medidor para até reiniciar o PC. O GDZ tem esse defeito. `EtwSessionCleaner` limpa as sessões
   com prefixo do próprio VL antes e depois de medir.

9. **A medição de FPS precisa de privilégio.** PresentMon abre uma sessão ETW, e o Windows exige
   Administrador ou o grupo "Usuários de log de desempenho" (`S-1-5-32-559`). Por isso o
   `app.manifest` pede `requireAdministrator`.

## Números medidos em GTA V

| Config | FPS médio | 1% low |
|---|---|---|
| Sem motor | 63,8 | 62,0 |
| Motor + FSR | 138,7 | 61,4 |
| Motor + FSR + geração de quadros | 168,7 | 143,0 |

Medido com PresentMon em fluxo contínuo, janela de 10 s, GTA V em modo sem bordas.

## Pendências conhecidas

- **SorSmartScreen sem assinatura**: o instalador mostra "aplicativo não reconhecido" até ter
  reputação ou um certificado de assinatura.
- **O `Lossless.dll` é redistribuído no instalador.** É decisão de licenciamento, não técnica.
  Se o autor do Lossless Scaling contestar, o caminho é remover o binário do pacote e exigir que
  o cliente aponte uma instalação existente.
- **Nunca foi testado numa máquina limpa.** Toda validação rodou nesta máquina, que já tem o SDK,
  o grupo de log de desempenho e os binários. É o teste mais valioso que falta.
