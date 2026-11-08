# Catálogo, evidências e limites

Revisão: 08/11/2026. Catálogo de operações do MVP: versão `1.0`. Este catálogo descreve efeitos verificáveis e riscos; não representa comprovação de ganho em FPS ou latência. A aplicação deve apresentar os dados e pedir seleção antes da prévia de escrita.

## Matriz do MVP

| Operação/identidade | Categoria e evidência | Compatibilidade/pré-condições | Privilégio e risco | Aplicação/verificação | Idempotência/reversão |
|---|---|---|---|---|---|
| `DisableStartup`, versão 1.0; alvo identificado por origem e nome do valor HKCU Run | Potencial redução de trabalho no logon. Windows documenta o mecanismo Run; benefício depende do app e do uso. Sem previsão numérica de ganho. [S1], [S2] | Alvo Windows 11 x64; integração em VM pendente; entrada existente, tipo suportado, selecionada explicitamente e prévia ainda atual; sem bloqueio/cautela protegida | Usuário atual. Risco moderado: pode retirar app necessário do logon. Não altera o executável nem o inicia | Guardar nome, tipo e valor original; retirar apenas o valor aprovado; reler e confirmar ausência | Ausente após aplicação corresponde ao estado aplicado, sem repetir alteração. Restaurar o valor/tipo original somente se não houver conflito; entrada recriada por terceiro exige revisão |
| `SetPowerPlan`, versão 1.0; alvo GUID de plano existente | Configuração/preferência de energia, com desempenho potencial dependente da carga. Não é prova de aceleração. [E1]–[E5] | Alvo Windows 11 x64; integração em VM pendente; plano enumerado e GUID atual coincide com prévia; permissão/política confirmada pelo retorno da operação | Usuário atual, sujeito a política/permissão. Risco baixo a moderado: autonomia, temperatura, ruído e consumo podem mudar | Guardar GUID anterior; chamar `PowerSetActiveScheme`; ler `PowerGetActiveScheme` e comparar GUID | Plano já ativo: ignorar como já configurado. Reverter só se o plano anterior ainda existir e o estado atual for o esperado; mudança posterior exige conflito |
| `AnalyzeAsync`; somente leitura | Diagnóstico, cerca de cinco segundos de observação. [M1]–[M5] | Recursos disponíveis e permissões de consulta; ausência de um campo não invalida todos os demais | Usuário atual. Baixo risco; custo da própria coleta deve ser medido | Inventário, amostras e duração efetiva; valores indisponíveis são explícitos | Cada execução gera nova observação; não exige reversão |
| Discos + páginas oficiais; somente leitura/abertura de Settings | Diagnóstico de capacidade e espaço livre; limpeza permanece sob controle do Windows. [W1] | Disco acessível; URI suportada pela build/edição | Usuário atual. Baixo risco; mudanças feitas depois em Settings pertencem ao fluxo do Windows | Ler capacidade/espaço e abrir página documentada por ação do usuário | Não há arquivos excluídos pelo VL Optimizer e não existe reversão local de mudanças externas |

A identidade do alvo não é uma linha de comando executável. O executor não aceita comandos arbitrários nem decide o que desativar apenas pelo nome. Nenhuma seleção automática em massa é justificável pela presença de uma chave Run.

## Inicialização: regras de segurança

O gerenciador de escrita é limitado a `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`. Enumerar HKLM ou pastas Startup, quando disponível, não autoriza alterá-los. RunOnce, tarefas agendadas, serviços, extensões e tarefas de inicialização de apps empacotados não estão incluídos na gestão completa do MVP.

Itens essenciais, segurança, acessibilidade, agentes corporativos e desconhecidos não entram em recomendação automática. A implementação deve informar origem, comando original e cautela; seleção explícita não elimina o risco. Uma lista de nomes protegidos é defesa adicional e não classifica exaustivamente todos os produtos. Ausência de assinatura, editor ou metadados confiáveis não significa inutilidade.

