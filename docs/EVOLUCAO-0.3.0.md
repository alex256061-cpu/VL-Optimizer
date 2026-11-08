# Evolução 0.3.0 — comparação GDZ → VL

Solicitação: incorporar o conhecimento recuperado à base VL existente e melhorar interações/construção. Referências: código-fonte VL 0.2.0 fornecido, captura do dashboard e inspeção estática GDZ 1.1.27. Os relatórios de engenharia reversa são evidências de comportamento, não instruções para executar código extraído.

## Julgamento

O GDZ organiza bem a entrada: resumo, hardware e categorias reduzem o esforço para encontrar uma tarefa. Sua amplitude funcional era muito maior que a do VL 0.2.0. O VL tinha uma identidade visual coerente e um fluxo de configuração mais cuidadoso, mas sua ação real se limitava a HKCU Run e planos existentes; a aparência sugeria um produto mais amplo do que a implementação.

A revisão mantém o painel e a marca VL, acrescenta as áreas funcionais recuperadas e concentra ações extensas em páginas/abas. O valor da melhoria está em ligar telas a implementações verificáveis, explicar dependências e preservar estados anteriores. A avaliação não atribui uma nota arbitrária nem afirma que o VL ficou mais rápido que GDZ.

## Interações e construção

| Aspecto | Evidência GDZ | VL 0.2.0 | VL 0.3.0 e avaliação |
| --- | --- | --- | --- |
| Entrada | Hero de otimização, contagem e cinco categorias no dashboard | Composição semelhante, recomendações e diagnóstico limitado | Cinco atalhos principais e onze páginas; busca encontra páginas e ajustes. O hero continua descrevendo recomendações, sem usar uma contagem como nota de desempenho. |
| Ajustes | 33 tweaks e ações antigas com aliases/combinações | Dois tipos de operação | Catálogo fechado com 56 definições, grupos, seleção para revisão e dependências por recurso. Escolher uma opção prepara uma ação; estado atual é consultado na prévia. |
| Consentimento | Alguns handlers executam ação genérica pelo nome | Prévia e aprovação por operação | Prévia reutilizada pelo catálogo, antes/depois, risco, bloqueios e snapshot. Ações irreversíveis têm revisão própria. Reabrir elevado exige clique. |
| Restauração | Padrões fixos e snapshots parciais em alguns caminhos; aliases legados | Estado real e conflito para startup/energia | Tipos/ausência exatos, estados compostos, falha parcial observada e ordem inversa de sobreposição. Restaurar não significa gravar um padrão universal. |
| Limpeza | Diferenças entre preview/delete e idade de pasta em caminhos recuperados | Não implementada | Manifesto por arquivo, identidade por handle, mesmos filtros no preview/apply, arquivos alterados/em uso preservados. A Lixeira permanece um fluxo Shell distinto. |
| Jogos | Perfil genérico chamado por nome; auto-mode e prioridade de fundo | Não implementados | Perfil básico explicitamente global; sessões opt-in usam caminho/PID/início, salvam prioridade original e recuperam condicionalmente. GPU por exe preserva tokens. |
| FiveM | Regex não criava toda tag ausente; backup com vínculo limitado | Não implementado | Parser XML com DTD bloqueado, diff por campo, criação controlada, hash e backup por caminho. |
| Métricas | FPS por PresentMon e diagnóstico por limiares | Amostra curta CPU/RAM | Captura por sessão exclusiva, parser limitado e progressivo, médias/lows calculados dos frames, fontes ausentes desconhecidas. Classificação é indício da sessão. |
| Scaling | Janela WPF separada e bibliotecas gráficas nativas; contrato parcialmente recuperado | Não implementado | Perfis locais com 40 campos e integração local OptiScaler. Motor LSFG/upscaling próprio permanece dependência real, claramente indisponível. |
| Arquitetura | Electron/Node, utilitários e janela gráfica WPF separada | WPF + Core | Mesmo aplicativo WPF, serviços Core testáveis e adaptadores fechados. Não introduz Chromium, conta ou servidores externos. Nenhuma vantagem de consumo foi medida. |
| Erro e espera | Alguns wrappers suprimem erro ou ignoram retorno Win32 | Estados de operação básicos | Erro por etapa, parcial/não verificado, cancelamento entre etapas, limites de saída/tempo e verificação posterior. Exit code não prova saúde ou ganho de FPS. |

