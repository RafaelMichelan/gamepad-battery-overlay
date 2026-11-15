# Protocolo de testes

**Regra do projeto:** toda alteração no programa passa por este protocolo **antes** de ser entregue ou publicada.

1. Rodar a **suíte automática** (seção 1). Tem que terminar com **0 FALHOU**.
2. Se a mudança mexeu em algo que só dá para conferir com o controle na mão, rodar os itens correspondentes do **checklist manual** (seção 2).
3. **Bug encontrado → corrigir na hora** e rodar a suíte de novo, do zero.
4. **Todo bug corrigido ganha um teste** que pega o problema se ele voltar, e entra no **histórico** (seção 3).
5. O resultado da última execução fica em `tools/testes/ultimo-resultado.txt`.

---

## 1. Suíte automática

```bat
powershell -ExecutionPolicy Bypass -File tools\testes\rodar-testes.ps1
```

Leva cerca de 1 minuto e meio. Ela fecha o programa (para recompilar), abre e fecha o programa em vários modos de teste e, no final, deixa ele aberto. Com `-SemApp`, pula a parte que abre o programa.

Para os testes de hardware, **deixe os controles ligados e parados**. Sem controle conectado, esses testes aparecem como *PULADO*, não como falha.

| ID | O que testa | Como |
|---|---|---|
| C1 | Compila sem erros **e sem avisos** | roda `compilar.bat` |
| C2 | A versão gravada no .exe bate com `AppInfo.Version` | propriedades do .exe |
| U1 | Bateria do DualSense na escala corrigida (0 a 8): **nível 8 = 100%**, todos os estados e byte inválido | casos fixos |
| U2 | Bateria do DS4 na escala corrigida: bateria de 0 a 8, cabo de 0 a 11, completa e inválido | casos fixos |
| U1b / U2b | As mesmas decodificações na **escala antiga** (0 a 10), a opção para voltar atrás | casos fixos |
| U8 | O alerta crítico (< 10%) só dispara no nível mais baixo, nas duas escalas | casos fixos |
| U3 | Layout dos 4 relatórios (DS BT/USB, DS4 BT/USB): ID, início dos dados, byte da bateria | casos fixos |
| U4 | Atalho: Mute (PS5); L3+R3 **juntos** (PS4), com L3 ou R3 sozinhos **não** disparando | pacotes sintéticos |
| U5 | PIDs reconhecidos (0CE6, 0DF2, 05C4, 09CC) e PID desconhecido recusado | casos fixos |
| U6 | CRC32: pacote correto aceito, pacote com 1 bit trocado recusado | CRC calculado de forma independente |
| X1 | Xbox Bluetooth: caminho HID reconhecido, MAC lido do caminho; 360 virtual (028E), não-Bluetooth e DualSense recusados | caminhos fixos |
| X2 | Xbox USB (XInput): só Xbox de verdade "com fio"; 360 virtual e Xbox Bluetooth ficam de fora | casos fixos |
| X3 | Regra **"USB mode"**: sem informação de bateria mostra o texto e **nunca** é crítico/baixo; Xbox BT com 5% é crítico; PS com 0% continua crítico | casos fixos |
| X4 | Nomes dos modelos Xbox pelo PID | casos fixos |
| L1 | Idioma: `Apply("en")`/`Apply("pt")` trocam os textos ("sem info"/"no info", "Controle Xbox"/"Xbox controller"); código inválido usa o idioma do Windows | casos fixos |
| T1 | Ícone da bandeja: tamanho 16/20/24 px, barra cheia em 100%, vazia em 0%, pelo menos 1 px acima de 0%, metade em 50%, controle claro/escuro conforme o tema, apagado sem controle | desenho gerado |
| T2 | `assets\icone.ico` válido (16 a 256 px) e embutido no .exe | arquivo e .exe |
| X5 | Atalho do Xbox: L3+R3 **juntos** dispara; L3 ou R3 sozinhos **não** | botões sintéticos |
| R1 | RivaTuner: texto igual ao overlay (`PS5 75%` com a cor do nível, `+` carregando, USB mode, aviso numa segunda linha, vazio com o overlay oculto) | casos fixos |
| R2 | RivaTuner **simulado** (memória compartilhada falsa, o RTSS de verdade não é tocado): pega o primeiro espaço livre, não mexe no espaço de outro programa, reaproveita o mesmo espaço, não reescreve sem mudança, respeita a trava "ocupado", limpa ao sair, escreve no campo antigo em RTSS < 2.7 e ignora quando não há RivaTuner | memória simulada |
| R3 | RivaTuner só quando o jogo **esconde** o overlay (posse exclusiva da tela): com as otimizações ligadas ou DX12, overlay normal; a janela só some com o RivaTuner mostrando | casos fixos |
| S1 | Iniciar com o Windows (chaves de teste no registro): não cria sem querer, recria a entrada que sumiu, corrige caminho antigo, não mexe na entrada certa, respeita a desativação do Gerenciador de Tarefas, marcar reativa, desmarcar tira tudo | registro de teste |
| S2 | Reconhece "aberto com o Windows" pela marca `--inicio-windows` **ou** por abrir até 3 min depois da área de trabalho (ou 5 min depois de o PC ligar); aberto à mão depois disso não conta | casos fixos |
| G1 | Nome novo: as configurações e o "Iniciar com o Windows" migram do nome antigo, mantendo o tipo dos valores, uma vez só e sem sobrescrever | chaves de teste no registro |
| U7 | Os 8 efeitos sonoros: WAV válido (cabeçalho bate com o conteúdo), de 0,1 a 1,5 s, pico entre -30 e -15 dBFS e sem estalo no fim | sons gerados |
| H1 | Para cada controle conectado: MAC lido (12 dígitos hex) | controle real |
| H2 | ≥ 90% dos relatórios completos e com CRC válido | 300 pacotes |
| H3 | Relógio interno do controle avançando (dados novos, não repetidos) | 300 pacotes |
| H4 | Analógicos parados perto de 128 (prova que os bytes estão alinhados) | 300 pacotes |
| H5 | Byte da bateria decodifica e não oscila | 300 pacotes |
| H6 | Xbox Bluetooth: MAC lido e bateria entre 0 e 100%, igual em duas leituras seguidas | controle real |
| H7 | Xbox no cabo: vira "USB mode" | controle real |
| A1 | O overlay aparece com **uma linha por controle** | janela real |
| A2 | O overlay está no canto configurado na bandeja | janela real |
| A3 | A notificação de conexão **não cobre o overlay** | monitora as janelas por 3 s |
| A4 | Só abre uma vez: a segunda cópia fecha sozinha | abre duas vezes |
| A5 | CPU < 3% de um núcleo | 10 s medindo |
| A6 | O programa continua rodando e respondendo | processo |
| A7 | **Rajada**: 19 apertos do atalho e 5 avisos seguidos, sem nada sobreposto por mais de 400 ms e com no máximo 4 (+1 saindo) notificações | `--teste-rajada` |
| A8 | "Testar notificações" roda inteiro sem sobrepor e sem travar | `--demo` |
| A9 | Bateria abaixo de 10% aparece **mesmo com o overlay oculto** | `--teste-bateria-baixa` |
| A10 | `--exportar-sons` gera os 8 arquivos .wav e sai | pasta temporária |
| A15 | O programa aberto com `--rtss-modo sempre` escreve no RivaTuner simulado os controles conectados | `--rtss-mapa` |
| A16 | Aberto com `--inicio-windows` (como o Windows abre): mostra o aviso "ativo" e anota "aberto pelo Windows" no registro de inicializações | `--inicio-windows` |
| A17 | Com o overlay **oculto**, um DualSense simulado completa a carga e o aviso "Carga completa" aparece | `--teste-carga-completa` |
| A14 | A versão antiga (`DualSenseOverlay.exe`) aberta junto é fechada pelo programa novo | processo antigo simulado |
| A13 | Notificações em inglês rodam sem sobrepor, e o `--idioma` **não** grava no registro | `--demo --idioma en` |
| A11 | Xbox no cabo simulado: o overlay ganha **uma linha a mais** ("USB mode") | `--teste-xbox-usb` |
| A12 | Xbox no cabo simulado **não** entra no alerta obrigatório: altura igual à do A9 | `--teste-xbox-usb --teste-bateria-baixa` |
| D1 | A mesma versão no Leia-me, no README, no CHANGELOG e no modelo de issue | texto dos arquivos |
| D2 | Arquivos obrigatórios do repositório presentes | arquivos |

