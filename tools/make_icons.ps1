# Generate assets/icons/*.png (and assets/app.ico) from Microsoft's Fluent UI System Icons (MIT).
# The font is downloaded to a cache folder and is NOT committed; only the generated PNGs are.
# Naming: <name>.png is the 100% size, <name>200.png the 200% size. Strips (nav_*, tool_arrow) hold
# 4 states side by side: normal, hot, pushed, disabled.
# Usage: powershell -File tools\make_icons.ps1 [-FontDir <cache>] [-Sheet preview.png]
# ASCII only (Windows PowerShell 5.1 reads scripts as ANSI).
param([string]$FontDir = (Join-Path $env:TEMP 'fluent-system-icons'), [string]$Sheet = '')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root 'assets\icons'
New-Item -ItemType Directory -Force -Path $out | Out-Null
New-Item -ItemType Directory -Force -Path $FontDir | Out-Null

$base = 'https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main'
foreach ($f in 'FluentSystemIcons-Regular.ttf', 'FluentSystemIcons-Regular.json', 'FluentSystemIcons-Filled.ttf', 'FluentSystemIcons-Filled.json') {
    $p = Join-Path $FontDir $f
    if (-not (Test-Path $p)) { Write-Output "downloading $f"; Invoke-WebRequest -Uri "$base/fonts/$f" -OutFile $p -UseBasicParsing -TimeoutSec 180 }
}

$fonts = New-Object Drawing.Text.PrivateFontCollection
$fonts.AddFontFile((Join-Path $FontDir 'FluentSystemIcons-Regular.ttf'))
$fonts.AddFontFile((Join-Path $FontDir 'FluentSystemIcons-Filled.ttf'))
$famRegular = $fonts.Families | Where-Object { $_.Name -match 'Regular' } | Select-Object -First 1
$famFilled = $fonts.Families | Where-Object { $_.Name -match 'Filled' } | Select-Object -First 1
if (-not $famRegular -or -not $famFilled) { throw "font families not found: $(($fonts.Families | ForEach-Object Name) -join ', ')" }
$mapRegular = Get-Content (Join-Path $FontDir 'FluentSystemIcons-Regular.json') -Raw | ConvertFrom-Json
$mapFilled = Get-Content (Join-Path $FontDir 'FluentSystemIcons-Filled.json') -Raw | ConvertFrom-Json

function Glyph([string]$name, [string]$style, [int]$design) {
    $map = if ($style -eq 'f') { $mapFilled } else { $mapRegular }
    $suffix = if ($style -eq 'f') { 'filled' } else { 'regular' }
    foreach ($d in @($design, 16, 20, 24)) {
        $key = "ic_fluent_${name}_${d}_$suffix"
        $cp = $map.$key
        if ($cp) { return [char]::ConvertFromUtf32([int]$cp) }
    }
    throw "glyph not found: $name ($style)"
}

function Color([string]$hex) {
    $h = $hex.TrimStart('#')
    if ($h.Length -eq 6) { $h = 'FF' + $h }
    return [Drawing.Color]::FromArgb([Convert]::ToInt32($h, 16))
}

# Draw one glyph centred in (x, y, size, size); scale < 1 shrinks it inside the cell.
function DrawGlyph($g, [string]$text, $family, [int]$x, [int]$y, [int]$size, $brush, [double]$scale = 1.0) {
    $em = [single]($size * $scale)
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddString($text, $family, 0, $em, (New-Object Drawing.PointF 0, 0), [Drawing.StringFormat]::GenericTypographic)
    # Fluent glyphs are drawn on an em-square; place that square in the cell (ascent = top of the em box)
    $asc = $family.GetCellAscent(0) / [double]$family.GetEmHeight(0) * $em
    $m = New-Object Drawing.Drawing2D.Matrix
    $m.Translate([single]($x + ($size - $em) / 2), [single]($y + ($size - $em) / 2 - ($asc - $em)))
    $path.Transform($m)
    $g.FillPath($brush, $path)
    $path.Dispose(); $m.Dispose()
}

function NewCanvas([int]$w, [int]$h) {
    $b = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)
    return @($b, $g)
}

function SaveBoth([string]$name, [scriptblock]$paint, [int]$w, [int]$h) {
    foreach ($k in 1, 2) {
        $bg = NewCanvas ($w * $k) ($h * $k)
        & $paint $bg[1] $k
        $file = if ($k -eq 1) { "$name.png" } else { "${name}200.png" }
        $bg[0].Save((Join-Path $out $file), [Drawing.Imaging.ImageFormat]::Png)
        $bg[1].Dispose(); $bg[0].Dispose()
    }
}

