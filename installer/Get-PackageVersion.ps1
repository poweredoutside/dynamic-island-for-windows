[CmdletBinding()]
param(
    [Parameter(Mandatory)][version]$Version,
    [version[]]$KnownPackages = @()
)

if ($Version.Build -lt 0 -or $Version.Major -lt 1 -or
    $Version.Major -gt 65535 -or $Version.Minor -gt 65535 -or
    $Version.Build -gt 65535 -or $Version.Revision -gt 65535) {
    throw "Invalid MSIX version: $Version. Use major.minor.build or major.minor.build.revision."
}

# An explicit four-part version is useful for reproducible packaging.
if ($Version.Revision -ge 0) { return $Version }

$revision = 0
foreach ($known in $KnownPackages) {
    if ($known.Major -eq $Version.Major -and $known.Minor -eq $Version.Minor -and $known.Build -eq $Version.Build) {
        $revision = [Math]::Max($revision, $known.Revision + 1)
    }
}
if ($revision -gt 65535) { throw "There are no remaining MSIX revisions for $Version." }
return [version]::new($Version.Major, $Version.Minor, $Version.Build, $revision)
