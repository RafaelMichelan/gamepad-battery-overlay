# Instruções para o assistente (projeto feito com vibe-coding)

Projeto: **Gamepad Battery Overlay** (até a v1.5: DualSense Battery Overlay), de Rafael Gonçalez. C# / .NET Framework 4, compilado com `compilar.bat`, sem dependências.

## Protocolo de testes: obrigatório a cada alteração
Toda vez que o código (`src/`), as ferramentas (`tools/`) ou o `compilar.bat` forem alterados:

1. Rodar a suíte completa **antes de dizer que terminou**:
   `powershell -ExecutionPolicy Bypass -File tools\testes\rodar-testes.ps1`
2. Se algo der **FALHOU**: corrigir na hora e rodar a suíte inteira de novo, até dar 0 FALHOU. Não entregar com falha.
3. Todo bug corrigido ganha um teste que pega o problema se ele voltar (na suíte ou no checklist manual) e uma linha no histórico de `docs/PROTOCOLO-DE-TESTES.md`.
4. Funcionalidade nova ganha teste novo na suíte (ou item no checklist manual, se depender do controle na mão).
5. Dizer ao usuário quais itens do checklist manual (M1–M33) ele precisa conferir por causa da mudança.
6. Manter a versão igual em `src/AssemblyInfo.cs`, `Leia-me.txt`, `README.md`, `CHANGELOG.md` e `.github/ISSUE_TEMPLATE` (o teste D1 confere isso).

Detalhes em `docs/PROTOCOLO-DE-TESTES.md`.

## Convenções
- Documentação em português do Brasil (Leia-me.txt), com o guia em inglês em ReadMe.txt: toda mudança no Leia-me vale também no ReadMe.
- Textos da interface sempre nos dois idiomas: `Lang.T("português", "English")` (src/Lang.cs). Nenhum texto visível sem tradução.
- Assinatura: "Rafael Gonçalez", "Feito com vibe-coding".
- O programa só **lê** os controles (acesso compartilhado). Nunca enviar output reports (LED, vibração), para não interferir com DS4Windows, DSX ou jogos.
