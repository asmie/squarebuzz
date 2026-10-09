param(
    [Parameter(Mandatory)][string]$BundlePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$IntermediateDirectory = 'src/Squarebuzz.App/obj/Release/net10.0-android',
    [string]$DotnetPacksDirectory = 'C:/Program Files/dotnet/packs'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundleFullPath = (Resolve-Path -LiteralPath $BundlePath).Path
$outputFullPath = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputFullPath -Force | Out-Null
$symbolsZip = Join-Path $outputFullPath 'native-debug-symbols.zip'
if (Test-Path -LiteralPath $symbolsZip) { throw 'Symbol archive already exists; choose a fresh output directory.' }
$scratch = Join-Path $outputFullPath ('symbols-work-' + [guid]::NewGuid().ToString('N'))
$inputDirectory = Join-Path $scratch 'input'
$symbolsDirectory = Join-Path $scratch 'symbols'
New-Item -ItemType Directory -Path $inputDirectory,$symbolsDirectory -Force | Out-Null
$bundle = [System.IO.Compression.ZipFile]::OpenRead($bundleFullPath)
try {
    foreach ($entry in $bundle.Entries) {
        if ($entry.FullName -notmatch '^base/lib/([a-zA-Z0-9_-]+)/([^/]+\.so)$') { continue }
        $target = Join-Path $inputDirectory ($Matches[1] + '/' + $Matches[2])
        New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target)
    }
} finally { $bundle.Dispose() }
$roots = @((Resolve-Path -LiteralPath $IntermediateDirectory).Path)
# Runtime packages may contain symbols absent from the stripped app libraries.
$roots += @(Get-ChildItem -LiteralPath $DotnetPacksDirectory -Directory |
    Where-Object Name -Match '^Microsoft\.(NETCore\.App\.Runtime\.Mono\.android-|Android\.Runtime\.)' |
    ForEach-Object FullName)
$reportPath = Join-Path $outputFullPath 'native-symbols-coverage.json'
& node (Join-Path $PSScriptRoot 'collect-android-symbols.mjs') $inputDirectory $symbolsDirectory $reportPath @roots
if ($LASTEXITCODE -ne 0) { throw 'Native symbol collection failed.' }
[System.IO.Compression.ZipFile]::CreateFromDirectory($symbolsDirectory, $symbolsZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$report | Add-Member -NotePropertyName bundle_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $bundleFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
$report | Add-Member -NotePropertyName symbols_zip_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $symbolsZip -Algorithm SHA256).Hash.ToLowerInvariant()
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
# Every deletion is limited to the unique workspace directory created above.
$resolvedScratch = [System.IO.Path]::GetFullPath($scratch)
if (-not $resolvedScratch.StartsWith($outputFullPath.TrimEnd('\','/') + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Scratch directory is outside the selected output directory.'
}
Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
Write-Output $symbolsZip
