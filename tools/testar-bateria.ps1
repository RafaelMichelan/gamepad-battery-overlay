<#
  Diagnóstico da leitura de bateria (DualSense / DualSense Edge / DualShock 4).

  .\testar-bateria.ps1                          -> Verificar: confere CRC e alinhamento de todos os controles
  .\testar-bateria.ps1 -Modo AoVivo             -> bytes em tempo real (mexa nos analógicos, ligue o cabo...)
  .\testar-bateria.ps1 -Modo AoVivo -Controle PS4  -> escolhe o controle quando houver mais de um
  .\testar-bateria.ps1 -Modo Log                -> grava a bateria de todos os controles num CSV a cada minuto
#>
param(
    [ValidateSet('Verificar', 'AoVivo', 'Log')] [string]$Modo = 'Verificar',
    [ValidateSet('', 'PS4', 'PS5')] [string]$Controle = '',
    [int]$IntervaloSeg = 60
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot '..\src\PadHid.cs')

function Get-Pads {
    $pads = @([PadHid]::Enumerate())
    if ($Controle) { $pads = @($pads | Where-Object { $_.Tag -eq $Controle }) }
    if ($pads.Count -eq 0) { Write-Host "Nenhum controle encontrado." -ForegroundColor Red; exit 1 }
    return $pads
}

function Get-Stamp($d, $r) {
    if ($d.IsDS4) { return [BitConverter]::ToUInt16($r, $d.TimestampIndex) }
    return [BitConverter]::ToUInt32($r, $d.TimestampIndex)
}

function Decode($d, [byte]$st) {
    $pct = 0; $status = 0
    $ok = [PadHid]::DecodeBattery($d.Model, $st, [ref]$pct, [ref]$status)
    $txt = if (-not $ok) { 'ERRO' } else { switch ($status) { 0 { 'descarregando' } 1 { 'carregando' } 2 { 'carga completa' } } }
    [pscustomobject]@{ Raw = ('0x{0:X2}' -f $st); Nivel = ($st -band 0x0F); Estado = $txt; Pct = $pct }
}

