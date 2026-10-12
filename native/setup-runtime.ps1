param([string]$CacheDirectory, [string]$LocalRuntime)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runtimeRoot = Join-Path $projectRoot 'runtime'
$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime-assets.json') -Raw | ConvertFrom-Json
if (!$CacheDirectory) { $CacheDirectory = Join-Path $runtimeRoot 'downloads' }
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
function Test-Executable($asset, [string]$runtimePath) {
    $path = Join-Path $runtimePath $asset.executable
    (Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $asset.executableSha256
}
foreach ($name in @('nvim','neovide')) {
    $asset = $assets.$name
    if (Test-Executable $asset $runtimeRoot) { Write-Output "$name $($asset.version) verified"; continue }
    if (Test-Path -LiteralPath (Join-Path $runtimeRoot $name)) { throw "Existing $name runtime does not match the pinned version; preserve it and use a clean source directory" }
    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    if ($LocalRuntime) {
        if (!(Test-Executable $asset $LocalRuntime)) { throw "Local $name runtime checksum mismatch" }
        Copy-Item -LiteralPath (Join-Path $LocalRuntime $name) -Destination $runtimeRoot -Recurse
    } else {
        $archive = Join-Path $CacheDirectory ($name + '-' + $asset.version + '.zip')
        if (!(Test-Path -LiteralPath $archive)) {
            $pending = $archive + '.pending'
            try {
                if (Get-Command node.exe -ErrorAction SilentlyContinue) {
                    & node.exe (Join-Path $PSScriptRoot 'download.mjs') $asset.url $pending
                    if ($LASTEXITCODE -ne 0) { throw "Download failed: $name" }
                } else {
                    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
                    Invoke-WebRequest -UseBasicParsing -Uri $asset.url -OutFile $pending -TimeoutSec 120
                }
                if ((Get-FileHash -LiteralPath $pending -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.archiveSha256) { throw "Archive checksum mismatch: $name" }
                Move-Item -LiteralPath $pending -Destination $archive
            } finally { if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending } }
        }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.archiveSha256) { throw "Cached archive checksum mismatch: $name" }
        $unpack = Join-Path ([IO.Path]::GetTempPath()) ('PinyinVim-runtime-' + [guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $archive -DestinationPath $unpack
        if ($name -eq 'nvim') {
            Copy-Item -LiteralPath (Join-Path $unpack 'nvim-win64') -Destination (Join-Path $runtimeRoot 'nvim') -Recurse
        } else {
            New-Item -ItemType Directory -Path (Join-Path $runtimeRoot 'neovide') | Out-Null
            Copy-Item -LiteralPath (Join-Path $unpack 'neovide.exe') -Destination (Join-Path $runtimeRoot 'neovide/neovide.exe')
        }
    }
    if (!(Test-Executable $asset $runtimeRoot)) { throw "Installed runtime checksum mismatch: $name" }
    Write-Output "$name $($asset.version) prepared and verified"
}
