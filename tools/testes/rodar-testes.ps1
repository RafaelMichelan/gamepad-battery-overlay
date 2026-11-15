<#
  Suíte de testes de regressão do Gamepad Battery Overlay.
  Roda TUDO o que dá para testar sem mexer fisicamente no controle.
  O checklist manual fica em docs\PROTOCOLO-DE-TESTES.md.

  Uso (na pasta do projeto):
    powershell -ExecutionPolicy Bypass -File tools\testes\rodar-testes.ps1
    ... -SemApp        pula os testes que abrem o programa (só compilação, unidade, hardware e docs)

  Saída: lista OK / FALHOU / PULADO, salva em tools\testes\ultimo-resultado.txt
  Código de saída: 0 = tudo certo, 1 = alguma falha.
  ATENÇÃO: fecha o GamepadBatteryOverlay.exe aberto (recompila) e reabre no final.
#>
param([switch]$SemApp)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exe = Join-Path $root 'GamepadBatteryOverlay.exe'
$results = New-Object System.Collections.ArrayList
$inicio = Get-Date

function Res($id, $nome, $status, $det) {
    [void]$results.Add([pscustomobject]@{ ID = $id; Teste = $nome; Resultado = $status; Detalhe = $det })
    $cor = @{ OK = 'Green'; FALHOU = 'Red'; PULADO = 'DarkYellow' }[$status]
    Write-Host ("[{0,-6}] {1,-4} {2}{3}" -f $status, $id, $nome, $(if ($det) { "  -> $det" } else { '' })) -ForegroundColor $cor
}

# O bloco devolve $true (passou), "PULADO: motivo" ou qualquer outra coisa (= falhou, com o detalhe)
function Check($id, $nome, [scriptblock]$sb) {
    try {
        $r = @(& $sb)
        $r = if ($r.Count) { $r[-1] } else { $null }
        if ($r -eq $true) { Res $id $nome 'OK' '' }
        elseif ($r -is [string] -and $r.StartsWith('PULADO')) { Res $id $nome 'PULADO' ($r.Substring(6).Trim(': ')) }
        else { Res $id $nome 'FALHOU' "$r" }
    }
    catch { Res $id $nome 'FALHOU' $_.Exception.Message }
}

