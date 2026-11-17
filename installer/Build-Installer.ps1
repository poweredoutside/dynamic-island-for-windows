[CmdletBinding()]
param(
    [version]$Version = '1.1.9.0',
    [string]$PfxPath,
    [string]$PfxPassword = $env:DYNAMIC_ISLAND_PFX_PASSWORD
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot 'artifacts'
$buildRoot = Join-Path $outputRoot ('build-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $buildRoot 'payload'
$release = Join-Path $outputRoot ('DynamicIsland-' + $Version.ToString(4) + '-x64')
$toolRoot = Join-Path $projectRoot '.tools\packages'
New-Item -ItemType Directory -Force -Path $payload, $release | Out-Null
& (Join-Path $PSScriptRoot 'Test-InstallerPaths.ps1')
& (Join-Path $PSScriptRoot 'Test-InstallerUpdates.ps1')
$hardwareInstaller = & (Join-Path $PSScriptRoot 'Get-HardwareDependency.ps1')
Copy-Item -LiteralPath $hardwareInstaller -Destination (Join-Path $release 'PawnIO_setup.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot '.tools\hardware\PawnIO-2.2.0-source.zip') -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $projectRoot '.tools\hardware\PawnIO-2.2.0-source.zip') -Destination $outputRoot -Force

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File exited with code $LASTEXITCODE" }
}

Invoke-Checked 'dotnet' @('restore', (Join-Path $PSScriptRoot 'SdkTools.csproj'), '--packages', $toolRoot)
$sdk = Join-Path $toolRoot 'microsoft.windows.sdk.buildtools\10.0.26100.9169'
$makeAppx = (Get-ChildItem -LiteralPath $sdk -Recurse -Filter makeappx.exe | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1).FullName
$signTool = (Get-ChildItem -LiteralPath $sdk -Recurse -Filter signtool.exe | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1).FullName
Invoke-Checked 'dotnet' @('publish', (Join-Path $projectRoot 'DynamicIslandWin.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', ('-p:Version=' + $Version.ToString(3)), '-o', $payload)
Invoke-Checked 'dotnet' @('publish', (Join-Path $projectRoot 'broker\ActivationHost.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', ('-p:Version=' + $Version.ToString(3)), '-o', (Join-Path $payload 'Activation'))

if (!$PfxPath) {
    $signingRoot = Join-Path $projectRoot '.tools\signing'
    New-Item -ItemType Directory -Force -Path $signingRoot | Out-Null
    $PfxPath = Join-Path $signingRoot 'development.pfx'
    $PfxPassword = ''
    if (!(Test-Path -LiteralPath $PfxPath)) {
        # Development certificate stays on disk; building never modifies trust stores.
        $rsa = [Security.Cryptography.RSACng]::new(2048)
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=DynamicIslandWin', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $oids = [Security.Cryptography.OidCollection]::new()
        [void]$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddYears(3))
        [IO.File]::WriteAllBytes($PfxPath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, ''))
        $certificate.Dispose()
        $rsa.Dispose()
    }
}
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($PfxPath, $PfxPassword)
[IO.File]::WriteAllBytes((Join-Path $release 'DynamicIsland.cer'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
$manifest = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw -Encoding UTF8)
$manifest.Package.Identity.Version = $Version.ToString(4)
$manifest.Package.Identity.Publisher = $certificate.Subject
$manifest.Save((Join-Path $payload 'AppxManifest.xml'))
$certificate.Dispose()

# Simple native package icons, generated without extra build dependencies.
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $payload 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
foreach ($icon in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
    $size = [int]$icon[1]
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::FromArgb(11, 10, 18))
    $mint = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(52, 245, 197))
    $graphics.FillEllipse($mint, [single]($size * .12), [single]($size * .33), [single]($size * .35), [single]($size * .35))
    $graphics.FillEllipse($mint, [single]($size * .53), [single]($size * .33), [single]($size * .35), [single]($size * .35))
    $bitmap.Save((Join-Path $assets $icon[0]), [Drawing.Imaging.ImageFormat]::Png)
    $mint.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}

$packagePath = Join-Path $release 'DynamicIsland.msix'
$packLog = Join-Path $buildRoot 'makeappx.log'
& $makeAppx pack /d $payload /p $packagePath /o /h SHA256 *> $packLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $packLog -Tail 25
    throw "MakeAppx failed; see $packLog"
}
$signArguments = @('sign', '/fd', 'SHA256', '/f', $PfxPath)
if ($PfxPassword) { $signArguments += @('/p', $PfxPassword) }
$signArguments += $packagePath
Invoke-Checked $signTool $signArguments
# /pa also checks trust, which is intentionally not installed on the build machine.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1'), (Join-Path $PSScriptRoot 'Setup.cmd'), (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'INSTALL.txt') -Destination $release -Force
Get-FileHash -LiteralPath $packagePath -Algorithm SHA256 | ForEach-Object { $_.Hash + '  DynamicIsland.msix' } | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII
& (Join-Path $PSScriptRoot 'Test-Package.ps1') -Path $packagePath
& (Join-Path $PSScriptRoot 'Build-Setup.ps1') -ReleaseDirectory $release -PfxPath $PfxPath -PfxPassword $PfxPassword
$zipPath = Join-Path $outputRoot ('DynamicIsland-Setup-' + $Version.ToString(3) + '-x64.zip')
Compress-Archive -LiteralPath $release -DestinationPath $zipPath -CompressionLevel NoCompression -Force
Write-Host "Installer ready: $release"
Write-Host "Installer ZIP: $zipPath"
