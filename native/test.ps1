param([switch]$UiOnly, [string]$HostExecutable)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testOutput = Join-Path ([IO.Path]::GetTempPath()) ('PinyinVim-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testOutput | Out-Null
$previousLog = $env:NVIM_LOG_FILE
$env:NVIM_LOG_FILE = Join-Path $testOutput 'nvim.log'
Push-Location $projectRoot
try {
    $nvim = Join-Path $projectRoot 'runtime/nvim/bin/nvim.exe'
    $scripts = if ($UiOnly) { @('nvim-contract','draft-contract') } else {
        @('chinese','equivalence','find-equivalence','chinese-contract','navigation-regressions','nvim-contract','draft-contract','capsule-feedback','latency')
    }
    foreach ($script in $scripts) {
        $failurePath = Join-Path $PSScriptRoot "tests/$script.failures"
        if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
        $startInfo = New-Object Diagnostics.ProcessStartInfo
        $startInfo.FileName = $nvim
        $startInfo.Arguments = "--headless -n -u NONE -i NONE -S native/tests/$script.vim"
        $startInfo.WorkingDirectory = $projectRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($startInfo)
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(30000)) { $process.Kill(); throw "Headless Neovim timed out: $script" }
        $exitCode = $process.ExitCode
        $errors = $errorOutput.GetAwaiter().GetResult()
        $process.Dispose()
        if ($exitCode -ne 0) {
            if (Test-Path -LiteralPath $failurePath) { Get-Content -LiteralPath $failurePath }
            if ($errors) { Write-Output $errors }
            throw "Headless Neovim failed: $script"
        }
        Write-Output "$script passed (current Neovim runtime)"
    }
    $framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
    $contracts = @(
        @{Name='WorkerTests'; Sources=@('host/BoundedWorker.cs','tests/WorkerTests.cs'); References=@()},
        @{Name='PastePolicyTests'; Sources=@('host/PastePolicy.cs','tests/PastePolicyTests.cs'); References=@()},
        @{Name='FocusRestorePolicyTests'; Sources=@('host/FocusRestorePolicy.cs','tests/FocusRestorePolicyTests.cs'); References=@()},
        @{Name='EditorSessionTests'; Sources=@('host/EditorSession.cs','tests/EditorSessionTests.cs'); References=@()},
        @{Name='ImeControllerTests'; Sources=@('host/ImeStateController.cs','tests/ImeControllerTests.cs'); References=@()},
        @{Name='TargetIdentityTests'; Sources=@('host/WindowsIntegration.cs','host/FocusRestorePolicy.cs','host/BoundedWorker.cs','host/PastePolicy.cs','tests/TargetIdentityTests.cs'); References=@("/r:$framework/WPF/UIAutomationClient.dll","/r:$framework/WPF/UIAutomationTypes.dll","/r:$framework/WPF/WindowsBase.dll")}
    )
    foreach ($contract in $contracts) {
        $output = Join-Path $testOutput ($contract.Name + '.exe')
        $sources = @($contract.Sources | ForEach-Object { Join-Path $PSScriptRoot $_ })
        $compilerArgs = @('/nologo','/target:exe',"/out:$output") + $contract.References + $sources
        & $compiler $compilerArgs
        if ($LASTEXITCODE -ne 0) { throw "Contract compilation failed: $($contract.Name)" }
        & $output
        if ($LASTEXITCODE -ne 0) { throw "Contract failed: $($contract.Name)" }
    }
    if ($HostExecutable) {
        $hostPath = (Resolve-Path -LiteralPath $HostExecutable).Path
        $output = Join-Path $testOutput 'HostSubmitTests.exe'
        & $compiler /nologo /target:exe /r:System.Web.Extensions.dll "/out:$output" (Join-Path $PSScriptRoot 'tests/HostSubmitTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Host contract compilation failed' }
        & $output $hostPath
        if ($LASTEXITCODE -ne 0) { throw 'Actual host submission contract failed' }
        $version = (Get-Content -LiteralPath (Join-Path $projectRoot 'package.json') -Raw | ConvertFrom-Json).version
        $metadataTest = Join-Path $testOutput 'ReleaseMetadataTests.exe'
        & $compiler /nologo /target:exe "/out:$metadataTest" (Join-Path $PSScriptRoot 'tests/ReleaseMetadataTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Release metadata test compilation failed' }
        & $metadataTest $hostPath $version
        if ($LASTEXITCODE -ne 0) { throw 'Release metadata contract failed' }
        $notificationTest = Join-Path $testOutput 'NotificationSettingsTests.exe'
        $notificationReferences = @('/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll', "/r:$framework/System.Xaml.dll",
            "/r:$framework/WPF/WindowsBase.dll", "/r:$framework/WPF/PresentationCore.dll", "/r:$framework/WPF/PresentationFramework.dll")
        & $compiler /nologo /target:exe "/out:$notificationTest" $notificationReferences (Join-Path $PSScriptRoot 'tests/NotificationSettingsTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Notification preference test compilation failed' }
        & $notificationTest $hostPath
        if ($LASTEXITCODE -ne 0) { throw 'Notification preference and icon contract failed' }
    }
    if (!$UiOnly) { Get-Content native/tests/latency.json }
} finally {
    Pop-Location
    $env:NVIM_LOG_FILE = $previousLog
}
