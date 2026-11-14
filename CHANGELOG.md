# Changelog

Todas as mudanças importantes deste projeto ficam registradas aqui.

## [1.7] - 2026-11-14

### Adicionado
- **Integração opcional com o RivaTuner (RTSS)**: a bateria (`PS5 75%   XBOX 77%`, nas cores do nível) e os avisos aparecem **dentro do jogo**, desenhados pelo RivaTuner, inclusive em **tela cheia exclusiva com as otimizações de tela inteira desligadas**, onde nenhuma janela do Windows aparece (relatado pelo usuário no *Resident Evil Revelations*, DX9).
  - O programa só escreve o texto pelo canal oficial do RivaTuner (memória compartilhada `RTSSSharedMemoryV2`, a mesma do HWiNFO e do AIDA64), usa um espaço próprio, respeita a trava "ocupado" do RTSS e limpa o texto ao sair. **Nada é injetado no jogo.**
  - Menu da bandeja **"RivaTuner (RTSS)"**: Desligado / Só em jogos em tela cheia exclusiva (padrão) / Sempre. O submenu mostra se o RivaTuner está aberto.
  - Em tela cheia exclusiva, a janela do overlay e os cartões ficam escondidos e tudo vai para o RivaTuner, sem duplicar. Os sons continuam.
  - A tela cheia exclusiva é detectada pelo próprio Windows (`SHQueryUserNotificationState`).
- Opções de linha de comando `--rtss-modo desligado|tela-cheia|sempre` e `--rtss-mapa <nome>` (RivaTuner simulado, usado pela suíte).
- Testes R1 (texto), R2 (RivaTuner simulado: espaço livre, atualização, trava, limpeza, RTSS antigo) e A15 (o programa escreve no RivaTuner), mais o item manual M33.

### Alterado
- O `compilar.bat` compila com `/unsafe` (a trava do RivaTuner precisa de uma operação atômica na memória compartilhada).
- Documentação de tela cheia: jogos DX12 sempre mostram o overlay; jogos DX9/10/11 com as otimizações desligadas precisam do RivaTuner.

### Corrigido
- A suíte falhava nos testes de janela (A11, A12) quando havia um jogo aberto em tela cheia exclusiva, porque o programa (corretamente) esconde a janela nesse caso. Os testes de janela agora rodam com o RivaTuner desligado.

## [1.6.1] - 2026-11-14

### Alterado
- **Licença nova: [PolyForm Noncommercial 1.0.0](LICENSE)** (até a v1.6 era MIT). O programa continua **100% gratuito**: pode ser usado, copiado, modificado e compartilhado de graça, para fins não comerciais. **Vender, cobrar pelo download ou usar comercialmente é proibido.**
- Aviso "gratuito, venda proibida" em destaque no README, no Leia-me, no ReadMe e na página de download, com o endereço oficial no GitHub.
- O programa em si não mudou.

## [1.6] - 2026-11-14

### Alterado
- **Nome novo: Gamepad Battery Overlay** (antes *DualSense Battery Overlay*), porque o programa agora funciona com controles de PS5, PS4 e Xbox, e para o nome não usar marcas de terceiros. O executável passa a ser `GamepadBatteryOverlay.exe` e as configurações ficam em `HKCU\Software\GamepadBatteryOverlay`.

### Adicionado
- **Migração automática** para quem atualiza: na primeira vez que a v1.6 abre, as configurações da chave antiga (`HKCU\Software\DualSenseOverlay`) são copiadas, mantendo os tipos, e a entrada "Iniciar com o Windows" passa para o nome novo, apontando para o .exe novo. A chave antiga é mantida e a migração não roda de novo.
- Se a versão antiga (`DualSenseOverlay.exe`) estiver aberta, a nova fecha ela, para não mostrar overlay, notificações e ícone em dobro.
- Testes G1 (migração, com chaves de teste no registro) e A14 (fecha a versão antiga), mais o item manual M32.

## [1.5] - 2026-11-14

### Adicionado
- **Ícone do programa** (o .exe), feito a partir da arte do projeto: aparece no Explorer, na área de trabalho e no Gerenciador de Tarefas. O fundo de fora do círculo é transparente, e o ícone tem 9 tamanhos (16 a 256 px).
- `tools/gerar-icone.ps1`: gera o `assets/icone.ico` a partir da arte (PNG, JPG ou WebP, lidos pelo próprio Windows). O `compilar.bat` embute o ícone com `/win32icon`.
- Testes T1 (ícone da bandeja) e T2 (ícone do .exe), mais os itens manuais M30 e M31.

