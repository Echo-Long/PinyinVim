param([string]$OutputDirectory, [switch]$Start)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
$release = Get-PinyinRelease
Sync-PinyinLockMetadata -Root $release.Root
& (Join-Path $PSScriptRoot 'setup-runtime.ps1')
if (!$OutputDirectory) { $OutputDirectory = $release.BuildDirectory }
& (Join-Path $PSScriptRoot 'prototypes/capsules/build.ps1') -OutputDirectory $OutputDirectory -EntryName PinyinVim.exe -Start:$Start
