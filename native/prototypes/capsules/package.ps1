param([switch]$UseExistingBuild, [switch]$FocusedUiChecks)
$ErrorActionPreference = 'Stop'
$product = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
& (Join-Path $product 'native/package.ps1') -UseExistingBuild:$UseExistingBuild -FocusedUiChecks:$FocusedUiChecks