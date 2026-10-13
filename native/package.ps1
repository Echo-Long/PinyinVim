param([switch]$UseExistingBuild, [switch]$FocusedUiChecks)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
$release = Get-PinyinRelease
if (!$UseExistingBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$entry = Join-Path $release.BuildDirectory 'PinyinVim.exe'
if (!(Test-Path -LiteralPath $entry)) { throw 'Build the current release first' }
& (Join-Path $PSScriptRoot 'test.ps1') -UiOnly:$FocusedUiChecks -HostExecutable $entry
Assert-PinyinOutputIdle -Path $release.PortableDirectory
if (Test-Path -LiteralPath $release.PortableDirectory) { throw 'Portable release directory already exists; preserve it before repackaging' }
New-Item -ItemType Directory -Path $release.PortableDirectory | Out-Null
foreach ($folder in @('runtime','fonts','licenses','nvim')) {
    Copy-Item -LiteralPath (Join-Path $release.BuildDirectory $folder) -Destination $release.PortableDirectory -Recurse
}
foreach ($file in @('PinyinVim.exe','CapsuleWindowPolicy.dll')) { Copy-Item -LiteralPath (Join-Path $release.BuildDirectory $file) -Destination $release.PortableDirectory }
foreach ($file in @('LICENSE','docs/THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $release.Root $file) -Destination $release.PortableDirectory }
$template = Get-Content -LiteralPath (Join-Path $release.Root 'docs/release-notes.template.md') -Raw -Encoding UTF8
$notes = $template.Replace('{{DISPLAY_VERSION}}',$release.Display).Replace('{{VERSION}}',$release.Version)
$encoding = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $release.Directory 'RELEASE_NOTES.md'),$notes,$encoding)
[IO.File]::WriteAllText((Join-Path $release.PortableDirectory 'RELEASE_NOTES.md'),$notes,$encoding)
$portableReadme = (Get-Content -LiteralPath (Join-Path $release.Root 'docs/portable-readme.md') -Raw -Encoding UTF8).Replace('{{DISPLAY_VERSION}}',$release.Display)
[IO.File]::WriteAllText((Join-Path $release.PortableDirectory 'README-zh.md'),$portableReadme,$encoding)
$portableEnglish = (Get-Content -LiteralPath (Join-Path $release.Root 'docs/portable-readme.en.md') -Raw -Encoding UTF8).Replace('{{DISPLAY_VERSION}}',$release.Display)
[IO.File]::WriteAllText((Join-Path $release.PortableDirectory 'README.md'),$portableEnglish,$encoding)
$manifest = [ordered]@{version=$release.Version;displayVersion=$release.Display;entry='PinyinVim.exe';platform='windows-x64';clipboardNotification='controlled by settings.notifications; enabled by default';files=@()}
foreach ($file in @(Get-ChildItem -LiteralPath $release.PortableDirectory -Recurse -File | Sort-Object FullName)) {
    $relative = $file.FullName.Substring($release.PortableDirectory.Length+1).Replace('\','/')
    $manifest.files += [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
}
[IO.File]::WriteAllText((Join-Path $release.PortableDirectory 'manifest.json'),($manifest|ConvertTo-Json -Depth 6),$encoding)
& (Join-Path $PSScriptRoot 'verify-release.ps1') -Directory $release.PortableDirectory
$sourceRoot = Join-Path $release.Directory 'source/PinyinVim'
if (Test-Path -LiteralPath $sourceRoot) { throw 'Source staging directory already exists; preserve it before repackaging' }
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
$allow = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-files.json') -Raw | ConvertFrom-Json
$sourceFiles = @($allow.files)
foreach ($tree in $allow.trees) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $release.Root $tree) -Recurse -File) {
        $sourceFiles += $file.FullName.Substring($release.Root.Length+1).Replace('\','/')
    }
}
foreach ($testFile in $allow.tests) { $sourceFiles += 'native/tests/' + $testFile }
foreach ($relative in @($sourceFiles | Sort-Object -Unique)) {
    $source = Join-Path $release.Root $relative
    if (!(Test-Path -LiteralPath $source)) { throw "Source allowlist file missing: $relative" }
    $destination = Join-Path $sourceRoot $relative
    New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$portableZip = Join-Path $release.Directory ('PinyinVim-' + $release.Display + '-win32-x64.zip')
$sourceZip = Join-Path $release.Directory ('PinyinVim-' + $release.Display + '-source.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($release.PortableDirectory,$portableZip,[IO.Compression.CompressionLevel]::Optimal,$true)
[IO.Compression.ZipFile]::CreateFromDirectory($sourceRoot,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$true)
$checksums = foreach ($path in @($portableZip,$sourceZip)) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($path) }
[IO.File]::WriteAllLines((Join-Path $release.Directory 'SHA256SUMS.txt'),[string[]]$checksums,$encoding)
Write-Output $portableZip
Write-Output $sourceZip
Write-Output $sourceRoot
