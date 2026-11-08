> Revisão 0.4.0: veja [funções do menu](FUNCOES-DO-MENU-0.4.0.md). São 13 entradas principais, três atalhos inferiores e quatro páginas auxiliares preservadas. Os detalhes históricos 0.3.0 estão em `historico/0.3.0`.

# Distribuição e evolução comercial

Implementação e plano de evolução registrados em 08/11/2026. Os scripts de publicação, instalação e desinstalação estão presentes no repositório. A execução real da instalação/desinstalação não foi validada nesta revisão. A existência de um executável ou instalador não representa assinatura, homologação comercial ou compatibilidade já validada. Resultados concretos de empacotamento e instalação ficam em [VALIDACAO.md](VALIDACAO.md).

## Formatos implementados no script de publicação

O alvo de publicação é Windows 10/11 cliente x64, build >=19041 e limites por recurso, com .NET 10. `scripts/publish.ps1` publica Release para `win-x64`, autocontido, com `PublishSingleFile` e extração das bibliotecas nativas necessárias. A mesma publicação alimenta os seguintes artefatos em `release`:

- `VL-Optimizer-0.4.0.exe`: aplicativo portátil.
- `VL-Optimizer-0.4.0-portatil.zip`: aplicativo e `LEIA-ME.txt`.
- `Instalar-VL-Optimizer-0.4.0.exe`: pacote autoextraível IExpress, salvo quando a publicação não recebe `-SkipInstaller`.
- `SHA256SUMS.txt`: hashes SHA-256 dos EXEs e ZIPs existentes na pasta de release.

O Release público 0.4.0 inclui o arquivo específico `SHA256SUMS-0.4.0.txt`, o guia `COMECE-AQUI-0.4.0.txt`, o código-fonte e `VERIFICACAO-0.4.0.json`. O portátil final passou pelo smoke; o CAB do instalador foi extraído e seu manifesto conferido, sem executar instalação. Consulte os números em artifacts/release-validation-0.4.0.json.

O script existe e define esses formatos; a presença e os hashes efetivos dos artefatos gerados precisam ser registrados pela execução do build. O pacote autocontido inclui o runtime .NET necessário, mas não dispensa recursos nativos compatíveis do Windows. [Publicação .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/).

Autocontido aumenta o tamanho de distribuição e vincula o runtime à versão publicada. Instalar um runtime mais novo no Windows não atualiza o runtime empacotado. O responsável pelo produto precisa acompanhar correções do .NET e republicar o app. Não há comparação medida de tamanho ou consumo nesta decisão.

Trimming e AOT não foram habilitados. O EXE autocontido pode extrair dependências nativas para o cache do runtime; “portátil” não significa execução sem arquivos temporários. Uma alteração futura da estratégia de publicação exige validar WPF, serialização, recursos e execução sem SDK.

## Instalação por usuário

`installer/Install.ps1` instala em `%LOCALAPPDATA%\Programs\VLOptimizer`, cria o atalho no menu Iniciar do usuário e registra desinstalação em HKCU. O pacote o inicia via Windows PowerShell sem pedido de elevação. Não cria serviço, tarefa, entrada de logon ou execução residente, e a instalação não aplica otimizações.

O script de publicação chama `%WINDIR%\System32\iexpress.exe` para transportar e iniciar essa rotina. Sem esse executável, a geração do instalador falha; `-SkipInstaller` permite gerar somente o portátil. IExpress é o empacotador autoextraível, enquanto cópia, atalhos, rollback limitado e desinstalação são responsabilidades dos scripts embarcados. A verificação do pacote não equivale a executar a instalação em uma máquina de teste.

