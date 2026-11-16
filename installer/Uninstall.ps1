$ErrorActionPreference = 'Stop'
$packages = @(Get-AppxPackage -Name DynamicIslandWin)
foreach ($package in $packages) {
    if ([version]$package.Version -ge [version]'1.0.4.0') {
        $executable = Join-Path $package.InstallLocation 'DynamicIslandWin.exe'
        # Graceful shutdown restores Windows banners before package data is removed.
        $shutdown = Start-Process -FilePath $executable -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
        if (!$shutdown.WaitForExit(5000)) { throw 'OC - Dynamic Island shutdown timed out. Exit it from the tray and retry.' }
        $until = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $running = @(Get-Process -Name DynamicIslandWin -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable })
            if ($running.Count -eq 0) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $until)
        if ($running.Count -gt 0) { throw 'OC - Dynamic Island is still running. Exit it from the tray and retry.' }
    }
    Remove-AppxPackage -Package $package.PackageFullName
}
Write-Host 'OC - Dynamic Island removed. Existing portable settings are retained.'
