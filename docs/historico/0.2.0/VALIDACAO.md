# Validação do MVP — revisão 0.2.0

Plano revisado em 08/11/2026 para a versão 0.2.0. Separar teste planejado de teste realmente executado. Um item sem evidência continua pendente. Esta documentação não autoriza aplicar otimizações no PC de desenvolvimento.

## Evidência e estado atual

Esta revisão inclui vermelho/preto em `Palette.cs` e `App.xaml`, sidebar overlay de 264/72 DIPs, logo fixa com órbitas animadas, pulsos nos circuitos, diagnóstico inicial somente leitura e painel compacto de energia com planos reais. A animação ambiente atende ao pedido explícito do usuário, substituindo a escolha anterior de manter as cenas estáticas.

A rodada 0.2.0 foi executada em 08/11/2026: build Release com 0 avisos/erros, 33/33 testes isolados e smokes estático e temporal do EXE autocontido final com código de saída 0. Os números abaixo pertencem à execução atual. Resultados, limites e distribuição estão em [RESULTADOS.md](RESULTADOS.md).

| Verificação | Estado nesta redação | Evidência |
|---|---|---|
| Revisão de documentação .NET/WPF e APIs Windows | Registrada em 08/11/2026 | Links em [ARQUITETURA.md](ARQUITETURA.md) e [CATALOGO-E-EVIDENCIAS.md](CATALOGO-E-EVIDENCIAS.md) |
| Inspeção dos repositórios de referência sem execução | Registrada em 08/11/2026 | Escopo/licenças registrados no catálogo |
| Compilação e testes automatizados 0.2.0 | Executados: 0 avisos/erros, 33/33 testes isolados aprovados | `scripts/build.ps1`, `artifacts/test-results.json` e [RESULTADOS.md](RESULTADOS.md) |
| Diagnóstico somente leitura em Windows 10 Pro x64, build 19045 | Executado sem administrador; smokes finais estático e temporal aprovados | `artifacts/private/final-smoke-02/smoke-result.json` e `artifacts/private/final-motion-smoke/motion-smoke-result.json`; não homologa alterações |
| Publicação autocontida e conteúdo do instalador 0.2.0 | EXE final executado; CAB extraído com cinco arquivos; três hashes do manifesto e igualdade do EXE conferidos; `asInvoker` confirmado | [RESULTADOS.md](RESULTADOS.md) e manifesto da distribuição atual; instalador não executado |
| Diagnóstico somente leitura em Windows 11 x64 | A registrar | Inserir build/edição, CPU, RAM, GPU e duração real |
| Aplicação/restauração de startup e energia em VM | A executar em ambiente autorizado | Snapshot da VM, dados anteriores e posteriores |
| Render estático e composição 0.2.0 | Executado: sete páginas; 1366×768, 1536×884 e 683×384 DIPs; energia antes/depois e drawer aberto/recolhido | `artifacts/private/final-smoke-02/` e `artifacts/private/final-motion-smoke/` |
| Movimento temporal 0.2.0 | Executado: 30 quadros; 29/29 pares com movimento nas órbitas e circuito isolado; pausa/reduzido/economia exatamente estáticos; minimizar/retomar e sidebar aprovados | `artifacts/private/final-motion-smoke/motion-smoke-result.json`, `motion-verification.json` e [motion-summary.json](../artifacts/motion-summary.json) |
| Matriz de DPI real, teclado e acessibilidade | Validação completa a executar | Capturas, passos e resultado; render em DIPs não substitui DPI real |
| Consumo básico CPU/RAM em repouso | Amostras de cerca de 3 s: visível animado 1,009% CPU normalizada; minimizado 0% na amostra | Working set/privada e limites em [RESULTADOS.md](RESULTADOS.md); não mede GPU ou orçamento permanente |
| Profiling de GPU, transições e 60 FPS | A executar | Hardware, ferramenta, cenário e captura; não presumir meta atingida |
| Assinatura de código e atualização verificada | Não implantadas | Requisitos em [DISTRIBUICAO.md](DISTRIBUICAO.md) |

