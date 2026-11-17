[CmdletBinding()]
param([string]$Tag = 'v1.1.9', [string]$ReleaseName = 'OC - Dynamic Island 0.0.2 — громкость и прозрачность')
$ErrorActionPreference = 'Stop'
$env:GCM_INTERACTIVE = 'Never'
$env:GIT_TERMINAL_PROMPT = '0'
$credentialLines = ('protocol=https' + [Environment]::NewLine + 'host=github.com' + [Environment]::NewLine + [Environment]::NewLine) | git credential fill 2>$null
$tokenLine = $credentialLines | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
if (!$tokenLine) { throw 'GitHub credential is unavailable.' }
$headers = @{ Authorization = 'Bearer ' + $tokenLine.Substring(9); Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'OC-Dynamic-Island-Publisher' }
$repo = 'poweredoutside/dynamic-island-for-windows'
$api = 'https://api.github.com/repos/' + $repo
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
if (git -C $root status --porcelain) { throw 'Commit the verified source before publishing.' }
$remote = (git -C $root ls-remote origin refs/heads/main).Split("`t")[0]
if ($remote -ne (git -C $root rev-parse HEAD).Trim()) { throw 'Push the verified source to origin/main before publishing.' }
$commit = (git -C $root rev-parse HEAD).Trim()
$notes = [IO.File]::ReadAllText((Join-Path $root 'CHANGELOG.md'))
$notes = ($notes -split '(?m)^## ')[1]
$notes = '## ' + $notes
$notes = $notes.Replace('(FEATURES.ru.md)', '(https://github.com/' + $repo + '/blob/main/FEATURES.ru.md)')
$payload = @{ tag_name = $Tag; target_commitish = $commit; name = $ReleaseName; body = $notes; draft = $true; prerelease = $false } | ConvertTo-Json
$releases = Invoke-RestMethod ($api + '/releases') -Headers $headers
$release = $releases | Where-Object { $_.tag_name -eq $Tag } | Select-Object -First 1
if (!$release) { $release = Invoke-RestMethod ($api + '/releases') -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($payload)) }
if (!$release.draft) { throw 'The release is already public; do not replace public assets.' }
$release.id | Set-Content (Join-Path $artifacts 'github-release-id.txt')
Add-Type -AssemblyName System.Net.Http
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromMinutes(20)
foreach ($key in $headers.Keys) { [void]$client.DefaultRequestHeaders.TryAddWithoutValidation($key, $headers[$key]) }
try {
    foreach ($name in @('ocdynamicisland.exe', 'SHA256SUMS.txt', 'PawnIO-2.2.0-source.zip')) {
        $path = Join-Path $artifacts $name
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $assets = Invoke-RestMethod ($api + '/releases/' + $release.id + '/assets') -Headers $headers
        $asset = $assets | Where-Object { $_.name -eq $name } | Select-Object -First 1
        if ($asset) {
            if ($asset.state -ne 'uploaded' -or $asset.digest -ne ('sha256:' + $hash)) { throw 'An existing draft asset does not match the local file.' }
            Write-Host ('Already verified: ' + $name)
            continue
        }
        $url = 'https://uploads.github.com/repos/' + $repo + '/releases/' + $release.id + '/assets?name=' + [Uri]::EscapeDataString($name)
        $stream = [IO.File]::OpenRead($path)
        $content = [Net.Http.StreamContent]::new($stream)
        $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
        try {
            Write-Host ('Uploading ' + $name + ' (' + $stream.Length + ' bytes)')
            $response = $client.PostAsync($url, $content).GetAwaiter().GetResult()
            try {
                if (!$response.IsSuccessStatusCode) { throw ('GitHub asset upload returned HTTP ' + [int]$response.StatusCode) }
                $uploaded = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                for ($attempt=0; $attempt -lt 5 -and !$uploaded.digest; $attempt++) {
                    Start-Sleep -Seconds 1
                    $uploaded = Invoke-RestMethod ($api + '/releases/assets/' + $uploaded.id) -Headers $headers
                }
                if ($uploaded.state -ne 'uploaded' -or $uploaded.size -ne (Get-Item -LiteralPath $path).Length -or $uploaded.digest -ne ('sha256:' + $hash)) { throw 'GitHub asset checksum or size mismatch.' }
                Write-Host ('Verified GitHub SHA-256: ' + $name)
            } finally { $response.Dispose() }
        } finally { $content.Dispose(); $stream.Dispose() }
    }
    $published = Invoke-RestMethod ($api + '/releases/' + $release.id) -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"make_latest":"true"}'
    $published.html_url | Set-Content (Join-Path $artifacts 'github-release-url.txt')
    Write-Host ('Published: ' + $published.html_url)
} finally { $client.Dispose() }
