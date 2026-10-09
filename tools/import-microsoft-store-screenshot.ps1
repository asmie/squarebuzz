#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('01-menu','02-campaign','03-game','04-daily')]
    [string]$Name
)

# The user takes the actual Windows app screenshot with Alt+PrintScreen.
# This helper reads that image without controlling or altering the app UI.
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run with Windows PowerShell in STA mode: powershell.exe -STA -File tools\import-microsoft-store-screenshot.ps1 -Name 01-menu'
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if (-not [Windows.Forms.Clipboard]::ContainsImage()) {
    throw 'Clipboard has no image. Activate squarebuzz and press Alt+PrintScreen first.'
}
$repo = Split-Path -Parent $PSScriptRoot
$base = Join-Path $repo 'artifacts\microsoft-store\1.0.6.0'
$images = Join-Path $base 'store-listing\images\screenshots'
$image = [Windows.Forms.Clipboard]::GetImage()
try {
    if ($image.Width -lt 1366 -or $image.Height -lt 768) {
        throw "Screenshot is $($image.Width)x$($image.Height). Maximize the app on a display with at least 1366x768 pixels and capture again. Do not upscale the image."
    }
    New-Item -ItemType Directory -Path $images -Force | Out-Null
    $file = Join-Path $images "$Name.png"
    $temporary = Join-Path $images "$Name.tmp.png"
    $image.Save($temporary, [Drawing.Imaging.ImageFormat]::Png)
    if ((Get-Item -LiteralPath $temporary).Length -ge 50MB) {
        Remove-Item -LiteralPath $temporary
        throw 'Screenshot exceeds the Microsoft Store 50 MB limit.'
    }
    Move-Item -LiteralPath $temporary -Destination $file -Force
    Write-Host "Saved $file ($($image.Width)x$($image.Height))"
} finally {
    if ($null -ne $image) { $image.Dispose() }
}
$runtime = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
if (-not (Test-Path -LiteralPath $runtime)) { throw "CSV builder runtime missing: $runtime" }
& $runtime (Join-Path $repo 'store\microsoft-store\build-listing-assets.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Screenshot saved, but updating the import CSV failed.' }
Write-Host 'Use Partner Center > Import listings > Import folder, and select the store-listing folder.'
