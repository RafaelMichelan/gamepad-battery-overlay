<#
  Gera o ícone do programa (assets\icone.ico) a partir da arte original.
  Uso (na pasta do projeto):
    powershell -STA -ExecutionPolicy Bypass -File tools\gerar-icone.ps1 -Arte <imagem .png/.webp/.jpg>
  Sem -Arte, usa assets\icone-arte.png.

  O que faz:
   1. lê a imagem pelo decodificador do próprio Windows (WIC: aceita PNG, JPG e WebP no Windows 11);
   2. salva uma cópia em assets\icone-arte.png (a arte original, quadrada);
   3. recorta o círculo: o fundo de fora do anel fica transparente, com borda suave;
   4. monta o .ico com 16, 20, 24, 32, 40, 48, 64, 128 e 256 px (cada tamanho em PNG dentro do .ico).
  O compilar.bat embute o assets\icone.ico no .exe.
#>
param([string]$Arte, [double]$Raio = 0.497)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$arteSalva = Join-Path $assets 'icone-arte.png'
if (-not $Arte) { $Arte = $arteSalva }
$Arte = (Resolve-Path $Arte).Path
Add-Type -AssemblyName System.Drawing, PresentationCore, WindowsBase

# 1-2) qualquer formato que o Windows leia -> PNG
if ($Arte -ne $arteSalva) {
    $dec = [Windows.Media.Imaging.BitmapDecoder]::Create([Uri]$Arte, [Windows.Media.Imaging.BitmapCreateOptions]::None, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add($dec.Frames[0])
    $fs = [IO.File]::Create($arteSalva); $enc.Save($fs); $fs.Close()
}
$src = New-Object Drawing.Bitmap $arteSalva

# 3) recorte redondo
$n = [Math]::Min($src.Width, $src.Height)
$cut = New-Object Drawing.Bitmap $n, $n, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$cx = $n / 2.0; $r = $n * $Raio
$rectS = New-Object Drawing.Rectangle 0, 0, $n, $n
$ds = $src.Clone($rectS, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bdS = $ds.LockBits($rectS, 'ReadOnly', [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bdD = $cut.LockBits($rectS, 'WriteOnly', [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($bdS.Stride * $n)
[Runtime.InteropServices.Marshal]::Copy($bdS.Scan0, $bytes, 0, $bytes.Length)
for ($y = 0; $y -lt $n; $y++) {
    $dy = $y + 0.5 - $cx
    for ($x = 0; $x -lt $n; $x++) {
        $dx = $x + 0.5 - $cx
        $d = [Math]::Sqrt($dx * $dx + $dy * $dy)
        $a = [Math]::Max(0.0, [Math]::Min(1.0, ($r - $d) / 2.0 + 0.5))
        $i = $y * $bdS.Stride + $x * 4 + 3
        $bytes[$i] = [byte]([Math]::Round($bytes[$i] * $a))
    }
}
[Runtime.InteropServices.Marshal]::Copy($bytes, 0, $bdD.Scan0, $bytes.Length)
$ds.UnlockBits($bdS); $cut.UnlockBits($bdD)

# 4) .ico com vários tamanhos (entradas PNG)
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($b)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g.DrawImage($cut, 0, 0, $s, $s); $g.Dispose()
    $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    , $ms.ToArray()
}
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($k = 0; $k -lt $sizes.Count; $k++) {
    $s = $sizes[$k]; $len = $pngs[$k].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))  # 256 vira 0, como manda o formato
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset); $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'icone.ico'), $out.ToArray())
$cut.Save((Join-Path $assets 'icone-256.png'), [Drawing.Imaging.ImageFormat]::Png) | Out-Null
"OK: assets\icone.ico ($($sizes -join ', ') px, $([Math]::Round($out.Length / 1KB)) KB)"
