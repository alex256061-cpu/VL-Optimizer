# VL Optimizer — Instruções de instalação

## Download

Página de releases: <https://github.com/alex256061-cpu/VL-Optimizer/releases/latest>

Arquivos disponíveis:

| Arquivo | Tamanho | Para quem |
|---|---|---|
| `Instalar-VL-Optimizer-0.4.0.exe` | 48,5 MB | Instalação normal, com atalho no menu Iniciar |
| `VL-Optimizer-0.4.0-portatil.zip` | 59,9 MB | Quem não quer instalar, ou quer rodar de um pendrive |

---

## Requisitos

| Item | Mínimo |
|---|---|
| Windows | 10 ou 11, **64 bits** |
| Conta | Usuário comum com direito de administrador (para o UAC) |
| Espaço em disco | ~200 MB depois de instalado |
| Jogo | Em modo **janela** ou **sem bordas** (tela cheia exclusiva não pode ser capturada) |

Não precisa ter o Lossless Scaling instalado. O motor de escala vai junto no pacote.

---

## Instalação

### 1. O Windows vai avisar que o arquivo não é seguro

Isso aparece porque o VL Optimizer **não tem assinatura digital** (um certificado custa cerca de
R$ 1.500 por ano; ver "Por que o Windows avisa" abaixo). Não significa que o arquivo está
infectado.

No **Microsoft Edge**:
1. Abre o download → o aviso aparece: *"VL-Optimizer... não é comumente baixado"*
2. Clique nos três pontos `···` ao lado do arquivo → **"Manter"**
3. Se aparecer outro aviso → **"Mostrar mais"** → **"Manter mesmo assim"**

No **Google Chrome**:
1. Abre o download → *"Arquivo perigoso"* ou *"Não é comumente baixado"*
2. Clique em **"Manter"** → confirme

### 2. Executar o instalador

Duplo-clique em `Instalar-VL-Optimizer-0.4.0.exe`.

Vai aparecer a tela azul do Windows: **"Deseja permitir que este aplicativo faça alterações..."**
Clique em **Sim**.

> **Por que pede admin:** a medição de FPS usa uma sessão de rastreamento do Windows (ETW), e o
> Windows não permite isso sem elevação. A escala funciona sem, mas o FPS não mede.

O instalador:
- Verifica o SHA-256 de cada arquivo antes de copiar
- Instala em `%LOCALAPPDATA%\Programs\VLOptimizer`
- Cria atalho no menu Iniciar
- Registra a desinstalação no Painel de Controle

**Não** pede reinicialização. **Não** altera configurações do Windows na instalação.

### 3. Abrir

Menu Iniciar → **VL Optimizer**. Aceitar o UAC.

---

## Alternativa: versão portátil (sem instalar)

1. Baixe `VL-Optimizer-0.4.0-portatil.zip`
2. Clique com o botão direito → **Extrair tudo** (Windows já sabe extrair ZIP)
3. Abra a pasta extraída e execute `VLOptimizer.exe`
4. Aceite o UAC

Os 4 arquivos precisam ficar **na mesma pasta**. Se você mover só o `.exe`, a escala e o medidor
de FPS param de funcionar.

---

## Primeiro uso

### Escala (VL Scaling)

1. Abra o jogo primeiro, em **modo janela** ou **sem bordas**
2. No VL Optimizer: **VL Scaling** → **Escolher jogo** → selecione o jogo na lista
3. **Iniciar motor**
4. Aparece uma janela **"VL Scaling"** com o jogo escalado dentro
5. Ajuste o perfil (algoritmo, fator de escala, geração de quadros) e clique **Aplicar**

> Se a janela mostrar **"Aguardando jogo…"**, o jogo está em tela cheia exclusiva. Mude para
> sem bordas nas configurações gráficas do jogo.

### Medição de FPS

1. **Medições** → aba **FPS**
2. **Iniciar medição ao vivo**
3. Entre no jogo. Os números aparecem em ~4 segundos.

**Atalho global `Ctrl+Alt+B`**, funciona com o jogo em foco:
- Sem medição rodando → inicia
- Com medição rodando → marca a **base** para comparar

Fluxo de comparação:
1. Jogue sem escala por alguns segundos
2. `Ctrl+Alt+B` → marca a base
3. Ative a escala no VL
4. O painel mostra `Base 64,0 FPS → agora 138,7 FPS · +117%`

---

## Problemas comuns

### "O Windows protegeu o computador" ao abrir o instalador
SmartScreen por falta de assinatura. Clique em **"Mais informações"** → **"Executar assim mesmo"**.

### A janela do VL Scaling fica preta ou mostra "Aguardando jogo…"
O jogo está em tela cheia exclusiva. Nas opções gráficas do jogo, mude para **janela** ou
**sem bordas**. Vale para qualquer ferramenta de captura (OBS, Xbox Game Bar, Lossless).

### O medidor de FPS não mostra número nenhum
Verifique, nesta ordem:
1. **Aceitou o UAC ao abrir?** Sem admin a medição não funciona — feche o VL e abra de novo.
2. **O jogo está renderizando?** No menu de pausa o jogo para de gerar quadros.
3. Aguarde 4 a 6 segundos após iniciar a medição.
4. Reinicie o PC se o medidor parou de funcionar depois de vários usos — sessões de rastreamento
   do Windows podem ficar presas.

### "Motor não encontrado" no VL Scaling
O `Lossless.dll` não está na pasta do `VLOptimizer.exe`. Na versão instalada isso não deveria
acontecer; na portátil, confira se os 4 arquivos foram extraídos juntos.

### SmartScreen ao abrir o VL Optimizer depois de instalar
Inofensivo, mesma causa da assinatura.

---

## Desinstalar

**Painel de Controle** → **Programas e Recursos** → **VL Optimizer** → Desinstalar.

Ou no menu Iniciar → pasta **VL Optimizer** → **Desinstalar**.

O histórico de medições em `%LOCALAPPDATA%\VLOptimizer` é preservado de propósito. Apague a pasta
manualmente se quiser remover tudo.

---

## Por que o Windows avisa

O VL Optimizer não tem certificado de assinatura de código. Sem ele, o Windows mostra aviso em
qualquer executável baixado da internet — inclusive em programas legítimos. É uma escolha do
projeto, não um defeito do arquivo.

Como conferir que o arquivo que você baixou é o publicado:

```powershell
Get-FileHash .\Instalar-VL-Optimizer-0.4.0.exe -Algorithm SHA256
```

Deve dar:

```
09F8B36EAA130A7F406C95FBC590BD5EB0C23BEA72F66B31FD69AC4C9597962E
```

---

## Avisos honestos

- **O pacote inclui o `Lossless.dll`**, que é software comercial de terceiros (Lossless Scaling).
  Ele é distribuído aqui pela mesma razão que outras ferramentas fazem isso. Se isso mudar, o
  download pode sair do ar.
- **Anti-cheat.** Não injetamos nada no jogo — o motor captura a tela como o OBS faz. Ainda assim,
  se um jogo tiver anti-cheat com política restritiva, teste fora de partidas competitivas.
- **Não foi testado numa máquina limpa.** Toda validação foi feita no ambiente de desenvolvimento.