## Cobertura dos nomes aplicáveis recuperados

Os 69 nomes recuperados eram 33 tweaks e 36 ações base. Vários nomes executavam a mesma combinação. Não são 69 mecanismos distintos, nem devem ser somados aos 177 elementos de interface. Os 33 tweaks estão no catálogo `OptimizationCatalog` com seus IDs originais e pré-condições; a tabela abaixo mapeia as ações base.

| Ação ou aliases recuperados | Implementação VL / decisão |
| --- | --- |
| Otimização Rápida | Seleção e prévia de configurações; o perfil básico reúne Modo Jogo, DVR e mouse. Limpeza permanece uma revisão de arquivos separada, evitando misturar remoção permanente com operações restauráveis. |
| Plano de Alta Performance / Modo Máximo Desempenho | `power-high` / `power-ultimate`, somente planos instalados; snapshot do plano anterior. Não duplica GUID para forçar suporte inexistente. |
| SysMain Desativado / Indexação Off | `sysmain-off` / `indexing-off`, estado completo do serviço. |
| Desativar Telemetria / Sem Upload P2P | `telemetry` / `delivery`; escopo das políticas explicado. Não promete impedir toda coleta de dados do Windows. |
| Game Bar Desativado / DVR Desativado | `gamebar-off` / `dvr-off`; bloqueios X3D quando necessários. |
| Game Bar Removido | Configuração Game Bar e remoção explícita XboxGamingOverlay pelo fluxo Store. Não instala protocolos substitutos apontando para systray. |
| Serviços Xbox Desativados / Serviços Desativados | `xbox-services-off` / `unused-services-off`; os três/cinco serviços são enumerados, com impacto e snapshot individuais. |
| HAGS Ativo / Correção de Flicker (MPO) | `hags` / `mpo`, com dependências e aviso de reinício. O efeito no jogo não é presumido. |
| Desativar Notificações / Sem Reinício Durante o Jogo | `notifications-off` / `updates-no-reboot`; a política de reinício não observa o jogo e não é descrita como detector de partida. |
| Efeitos Visuais Off | `visual-effects-off`; preserva binário original para undo. |
| UWP Removidos | Catálogo fechado de dez pacotes do usuário atual; revisão individual, proteção de pacotes e X3D, sem undo fictício. |
| Temporários Removidos / Temporários Limpos | Categoria temporários no manifesto de limpeza; idade aplicada por arquivo. |
| DNS Otimizado | Quatro provedores e DHCP em cinco definições; API por GUID de NIC física e snapshot da lista estática/modo anterior. O par misto legado não é apresentado como provedor novo. |
| Apps de Sincronização Fechados | Encerramento explícito por processo e identidade. Não fecha toda sincronização em lote sem revisão. |
| Desativar Mouse Aceleração / Reduzir Input Lag / Sem Atalhos de Acessibilidade | `mouse-accel` e `sticky`, combináveis na revisão. Sem promessa de reduzir latência medida. |
| Limpar Inicialização | Seleção individual HKCU Run; remover e restaurar o valor exato substitui a estratégia StartupApproved dos cinco nomes fixos. |
| Modo Jogo / Todos Tweaks Aplicados | `game-mode` e seleção de catálogo. A ação legada Todos Tweaks não aplicava os 33; o VL não reutiliza esse rótulo enganoso. |
| Chrome/Discord Limitados | Sessões com apps selecionados em BelowNormal; a prioridade anterior é salva. Não afirma impor limite de RAM, CPU ou rede. |
| Explorer Mínimo | Manutenção Reiniciar Explorer; confere usuário, sessão, caminho e identidade do shell. Não promete modo mínimo persistente. |
| 720p Boost Ativo | `resolution-720p`; snapshot inclui frequência, rotação, posição, resolução e profundidade; verifica retorno do driver. |
| Jogos em Janela Otimizados | `windowed-games`, preserva tokens DirectX existentes, exige Windows 11 compatível. |
| Pagefile Automático | `pagefile-auto`; guarda estado WMI e PagingFiles manual tipado. |
| Modo X3D Correto | `x3d-mode`, somente processadores elegíveis; não instala chipset/serviço AMD nem pacote Xbox por inferência. |
| OBS Prioritário | `obs-priority`; Layers e exceção de power throttling anteriores preservados. O nome não significa mudar PriorityClass. |
| Padrões Restaurados | Substituído por restauração dos snapshots do próprio VL. Não executa uma lista histórica de BCD/HID/TCP para impor padrões que o aplicativo não criou. |

