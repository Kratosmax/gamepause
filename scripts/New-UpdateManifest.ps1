param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$PrivateKeyPath,
    [string]$Repository = 'Kratosmax/gamepause',
    [string]$ReleaseNotes = '',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$parsedVersion = $null
if (-not [Version]::TryParse($Version.TrimStart('v', 'V'), [ref]$parsedVersion)) {
    throw "Invalid release version: $Version"
}
$Version = $Version.TrimStart('v', 'V')
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$PrivateKeyPath = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
if (-not $OutputPath) { $OutputPath = Join-Path (Split-Path $PackagePath) 'latest.json' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)

$opensslCandidates = @(
    $env:OPENSSL_EXE,
    (Get-Command openssl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    'C:\Program Files\Git\usr\bin\openssl.exe'
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$openssl = $opensslCandidates | Select-Object -First 1
if (-not $openssl) { throw 'OpenSSL was not found. Set OPENSSL_EXE to its executable path.' }

$sha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToUpperInvariant()
$downloadUrl = "https://github.com/$Repository/releases/download/v$Version/GamePause-$Version.zip"
$payload = "$Version`n$downloadUrl`n$sha256"
$workingDirectory = Join-Path (Split-Path $OutputPath) ('.signing-' + [Guid]::NewGuid().ToString('N'))
$payloadPath = Join-Path $workingDirectory 'payload.txt'
$signaturePath = Join-Path $workingDirectory 'signature.bin'
$utf8NoBom = New-Object Text.UTF8Encoding($false)

New-Item -ItemType Directory -Path $workingDirectory -Force | Out-Null
try {
    [IO.File]::WriteAllText($payloadPath, $payload, $utf8NoBom)
    & $openssl dgst -sha256 -sign $PrivateKeyPath -out $signaturePath $payloadPath
    if ($LASTEXITCODE -ne 0) { throw 'OpenSSL failed to sign the update manifest.' }
    $signature = [Convert]::ToBase64String([IO.File]::ReadAllBytes($signaturePath))
    $manifest = [ordered]@{
        version = $Version
        downloadUrl = $downloadUrl
        sha256 = $sha256
        signature = $signature
        releaseNotes = $ReleaseNotes
    } | ConvertTo-Json
    [IO.File]::WriteAllText($OutputPath, $manifest + [Environment]::NewLine, $utf8NoBom)
}
finally {
    if (Test-Path -LiteralPath $workingDirectory) {
        Remove-Item -LiteralPath $workingDirectory -Recurse -Force
    }
}

Write-Host "Update manifest created at $OutputPath"
Write-Host "Package SHA-256: $sha256"
