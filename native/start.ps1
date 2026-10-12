param([switch]$Background)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
$release = Get-PinyinRelease
$executable = Join-Path $release.PortableDirectory 'PinyinVim.exe'
if (!(Test-Path -LiteralPath $executable)) { $executable = Join-Path $release.BuildDirectory 'PinyinVim.exe' }
if (!(Test-Path -LiteralPath $executable)) { throw '请先运行 npm run build 或 native/build.ps1' }
if ($Background) { Start-Process -FilePath $executable -ArgumentList '--background' -WindowStyle Hidden }
else { Start-Process -FilePath $executable -WindowStyle Hidden }