Todos os testes que abrem o programa usam `--mudo`, para não tocar som no PC durante os testes.

### Modos de teste escondidos do .exe
Só servem para testes, não para uso normal:
- `--demo` mostra todos os tipos de notificação em sequência.
- `--teste-rajada` simula o atalho apertado várias vezes seguidas e uma série de avisos.
- `--teste-bateria-baixa` finge 5% de bateria e começa com o overlay oculto.
- `--idioma pt|en` força o idioma só nesta execução (não salva).
- `--rtss-modo desligado|tela-cheia|sempre` força o modo do RivaTuner só nesta execução. Os testes de janela usam `desligado`, para não falharem se houver um jogo em tela cheia exclusiva aberto.
- `--teste-carga-completa` começa com o overlay oculto e finge um DualSense carregando que completa a carga 2 s depois.
- `--inicio-windows` é a marca que a entrada de inicialização passa (o programa se considera aberto pelo Windows).
- `--rtss-mapa <nome>` usa outra memória compartilhada no lugar da do RivaTuner (RivaTuner simulado).
- `--teste-xbox-usb` finge um controle Xbox ligado no cabo (linha "USB mode").
- `--mudo` não toca nenhum som.
- `--exportar-sons <pasta>` salva os efeitos sonoros como .wav e sai.