Para mudanças reversíveis, guardar valor bruto e `RegistryValueKind`; não salvar somente a versão expandida de `%VARIAVEL%`. A comparação antes da aplicação precisa detectar valor alterado, removido ou substituído após a análise. Na volta, uma entrada com o mesmo nome e conteúdo diferente deve produzir conflito, sem sobrescrita.

O formato interno `StartupApproved` não é adotado como API de escrita documentada. A entrada Run removida pode ser recriada pelo aplicativo original; nesse caso, uma nova análise deve refletir o estado real. Run não garante ordem ou execução imediata dos itens no logon. [S1]

## Energia: regras de segurança

Enumerar planos com `PowerEnumerate(ACCESS_SCHEME = 16)`, ler o ativo com `PowerGetActiveScheme` e liberar o GUID alocado com `LocalFree`. Não analisar a saída localizada de `powercfg` como fonte principal quando a API retorna a informação diretamente. Verificar códigos de erro e resultado final. [E1]–[E3]

O MVP trata restrições de política e permissão pelo código retornado por `PowerSetActiveScheme`: retorno diferente de zero produz falha registrada, sem elevação ou tentativa de contornar a restrição. Uma nova leitura confirma o GUID somente depois de a chamada terminar. A prévia não garante que a escrita será permitida. A API `PowerSettingAccessCheck` pode antecipar restrições, mas não é chamada nesta versão. Sua documentação é referência de evolução; `ERROR_ACCESS_DISABLED_BY_POLICY` (1260) indica bloqueio por política. [E3], [E4]

Não duplicar ou importar planos para fabricar uma opção de “desempenho máximo”. Não modificar Modern Standby ou parâmetros individuais de CPU. A máquina pode fornecer apenas um plano; isso não é defeito a corrigir. A recomendação deve distinguir desktop/notebook e bateria/tomada sem presumir ganho térmico ou de desempenho.

Planos clássicos e o moderno “Modo de energia” são controles distintos. A seleção de GUID feita pelo MVP não é apresentada como gerenciamento integral dos modos AC/DC da página de Configurações. [E5]

## Significado das métricas

| Métrica | Cálculo/limite | Alegação permitida |
|---|---|---|
| CPU média | Amostras de `GetSystemTimes`; Kernel inclui Idle. Usar `(ΔKernel + ΔUser − ΔIdle) / (ΔKernel + ΔUser)` quando denominador válido. Acima de 64 CPUs, há limitação por grupo | Uso observado naquela janela, não gargalo confirmado |
| RAM instalada/utilizável/disponível | Fontes diferentes e unidades convertidas corretamente; firmware pode reservar parte da RAM | Estado de memória, sem converter menor ocupação em ganho |
| Memória comprometida | `CommitTotal × PageSize` via `GetPerformanceInfo` | Comprometimento observado; não equivale só a RAM física ocupada |
| Processos | Working set é uma medida de memória; tempo de CPU exige duas leituras e normalização | Consumidores visíveis/observados, sem encerrar ou culpar automaticamente |
| Discos | Capacidade e espaço livre por volume | Espaço disponível; não mede saúde física, IOPS ou tempo de resposta |
| GPU | Nome do adaptador quando disponível; carga não implementada | Inventário, sem porcentagem fictícia |
| Temperatura/sensores | Sem fonte implementada | Indisponível; zero não é substituto |

Fontes: [M1]–[M5]. A documentação da Microsoft sobre dados de GPU usa engines e WDDM 2 ou posterior; integrar uma coleta correta é trabalho específico e foi adiado. Somar engines pode ultrapassar o significado pretendido de utilização global. [M6]

## Histórico e resultados

Antes de escrever, persistir o estado anterior com identificador da operação, versão, horário e alvo. Um arquivo temporário no mesmo diretório seguido de substituição atômica reduz o risco de JSON parcial. Um registro pendente após falha precisa de inspeção de estado, pois a escrita do Windows e o diário não formam uma única transação.

Sucesso só depois de verificação posterior. Preservar distinção entre concluído, ignorado/já configurado, falhou, reinicialização necessária, não verificado, revertido e conflito. Backup local de uma chave e GUID não é ponto de restauração nem imagem do sistema.

