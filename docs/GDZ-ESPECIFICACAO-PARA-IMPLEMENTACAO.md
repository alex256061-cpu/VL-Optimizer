# GDZ: especificação extraída para implementar funcionalidades próprias

Esta entrega aprofunda a versão instalada **1.1.27**, em **08/11/2026**, para orientar o backend de um aplicativo com interface já existente. Não foi construído nem alterado o aplicativo VL Optimizer. Nenhuma otimização, limpeza ou reparo do GDZ foi acionado durante a inspeção.

O material agora descreve operações concretas: entradas, valores, comandos, consultas, arquivos, persistência, reversão e critérios para conferir resultados. Também distingue ações atuais, aliases, compatibilidade antiga e componentes externos. Não basta copiar o texto do botão para obter comportamento equivalente.

## Os quatro aprofundamentos desta etapa

| Entrega | Conteúdo recuperado |
|---|---|
| [Operações implementáveis](GDZ-OPERACOES-IMPLEMENTAVEIS.md) | 69 nomes de aplicação (36 ações base + 33 tweaks), 2 padrões dinâmicos, 16 categorias de limpeza, 12 jogos e 2 presets FiveM, com Registro/serviços/DRS, detecção, snapshot e reversão. Inclui 13 ações fora de PAGES e 8 nomes apenas de reversão antiga. |
| [Algoritmos e contratos](GDZ-ALGORITMOS-E-CONTRATOS.md) | 34 contratos e 12 formatos persistidos: regras dos coletores e scripts, unidades, stdout/estado, limites e diferenças entre prévia e aplicação. Há ainda 32 constantes resolvidas e 54 payloads embutidos identificados nas evidências privadas. |
| [Janela WPF real](GDZ-SCALING-CONTRATO-WPF.md) | 7 arquivos XAML, 72 elementos interativos/com eventos e 81 vínculos: opções reais de scaling, intervalos numéricos, seleção de perfis, atraso e persistência. |
| [Motor nativo](GDZ-MOTOR-NATIVO-APROFUNDADO.md) | Desmontagem dos 11 exports, mudanças de preferência gráfica e driver, transferência de parâmetros e 300 shaders inventariados/desmontados (202 DXBC + 98 SPIR-V). |

O [mapa anterior](GDZ-MAPA-COMPLETO.md) continua como índice dos **177 recursos/controles**, **229 funções semânticas JavaScript**, **87 chamadas IPC permitidas**, **9 mensagens**, **4 eventos** e **533 métodos CLR**. Essas medidas se sobrepõem e não devem ser somadas. As novas extrações aprofundam o comportamento; não transformam cada instrução de máquina em uma nova função do produto.

## Até onde já é possível especificar o backend

“Especificável” significa que o mecanismo e seus dados foram recuperados o suficiente para desenhar uma implementação independente. Não significa que executar os comandos foi validado em todas as versões do Windows ou que um ganho de desempenho foi demonstrado.

| Bloco | O que já está especificado | O que precisa ser confirmado na implementação futura |
|---|---|---|
| Ajustes de Windows | Valores/tipos de Registro, serviços, comandos adicionais, regras de hardware, detecção e opções de reversão do catálogo | Matriz de builds/edições, privilégio necessário, efeitos reais e tratamento de falha parcial |
| Energia e desempenho | Planos e transições, snapshots, restrições para certos processadores e ações compostas | Disponibilidade dos planos, restauração após falha e interação com configuração externa |
| Mouse e teclado | Valores de preferência e acessibilidade, regras de enumeração e estimativa de polling no renderer | Reinício/logoff quando necessário e distinção entre frequência de eventos e polling USB real |
| RAM/standby | Consulta antes/depois, chamada nativa e privilégio usados na purga, exclusão mútua de execução | Retornos NTSTATUS, disponibilidade de privilégio e experiência de uso; MB liberados não equivalem a FPS |
| Hardware e sensores | Fontes WMI/contadores/APIs, campos, unidades, conversões, caches e heurísticas | Ausência de sensores, GPUs múltiplas, nomes de contadores e sensores que não representam a temperatura do die |
| Gargalo | Seleção de processo, janela de medição, agregação de CPU/GPU/RAM e regras de classificação | Carga controlada para conferir a heurística; amostras não estabelecem causalidade |
| FPS e comparação | Linha CSV do PresentMon, escolha de PID, filtros, janela, média e lows, baseline e sessões | Compatibilidade do capturador e processos, perdas de amostras e separação de tempo de relógio/tempo de frames |
| Limpeza e uso de disco | Categorias/caminhos/idade, travessia, links, arquivos cloud, resultados e limites de varredura | Mudanças entre prévia e exclusão, bloqueios, permissões e restauração impossível de arquivos já apagados |
| Programas e inicialização | Inventário Uninstall, chamada do desinstalador, tarefas/Run e heurísticas de sobras | Desinstaladores específicos e falso positivo de pastas; não reproduzir heurística como prova de sobra |
| Jogos e modo automático | Descoberta de bibliotecas, executáveis conhecidos, prioridades, fechamento/reabertura e plano de energia | Steam/Epic/Riot/FiveM reais e restauração do estado anterior em vez de pressupor Normal |
| Presets FiveM | Tags alteradas por preset, diferenças entre FPS/balanced, descoberta e backup | Configurações com tags ausentes, múltiplas instalações e backup vinculado ao caminho original |
| Histórico e reversão | Identidade tipada dos itens, sequências, transferência de snapshot e restauração do estado mais antigo | Escrita transacional, alterações externas concorrentes, falha parcial e ações sem inversão completa |
| Integração OptiScaler | Descoberta do executável, aquisição do pacote, cópia/backup e parâmetros de INI observados | Contrato do pacote externo, versão, integridade e compatibilidade por jogo; o GDZ não implementa o upscaler no JavaScript |
| Scaling WPF | Perfil, enums, limites de UI, atalho, automação por janela, persistência e contrato de 33 argumentos | O motor gráfico é um trabalho separado; opções recuperadas não implementam sozinhas captura/interpolação/HDR |
| Motor gráfico nativo | Exports, parte do fluxo interno, escrita de preferências e recursos/shaders identificados | Reconstrução do pipeline completo, dependências de shader, sincronização, buffers, qualidade e correção visual |
| Conta, licença e atualização | Contratos e verificações observáveis no cliente, estados offline e assinatura de release | Código do servidor não está no pacote local. Um produto próprio precisa de seus próprios serviços/chaves, se oferecer essas funções |

