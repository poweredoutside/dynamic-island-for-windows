$ErrorActionPreference = 'Stop'
$dependencyRoot = Join-Path (Split-Path -Parent $PSScriptRoot) '.tools\hardware'
New-Item -ItemType Directory -Force -Path $dependencyRoot | Out-Null
$path = Join-Path $dependencyRoot 'PawnIO_setup.exe'
$expected = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
if (!(Test-Path -LiteralPath $path)) {
    Invoke-WebRequest -UseBasicParsing 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $path
}
if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw 'PawnIO installer SHA-256 mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $path
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne 'F380DCC9F706E2756A5047B832FFE719E1BC35F5') {
    throw 'PawnIO installer signature is invalid.'
}
# Distribute the corresponding driver source, including its pinned submodules,
# alongside the binary release so recipients can rebuild the GPL driver.
$source = Join-Path $dependencyRoot 'PawnIO-source'
$sourceZip = Join-Path $dependencyRoot 'PawnIO-2.2.0-source.zip'
if (!(Test-Path -LiteralPath $sourceZip)) {
    if (!(Test-Path -LiteralPath $source)) {
        & git clone --depth 1 --branch 2.2.0 --recurse-submodules --shallow-submodules https://github.com/namazso/PawnIO.git $source | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not download PawnIO source.' }
    }
    if ((& git -C $source rev-parse HEAD).Trim() -ne '5cdf470831fdfff3f7f1d06363ca6b230f3bf35a') { throw 'Unexpected PawnIO source revision.' }
    & git -C $source submodule update --init --recursive --depth 1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not download PawnIO source dependencies.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse -Force) {
            $relative = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
            if ($relative -match '(^|/)\.git(/|$)') { continue }
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, ('PawnIO-2.2.0/' + $relative))
        }
    } finally { $zip.Dispose() }
}
Write-Output $path
