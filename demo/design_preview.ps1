<#
.SYNOPSIS
  Draws a cover and a step slide in a few colour schemes from the last run's captures, so a
  scheme can be chosen before the video is rebuilt. Writes design_previews\*.png.
#>
param([string]$Work = (Join-Path $env:TEMP 'bulknotes_demo_en'), [string]$Shot = 's05_filters')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$W = 1920; $H = 1080; $top = 96; $bottomBand = 190
$out = Join-Path $PSScriptRoot 'design_previews'
New-Item -ItemType Directory -Force $out | Out-Null
function C($hex) { [Drawing.ColorTranslator]::FromHtml($hex) }

$themes = @(
    @{ Name = 'Sky';  BgTop = '#E6F1FB'; BgBottom = '#BFD9F0'; Head = '#1F3A5F'; CoverTop = '#24477A'; CoverBottom = '#14284A'; Accent = '#F2B544'; Band = '#18222E' },
    @{ Name = 'Teal'; BgTop = '#E2F4F1'; BgBottom = '#B4DCD5'; Head = '#1D4E57'; CoverTop = '#23646C'; CoverBottom = '#123A40'; Accent = '#F4C26B'; Band = '#16252A' },
    @{ Name = 'Sand'; BgTop = '#FBF4EA'; BgBottom = '#EBD8BF'; Head = '#3B3A5A'; CoverTop = '#3F3E66'; CoverBottom = '#25243F'; Accent = '#E8A13A'; Band = '#22212E' }
)

function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}
function Centered($g, $text, $font, $brush, $rect) {
    $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString($text, $font, $brush, $rect, $sf)
}
function NewG($bmp) { $g = [Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'; $g }

foreach ($t in $themes) {
    # ---- cover
    $bmp = New-Object Drawing.Bitmap $W, $H; $g = NewG $bmp
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Rectangle 0, 0, $W, $H), (C $t.CoverTop), (C $t.CoverBottom), 90.0
    $g.FillRectangle($grad, 0, 0, $W, $H)
    $accent = C $t.Accent
    # a framed panel: translucent fill, gold border
    $px = 360; $py = 290; $pw = $W - 720; $ph = 470
    $panel = RoundRect $px $py $pw $ph 28
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(40, 255, 255, 255))), $panel)
    $g.DrawPath((New-Object Drawing.Pen $accent, 4), $panel)
    # the tag above the panel
    $tagFont = New-Object Drawing.Font 'Segoe UI Semibold', 20
    $tagText = 'PARATEXT PLUGIN'
    $sz = $g.MeasureString($tagText, $tagFont)
    $tw = $sz.Width + 44; $th = 48; $tx = ($W - $tw) / 2; $ty = $py - $th / 2
    $tag = RoundRect $tx $ty $tw $th 24
    $g.FillPath((New-Object Drawing.SolidBrush $accent), $tag)
    Centered $g $tagText $tagFont (New-Object Drawing.SolidBrush (C $t.CoverBottom)) (New-Object Drawing.RectangleF $tx, $ty, $tw, $th)
    # the titles and a gold rule between them
    Centered $g 'Plugins from Phil' (New-Object Drawing.Font 'Segoe UI Semibold', 80) ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF $px, ($py + 60), $pw, 170)
    $g.FillRectangle((New-Object Drawing.SolidBrush $accent), ($W / 2 - 160), ($py + 250), 320, 5)
    Centered $g 'Bulk Note Editing' (New-Object Drawing.Font 'Segoe UI', 50) (New-Object Drawing.SolidBrush $accent) (New-Object Drawing.RectangleF $px, ($py + 280), $pw, 120)
    $g.Dispose(); $bmp.Save((Join-Path $out "$($t.Name)_cover.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

    # ---- a step
    $bmp = New-Object Drawing.Bitmap $W, $H; $g = NewG $bmp
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Rectangle 0, $top, $W, ($H - $top - $bottomBand)), (C $t.BgTop), (C $t.BgBottom), 90.0
    $g.FillRectangle($grad, 0, $top, $W, ($H - $top - $bottomBand))
    $g.FillRectangle((New-Object Drawing.SolidBrush (C $t.Head)), 0, 0, $W, $top)
    $g.FillRectangle((New-Object Drawing.SolidBrush $accent), 0, ($top - 5), $W, 5)
    $g.DrawString('Combine filters', (New-Object Drawing.Font 'Segoe UI Semibold', 30), [Drawing.Brushes]::White, 40, 20)
    $img = [Drawing.Image]::FromFile((Join-Path $Work "shots\$Shot.png"))
    $areaW = $W - 80; $areaH = $H - $top - $bottomBand - 40
    $sc = [Math]::Min($areaW / $img.Width, $areaH / $img.Height)
    $iw = $img.Width * $sc; $ih = $img.Height * $sc; $ox = ($W - $iw) / 2; $oy = $top + 20 + ($areaH - $ih) / 2
    # soft shadow (a few spread rectangles), white frame, the window
    for ($k = 12; $k -ge 2; $k -= 2) {
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(10, 20, 40, 70))), (RoundRect ($ox - $k + 6) ($oy - $k + 10) ($iw + 2 * $k) ($ih + 2 * $k) (10 + $k)))
    }
    $g.FillPath([Drawing.Brushes]::White, (RoundRect ($ox - 6) ($oy - 6) ($iw + 12) ($ih + 12) 10))
    $g.DrawImage($img, [float]$ox, [float]$oy, [float]$iw, [float]$ih); $img.Dispose()
    $g.FillRectangle((New-Object Drawing.SolidBrush (C $t.Band)), 0, ($H - $bottomBand), $W, $bottomBand)
    Centered $g 'Filters can be combined: here, notes created by Luis Revisor that contain the word revisar. The bold line at the top always says which filters are in effect.' (New-Object Drawing.Font 'Segoe UI', 27) ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF 70, ($H - $bottomBand + 8), ($W - 140), ($bottomBand - 16))
    $g.Dispose(); $bmp.Save((Join-Path $out "$($t.Name)_step.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "drew $($t.Name)"
}
