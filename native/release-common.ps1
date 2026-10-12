$ErrorActionPreference = 'Stop'
function Get-PinyinRelease {
    param([string]$Root = (Split-Path $PSScriptRoot -Parent))
    $metadata = Get-Content -LiteralPath (Join-Path $Root 'package.json') -Raw | ConvertFrom-Json
    if ($metadata.version -notmatch '^\d+\.\d+\.\d+$') { throw 'package.json version must be major.minor.patch' }
    $display = 'V' + $metadata.version
    [pscustomobject]@{Root=$Root;Version=[string]$metadata.version;Display=$display;FileVersion=([string]$metadata.version + '.0');
        Directory=(Join-Path $Root ('dist/' + $display));BuildDirectory=(Join-Path $Root ('dist/' + $display + '/build'));
        PortableDirectory=(Join-Path $Root ('dist/' + $display + '/PinyinVim-win32-x64'))}
}
function Write-PinyinVersionSource {
    param([string]$Path, $Release)
    $runtime = Get-Content -LiteralPath (Join-Path $Release.Root 'native/runtime-assets.json') -Raw | ConvertFrom-Json
    $text = @"
using System.Reflection;
[assembly: AssemblyVersion("$($Release.FileVersion)")]
[assembly: AssemblyFileVersion("$($Release.FileVersion)")]
[assembly: AssemblyInformationalVersion("$($Release.Version)")]
namespace PinyinVim.Native { internal static class ReleaseVersion {
    internal const string Value = "$($Release.Version)";
    internal const string Display = "V" + Value;
    internal const string Neovim = "$($runtime.nvim.version)";
    internal const string Neovide = "$($runtime.neovide.version)";
} }
"@
    [IO.File]::WriteAllText($Path, $text, (New-Object Text.UTF8Encoding($false)))
}
function Sync-PinyinLockMetadata {
    param([string]$Root)
    $metadata = Get-Content -LiteralPath (Join-Path $Root 'package.json') -Raw | ConvertFrom-Json
    $lockPath = Join-Path $Root 'package-lock.json'
    if (!(Test-Path -LiteralPath $lockPath)) { return }
    $isCore = $PSVersionTable.PSEdition -eq 'Core'
    if ($isCore) {
        # Hashtable parsing preserves package-lock's empty root-package key.
        $lock = [IO.File]::ReadAllText($lockPath) | ConvertFrom-Json -AsHashtable
    } else {
        Add-Type -AssemblyName System.Web.Extensions
        $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
        $lock = $serializer.DeserializeObject([IO.File]::ReadAllText($lockPath))
    }
    $rootPackage = $lock['packages']['']
    if ($lock.version -ne $metadata.version -or $rootPackage.version -ne $metadata.version -or $lock.name -ne $metadata.name -or $rootPackage.license -ne $metadata.license) {
        $lock.name = $metadata.name; $lock.version = $metadata.version
        $rootPackage.name = $metadata.name; $rootPackage.version = $metadata.version; $rootPackage.license = $metadata.license
        $lockJson = if ($isCore) { ConvertTo-Json -InputObject $lock -Depth 32 } else { $serializer.Serialize($lock) }
        [IO.File]::WriteAllText($lockPath,$lockJson,(New-Object Text.UTF8Encoding($false)))
    }
}
function Assert-PinyinOutputIdle {
    param([string]$Path)
    $prefix = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count) { throw "Output is running: $Path" }
}