### Registro de integração

Os resultados da execução estão em [RESULTADOS.md](RESULTADOS.md). Para repetir ou ampliar a validação, usar os campos abaixo sem substituir pendências da matriz por um resultado genérico:

```text
Data/hora e fuso:
Versão do aplicativo / identificação do código:
Windows (edição, build, arquitetura):
SDK .NET e runtime usados:
Hardware de referência:
Comando de compilação:
Resultado / avisos:
Comando de testes:
Quantidade e resultado:
Pacote gerado e SHA-256:
Diagnóstico somente leitura (duração/amostras):
Capturas e logs associados:
Smoke estático (comando/resultado):
Smoke temporal (comando, quadros, pixels e pausas):
Pendências conhecidas:
```

## Camadas de teste

As regras do motor, os conflitos e os estados do diário devem ser exercitados com adaptadores falsos, sem tocar no Registro real ou no plano da máquina. Os testes de integração que alteram Windows pertencem a uma VM descartável, com snapshot e autorização de operação.

O diagnóstico somente leitura pode ser executado no ambiente de desenvolvimento. Obter métricas reais não autoriza desativar entradas, trocar energia, excluir arquivos, instalar drivers ou executar utilitários de terceiros.

### Motor e persistência

| Caso | Resultado esperado |
|---|---|
| Prévia de qualquer operação | Nenhum estado Windows alterado; efeito e risco legíveis |
| Alvo alterado entre análise/prévia/aplicação | Rejeitar prévia antiga e pedir nova revisão; não aplicar cegamente |
| Backup não consegue ser persistido | Operação de escrita não começa |
| Gravação interrompida | Diário anterior legível; arquivo temporário não vira histórico válido por acidente |
| App fecha após backup e antes da alteração | Registro pendente conciliável sem afirmar sucesso |
| App fecha após alteração e antes de gravar conclusão | Verificar estado real no retorno e distinguir não verificado de concluído |
| Primeira ação conclui e a segunda falha | Cada resultado persistido; sucesso parcial claro; reversão apenas dos itens apropriados |
| Cancelamento antes da primeira ação | Nenhuma alteração |
| Cancelamento entre ações | Preservar resultados já verificados e não iniciar próxima ação |
| Cancelamento durante escrita curta | Concluir e verificar unidade em andamento antes de parar |
| Estado já corresponde ao desejado | Ignorado/já configurado, sem multiplicar efeito |
| Falha de leitura posterior | Resultado não verificado; nunca sucesso apenas pelo retorno da escrita |
| Duas instâncias | Escritas e histórico serializados ou segunda instância bloqueada; não perder registros |
| Histórico inválido/corrompido | Aviso e preservação do arquivo para diagnóstico; não executar backups não interpretáveis |

### Inicialização em VM

Usar uma chave/entrada de fixture claramente identificada e inofensiva. O teste não deve executar o comando registrado. Criar, remover e restaurar apenas a fixture; nunca selecionar entradas reais do usuário para testar o motor.

| Caso | Resultado esperado |
|---|---|
| `REG_SZ` em HKCU Run | Backup exato, remoção confirmada e restauração igual |
| `REG_EXPAND_SZ` com `%VARIAVEL%` | Preservar tipo e texto sem expandir variáveis no backup |
| Nome/caminho com espaços e acentos | Mesma identidade preservada; nenhum parsing como comando |
| Registro inexistente ou já removido | Ignorado ou conflito explicado; sem exclusão de outro valor |
| Nome existe, mas conteúdo mudou após prévia | Não remover o novo valor |
| Item classificado como protegido/cauteloso | Não entrar no plano automático; bloqueio/cautela claramente exposto |
| Entrada desconhecida | Sem recomendação automática de desativar |
| Origem HKLM, Startup ou RunOnce | Somente leitura/fora do gerenciador de escrita do MVP |
| Restauração com nome ausente | Recriar o valor original e verificar |
| Restauração com valor original já presente | Informar já restaurado sem sobrescrita |
| Restauração com nome presente e valor diferente | Conflito; preservar mudança posterior |
| Novo logon/reinicialização | Persistência da configuração e histórico confirmadas; não alegar velocidade sem cronometragem controlada |