### Alterado
- **Ícone da bandeja**: no lugar da bolinha com a dezena, um controle com uma barra de bateria embaixo que enche conforme a % do controle mais descarregado e muda de cor. Desenhado pixel a pixel para 16 e 24 px. O controle fica claro na barra de tarefas escura e escuro na clara, e muda na hora quando o tema do Windows muda. Sem controle, o ícone fica apagado e com a barra vazia.

## [1.4] - 2026-11-14

### Adicionado
- **Tradução para o inglês** de todo o programa: menu da bandeja, notificações, dica do ícone e textos do overlay ("sem info" → "no info").
- Opção **"Idioma / Language"** no menu da bandeja (Português (Brasil) / English). Na primeira vez segue o idioma do Windows (português se o Windows estiver em português, inglês nos outros casos). A troca vale na hora, sem reiniciar, e fica salva no registro (valor `Language`).
- **ReadMe.txt**: guia completo em inglês, equivalente ao Leia-me.txt.
- `src/Lang.cs`: cada texto é escrito nos dois idiomas no próprio lugar onde é usado (`Lang.T("português", "English")`).
- Opção de linha de comando `--idioma pt|en` (força o idioma só naquela execução, sem salvar), usada pela suíte.
- Testes L1 (troca de idioma e textos traduzidos) e A13 (notificações em inglês rodam sem sobrepor), mais os itens manuais M28 e M29. O D1 agora confere também a versão no ReadMe.txt.

## [1.3] - 2026-11-14

### Adicionado
- Suporte a **controles Xbox** (Series X|S, One S, Elite 2). Linha `XBOX` no overlay e avisos de conectado, desconectado e bateria muito baixa, com os sons.
  - **Bluetooth:** a bateria vem da mesma informação que a tela "Bluetooth e dispositivos" das Configurações do Windows usa (propriedade de bateria do Bluetooth). O controle não é aberto nem recebe comandos. É lida a cada 30 s, porque o próprio controle só atualiza esse valor de vez em quando; conexão e desconexão continuam sendo detectadas na hora.
  - **USB:** o Windows não informa a bateria do Xbox no cabo, então a linha mostra **"USB mode"** no lugar do ícone e da porcentagem, e o aviso diz "via USB · USB mode".
- **Atalho L3+R3 no Xbox** (Bluetooth e USB) para mostrar e ocultar o overlay, igual ao DualShock 4. Os botões são lidos pelo XInput (só leitura, 20 vezes por segundo, só nas posições que têm um Xbox de verdade); o 360 virtual do DS4Windows/DSX é ignorado, para o atalho não disparar duas vezes.
- Regra geral para qualquer controle: **com informação de bateria** mostra ícone e %; **sem informação** mostra o texto no lugar da bateria e **nunca** dispara o alerta de bateria baixa nem entra no "Só mostrar com bateria baixa".
- O controle Xbox 360 virtual criado pelo DS4Windows/DSX (ViGEm, PID `028E`) é ignorado, para não aparecer um "Xbox" falso.
- Novo modo de teste `--teste-xbox-usb` (finge um Xbox no cabo) e testes X1 a X5, H6/H7, A11 e A12, mais os itens manuais M22 a M27.

### Limitações
- Pelo Bluetooth, o Xbox não informa se está carregando: não há os avisos "Carregando" e "Carga completa" para ele.
- Um Xbox 360 com fio de verdade também usa o PID `028E` e por isso fica de fora.

## [1.2.1] - 2026-11-13

### Corrigido
- **Controle cheio aparecia como 85%** (80% no DSX). Medindo os controles, ficou claro que na bateria eles vão só até o nível 8: o DualSense com carga completa manda `0x28` (completo + nível 8). A escala agora é de 0 a 8 na bateria (cheio = 100%), e no DS4 com cabo é de 0 a 11, como o DS4Windows.

### Adicionado
- Opção **"Escala da bateria"** na bandeja: "0 a 8 — corrigida" (padrão) ou "0 a 10 — antiga (igual ao DSX)". A troca recalcula os controles conectados na hora.
- Testes U1/U2 (escala corrigida), U1b/U2b (escala antiga) e U8 (o alerta crítico só no nível mais baixo, nas duas escalas), mais o item manual M21.
- Backup local da v1.2 em `backups/` (fica fora do repositório).

