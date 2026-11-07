Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$iconsDir = Join-Path $root "src\DesControlCenter.App\Tray\Icons"
$outPath = Join-Path $env:TEMP "des-tray-icons-preview.png"

$scale = 6
$cell = 32 * $scale
$padding = 20
$width = $cell * 3 + $padding * 4
$height = $cell + $padding * 2

$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.Clear([System.Drawing.Color]::FromArgb(255, 15, 22, 32))

$files = @("ok.ico", "warning.ico", "problem.ico")
for ($i = 0; $i -lt $files.Length; $i++) {
    $icon = New-Object System.Drawing.Icon (Join-Path $iconsDir $files[$i])
    $bmpIcon = $icon.ToBitmap()
    $x = $padding + $i * ($cell + $padding)
    $y = $padding
    $g.DrawImage($bmpIcon, $x, $y, $cell, $cell)
    $bmpIcon.Dispose()
    $icon.Dispose()
}

$g.Flush()
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

Write-Host $outPath