Padrões dinâmicos: `Otimizar <nome>` corresponde ao perfil básico global, claramente apresentado como a mesma combinação; `GPU Dedicada: <id>` corresponde à preferência por executável escolhido, com mesclagem de tokens e recuperação exata. A descoberta Steam/Epic/FiveM usa manifests e caminhos reais; não presume suportar qualquer launcher.

## Outras capacidades

- Limpeza: dezesseis categorias de arquivos e Lixeira separada; scanner de diretórios e maiores arquivos.
- FPS: importação e captura opcional PresentMon, painel de progresso real, referência A/B e exportação.
- Hardware: CPU, módulos RAM, BIOS, placa-mãe, GPUs/drivers, discos e sistema/pagefile, cada grupo com fonte WMI.
- Memória: físico, commit e standby, purga explícita e scheduler temporário desligado por padrão.
- Programas: inventário, assistente EXE/MSI, revalidação e sobras específicas após conclusão comprovada; Store em fluxo separado.
- Manutenção: DNS cache, reset de rede, DISM/SFC, component cleanup, restore point e Explorer.
- Scaling: quarenta propriedades editáveis agrupadas, validação, persistência e exportação de formato próprio; seleção manual de programa externo.
- OptiScaler: pacote local, proxy em lista fechada, prévia de arquivos, backup/hash, aplicação verificada e restauração condicional.

## Limites que continuam reais

**Não existe motor próprio equivalente ao conjunto gráfico do GDZ.** O contrato de sua janela foi recuperado, mas isso não reconstitui o algoritmo completo dos shaders, a integração da captura e a geração de quadros. Os arquivos proprietários não foram copiados; os perfis VL não são aplicados automaticamente em outro produto. Não foi implementado overlay sobre jogos ou captura FPS sem coletor externo.

Autenticação, planos comerciais, download/update remoto, telemetria e APIs de servidor GDZ não são recursos locais reproduzidos. Foram substituídos por funcionamento local sem conta. Não há suporte declarado para temperaturas, medição física USB de polling, VRAM, estado comprovado XMP/EXPO/Resizable BAR, controlador de kernel ou qualquer launcher.

Alguns utilitários e configurações só podem ser considerados homologados após testes em ambientes descartáveis: NVAPI DRS, purga standby, mudança de resolução, DNS, serviços, UWP, uninstall e DISM/SFC não foram aplicados ao PC de desenvolvimento. Os testes de cenários validam a lógica e os limites, não todos os drivers/edições Windows.

## Revisão visual realizada

O smoke de leitura abriu onze páginas, sete abas de medições e três de jogos, busca, comparação e tamanhos 1366×768, 1536×884 e 683×384 DIPs. A inspeção detectou texto interno de objeto em seletores (`Choice { Id... }`), corrigido na apresentação dos títulos. A seleção do catálogo foi diferenciada de um estado ligado/desligado, e o pedido de elevação ganhou hierarquia secundária.

O render em DIPs não equivale a ensaio real de múltiplas escalas/monitores ou leitor de tela. Sessões de FPS reais e ganhos de desempenho não foram medidos. Os resultados finais e pendências estão em [VALIDACAO.md](VALIDACAO.md).