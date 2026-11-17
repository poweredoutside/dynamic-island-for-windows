$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$fixture = Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts\installer-update-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $fixture
    New-Item -ItemType File -Path (Join-Path $fixture 'DynamicIsland.cer') | Out-Null
    $zip = [IO.Compression.ZipFile]::Open((Join-Path $fixture 'DynamicIsland.msix'), [IO.Compression.ZipArchiveMode]::Create)
    try {
        $writer = [IO.StreamWriter]::new($zip.CreateEntry('AppxManifest.xml').Open())
        try { $writer.Write('<Package><Identity Name="DynamicIslandWin" Publisher="CN=DynamicIslandWin" Version="1.0.1.0" ProcessorArchitecture="x64" /></Package>') }
        finally { $writer.Dispose() }
    } finally { $zip.Dispose() }

    # PlanOnly must never reach deployment or removal, even with a conflict.
    function Add-AppxPackage { throw 'Unexpected deployment during update planning' }
    function Remove-AppxPackage { throw 'Installer must never uninstall existing app data' }
    function Get-AppxPackage { param([string]$Name) if ($null -ne $global:DynamicIslandUpdateTestPackage) { $global:DynamicIslandUpdateTestPackage } }
    $cases = @(
        @{ Version = $null; Architecture = 'Neutral'; Expected = 'Install' },
        @{ Version = '1.0.0.0'; Architecture = 'Neutral'; Expected = 'Update' },
        @{ Version = '1.0.0.0'; Architecture = 'X64'; Expected = 'Update' },
        @{ Version = '1.0.1.0'; Architecture = 'X64'; Expected = 'AlreadyInstalled' },
        @{ Version = '1.0.1.0'; Architecture = 'Neutral'; Expected = 'RequireNewerVersion' },
        @{ Version = '1.0.2.0'; Architecture = 'X64'; Expected = 'RequireNewerVersion' }
    )
    foreach ($case in $cases) {
        $global:DynamicIslandUpdateTestPackage = if ($case.Version) {
            [pscustomobject]@{ Version = $case.Version; Architecture = $case.Architecture; Publisher = 'CN=DynamicIslandWin'; PackageFullName = 'test-existing-package' }
        } else { $null }
        $plan = & (Join-Path $fixture 'Install.ps1') -PlanOnly -InstallCpuTemperatureDriver
        if ($plan.Action -ne $case.Expected) { throw "Unexpected update plan for $($case.Version)/$($case.Architecture): $($plan.Action)" }
    }
    Write-Host "PASS: $($cases.Count) installer update scenarios, including neutral-to-x64 migration."
}
finally {
    Remove-Variable -Name DynamicIslandUpdateTestPackage -Scope Global -ErrorAction SilentlyContinue
    $absoluteFixture = [IO.Path]::GetFullPath($fixture)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (!$absoluteFixture.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path' }
    Remove-Item -LiteralPath $absoluteFixture -Recurse -Force
}
