> Revisão 0.4.0: veja [funções do menu](FUNCOES-DO-MENU-0.4.0.md). São 13 entradas principais, três atalhos inferiores e quatro páginas auxiliares preservadas. Os detalhes históricos 0.3.0 estão em `historico/0.3.0`.

# Arquitetura — VL Optimizer 0.4.0

Implementação independente em .NET 10/WPF, mantendo a UI e os componentes de movimento do projeto 0.2.0. A especificação extraída do GDZ orienta comportamentos; seus binários, shaders, JavaScript e assemblies não são dependências do produto.

## Fronteiras

| Componente | Responsabilidade |
| --- | --- |
| MainWindow + Feature*.cs | Navegação, busca, seleção, revisão, progresso, cancelamento, estado vazio/erro e histórico. Operações são disparadas explicitamente. |
| IOptimizerBackend | Análise geral, prévia/aplicação/reversão das configurações e diário unificado de atividades. |
| ConfigurationEngine | Catálogo fechado, dependências por build/hardware, snapshot tipado, prévia de instância, revalidação de todos os alvos antes da primeira escrita e restauração condicional. |
| ConfigurationWindowsAccess e adaptadores | Registro, serviços, planos/hibernação, Steam VDF, NVAPI DRS, pagefile WMI, DNS IP Helper, DEVMODEW e exceções de power throttling. Sem comando livre recebido da UI. |
| OptimizerToolkit | Limpeza, disco, bibliotecas, FiveM, FPS, RAM, diagnóstico, processos, sessões e desinstalação. Construção dos serviços não ativa coletores nem agendamentos. |
| GameGraphicsService | GPU por executável: mescla tokens, verifica hash PE, journal antes da escrita e restaura valor/ausência exatos. |
| HardwareInventoryService | Sete consultas WMI fixas e inventário com origem e falhas isoladas. Não deduz XMP/EXPO, temperatura ou atualidade de driver. |
| MaintenanceService | Oito ações fechadas com etapas fixas, pré-condições, prazo, saída e resultado parcial/não verificado. Não transforma exit code em prova de saúde. |
| UwpManagementService | Inventário e remoção explícita de pacotes permitidos do usuário atual; não oferece restauração fictícia dos arquivos do pacote. |
| ScalingProfileService / OptiScalerService | Perfis locais validados e integração manual externa; instalação local de bibliotecas com manifesto/hash/backup. Sem motor gráfico próprio. |

## Transações e estados

Configurações usam `analisar → prévia → consentimento → revalidar → persistir → escrever → reler → registrar`. Uma prévia é vinculada ao objeto emitido pela instância, tem prazo e é consumida uma vez. O diário mantém schema 1 para ler registros anteriores. Tipos novos foram adicionados sem mudar o significado dos antigos.

O snapshot distingue chave/valor ausente, tipo do Registro e dados exatos. Serviços guardam inicialização, execução e atraso automático. Pagefile, DNS e modo de vídeo têm estados compostos. Uma escrita parcialmente efetiva pode guardar `ObservedAfter`, permitindo restaurar o estado intermediário conhecido sem tratar qualquer divergência como autorização.

A restauração confere se o estado é anterior, aplicado ou intermediário registrado; mudanças externas são preservadas. Recursos sobrepostos exigem ordem inversa, inclusive entre plano legado e catálogo. O cancelamento ocorre entre etapas seguras. Interrupção não é convertida em sucesso: snapshots permanecem recuperáveis.

As exclusões não possuem undo. Limpeza confere o mesmo manifesto, identidade Win32 do arquivo, datas/bytes e filtros no preview e apply; preserva arquivos novos, alterados ou em uso. Lixeira conserva confirmação Shell. Desinstaladores e ferramentas DISM/SFC têm efeitos próprios; o diário registra atividade e não promete revertê-los.

FiveM, GPU por jogo, sessões e OptiScaler mantêm backups próprios com identidade/caminho e conflitos. O histórico geral aponta a página de restauração correspondente; o botão geral de restauração se limita às configurações que possuem snapshot compatível.

## Processos e dependências

A aplicação continua `asInvoker`. Análise não exige elevação; recursos administrativos bloqueiam a prévia quando necessário. Reabrir elevado é uma escolha explícita de UI, sem serviço residente. O modo smoke bloqueia ações de escrita, exportação interativa e launch externo.

Executáveis externos são casos fechados: utilitários do Windows em caminhos fixos; desinstalador registrado e revalidado; PresentMon escolhido; programa externo de scaling escolhido. Argumentos são estruturados. Não há terminal livre, scripts de pacote, execução de código extraído ou comunicação com servidores GDZ.

Captura PresentMon recebe PID, duração e sessão ETW exclusiva. O acompanhamento lê apenas o CSV temporário dessa sessão, suporta chunks/linhas parciais e mantém limites de bytes/linhas/grupos. Cancelar encerra somente o coletor/sessão criados pelo VL. As estatísticas finais são recalculadas pelo parser completo.

## Compatibilidade e limites

Windows 10/11 cliente x64, build ≥19041, com bloqueios adicionais por definição. Recursos exclusivos Windows 11 exigem build 22000/22631 quando aplicável. Drivers, edição, políticas e dispositivos podem impedir uma operação mesmo em uma build elegível. Não há homologação Windows Server, ARM64 ou x86.

O WMI detalhado usa resultados bidirecionais: o smoke nativo detectou falha COM ao enumerar resultados forward-only via scripting, corrigida antes da entrega. As consultas são assíncronas em relação à UI; cancelamento é atendido entre consultas e não interrompe uma chamada COM já em andamento.

Há coordenação por arquivo/semáforo nas persistências que exigem exclusão mútua e gravação temporária + flush + rename. Isso não constitui defesa contra outro processo malicioso com os mesmos privilégios nem backup integral do sistema.

A composição atual usa parciais WPF, sem reescrever a base em MVVM. Os serviços são testáveis sem UI. Uma evolução estrutural útil é separar viewmodels por área após estabilizar os fluxos; a revisão não afirma que o número de linhas ou a escolha WPF comprovam menor consumo que Electron.
## Organização 0.4.0

ProductPages centraliza as 16 rotas e o escopo explícito das configurações. A UI reaproveita o motor transacional sem duplicar efeitos Windows. A revisão filtra a seleção para a área atual, e a busca abre a aba responsável pelo ajuste.

OptiScalerSettingsService edita dez chaves existentes do INI, preserva encoding/comentários e usa backups ordenados por sequência, com hash do jogo/arquivo e conflitos. FpsOverlayWindow é uma janela externa sem ativação e com passagem de cliques; recebe exclusivamente os dados reais da captura própria, fecha ao término e não injeta código. A manutenção passou a oito ações; UpdateServices é uma configuração reversível separada.