---

## 2. Checklist manual (precisa do controle na mão)

Rode os itens que tenham relação com o que foi alterado. Antes de publicar uma versão nova, rode **todos**.

| ID | Passo | Esperado |
|---|---|---|
| M1 | DualSense: apertar **Mute** | O overlay some e aparece "Overlay oculto". Apertar de novo: o overlay volta e aparece "Overlay visível" |
| M2 | DS4: apertar **L3+R3** juntos | Igual ao M1 |
| M3 | DS4: apertar só L3 e depois só R3 | **Nada** acontece |
| M4 | Apertar o atalho 10× rápido | Um único cartão que alterna o texto, sem cartões encavalados |
| M5 | Ligar o cabo USB num controle | "Carregando" em ~1 s, barra azul com raio. Continua aparecendo **uma** linha para esse controle |
| M6 | Tirar o cabo | "Carregamento parado" (laranja) |
| M7 | Desligar o controle (segurar PS por 10 s) | "(controle) desconectado" em ~2 s e a linha some |
| M8 | Ligar o controle (PS) | "(controle) conectado" e a linha volta |
| M9 | Ligar o DualSense e o DS4 juntos | Duas linhas, PS5 e PS4, na ordem em que conectaram |
| M10 | Bandeja: mudar a **Posição** para cada um dos 4 cantos | O overlay e as notificações vão para o canto escolhido |
| M11 | Bandeja: **Só mostrar com bateria baixa** | O overlay some (se a bateria estiver acima de 20%) |
| M12 | Bandeja: desligar **Notificações** e desligar e ligar o controle | Nenhum aviso aparece |
| M13 | Bandeja: desligar **Atalho no controle** e apertar Mute ou L3+R3 | Nada acontece |
| M14 | Bandeja: desligar **Sempre mostrar abaixo de 10%** e rodar `GamepadBatteryOverlay.exe --teste-bateria-baixa` | O overlay **não** aparece |
| M15 | Bandeja: **Iniciar com o Windows** marcado e reiniciar o PC (sem controle ligado) | Uns segundos depois de entrar no Windows aparece o aviso **"Gamepad Battery Overlay ativo"**, e o ícone está na bandeja. Em `%LOCALAPPDATA%\GamepadBatteryOverlay\inicializacoes.log` a última linha diz "aberto pelo Windows". ✅ v1.7.1: reiniciado sem controle, o aviso apareceu e o registro anotou "aberto pelo Windows" |
| M16 | Comparar a bateria com o DSX ou a Steam | Mesmo nível (o DSX mostra o limite de baixo da faixa e o overlay mostra o meio, ex.: 60% × 65%) |
| M17 | Rodar um jogo em janela sem bordas **e** em tela cheia exclusiva | O overlay e as notificações aparecem por cima do jogo e o jogo não perde o foco. ✅ v1.2: em tela cheia exclusiva, overlay visível no Resident Evil 4 Remake (DX12) e no Resident Evil Revelations (DX9) |
| M18 | Bandeja: **Testar notificações**, ouvindo | Cada aviso tem seu som, curto e baixinho, sem estalos |
| M19 | Bandeja: desligar **Sons das notificações** e usar o atalho | Os avisos aparecem **sem som**. Ao religar, toca um "tic" de amostra |
| M20 | Abaixar o volume do programa no Mixer de Volume do Windows | O volume dos sons acompanha |
| M21 | Tirar do cabo um controle com carga completa | Mostra **100%** (escala corrigida). Bandeja > Escala da bateria > antiga: passa a mostrar 85% **na hora**. Voltar para a corrigida: 100% de novo |
| M22 | Ligar o Xbox pelo Bluetooth | "Xbox Series conectado · via Bluetooth" e a linha `XBOX` com a bateria. Comparar com Configurações > Bluetooth e dispositivos: diferença de no máximo 1 a 2% |
| M23 | Desligar o Xbox (segurar o botão Xbox) | "Xbox Series desconectado" em ~2 s e a linha some |
| M24 | Ligar o Xbox no cabo USB | "Xbox Series conectado · via USB · USB mode" e a linha `XBOX  USB mode`, sem ícone de bateria e sem alerta de bateria baixa |
| M25 | DS4Windows aberto, emulando Xbox 360 | **Não** aparece nenhuma linha `XBOX` a mais (o 360 virtual é ignorado) |
| M26 | Xbox: apertar **L3+R3** juntos (Bluetooth e, se der, no cabo) | Igual ao M1. Só L3 ou só R3: **nada** acontece |
| M27 | DS4Windows aberto emulando 360 + DS4: apertar L3+R3 no DS4 | Um único "Overlay oculto/visível" (não dispara duas vezes pelo 360 virtual) |
| M28 | Bandeja: **Idioma / Language > English** | Aviso "Language: English" e, ao abrir o menu de novo, tudo em inglês. Desligar e ligar um controle: avisos em inglês ("connected" / "disconnected"). Fechar e abrir o programa: continua em inglês |
| M29 | Bandeja: **Idioma / Language > Português (Brasil)** | Volta tudo para o português na hora, inclusive a dica do ícone |
| M30 | Olhar o ícone da bandeja e depois trocar o Windows para o tema claro (Configurações > Personalização > Cores) | Controle com a barra na cor do nível do controle mais descarregado. Na troca de tema o controle muda de claro para escuro na hora. Sem controle: ícone apagado |
| M31 | Ver o `GamepadBatteryOverlay.exe` no Explorer (ícones grandes e lista) e no Gerenciador de Tarefas | Aparece o ícone redondo do projeto, sem fundo preto nos cantos |
| M32 | Com a v1.5 configurada (ex.: outro canto, inglês, Iniciar com o Windows marcado), abrir a v1.6 | Mesmo canto, mesmo idioma e "Iniciar com o Windows" continua marcado; a v1.5, se estava aberta, fecha sozinha |
| M33 | RivaTuner aberto, jogo DX9/DX11 com "Desabilitar otimizações de tela inteira" marcado (ex.: RE Revelations), em tela cheia exclusiva | A linha `PS5 75%` (cores do nível) aparece **dentro do jogo**; desligar e ligar um controle mostra o aviso no jogo por ~5 s; o atalho (Mute / L3+R3) esconde e mostra. Sair do jogo: o overlay normal volta. Fechar o programa: o texto some do RivaTuner. Bandeja > RivaTuner > Desligado: nada aparece no jogo. ✅ v1.7: *Resident Evil Revelations* (DX9), otimizações desligadas, controle Xbox: bateria e avisos aparecem no jogo |
| M34 | RivaTuner aberto, jogo em tela cheia com as otimizações de tela inteira **ligadas** (ex.: RE Revelations sem marcar a opção) e um jogo DX12 (ex.: RE4 Remake) | Aparece o **overlay normal** (janela), não o texto do RivaTuner. ✅ v1.7.1: *RE Revelations* (DX9) com as otimizações ligadas mostrou o overlay normal; desligadas, o RivaTuner |
| M35 | Abrir o menu da bandeja e passar por todos os grupos (Overlay, Notificações, Controle, RivaTuner, Idioma), em português e em inglês | Tudo no lugar, sem texto cortado; desmarcar "Aviso ao iniciar com o Windows" e reiniciar o PC: o programa abre sem o aviso |