Inno Setup não integra o pipeline atual. Caso seja adotado no futuro, usar modo não administrativo e diretórios/atalhos do usuário, conforme documentação do fornecedor. Não baixar um compilador ou executar instalador remoto implicitamente no PC do cliente. [Inno Setup: privilégios](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), [Inno Setup: constantes de diretório](https://jrsoftware.org/ishelp/topic_consts.htm).

O aplicativo portátil não instala atalhos ou entrada de desinstalação. Ambas as formas usam `%LOCALAPPDATA%\VLOptimizer` para o histórico; não o armazenam na pasta do EXE. A interface e o `LEIA-ME.txt` informam esse caminho.

### Comportamento atual e limites

- Instalação nova apenas: pasta de destino, registro de desinstalação ou atalho preexistente interrompem o processo. Não há atualização sobre a instalação antiga. Desinstalar a anterior preserva o histórico e permite instalar a nova versão.
- O instalador valida destino e ancestrais contra reparse points e confere hashes de `VLOptimizer.exe`, `Uninstall.ps1` e `LEIA-ME.txt` pelo `payload.json` antes de copiar.
- A cópia é preparada em uma pasta exclusiva no mesmo diretório de programas; um rename publica a pasta final antes de criar atalho e registro de desinstalação.
- Falhas tratadas tentam remover apenas chave, atalho e arquivos conhecidos criados naquela execução, sem exclusão recursiva. O rollback é limitado: encerramento abrupto, queda de energia ou erro no próprio rollback podem deixar uma instalação parcial que precisará de revisão manual.
- `installer/Uninstall.ps1` só aceita ser executado na pasta instalada esperada, pede confirmação e recusa remoção com a instância instalada aberta. Remove arquivos conhecidos, atalho e entrada de desinstalação; preserva sempre o histórico local.
- Desinstalar não desfaz alterações de startup ou energia. A confirmação orienta o usuário a restaurar pelo histórico antes, caso deseje.
- Os scripts usam caminhos literais e não delegam remoção recursiva a outro shell. Caminhos com espaços/acentos e condições de bloqueio ainda precisam dos testes de integração descritos em `VALIDACAO.md`.

## Pipeline de release

1. Fixar uma versão de produto e registrar o SDK .NET exato usado no build.
2. Compilar Release com warnings tratadas, executar testes apropriados e registrar saídas.
3. Publicar `win-x64` autocontido e validar execução em Windows 11 x64 sem SDK/Visual Studio.
4. Gerar o pacote portátil e instalador a partir da mesma publicação.
5. Calcular SHA-256 dos artefatos finais e incluir manifesto de versão/arquitetura; recalcular depois de assinatura, se houver.
6. Executar instalação, atualização manual e desinstalação em VM com usuário comum, caminho com acentos e instalação anterior.
7. Verificar que abrir/instalar o pacote não aplica otimizações e que o histórico anterior é preservado.
8. Assinar e validar apenas quando houver certificado e cadeia de release configurados; registrar exatamente o que foi assinado.

`scripts/build.ps1` compila a solução e executa os testes; `-Publish` chama o script de publicação depois. `scripts/bootstrap-sdk.ps1` fornece o SDK local definido no script e confere SHA-512 do download contra os metadados oficiais. Registrar o SDK efetivo do build; possuir um runtime local não é evidência de testes aprovados.

Os scripts têm funções diferentes: gerar o instalador não executa a instalação. Validar publicação, extração, instalação, desinstalação e preservação do histórico como etapas separadas, com seus resultados concretos.

## Assinatura de código: ainda não implantada

Preparar Authenticode para EXE/instalador e scripts distribuídos, quando aplicável. Será necessário certificado legítimo do editor, acesso protegido à chave, serviço de timestamp e SignTool do Windows SDK. Não gerar certificado local e apresentá-lo como assinatura publicamente confiável.

Usar digest SHA-256 e timestamp RFC 3161 com SHA-256; verificar a assinatura e o publicador esperado no pipeline. Um hash detecta diferença apenas em relação ao hash que o usuário recebeu; sozinho não autentica quem publicou o pacote. Assinatura também não é prova de que o software foi amplamente testado ou garantia de ausência de avisos do Windows. [SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool), [timestamp Authenticode](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures).

Nenhum certificado, assinatura, identidade de editor ou reputação SmartScreen é presumido nesta entrega. Chaves privadas e senhas não devem ser armazenadas no repositório ou em exemplos de comando.

## Atualizações verificadas: ainda não implantadas

O MVP não possui autoatualizador. A atualização inicial exige fechar o aplicativo, desinstalar a instalação anterior e instalar o novo pacote; o histórico local é preservado. Esse procedimento ainda exige teste em VM. Antes de suportar atualização automática, implementar e testar:

- Manifesto assinado com versão, arquitetura, hash, tamanho e compatibilidade mínimos.
- Transporte HTTPS, verificação criptográfica e autenticação do publicador; não confiar somente em URL/hash do mesmo servidor comprometido.
- Proteção contra downgrade/replay e rejeição de formato ou pacote inesperado.
- Download em área de staging, verificação antes de executar, substituição consistente e recuperação em caso de interrupção.
- Migração versionada do histórico, backup antes de migração e comportamento explícito ao tentar voltar para versão antiga.
- Reautenticação/consentimento para eventual operação elevada e separação entre o processo instalador e a UI sem privilégio.

Não usar `irm | iex` ou equivalentes como mecanismo de atualização do produto. A pesquisa de utilitários de terceiros não autoriza incorporar sua execução ao cliente.

## Dados locais e suporte

Diagnóstico e operações básicas não exigem conta ou telemetria. O histórico contém nomes/comandos de inicialização, informações do computador e configurações anteriores: dados potencialmente pessoais. Exportar para suporte deve ser uma ação explícita, com possibilidade de revisão/redação dos campos sensíveis. Não publicar logs automaticamente.

O produto precisa explicar onde ficam seus dados, como exportar e como removê-los. Remover histórico pode eliminar a capacidade de desfazer mudanças; solicitar ação explícita para isso. Um backup local do aplicativo não substitui backup de arquivos nem imagem do sistema.

Antes de distribuição comercial: concluir matriz de validação, revisão de licenças, contrato de suporte, assinatura, canal de release e política de atualização. O [Winhance usa licença com restrição de concorrência](https://raw.githubusercontent.com/memstechtips/Winhance/main/LICENSE.txt); seu código não deve compor este produto. Se código MIT do [WinUtil](https://raw.githubusercontent.com/ChrisTitusTech/winutil/main/LICENSE) vier a ser incorporado, preservar os avisos exigidos e documentar exatamente os componentes. Referência de ideias não significa reutilização de código.
