param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
$release = Get-PinyinRelease
$root = (Resolve-Path -LiteralPath $Directory).Path
$manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $release.Version) { throw 'Release version mismatch' }
$entry = Join-Path $root 'PinyinVim.exe'
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($entry)
if ($fileVersion.FileVersion -ne $release.FileVersion -or $fileVersion.ProductVersion -ne $release.Version) { throw 'Executable metadata version mismatch' }
$forbidden = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
    $_.Name -match '^(draft\.txt|settings\.json|last-submitted\.txt|owned-processes\.json|test-submitted\.txt)$' -or
    $_.Extension -in @('.log','.jsonl','.pending','.zip','.7z') -or $_.FullName -match '[\\/](prototype-profile|test-profile|build-source|downloads)[\\/]'
})
if ($forbidden.Count) { throw 'Release contains local state or build/download artifacts' }
$topEntries = @(Get-ChildItem -LiteralPath $root -Filter '*.exe' -File)
if ($topEntries.Count -ne 1 -or $topEntries[0].Name -ne 'PinyinVim.exe') { throw 'Release must have one top-level executable' }
$rootManifest = Join-Path $root 'manifest.json'
$payload = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object FullName -ne $rootManifest)
if ($payload.Count -ne $manifest.files.Count) { throw 'Manifest file count mismatch' }
foreach ($file in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $file.path))
    if (!$path.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes package' }
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Release checksum mismatch: $($file.path)" }
}
foreach ($licenseFile in @('Neovim-APACHE-2.0.txt','Neovim-Vim-runtime-license.txt','Neovide-MIT.txt','jieba-MIT.txt','pinyin-pro-MIT.txt','SarasaMonoSC-OFL-1.1.txt','ICONS-LICENSE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $root ('licenses/' + $licenseFile)))) { throw "Missing license: $licenseFile" }
}
Write-Output "$($release.Display): $($payload.Count) payload files verified; no local profile or logs"
