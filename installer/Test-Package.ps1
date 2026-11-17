param([string]$Path = (Join-Path $PSScriptRoot '..\artifacts\DynamicIsland-1.1.9.0-x64\DynamicIsland.msix'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path))
try {
    foreach ($required in @('AppxManifest.xml', 'AppxSignature.p7x', 'AppxBlockMap.xml', 'DynamicIslandWin.exe', 'DynamicIslandWin.dll', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll', 'Activation/NotificationActivationHost.exe', 'Assets/StoreLogo.png', 'Assets/Square44x44Logo.png', 'Assets/Square150x150Logo.png')) {
        if (!$archive.GetEntry($required)) { throw "Missing package file: $required" }
    }
    $hostEntry = $archive.GetEntry('Activation/NotificationActivationHost.exe')
    if (!$archive.GetEntry('LibreHardwareMonitorLib.dll') -or !$archive.GetEntry('Hardware/PawnIO_setup.exe') -or !$archive.GetEntry('licenses/LibreHardwareMonitor/LICENSE')) { throw 'CPU temperature dependency or its license is missing.' }
    $driver = $archive.GetEntry('Hardware/PawnIO_setup.exe').Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        if ([BitConverter]::ToString($sha.ComputeHash($driver)).Replace('-', '') -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032') { throw 'Bundled CPU temperature driver checksum mismatch.' }
    } finally { $sha.Dispose(); $driver.Dispose() }
    if ($hostEntry.Length -lt 30000000) { throw 'Activation host is missing its self-contained runtime.' }
    $hostReader = [IO.BinaryReader]::new($hostEntry.Open())
    try { $hostHeader = $hostReader.ReadBytes(4096) } finally { $hostReader.Dispose() }
    $peOffset = [BitConverter]::ToInt32($hostHeader, 0x3c)
    if ([BitConverter]::ToUInt16($hostHeader, $peOffset + 92) -ne 2) { throw 'Activation host would open a console.' }
    if (@($archive.Entries | Where-Object { $_.FullName -match '\.(pfx|p12)$' }).Count -gt 0) { throw 'Private key included in package' }
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.Package.Identity.Name -ne 'DynamicIslandWin') { throw 'Unexpected package identity' }
    if ($manifest.Package.Properties.DisplayName -ne 'OC - Dynamic Island' -or $manifest.Package.Applications.Application.VisualElements.DisplayName -ne 'OC - Dynamic Island') { throw 'Incorrect product display name' }
    if (@($manifest.Package.Capabilities.ChildNodes | Where-Object { $_.NamespaceURI -eq 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3' -and $_.GetAttribute('Name') -eq 'userNotificationListener' }).Count -ne 1) { throw 'Notification capability missing' }
    if (!$manifest.SelectSingleNode('//*[local-name()="StartupTask" and @TaskId="DynamicIslandStartup"]')) { throw 'Startup task missing' }
    if ($manifest.Package.Applications.Application.Executable -ne 'DynamicIslandWin.exe') { throw 'Wrong entry point' }
    if (!$manifest.SelectSingleNode('//*[local-name()="RegistryWriteVirtualization" and text()="disabled"]')) { throw 'Real Windows notification settings would be virtualized' }
    if (!$manifest.SelectSingleNode('//*[local-name()="Capability" and @Name="unvirtualizedResources"]')) { throw 'Shared notification settings capability missing' }
    if (!$manifest.SelectSingleNode('//*[local-name()="Protocol" and @Name="dynamicisland-capture" and contains(@Parameters,"--capture-response")]')) { throw 'Screen recording callback missing' }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $Path)) 'DynamicIsland.cer'))
    try {
        if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Wrong signing certificate' }
        if ($manifest.Package.Identity.Publisher -ne $certificate.Subject) { throw 'Publisher mismatch' }
    } finally { $certificate.Dispose() }
    Write-Host "PASS: signed MSIX structure, notification capability, self-contained runtime, startup entry, publisher, assets; $($archive.Entries.Count) entries."
}
finally { $archive.Dispose() }
