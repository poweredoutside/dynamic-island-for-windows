[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [string]$PfxPath,
    [string]$PfxPassword = $env:DYNAMIC_ISLAND_PFX_PASSWORD
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
foreach ($required in @('DynamicIsland.msix', 'DynamicIsland.cer')) {
    if (!(Test-Path -LiteralPath (Join-Path $release $required))) { throw "Missing signed payload: $required" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $release 'DynamicIsland.msix'))
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
$version = [version]$manifest.Package.Identity.Version
$publishDirectory = Join-Path $projectRoot ('artifacts\setup-build-' + [guid]::NewGuid().ToString('N'))
$manifestPath = $publishDirectory + '.manifest.xml'
$manifest.Save($manifestPath)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1'), (Join-Path $PSScriptRoot 'INSTALL.txt') -Destination $release -Force
try {
    & dotnet publish (Join-Path $PSScriptRoot 'Setup\Setup.csproj') -c Release -r win-x64 --self-contained true ('-p:InstallerPayloadDirectory=' + $release) ('-p:InstallerIdentityManifest=' + $manifestPath) ('-p:Version=' + $version.ToString(3)) -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'GUI setup build failed' }
} finally {
    Remove-Item -LiteralPath $manifestPath -ErrorAction SilentlyContinue
}
$setup = Join-Path $publishDirectory 'ocdynamicisland.exe'
if (!$PfxPath) { $PfxPath = Join-Path $projectRoot '.tools\signing\development.pfx'; $PfxPassword = '' }
$sdkRoot = Join-Path $projectRoot '.tools\packages\microsoft.windows.sdk.buildtools\10.0.26100.9169'
$signTool = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter signtool.exe | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1 -ExpandProperty FullName
if (!$signTool) { throw 'Build installer/Build-Installer.ps1 first to provision SDK signing tools.' }
$signArguments = @('sign', '/fd', 'SHA256', '/f', $PfxPath)
if ($PfxPassword) { $signArguments += @('/p', $PfxPassword) }
$signArguments += $setup
& $signTool @signArguments
if ($LASTEXITCODE -ne 0) { throw 'GUI setup signing failed' }
$releaseSetup = Join-Path $release 'ocdynamicisland.exe'
Copy-Item -LiteralPath $setup -Destination $releaseSetup -Force
$standalone = Join-Path $projectRoot 'artifacts\ocdynamicisland.exe'
Copy-Item -LiteralPath $setup -Destination $standalone -Force
$hashes = foreach ($name in @('DynamicIsland.msix', 'ocdynamicisland.exe', 'PawnIO-2.2.0-source.zip')) {
    (Get-FileHash -LiteralPath (Join-Path $release $name) -Algorithm SHA256).Hash + '  ' + $name
}
$hashes | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII
$publicHashes = $hashes
$publicHashes | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\SHA256SUMS.txt') -Encoding ASCII
& (Join-Path $PSScriptRoot 'Test-Setup.ps1') -Path $standalone -PackagePath (Join-Path $release 'DynamicIsland.msix')
Write-Host "Standalone graphical installer ready: $standalone"
