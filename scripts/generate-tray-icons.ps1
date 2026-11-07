Add-Type -AssemblyName System.Drawing

function New-StatusIcon {
    param(
        [string]$Path,
        [System.Drawing.Color]$Color
    )

    $size = 32
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Filled circle body
    $bodyBrush = New-Object System.Drawing.SolidBrush $Color
    $g.FillEllipse($bodyBrush, 2, 2, $size - 4, $size - 4)

    # Subtle darker rim for definition against light/dark taskbars
    $rimColor = [System.Drawing.Color]::FromArgb(90, 0, 0, 0)
    $rimPen = New-Object System.Drawing.Pen $rimColor, 1.5
    $g.DrawEllipse($rimPen, 2, 2, $size - 4, $size - 4)

    # Two small triangular cat ears on top, nodding to the DES "cat" branding
    $earColor = [System.Drawing.Color]::FromArgb(255, [Math]::Max(0, $Color.R - 25), [Math]::Max(0, $Color.G - 25), [Math]::Max(0, $Color.B - 25))
    $earBrush = New-Object System.Drawing.SolidBrush $earColor
    $leftEar = @(
        (New-Object System.Drawing.Point 6, 9),
        (New-Object System.Drawing.Point 11, 9),
        (New-Object System.Drawing.Point 7, 1)
    )
    $rightEar = @(
        (New-Object System.Drawing.Point 21, 9),
        (New-Object System.Drawing.Point 26, 9),
        (New-Object System.Drawing.Point 25, 1)
    )
    $g.FillPolygon($earBrush, $leftEar)
    $g.FillPolygon($earBrush, $rightEar)
    $g.FillEllipse($bodyBrush, 2, 2, $size - 4, $size - 4)
    $g.DrawEllipse($rimPen, 2, 2, $size - 4, $size - 4)

    $g.Flush()

    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    $fs = New-Object System.IO.FileStream $Path, ([System.IO.FileMode]::Create)
    $icon.Save($fs)
    $fs.Close()
    $icon.Dispose()
    $g.Dispose()
    $bmp.Dispose()
}

$root = Split-Path -Parent $PSScriptRoot
$iconsDir = Join-Path $root "src\DesControlCenter.App\Tray\Icons"
New-Item -ItemType Directory -Force -Path $iconsDir | Out-Null

New-StatusIcon -Path (Join-Path $iconsDir "ok.ico") -Color ([System.Drawing.Color]::FromArgb(255, 63, 191, 111))
New-StatusIcon -Path (Join-Path $iconsDir "warning.ico") -Color ([System.Drawing.Color]::FromArgb(255, 224, 169, 62))
New-StatusIcon -Path (Join-Path $iconsDir "problem.ico") -Color ([System.Drawing.Color]::FromArgb(255, 229, 83, 75))

Write-Host "Icons written to $iconsDir"
