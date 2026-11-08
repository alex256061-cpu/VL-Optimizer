# Testar o VL Optimizer

## Download e primeiro uso

1. Abra a [versão de teste 0.3.0](https://github.com/alex256061-cpu/VL-Optimizer/releases/tag/v0.3.0).
2. Em **Assets**, baixe **VL-Optimizer-0.3.0-portatil.zip**.
3. Extraia todos os arquivos para uma pasta local e abra **VLOptimizer.exe**. Não precisa instalar .NET ou ferramentas de programação.
4. Aguarde o diagnóstico. A primeira abertura apenas consulta o computador.

Requisitos: Windows 10/11 cliente x64, build 19041 ou superior. Cada recurso pode exigir versão do Windows, hardware ou permissão adicionais. Esta versão de teste não possui assinatura de código; o Windows pode identificar o publicador como desconhecido.

O arquivo **Instalar-VL-Optimizer-0.3.0.exe** é opcional: cria instalação e atalho para o usuário atual. Se houver uma instalação anterior do VL, desinstale-a antes de instalar esta versão. O portátil funciona sem esse instalador.

## Roteiro rápido

- Confira se CPU, GPU, RAM e discos correspondem ao computador.
- Navegue pelo catálogo e use **Consultar estado**. Selecione itens para ver a prévia; aplicar exige outra confirmação.
- Teste a busca, redimensione a janela e confira o modo de movimento reduzido em Configurações.
- Em Medições, consulte hardware/memória e verifique se fontes ausentes são indicadas como indisponíveis.
- Em Jogos, execute a busca de bibliotecas. Nenhum jogo é iniciado pela busca.

Para a primeira avaliação, basta usar as consultas e cancelar as prévias. Se decidir aplicar algo depois, revise o impacto mostrado na tela. Limpeza, purga de standby, remoção de programas/pacotes e algumas ferramentas de manutenção não possuem desfazer. Configurações restauráveis ficam no Histórico; FiveM, GPU por jogo e OptiScaler têm backups nas respectivas páginas. Desinstalar o VL não desfaz ajustes do Windows.

## Dependências opcionais e limites

PresentMon não acompanha o app. Sem ele, é possível importar um CSV já existente; uma captura nova exige escolher uma cópia local do coletor. OptiScaler também exige pacote local selecionado.

O editor Scaling guarda perfis; o motor gráfico próprio de upscaling/geração de quadros não está incluído. Não há promessa de ganho de FPS. Os 204 testes isolados e o smoke de leitura não substituem homologação de mudanças reais em Windows e drivers distintos.

## Enviar um problema

Abra [Relatar problema](https://github.com/alex256061-cpu/VL-Optimizer/issues/new/choose) e informe versão do VL, versão/build do Windows, tela, passos, resultado esperado e resultado observado. Uma captura ajuda quando o problema é visual.

Revise capturas e exportações antes de compartilhar: podem conter nomes, caminhos e informações do computador. Não envie senhas, chaves ou arquivos completos da pasta de dados. Histórico e backups locais ficam em `%LOCALAPPDATA%\VLOptimizer`; preserve-os enquanto precisar restaurar operações.

## Conferir integridade

Baixe **SHA256SUMS-0.3.0.txt** do mesmo Release e compare o hash do arquivo escolhido:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VL-Optimizer-0.3.0-portatil.zip" -Algorithm SHA256
```

Um hash verifica correspondência com o arquivo publicado; não representa assinatura digital do editor.
