# Histórico de mudanças

[English](CHANGELOG.md) · [Português (Brasil)](CHANGELOG.pt-BR.md)

As entradas abaixo registram somente versões públicas. Alterações de desenvolvimento ainda não lançadas não recebem uma versão pública.

## 1.2.0 — Variações na reprodução e pausas entre ciclos

- Define em pixels o deslocamento máximo do mouse em cada eixo. Com 0 px, o MacroDeck usa as coordenadas gravadas.
- Adiciona atrasos aleatórios opcionais entre ações sem prolongar o tempo de teclas ou botões pressionados.
- Permite variar a quantidade de cliques repetidos por uma porcentagem configurável. A porcentagem fica salva ao desligar a opção. Cliques isolados e arrastes são preservados.
- Permite somar um atraso aleatório à espera configurada entre ciclos.
- Adiciona pausas mais longas entre ciclos, com chance de 1% a 100%, duração de 1 a 60 minutos e um mínimo de ciclos completos antes de outra pausa. A reprodução retoma automaticamente e pode ser interrompida durante a espera.
- Melhora o retorno suave do cursor com mudanças graduais de velocidade e pequenos ajustes laterais ao longo do trajeto.
- Salva as opções de reprodução por macro e permite abrir arquivos de macros antigos.

## 1.1.5 — Leitor de tela e melhorias na reprodução

- Adiciona a aba Leitor para montar, reordenar, salvar e reabrir fluxos que localizam textos pelo reconhecimento do Windows ou comparam uma imagem de referência com a tela. Os fluxos salvos preservam as imagens de referência.
- Permite importar uma imagem de arquivo, capturá-la na tela ou colá-la da área de transferência. Cada etapa pode usar o monitor inteiro como área padrão ou uma região personalizada, com tempo limite de busca e pausa após a ação.
- Permite testar uma etapa movendo o cursor até o alvo, sem clicar. Na execução do fluxo, verifica se o alvo está estável e é único antes de clicar e interrompe a execução ao pressionar Esc ou se a janela alvo ou a configuração dos monitores mudar.
- Direciona o atalho global de Reproduzir/Parar para a aba ativa e permite interromper um macro ou fluxo em execução mesmo após trocar de aba. Melhora as transições, o alinhamento dos painéis do Leitor e as bordas das listas suspensas.
- Aumenta a precisão da gravação e da reprodução com temporização mais fina, captura mais frequente do mouse e registro da posição inicial do cursor. Reproduz ações nas coordenadas gravadas e mantém a compatibilidade com macros antigos.
- Adiciona opções por macro para pequenas variações no tempo e na posição do cursor e para mover o cursor suavemente até o ponto inicial antes e depois de cada ciclo concluído. Ambas vêm desativadas.
- Solta teclas e botões do mouse mantidos pressionados ao interromper um macro e aguarda a conclusão dessa limpeza antes de iniciar outra operação ou fechar o aplicativo.
- Aplica os valores digitados de repetição antes de reproduzir ou salvar, preserva alterações pendentes ao trocar de idioma e salva as opções dos macros e as preferências do aplicativo sem deixar arquivos gravados pela metade.
- Minimiza para a área de notificação do Windows, de onde o aplicativo pode ser restaurado ou fechado. Os atalhos globais continuam disponíveis.
- Avisa ao abrir sem privilégios de administrador. Permite reiniciar como administrador ou lembrar de solicitar essa permissão nas próximas aberturas. O Windows ainda pede aprovação quando necessário.
- Exibe a versão do próprio executável na tela Sobre, em inglês e português do Brasil.
- Corrige o script de compilação local para ignorar arquivos gerados por compilações anteriores no Visual Studio.

## 1.1.4 — correções ao salvar macros e opções de reprodução

- Salva as opções de reprodução somente no macro ativo, sem alterar os valores dos outros macros salvos.
- Permite salvar as opções editadas como um novo macro quando o nome é alterado.
- Preserva as ações e as opções salvas ao substituir ou criar uma cópia do macro.

## 1.1.3 — transições mais suaves e correções de tema

- Melhora as transições da janela, das páginas e do menu, incluindo minimizar, restaurar e maximizar.
- Remove o flash branco do menu e evita que os cantos dos painéis arredondados pisquem durante a troca de tema.
- Reduz a lentidão do cursor na inicialização, ativando a captura do mouse somente durante uma gravação.
- Estabiliza o fade entre os temas claro e escuro e atualiza as cores da janela Opções conforme o tema selecionado.

## 1.1.2 — correção do texto de repetição

- Ajusta a largura do controle de repetição ao espaço do painel para evitar o corte do rótulo em inglês.

## 1.1.1 — correções de alinhamento e fontes

- Centraliza verticalmente o texto dos campos e alinha as unidades aos valores.
- Centraliza as setas dos controles de incremento, mantendo o alinhamento em diferentes tamanhos.
- Aplica a fonte selecionada de forma consistente nos intervalos e nas janelas reabertas.

## 1.1.0 — suporte ao idioma inglês

- Adiciona traduções para inglês e português do Brasil, com troca de idioma aplicada imediatamente.
- Detecta o idioma de interface do Windows como padrão na primeira execução; uma preferência salva no aplicativo prevalece.
- Define a documentação em inglês como principal e mantém a documentação em português disponível.
- Corrige o corte da janela Opções e faz o workflow de CI do Windows localizar o MSBuild corretamente.
- Gera e anexa um executável Windows com versão às releases do GitHub marcadas por tag.

## 1.0.0 — primeira versão pública

- Gravação, teste, salvamento e reprodução de macros de teclado e mouse.
- Atalhos globais configuráveis e controles de repetição com intervalo.
- Biblioteca de macros com seleção de pasta, atualização e exclusão protegida por confirmação.
- Temas claro e escuro, cores personalizáveis, fontes instaladas e preferências persistentes.
- Interface Windows Forms com ícone MacroDeck.
- Escala da interface independente da resolução, com ajuste ao espaço disponível e a mudanças de monitor/DPI.
- Suporte inicial exclusivo para Windows e .NET Framework 4.8.

---

[README do projeto](README.pt-BR.md) · [English](CHANGELOG.md)