### Energia em VM e notebook de referência

| Caso | Resultado esperado |
|---|---|
| Enumerar sem alterar | Lista de planos existentes e ativo corretos |
| Plano já ativo | Já configurado/ignorado |
| Troca explicitamente aprovada | GUID anterior persistido; GUID final lido igual ao selecionado |
| Plano removido após prévia | Falha/confirmação de indisponibilidade; não importar/criar plano |
| Bloqueio por política/permissão | Informar motivo, sem contornar ou elevar genericamente |
| Mudança por outro app antes de aplicar | Prévia invalidada |
| Mudança por outro app antes de desfazer | Conflito; não sobrescrever |
| Plano anterior já removido | Restauração incompleta explícita; não fabricar o plano |
| Notebook na bateria | Estado de alimentação e riscos de autonomia corretos |
| Notebook na tomada | Estado de alimentação atualizado; sem recomendação universal de desempenho máximo |
| Fonte de energia indisponível | Desconhecido, sem assumir desktop ou tomada |
| Apenas um plano disponível | Situação válida; não classificar como problema |

O MVP não tem broker elevado, portanto recusa de UAC não é um fluxo implementado de otimização. Esse caso será obrigatório quando existir uma operação elevada. Agora, validar que a UI funciona em usuário comum e que erros de acesso não iniciam um fluxo oculto de elevação.

## Diagnóstico, UI e consumo

| Área | Critério de aceitação |
|---|---|
| Coleta | Primeira análise somente leitura na abertura normal; cerca de cinco segundos de observação, duração real registrada, UI responsiva e cancelamento disponível |
| Dados | CPU/RAM/discos correspondem ao período/contexto; campos com erro permanecem indisponíveis |
| Falhas de inventário | Uma consulta com acesso negado ou recurso ausente não derruba toda análise |
| Processos que encerram durante coleta | Ignorados com segurança, sem reutilizar dados de outro PID |
| Comprometimento | Unidades de páginas convertidas corretamente; sem confundir com RAM física |
| Sensores/GPU | Uso e temperaturas ausentes não são desenhados como zero |
| Reinicialização | Sinal conhecido apresentado com sua limitação; sem prometer cobertura universal |
| Armazenamento | Capacidade/livre reais; link do Windows abre página conhecida; nenhum arquivo é apagado |
| Teclado | Tab/Shift+Tab percorrem controles; foco visível; Enter/Espaço acionam o correto; seleção não muda sem intenção |
| Leitor de tela | Nome, função, estado e rótulos acessíveis; ausência de texto somente decorativo como único rótulo |
| 1366×768 e Full HD | Conteúdo utilizável e rolável; botões e diálogos não ficam inacessíveis |
| DPI 125%, 150%, 200% | Sem recorte de ação crítica ou texto; conferir limite útil do monitor e mudança entre monitores |
| Movimento reduzido e economia | UI utilizável estática; órbitas, pulsos e relógios opcionais cessam, sem salvar preferências temporárias do smoke |
| Minimizado e restaurado | Relógios decorativos param; retorno retoma as cenas se permitido, sem busy loop ou recriação de geometria por frame |
| Drawer | Overlay real de 264 DIPs, rail de 72 DIPs, rótulos inteiros, conteúdo sem deslocamento; verificar abertura/recolhimento por ponteiro e foco por teclado |
| Energia | Planos reais e ativo identificado após análise; antes dela, um painel compacto com CTA; revisão obrigatória antes de alterar |
| Tema | Vermelho/preto coerente em CTAs, foco, seleção, bordas, ícones e fundos; sem roxo ou verde-limão remanescentes |
| Offline | Análise e operações locais funcionam sem conta ou rede |

