[CmdletBinding()]
param([switch]$NoLaunch, [switch]$ResolveOnly, [switch]$PlanOnly, [switch]$CloseRunningApp, [switch]$Gui, [switch]$InstallCpuTemperatureDriver)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
if ($Gui) { Write-Output 'DI_STATUS:Checking' }

# Distributed installers carry the package beside this script. In the source
# checkout, find the newest completed release instead of looking in installer/.
$packageDirectory = $PSScriptRoot
if (!(Test-Path -LiteralPath (Join-Path $packageDirectory 'DynamicIsland.msix') -PathType Leaf)) {
    $projectRoot = Split-Path -Parent $PSScriptRoot
    $artifacts = Join-Path $projectRoot 'artifacts'
    $isSourceInstaller = (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Build-Installer.ps1') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $projectRoot 'DynamicIslandWin.csproj') -PathType Leaf)
    $releases = @()
    if ($isSourceInstaller -and (Test-Path -LiteralPath $artifacts -PathType Container)) {
        $releases = @(Get-ChildItem -LiteralPath $artifacts -Directory | ForEach-Object {
            if ($_.Name -match '^DynamicIsland-(\d+\.\d+\.\d+\.\d+)-x64$' -and
                (Test-Path -LiteralPath (Join-Path $_.FullName 'DynamicIsland.msix') -PathType Leaf) -and
                (Test-Path -LiteralPath (Join-Path $_.FullName 'DynamicIsland.cer') -PathType Leaf)) {
                [pscustomobject]@{ Directory = $_.FullName; Version = [version]$Matches[1] }
            }
        } | Sort-Object -Property Version -Descending)
    }
    if ($releases.Count -eq 0) {
        throw 'Installation package not found. Extract the complete installer ZIP, or build it with installer\Build-Installer.ps1 before running Setup.cmd.'
    }
    $packageDirectory = $releases[0].Directory
}
$packagePath = Join-Path $packageDirectory 'DynamicIsland.msix'
$certificatePath = Join-Path $packageDirectory 'DynamicIsland.cer'
if (!(Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
    throw "Signing certificate not found: $certificatePath. Extract the complete installer ZIP and run Setup.cmd again."
}
# Used by path-resolution checks; never changes certificates or installs an app.
if ($ResolveOnly) {
    [pscustomobject]@{ PackagePath = $packagePath; CertificatePath = $certificatePath }
    return
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    if (!$manifestEntry) { throw 'The installation package has no AppxManifest.xml.' }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
$identity = $manifest.Package.Identity
if ($identity.Name -ne 'DynamicIslandWin') { throw 'Unexpected installation package identity.' }
$incomingVersion = [version]$identity.Version
$incomingArchitecture = [string]$identity.ProcessorArchitecture
# Only consider this publisher's package. Never uninstall a conflicting package:
# a higher version lets Windows update architecture and retain application data.
$existing = Get-AppxPackage -Name $identity.Name | Where-Object { $null -ne $_ -and $_.Publisher -eq $identity.Publisher } |
    Sort-Object -Property { [version]$_.Version } -Descending | Select-Object -First 1
$action = 'Install'
if ($existing) {
    if ([version]$existing.Version -gt $incomingVersion -or
        ([version]$existing.Version -eq $incomingVersion -and [string]$existing.Architecture -ne $incomingArchitecture)) {
        $action = 'RequireNewerVersion'
    }
    elseif ([version]$existing.Version -eq $incomingVersion) { $action = 'AlreadyInstalled' }
    else { $action = 'Update' }
}
if ($PlanOnly) {
    [pscustomobject]@{ PackagePath = $packagePath; Version = $incomingVersion; Architecture = $incomingArchitecture;
        Action = $action; InstalledPackage = $(if ($existing) { $existing.PackageFullName } else { $null }) }
    return
}
if ($action -eq 'RequireNewerVersion') {
    throw "Windows already has $($existing.PackageFullName). Install a package newer than $($existing.Version) to update safely. The existing app has been kept."
}
function Install-TemperatureDriver {
    if (!$InstallCpuTemperatureDriver) { return }
    $driverRegistration = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue
    if ($driverRegistration) { return }
    $driverPath = Join-Path $packageDirectory 'PawnIO_setup.exe'
    if ((Get-FileHash -LiteralPath $driverPath -Algorithm SHA256).Hash -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032') { throw 'PawnIO installer SHA-256 mismatch.' }
    $driverSignature = Get-AuthenticodeSignature -LiteralPath $driverPath
    if ($driverSignature.Status -ne 'Valid' -or $driverSignature.SignerCertificate.Thumbprint -ne 'F380DCC9F706E2756A5047B832FFE719E1BC35F5') { throw 'PawnIO signature is invalid.' }
    if ($Gui) { Write-Output 'DI_STATUS:TemperatureDriver' }
    try {
        $driverInstall = Start-Process -FilePath $driverPath -Verb RunAs -WindowStyle Hidden -ArgumentList @('-install', '-silent') -Wait -PassThru
        if ($driverInstall.ExitCode -ne 0) { Write-Output 'DI_WARNING:TemperatureDriver' }
    }
    catch { Write-Output 'DI_WARNING:TemperatureDriver' }
}
if ($action -eq 'AlreadyInstalled') {
    Install-TemperatureDriver
    Write-Host "OC - Dynamic Island $incomingVersion is already installed."
    if ($Gui) { Write-Output ('DI_APPID:' + $existing.PackageFamilyName + '!App') }
    if (!$NoLaunch) { Start-Process -FilePath 'explorer.exe' -ArgumentList ('shell:AppsFolder\' + $existing.PackageFamilyName + '!App') }
    return
}
Write-Host "Installation package: $packagePath ($action, version $incomingVersion)"
if ([Environment]::OSVersion.Version.Build -lt 19041) { throw 'Windows 10 version 2004 or later is required.' }
if (Get-Process -Name DynamicIslandWin -ErrorAction SilentlyContinue) {
    if ($CloseRunningApp -and $existing -and [version]$existing.Version -ge [version]'1.0.5.0') {
        if ($Gui) { Write-Output 'DI_STATUS:Closing' }
        $shutdownExe = Join-Path $existing.InstallLocation 'DynamicIslandWin.exe'
        $shutdown = Start-Process -FilePath $shutdownExe -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
        [void]$shutdown.WaitForExit(5000)
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while ((Get-Process -Name DynamicIslandWin -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }
    }
    if (Get-Process -Name DynamicIslandWin -ErrorAction SilentlyContinue) {
        throw 'Exit OC - Dynamic Island using its tray menu, then run Setup again.'
    }
}

# Production packages signed by a trusted publisher skip this development step.
$signature = Get-AuthenticodeSignature -LiteralPath $packagePath
if ($signature.Status -ne 'Valid') {
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw 'The package signature does not match DynamicIsland.cer.'
    }
    # Windows PowerShell maps CERT_E_UNTRUSTEDROOT to UnknownError for MSIX.
    # Accept only that chain condition. Add-AppxPackage validates the signature
    # and the payload using Windows deployment policy after trust is installed.
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    [void]$chain.Build($certificate)
    $chainErrors = @($chain.ChainStatus | Where-Object { $_.Status -ne [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot })
    $chain.Dispose()
    if ($signature.Status -notin @('NotTrusted', 'UnknownError') -or $chainErrors.Count -gt 0) {
        throw "Invalid package signature or certificate: $($signature.Status)"
    }
    Write-Host ('Installing the development publisher certificate: ' + $certificate.Thumbprint)
    if ($Gui) { Write-Output 'DI_STATUS:Trust' }
    Write-Host 'Windows will request administrator permission to trust this certificate.'
    $escapedPath = $certificatePath.Replace("'", "''")
    $thumbprint = $certificate.Thumbprint
    $command = "`$ErrorActionPreference='Stop'; `$cert=Import-Certificate -FilePath '$escapedPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople; if (`$cert.Thumbprint -ne '$thumbprint') { exit 1 }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $elevated = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile', '-EncodedCommand', $encoded) -Wait -PassThru
    if ($elevated.ExitCode -ne 0) { throw 'Publisher certificate installation was cancelled or failed.' }
    if (!(Test-Path -LiteralPath ('Cert:\LocalMachine\TrustedPeople\' + $thumbprint))) { throw 'Publisher certificate was not installed.' }
    $certificate.Dispose()
}

# Runs as the original user; MSIX handles atomic updates and Settings > Apps removal.
if ($Gui) { Write-Output 'DI_STATUS:Installing' }
Add-AppxPackage -Path $packagePath -ErrorAction Stop
Install-TemperatureDriver
$installed = Get-AppxPackage -Name $identity.Name | Where-Object { $_.Publisher -eq $identity.Publisher -and [version]$_.Version -eq $incomingVersion -and [string]$_.Architecture -eq $incomingArchitecture } | Select-Object -First 1
if (!$installed) { throw 'Windows did not register the expected OC - Dynamic Island version and architecture.' }
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DynamicIslandWin' -ErrorAction SilentlyContinue
Write-Host 'OC - Dynamic Island installed. Allow notification access when the application asks.'
if ($Gui) { Write-Output ('DI_APPID:' + $installed.PackageFamilyName + '!App') }
if (!$NoLaunch) {
    Start-Process -FilePath 'explorer.exe' -ArgumentList ('shell:AppsFolder\' + $installed.PackageFamilyName + '!App')
}
