# GDZ Optimizer — recuperação estática de strings e expressões

O backend original tem 2.376.174 caracteres e combina uma tabela de strings cifradas, RC4, base64 com alfabeto próprio, rotação inicial e camadas de funções intermediárias. Foi criada uma ferramenta original para recuperar esses dados por análise sintática, sem carregar nem executar JavaScript do programa.

## Resultado recuperado

| Item | Resultado |
|---|---:|
| Strings cifradas no array original | 12.955 |
| Deslocamento do índice | 284 |
| Rotação recuperada | 379 posições |
| Checksum calculado / esperado | 373.766 / 373.766 |
| Chamadas puras substituídas por valores | 13.995 |
| Constantes aritméticas/unárias resolvidas | 22.335 |
| Acessos a constantes de dicionários resolvidos | 25.473 |
| Total de substituições registradas | 61.803 |
| Call sites de decodificação pendentes fora das definições dos wrappers | 0 |
| Helpers de operadores expandidos na versão focada | 1.399 |
| Definições de wrappers omitidas na versão focada | 1.573 |
| Dicionários sem referências omitidos | 2.147 |
| Ramos com condição comprovadamente constante simplificados | 198 |
| Declarações nomeadas de função de negócio preservadas | 159 |
| Expressões de função atribuídas a variáveis nomeadas preservadas | 70 |
| Casos de regressão do interpretador | 13 aprovados |

Esses números descrevem a recuperação de dados e a simplificação de expressões. Não comprovam equivalência integral de execução, ausência de erros no produto nem ganho de desempenho das otimizações.

## Como a ferramenta funciona

1. Babel interpreta o arquivo como uma árvore sintática. O código do alvo não é enviado a `eval`, `vm`, `require`, `import`, Electron ou PowerShell.
2. A tabela cifrada é extraída somente de literais string. O deslocamento do decoder é calculado a partir da expressão aritmética original.
3. Uma implementação própria do alfabeto base64 e do RC4 decifra entradas. Uma busca limitada ao tamanho da tabela recupera a rotação que satisfaz o checksum original.
4. Um interpretador restrito aceita literais, operações aritméticas/lógicas, templates, dicionários de offsets e funções auxiliares comprovadamente puras. Chamadas externas, instruções de sistema e expressões sem prova suficiente ficam em aberto.
5. Acesso a uma variável `const` que contém objeto não é tratado como constante automaticamente. Somente dicionários ofuscadores de formato limitado, sem escrita de propriedades, atualização, exclusão, referência que escape ou dependência de `this`, podem ser resolvidos. Estado como `_standby`, `fps`, `opti`, `pcLive`, `sensors` e `state` continua dinâmico.
6. Os argumentos, inclusive os não usados, e os inicializadores locais são verificados para impedir que efeitos sejam descartados. O inlining de operadores exige cada parâmetro uma única vez e na ordem original de avaliação; helpers com curto-circuito não são expandidos por essa etapa.
7. Os dois arquivos gerados são novamente analisados pelo parser. O catálogo fornece também offsets originais, linhas na versão focada e os corpos das 229 funções nomeadas.

## Arquivos de evidência

Todos os artefatos abaixo ficam em `artifacts/private/gdz-analysis/deep/`, já ignorado pelo Git:

- `backend-readable.js`: versão formatada com strings e constantes recuperadas; conserva a estrutura, incluindo o bootstrap, array e decoder originais.
- `backend-focused.js`: versão para leitura, com branches constantes simplificados, operadores expandidos e helpers/tabelas sem uso omitidos.
- `backend-replacements.json`: offset inicial/final original, classe da transformação e valor recuperado de cada substituição.
- `backend-static-metadata.json`: origem, SHA-256, parâmetros do decoder, contagens, limites e resultados das regressões.
- `backend-function-index.json`: inventário das funções no backend original, com parâmetros, offsets e classificação de wrappers.
- `backend-functions-readable.json`: 229 funções de negócio nomeadas, incluindo código legível, linhas, parâmetros, chamadas diretas e ligação com o original.
- `bound-functions-explained.json`: interpretação individual das 70 expressões nomeadas; seu catálogo legível está em `docs/GDZ-HELPERS-EXPRESSOES.md`.

SHA-256 do original analisado: `8f57b00ffaa35c1f5b88bc815d6c2d57838ab989b571883568733376376f2095`.

SHA-256 final de `backend-focused.js`: `52ca9c4279b739ec674637dae36a361b62eaf06b94aaa6db2a9caba6d4baaef0` (722.411 bytes; 9.102 linhas considerando a linha final vazia).

## Reprodução

Dependências locais: Node.js e Babel 8.1.0 (`@babel/parser`, `@babel/traverse`, `@babel/generator`, `@babel/types`) em `.tools/gdz-static/`, com versões exatas registradas no `package-lock.json` local. Os arquivos da instalação original permanecem intactos.

```powershell
npm install --prefix .tools/gdz-static --no-audit --no-fund @babel/parser@8.1.0 @babel/traverse@8.1.0 @babel/generator@8.1.0 @babel/types@8.1.0
node scripts/decode-gdz-static.cjs
python scripts/build-gdz-bound-catalog.py
```

Os 13 casos de regressão cobrem: estado de objeto `const`, escrita em objeto ofuscado, escape de objeto, mutação de array, leitura de offset imutável, operador puro, inicializador não usado com efeito, efeito dentro de inicializador de objeto, argumento não usado com efeito, argumento extra com efeito, efeito em objeto passado como argumento, efeito durante teste de truthiness de objeto e resultado de `typeof` para funções.

Nomes de variáveis, regras dinâmicas, chamadas de sistema e parte do fluxo de controle permanecem como foram encontrados. A entrega é um artefato de análise para conferir o comportamento observado no código; não substitui o executável nem altera licença, autenticação ou configurações do Windows.
