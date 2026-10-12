$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & node native/vim/data/generate.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Dictionary generation failed; run npm ci first' }
    foreach ($name in @('chinese-words.txt','chinese-initials.txt')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('vim/data/' + $name)) -Destination (Join-Path $PSScriptRoot ('nvim/data/' + $name)) -Force
    }
} finally { Pop-Location }