switch ($Modo) {
'Verificar' {
    $geral = $true
    foreach ($d in (Get-Pads)) {
        Write-Host ""
        Write-Host ("=== {0} (PID {1:X4}) via {2} ===" -f $d.Name, $d.Pid, $(if ($d.Bluetooth) { 'Bluetooth' } else { 'USB' })) -ForegroundColor Cyan
        Write-Host "Capturando 500 relatorios... (deixe o controle parado, sem tocar)"
        $reps = [PadHid]::CaptureRaw($d, 500)
        $full = @($reps | Where-Object { [PadHid]::IsFullReport($d, $_, $_.Length) })
        $ts = @($full | ForEach-Object { Get-Stamp $d $_ })
        $mudou = 0; for ($i = 1; $i -lt $ts.Count; $i++) { if ($ts[$i] -ne $ts[$i - 1]) { $mudou++ } }
        $okTs = $full.Count -gt 1 -and $mudou -ge ($full.Count - 2)
        $okFull = $full.Count -ge ($reps.Count * 0.9) -and $full.Count -gt 0

        Write-Host ("Relatorios lidos:            {0}  (buffer {1} bytes, esperado ID 0x{2:X2})" -f $reps.Count, $d.InLen, $d.ReportId)
        $txtCrc = if ($d.Bluetooth) { "completos e com CRC32 valido" } else { "completos" }
        Write-Host ("Relatorios {0}: {1} de {2}" -f $txtCrc, $full.Count, $reps.Count) -ForegroundColor $(if ($okFull) { 'Green' } else { 'Red' })
        Write-Host ("Relogio interno do controle: mudou em {0} de {1}" -f $mudou, [Math]::Max(0, $full.Count - 1)) -ForegroundColor $(if ($okTs) { 'Green' } else { 'Red' })
        $nomes = 'LX', 'LY', 'RX', 'RY'
        for ($k = 0; $k -lt 4; $k++) {
            $m = $full | ForEach-Object { $_[$d.Base + $k] } | Measure-Object -Average -Minimum -Maximum
            Write-Host ("Analogico {0}: media {1,5:N1}  (min {2}, max {3})  -> parado fica perto de 128" -f $nomes[$k], $m.Average, $m.Minimum, $m.Maximum)
        }
        $bat = @($full | ForEach-Object { $_[$d.BatteryIndex] } | Group-Object | Sort-Object Count -Descending)
        Write-Host ("Byte de bateria (posicao {0}):" -f $d.BatteryIndex)
        foreach ($g in $bat) { $x = Decode $d ([byte]$g.Name); Write-Host ("  {0}  nivel {1} -> ~{2}%  {3}   (em {4} relatorios)" -f $x.Raw, $x.Nivel, $x.Pct, $x.Estado, $g.Count) }
        if ($okFull -and $okTs -and $bat.Count -eq 1) { Write-Host "OK: relatorios integros e bateria estavel." -ForegroundColor Green }
        else { Write-Host "Atencao: algo nao bateu (veja acima)." -ForegroundColor Yellow; $geral = $false }
    }
    if (-not $geral) { exit 2 }
}
'AoVivo' {
    $d = (Get-Pads)[0]
    Write-Host ("{0} via {1}. Ctrl+C para sair." -f $d.Name, $(if ($d.Bluetooth) { 'Bluetooth' } else { 'USB' })) -ForegroundColor Cyan
    Write-Host ("Mexa nos analogicos/botoes: so eles mudam; a bateria fica fixa. Aperte {0} para ver o atalho." -f $d.ToggleName) -ForegroundColor Cyan
    while ($true) {
        $r = [PadHid]::CaptureRaw($d, 30) | Where-Object { [PadHid]::IsFullReport($d, $_, $_.Length) } | Select-Object -Last 1
        if ($null -eq $r) { Write-Host "`rsem relatorio completo...                        " -NoNewline; Start-Sleep 1; continue }
        $x = Decode $d $r[$d.BatteryIndex]
        $b = $d.Base
        $line = "LX {0,3} LY {1,3} RX {2,3} RY {3,3} | {4} {5} | bateria {6} = ~{7}% {8}" -f `
            $r[$b], $r[$b + 1], $r[$b + 2], $r[$b + 3], $d.ToggleName, $(if ([PadHid]::TogglePressed($d, $r)) { 'SIM' } else { 'nao' }), $x.Raw, $x.Pct, $x.Estado
        Write-Host ("`r" + $line.PadRight(110)) -NoNewline
        Start-Sleep -Milliseconds 300
    }
}
'Log' {
    $csv = Join-Path $PSScriptRoot ("log-bateria-{0:yyyyMMdd-HHmm}.csv" -f (Get-Date))
    "hora;controle;conexao;raw;nivel;pct;estado" | Out-File $csv -Encoding utf8
    Write-Host "Gravando em $csv a cada $IntervaloSeg s. Ctrl+C para parar." -ForegroundColor Cyan
    $inicio = Get-Date
    while ($true) {
        $pads = @([PadHid]::Enumerate())
        if ($Controle) { $pads = @($pads | Where-Object { $_.Tag -eq $Controle }) }
        if ($pads.Count -eq 0) { Write-Host ("{0:HH:mm:ss}  nenhum controle" -f (Get-Date)) -ForegroundColor Yellow }
        foreach ($d in $pads) {
            $r = [PadHid]::CaptureRaw($d, 30) | Where-Object { [PadHid]::IsFullReport($d, $_, $_.Length) } | Select-Object -Last 1
            if (-not $r) { continue }
            $x = Decode $d $r[$d.BatteryIndex]
            $conn = if ($d.Bluetooth) { 'Bluetooth' } else { 'USB' }
            "{0:HH:mm:ss};{1};{2};{3};{4};{5};{6}" -f (Get-Date), $d.Tag, $conn, $x.Raw, $x.Nivel, $x.Pct, $x.Estado | Out-File $csv -Append -Encoding utf8
            Write-Host ("{0:HH:mm:ss}  (+{1:N0} min)  {2}  {3}  ~{4}%  {5}" -f (Get-Date), ((Get-Date) - $inicio).TotalMinutes, $d.Tag, $x.Raw, $x.Pct, $x.Estado)
        }
        Start-Sleep $IntervaloSeg
    }
}
}
