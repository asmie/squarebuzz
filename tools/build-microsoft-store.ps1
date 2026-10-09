param(
    [string]$Version = '',
    [ValidateSet('x64', 'x86', 'arm64')]
    [string[]]$Architectures = @('x64', 'x86', 'arm64'),
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'src/Squarebuzz.App/Squarebuzz.App.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
if (!$Version) {
    $display = [version]($projectXml.Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1)
    $build = $projectXml.Project.PropertyGroup.ApplicationVersion | Where-Object { $_ } | Select-Object -First 1
    $Version = "$($display.Major).$($display.Minor).$build.0"
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw 'Store version must have four components and end in .0.' }
$parsedVersion = [version]$Version
if ($parsedVersion.Major -eq 0 -or (@($parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build) | Where-Object { $_ -gt 65535 })) {
    throw 'Store version components must be in range 0..65535, with a nonzero major version.'
}
$displayVersion = "$($parsedVersion.Major).$($parsedVersion.Minor).$($parsedVersion.Build)"
$output = Join-Path $repo "artifacts/microsoft-store/$Version"
$final = Join-Path $output 'final'
$bundleInputs = Join-Path $output 'bundle-inputs'
New-Item -ItemType Directory -Path $output, $final, $bundleInputs -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsRoot) { throw 'Visual Studio C++ Build Tools are required to generate APPXSYM files.' }
$vc = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC/Tools/MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pdbTool = Join-Path $vc.FullName 'bin/Hostx64/x64/mspdbcmf.exe'
if (!(Test-Path -LiteralPath $pdbTool)) { throw "Missing symbol tool: $pdbTool" }
$sdk = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin') -Directory |
    Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64/makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk) { throw 'Install a Windows SDK containing MakeAppx.' }