# ---------- single icons: name = glyph, style, colour, scale ----------
$icons = [ordered]@{
    'tab_add_fore'     = @('add', 'r', '#333333', 0.9)
    'tab_close_fore'   = @('dismiss', 'r', '#555555', 0.7)
    'list_item_close'  = @('dismiss', 'r', '#777777', 0.7)
    'close_fore'       = @('dismiss', 'r', '#333333', 0.8)
    'close_fore_white' = @('dismiss', 'r', '#FFFFFF', 0.8)
    'min_fore'         = @('subtract', 'r', '#333333', 0.8)
    'max_fore'         = @('square', 'r', '#333333', 0.75)
    'restore_fore'     = @('square_multiple', 'r', '#333333', 0.75)
    'menu_fore'        = @('line_horizontal_3', 'r', '#333333', 0.85)
    'favorite_icon'    = @('star', 'f', '#F7B500', 1.0)
    'favorite_more'    = @('chevron_double_right', 'r', '#555555', 0.8)
    'addr_refresh'     = @('arrow_clockwise', 'r', '#555555', 0.85)
    'edit_search'      = @('search', 'r', '#555555', 0.9)
    'menu_shortcut'    = @('desktop', 'r', '#444444', 1.0)
    'menu_sysexplorer' = @('folder', 'r', '#444444', 1.0)
    'menu_update'      = @('flash', 'r', '#444444', 1.0)
    'menu_exit'        = @('sign_out', 'r', '#444444', 1.0)
    'option_navigate'  = @('panel_left', 'r', '#444444', 1.0)
    'option_detail'    = @('info', 'r', '#444444', 1.0)
    'option_preview'   = @('eye', 'r', '#444444', 1.0)
    'option_check'     = @('checkbox_checked', 'r', '#444444', 1.0)
    'option_extension' = @('document_text', 'r', '#444444', 1.0)
    'option_hidden'    = @('eye_off', 'r', '#444444', 1.0)
    'view_largeex'     = @('square', 'r', '#444444', 1.0)
    'view_large'       = @('grid', 'r', '#444444', 1.0)
    'view_medium'      = @('grid_dots', 'r', '#444444', 1.0)
    'view_small'       = @('apps_list', 'r', '#444444', 1.0)
    'view_list'        = @('list', 'r', '#444444', 1.0)
    'view_detail'      = @('table', 'r', '#444444', 1.0)
    'view_tile'        = @('apps', 'r', '#444444', 1.0)
    'view_content'     = @('content_view', 'r', '#444444', 1.0)
}
# toolbar buttons get a normal and a _disabled variant
$tools = [ordered]@{
    'tool_new' = 'add_circle'; 'tool_view' = 'text_bullet_list_ltr'; 'tool_copy' = 'copy'; 'tool_cut' = 'cut'
    'tool_paste' = 'clipboard_paste'; 'tool_rename' = 'rename'; 'tool_delete' = 'delete'
}
foreach ($n in $tools.Keys) {
    $icons[$n] = @($tools[$n], 'r', '#333333', 1.0)
    $icons["${n}_disabled"] = @($tools[$n], 'r', '#BBBBBB', 1.0)
}

foreach ($n in $icons.Keys) {
    $spec = $icons[$n]
    $fam = if ($spec[1] -eq 'f') { $famFilled } else { $famRegular }
    $text = Glyph $spec[0] $spec[1] 16
    $brush = New-Object Drawing.SolidBrush (Color $spec[2])
    SaveBoth $n { param($g, $k) DrawGlyph $g $text $fam 0 0 (16 * $k) $brush $spec[3] }.GetNewClosure() 16 16
    $brush.Dispose()
}

# ---------- 4-state strips ----------
$stateColors = @('#555555', '#0067C0', '#003E92', '#C8C8C8')
function Strip([string]$name, [string]$glyphName, [int]$cell, [double]$scale) {
    $text = Glyph $glyphName 'r' 16
    $fam = $famRegular
    SaveBoth $name {
        param($g, $k)
        for ($i = 0; $i -lt 4; $i++) {
            $b = New-Object Drawing.SolidBrush (Color $stateColors[$i])
            DrawGlyph $g $text $fam ($i * $cell * $k) 0 ($cell * $k) $b $scale
            $b.Dispose()
        }
    }.GetNewClosure() ($cell * 4) $cell
}
Strip 'nav_back' 'arrow_left' 16 0.95
Strip 'nav_forward' 'arrow_right' 16 0.95
Strip 'nav_more' 'chevron_down' 16 0.8
Strip 'nav_up' 'arrow_up' 16 0.95
Strip 'tool_arrow' 'chevron_down' 8 1.0
# 8 px chevron used between breadcrumb segments
$chev = Glyph 'chevron_right' 'r' 16
SaveBoth 'addr_btn_arrow' { param($g, $k) $b = New-Object Drawing.SolidBrush (Color '#555555'); DrawGlyph $g $chev $famRegular 0 0 (8 * $k) $b 1.0; $b.Dispose() }.GetNewClosure() 8 8

