# Генерує src\Keybrd\Assets\app.ico: синій квадрат із заокругленнями і стилізованою клавіатурою.
# Запуск у PowerShell 7: pwsh -File tools\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\Keybrd\Assets\app.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function RoundRect([System.Drawing.Graphics]$g, [System.Drawing.Brush]$b, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(0.5, $r * 2)
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $g.FillPath($b, $p); $p.Dispose()
}

function Render([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)

    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))
    $key = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $hot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 76, 194, 255))
    $pad = $s * 0.04
    RoundRect $g $bg $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.2)

    # Три ряди клавіш і пробіл. Одна клавіша — «наступна», світло-блакитна.
    $cols = if ($s -le 24) { 4 } else { 5 }
    $left = $s * 0.17; $width = $s * 0.66; $gap = $s * 0.045
    $kw = ($width - $gap * ($cols - 1)) / $cols; $kh = $s * 0.12
    $top = $s * 0.24
    for ($r = 0; $r -lt 3; $r++) {
        for ($c = 0; $c -lt $cols; $c++) {
            $b = if ($r -eq 1 -and $c -eq 1) { $hot } else { $key }
            RoundRect $g $b ($left + $c * ($kw + $gap)) ($top + $r * ($kh + $gap)) $kw $kh ($s * 0.025)
        }
    }
    $sy = $top + 3 * ($kh + $gap)
    RoundRect $g $key ($left + $kw + $gap) $sy ($width - 2 * ($kw + $gap)) $kh ($s * 0.025)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    ,$ms.ToArray()
}

# ICO з PNG-кадрами (підтримується з Windows Vista)
$fs = [System.IO.File]::Create($out); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

# Прев'ю для перегляду
$prev = Render 256; $prev.Save((Join-Path (Split-Path $out) 'app-256.png')); $prev.Dispose()
Write-Host "Готово: $out"
