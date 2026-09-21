# Generate placeholder PNG assets for SystemMate
# Run once before building. Real assets should replace these.
Add-Type -AssemblyName System.Drawing

function New-PlaceholderPng {
    param([string]$Path, [int]$Width, [int]$Height, [string]$Label = "SM")
    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 37, 99, 235))  # AccentBlue
    $font = New-Object System.Drawing.Font("Segoe UI", [Math]::Max(8, $Width / 4), [System.Drawing.FontStyle]::Bold)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($Label, $font, $brush, [System.Drawing.RectangleF]::new(0,0,$Width,$Height), $sf)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Created $Path ($Width x $Height)"
}

$assetsDir = Join-Path $PSScriptRoot "..\src\SystemMate\Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

New-PlaceholderPng "$assetsDir\StoreLogo.png"        50  50  "SM"
New-PlaceholderPng "$assetsDir\Square44x44Logo.png"  44  44  "SM"
New-PlaceholderPng "$assetsDir\Square150x150Logo.png" 150 150 "SystemMate"
New-PlaceholderPng "$assetsDir\Wide310x150Logo.png"  310 150 "SystemMate"
New-PlaceholderPng "$assetsDir\SplashScreen.png"     620 300 "SystemMate"

Write-Host "`nAll placeholder assets generated. Replace with real icons before release."
