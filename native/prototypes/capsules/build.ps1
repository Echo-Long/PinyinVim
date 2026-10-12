param([switch]$Start, [switch]$UseExistingRuntime, [string]$OutputDirectory, [string]$PresentationBaseline, [ValidateSet("PinyinVim-Capsules.exe","PinyinVim.exe")][string]$EntryName = "PinyinVim.exe")
$ErrorActionPreference = 'Stop'
$product = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
. (Join-Path $product 'native/release-common.ps1')
$releaseInfo = Get-PinyinRelease -Root $product
$release = $product
if ($PresentationBaseline) {
    $release = (Resolve-Path -LiteralPath $PresentationBaseline -ErrorAction Stop).Path
    foreach ($relative in @('CapsuleWindowPolicy.dll','nvim/lua/pinyinvim/capsules.lua','runtime/nvim/bin/PinyinVim-GuiBridge.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $release $relative))) { throw "Incomplete presentation baseline: $relative" }
    }
}
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { $releaseInfo.BuildDirectory }
$outputPrefix = $output.TrimEnd('\') + '\'
$active = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase) })
if ($active.Count -gt 0) { throw "Output is running; choose a separate output directory: $output" }
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
New-Item -ItemType Directory -Force $output | Out-Null
if ($PresentationBaseline) {
    Copy-Item -LiteralPath (Join-Path $release 'CapsuleWindowPolicy.dll') -Destination $output -Force
} else {
    $gcc = (Get-Command gcc.exe -ErrorAction Stop).Source
    & $gcc '-shared' '-O2' '-Wall' '-Wextra' '-static-libgcc' (Join-Path $PSScriptRoot 'window-policy.c') '-o' (Join-Path $output 'CapsuleWindowPolicy.dll') '-luser32' '-lkernel32' '-lgdi32' '-ldwmapi' '-lole32' '-ld3d11' '-ldxgi' '-lwindowscodecs'
    if ($LASTEXITCODE -ne 0) { throw 'Prototype native window policy compilation failed' }
}
foreach ($folder in @('runtime')) {
    if (!$UseExistingRuntime) {
        New-Item -ItemType Directory -Force (Join-Path $output 'runtime') | Out-Null
        foreach ($component in @('nvim','neovide')) {
            Copy-Item -LiteralPath (Join-Path $release ('runtime/' + $component)) -Destination (Join-Path $output 'runtime') -Recurse -Force
        }
    }
    elseif (!(Test-Path -LiteralPath (Join-Path $output $folder))) { throw "Missing existing prototype folder: $folder" }
}
foreach ($folder in @('fonts','licenses')) { New-Item -ItemType Directory -Force (Join-Path $output $folder) | Out-Null }
Copy-Item -LiteralPath (Join-Path $product 'app/fonts/SarasaMonoSC-Regular.ttf') -Destination (Join-Path $output 'fonts') -Force
Copy-Item -LiteralPath (Join-Path $product 'app/fonts/OFL-1.1.txt') -Destination (Join-Path $output 'licenses/SarasaMonoSC-OFL-1.1.txt') -Force
Copy-Item -LiteralPath (Join-Path $product 'native/vendor/jieba-LICENSE') -Destination (Join-Path $output 'licenses/jieba-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $product 'native/vendor/neovide-LICENSE.txt') -Destination (Join-Path $output 'licenses/Neovide-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $product 'native/vendor/pinyin-pro-LICENSE.txt') -Destination (Join-Path $output 'licenses/pinyin-pro-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $product 'runtime/nvim/share/nvim/runtime/doc/uganda.txt') -Destination (Join-Path $output 'licenses/Neovim-Vim-runtime-license.txt') -Force
Copy-Item -LiteralPath (Join-Path $product 'native/vendor/neovim-APACHE-2.0.txt') -Destination (Join-Path $output 'licenses/Neovim-APACHE-2.0.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/PinyinVimIcons.ttf') -Destination (Join-Path $output 'fonts') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/ICONS-LICENSE.txt') -Destination (Join-Path $output 'licenses') -Force
if (!$PresentationBaseline) {
    & (Join-Path $framework 'csc.exe') '/nologo' '/target:exe' '/platform:x64' '/optimize+' '/r:System.dll' '/r:System.Core.dll' "/out:$output/runtime/nvim/bin/PinyinVim-GuiBridge.exe" (Join-Path $PSScriptRoot 'CapsuleGuiBridge.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Capsule presentation adapter compilation failed' }
}
Copy-Item -LiteralPath (Join-Path $product 'native/nvim') -Destination $output -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $output 'nvim/lua/pinyinvim') | Out-Null
$capsuleLua = Join-Path $PSScriptRoot 'lua/pinyinvim/capsules.lua'
Copy-Item -LiteralPath $capsuleLua -Destination (Join-Path $output 'nvim/lua/pinyinvim/capsules.lua') -Force
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Replace-Once([string]$text, [string]$before, [string]$after) {
    $text = $text.Replace("`r`n", "`n")
    $before = $before.Replace("`r`n", "`n")
    $after = $after.Replace("`r`n", "`n")
    if ([regex]::Matches($text, [regex]::Escape($before)).Count -ne 1) {
        throw "Prototype source anchor missing or duplicated: $before"
    }
    return $text.Replace($before, $after)
}
$init = Join-Path $output 'nvim/init.vim'
[System.IO.File]::WriteAllText($init, [System.IO.File]::ReadAllText($init).Replace('guifont=Consolas,等距更纱黑体\ SC:h14','guifont=Consolas,PinyinVim\ Icons,Microsoft\ YaHei\ UI,等距更纱黑体\ SC:h14'), $utf8)
[System.IO.File]::AppendAllText($init, "`nlua require('pinyinvim.capsules').setup()`n", $utf8)
$bridge = Join-Path $output 'nvim/plugin/bridge.vim'
$bridgeText = [System.IO.File]::ReadAllText($bridge).Replace("  elseif l:kind ==# 'quit'", "  elseif l:kind ==# 'capsule-geometry'`n    call luaeval(`"require('pinyinvim.capsules').on_geometry(_A)`", a:message)`n  elseif l:kind ==# 'quit'")
$bridgeText += @'

function! PinyinCapsuleMetrics(columns, rows, scale, font, bounds, revision) abort
  call s:Send({'kind':'capsule-metrics', 'columns':a:columns, 'rows':a:rows, 'scale':a:scale, 'font':a:font, 'bounds':a:bounds, 'revision':a:revision})
endfunction

function! PinyinCapsuleAdjustFont(delta) abort
  let s:settings.fontSize = max([10, min([28, get(s:settings, 'fontSize', 14) + a:delta])])
  call s:ApplySettings(s:settings)
  call s:Send({'kind':'settings-update','settings':s:settings}, 1)
endfunction
'@
$bridgeText = $bridgeText.Replace("execute 'set guifont=Consolas,等距更纱黑体\ SC:h'", "execute 'set guifont=Consolas,PinyinVim\ Icons,Microsoft\ YaHei\ UI,等距更纱黑体\ SC:h'")
$bridgeText = Replace-Once $bridgeText "  let l:size = max([10, min([28, get(a:settings, 'fontSize', 14)])])" "  call luaeval(`"require('pinyinvim.capsules').apply_settings(_A)`", a:settings)`n  let l:size = max([10, min([28, get(a:settings, 'fontSize', 14)])])"
$bridgeText = Replace-Once $bridgeText 'function! PinyinSettingsView() abort' 'function! s:BaseSettingsView() abort'
$bridgeText = Replace-Once $bridgeText "  elseif l:kind ==# 'capsule-geometry'" "  elseif l:kind ==# 'capsule-notice'`n    let g:pinyinvim_notice = get(a:message, 'text', '')`n    lua require('pinyinvim.capsules').render()`n  elseif l:kind ==# 'capsule-geometry'"
$bridgeText += "`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'capsule-settings.vim'))
$bridgeText = Replace-Once $bridgeText "  elseif l:kind ==# 'capsule-geometry'" "  elseif l:kind ==# 'capsule-hover'`n    call luaeval(`"require('pinyinvim.capsules').set_hover(_A)`", get(a:message, 'panel', ''))`n  elseif l:kind ==# 'capsule-geometry'"
[System.IO.File]::WriteAllText($bridge, $bridgeText.Replace("`r`n", "`n"), $utf8)

# Generate a separate host from the current sources. The main host/config and
# published release are never edited. Fail if a source anchor has changed.
$program = [System.IO.File]::ReadAllText((Join-Path $product 'native/host/Program.cs')).Replace("`r`n", "`n")
$program = Replace-Once $program 'bool test = Array.IndexOf(args, "--test") >= 0;' 'bool test = true;'
$uniqueStart = $program.IndexOf('                if (!unique)')
$uniqueEnd = $program.IndexOf('                // A normal double-click', $uniqueStart)
if ($uniqueStart -lt 0 -or $uniqueEnd -lt 0) { throw 'Missing instance-reuse anchor' }
$program = $program.Substring(0, $uniqueStart) + "                if (!unique) { if (!CapsuleNative.OpenExistingInstance() && Array.IndexOf(args, `"--background`") < 0) MessageBox.Show(`"旧版胶囊实例仍在运行。请从旧版托盘退出后再启动新版；不会显示旧版编辑会话。`", `"拼音浮笺`"); return; }`n" + $program.Substring($uniqueEnd)
$program = Replace-Once $program '"Local\\PinyinVim.Native." + (test ? "Test" : "Main")' '"Local\\PinyinVim.Native.CapsulesPrototype"'
$program = Replace-Once $program '"Local\\PinyinVim.Native.CapsulesPrototype"' '"Local\\PinyinVim.Native.CapsulesPrototype" + CapsuleNative.TestInstanceSuffix'
$program = Replace-Once $program 'bool show = !test && Array.IndexOf(args, "--background") < 0;' 'bool show = Array.IndexOf(args, "--background") < 0;'
$program = Replace-Once $program 'Path.Combine(root, "test-profile")' 'CapsuleNative.ProfileDirectory(root)'
$program = Replace-Once $program 'if (!test && File.Exists(bundledFont))' 'if (File.Exists(bundledFont))'
$program = Replace-Once $program 'Text = "拼音浮笺 · 原生 Vim"' 'Text = "拼音浮笺 · " + ReleaseVersion.Display'
$program = Replace-Once $program '"--neovim-bin " + Quote(neovim)' '"--frame none --no-tabs --neovim-bin " + Quote(Path.Combine(Path.GetDirectoryName(neovim), "PinyinVim-GuiBridge.exe"))'
$program = Replace-Once $program 'internal sealed class Host : Form' 'internal sealed partial class Host : Form'
$program = Replace-Once $program '            settings = LoadSettings();' '            LoadCapsuleIconFont(); settings = LoadSettings();'
$program = Replace-Once $program '            if (opening || submitting) return;' '            if (opening || submitting) return;
            if (IsPreferencesForeground()) { Open(target); return; }'
$program = Replace-Once $program 'Text = "拼音浮笺后台"' 'Text = "拼音浮笺 · 胶囊后台" + CapsuleNative.TestInstanceSuffix'
$program = Replace-Once $program 'catch (Exception ex) { MessageBox.Show("拼音浮笺启动失败：" + ex.Message, "拼音浮笺"); }' 'catch (Exception ex) { if (CapsuleNative.IsTestInstance) { Console.Error.WriteLine(ex); Environment.Exit(1); } else MessageBox.Show("拼音浮笺启动失败：" + ex.Message, "拼音浮笺"); }'
$program = Replace-Once $program 'if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1)' 'if (CapsuleNativeClose(ref m)) return; if (m.Msg == 0x0312 && m.WParam.ToInt32() == capsuleHotkeyId)'
$program = Replace-Once $program "            if (!Win.RegisterHotKey(Handle, 1, test ? 0x4003u : 0x4006u, test ? 0x79u : 0x58u))`n                throw new InvalidOperationException(`"热键已被占用。请先从托盘退出旧版，再启动本版。`");" '            InitializeCapsuleHotkey();'
$program = Replace-Once $program 'ContextMenuStrip = menu, Visible = true' 'ContextMenuStrip = menu, Visible = !CapsuleNative.IsTestInstance'
$program = Replace-Once $program '            tray.DoubleClick += (s, e) => Open(null);' '            tray.DoubleClick += (s, e) => Open(null);
            if (CapsuleNative.IsTestInstance) CapsuleNative.SetNumber(Handle, "PinyinCapsules.TrayReady", 1);'
$program = Replace-Once $program 'psi.WorkingDirectory = profile;' 'psi.WorkingDirectory = profile; psi.RedirectStandardError = true;'
$program = Replace-Once $program 'string oldFont = Environment.GetEnvironmentVariable("PINYINVIM_FONT");' 'string oldFont = Environment.GetEnvironmentVariable("PINYINVIM_FONT"); string capsuleOldLog = Environment.GetEnvironmentVariable("RUST_LOG");'
$program = Replace-Once $program 'editor = Process.Start(psi);' 'Environment.SetEnvironmentVariable("RUST_LOG", "error,neovide::renderer::grid_renderer=trace"); editor = Process.Start(psi); StartCapsuleFontTelemetry(editor);'
$program = Replace-Once $program 'Environment.SetEnvironmentVariable("PINYINVIM_FONT", oldFont);' 'Environment.SetEnvironmentVariable("PINYINVIM_FONT", oldFont); Environment.SetEnvironmentVariable("RUST_LOG", capsuleOldLog);'
$program = $program.Replace('Win.HideNeovideBootstrapWindows(', 'CapsuleNative.HideRendererWindows(').Replace('Win.HideNeovideAuxWindows(editor.Id, editorWindow);', '// Preserve Winit internal event targets for sizing-loop events.')
$program = Replace-Once $program 'Win.CaptionTheme(editorWindow, dark);' 'CapsuleNative.ApplyNativeFrame(editorWindow, dark, !settings.ContainsKey("glass") || GetBool(settings, "glass"), settings.ContainsKey("blurRadius") ? Convert.ToInt32(settings["blurRadius"]) : 5, settings.ContainsKey("backgroundTransparency") ? Convert.ToInt32(settings["backgroundTransparency"]) : 50, settings.ContainsKey("backgroundImagePath") ? Convert.ToString(settings["backgroundImagePath"]) : "");'
$program = Replace-Once $program '{"clearOnClose",false},{"fontSize",14}' '{"clearOnClose",false},{"glass",true},{"fontSize",14}'
$program = Replace-Once $program '"pinyin","words","ime","dark","clearOnClose"' '"pinyin","words","ime","dark","clearOnClose","glass"'
$program = Replace-Once $program '            try
            {
                var saved = json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(profile,"settings.json")));' '            LoadCapsuleSettings(values, null);
            try
            {
                var saved = json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(profile,"settings.json")));
                LoadCapsuleSettings(values, saved);'
$settingsStart = $program.IndexOf('                    settings = next;')
$settingsEnd = $program.IndexOf('                    SendAck(message, "settings-ack", true, "");', $settingsStart)
if ($settingsStart -lt 0 -or $settingsEnd -lt 0) { throw 'Missing settings commit block' }
$program = $program.Substring(0,$settingsStart) + '                    string capsuleError;
                    if (!SaveCapsulePreferences(next, out capsuleError)) { RejectCapsuleSettings(message, capsuleError); return; }
' + $program.Substring($settingsEnd)
$program = Replace-Once $program 'Win.SetWindowText(editorWindow, "拼音浮笺  ·  Esc 收起");' 'Win.SetWindowText(editorWindow, "拼音浮笺"); StartCapsulePolicy();'
$program = Replace-Once $program 'if (kind == "settings") { ShowSettings(); return; }' 'if (kind == "capsule-metrics") { CapsuleMetrics(message); return; } if (kind == "settings") { ShowSettings(); return; }'
$program = Replace-Once $program 'disposed = true; Win.UnregisterHotKey(Handle,1);' 'disposed = true; StopCapsulePolicy(); Win.UnregisterHotKey(Handle,1); Win.UnregisterHotKey(Handle,2);'
$program = Replace-Once $program '                Metric("engine-ready", startup.Elapsed.TotalMilliseconds);' '                if (!String.IsNullOrEmpty(capsuleStartupNotice)) Send(new Dictionary<string, object> { {"kind", "capsule-notice"}, {"text", capsuleStartupNotice} });
                await AwaitCapsulePresentation();
                Metric("engine-ready", startup.Elapsed.TotalMilliseconds);'
$program = Replace-Once $program '            if (!ready)
            {
                if (!preparingNoticeShown)' '            if (!ready || !capsulePresentationReady)
            {
                if (!preparingNoticeShown)'
$showStart = $program.IndexOf('            Win.ShowWindow(editorWindow, 5);')
$showEnd = $program.IndexOf('            Metric("hotkey-to-show", invocation.Elapsed.TotalMilliseconds);', $showStart)
if ($showStart -lt 0 -or $showEnd -lt 0) { throw 'Missing authenticated show anchor' }
$showEnd += '            Metric("hotkey-to-show", invocation.Elapsed.TotalMilliseconds);'.Length
$program = $program.Substring(0,$showStart) + '            ScheduleCapsuleShow();' + $program.Substring($showEnd)
$program = Replace-Once $program '            session++;' '            capsuleSessionPrepared = false;
            session++;'
$program = Replace-Once $program '            if (ready) Send(new Dictionary<string, object> { {"kind", "settings-view"} });' '            ShowCapsuleSettings();'
$program = Replace-Once $program 'bool ok = await Submit(GetString(message, "text"));' 'bool ok = await SubmitCapsule(GetString(message, "text"));'
$program = Replace-Once $program '                Win.SetWindowPos(editorWindow, IntPtr.Zero, x, y, width, height, 0x0010);' '                Win.SetWindowPos(editorWindow, IntPtr.Zero, x, y, width, height, 0x0010);
                KeepCapsuleOnScreen();'
$program = Replace-Once $program '                if (invocation != null) { Metric("hotkey-to-vim-ready", invocation.Elapsed.TotalMilliseconds); invocation = null; }' '                if (!activeSession.IsCurrent(message.ContainsKey("session") ? message["session"] : null)) { SendAck(message, "session-ready-ack", false, "stale-session"); return; }
                capsuleSessionPrepared = true;
                if (invocation != null) Metric("hotkey-to-vim-ready", invocation.Elapsed.TotalMilliseconds);
                TryShowCapsuleSession();'
$program = Replace-Once $program '        void HideEditor()
        {' '        void HideEditor()
        {
            capsulePendingShow = 0; capsuleSessionPrepared = false;'
$program = Replace-Once $program '                bridgeReadyPending = true;
                try { File.AppendAllText(Path.Combine(profile, "bridge-errors.log"), "ready-complete: " + ex + Environment.NewLine); }' '                ready = false; bridgeReadyPending = true;
                try { File.AppendAllText(Path.Combine(profile, "bridge-errors.log"), "ready-complete: " + ex + Environment.NewLine); }'
$program = Replace-Once $program '                try { File.AppendAllText(Path.Combine(profile, "bridge-errors.log"), "ready-complete: " + ex + Environment.NewLine); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }' '                try { File.AppendAllText(Path.Combine(profile, "bridge-errors.log"), "ready-complete: " + ex + Environment.NewLine); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (ex is CapsulePresentationException) { if (!CapsuleNative.IsTestInstance) ShowNotification(4000, "拼音浮笺启动失败", ex.Message, ToolTipIcon.Error); quitting = true; Close(); }'
$program = Replace-Once $program '            StopOrphanedEditorProcesses();' '            RecoverCapsuleProcesses();'
$program = $program.Replace('StopEditorProcessTree();', 'StopEditorProcessTree(); StopCapsuleOwnedProcesses();')
$program = Replace-Once $program '                    string path = candidate.MainModule.FileName;' '                    if (!CapsuleOwnsProcess(candidate)) continue;
                    string path = candidate.MainModule.FileName;'
$program = Replace-Once $program 'editor = Process.Start(psi); StartCapsuleFontTelemetry(editor);' 'editor = Process.Start(psi); CaptureCapsuleProcesses(); StartCapsuleFontTelemetry(editor);'
$program = Replace-Once $program '(int)(210 * scale)' '(int)(320 * scale)'
$buildSource = Join-Path $output 'build-source'
New-Item -ItemType Directory -Force $buildSource | Out-Null
[System.IO.File]::WriteAllText((Join-Path $buildSource 'Program.cs'), $program, $utf8)
Write-PinyinVersionSource -Path (Join-Path $buildSource 'ProductVersion.g.cs') -Release $releaseInfo
$sources = @(Join-Path $buildSource 'Program.cs') + @(Join-Path $buildSource 'ProductVersion.g.cs') + @(Join-Path $PSScriptRoot 'CapsuleHost.cs') + @(Join-Path $PSScriptRoot 'CapsuleSettings.cs') + @(Join-Path $PSScriptRoot 'PreferencesWindow.cs') + @(Join-Path $PSScriptRoot 'CapsuleProcesses.cs') + @(Get-ChildItem -LiteralPath (Join-Path $product 'native/host') -Filter '*.cs' | Where-Object Name -ne 'Program.cs' | ForEach-Object FullName)
$appIcon = Join-Path $product 'assets/branding/pinyinvim.ico'
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+', '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll', "/r:$framework/WPF/UIAutomationClient.dll", "/r:$framework/WPF/UIAutomationTypes.dll", "/r:$framework/WPF/WindowsBase.dll", "/r:$framework/WPF/PresentationCore.dll", "/r:$framework/WPF/PresentationFramework.dll", "/r:$framework/System.Xaml.dll", "/r:$framework/WPF/WindowsFormsIntegration.dll", "/win32manifest:$product/native/host/app.manifest", "/win32icon:$appIcon", "/resource:$appIcon,PinyinVim.AppIcon", "/out:$output/$EntryName") + $sources
& (Join-Path $framework 'csc.exe') $compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Capsules prototype host compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output -Force
Write-Output $output
if ($Start) { Start-Process -FilePath (Join-Path $output $EntryName) -WorkingDirectory $output -WindowStyle Hidden }

