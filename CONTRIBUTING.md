# Como contribuir

O MacroDeck está sendo preparado para a primeira publicação pública. Contribuições devem ser pequenas, focadas e acompanhadas de uma descrição clara do comportamento alterado.

## Ambiente

- Windows
- .NET Framework 4.8 Developer Pack
- Visual Studio ou compilador C# do .NET Framework

Compile com `Source/build.ps1` ou abra `Source/MacroDeck.csproj` no Visual Studio. Antes de enviar uma alteração, revise o diff e confirme que ela compila no Windows.

## Versões e publicação

O repositório recebe somente alterações selecionadas e revisadas para uma release. O desenvolvimento local pode avançar sem criar uma nova versão pública a cada ajuste. As releases usam SemVer (`MAJOR.MINOR.PATCH`): mudanças compatíveis de recursos incrementam MINOR; correções compatíveis incrementam PATCH; mudanças incompatíveis incrementam MAJOR. Cada release deve atualizar `CHANGELOG.md`, a versão do assembly e a tag Git. O artefato binário publicado é para Windows.

## Escopo atual

O programa depende de Windows Forms e APIs de entrada do Windows. Não envie alterações que apresentem suporte a outros sistemas como disponível sem uma implementação e validação próprias.
