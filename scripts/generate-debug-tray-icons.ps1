# TEMPORARY: maximally visible, unmissable tray icons for debugging tray visibility.
# Run scripts/generate-tray-icons.ps1 afterwards to restore the normal subtle "cat" design.
Add-Type -AssemblyName System.Drawing

function New-DebugIcon {
    param([string]$Path, [System.Drawing.Color]$Color)

    $size = 32
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($Color)

    # Thick black border + white inner border so it pops against any taskbar theme
    $blackPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), 3
    $g.DrawRectangle($blackPen, 1, 1, $size - 3, $size - 3)
    $whitePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 1
    $g.DrawRectangle($whitePen, 4, 4, $size - 9, $size - 9)

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

New-DebugIcon -Path (Join-Path $iconsDir "ok.ico") -Color ([System.Drawing.Color]::FromArgb(255, 0, 255, 0))
New-DebugIcon -Path (Join-Path $iconsDir "warning.ico") -Color ([System.Drawing.Color]::FromArgb(255, 255, 255, 0))
New-DebugIcon -Path (Join-Path $iconsDir "problem.ico") -Color ([System.Drawing.Color]::FromArgb(255, 255, 0, 255))

Write-Host "DEBUG icons written to $iconsDir (revert with generate-tray-icons.ps1)"