# ---------- backgrounds (drawn shapes, no glyphs) ----------
function RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r, [bool]$bottomSquare = $false) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    if ($bottomSquare) { $p.AddLine($x + $w, $y + $h, $x, $y + $h) }
    else { $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90) }
    $p.CloseFigure()
    return $p
}
# hover / pressed backgrounds for small buttons (stretched as a 9-slice, corners 5 px)
foreach ($pair in @(@('tab_add_hot', '#0F000000'), @('tab_add_pushed', '#1F000000'))) {
    $hex = $pair[1]
    SaveBoth $pair[0] { param($g, $k) $b = New-Object Drawing.SolidBrush (Color $hex); $p = RoundRect 0 0 (24 * $k) (24 * $k) (4 * $k); $g.FillPath($b, $p); $p.Dispose(); $b.Dispose() }.GetNewClosure() 24 24
}
# tab close button hover / pressed: round
foreach ($pair in @(@('tab_close_hot', '#1A000000'), @('tab_close_pushed', '#33000000'))) {
    $hex = $pair[1]
    SaveBoth $pair[0] { param($g, $k) $b = New-Object Drawing.SolidBrush (Color $hex); $g.FillEllipse($b, 0, 0, 16 * $k, 16 * $k); $b.Dispose() }.GetNewClosure() 16 16
}
# tabs: 40x30, stretched with corners left 12 / top 8 / right 12 / bottom 4 (see TitleBar.DrawTab)
foreach ($pair in @(@('tab_normal', '#00FFFFFF'), @('tab_hot', '#80FFFFFF'), @('tab_pushed', '#FFFFFFFF'))) {
    $hex = $pair[1]
    SaveBoth $pair[0] { param($g, $k) $b = New-Object Drawing.SolidBrush (Color $hex); $p = RoundRect (4 * $k) 0 (32 * $k) (30 * $k) (6 * $k) $true; $g.FillPath($b, $p); $p.Dispose(); $b.Dispose() }.GetNewClosure() 40 30
}

# ---------- application icon: filled folder with a blue gradient, PNG-compressed ICO ----------
$folder = Glyph 'folder' 'f' 24
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bg = NewCanvas $s $s
    $rect = New-Object Drawing.RectangleF 0, 0, $s, $s
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, (Color '#5AA8FF'), (Color '#1E63D6'), 90.0
    DrawGlyph $bg[1] $folder $famFilled 0 0 $s $grad 1.0
    $grad.Dispose(); $bg[1].Dispose()
    if ($s -ge 256) {
        # 256 px: PNG-compressed (standard for Vista+ icons)
        $ms = New-Object IO.MemoryStream
        $bg[0].Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bg[0].Dispose()
        , $ms.ToArray()
    } else {
        # smaller sizes: classic 32-bit DIB (BITMAPINFOHEADER, bottom-up BGRA, then a 1-bit AND mask);
        # older readers (incl. .NET Framework System.Drawing) cannot decode PNG frames
        $bmp = $bg[0]
        $maskStride = [int][Math]::Ceiling($s / 32.0) * 4
        $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
        $w.Write([int32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int32]0); $w.Write([int32]($s * $s * 4 + $maskStride * $s)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([int32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
        $w.Write((New-Object byte[] ($maskStride * $s)))   # all-zero AND mask: alpha channel decides
        $w.Flush(); $bmp.Dispose()
        , $ms.ToArray()
    }
}
$ico = Join-Path $root 'assets\app.ico'
$fs = [IO.File]::Create($ico); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $wh = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$wh); $bw.Write([byte]$wh); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()

$count = (Get-ChildItem $out -Filter *.png).Count
Write-Output "generated $count png in $out, app.ico with $($sizes.Count) sizes"

# ---------- optional preview sheet (200% images on white and on the title-bar colour) ----------
if ($Sheet) {
    $files = Get-ChildItem $out -Filter '*200.png' | Sort-Object Name
    $cols = 8; $cw = 150; $ch = 70
    $rows = [Math]::Ceiling($files.Count / $cols)
    $bmp = New-Object Drawing.Bitmap ($cols * $cw), ($rows * $ch + 270)
    $g = [Drawing.Graphics]::FromImage($bmp); $g.Clear([Drawing.Color]::White)
    $font = New-Object Drawing.Font 'Segoe UI', 8
    $frame = New-Object Drawing.SolidBrush (Color '#DFE8F1')
    for ($i = 0; $i -lt $files.Count; $i++) {
        $x = ($i % $cols) * $cw; $y = [Math]::Floor($i / $cols) * $ch
        $g.FillRectangle($frame, $x + 2, $y + 2, $cw - 4, 44)
        $img = [Drawing.Image]::FromFile($files[$i].FullName)
        $g.DrawImage($img, $x + 6, $y + 6, $img.Width, $img.Height); $img.Dispose()
        $label = $files[$i].BaseName -replace '200$', ''
        $g.DrawString($label, $font, [Drawing.Brushes]::Black, [single]($x + 4), [single]($y + 48))
    }
    $icon = New-Object Drawing.Icon $ico, 256, 256
    $g.DrawIcon($icon, 10, $rows * $ch + 6); $icon.Dispose()
    $g.Dispose(); $bmp.Save($Sheet); $bmp.Dispose()
    Write-Output "preview: $Sheet"
}