---

## 3. Histórico de bugs (cada um tem um teste que pega se voltar)

| # | Versão | Bug | Teste que cobre |
|---|---|---|---|
| 1 | 1.0 (dev) | A thread de leitura morria por chamar `BeginInvoke` antes de a janela existir, e o overlay nunca atualizava | A1, A6 |
| 2 | 1.0 (dev) | O diagnóstico acusava falha porque o contador de sequência do DualSense não muda; trocado pelo relógio interno | H3 |
| 3 | 1.0 (dev) | O diagnóstico contava as ocorrências do byte de bateria em vez dos valores diferentes (falso "Atenção") | H5 |
| 4 | 1.1 (dev) | Ao conectar, a notificação aparecia **por cima do overlay**, porque era posicionada antes de o overlay crescer | A3 |
| 5 | 1.1 | Notificações **encavalavam** ao ligar e desligar o overlay muitas vezes: a posição era calculada uma vez só e o overlay mudava de altura | A7, M4 |
| 6 | 1.1 | Em rajadas chegavam a ficar **6 cartões** na tela: os que estavam saindo se acumulavam (achado pela suíte) | A7 |
| 7 | 1.1 | **CPU em 3,3%** com o DS4 no USB e no Bluetooth ao mesmo tempo: cada pacote criava um evento do Windows e calculava o CRC (achado pela suíte). Agora ~1% | A5 |
| 9 | 1.2 | **Controle cheio aparecia como 85%**: a escala supunha níveis de 0 a 10, mas na bateria os controles param no 8 (relatado pelo usuário, confirmado pelo byte `0x28` = completo + nível 8 e pelo DS4Windows, que mostra 100% com o mesmo DS4) | U1, U2, M21 |
| 10 | 1.3 (dev) | A etiqueta **XBOX** (4 letras) encostava no ícone da bateria; agora usa uma fonte menor (achado na captura de tela do overlay) | M22, M24 |
| 11 | 1.7 (dev) | Os testes de janela (A11, A12) falhavam com um jogo aberto em tela cheia exclusiva, porque o programa esconde a janela e passa para o RivaTuner (achado pela suíte com o *RE Revelations* aberto) | A11, A12 com `--rtss-modo desligado` |
| 12 | 1.7 | Com o RivaTuner aberto, **todo** jogo em tela cheia ia para o RivaTuner, inclusive com as otimizações ligadas (quando o overlay aparece). O "jogo em tela cheia" do Windows vale nos dois casos; medido no RE Revelations, só a posse exclusiva da tela separa (relatado pelo usuário) | R3, M34 |
| 13 | 1.7.1 (dev) | O A12 falhava às vezes: olhava a altura num instante só e o DualSense no Bluetooth pode demorar alguns segundos para mandar a 1ª leitura depois que o programa anterior foi fechado (achado pela suíte) | A12 agora espera até 9 s e confere de novo 2 s depois |
| 14 | 1.7 | "Iniciar com o Windows" parecia não funcionar: numa inicialização o programa abriu mas ficou invisível (sem controle, só no ícone da bandeja); em outras a entrada de inicialização tinha sumido (relatado pelo usuário, confirmado nos eventos 9705-9708 do Shell-Core) | S1, A16, M15 |
| 15 | 1.7.1 (dev) | O aviso de "aberto com o Windows" não apareceu no boot: a entrada chegou sem a marca `--inicio-windows` (algo a reescreveu) e a gravação do registro de inicializações falhou calada (achado no teste real do usuário) | S2, M15 |
| 8 | 1.2 (dev) | O WAV gerado gravava **todas** as amostras, mesmo depois do corte do silêncio: o cabeçalho dizia um tamanho e o arquivo tinha outro (WAV malformado e sons com 2 a 3 s) | U7 |

### Como adicionar um bug novo
1. Escrever um teste que **falha** com o bug (na suíte, se der para automatizar, ou no checklist manual).
2. Corrigir.
3. Ver o teste passar e rodar a suíte inteira.
4. Acrescentar uma linha nesta tabela e uma entrada "Corrigido" no `CHANGELOG.md`.