function Secao($t) { Write-Host ""; Write-Host "== $t ==" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------------------------
Secao "1. Compilação"
# ---------------------------------------------------------------------------------------------
Check 'C1' 'Compila sem erros e sem avisos' {
    $out = cmd /c "echo.| `"$root\compilar.bat`"" | Out-String
    if ($out -notmatch 'OK: GamepadBatteryOverlay.exe gerado') { return "erro de compilação: $out" }
    if ($out -match 'warning') { return "avisos: $($out.Trim())" }
    $true
}
Check 'C2' 'Versão gravada no .exe = AppInfo.Version' {
    $ver = ([regex]::Match((Get-Content "$root\src\AssemblyInfo.cs" -Raw), 'Version = "([\d.]+)"')).Groups[1].Value
    $fv = (Get-Item $exe).VersionInfo.FileVersion
    if (-not $fv.StartsWith($ver)) { return "exe=$fv, AppInfo=$ver" }
    $true
}

# ---------------------------------------------------------------------------------------------
Secao "2. Testes de unidade (lógica de leitura)"
# ---------------------------------------------------------------------------------------------
# Rtss.cs usa ponteiros (trava atômica do RivaTuner): compila com /unsafe, igual ao compilar.bat
$cp = New-Object CodeDom.Compiler.CompilerParameters
$cp.CompilerOptions = '/unsafe'; $cp.GenerateInMemory = $true; [void]$cp.ReferencedAssemblies.Add('System.dll'); [void]$cp.ReferencedAssemblies.Add('System.Core.dll')
Add-Type -Path "$root\src\PadHid.cs", "$root\src\XboxPad.cs", "$root\src\Lang.cs", "$root\src\Rtss.cs" -CompilerParameters $cp

# RivaTuner simulado: cria uma memória compartilhada no mesmo formato da do RTSS (8 espaços), sem tocar no RTSS de verdade
$RTSS_ENTRY = 299520; $RTSS_ARR = 96
function New-FakeRtss([string]$name, [int]$ver = 0x00020014) {
    $mmf = [IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew($name, $RTSS_ARR + 8 * $RTSS_ENTRY)
    $acc = $mmf.CreateViewAccessor()
    $acc.Write(0, [int]0x52545353); $acc.Write(4, [int]$ver); $acc.Write(20, [int]$RTSS_ENTRY); $acc.Write(24, [int]$RTSS_ARR); $acc.Write(28, [int]8)
    return @($mmf, $acc)
}
function Get-RtssSlot($acc, [int]$i, [int]$off, [int]$len) {
    $b = New-Object byte[] $len; [void]$acc.ReadArray($RTSS_ARR + $i * $RTSS_ENTRY + $off, $b, 0, $len)
    $n = [Array]::IndexOf($b, [byte]0); if ($n -lt 0) { $n = $len }
    [Text.Encoding]::Default.GetString($b, 0, $n)
}
function Set-RtssSlot($acc, [int]$i, [int]$off, [string]$s) {
    $b = [Text.Encoding]::Default.GetBytes($s + [char]0); $acc.WriteArray($RTSS_ARR + $i * $RTSS_ENTRY + $off, $b, 0, $b.Length)
}

# casos: @(byte, decodifica?, % esperado, estado esperado)  — estado 0 descarregando, 1 carregando, 2 completo
function Test-Decode($model, [bool]$legacy, $casos) {
    [PadHid]::LegacyScale = $legacy
    try {
        foreach ($c in $casos) {
            $p = 0; $s = 0
            $ok = [PadHid]::DecodeBattery([PadModel]$model, [byte]$c[0], [ref]$p, [ref]$s)
            if ($ok -ne $c[1] -or ($ok -and ($p -ne $c[2] -or $s -ne $c[3]))) {
                return ("escala {0}, 0x{1:X2}: deu ok={2} {3}% st={4}, esperado ok={5} {6}% st={7}" -f $(if ($legacy) { 'antiga' } else { 'corrigida' }), $c[0], $ok, $p, $s, $c[1], $c[2], $c[3])
            }
        }
        return $true
    }
    finally { [PadHid]::LegacyScale = $false }
}
Check 'U1' 'Bateria do DualSense — escala corrigida 0 a 8 (bug corrigido v1.2.1: cheio mostrava 85%)' {
    Test-Decode 'DualSense' $false @(@(0x08, $true, 100, 0), @(0x06, $true, 75, 0), @(0x04, $true, 50, 0), @(0x01, $true, 12, 0),
        @(0x00, $true, 0, 0), @(0x0A, $true, 100, 0), @(0x18, $true, 100, 1), @(0x14, $true, 50, 1), @(0x28, $true, 100, 2), @(0xB5, $false, 0, 0))
}
Check 'U2' 'Bateria do DualShock 4 — escala corrigida (bateria 0 a 8, cabo 0 a 11)' {
    Test-Decode 'DualShock4' $false @(@(0x08, $true, 100, 0), @(0x06, $true, 75, 0), @(0x00, $true, 0, 0), @(0x0A, $true, 100, 0),
        @(0x18, $true, 72, 1), @(0x1A, $true, 90, 1), @(0x1B, $true, 100, 2), @(0x1C, $false, 0, 0))
}
Check 'U1b' 'Bateria do DualSense — escala antiga 0 a 10 (opção "voltar")' {
    Test-Decode 'DualSense' $true @(@(0x06, $true, 65, 0), @(0x16, $true, 65, 1), @(0x25, $true, 100, 2), @(0x0A, $true, 100, 0),
        @(0x00, $true, 5, 0), @(0x1A, $true, 100, 1), @(0xB5, $false, 0, 0))
}
Check 'U2b' 'Bateria do DualShock 4 — escala antiga 0 a 10 (opção "voltar")' {
    Test-Decode 'DualShock4' $true @(@(0x06, $true, 65, 0), @(0x0A, $true, 100, 0), @(0x00, $true, 5, 0), @(0x16, $true, 65, 1),
        @(0x1A, $true, 100, 1), @(0x1B, $true, 100, 2), @(0x1C, $false, 0, 0))
}
Check 'U8' 'Alerta crítico (< 10%) só no nível mais baixo, nas duas escalas' {
    foreach ($legacy in $false, $true) {
        [PadHid]::LegacyScale = $legacy
        $p = 0; $s = 0
        [void][PadHid]::DecodeBattery([PadModel]::DualSense, [byte]0x00, [ref]$p, [ref]$s); $p0 = $p
        [void][PadHid]::DecodeBattery([PadModel]::DualSense, [byte]0x01, [ref]$p, [ref]$s); $p1 = $p
        [PadHid]::LegacyScale = $false
        if ($p0 -ge 10 -or $p1 -lt 10) { return "escala antiga=${legacy}: nível 0 -> $p0%, nível 1 -> $p1%" }
    }
    $true
}
Check 'U3' 'Layout dos relatórios (ID, início, byte da bateria)' {
    $casos = @(@('DualSense', $true, 0x31, 2, 54), @('DualSense', $false, 0x01, 1, 53),
               @('DualShock4', $true, 0x11, 3, 32), @('DualShock4', $false, 0x01, 1, 30))
    foreach ($c in $casos) {
        $d = New-Object PadDevice; $d.Model = [PadModel]$c[0]; $d.Bluetooth = $c[1]
        if ($d.ReportId -ne $c[2] -or $d.Base -ne $c[3] -or $d.BatteryIndex -ne $c[4]) {
            return ("{0} bt={1}: id=0x{2:X2} base={3} bat={4}" -f $c[0], $c[1], $d.ReportId, $d.Base, $d.BatteryIndex)
        }
    }
    $true
}
Check 'U4' 'Atalho: Mute (PS5) e L3+R3 juntos (PS4)' {
    $casos = @(@('DualSense', $true, 11, 0x04, $true), @('DualSense', $true, 11, 0x00, $false), @('DualSense', $false, 10, 0x04, $true),
               @('DualShock4', $true, 8, 0xC0, $true), @('DualShock4', $true, 8, 0x40, $false), @('DualShock4', $true, 8, 0x80, $false),
               @('DualShock4', $false, 6, 0xC0, $true))
    foreach ($c in $casos) {
        $d = New-Object PadDevice; $d.Model = [PadModel]$c[0]; $d.Bluetooth = $c[1]
        $buf = New-Object byte[] 80; $buf[$c[2]] = [byte]$c[3]
        if ([PadHid]::TogglePressed($d, $buf) -ne $c[4]) { return ("{0} bt={1} byte{2}=0x{3:X2}: esperado {4}" -f $c[0], $c[1], $c[2], $c[3], $c[4]) }
    }
    $true
}
Check 'U5' 'Modelos reconhecidos pelo PID' {
    $m = @{ 0x0CE6 = 'DualSense'; 0x0DF2 = 'DualSenseEdge'; 0x05C4 = 'DualShock4'; 0x09CC = 'DualShock4' }
    foreach ($pid_ in $m.Keys) { $r = [PadHid]::ModelFromPid([uint16]$pid_); if ("$r" -ne $m[$pid_]) { return ("PID {0:X4} -> '{1}'" -f $pid_, $r) } }
    if ($null -ne [PadHid]::ModelFromPid([uint16]0x1234)) { return "PID desconhecido foi aceito" }
    $true
}
Check 'U6' 'CRC32 do Bluetooth (pacote válido passa, corrompido falha)' {
    $poly = [int64]3988292384
    $tbl = foreach ($n in 0..255) { $c = [int64]$n; for ($k = 0; $k -lt 8; $k++) { if ($c -band 1) { $c = ($c -shr 1) -bxor $poly } else { $c = $c -shr 1 } }; $c }
    $rnd = New-Object Random 42
    $buf = New-Object byte[] 78; $rnd.NextBytes($buf); $buf[0] = 0x31
    $crc = [int64]4294967295
    foreach ($b in (@([byte]0xA1) + $buf[0..73])) { $crc = $tbl[($crc -bxor $b) -band 255] -bxor ($crc -shr 8) }
    $crc = $crc -bxor [int64]4294967295
    for ($i = 0; $i -lt 4; $i++) { $buf[74 + $i] = [byte](($crc -shr (8 * $i)) -band 255) }
    if (-not [PadHid]::CheckCrc($buf)) { return "pacote com CRC correto foi rejeitado" }
    $buf[20] = $buf[20] -bxor 0x01
    if ([PadHid]::CheckCrc($buf)) { return "pacote corrompido foi aceito" }
    $true
}

Check 'X1' 'Xbox Bluetooth: caminho HID reconhecido, MAC lido, controle virtual do DS4Windows ignorado' {
    $real = '\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}&dev&vid_045e&pid_0b13&rev_0524&a1b2c3d4e5f6&ig_00#b&1234abcd&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
    $pid_ = [uint16]0
    if (-not [XboxHid]::IsBluetoothXboxPath($real, [ref]$pid_) -or $pid_ -ne 0x0B13) { return "Xbox Series BT recusado (pid $pid_)" }
    if ([XboxHid]::MacFromPath($real) -ne 'a1b2c3d4e5f6') { return "MAC lido: '$([XboxHid]::MacFromPath($real))'" }
    $virt = '\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}&dev&vid_045e&pid_028e&rev_0100&112233445566&ig_00#x#{4d1e55b2-f16f-11cf-88cb-001111000030}'
    if ([XboxHid]::IsBluetoothXboxPath($virt, [ref]$pid_)) { return "PID 028E (360 virtual) foi aceito" }
    $usbHid = '\\?\hid#vid_045e&pid_028e&ig_00#1&2&3#{4d1e55b2-f16f-11cf-88cb-001111000030}'
    if ([XboxHid]::IsBluetoothXboxPath($usbHid, [ref]$pid_)) { return "dispositivo que não é Bluetooth foi aceito" }
    $sony = '\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&1&0#{4d1e55b2-f16f-11cf-88cb-001111000030}'
    if ([XboxHid]::IsBluetoothXboxPath($sony, [ref]$pid_)) { return "DualSense foi aceito como Xbox" }
    $true
}
Check 'X2' 'Xbox USB (XInput): só Xbox de verdade com fio; 360 virtual e Xbox Bluetooth ficam de fora' {
    $casos = @(@(0x045E, 0x0B12, 1, $true), @(0x045E, 0x02EA, 1, $true), @(0x045E, 0x028E, 1, $false), @(0x045E, 0x0B13, 0, $false), @(0x054C, 0x0CE6, 1, $false))
    foreach ($c in $casos) {
        if ([XboxHid]::IsWiredXbox([uint16]$c[0], [uint16]$c[1], [byte]$c[2]) -ne $c[3]) { return ("VID {0:X4} PID {1:X4} tipo {2}: esperado {3}" -f $c[0], $c[1], $c[2], $c[3]) }
    }
    $true
}
Check 'X3' 'Regra "USB mode": sem informação de bateria mostra o texto e nunca dispara o alerta' {
    $usb = (New-Object XboxPad -Property @{ Key = 'k'; Path = 'xinput:0'; Pid = 0x0B12; Bluetooth = $false }).ToState()
    if (-not $usb.NoBattery -or [BatteryRules]::ShowsBattery($usb)) { return "Xbox no cabo deveria ficar sem bateria" }
    if ([BatteryRules]::IsCritical($usb) -or [BatteryRules]::IsLow($usb)) { return "Xbox no cabo disparou alerta de bateria baixa" }
    if ([BatteryRules]::NoBatteryLabel($usb) -ne 'USB mode') { return "texto '$([BatteryRules]::NoBatteryLabel($usb))'" }
    $bt5 = (New-Object XboxPad -Property @{ Key = 'm'; Path = 'p'; Pid = 0x0B13; Bluetooth = $true; Percent = 5 }).ToState()
    if (-not [BatteryRules]::ShowsBattery($bt5) -or -not [BatteryRules]::IsCritical($bt5)) { return "Xbox BT com 5% deveria ser crítico" }
    $bt80 = (New-Object XboxPad -Property @{ Key = 'm'; Path = 'p'; Pid = 0x0B13; Bluetooth = $true; Percent = 80 }).ToState()
    if ([BatteryRules]::IsCritical($bt80) -or [BatteryRules]::IsLow($bt80) -or $bt80.Percent -ne 80) { return "Xbox BT com 80% errado" }
    $btNo = (New-Object XboxPad -Property @{ Key = 'm'; Path = 'p'; Pid = 0x0B13; Bluetooth = $true; Percent = -1 }).ToState()
    if (-not $btNo.NoBattery -or [BatteryRules]::IsCritical($btNo)) { return "Xbox BT sem leitura deveria ficar sem bateria e sem alerta" }
    $ps = New-Object BatteryState -Property @{ Ok = $true; Percent = 0; Status = 0 }
    if (-not [BatteryRules]::IsCritical($ps)) { return "PS com 0% deixou de ser crítico" }
    $true
}
Check 'X4' 'Nomes dos modelos Xbox pelo PID' {
    $m = @{ 0x0B12 = 'Xbox Series'; 0x0B13 = 'Xbox Series'; 0x0B22 = 'Xbox Elite 2'; 0x02FD = 'Xbox One'; 0x1234 = 'Controle Xbox' }
    foreach ($k in $m.Keys) { $n = [XboxHid]::ModelName([uint16]$k); if ($n -ne $m[$k]) { return ("PID {0:X4} -> '{1}'" -f $k, $n) } }
    $true
}

Check 'X5' 'Atalho do Xbox: L3+R3 juntos; L3 ou R3 sozinhos não disparam' {
    $casos = @(@(0x00C0, $true), @(0x10C0, $true), @(0x0040, $false), @(0x0080, $false), @(0x0000, $false), @(0x0030, $false))
    foreach ($c in $casos) { if ([XboxHid]::TogglePressed([uint16]$c[0]) -ne $c[1]) { return ("botões 0x{0:X4}: esperado {1}" -f $c[0], $c[1]) } }
    $true
}

Check 'L1' 'Idioma: troca português/inglês e textos traduzidos (sem info / no info, Controle Xbox / Xbox controller)' {
    try {
        $bt = New-Object BatteryState -Property @{ Ok = $true; NoBattery = $true; Bluetooth = $true }
        [Lang]::Apply('en')
        if (-not [Lang]::En -or [Lang]::Code -ne 'en') { return "Apply('en') não ligou o inglês" }
        if ([Lang]::T('sim', 'yes') -ne 'yes') { return "T() não devolveu o inglês" }
        if ([BatteryRules]::NoBatteryLabel($bt) -ne 'no info') { return "BT sem bateria em inglês: '$([BatteryRules]::NoBatteryLabel($bt))'" }
        if ([XboxHid]::ModelName([uint16]0x1234) -ne 'Xbox controller') { return "nome genérico em inglês: '$([XboxHid]::ModelName([uint16]0x1234))'" }
        [Lang]::Apply('pt')
        if ([Lang]::En -or [Lang]::T('sim', 'yes') -ne 'sim') { return "Apply('pt') não voltou ao português" }
        if ([BatteryRules]::NoBatteryLabel($bt) -ne 'sem info') { return "BT sem bateria em português: '$([BatteryRules]::NoBatteryLabel($bt))'" }
        [Lang]::Apply('xx')
        if ([Lang]::Code -ne [Lang]::SystemDefault()) { return "código inválido não caiu no idioma do Windows" }
        $true
    }
    finally { [Lang]::Apply('pt') }
}

Add-Type -AssemblyName System.Drawing
Add-Type -Path "$root\src\TrayIcon.cs" -ReferencedAssemblies System.Drawing
Check 'T1' 'Ícone da bandeja: controle + barra que enche com a %, nos tamanhos 16/20/24 e nos temas claro/escuro' {
    $verde = [Drawing.Color]::FromArgb(90, 210, 110)
    foreach ($sz in 16, 20, 24) { $b = [TrayIcon]::Draw($sz, 50, $verde, $false); if ($b.Width -ne $sz -or $b.Height -ne $sz) { return "pedido $sz px, veio $($b.Width)x$($b.Height)" } }
    $casos = @(@(12, 100, 12), @(12, 0, 0), @(12, 1, 1), @(12, 50, 6), @(17, 100, 17), @(17, 12, 2))
    foreach ($c in $casos) { $w = [TrayIcon]::FillWidth($c[0], $c[1]); if ($w -ne $c[2]) { return "FillWidth($($c[0]), $($c[1])%) = $w, esperado $($c[2])" } }
    # 16 px: barra interna começa em (1,11); 100% pinta até x=12; 0% não pinta
    $cheio = [TrayIcon]::Draw(16, 100, $verde, $false)
    if ($cheio.GetPixel(1, 11).ToArgb() -ne $verde.ToArgb() -or $cheio.GetPixel(12, 14).ToArgb() -ne $verde.ToArgb()) { return "100%: barra não ficou cheia" }
    $vazio = [TrayIcon]::Draw(16, 0, $verde, $false)
    if ($vazio.GetPixel(1, 11).A -ne 0) { return "0%: a barra foi pintada" }
    $meio = [TrayIcon]::Draw(16, 50, $verde, $false)
    if ($meio.GetPixel(6, 12).ToArgb() -ne $verde.ToArgb() -or $meio.GetPixel(7, 12).A -ne 0) { return "50%: a barra deveria ir até x=6" }
    # tema: controle claro na barra escura, escuro na barra clara
    $esc = [TrayIcon]::Draw(16, 50, $verde, $false).GetPixel(5, 3); $cla = [TrayIcon]::Draw(16, 50, $verde, $true).GetPixel(5, 3)
    if ($esc.ToArgb() -ne [TrayIcon]::LightInk.ToArgb() -or $cla.ToArgb() -ne [TrayIcon]::DarkInk.ToArgb()) { return "cor do controle não segue o tema" }
    # sem controle: desenha apagado (alpha < 255), sem preencher
    $nada = [TrayIcon]::Draw(16, -1, [Drawing.Color]::Gray, $false)
    if ($nada.GetPixel(5, 3).A -ge 255 -or $nada.GetPixel(1, 11).A -ne 0) { return "sem controle: deveria ficar apagado e vazio" }
    $true
}
Check 'T2' 'Ícone do .exe: assets\icone.ico válido (16 a 256 px) e embutido no programa' {
    $ico = [IO.File]::ReadAllBytes("$root\assets\icone.ico")
    if ([BitConverter]::ToUInt16($ico, 0) -ne 0 -or [BitConverter]::ToUInt16($ico, 2) -ne 1) { return "cabeçalho do .ico inválido" }
    $n = [BitConverter]::ToUInt16($ico, 4); $tams = @(for ($k = 0; $k -lt $n; $k++) { $t = $ico[6 + 16 * $k]; if ($t -eq 0) { 256 } else { $t } })
    foreach ($s in 16, 24, 32, 48, 256) { if ($tams -notcontains $s) { return "falta o tamanho $s px no .ico (tem: $($tams -join ', '))" } }
    $b = ([Drawing.Icon]::ExtractAssociatedIcon($exe)).ToBitmap(); $amarelos = 0
    for ($y = 0; $y -lt $b.Height; $y++) { for ($x = 0; $x -lt $b.Width; $x++) { $p = $b.GetPixel($x, $y); if ($p.R -gt 200 -and $p.G -gt 150 -and $p.B -lt 90) { $amarelos++ } } }
    if ($amarelos -lt 20) { return "o .exe não está com o ícone do programa ($amarelos pixels amarelos)" }
    $true
}

Check 'R1' 'RivaTuner: texto do jogo igual ao overlay (cores do nível, USB mode, aviso, vazio quando oculto)' {
    $ps5 = New-Object BatteryState -Property @{ Ok = $true; Tag = 'PS5'; Percent = 75; Status = 0 }
    $ps4 = New-Object BatteryState -Property @{ Ok = $true; Tag = 'PS4'; Percent = 45; Status = 1 }
    $xb = New-Object BatteryState -Property @{ Ok = $true; Tag = 'XBOX'; NoBattery = $true; Bluetooth = $false }
    $low = New-Object BatteryState -Property @{ Ok = $true; Tag = 'PS5'; Percent = 12; Status = 0 }
    $l = New-Object 'System.Collections.Generic.List[BatteryState]'; $l.Add($ps5); $l.Add($ps4); $l.Add($xb)
    $t = [RtssOsd]::Format($l, $null)
    if ($t -ne 'PS5 <C=5AD26E>75%<C>   PS4 <C=50AAFF>45%+<C>   XBOX USB mode') { return "texto: '$t'" }
    $l2 = New-Object 'System.Collections.Generic.List[BatteryState]'; $l2.Add($low)
    $t2 = [RtssOsd]::Format($l2, 'Bateria muito baixa')
    if ($t2 -ne "PS5 <C=F05046>12%<C>`nBateria muito baixa") { return "texto com aviso: '$t2'" }
    $vazio = New-Object 'System.Collections.Generic.List[BatteryState]'
    if ([RtssOsd]::Format($vazio, $null) -ne '') { return "sem linhas deveria ficar vazio (overlay oculto)" }
    if ([RtssOsd]::Format($vazio, 'Overlay oculto') -ne 'Overlay oculto') { return "só o aviso deveria aparecer" }
    $true
}
Check 'R2' 'RivaTuner simulado: pega um espaço livre, atualiza, respeita a trava "ocupado", não mexe no espaço dos outros e limpa ao sair' {
    $name = 'GBO_TesteRTSS_' + [Guid]::NewGuid().ToString('N')
    $mmf, $acc = New-FakeRtss $name
    try {
        Set-RtssSlot $acc 1 256 'OutroApp'; Set-RtssSlot $acc 1 512 'texto do outro'
        $r = New-Object RtssOsd $name
        if (-not $r.IsAvailable()) { return "não reconheceu o RivaTuner simulado" }
        if (-not $r.Update('abc')) { return "Update falhou" }
        if ((Get-RtssSlot $acc 2 256 256) -ne 'GamepadBatteryOverlay' -or (Get-RtssSlot $acc 2 512 4096) -ne 'abc') { return "não escreveu no primeiro espaço livre (2)" }
        if ((Get-RtssSlot $acc 1 256 256) -ne 'OutroApp' -or (Get-RtssSlot $acc 1 512 4096) -ne 'texto do outro') { return "mexeu no espaço de outro programa" }
        if ($acc.ReadInt32(32) -ne 1 -or $acc.ReadInt32(36) -ne 0) { return "contador de quadro=$($acc.ReadInt32(32)), trava=$($acc.ReadInt32(36)) (esperado 1 e 0)" }
        [void]$r.Update('def')
        if ((Get-RtssSlot $acc 2 512 4096) -ne 'def' -or (Get-RtssSlot $acc 3 256 256) -ne '') { return "não reaproveitou o mesmo espaço" }
        $f = $acc.ReadInt32(32); [void]$r.UpdateIfChanged('def')
        if ($acc.ReadInt32(32) -ne $f) { return "reescreveu sem o texto mudar" }
        $acc.Write(36, [int]1)
        if ($r.Update('ghi')) { return "escreveu com o RivaTuner ocupado" }
        if ((Get-RtssSlot $acc 2 512 4096) -ne 'def') { return "o texto mudou com o RivaTuner ocupado" }
        $acc.Write(36, [int]0)
        $r.Release()
        if ((Get-RtssSlot $acc 2 256 256) -ne '' -or (Get-RtssSlot $acc 2 512 4096) -ne '') { return "não limpou o espaço ao sair" }
        if ((Get-RtssSlot $acc 1 256 256) -ne 'OutroApp') { return "a limpeza apagou o espaço de outro programa" }
    }
    finally { $acc.Dispose(); $mmf.Dispose() }
    # RTSS antigo (antes da 2.7): escreve no campo curto szOSD
    $name2 = 'GBO_TesteRTSS_' + [Guid]::NewGuid().ToString('N')
    $mmf, $acc = New-FakeRtss $name2 0x00020005
    try {
        [void](New-Object RtssOsd $name2).Update('velho')
        if ((Get-RtssSlot $acc 1 0 256) -ne 'velho') { return "RTSS antigo: não escreveu no campo szOSD" }
    }
    finally { $acc.Dispose(); $mmf.Dispose() }
    $sem = New-Object RtssOsd ('GBO_NaoExiste_' + [Guid]::NewGuid().ToString('N'))
    if ($sem.IsAvailable() -or $sem.Update('x')) { return "sem RivaTuner deveria só ignorar" }
    $true
}

Check 'R3' 'RivaTuner só quando o jogo esconde o overlay (bug corrigido v1.7.1: ia para o RivaTuner com as otimizações ligadas)' {
    # medido no RE Revelations: otimizações ligadas -> "jogo em tela cheia" mas SEM posse exclusiva (overlay aparece);
    # desligadas -> posse exclusiva (overlay some). A decisão só pode depender da posse exclusiva.
    $casos = @(
        @(1, $false, $false, 'padrão, otimizações ligadas / DX12: overlay normal'),
        @(1, $true, $true, 'padrão, posse exclusiva: RivaTuner'),
        @(2, $false, $true, 'sempre: RivaTuner mesmo sem tela cheia'),
        @(0, $true, $false, 'desligado: nunca'))
    foreach ($c in $casos) { if ([RtssRules]::ShouldWrite($c[0], $c[1]) -ne $c[2]) { return "$($c[3]): esperado $($c[2])" } }
    if ([RtssRules]::HideWindow($true, $false)) { return "escondeu a janela sem posse exclusiva (modo sempre em janela)" }
    if ([RtssRules]::HideWindow($false, $true)) { return "escondeu a janela com o RivaTuner fechado (ficaria sem nada na tela)" }
    if (-not [RtssRules]::HideWindow($true, $true)) { return "não escondeu a janela com o RivaTuner mostrando em tela exclusiva" }
    $true
}

Add-Type -Path "$root\src\Settings.cs"
Check 'G1' 'Nome novo (v1.6): configurações e "Iniciar com o Windows" migram do nome antigo, uma vez só' {
    $base = 'Software\GBO_TesteMigracao'; $hk = "HKCU:\$base"
    try {
        Remove-Item $hk -Recurse -Force -ErrorAction SilentlyContinue
        New-Item "$hk\Antigo" -Force | Out-Null
        Set-ItemProperty "$hk\Antigo" -Name Corner -Value 'BL'
        New-ItemProperty "$hk\Antigo" -Name Notify -Value 0 -PropertyType DWord | Out-Null
        if (-not [SettingsMigration]::CopyKey("$base\Antigo", "$base\Novo")) { return "não copiou da chave antiga" }
        $n = Get-ItemProperty "$hk\Novo"
        if ($n.Corner -ne 'BL' -or $n.Notify -ne 0) { return "valores copiados errados (Corner=$($n.Corner), Notify=$($n.Notify))" }
        if ((Get-Item "$hk\Novo").GetValueKind('Notify') -ne 'DWord') { return "Notify perdeu o tipo DWORD (o programa lê como número)" }
        Set-ItemProperty "$hk\Antigo" -Name Corner -Value 'TL'
        if ([SettingsMigration]::CopyKey("$base\Antigo", "$base\Novo")) { return "migrou de novo por cima da chave nova" }
        if ((Get-ItemProperty "$hk\Novo").Corner -ne 'BL') { return "a chave nova foi sobrescrita" }
        if ([SettingsMigration]::CopyKey("$base\NaoExiste", "$base\Novo2")) { return "inventou configurações sem chave antiga" }
        New-Item "$hk\Run" -Force | Out-Null
        Set-ItemProperty "$hk\Run" -Name 'Velho' -Value '"C:\x\DualSenseOverlay.exe"'
        if (-not [SettingsMigration]::MoveRunEntry("$base\Run", 'Velho', 'Novo', 'C:\y\GamepadBatteryOverlay.exe')) { return "não moveu a entrada de iniciar com o Windows" }
        $run = Get-ItemProperty "$hk\Run"
        if ($null -ne $run.Velho -or $run.Novo -ne '"C:\y\GamepadBatteryOverlay.exe"') { return "entrada de iniciar com o Windows errada: '$($run.Novo)'" }
        if ([SettingsMigration]::MoveRunEntry("$base\Run", 'Velho', 'Novo', 'C:\z.exe')) { return "mexeu na entrada sem existir a antiga" }
        $true
    }
    finally { Remove-Item $hk -Recurse -Force -ErrorAction SilentlyContinue }
}

Check 'S1' 'Iniciar com o Windows: recria a entrada que sumiu, corrige o caminho, respeita o Gerenciador de Tarefas (bug corrigido v1.7.1)' {
    $base = 'Software\GBO_TesteInicio'; $hk = "HKCU:\$base"; $run = "$base\Run"; $apr = "$base\Approved"
    $exeA = 'C:\Jogos\GamepadBatteryOverlay.exe'; $cmdA = [StartupEntry]::Command($exeA)
    try {
        Remove-Item $hk -Recurse -Force -ErrorAction SilentlyContinue
        if ($cmdA -ne '"C:\Jogos\GamepadBatteryOverlay.exe" --inicio-windows') { return "comando: $cmdA" }
        if ([StartupEntry]::Heal($run, $apr, 'GBO', $exeA, $false) -ne 'desligada' -or $null -ne [StartupEntry]::Current($run, 'GBO')) { return "criou a entrada sem o usuário querer" }
        if ([StartupEntry]::Heal($run, $apr, 'GBO', $exeA, $true) -ne 'recriada') { return "não recriou a entrada que sumiu" }
        if ([StartupEntry]::Current($run, 'GBO') -ne $cmdA) { return "entrada recriada errada: $([StartupEntry]::Current($run, 'GBO'))" }
        if ([StartupEntry]::Heal($run, $apr, 'GBO', $exeA, $true) -ne 'ok') { return "mexeu numa entrada que já estava certa" }
        Set-ItemProperty "$hk\Run" -Name GBO -Value '"D:\pasta antiga\GamepadBatteryOverlay.exe"'
        if ([StartupEntry]::Heal($run, $apr, 'GBO', $exeA, $true) -ne 'corrigida' -or [StartupEntry]::Current($run, 'GBO') -ne $cmdA) { return "não corrigiu o caminho antigo" }
        if (-not [StartupEntry]::IsActive($run, $apr, 'GBO', $exeA)) { return "entrada certa não contou como ativa" }
        # desativado no Gerenciador de Tarefas (03): o autoconserto não reativa
        New-Item "$hk\Approved" -Force | Out-Null
        New-ItemProperty "$hk\Approved" -Name GBO -PropertyType Binary -Value ([byte[]](3,0,0,0,0,0,0,0,0,0,0,0)) | Out-Null
        if ([StartupEntry]::Heal($run, $apr, 'GBO', $exeA, $true) -ne 'desativada') { return "não percebeu a desativação no Gerenciador de Tarefas" }
        if ((Get-ItemProperty "$hk\Approved").GBO[0] -ne 3) { return "o autoconserto reativou o que o usuário desativou no Gerenciador de Tarefas" }
        if ([StartupEntry]::IsActive($run, $apr, 'GBO', $exeA)) { return "desativado no Gerenciador contou como ativo" }
        # marcar na bandeja (Enable) reativa
        [StartupEntry]::Enable($run, $apr, 'GBO', $exeA)
        if ((Get-ItemProperty "$hk\Approved").GBO[0] -ne 2 -or -not [StartupEntry]::IsActive($run, $apr, 'GBO', $exeA)) { return "marcar na bandeja não reativou" }
        [StartupEntry]::Disable($run, $apr, 'GBO')
        if ($null -ne [StartupEntry]::Current($run, 'GBO') -or $null -ne (Get-ItemProperty "$hk\Approved").GBO) { return "desmarcar não tirou a entrada" }
        $true
    }
    finally { Remove-Item $hk -Recurse -Force -ErrorAction SilentlyContinue }
}

Check 'S2' 'Aberto com o Windows: reconhece pela marca OU por abrir logo depois de entrar no Windows (bug corrigido v1.7.1: aviso sumiu sem a marca)' {
    $casos = @(
        @($true, 9999, 99999, $true, 'com a marca'),
        @($false, 30, 99999, $true, 'sem a marca, 30 s depois da área de trabalho abrir (o caso do teste real)'),
        @($false, 179, 99999, $true, 'sem a marca, 3 min depois da área de trabalho'),
        @($false, 600, 120, $true, 'sem a marca, PC ligado há 2 min'),
        @($false, 600, 99999, $false, 'aberto à mão, bem depois'),
        @($false, -1, 99999, $false, 'sem saber da área de trabalho, PC ligado há muito tempo'))
    foreach ($c in $casos) { if ([LogonStart]::Decide($c[0], $c[1], $c[2]) -ne $c[3]) { return "$($c[4]): esperado $($c[3])" } }
    $true
}

Add-Type -Path "$root\src\Sounds.cs"
Check 'U7' 'Efeitos sonoros: WAV válido, curto e com volume discreto' {
    foreach ($k in [Enum]::GetValues([Sfx])) {
        $b = [SoundFx]::Wav($k)
        $riff = [Text.Encoding]::ASCII.GetString($b, 0, 4); $wave = [Text.Encoding]::ASCII.GetString($b, 8, 4)
        if ($riff -ne 'RIFF' -or $wave -ne 'WAVE') { return "${k}: cabeçalho inválido" }
        if ([BitConverter]::ToInt32($b, 4) -ne $b.Length - 8) { return "${k}: tamanho RIFF errado" }
        $data = [BitConverter]::ToInt32($b, 40)
        if ($data -ne $b.Length - 44) { return "${k}: cabeçalho diz $data bytes, arquivo tem $($b.Length - 44) (bug corrigido v1.2)" }
        $seg = $data / 88200
        if ($seg -lt 0.1 -or $seg -gt 1.5) { return ("{0}: duração {1:N2} s (esperado 0,1 a 1,5 s)" -f $k, $seg) }
        $pk = 0; for ($i = 44; $i -lt $b.Length; $i += 2) { $v = [Math]::Abs([int][BitConverter]::ToInt16($b, $i)); if ($v -gt $pk) { $pk = $v } }
        $db = 20 * [Math]::Log10([Math]::Max(1, $pk) / 32767)
        if ($db -gt -15 -or $db -lt -30) { return ("{0}: pico {1:N1} dBFS (esperado entre -30 e -15: discreto mas audível)" -f $k, $db) }
        $last = [Math]::Abs([int][BitConverter]::ToInt16($b, $b.Length - 2))
        if ($last -gt 50) { return "${k}: termina abrupto (amostra final $last), pode estalar" }
    }
    $true
}

# ---------------------------------------------------------------------------------------------
Secao "3. Hardware (controles conectados agora — deixe-os parados)"
# ---------------------------------------------------------------------------------------------
$pads = @([PadHid]::Enumerate())
$xboxes = @([XboxHid]::Enumerate($true, $null))
$logicos = @{}
if ($pads.Count -eq 0 -and $xboxes.Count -eq 0) { Res 'H0' 'Controles conectados' 'PULADO' 'nenhum controle conectado' }
foreach ($x in $xboxes) {
    $logicos[$x.Key] = $true
    $tag = "XBOX-$(if ($x.Bluetooth) { 'BT' } else { 'USB' })"
    if ($x.Bluetooth) {
        Check 'H6' "$tag MAC lido e bateria do Bluetooth entre 0 e 100% (agora: $($x.Percent)%)" {
            if ($x.Key -notmatch '^[0-9a-f]{12}$') { return "MAC='$($x.Key)'" }
            if ($x.Percent -lt 0 -or $x.Percent -gt 100) { return "o Windows não informou a bateria ($($x.Percent))" }
            $de_novo = [XboxHid]::ReadBluetoothBattery($x.Path)
            if ([Math]::Abs($de_novo - $x.Percent) -gt 2) { return "duas leituras seguidas diferentes: $($x.Percent)% e $de_novo%" }
            $true
        }
    }
    else { Check 'H7' "$tag no cabo vira USB mode" { if ($x.ToState().NoBattery) { $true } else { 'deveria ficar sem bateria' } } }
}
foreach ($d in $pads) {
    $conn = if ($d.Bluetooth) { 'BT' } else { 'USB' }
    $tag = "$($d.Tag)-$conn"
    $h = [PadHid]::Open($d.Path, $false)
    $mac = if ($h) { [PadHid]::GetIdentity($h, $d) } else { $null }
    if ($h) { $h.Dispose() }
    if ($mac) { $logicos[$mac] = $true } else { $logicos[$d.Path] = $true }
    Check "H1" "$tag identidade (MAC) lida" { if ($mac -match '^[0-9a-f]{12}$') { $true } else { "MAC='$mac'" } }
    $raw = [PadHid]::CaptureRaw($d, 300)
    $full = @($raw | Where-Object { [PadHid]::IsFullReport($d, $_, $_.Length) })
    Check "H2" "$tag relatórios completos e íntegros (CRC no BT)" { if ($full.Count -ge 0.9 * $raw.Count -and $full.Count -gt 0) { $true } else { "$($full.Count) de $($raw.Count)" } }
    Check "H3" "$tag relógio interno avançando (dados novos)" {
        $ts = @($full | ForEach-Object { if ($d.IsDS4) { [BitConverter]::ToUInt16($_, $d.TimestampIndex) } else { [BitConverter]::ToUInt32($_, $d.TimestampIndex) } })
        $mud = 0; for ($i = 1; $i -lt $ts.Count; $i++) { if ($ts[$i] -ne $ts[$i - 1]) { $mud++ } }
        # o DS4 no Bluetooth às vezes repete o relógio em alguns pacotes seguidos: exige 90%
        if ($ts.Count -gt 1 -and $mud -ge 0.9 * ($ts.Count - 1)) { $true } else { "mudou $mud de $($ts.Count - 1)" }
    }
    Check "H4" "$tag analógicos parados perto de 128 (alinhamento)" {
        for ($k = 0; $k -lt 4; $k++) {
            $avg = ($full | ForEach-Object { $_[$d.Base + $k] } | Measure-Object -Average).Average
            if ($avg -lt 90 -or $avg -gt 166) { return "eixo $k média $([int]$avg) (o controle estava sendo mexido?)" }
        }
        $true
    }
    Check "H5" "$tag bateria decodifica e está estável" {
        $vals = @($full | ForEach-Object { $_[$d.BatteryIndex] } | Select-Object -Unique)
        foreach ($v in $vals) { $p = 0; $s = 0; if (-not [PadHid]::DecodeBattery($d.Model, [byte]$v, [ref]$p, [ref]$s)) { return ("byte inválido 0x{0:X2}" -f $v) } }
        if ($vals.Count -gt 2) { return "oscilou entre $($vals.Count) valores" }
        $true
    }
}
$nLogicos = $logicos.Count

# ---------------------------------------------------------------------------------------------
Secao "4. Programa rodando (janelas, notificações, desempenho)"
# ---------------------------------------------------------------------------------------------
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class TestWin {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    public struct RECT { public int L, T, R, B; }
    // [hwnd, x, y, largura, altura] das janelas visíveis do processo
    public static List<long[]> Windows(int pid) {
        var list = new List<long[]>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            RECT r;
            if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out r))
                list.Add(new long[] { h.ToInt64(), r.L, r.T, r.R - r.L, r.B - r.T });
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@
Add-Type -AssemblyName System.Windows.Forms
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$corner = 'TR'; $notifyOn = $true
try { $k = Get-ItemProperty 'HKCU:\Software\GamepadBatteryOverlay' -ErrorAction Stop; if ($k.Corner) { $corner = $k.Corner }; if ($null -ne $k.Notify) { $notifyOn = $k.Notify -eq 1 } } catch { }
$OVERLAY_W = 128; $TOAST_W = 380; $SH = 18

function Stop-App { Get-Process GamepadBatteryOverlay -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 600 }
# --mudo: os testes não tocam som no PC de quem está testando
# Os testes de janela rodam com o RivaTuner desligado: com um jogo em tela cheia exclusiva aberto, o programa
# (corretamente) esconde a janela e mostra pelo RivaTuner, o que faria esses testes falharem sem motivo.
# O RivaTuner tem os próprios testes (R1, R2, A15), que passam --rtss-modo explicitamente.
function Start-App([string[]]$a) {
    Stop-App
    $a = @($a | Where-Object { $_ })
    if ($a -notcontains '--rtss-modo') { $a += @('--rtss-modo', 'desligado') }
    Start-Process $exe -ArgumentList (@('--mudo') + $a) -PassThru
}
function Wins($p) {
    foreach ($w in [TestWin]::Windows($p.Id)) {
        $kind = if ($w[3] -eq $OVERLAY_W) { 'overlay' } elseif ($w[3] -eq $TOAST_W) { 'toast' } else { 'outra' }
        # para as notificações, o retângulo que conta é o do cartão (sem a margem da sombra)
        if ($kind -eq 'toast') { [pscustomobject]@{ H = $w[0]; Kind = $kind; X = $w[1] + $SH; Y = $w[2] + $SH; W = $w[3] - 2 * $SH; Ht = $w[4] - 2 * $SH } }
        else { [pscustomobject]@{ H = $w[0]; Kind = $kind; X = $w[1]; Y = $w[2]; W = $w[3]; Ht = $w[4] } }
    }
}
function Wait-Overlay($p, $ms) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) { $o = @(Wins $p | Where-Object Kind -eq 'overlay'); if ($o.Count) { return $o[0] }; Start-Sleep -Milliseconds 100 }
    return $null
}
# Observa as janelas por N segundos. Falha se duas ficarem sobrepostas por mais de 400 ms seguidos.
function Watch-Overlap($p, $seconds) {
    $first = @{}; $maxT = 0; $sw = [Diagnostics.Stopwatch]::StartNew(); $samples = 0
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        if ($p.HasExited) { return "o programa fechou sozinho (código $($p.ExitCode))" }
        $w = @(Wins $p | Where-Object { $_.Kind -ne 'outra' })
        $nT = @($w | Where-Object Kind -eq 'toast').Count; if ($nT -gt $maxT) { $maxT = $nT }
        $agora = @{}
        for ($i = 0; $i -lt $w.Count; $i++) { for ($j = $i + 1; $j -lt $w.Count; $j++) {
            $a = $w[$i]; $b = $w[$j]
            $ix = [Math]::Min($a.X + $a.W, $b.X + $b.W) - [Math]::Max($a.X, $b.X)
            $iy = [Math]::Min($a.Y + $a.Ht, $b.Y + $b.Ht) - [Math]::Max($a.Y, $b.Y)
            if ($ix -gt 2 -and $iy -gt 2) {
                $key = "$($a.H)-$($b.H)"; $agora[$key] = $true
                if (-not $first.ContainsKey($key)) { $first[$key] = $sw.ElapsedMilliseconds }
                elseif ($sw.ElapsedMilliseconds - $first[$key] -gt 400) { return "$($a.Kind) e $($b.Kind) sobrepostos por mais de 400 ms (${iy}px)" }
            }
        } }
        foreach ($k in @($first.Keys)) { if (-not $agora.ContainsKey($k)) { $first.Remove($k) } }
        $samples++
        Start-Sleep -Milliseconds 40
    }
    if ($maxT -gt 5) { return "$maxT notificações ao mesmo tempo (máx. 5: 4 + 1 saindo)" }
    return [pscustomobject]@{ MaxToasts = $maxT; Samples = $samples }
}
$temControle = $nLogicos -gt 0
$todosCarregando = -not @($xboxes | Where-Object { $_.Bluetooth -and $_.Percent -ge 0 }).Count
foreach ($d in $pads) {
    $r = [PadHid]::CaptureRaw($d, 20) | Where-Object { [PadHid]::IsFullReport($d, $_, $_.Length) } | Select-Object -Last 1
    if ($r) { $p = 0; $s = 0; [void][PadHid]::DecodeBattery($d.Model, $r[$d.BatteryIndex], [ref]$p, [ref]$s); if ($s -eq 0) { $todosCarregando = $false } }
}

