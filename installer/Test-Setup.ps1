[CmdletBinding()]
param(
    [string]$Path,
    [string]$PackagePath
)
$ErrorActionPreference = 'Stop'
if (!$Path) { $Path = Join-Path $PSScriptRoot '..\artifacts\ocdynamicisland.exe' }
if (!$PackagePath) { $PackagePath = Join-Path $PSScriptRoot '..\artifacts\DynamicIsland-1.1.5.0-x64\DynamicIsland.msix' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $projectRoot ('artifacts\setup-check-' + [guid]::NewGuid().ToString('N'))
$isolated = Join-Path $fixture 'standalone with spaces'
New-Item -ItemType Directory -Path $isolated -Force | Out-Null
try {
    # No package, certificate, scripts or runtime beside the executable.
    $setup = Join-Path $isolated 'ocdynamicisland.exe'
    Copy-Item -LiteralPath $Path -Destination $setup
    $reportPath = Join-Path $fixture 'report.json'
    $process = Start-Process -FilePath $setup -ArgumentList @('--verify-payload', ('"' + $reportPath + '"')) -WindowStyle Hidden -Wait -PassThru
    if (!(Test-Path -LiteralPath $reportPath)) { throw "Setup did not produce a self-test report (exit $($process.ExitCode))." }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or !$report.success) { throw (Get-Content -LiteralPath $reportPath -Raw) }
    if ($report.subsystem -ne 'WindowsGui' -or !$report.hiddenPowerShell) { throw 'Installer opened a console.' }
    if ($report.embeddedPackageSha256 -ne (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash) { throw 'Embedded package differs from release.' }
    $setupSignature = Get-AuthenticodeSignature -LiteralPath $setup
    $packageSignature = Get-AuthenticodeSignature -LiteralPath $PackagePath
    if (!$setupSignature.SignerCertificate -or $setupSignature.SignerCertificate.Thumbprint -ne $packageSignature.SignerCertificate.Thumbprint) { throw 'Setup publisher differs from package publisher.' }
    $preview = Join-Path $projectRoot 'artifacts\setup-preview.png'
    $render = Start-Process -FilePath $setup -ArgumentList @('--render-preview', ('"' + $preview + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($render.ExitCode -ne 0 -or !(Test-Path -LiteralPath $preview)) { throw 'GUI setup preview failed.' }
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $projectRoot 'artifacts\setup-verification.json') -Force
    Write-Host ('PASS: standalone signed ocdynamicisland.exe; matching embedded package; Windows GUI subsystem; hidden child console; update plan ' + $report.plan.Plan.Action + '; existing-install flow=' + $report.existingInstallFlowChecked + '; offscreen GUI preview.')
}
finally {
    $target = [IO.Path]::GetFullPath($fixture)
    $allowed = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (!$target.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe setup test cleanup path' }
    Remove-Item -LiteralPath $target -Recurse -Force
}