### Render estático e sequência temporal

`--smoke-test --screenshot-dir <pasta>` produz capturas estáveis para verificar composição, páginas, busca, dados, recortes e adaptação de layout. Esse modo reduz os movimentos para evitar variação das imagens. Uma captura estática não é evidência de que órbitas ou circuitos se animam.

`--motion-smoke --screenshot-dir <pasta>` mantém as cenas do aplicativo em execução e amostra 30 quadros, nominalmente a cada 100 ms, com timestamps reais. O protocolo inclui:

1. Capturar Energia antes da análise, executar diagnóstico somente leitura e capturar a lista real depois.
2. Capturar a visão geral e o circuito isolado; comparar pixels das órbitas e dos pulsos entre o primeiro e o último quadro.
3. Pausar explicitamente as cenas e comparar duas capturas separadas no tempo; repetir com movimento reduzido e economia.
4. Minimizar, verificar os relógios suspensos, restaurar e verificar a retomada.
5. Capturar o drawer aberto, recolhido e em janela compacta; conferir alocação real, largura do recorte e limites dos rótulos.
6. Registrar o resultado estruturado em `motion-smoke-result.json`, timestamps, amostras de consumo e capturas associadas.

O modo temporal bloqueia aplicação/restauração, não abre recursos externos e não persiste as preferências temporárias do teste. A preferência global do Windows continua respeitada: se animações estiverem desativadas pelo sistema, registrar essa condição em vez de alterar o Windows para fazer o teste passar.

Movimento de pixels prova mudança visual na sequência capturada; capturas iguais e relógios suspensos sustentam a verificação de pausa. Trinta quadros amostrados a cerca de 10 Hz não medem 60 FPS, latência de entrada, fluidez contínua ou consumo de GPU. Na rodada final, o comparador aprovou os critérios, com mudança acima do limiar nos 29 pares orbitais e nos 29 pares do circuito isolado. A região `circuit_shell` selecionada da janela permaneceu estática/coberta pela composição; ela não é evidência de movimento naquela região. Pausa, movimento reduzido e economia tiveram igualdade exata da imagem inteira. O resultado estruturado e os limites estão em [RESULTADOS.md](RESULTADOS.md).

### Protocolo de profiling a executar

Registrar equipamento, Windows, driver, resolução, DPI, build Release, alimentação e aplicativos relevantes abertos. Medir repouso com painel visível, navegação/animações, diagnóstico, lista extensa e estado minimizado. Usar profiler apropriado para CPU, memória e renderização; gravar ferramenta/versão, duração, amostras e picos.

A meta visual é 60 FPS no hardware de referência durante transições, com limitações documentadas. Não declarar essa meta alcançada a partir da inspeção visual, da duração configurada da animação ou do FPS do monitor. Nenhum orçamento de RAM foi medido nesta redação. Registrar working set, memória privada, CPU e GPU observadas em vez de inventar números.

## Comparação de resultados

Para startup, o resultado imediatamente verificável é a alteração do valor e sua persistência; tempo de inicialização exige reinício, protocolo consistente e repetições. Para energia, verificar o GUID não prova ganho de desempenho ou autonomia. Para armazenamento, abrir Settings não libera espaço pelo aplicativo.

Qualquer comparação de uso de CPU/RAM deve registrar carga equivalente, duração e contexto. Uma diferença entre duas janelas curtas pode ser ruído ou atividade de outro processo. Não atribuir causalidade automaticamente. O MVP não oferece benchmark de jogos; alegações de FPS/frametime exigem instrumentação e um protocolo futuro independente.

## Bloqueios de liberação comercial

Não rotular esta entrega como comercialmente validada enquanto faltarem integração em VMs, hardware real, acessibilidade, DPI, consumo, assinatura, comportamento do instalador, recuperação de falhas e procedimento de atualização. Registrar e tratar defeitos observados antes de expandir o catálogo de alterações.