if ($SemApp) { Res 'A*' 'Testes com o programa aberto' 'PULADO' 'opção -SemApp' }
else {
    $p = Start-App
    $ov = Wait-Overlay $p 6000
    # os controles conectam um de cada vez (PS por leitura HID, Xbox pela thread do Xbox): espera todos entrarem
    if ($ov) { Start-Sleep -Milliseconds 2000; $ov = Wait-Overlay $p 1000 }
    Check 'A1' 'Overlay aparece com uma linha por controle' {
        if (-not $temControle) { if ($ov) { return "overlay visível sem controle" } else { return 'PULADO: nenhum controle conectado' } }
        if (-not $ov) { return "overlay não apareceu em 6 s" }
        if ($ov.Ht -ne 32 * $nLogicos) { return "altura $($ov.Ht), esperado $(32 * $nLogicos) ($nLogicos controle(s))" }
        $true
    }
    Check 'A2' "Overlay no canto configurado ($corner)" {
        if (-not $ov) { return 'PULADO: overlay não visível' }
        $x = if ($corner.EndsWith('L')) { $wa.Left + 12 } else { $wa.Right - $OVERLAY_W - 12 }
        $y = if ($corner.StartsWith('B')) { $wa.Bottom - $ov.Ht - 12 } else { $wa.Top + 12 }
        if ([Math]::Abs($ov.X - $x) -gt 1 -or [Math]::Abs($ov.Y - $y) -gt 1) { return "em ($($ov.X),$($ov.Y)), esperado ($x,$y)" }
        $true
    }
    Check 'A3' 'Notificação de conexão não cobre o overlay (bug corrigido v1.1)' {
        if (-not $temControle) { return 'PULADO: nenhum controle' }
        if (-not $notifyOn) { return 'PULADO: notificações desligadas na bandeja' }
        $r = Watch-Overlap $p 3; if ($r -is [string]) { return $r }
        $true
    }
    Check 'A4' 'Só abre uma vez (segunda cópia fecha sozinha)' {
        $p2 = Start-Process $exe -PassThru
        if (-not $p2.WaitForExit(4000)) { $p2.Kill(); return "segunda cópia continuou aberta" }
        if (@(Get-Process GamepadBatteryOverlay).Count -ne 1) { return "há $(@(Get-Process GamepadBatteryOverlay).Count) cópias" }
        $true
    }
    Check 'A5' 'Uso de CPU desprezível (< 3% de um núcleo)' {
        $c0 = (Get-Process -Id $p.Id).CPU; Start-Sleep 10; $c1 = (Get-Process -Id $p.Id).CPU
        $pct = ($c1 - $c0) / 10 * 100
        if ($pct -ge 3) { return ("{0:N1}% de um núcleo" -f $pct) }
        $true
    }
    Check 'A6' 'Continua rodando (sem travar)' { if ($p.HasExited) { "fechou (código $($p.ExitCode))" } elseif (-not (Get-Process -Id $p.Id).Responding) { "não está respondendo" } else { $true } }

    $p = Start-App @('--teste-rajada')
    Check 'A7' 'Rajada de atalho + avisos: nada sobreposto, máx. 4+1 (bug corrigido v1.1)' {
        $r = Watch-Overlap $p 11; if ($r -is [string]) { return $r }
        if ($r.MaxToasts -lt 2) { return "só $($r.MaxToasts) notificação(ões) apareceram — a rajada rodou?" }
        $true
    }

    $p = Start-App @('--demo')
    Check 'A8' 'Testar notificações (demo): roda até o fim sem sobrepor' {
        $r = Watch-Overlap $p 15; if ($r -is [string]) { return $r }
        if ($r.MaxToasts -lt 1) { return "nenhuma notificação apareceu" }
        $true
    }

    $p = Start-App @('--teste-bateria-baixa')
    Check 'A9' 'Bateria < 10% aparece mesmo com overlay oculto' {
        if (-not $temControle) { return 'PULADO: nenhum controle' }
        if ($todosCarregando) { return 'PULADO: todos os controles estão carregando (a simulação só vale descarregando)' }
        $o = Wait-Overlay $p 6000
        if (-not $o) { return "overlay não apareceu (deveria ser obrigatório)" }
        Start-Sleep -Milliseconds 2000; $o = Wait-Overlay $p 1000
        $script:alturaA9 = if ($o) { $o.Ht } else { 0 }
        $true
    }

    Check 'A14' 'Versão antiga (DualSenseOverlay.exe) aberta junto é fechada, para não mostrar tudo em dobro' {
        Stop-App
        # simula a versão antiga: um processo chamado DualSenseOverlay + o "sinal" de instância dela
        $dir = Join-Path $env:TEMP ('gbo-antigo-' + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $dir | Out-Null
        $fake = Join-Path $dir 'DualSenseOverlay.exe'; Copy-Item "$env:WINDIR\System32\PING.EXE" $fake
        $velho = Start-Process $fake -ArgumentList '-n', '60', '127.0.0.1' -WindowStyle Hidden -PassThru
        $mtx = New-Object Threading.Mutex($true, 'DualSenseOverlay_SingleInstance')
        try {
            $novo = Start-Process $exe -ArgumentList '--mudo' -PassThru
            if (-not $velho.WaitForExit(6000)) { $velho.Kill(); return "a versão antiga continuou aberta" }
            if ($novo.HasExited) { return "o programa novo fechou junto" }
            $true
        }
        finally { $mtx.ReleaseMutex(); $mtx.Dispose(); Start-Sleep -Milliseconds 300; Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }

    Check 'A15' 'Programa escreve a bateria no RivaTuner (simulado, modo "sempre") com o mesmo conteúdo do overlay' {
        $name = 'GBO_TesteRTSS_' + [Guid]::NewGuid().ToString('N')
        $mmf, $acc = New-FakeRtss $name
        try {
            $px = Start-App @('--rtss-mapa', $name, '--rtss-modo', 'sempre')
            Start-Sleep -Milliseconds 4500
            $dono = Get-RtssSlot $acc 1 256 256; $txt = Get-RtssSlot $acc 1 512 4096
            if ($dono -ne 'GamepadBatteryOverlay') { return "o programa não ocupou um espaço no RivaTuner (dono='$dono')" }
            if ($temControle -and $txt -notmatch '(PS5|PS4|XBOX)') { return "texto sem os controles conectados: '$txt'" }
            if (-not $temControle -and $txt -ne '') { return "sem controle, o texto deveria ficar vazio: '$txt'" }
            $true
        }
        finally { Stop-App; $acc.Dispose(); $mmf.Dispose() }
    }

    Check 'A16' 'Aberto pelo Windows (--inicio-windows): avisa que está ativo e anota no registro de inicializações' {
        $log = Join-Path $env:LOCALAPPDATA 'GamepadBatteryOverlay\inicializacoes.log'
        $antes = if (Test-Path $log) { @(Get-Content $log).Count } else { 0 }
        $px = Start-App @('--inicio-windows')
        $r = Watch-Overlap $px 7; if ($r -is [string]) { return $r }
        if ($notifyOn -and $r.MaxToasts -lt 1) { return "o aviso de que o programa abriu com o Windows não apareceu" }
        $ult = @(Get-Content $log) | Select-Object -Last 1
        if ($ult -notmatch 'aberto pelo Windows') { return "registro de inicializações não anotou: '$ult'" }
        $true
    }

    Check 'A17' 'Aviso de "Carga completa" aparece mesmo com o overlay oculto' {
        if (-not $notifyOn) { return 'PULADO: notificações desligadas na bandeja' }
        $px = Start-App @('--teste-carga-completa')
        # o DualSense simulado conecta (carregando) em ~1,5 s e completa a carga em ~3,5 s; o aviso "conectado" é
        # substituído pelo "Carga completa" (mesmo grupo) e dura uns 4 s: aos 6,5 s o cartão na tela só pode ser ele
        Start-Sleep -Milliseconds 6500
        $t = @(Wins $px | Where-Object Kind -eq 'toast')
        if ($t.Count -lt 1) { return "nenhum aviso na tela depois de completar a carga" }
        $true
    }

    $p = Start-App @('--demo', '--idioma', 'en')
    Check 'A13' 'Notificações em inglês (--idioma en): rodam sem sobrepor e sem salvar o idioma' {
        $antes = (Get-ItemProperty 'HKCU:\Software\GamepadBatteryOverlay' -ErrorAction SilentlyContinue).Language
        $r = Watch-Overlap $p 6; if ($r -is [string]) { return $r }
        if ($r.MaxToasts -lt 1) { return "nenhuma notificação apareceu" }
        $depois = (Get-ItemProperty 'HKCU:\Software\GamepadBatteryOverlay' -ErrorAction SilentlyContinue).Language
        if ("$antes" -ne "$depois") { return "o --idioma gravou no registro ('$antes' -> '$depois')" }
        $true
    }

    $p = Start-App @('--teste-xbox-usb')
    Check 'A11' 'Xbox no cabo (simulado): linha extra "USB mode" no overlay' {
        Start-Sleep -Milliseconds 3500
        $o = Wait-Overlay $p 6000
        if (-not $o) { return "overlay não apareceu" }
        if ($o.Ht -ne 32 * ($nLogicos + 1)) { return "altura $($o.Ht), esperado $(32 * ($nLogicos + 1)) ($nLogicos controle(s) + o Xbox simulado)" }
        $true
    }

    $p = Start-App @('--teste-xbox-usb', '--teste-bateria-baixa')
    Check 'A12' 'Xbox no cabo (sem bateria) não entra no alerta obrigatório de bateria baixa' {
        if ($null -eq $script:alturaA9) { return 'PULADO: o A9 não rodou' }
        # Espera os controles entrarem (o DualSense no Bluetooth às vezes demora alguns segundos para mandar a 1ª
        # leitura depois que o programa anterior foi fechado) e confere a altura até 9 s, sem depender de um instante só.
        # Depois de bater a altura esperada, confere de novo 2 s depois: o Xbox simulado (sem bateria) não pode aparecer.
        Start-Sleep -Milliseconds 2500
        $sw = [Diagnostics.Stopwatch]::StartNew(); $h = -1
        while ($sw.ElapsedMilliseconds -lt 9000) {
            $o = @(Wins $p | Where-Object Kind -eq 'overlay'); $h = if ($o.Count) { $o[0].Ht } else { 0 }
            if ($h -eq $script:alturaA9) { break }
            Start-Sleep -Milliseconds 200
        }
        if ($h -ne $script:alturaA9) { return "altura $h, esperado $($script:alturaA9) (igual ao A9: o USB mode não pode ser forçado)" }
        Start-Sleep -Milliseconds 2000
        $o = @(Wins $p | Where-Object Kind -eq 'overlay'); $h = if ($o.Count) { $o[0].Ht } else { 0 }
        if ($h -ne $script:alturaA9) { return "depois de 2 s a altura virou $h (esperado $($script:alturaA9)): o USB mode entrou no alerta?" }
        $true
    }
    Stop-App

    Check 'A10' 'Exportar sons (--exportar-sons) gera os 8 arquivos .wav' {
        $dir = Join-Path $env:TEMP ("dso-sons-" + [Guid]::NewGuid().ToString('N'))
        $px = Start-Process $exe -ArgumentList @('--exportar-sons', "`"$dir`"") -PassThru
        if (-not $px.WaitForExit(10000)) { $px.Kill(); return "não terminou em 10 s" }
        $n = @(Get-ChildItem $dir -Filter *.wav -ErrorAction SilentlyContinue).Count
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
        if ($n -ne [Enum]::GetValues([Sfx]).Length) { return "gerou $n arquivo(s)" }
        $true
    }
}
# a compilação fecha o programa: deixa ele aberto no final, em qualquer modo
if (-not (Get-Process GamepadBatteryOverlay -ErrorAction SilentlyContinue)) { Start-Process $exe | Out-Null }

# ---------------------------------------------------------------------------------------------
Secao "5. Documentação"
# ---------------------------------------------------------------------------------------------
$ver = ([regex]::Match((Get-Content "$root\src\AssemblyInfo.cs" -Raw), 'Version = "([\d.]+)"')).Groups[1].Value
Check 'D1' "Versão $ver igual em Leia-me, ReadMe, README, CHANGELOG e modelo de issue" {
    $erros = @()
    if ((Get-Content "$root\Leia-me.txt" -Raw -Encoding UTF8) -notmatch "OVERLAY\s+-\s+v$([regex]::Escape($ver))\b") { $erros += 'Leia-me.txt' }
    if ((Get-Content "$root\ReadMe.txt" -Raw -Encoding UTF8) -notmatch "OVERLAY\s+-\s+v$([regex]::Escape($ver))\b") { $erros += 'ReadMe.txt' }
    if ((Get-Content "$root\README.md" -Raw -Encoding UTF8) -notmatch "vers%C3%A3o-$([regex]::Escape($ver))-") { $erros += 'README.md' }
    $cl = [regex]::Match((Get-Content "$root\CHANGELOG.md" -Raw -Encoding UTF8), '## \[([\d.]+)\]').Groups[1].Value
    if ($cl -ne $ver) { $erros += "CHANGELOG.md ($cl)" }
    if ((Get-Content "$root\.github\ISSUE_TEMPLATE\relato-de-teste.md" -Raw -Encoding UTF8) -notmatch "programa:\*\* $([regex]::Escape($ver))") { $erros += 'modelo de issue' }
    if ($erros) { return "desatualizado: $($erros -join ', ')" }
    $true
}
Check 'D2' 'Arquivos obrigatórios do repositório presentes' {
    $faltam = @('README.md', 'Leia-me.txt', 'ReadMe.txt', 'LICENSE', 'CHANGELOG.md', '.gitignore', 'compilar.bat', 'docs\PROTOCOLO-DE-TESTES.md', 'assets\icone.ico', 'tools\gerar-icone.ps1') | Where-Object { -not (Test-Path (Join-Path $root $_)) }
    if ($faltam) { return "faltam: $($faltam -join ', ')" }
    $true
}

# ---------------------------------------------------------------------------------------------
$ok = @($results | Where-Object Resultado -eq 'OK').Count
$falhou = @($results | Where-Object Resultado -eq 'FALHOU').Count
$pulado = @($results | Where-Object Resultado -eq 'PULADO').Count
Write-Host ""
$resumo = "RESUMO: $ok OK, $falhou FALHOU, $pulado PULADO  ({0:N0} s)" -f ((Get-Date) - $inicio).TotalSeconds
Write-Host $resumo -ForegroundColor $(if ($falhou) { 'Red' } else { 'Green' })

$txt = @("Suíte de testes — Gamepad Battery Overlay v$ver", ("Data: {0:dd/MM/yyyy HH:mm}" -f (Get-Date)), "Controles: $($pads.Count) dispositivo(s), $nLogicos controle(s)", "")
$txt += $results | ForEach-Object { "[{0,-6}] {1,-4} {2}{3}" -f $_.Resultado, $_.ID, $_.Teste, $(if ($_.Detalhe) { "  -> $($_.Detalhe)" } else { '' }) }
$txt += ""; $txt += $resumo
$txt | Out-File (Join-Path $PSScriptRoot 'ultimo-resultado.txt') -Encoding utf8
if ($falhou) { exit 1 } else { exit 0 }
