# Draws the picture GitHub shows when a Nibble link is pasted somewhere - the repository's
# "social preview", and the card a release gets in a feed.
#
#   powershell -File tools\SocialCard.ps1
#   -> assets/social-preview.png   1280x640
#
# Same palette, type and pixel horizon as the home page, so a shared link looks like the
# product rather than like a screenshot with a logo pasted on it. Layout is in plain numbers
# on purpose: nothing here should depend on a chain of measured sizes.
param(
    [string]$Out = "$PSScriptRoot\..\assets\social-preview.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$cardWidth = 1280
$cardHeight = 640
$root = Split-Path $PSScriptRoot -Parent

function Colour([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

$paper = Colour "#F4F4F7"
$ink = Colour "#1D1D1F"
$ink2 = Colour "#6E6E73"
$line2 = Colour "#D8D8DE"
$hillFar = Colour "#DFEAD1"
$hillNear = Colour "#CFE0BB"
$accent = Colour "#A3E635"

$canvas = New-Object System.Drawing.Bitmap($cardWidth, $cardHeight)
$art = [System.Drawing.Graphics]::FromImage($canvas)
$art.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$art.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$art.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$art.Clear($paper)

# the faint graph paper the home page is drawn on
$gridPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(9, 0, 0, 0)), 1
for ($x = 0; $x -lt $cardWidth; $x += 32) { $art.DrawLine($gridPen, $x, 0, $x, $cardHeight) }
for ($y = 0; $y -lt $cardHeight; $y += 32) { $art.DrawLine($gridPen, 0, $y, $cardWidth, $y) }

# the horizon: two stair-stepped bands along the bottom, the way the new tab page ends
$pattern = @(0, 0, 8, 8, 16, 16, 8, 8, 0, 0, -8, -8)
function Draw-Band([System.Drawing.Color]$fill, [int]$top, [int]$step) {
    $points = New-Object System.Collections.Generic.List[System.Drawing.Point]
    $index = 0
    for ($x = 0; $x -le $cardWidth; $x += $step) {
        $level = $top + $pattern[$index % $pattern.Count]
        $next = $x + $step
        $points.Add((New-Object System.Drawing.Point($x, $level)))
        $points.Add((New-Object System.Drawing.Point($next, $level)))
        $index++
    }
    $points.Add((New-Object System.Drawing.Point($cardWidth, $cardHeight)))
    $points.Add((New-Object System.Drawing.Point(0, $cardHeight)))
    $brush = New-Object System.Drawing.SolidBrush $fill
    $art.FillPolygon($brush, $points.ToArray())
    $brush.Dispose()
}
Draw-Band $hillFar 592 40
Draw-Band $hillNear 624 40

# silkscreen, letter-spaced, centred on a given x - the shell's own pixel type
$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$fonts.AddFontFile((Join-Path $root "src\Nibble\Assets\Fonts\Silkscreen-Regular.ttf"))
$pixelName = $fonts.Families[0].Name

function Draw-Spaced([string]$text, [single]$size, [System.Drawing.Color]$colour,
                     [single]$centreX, [single]$y, [single]$spacing) {
    $font = New-Object System.Drawing.Font($pixelName, $size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush $colour
    $widths = @()
    foreach ($character in $text.ToCharArray()) { $widths += $art.MeasureString([string]$character, $font).Width }
    $total = ($widths | Measure-Object -Sum).Sum + ($spacing * ($text.Length - 1))
    $cursor = $centreX - ($total / 2)
    for ($i = 0; $i -lt $text.Length; $i++) {
        $art.DrawString([string]$text[$i], $font, $brush, $cursor, $y)
        $cursor += $widths[$i] + $spacing
    }
    $font.Dispose(); $brush.Dispose()
}

# a pill with a label in it, centred on x; returns its width so a row can be laid out
function Draw-Chip([string]$label, [single]$size, [single]$centreX, [single]$y) {
    $font = New-Object System.Drawing.Font($pixelName, $size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $text = $art.MeasureString($label, $font)
    $pad = 18
    $chipWidth = $text.Width + ($pad * 2)
    $chipHeight = 44
    $left = $centreX - ($chipWidth / 2)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $radius = 7
    $path.AddArc($left, $y, $radius * 2, $radius * 2, 180, 90)
    $path.AddArc($left + $chipWidth - ($radius * 2), $y, $radius * 2, $radius * 2, 270, 90)
    $path.AddArc($left + $chipWidth - ($radius * 2), $y + $chipHeight - ($radius * 2), $radius * 2, $radius * 2, 0, 90)
    $path.AddArc($left, $y + $chipHeight - ($radius * 2), $radius * 2, $radius * 2, 90, 90)
    $path.CloseFigure()
    $art.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), $path)
    $art.DrawPath((New-Object System.Drawing.Pen $line2, 1), $path)
    $brush = New-Object System.Drawing.SolidBrush $ink
    $art.DrawString($label, $font, $brush, ($left + $pad), ($y + (($chipHeight - $text.Height) / 2)))
    $path.Dispose(); $font.Dispose(); $brush.Dispose()
    return $chipWidth
}

# the wordmark, sized to a fixed box so the layout below it is predictable
$logo = [System.Drawing.Image]::FromFile((Join-Path $root "assets\nibble-logo.png"))
$logoWidth = 760
$logoHeight = [int][Math]::Round($logo.Height * ($logoWidth / $logo.Width))
$logoTop = 84
$art.DrawImage($logo, (($cardWidth - $logoWidth) / 2), $logoTop, $logoWidth, $logoHeight)

Draw-Spaced "A TINY, TASTY WEB BROWSER FOR WINDOWS" 23 $ink2 ($cardWidth / 2) ($logoTop + $logoHeight + 30) 3.8

$chipLabels = @("2.2 MB", "NO TELEMETRY", "THE ENGINE WINDOWS ALREADY HAS")
$chipWidths = @()
foreach ($label in $chipLabels) { $chipWidths += ($art.MeasureString($label, (New-Object System.Drawing.Font($pixelName, 17, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel))).Width + 40) }
$rowWidth = ($chipWidths | Measure-Object -Sum).Sum + (14 * ($chipLabels.Count - 1))
$cursor = ($cardWidth - $rowWidth) / 2
for ($i = 0; $i -lt $chipLabels.Count; $i++) {
    Draw-Chip $chipLabels[$i] 17 ($cursor + ($chipWidths[$i] / 2)) 468 | Out-Null
    $cursor += $chipWidths[$i] + 14
}

# one accent pixel, saying whose card this is
$accentBrush = New-Object System.Drawing.SolidBrush $accent
$art.FillRectangle($accentBrush, ($cardWidth / 2) - 8, 534, 16, 16)
Draw-Spaced "OPEN SOURCE . NIBBLE 1.0" 15 $ink2 ($cardWidth / 2) 562 3.2

$full = [System.IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
$canvas.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)

$art.Dispose(); $canvas.Dispose(); $logo.Dispose(); $accentBrush.Dispose(); $gridPen.Dispose(); $fonts.Dispose()
"SAVED $full ($cardWidth x $cardHeight, wordmark $logoWidth x $logoHeight)"
