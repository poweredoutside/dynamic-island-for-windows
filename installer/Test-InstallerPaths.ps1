$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$fixtureRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts\installer-path-checks-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $PSScriptRoot 'Install.ps1'
$checks = 0
function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ($Actual -ne $Expected) { throw "$Message (expected '$Expected', got '$Actual')" }
    $script:checks++
}
function Add-Package([string]$Directory, [switch]$WithoutCertificate) {
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    New-Item -ItemType File -Path (Join-Path $Directory 'DynamicIsland.msix') | Out-Null
    if (!$WithoutCertificate) { New-Item -ItemType File -Path (Join-Path $Directory 'DynamicIsland.cer') | Out-Null }
}
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
try {
    # Source checkout with multiple versions: sort versions numerically and ignore
    # unfinished builds, not alphabetically or by filesystem modification time.
    $checkout = Join-Path $fixtureRoot 'source with spaces'
    $scripts = Join-Path $checkout 'installer'
    New-Item -ItemType Directory -Force -Path $scripts | Out-Null
    Copy-Item -LiteralPath $source -Destination $scripts
    New-Item -ItemType File -Path (Join-Path $checkout 'DynamicIslandWin.csproj'), (Join-Path $scripts 'Build-Installer.ps1') | Out-Null
    Add-Package (Join-Path $checkout 'artifacts\DynamicIsland-1.0.9.0-x64')
    $latest = Join-Path $checkout 'artifacts\DynamicIsland-1.0.10.0-x64'
    Add-Package $latest
    Add-Package (Join-Path $checkout 'artifacts\DynamicIsland-2.0.0.0-x64') -WithoutCertificate
    Add-Package (Join-Path $checkout 'artifacts\build-temporary')
    $resolved = & (Join-Path $scripts 'Install.ps1') -ResolveOnly
    Assert-Equal $resolved.PackagePath (Join-Path $latest 'DynamicIsland.msix') 'Latest complete source release'
    Assert-Equal $resolved.CertificatePath (Join-Path $latest 'DynamicIsland.cer') 'Certificate beside selected release'

    # Distributed bundle must use its own package, even inside a checkout.
    Add-Package $scripts
    $resolved = & (Join-Path $scripts 'Install.ps1') -ResolveOnly
    Assert-Equal $resolved.PackagePath (Join-Path $scripts 'DynamicIsland.msix') 'Sibling package takes precedence'
    Push-Location $env:TEMP
    try { $resolved = & (Join-Path $scripts 'Install.ps1') -ResolveOnly }
    finally { Pop-Location }
    Assert-Equal $resolved.PackagePath (Join-Path $scripts 'DynamicIsland.msix') 'Independent of working directory'

    $missing = Join-Path $fixtureRoot 'missing bundle'
    New-Item -ItemType Directory -Path $missing | Out-Null
    Copy-Item -LiteralPath $source -Destination $missing
    $failure = $null
    try { & (Join-Path $missing 'Install.ps1') -ResolveOnly } catch { $failure = $_.Exception.Message }
    Assert-Equal ($failure -like 'Installation package not found.*') $true 'Clear missing-package error'
    Add-Package $missing -WithoutCertificate
    $failure = $null
    try { & (Join-Path $missing 'Install.ps1') -ResolveOnly } catch { $failure = $_.Exception.Message }
    Assert-Equal ($failure -like 'Signing certificate not found:*') $true 'Clear missing-certificate error'
    Write-Host "PASS: $checks installer path checks."
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedFixture.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