$makeAppx = Join-Path $sdk.FullName 'x64/makeappx.exe'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-ZipText($Archive, $Name) {
    $entry = $Archive.GetEntry($Name)
    if (!$entry) { throw "Missing archive entry: $Name" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Read-ZipStream($Archive, $Name) {
    $entry = $Archive.GetEntry($Name)
    if (!$entry) { throw "Missing archive entry: $Name" }
    $inputStream = $entry.Open()
    $memory = [IO.MemoryStream]::new()
    try { $inputStream.CopyTo($memory) } finally { $inputStream.Dispose() }
    $memory.Position = 0
    return $memory
}

$expectedCultures = Get-ChildItem -LiteralPath (Join-Path $repo 'src/Squarebuzz.App/Resources/Strings') -Filter 'AppStrings.*.resx' |
    ForEach-Object { $_.Name -replace '^AppStrings\.', '' -replace '\.resx$', '' }
$reports = @()
$symbolFiles = @()
foreach ($arch in ($Architectures | Select-Object -Unique)) {
    $packageDir = Join-Path $output "packages/$arch"
    $log = Join-Path $output "build-$arch.log"
    if (!$SkipBuild) {
        Write-Host "Building Store package: $arch ($Version)"
        & dotnet publish $project -f net10.0-windows10.0.19041.0 -c Release `
            -p:SquarebuzzTargetFramework=net10.0-windows10.0.19041.0 `
            "-p:RuntimeIdentifierOverride=win-$arch" -p:PublishProfile=MicrosoftStore `
            "-p:SquarebuzzWindowsDisplayVersion=$displayVersion" "-p:MsPdbCmfExeFullpath=$pdbTool" `
            "-p:AppxPackageDir=$packageDir/" -v:minimal *> $log
        if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 25; throw "Build failed: $arch (see $log)" }
    }
    $packages = @(Get-ChildItem -LiteralPath $packageDir -Recurse -Filter "Squarebuzz.App_${Version}_${arch}.msix")
    $symbols = @(Get-ChildItem -LiteralPath $packageDir -Recurse -Filter "Squarebuzz.App_${Version}_${arch}.appxsym")
    if ($packages.Count -ne 1 -or $symbols.Count -ne 1) { throw "Expected one MSIX and APPXSYM for $arch." }
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        [xml]$manifest = Read-ZipText $archive 'AppxManifest.xml'
        $identity = $manifest.Package.Identity
        if ($identity.Name -ne 'asmie.squarebuzz' -or $identity.Publisher -ne 'CN=98E7CD08-07AD-41BF-8337-6C4FB38FF65B' -or
            $identity.Version -ne $Version -or $identity.ProcessorArchitecture -ne $arch) { throw "Incorrect Store identity/version/architecture: $arch" }
        if ($manifest.Package.Properties.PublisherDisplayName -ne 'asmie') { throw 'Incorrect publisher display name.' }
        if ($manifest.Package.Dependencies.PackageDependency) { throw 'Unexpected external framework dependency in self-contained package.' }
        $names = @($archive.Entries.FullName)
        foreach ($required in @('Squarebuzz.App.exe', 'Squarebuzz.App.dll', 'Squarebuzz.Presentation.dll', 'Squarebuzz.Data.dll', 'Squarebuzz.Core.dll', 'coreclr.dll', 'Microsoft.UI.Xaml.dll')) {
            if ($names -notcontains $required) { throw "Missing $required in $arch" }
        }
        $machine = @{ x64 = 0x8664; x86 = 0x14c; arm64 = 0xaa64 }[$arch]
        foreach ($native in @('Squarebuzz.App.exe', 'coreclr.dll', 'Microsoft.ui.xaml.dll', 'e_sqlite3.dll')) {
            $peStream = Read-ZipStream $archive $native
            $pe = [System.Reflection.PortableExecutable.PEReader]::new($peStream)
            try {
                if ([int]$pe.PEHeaders.CoffHeader.Machine -ne $machine) { throw "Incorrect native architecture: $native ($arch)" }
            } finally { $pe.Dispose(); $peStream.Dispose() }
        }
        foreach ($culture in $expectedCultures) {
            if ($names -notcontains "$culture/Squarebuzz.App.resources.dll") { throw "Missing translation $culture in $arch" }
        }
        if (@($manifest.Package.Resources.Resource).Count -ne ($expectedCultures.Count + 1)) { throw 'Manifest language count differs from application resources.' }
        $reports += [ordered]@{ architecture = $arch; version = $Version; languages = @($manifest.Package.Resources.Resource.Language);
            package = $packages[0].Name; sha256 = (Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256).Hash;
            selfContained = $true; satelliteAssemblies = $expectedCultures.Count }
    } finally { $archive.Dispose() }
    $symbolArchive = [IO.Compression.ZipFile]::OpenRead($symbols[0].FullName)
    $packageArchive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $pdbs = @($symbolArchive.Entries | Where-Object { $_.Name -like '*.pdb' } | ForEach-Object Name)
        foreach ($pdb in @('Squarebuzz.App.pdb', 'Squarebuzz.Core.pdb', 'Squarebuzz.Data.pdb', 'Squarebuzz.Presentation.pdb')) {
            if ($pdbs -notcontains $pdb) { throw "Missing owned symbols: $pdb ($arch)" }
            $pdbStream = Read-ZipStream $symbolArchive $pdb
            $dllStream = Read-ZipStream $packageArchive ($pdb -replace '\.pdb$', '.dll')
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
            $pe = [System.Reflection.PortableExecutable.PEReader]::new($dllStream)
            try {
                $reader = $provider.GetMetadataReader([System.Reflection.Metadata.MetadataReaderOptions]::Default, $null)
                [byte[]]$id = $reader.DebugMetadataHeader.Id
                $pdbGuid = [Guid]::new([byte[]]$id[0..15])
                $debugEntry = $pe.ReadDebugDirectory() | Where-Object { $_.Type -eq 'CodeView' } | Select-Object -First 1
                if (!$debugEntry -or $pe.ReadCodeViewDebugDirectoryData($debugEntry).Guid -ne $pdbGuid) {
                    throw "Symbols do not match packaged assembly: $pdb ($arch)"
                }
            } finally { $pe.Dispose(); $provider.Dispose(); $pdbStream.Dispose(); $dllStream.Dispose() }
        }
        $reports[-1]['symbols'] = $pdbs
        $reports[-1]['symbolIdentifiersMatch'] = $true
    } finally { $symbolArchive.Dispose(); $packageArchive.Dispose() }
    $destination = Join-Path $bundleInputs $packages[0].Name
    Copy-Item -LiteralPath $packages[0].FullName -Destination $destination -Force
    $symbolDestination = Join-Path $final $symbols[0].Name
    Copy-Item -LiteralPath $symbols[0].FullName -Destination $symbolDestination -Force
    $symbolFiles += $symbolDestination
}
if (@(Get-ChildItem -LiteralPath $bundleInputs -Filter '*.msix').Count -ne $reports.Count) {
    throw 'Bundle input directory contains packages outside the requested architecture set. Use a new output version or the original set.'
}
$bundle = Join-Path $final "squarebuzz-$Version.msixbundle"
& $makeAppx bundle /o /bv $Version /d $bundleInputs /p $bundle *> (Join-Path $output 'bundle.log')
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx bundle validation failed; see bundle.log.' }
$upload = Join-Path $final "squarebuzz-$Version.msixupload"
$stream = [IO.File]::Open($upload, [IO.FileMode]::Create)
$uploadArchive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (@($bundle) + $symbolFiles)) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($uploadArchive, $file, [IO.Path]::GetFileName($file), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $uploadArchive.Dispose(); $stream.Dispose() }
$uploadArchive = [IO.Compression.ZipFile]::OpenRead($upload)
try {
    if ($uploadArchive.Entries.Count -ne ($reports.Count + 1)) { throw 'Incorrect Store upload contents.' }
    foreach ($entry in $uploadArchive.Entries) {
        $entryStream = $entry.Open()
        try { $entryHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)) }
        finally { $entryStream.Dispose() }
        if ($entryHash -ne (Get-FileHash -LiteralPath (Join-Path $final $entry.Name) -Algorithm SHA256).Hash) {
            throw "Corrupt upload entry: $($entry.Name)"
        }
    }
} finally { $uploadArchive.Dispose() }
$bundleArchive = [IO.Compression.ZipFile]::OpenRead($bundle)
try {
    [xml]$bundleManifest = Read-ZipText $bundleArchive 'AppxMetadata/AppxBundleManifest.xml'
    if ($bundleManifest.Bundle.Identity.Name -ne 'asmie.squarebuzz' -or $bundleManifest.Bundle.Identity.Version -ne $Version) {
        throw 'Incorrect bundle identity.'
    }
    foreach ($package in $reports) {
        $entryStream = $bundleArchive.GetEntry($package.package).Open()
        try { $entryHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)) }
        finally { $entryStream.Dispose() }
        if ($entryHash -ne $package.sha256) { throw "Corrupt bundled package: $($package.package)" }
    }
} finally { $bundleArchive.Dispose() }
$report = [ordered]@{
    version = $Version; storeId = '9P308NW6NSXM'; packageFamilyName = 'asmie.squarebuzz_v6mzytc1feger';
    packages = $reports; makeAppxBundleValidation = 'passed'; archiveContentsVerified = $true;
    installedRuntimeTest = 'not performed'; windowsAppCertificationKit = 'not performed';
    upload = [IO.Path]::GetFileName($upload); uploadSha256 = (Get-FileHash -LiteralPath $upload -Algorithm SHA256).Hash;
    bundleSha256 = (Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash;
    signing = 'Unsigned for Microsoft Store submission; Store signs during publishing.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $final 'validation.json') -Encoding utf8
$instructions = Get-Content -LiteralPath (Join-Path $repo 'store/microsoft-store/INSTRUCTIONS.pl.txt') -Raw
$instructions.Replace('1.0.6.0', $Version).Replace('x64, x86, ARM64', ($Architectures -join ', ')) |
    Set-Content -LiteralPath (Join-Path $final 'INSTRUCTIONS.pl.txt') -Encoding utf8
Write-Host "Ready for Partner Center: $upload"