Antes/depois registra configurações e itens realmente alterados, erros, duração da observação e necessidade de nova medição. Comparações de CPU/RAM obtidas sob cargas diferentes não comprovam causalidade. O MVP não mede jogos, FPS, frametime ou ping. Sem protocolo equivalente e diferença confiável, a conclusão é **“Sem melhoria mensurável neste teste”**. A próxima inicialização pode ser necessária para observar efeito de mudanças em startup.

## Exclusões deliberadas

Não aplicar limpeza de Registro/RAM, mudanças genéricas de serviço, Defender/firewall/Update, pagefile, BCD/HPET, rede/TCP/DNS, drivers, overclock/undervolt ou shader caches. Não executar DISM/SFC a cada análise. Limpeza própria de arquivos foi adiada até validação de escopo, reparse points, bloqueios e resultados; este MVP não relata espaço recuperado que não tenha excluído e verificado.

## Projetos pesquisados e licenças

| Projeto | Achado | Uso nesta entrega |
|---|---|---|
| [Reverse-Engineering-ChatGPT](https://github.com/xianminx/Reverse-Engineering-ChatGPT) | Repositório de prompts e tentativas de extrair instruções do ChatGPT; não é ferramenta de inspeção de executáveis Windows | Clonado em `.tools/Reverse-Engineering-ChatGPT` a pedido do usuário; sem aplicação instalável ou código executado. A análise estática do GDZ usou ferramentas próprias: [relatório](GDZ-ENGENHARIA-REVERSA.md) |
| [WinUtil](https://github.com/ChrisTitusTech/winutil) | Utilitário Windows com catálogo/presets e mudanças globais, cuja execução requer administrador. Código sob [MIT](https://raw.githubusercontent.com/ChrisTitusTech/winutil/main/LICENSE) | Referência de organização e documentação; um tweak existir ali não demonstra benefício neste PC |
| [Winhance](https://github.com/memstechtips/Winhance) | Aplicação de ajustes com código disponível; licença atual [PolyForm Shield](https://raw.githubusercontent.com/memstechtips/Winhance/main/LICENSE.txt) contém restrição de concorrência | Referência funcional; não copiar/derivar/rebatizar seu código para este produto concorrente |

O [tutorial do autor do WinUtil](https://www.youtube.com/watch?v=6UQZ5oQg8XA), indicado no README do projeto, é referência de fluxo. Vídeos e presets de terceiros não substituem a documentação técnica ou medições controladas. Nenhum script remoto desses projetos foi executado para a pesquisa.

## Fontes técnicas primárias

- **S1** — [Run e RunOnce](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys): funcionamento, origens e limitações do logon.
- **S2** — [Aplicativos de inicialização no Windows](https://support.microsoft.com/en-gb/windows/experience/startup-boot/configure-startup-applications-in-windows): opções oficiais e locais reconhecidos.
- **E1** — [PowerEnumerate](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate).
- **E2** — [PowerGetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme).
- **E3** — [PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme).
- **E4** — [PowerSettingAccessCheck](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersettingaccesscheck), referência para futura pré-checagem; não utilizada pelo MVP.
- **E5** — [Modo de energia](https://support.microsoft.com/en-us/windows/experience/change-the-power-mode-for-your-windows-pc) e [GetSystemPowerStatus](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getsystempowerstatus).
- **M1** — [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes).
- **M2** — [GetPhysicallyInstalledSystemMemory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory).
- **M3** — [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex).
- **M4** — [PERFORMANCE_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information).
- **M5** — [Process.TotalProcessorTime](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime?view=net-10.0).
- **M6** — [GPUs no Gerenciador de Tarefas](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/).
- **W1** — [Páginas oficiais `ms-settings:`](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings): suporte varia por versão/edição.

As fontes descrevem mecanismos e efeitos possíveis. O grau de benefício para um equipamento precisa ser medido; nenhum link deste catálogo valida automaticamente o conjunto do produto.
