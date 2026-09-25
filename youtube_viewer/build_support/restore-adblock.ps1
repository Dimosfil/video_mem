$ErrorActionPreference = 'Stop'
$appRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lockPath = Join-Path $appRoot 'adblock.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$cache = Join-Path $appRoot 'build\adblock'
$extension = Join-Path $cache 'extension'
$archive = Join-Path $cache "ubol-$($lock.version).zip"
New-Item -ItemType Directory -Force -Path $cache | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri $lock.url -OutFile $archive -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $lock.sha256) {
    throw "uBO Lite archive hash mismatch: $archive. Remove this cached archive and restore again."
}
$stamp = Join-Path $cache 'restored.sha256'
$fingerprint = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash +
    (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
if ((Test-Path -LiteralPath $stamp) -and
    (Get-Content -LiteralPath $stamp -Raw) -eq $fingerprint -and
    (Test-Path -LiteralPath (Join-Path $extension 'manifest.json'))) { return }
$target = [IO.Path]::GetFullPath($extension)
if (-not $target.StartsWith($appRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe extension output' }
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
Expand-Archive -LiteralPath $archive -DestinationPath $target
$manifestPath = Join-Path $target 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $lock.version) { throw 'uBO Lite version mismatch' }
# A public identity key keeps one extension/settings identity across install paths.
# No signing key is needed or stored. Filtering code and rules stay upstream.
$manifest | Add-Member -NotePropertyName key -NotePropertyValue $lock.publicKey -Force
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $appRoot 'THIRD-PARTY-NOTICES.md') -Destination $target
[IO.File]::WriteAllText($stamp, $fingerprint, [Text.UTF8Encoding]::new($false))
Write-Output "Restored uBO Lite $($lock.version) (SHA256 verified)"
