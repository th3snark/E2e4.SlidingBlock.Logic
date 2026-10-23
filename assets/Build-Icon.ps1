<#
Rasterises assets\icon.svg to assets\icon.png (256x256), the NuGet package icon.
Run from this folder with Windows PowerShell:  powershell -STA -File Build-Icon.ps1
Supports the SVG subset used in icon.svg: rect and circle with fill and fill-opacity (same renderer as E2e4.Chess Branding\Build-Icons.ps1).
#>
Add-Type -AssemblyName PresentationCore, WindowsBase
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$inv = [Globalization.CultureInfo]::InvariantCulture
function Num($e, $n, $d = 0) { if ($e.HasAttribute($n)) { [double]::Parse($e.GetAttribute($n), $inv) } else { $d } }

function New-Brush($color, $opacity) {
    $b = New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($color))
    $b.Opacity = $opacity; $b.Freeze(); $b
}

[xml]$svg = Get-Content (Join-Path $here 'icon.svg') -Raw -Encoding UTF8
$dv = New-Object Windows.Media.DrawingVisual
$dc = $dv.RenderOpen()
foreach ($e in @($svg.svg.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })) {
    $fill = New-Brush $e.GetAttribute('fill') (Num $e 'fill-opacity' 1)
    switch ($e.LocalName) {
        'rect' {
            $r = New-Object Windows.Rect (Num $e 'x'), (Num $e 'y'), (Num $e 'width'), (Num $e 'height')
            $dc.DrawRoundedRectangle($fill, $null, $r, (Num $e 'rx'), (Num $e 'ry' (Num $e 'rx')))
        }
        'circle' { $dc.DrawEllipse($fill, $null, (New-Object Windows.Point (Num $e 'cx'), (Num $e 'cy')), (Num $e 'r'), (Num $e 'r')) }
        default { throw "Unsupported SVG element <$($e.LocalName)>" }
    }
}
$dc.Close()
$bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap 256, 256, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
$bmp.Render($dv)
$enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
$fs = [IO.File]::Create((Join-Path $here 'icon.png')); $enc.Save($fs); $fs.Close()
'Wrote icon.png'