## Comportamentos que não convém copiar silenciosamente

São diferenças observadas no código, não resultados de testes destrutivos:

- A estimativa de disco ignora certos atributos de arquivos cloud; o caminho de exclusão não repete todas essas verificações. A prévia não é garantia do conjunto que será tentado na limpeza.
- A limpeza de temporários por item de primeiro nível pode aceitar a idade da pasta e depois percorrer filhos com corte diferente. Não interpretar a frase “mais de um dia” como filtro individual garantido de todo arquivo.
- O modo automático pode restaurar prioridade para **Normal**, sem recuperar a prioridade original do processo. A implementação futura deve decidir e declarar se preservará esse comportamento ou corrigirá a restauração.
- O preset FiveM balanced não altera as mesmas tags do preset FPS. Tags ausentes podem continuar ausentes; o backup observado não resolve automaticamente múltiplos caminhos de instalação.
- O `fps-record` coleta pelo PID até o prazo. Uma mensagem pedindo foco não equivale a um teste efetivo de janela em primeiro plano nesse fluxo. A duração salva pode derivar da soma dos frametimes.
- A janela WPF e a ponte JavaScript legada de Scaling não aceitam os mesmos conjuntos e intervalos. A UI real tem opções que o HTML empacotado não encaminha.
- Nomes presentes apenas em reversão antiga não comprovam um recurso atual de aplicação. Aliases e combinações não são necessariamente otimizações independentes.

## Contrato mínimo de cada operação futura

Para ligar os recursos à interface já existente, cada operação precisa destes campos. Isto é especificação; nenhum serviço de aplicação foi criado nesta etapa.

| Campo | Informação necessária |
|---|---|
| Identidade | ID estável e aliases apenas quando houver equivalência real |
| Disponibilidade | Build/edição do Windows, fornecedor, hardware, privilégio e dependência externa |
| Entrada | Tipo, unidade, enum, faixa e validação no backend |
| Consulta | Fonte do estado, tratamento de ausente e distinção entre falso e erro de consulta |
| Snapshot | Valor anterior tipado, existência anterior, caminho/escopo e sequência |
| Aplicação | Passos ordenados, timeout, código de retorno, cancelamento e falha parcial |
| Pós-condição | Nova consulta que comprova configuração aplicada; mensagens de sucesso não bastam |
| Reversão | Restituir valor anterior, apagar valor criado, tratar sobreposição e identificar efeitos irreversíveis |
| Evidência | Arquivo/linha/hash da extração usada e condição que permanece inferida |
| Verificação futura | Casos de borda e teste controlado em Windows apropriado |

Para operações compostas, consultar cada etapa separadamente. Um único retorno “success” pode ocultar serviço não alterado, comando extra malsucedido ou arquivo que não pôde ser removido. A interface deve representar sucesso parcial quando o backend conseguir distingui-lo.

## Evidências estruturadas locais

Os relatórios são a leitura humana. Estes inventários privados permitem consultar os dados sem procurar manualmente em milhares de linhas:

| Arquivo em `artifacts/private/gdz-analysis/deep` | Finalidade |
|---|---|
| `operations-declarative.json` | Dados declarativos recuperados dos catálogos, incluindo operações e valores exatos |
| `operation-specs.json` | 69 operações normalizadas, comandos e exemplos explicitamente sintéticos, 2 padrões e compatibilidade legada |
| `embedded-contracts.json` | Scripts e contratos internos recuperados para auditoria estática |
| `frontend-features.json` | Os 177 recursos/controles, capacidades, riscos e vínculos de backend |
| `backend-functions-readable.json` | As 229 funções com nomes semânticos e suas evidências de leitura |
| `ipc-explained.json` | Contratos IPC, payloads, efeitos, retornos e registros ausentes |
| `native-functions.json` | 533 métodos gerenciados, enums, propriedades e assinaturas |
| `wpf-controls.json` | Todos os controles e handlers extraídos do XAML/C# da janela real |
| `native-deep/` | Evidências de desmontagem e inventários do motor nativo |
| `deep-verification.json` | Conferência independente de hashes, cobertura e intervalos dos novos inventários; não é teste de efeito no Windows |

Código decompilado, bytecode gráfico e binários do alvo ficam nas evidências privadas. O pacote de entrega contém relatórios e ferramentas de análise próprios; não incorpora esses componentes ao VL Optimizer.

## Limite alcançado nesta etapa

A maior parte dos recursos de configuração do Windows, limpeza, inventário, métricas, perfis e automação possui agora um contrato concreto para implementação independente e verificação posterior. O que permanece aberto está concentrado no pipeline gráfico completo, no comportamento efetivo de dependências externas e no servidor remoto. A extração estática não recupera o código do servidor e não substitui testes controlados de efeitos no Windows.

Não é possível afirmar equivalência integral com o GDZ apenas pela presença de todos os botões. O próximo trabalho de produto pode partir destes contratos, mantendo a interface atual, e tratar os blocos ainda parciais como dependências ou trabalhos separados.
