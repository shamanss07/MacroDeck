# Histórico de mudanças

[English](CHANGELOG.md) · [Português (Brasil)](CHANGELOG.pt-BR.md)

As entradas abaixo registram somente versões públicas. Alterações de desenvolvimento ainda não lançadas não recebem uma versão pública.

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
