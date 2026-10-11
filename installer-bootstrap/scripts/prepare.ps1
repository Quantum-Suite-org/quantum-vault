# Prepares the bootstrapper: copies the chained installer payload, the EULA,
# the logo, and generates a proper multi-size icon from the logo.
# Run from installer-bootstrap\ (npm run prepare-assets).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root       = Split-Path -Parent $PSScriptRoot
$repo       = Split-Path -Parent $root
$logoSrc    = Join-Path $repo 'src\gui\Assets\quantum-suite-logo.jpg'
$eulaSrc    = Join-Path $repo 'EULA.txt'
$payloadSrc = Join-Path $repo 'dist\QuantumVault-0.1.2-Setup.exe'

$payloadDir  = Join-Path $root 'payload'
$rendAssets  = Join-Path $root 'renderer\assets'
$buildAssets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $payloadDir, $rendAssets, $buildAssets | Out-Null

# 1. Chained installer payload (the Inno Setup package)
if (-not (Test-Path $payloadSrc)) { throw "Payload not found: $payloadSrc. Build the installer first." }
Copy-Item $payloadSrc (Join-Path $payloadDir 'QuantumVault-Setup.exe') -Force
Write-Host "Payload copied: $((Get-Item $payloadSrc).Length) bytes"

# 2. EULA shown inside the bootstrapper
Copy-Item $eulaSrc (Join-Path $rendAssets 'eula.txt') -Force

# 3. Logo for the UI
Copy-Item $logoSrc (Join-Path $rendAssets 'logo.jpg') -Force

# 4. Multi-size icon generated from the logo (square crop, 256 master)
$img  = [System.Drawing.Image]::FromFile($logoSrc)
$side = [Math]::Min($img.Width, $img.Height)
$cropX = [int](($img.Width - $side) / 2)
$cropY = [int](($img.Height - $side) / 2)
$srcRect = New-Object System.Drawing.Rectangle -ArgumentList $cropX, $cropY, $side, $side
$dstRect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, 256, 256
$master = New-Object System.Drawing.Bitmap -ArgumentList 256, 256
$g = [System.Drawing.Graphics]::FromImage($master)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.Clear([System.Drawing.Color]::Black)
$g.DrawImage($img, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$img.Dispose()

function Get-PngBytes([System.Drawing.Bitmap]$source, [int]$size) {
  $b = New-Object System.Drawing.Bitmap -ArgumentList $size, $size
  $gr = [System.Drawing.Graphics]::FromImage($b)
  $gr.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $gr.DrawImage($source, 0, 0, $size, $size)
  $gr.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $b.Dispose()
  return ,$ms.ToArray()
}

$sizes = @(16, 32, 48, 256)
$pngs = @()
foreach ($s in $sizes) { $pngs += ,(Get-PngBytes $master $s) }
$master.Dispose()

$icoPath = Join-Path $buildAssets 'icon.ico'
$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter -ArgumentList $fs
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $data = $pngs[$i]
  $dim = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$dim)
  $bw.Write([byte]$dim)
  $bw.Write([byte]0)
  $bw.Write([byte]0)
  $bw.Write([uint16]1)
  $bw.Write([uint16]32)
  $bw.Write([uint32]$data.Length)
  $bw.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write([byte[]]$data) }
$bw.Close()
$fs.Close()

Write-Host "Icon generated: $((Get-Item $icoPath).Length) bytes"
Write-Host 'Assets prepared.'