## [1.2] - 2026-11-13

### Adicionado
- **Efeitos sonoros** sutis para cada notificação: conectado, desconectado, carregando, carga completa, carregamento parado, bateria muito baixa, overlay oculto e overlay visível. Os sons são sintetizados pelo próprio programa (notas tipo sino geradas em memória), sem arquivos de som e sem material de terceiros. Duram de 0,3 a 1,3 s, com pico em torno de -16 dBFS.
- Opção **"Sons das notificações"** no menu da bandeja, ligada por padrão.
- Opções de linha de comando `--mudo` (sem som) e `--exportar-sons <pasta>` (salva os sons em .wav).
- Testes U7 (sons válidos, curtos e discretos) e A10 (exportar sons), mais os itens M18 a M20 do checklist manual.

### Corrigido
- O gerador de WAV gravava todas as amostras, mesmo depois de cortar o silêncio do fim, e o cabeçalho ficava diferente do conteúdo. Achado durante o desenvolvimento e coberto pelo teste U7.

## [1.1] - 2026-11-13

### Adicionado
- Suporte ao **DualShock 4** (PS4), 1ª e 2ª geração, por Bluetooth e USB.
- Atalho **L3+R3** no DualShock 4 para mostrar e ocultar o overlay. O DualSense continua usando o Mute.
- **Vários controles ao mesmo tempo**: uma linha por controle no overlay, com a etiqueta PS5/PS4.
- **Alerta obrigatório abaixo de 10%**: o controle com bateria crítica aparece mesmo com o overlay oculto. Opção "Sempre mostrar com bateria abaixo de 10%" na bandeja, ligada por padrão.
- Notificação "Bateria muito baixa".
- Notificações empilháveis e com o nome do controle.
- Identificação do controle pelo MAC, para não repetir o mesmo controle quando ele está no USB e no Bluetooth ao mesmo tempo.

### Alterado
- A leitura agora é contínua, com uma thread por controle. A bateria atualiza em cerca de 1 s, em vez de a cada 3 s.
- `DualSenseHid.cs` foi substituído por `PadHid.cs`.
- A ferramenta de diagnóstico agora verifica todos os controles conectados e ganhou a opção `-Controle PS4|PS5`.
- No menu da bandeja, "Atualizar agora" virou "Procurar controles agora".

### Corrigido
- Notificações ficavam uma por cima da outra quando apareciam várias seguidas, por exemplo ao apertar o atalho muitas vezes. Agora a posição é recalculada continuamente e elas deslizam para o lugar certo. Avisos do mesmo tipo (atalho, estado de um mesmo controle) se substituem em vez de empilhar. O limite é de 4 na tela, e as mais antigas saem rápido.
- Em rajadas de avisos podiam ficar até 6 cartões na tela. Agora o máximo é 4 + 1 saindo.
- Uso de CPU alto (~3%) com vários controles ou com o mesmo controle no USB e no Bluetooth. A leitura agora bloqueia sem custo, pacotes repetidos são descartados sem calcular o CRC, e a desconexão é vigiada por um timer à parte. Ficou em ~1%.
- As notificações não redesenham quando nada mudou.

### Testes
- Protocolo de testes (`docs/PROTOCOLO-DE-TESTES.md`) e suíte automática de regressão (`tools/testes/rodar-testes.ps1`): compilação, unidade, hardware, programa rodando e documentação.
- Modos de teste escondidos no .exe: `--teste-rajada` e `--teste-bateria-baixa`.

## [1.0] - 2026-11-13

Primeira versão, por Rafael Gonçalez. Feita com vibe-coding.

### Adicionado
- Leitura da bateria do DualSense via HID nativo do Windows (Bluetooth e USB), sem programas de terceiros.
- Overlay sempre visível, com o clique passando através dele e cores por nível de bateria.
- Notificações animadas: conectado, desconectado, carregando, carga completa, carregamento parado.
- Detecção instantânea de conexão e desconexão via `WM_DEVICECHANGE`.
- Botão Mute do controle para mostrar e ocultar o overlay.
- Ícone na bandeja com menu: posição, modo "só bateria baixa", notificações, atalho do Mute, testar notificações, iniciar com o Windows.
- Ferramenta de diagnóstico `tools/testar-bateria.ps1` (CRC32, ao vivo, log).
