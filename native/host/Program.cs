using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PinyinVim.Native
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool test = Array.IndexOf(args, "--test") >= 0;
            bool unique;
            using (var mutex = new Mutex(true, "Local\\PinyinVim.Native." + (test ? "Test" : "Main"), out unique))
            {
                if (!unique)
                {
                    if (!test)
                    {
                        MessageBox.Show(
                            "已有一个拼音浮笺实例正在运行。请从旧版托盘退出后，再启动当前版本。",
                            "拼音浮笺",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
                }
                // A normal double-click is an explicit user request to open
                // the editor.  Keep the tray-only behavior available for
                // startup integration through the explicit --background flag.
                bool show = !test && Array.IndexOf(args, "--background") < 0;
                try { Application.Run(new Host(test, show || Array.IndexOf(args, "--show") >= 0)); }
                catch (Exception ex) { MessageBox.Show("拼音浮笺启动失败：" + ex.Message, "拼音浮笺"); }
            }
        }
    }

    internal sealed class Host : Form
    {
        readonly bool test, initialShow;
        readonly string root, profile, draft, bundledFont, token = Guid.NewGuid().ToString("N");
        bool bundledFontLoaded;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        readonly object logLock = new object(), sendLock = new object();
        readonly NotifyIcon tray;
        readonly Stopwatch startup = Stopwatch.StartNew();
        readonly EditorSession activeSession = new EditorSession();
        readonly ImeStateController imeController = new ImeStateController();
        TcpListener listener;
        StreamWriter writer;
        Process editor;
        IntPtr editorWindow;
        InputTarget target;
        Dictionary<string, object> settings;
        long session;
        bool visible, clearNext, submitting, quitting, ready, disposed, shownInitially, preparingNoticeShown, bridgeReadyPending, completingReady;
        ISubmissionPlatform submissionPlatform = new WindowsSubmissionPlatform();
        bool clearDraftOnExit;
        string lastSubmitReason = "copied";
        string clearHash = "";
        bool imeEnabled;
        System.Windows.Forms.Timer backgroundHideTimer;
        System.Windows.Forms.Timer windowRepairTimer;
        Thread bootstrapHideThread;
        volatile bool bootstrapHideStop;
        bool backgroundHideLogged;
        Stopwatch invocation;
        Rectangle? editorBounds;
        string routeLabel = "剪贴板提交";

        public Host(bool testMode, bool show)
        {
            test = testMode; initialShow = show;
            root = AppDomain.CurrentDomain.BaseDirectory;
            profile = test ? Path.Combine(root, "test-profile") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinyinVimNative");
            Directory.CreateDirectory(profile);
            draft = Path.Combine(profile, "draft.txt");
            if (!File.Exists(draft)) File.WriteAllText(draft, "", new UTF8Encoding(false));
            bundledFont = Path.Combine(root, "fonts", "SarasaMonoSC-Regular.ttf");
            if (!test && File.Exists(bundledFont))
            {
                bundledFontLoaded = Win.AddFontResourceEx(bundledFont, 0, IntPtr.Zero) > 0;
                if (bundledFontLoaded) Win.BroadcastFontChange();
            }
            settings = LoadSettings();
            imeEnabled = GetBool(settings, "ime");
            Metric(initialShow ? "startup-show" : "startup-background", 0);
            Text = "拼音浮笺后台"; ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            var unused = Handle;
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开草稿（剪贴板模式）", null, (s, e) => Open(null));
            menu.Items.Add("设置", null, (s, e) => ShowSettings());
            menu.Items.Add("退出（清理进程）", null, (s, e) => Quit());
            tray = new NotifyIcon { Text = "拼音浮笺 · 原生 Vim", Icon = MakeIcon(), ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (s, e) => Open(null);
            if (!Win.RegisterHotKey(Handle, 1, test ? 0x4003u : 0x4006u, test ? 0x79u : 0x58u))
                throw new InvalidOperationException("热键已被占用。请先从托盘退出旧版，再启动本版。");
            NativeWindows.Initialize(Metric);
            // Neovide can create its Winit event-target HWND after the RPC
            // ready message. Keep that helper hidden for the whole lifetime
            // and refresh the real renderer handle after native GUI changes.
            windowRepairTimer = new System.Windows.Forms.Timer { Interval = 20 };
            windowRepairTimer.Tick += (s, e) =>
            {
                if (disposed || editor == null || editor.HasExited) return;
                editor.Refresh();
                if (!visible && session == 0) Win.HideNeovideBootstrapWindows(editor.Id);
                var current = Win.FindNeovideWindow(editor.Id);
                if (current != IntPtr.Zero)
                {
                    editorWindow = current;
                    Win.HideNeovideAuxWindows(editor.Id, editorWindow);
                    if (!ready && bridgeReadyPending && !completingReady)
                        BeginInvoke((Action)(() => { if (!disposed) CompleteReady(); }));
                }
            };
            windowRepairTimer.Start();
            // Neovide can create and paint its renderer before the authenticated
            // bridge/session handshake completes. Keep the bootstrap frame
            // hidden for normal starts as well as --background starts; otherwise
            // users can type into an unbound buffer while the host still says
            // "编辑器正在准备". Open() stops this timer only after ready/session.
            backgroundHideTimer = new System.Windows.Forms.Timer { Interval = 20 };
            backgroundHideTimer.Tick += (s, e) =>
            {
                if (!visible && session == 0 && editorWindow != IntPtr.Zero)
                {
                    if (editor != null)
                    {
                        editor.Refresh();
                        Win.HideNeovideBootstrapWindows(editor.Id);
                        var current = Win.FindNeovideWindow(editor.Id);
                        if (current != IntPtr.Zero)
                        {
                            editorWindow = current;
                            Win.HideNeovideAuxWindows(editor.Id, editorWindow);
                        }
                    }
                    Win.ShowWindow(editorWindow, 0);
                    if (!backgroundHideLogged) { backgroundHideLogged = true; Metric("background-hide", 0); }
                }
            };
            backgroundHideTimer.Start();
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(1);
            // A killed host cannot run OnFormClosed. Remove only bundled
            // editor processes from this exact release directory before a new
            // bridge is created; unrelated Neovide/Neovim installations are
            // never touched.
            StopOrphanedEditorProcesses();
            AcceptEditor();
            BeginInvoke((Action)StartEditorSafely);
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1)
            {
                if (ready && Win.GetForegroundWindow() == editorWindow)
                {
                    HideAndRestore();
                }
                else CaptureAndOpen();
                return;
            }
            base.WndProc(ref m);
        }

        bool opening;
        async void CaptureAndOpen()
        {
            if (opening || submitting) return;
            opening = true;
            var watch = Stopwatch.StartNew();
            IntPtr source = Win.GetForegroundWindow();
            try
            {
                var captured = await NativeWindows.CaptureTargetAsync();
                // Never steal focus after the user has already moved to another app.
                if (Win.GetForegroundWindow() != source) return;
                Open(captured);
                Metric("hotkey-total-to-show", watch.Elapsed.TotalMilliseconds);
            }
            catch (Exception)
            {
                Metric("hotkey-capture-failed", watch.Elapsed.TotalMilliseconds);
                if (!disposed && Win.GetForegroundWindow() == source) Open(null);
            }
            finally { opening = false; }
        }

        async void AcceptEditor()
        {
            while (!disposed)
            {
                TcpClient client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    using (client)
                    using (var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false)))
                    using (var outgoing = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (line.Length > 16 * 1024 * 1024) break;
                            var message = json.Deserialize<Dictionary<string, object>>(line);
                            if (GetString(message, "token") != token) break;
                            writer = outgoing;
                            await OnMessage(message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!disposed)
                    {
                        Metric("bridge-disconnected", 0);
                        try { File.AppendAllText(Path.Combine(profile, "bridge-errors.log"), ex.ToString() + Environment.NewLine); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                finally { writer = null; if (client != null) client.Close(); }
            }
        }

        void StartEditorSafely()
        {
            try { StartEditor(); }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(profile, "startup-errors.log"), ex.ToString() + Environment.NewLine); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                quitting = true;
                if (!disposed) Close();
            }
        }

        void StartEditor()
        {
            if (quitting || disposed) return;
            ready = false;
            bridgeReadyPending = false;
            preparingNoticeShown = false;
            var executable = Path.Combine(root, "runtime", "neovide", "neovide.exe");
            var neovim = Path.Combine(root, "runtime", "nvim", "bin", "nvim.exe");
            var config = Path.Combine(root, "nvim", "init.vim");
            if (!File.Exists(executable)) throw new FileNotFoundException("缺少随附的 Neovide", executable);
            if (!File.Exists(neovim)) throw new FileNotFoundException("缺少随附的 Neovim", neovim);
            if (!File.Exists(config)) throw new FileNotFoundException("缺少 PinyinVim Neovim 配置", config);
            var neovideArgs = "--neovim-bin " + Quote(neovim) + " -- -u " + Quote(config) + " " + Quote(draft);
            var psi = new ProcessStartInfo(executable, neovideArgs);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = profile;
            // ProcessStartInfo.EnvironmentVariables enumerates the inherited
            // Windows environment. On some machines that contains both Path
            // and PATH, which .NET Framework 4.0 rejects as duplicate keys.
            // Set only our process-local variables while spawning, then put
            // the host environment back exactly as it was.
            string oldPort = Environment.GetEnvironmentVariable("PINYINVIM_PORT");
            string oldToken = Environment.GetEnvironmentVariable("PINYINVIM_TOKEN");
            string oldDraft = Environment.GetEnvironmentVariable("PINYINVIM_DRAFT");
            string oldTest = Environment.GetEnvironmentVariable("PINYINVIM_TEST");
            string oldFont = Environment.GetEnvironmentVariable("PINYINVIM_FONT");
            try
            {
                Environment.SetEnvironmentVariable("PINYINVIM_PORT", ((IPEndPoint)listener.LocalEndpoint).Port.ToString());
                Environment.SetEnvironmentVariable("PINYINVIM_TOKEN", token);
                Environment.SetEnvironmentVariable("PINYINVIM_DRAFT", draft);
                Environment.SetEnvironmentVariable("PINYINVIM_TEST", test ? "1" : "0");
                if (bundledFontLoaded) Environment.SetEnvironmentVariable("PINYINVIM_FONT", "等距更纱黑体 SC");
                editor = Process.Start(psi);
                // Catch a renderer or Winit helper HWND that appears between
                // Process.Start and the first repair-timer tick. The timer
                // repeats this until the authenticated session is visible.
                if (editor != null)
                {
                    Win.HideNeovideBootstrapWindows(editor.Id);
                    StartBootstrapHideThread();
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("PINYINVIM_PORT", oldPort);
                Environment.SetEnvironmentVariable("PINYINVIM_TOKEN", oldToken);
                Environment.SetEnvironmentVariable("PINYINVIM_DRAFT", oldDraft);
                Environment.SetEnvironmentVariable("PINYINVIM_TEST", oldTest);
                Environment.SetEnvironmentVariable("PINYINVIM_FONT", oldFont);
            }
            editor.EnableRaisingEvents = true;
            editor.Exited += (s, e) => { if (!disposed) BeginInvoke((Action)EditorExited); };
        }

        void StartBootstrapHideThread()
        {
            StopBootstrapHideThread();
            bootstrapHideStop = false;
            int processId = editor.Id;
            bootstrapHideThread = new Thread(() =>
            {
                while (!bootstrapHideStop)
                {
                    try
                    {
                        // The normal start keeps hiding until Open() explicitly
                        // reveals the authenticated session. Background mode
                        // can stop after ready because it never opens a session.
                        if (disposed || (ready && !initialShow) || session != 0 || visible) break;
                        Win.HideNeovideBootstrapWindows(processId);
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    Thread.Sleep(5);
                }
            }) { IsBackground = true, Name = "PinyinVim bootstrap window guard" };
            bootstrapHideThread.Start();
        }

        void StopBootstrapHideThread()
        {
            bootstrapHideStop = true;
            var thread = bootstrapHideThread;
            bootstrapHideThread = null;
            if (thread != null && thread != Thread.CurrentThread)
            {
                try { thread.Join(100); }
                catch (ThreadStateException) { }
            }
        }

        void EditorExited()
        {
            ready = false; bridgeReadyPending = false; visible = false; editorWindow = IntPtr.Zero;
            activeSession.Reset();
            session = 0;
            if (quitting) { Close(); return; }
            // A gVim X/`:q` is an explicit close. Recreating it here makes the
            // window pop back after the user just closed it. Tear down the
            // hidden host as well; the next global hotkey invocation starts a
            // fresh editor session.
            quitting = true;
            Close();
        }

        async void CompleteReady()
        {
            if (ready || !bridgeReadyPending || completingReady || disposed) return;
            if (editor == null || editor.HasExited) return;
            completingReady = true;
            try
            {
                editor.Refresh();
                // The bridge ready packet can precede the renderer HWND by a
                // few hundred milliseconds. Retry here instead of abandoning
                // the handshake and leaving the user in the preparing state.
                editorWindow = Win.FindNeovideWindow(editor.Id);
                if (editorWindow == IntPtr.Zero)
                {
                    await Task.Delay(100);
                    if (disposed || editor == null || editor.HasExited) return;
                    editor.Refresh();
                    editorWindow = Win.FindNeovideWindow(editor.Id);
                }
                if (editorWindow == IntPtr.Zero) return;
                Win.HideNeovideBootstrapWindows(editor.Id);
                Win.HideNeovideAuxWindows(editor.Id, editorWindow);
                Win.ShowWindow(editorWindow, 0);
                // Neovide owns its native caption, system menu and close
                // behavior. Do not rewrite its window style like the old gVim
                // shell; only set product text and the tray icon here.
                Win.SetWindowText(editorWindow, "拼音浮笺  ·  Esc 收起");
                Win.SendMessage(editorWindow, 0x80, IntPtr.Zero, tray.Icon.Handle);
                Win.SendMessage(editorWindow, 0x80, new IntPtr(1), tray.Icon.Handle);
                Win.SetWindowPos(editorWindow, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
                ApplyWindowTheme();
                ready = true;
                bridgeReadyPending = false;
                preparingNoticeShown = false;
                Send(new Dictionary<string, object> { {"kind","settings"}, {"settings",settings} });
                Metric("engine-ready", startup.Elapsed.TotalMilliseconds);
                if ((!shownInitially && initialShow) || invocation != null) Open(target);
                else
                {
                    // Background mode must remain hidden until the global
                    // hotkey explicitly creates a session.
                    HideEditor();
                    StopBootstrapHideThread();
                    BeginInvoke((Action)(() => { if (!disposed && !visible) Win.ShowWindow(editorWindow, 0); }));
                }
                shownInitially = true;
            }
            catch (Exception ex)
            {
                // Keep the renderer hidden and retry from the repair timer if
                // a transient HWND or Win32 call fails during bootstrap.
                bridgeReadyPending = true;
                try { File.AppendAllText(Path.Combine(profile, "bridge-errors.log"), "ready-complete: " + ex + Environment.NewLine); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            finally { completingReady = false; }
        }

        async Task OnMessage(Dictionary<string, object> message)
        {
            string kind = GetString(message, "kind");
            if (kind == "ready")
            {
                // A ready packet can arrive before Neovide has created its
                // renderer HWND. Keep the packet pending and finish it from
                // the repair timer once the real window exists; returning here
                // permanently left the host in "preparing" forever.
                bridgeReadyPending = true;
                CompleteReady();
                return;
            }
            if (kind == "settings") { ShowSettings(); return; }
            if (kind == "settings-update")
            {
                var next = message.ContainsKey("settings") ? message["settings"] as Dictionary<string, object> : null;
                if (next != null)
                {
                    settings = next;
                    imeEnabled = GetBool(settings, "ime");
                    if (!imeEnabled) ResetImeState();
                    File.WriteAllText(Path.Combine(profile, "settings.json"), json.Serialize(settings), new UTF8Encoding(false));
                    ApplyWindowTheme();
                    Send(new Dictionary<string, object> { {"kind", "settings"}, {"settings", settings}, {"requestId", GetString(message, "requestId")} });
                    SendAck(message, "settings-ack", true, "");
                }
                else SendAck(message, "settings-ack", false, "invalid-settings");
                return;
            }
            if (kind == "hide" || kind == "hide-request")
            {
                // Only Normal-mode Esc is allowed to request a host hide.  The
                // session id is checked at the protocol boundary and the
                // result is acknowledged so Neovim cannot silently drift.
                if (activeSession.IsCurrent(message.ContainsKey("session") ? message["session"] : null))
                {
                    HideAndRestore();
                    SendAck(message, "hide-ack", true, "hidden");
                }
                else SendAck(message, "hide-ack", false, "stale-session");
                return;
            }
            if (kind == "ime-result")
            {
                Metric("ime-" + GetString(message, "action") + "-" + GetString(message, "result"),
                    message.ContainsKey("ms") ? Convert.ToDouble(message["ms"]) : 0);
                return;
            }
            if (kind == "session-ready")
            {
                if (invocation != null) { Metric("hotkey-to-vim-ready", invocation.Elapsed.TotalMilliseconds); invocation = null; }
                SendAck(message, "session-ready-ack", true, "");
                return;
            }
            if (kind == "exiting")
            {
                StopEditorAfterNvimExit();
                return;
            }
            if (kind == "mode-change" || kind == "state")
            {
                string mode = GetString(message, "mode");
                activeSession.SetMode(mode);
                UpdateIme(mode);
                return;
            }
            if (kind == "ime-enter")
            {
                string mode = GetString(message, "mode");
                if (String.IsNullOrEmpty(mode)) mode = "INSERT";
                activeSession.SetMode(mode);
                UpdateIme(mode);
                SendAck(message, "ime-ack", true, mode.ToLowerInvariant());
                return;
            }
            if (kind == "ime-leave")
            {
                string mode = GetString(message, "mode");
                if (String.IsNullOrEmpty(mode)) mode = "NORMAL";
                activeSession.SetMode(mode);
                UpdateIme(mode);
                SendAck(message, "ime-ack", true, "normal");
                return;
            }
            if (kind == "clipboard-copied")
            {
                if (activeSession.IsCurrent(message.ContainsKey("session") ? message["session"] : null))
                    ShowNotification(1800, "拼音浮笺", "文字已复制到剪贴板。", ToolTipIcon.Info);
                return;
            }
            if (kind == "error") { Metric("vim-error", 0); if (test) File.AppendAllText(Path.Combine(profile, "vim-errors.log"), GetString(message, "message") + Environment.NewLine); return; }
            if (kind == "submit" || kind == "submit-request")
            {
                if (!activeSession.IsCurrent(message.ContainsKey("session") ? message["session"] : null))
                {
                    SendAck(message, "submit-ack", false, "stale-session");
                    return;
                }
                if (String.IsNullOrEmpty(GetString(message, "text")))
                {
                    SendAck(message, "submit-ack", false, "empty-draft");
                    return;
                }
                string rejection;
                if (!activeSession.TryBeginSubmit(GetString(message, "requestId"), out rejection))
                {
                    SendAck(message, "submit-ack", false, rejection);
                    return;
                }
                bool delivered = false;
                lastSubmitReason = "copied";
                try
                {
                    bool ok = await Submit(GetString(message, "text"));
                    delivered = ok;
                    SendAck(message, "submit-ack", ok, lastSubmitReason);
                }
                finally { activeSession.FinishSubmit(delivered); }
            }
        }

        void Open(InputTarget captured)
        {
            if (submitting) return;
            invocation = Stopwatch.StartNew();
            target = captured;
            if (!ready)
            {
                if (!preparingNoticeShown)
                {
                    ShowNotification(1200, "拼音浮笺", "编辑器正在准备，准备好后将自动打开。", ToolTipIcon.Info);
                    preparingNoticeShown = true;
                }
                return;
            }
            session++;
            activeSession.Begin(session, target);
            routeLabel = activeSession.RouteLabel;
            ResetImeState();
            Send(new Dictionary<string, object> {
                {"kind","session"},
                {"session",session},
                {"route",activeSession.RouteValue},
                {"routeLabel",activeSession.RouteLabel},
                {"targetValidated",activeSession.TargetValidated},
                {"paste",activeSession.Route == SubmitRoute.Paste},
                {"clear",clearNext},
                {"clearHash",clearHash},
                {"settings",settings}
            });
            clearNext = false;
            clearHash = "";
            if (!visible)
            {
                Rectangle area = editorBounds.HasValue ? Screen.FromRectangle(editorBounds.Value).WorkingArea : Screen.FromPoint(Cursor.Position).WorkingArea;
                double scale = Win.WindowDpi(editorWindow) / 96.0;
                int width = Math.Min(editorBounds.HasValue ? editorBounds.Value.Width : (int)(760 * scale), area.Width - 40);
                int height = Math.Min(editorBounds.HasValue ? editorBounds.Value.Height : (int)(210 * scale), area.Height - 40);
                int desiredX = editorBounds.HasValue ? editorBounds.Value.X : area.Right - width - (int)(24 * scale);
                int desiredY = editorBounds.HasValue ? editorBounds.Value.Y : area.Top + (area.Height - height) / 2;
                int x = Math.Min(Math.Max(area.Left + 20, desiredX), area.Right - width - 20);
                int y = Math.Min(Math.Max(area.Top + 20, desiredY), area.Bottom - height - 20);
                // Put the editor above the current app for this invocation,
                // but never mark it HWND_TOPMOST. TOPMOST made native close
                // messages unreliable on some Neovide Windows builds.
                Win.SetWindowPos(editorWindow, IntPtr.Zero, x, y, width, height, 0x0010);
            }
            if (editor != null && !editor.HasExited)
            {
                var current = Win.FindNeovideWindow(editor.Id);
                if (current != IntPtr.Zero) editorWindow = current;
                Win.HideNeovideAuxWindows(editor.Id, editorWindow);
            }
            Win.ShowWindow(editorWindow, 5);
            Win.SetForegroundWindow(editorWindow);
            visible = true;
            activeSession.Visible = true;
            StopBootstrapHideThread();
            if (backgroundHideTimer != null) { backgroundHideTimer.Stop(); backgroundHideTimer.Dispose(); backgroundHideTimer = null; }
            Metric("hotkey-to-show", invocation.Elapsed.TotalMilliseconds);
        }

        async Task<bool> Submit(string text)
        {
            // An empty draft must not open, clear or replace any clipboard format.
            if (String.IsNullOrEmpty(text)) return false;
            if (submitting) return false;
            submitting = true;
            var watch = Stopwatch.StartNew();
            ISubmissionInput input = null;
            try
            {
                lastSubmitReason = "clipboard-failed";
                // Accept the immutable request and hide immediately. Neither
                // physical key-up nor a slow target provider gates dismissal.
                input = submissionPlatform.CaptureKeys(SubmissionVirtualKey());
                HideEditor();
                Metric("submit-to-hide", watch.Elapsed.TotalMilliseconds);
                bool copied = await submissionPlatform.CopyAsync(text);
                if (!copied) { ShowNotification(1800, "拼音浮笺", "剪贴板暂时被占用，草稿仍在。", ToolTipIcon.Info); return false; }
                Metric("submit-to-clipboard", watch.Elapsed.TotalMilliseconds);
                bool pasted = false;
                PasteResult pasteResult = null;
                if (activeSession.Route == SubmitRoute.Paste && target != null && target.Editable && text.Length > 0)
                {
                    pasteResult = await submissionPlatform.DeliverAsync(target, editorWindow, true, input);
                    pasted = pasteResult.Success;
                }
                else if (target != null && target.Window != IntPtr.Zero)
                {
                    await submissionPlatform.DeliverAsync(target, editorWindow, false, input);
                }
                Metric(pasted ? "submit-to-paste" : "submit-to-clipboard-mode", watch.Elapsed.TotalMilliseconds);
                // SendInput confirms dispatch, not target text acceptance.
                // Keep a local recovery copy before the next session clears.
                if (pasted)
                {
                    try { File.WriteAllText(Path.Combine(profile, "last-submitted.txt"), text, new UTF8Encoding(false)); }
                    catch (IOException) { pasted = false; }
                    catch (UnauthorizedAccessException) { pasted = false; }
                }
                clearNext = pasted;
                if (pasted)
                {
                    using (var hash = System.Security.Cryptography.SHA256.Create())
                        clearHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
                }
                lastSubmitReason = pasteResult != null && pasteResult.Success ? "paste-sent"
                    : activeSession.Route == SubmitRoute.Paste ? "copied-fallback" : "copied";
                string notice = PastePolicy.SubmissionNotice(copied, activeSession.Route == SubmitRoute.Paste,
                    pasteResult != null && pasteResult.Success, pasteResult == null ? "invalid-target" : pasteResult.Reason);
                if (notice != null) ShowNotification(1800, "拼音浮笺", notice, ToolTipIcon.Info);
                return true;
            }
            finally { if (input != null) input.Complete(); submitting = false; }
        }

        int SubmissionVirtualKey() { return 13; }

        void HideEditor()
        {
            if (editorWindow != IntPtr.Zero)
            {
                Win.WindowRect bounds;
                if (visible && Win.GetWindowRect(editorWindow, out bounds)) editorBounds = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                Win.ShowWindow(editorWindow, 0);
            }
            visible = false;
            activeSession.Visible = false;
        }
        void HideAndRestore()
        {
            NativeWindows.CancelPaste();
            HideEditor();
            if (target != null && Win.IsWindow(target.Window)) Win.SetForegroundWindow(target.Window);
        }
        void ApplyWindowTheme()
        {
            if (editorWindow == IntPtr.Zero) return;
            bool dark = GetBool(settings, "dark");
            Win.CaptionTheme(editorWindow, dark);
        }

        void UpdateIme(string mode)
        {
            if (editorWindow == IntPtr.Zero || String.IsNullOrEmpty(mode)) return;
            imeController.Enabled = imeEnabled;
            bool verified = imeController.Apply(editorWindow, mode);
            Metric("ime-" + mode.ToLowerInvariant() + (verified ? "-ok" : "-unverified"), 0);
        }

        void ResetImeState()
        {
            imeController.Enabled = imeEnabled;
            imeController.Reset(editorWindow);
        }

        async void StopEditorAfterNvimExit()
        {
            if (disposed) return;
            quitting = true;
            ready = false;
            await Task.Delay(180);
            StopEditorProcessTree();
            if (!disposed) Close();
        }

        void StopEditorProcessTree()
        {
            var current = editor;
            int pid = 0;
            if (current != null) try
            {
                current.Refresh();
                if (!current.HasExited) pid = current.Id;
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            // Neovide owns the bundled nvim process. Kill the tracked process
            // tree first so an unresponsive bridge cannot leave nvim holding
            // files open after the tray command has finished.
            if (pid != 0) try
            {
                string taskkill = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe");
                if (String.IsNullOrEmpty(taskkill) || !File.Exists(taskkill)) taskkill = "taskkill.exe";
                using (var killer = new Process())
                {
                    killer.StartInfo = new ProcessStartInfo
                    {
                        FileName = taskkill,
                        Arguments = "/PID " + pid.ToString() + " /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    killer.Start();
                    killer.WaitForExit(2000);
                }
            }
            catch (Exception) { }

            // Fall back to the direct handle in case taskkill was unavailable
            // or the process exited between the two calls.
            try
            {
                current.Refresh();
                if (!current.HasExited)
                {
                    current.Kill();
                    current.WaitForExit(1500);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            // If Neovide already disappeared, its child can outlive the
            // parent. Match only the exact bundled executable paths owned by
            // this host so unrelated editors are never terminated.
            StopBundledProcess("neovide", Path.Combine(root, "runtime", "neovide", "neovide.exe"));
            StopBundledProcess("nvim", Path.Combine(root, "runtime", "nvim", "bin", "nvim.exe"));
        }

        void StopBundledProcess(string processName, string executablePath)
        {
            foreach (var candidate in Process.GetProcessesByName(processName))
            {
                try
                {
                    string path = candidate.MainModule.FileName;
                    if (!String.Equals(path, executablePath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!candidate.HasExited)
                    {
                        candidate.Kill();
                        candidate.WaitForExit(1200);
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                catch (Exception) { }
                finally { candidate.Dispose(); }
            }
        }

        void StopOrphanedEditorProcesses()
        {
            StopBundledProcess("neovide", Path.Combine(root, "runtime", "neovide", "neovide.exe"));
            StopBundledProcess("nvim", Path.Combine(root, "runtime", "nvim", "bin", "nvim.exe"));
        }

        void RequestSubmit()
        {
            if (!ready || session == 0 || submitting) return;
            Send(new Dictionary<string, object> { {"kind", "request-submit"}, {"session", session} });
        }
        void Send(Dictionary<string, object> message)
        {
            message["token"] = token;
            try { lock (sendLock) { if (writer != null) writer.WriteLine(json.Serialize(message)); } }
            catch (IOException) { Metric("bridge-send-failed", 0); }
        }

        void SendAck(Dictionary<string, object> request, string kind, bool ok, string reason)
        {
            Send(new Dictionary<string, object> {
                {"kind", kind},
                {"requestId", GetString(request, "requestId")},
                {"session", activeSession.Id},
                {"ok", ok},
                {"reason", reason ?? ""}
            });
        }

        void ShowSettings()
        {
            // Settings are a view in the same Neovide/Neovim window. A
            // separate WinForms dialog was the source of the second-window
            // failure and let the editor show through underneath it.
            if (ready) Send(new Dictionary<string, object> { {"kind", "settings-view"} });
        }

        Dictionary<string,object> LoadSettings()
        {
            var values = new Dictionary<string,object> { {"notifications",true},{"pinyin",true},{"words",true},{"ime",true},{"dark",false},{"clearOnClose",false},{"fontSize",14} };
            try
            {
                var saved = json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(profile,"settings.json")));
                foreach (string key in new[] {"notifications","pinyin","words","ime","dark","clearOnClose"}) if (saved.ContainsKey(key) && saved[key] is bool) values[key] = saved[key];
                if (saved.ContainsKey("fontSize")) values["fontSize"] = Math.Max(10,Math.Min(28,Convert.ToInt32(saved["fontSize"])));
            }
            catch (Exception) { }
            return values;
        }

        void Quit()
        {
            quitting = true;
            NativeWindows.CancelPaste();
            bool clearOnClose = settings.ContainsKey("clearOnClose") && Convert.ToBoolean(settings["clearOnClose"]);
            clearDraftOnExit = clearOnClose;
            if (editor != null && !editor.HasExited)
            {
                if (!visible) { Win.ShowWindow(editorWindow, 5); Win.SetForegroundWindow(editorWindow); visible = true; }
                Send(new Dictionary<string, object> { {"kind","quit"}, {"clearOnClose",clearOnClose} });
                ScheduleQuitFallback();
            }
            else Close();
        }

        void ScheduleQuitFallback()
        {
            Task.Delay(1500).ContinueWith(_ =>
            {
                if (disposed) return;
                try
                {
                    BeginInvoke((Action)(() => { if (!disposed) Close(); }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
        }
        void ClearDraft()
        {
            try { File.WriteAllText(draft, "", new UTF8Encoding(false)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try { File.WriteAllText(Path.Combine(profile, "last-submitted.txt"), "", new UTF8Encoding(false)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            disposed = true; Win.UnregisterHotKey(Handle,1);
            NativeWindows.CancelPaste();
            var submissionCleanup = submissionPlatform as IDisposable;
            if (submissionCleanup != null) submissionCleanup.Dispose();
            if (windowRepairTimer != null) { windowRepairTimer.Stop(); windowRepairTimer.Dispose(); windowRepairTimer = null; }
            if (backgroundHideTimer != null) { backgroundHideTimer.Stop(); backgroundHideTimer.Dispose(); backgroundHideTimer = null; }
            StopBootstrapHideThread();
            if (listener != null) listener.Stop();
            StopEditorProcessTree();
            if (clearDraftOnExit) ClearDraft();
            tray.Visible = false; tray.Dispose();
            if (bundledFontLoaded) { Win.RemoveFontResourceEx(bundledFont, 0, IntPtr.Zero); Win.BroadcastFontChange(); }
            base.OnFormClosed(e);
            // The host form is intentionally invisible; explicitly terminate
            // the WinForms message loop so a hidden tray host cannot survive
            // after the window and editor tree have already been closed.
            Application.ExitThread();
            Environment.Exit(0);
        }
        void Metric(string name, double milliseconds)
        {
            try
            {
                lock (logLock)
                {
                    string path = Path.Combine(profile,"latency.jsonl");
                    if (File.Exists(path) && new FileInfo(path).Length > 1024*1024) File.WriteAllText(path, "");
                    File.AppendAllText(path, json.Serialize(new { metric = name, ms = Math.Round(milliseconds, 3), time = DateTime.UtcNow.ToString("o") }) + "\n");
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        static bool GetBool(Dictionary<string,object> value,string key) { return value.ContainsKey(key) && Convert.ToBoolean(value[key]); }
        static string GetString(Dictionary<string,object> value,string key) { return value.ContainsKey(key) ? Convert.ToString(value[key]) : ""; }
        static string Quote(string text) { return "\"" + text.Replace("\"", "\\\"") + "\""; }
        void ShowNotification(int timeout, string title, string text, ToolTipIcon kind)
        {
            object enabled;
            // Missing values keep notifications enabled for older profiles.
            if (settings != null && settings.TryGetValue("notifications", out enabled)
                && enabled is bool && !(bool)enabled) return;
            tray.ShowBalloonTip(timeout, title, text, kind);
        }
        static Icon MakeIcon()
        {
            using (var stream = typeof(Host).Assembly.GetManifestResourceStream("PinyinVim.AppIcon"))
            {
                if (stream != null)
                    using (var icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
            }
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    internal sealed class EditorOwner : IWin32Window
    {
        public IntPtr Handle { get; private set; }
        public EditorOwner(IntPtr handle) { Handle = handle; }
    }

    internal static class Win
    {
        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] internal struct WindowRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct ClientRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        internal static IntPtr FindNeovideWindow(int processId)
        {
            IntPtr render = IntPtr.Zero, fallback = IntPtr.Zero;
            EnumWindows((hwnd, _) =>
            {
                uint owner;
                GetWindowThreadProcessId(hwnd, out owner);
                if (owner != (uint)processId) return true;

                var cls = new StringBuilder(128);
                GetClassName(hwnd, cls, cls.Capacity);
                // Neovide's actual renderer is the Window Class. Its Winit
                // Thread Event Target is a visible-looking helper window that
                // must never be positioned or shown by the host.
                if (string.Equals(cls.ToString(), "Window Class", StringComparison.Ordinal))
                {
                    render = hwnd;
                    return true;
                }
                var title = new StringBuilder(256);
                GetWindowText(hwnd, title, title.Capacity);
                if (!IsWindowVisible(hwnd) || title.Length == 0) return true;
                if (fallback == IntPtr.Zero && !string.Equals(cls.ToString(), "Winit Thread Event Target", StringComparison.Ordinal))
                    fallback = hwnd;
                return true;
            }, IntPtr.Zero);
            return render != IntPtr.Zero ? render : fallback;
        }
        internal static bool IsNeovideRenderWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            var cls = new StringBuilder(128);
            return GetClassName(hwnd, cls, cls.Capacity) > 0 && string.Equals(cls.ToString(), "Window Class", StringComparison.Ordinal);
        }
        internal static void HideNeovideAuxWindows(int processId, IntPtr keep)
        {
            EnumWindows((hwnd, _) =>
            {
                uint owner;
                GetWindowThreadProcessId(hwnd, out owner);
                if (owner != (uint)processId || hwnd == keep) return true;
                var cls = new StringBuilder(128);
                GetClassName(hwnd, cls, cls.Capacity);
                if (string.Equals(cls.ToString(), "Winit Thread Event Target", StringComparison.Ordinal) && IsWindowVisible(hwnd))
                    ShowWindow(hwnd, 0);
                return true;
            }, IntPtr.Zero);
        }
        internal static void HideNeovideBootstrapWindows(int processId)
        {
            // Before the bridge is ready there may be no renderer HWND yet,
            // while Neovide's Winit event-target HWND is already visible. Do
            // not select or position that helper; hide every top-level window
            // owned by the bundled process until Open() explicitly reveals the
            // authenticated session window.
            EnumWindows((hwnd, _) =>
            {
                uint owner;
                GetWindowThreadProcessId(hwnd, out owner);
                if (owner == (uint)processId && IsWindowVisible(hwnd)) ShowWindow(hwnd, 0);
                return true;
            }, IntPtr.Zero);
        }
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out WindowRect bounds);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr h, out ClientRect bounds);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);
        internal static uint WindowDpi(IntPtr h)
        {
            try { uint dpi = GetDpiForWindow(h); return dpi == 0 ? 96u : dpi; }
            catch (EntryPointNotFoundException) { return 96; }
        }
        internal static void CaptionTheme(IntPtr h, bool dark)
        {
            try
            {
                int rounded = 2, immersive = dark ? 1 : 0, backdrop = 3;
                Color caption = dark ? Color.FromArgb(31, 37, 45) : Color.FromArgb(242, 245, 248);
                Color foreground = dark ? Color.FromArgb(224, 231, 239) : Color.FromArgb(67, 82, 98);
                Color border = dark ? Color.FromArgb(72, 84, 98) : Color.FromArgb(205, 216, 226);
                int captionColor = ColorTranslator.ToWin32(caption), textColor = ColorTranslator.ToWin32(foreground), borderColor = ColorTranslator.ToWin32(border);
                DwmSetWindowAttribute(h, 20, ref immersive, sizeof(int));
                DwmSetWindowAttribute(h, 33, ref rounded, sizeof(int));
                DwmSetWindowAttribute(h, 38, ref backdrop, sizeof(int));
                DwmSetWindowAttribute(h, 34, ref borderColor, sizeof(int));
                DwmSetWindowAttribute(h, 35, ref captionColor, sizeof(int));
                DwmSetWindowAttribute(h, 36, ref textColor, sizeof(int));
                // Neovide owns the client-surface opacity. Do not add a second
                // Win32 alpha layer here; it would multiply the editor alpha
                // and make the text/controls unexpectedly faint.
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h,int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h,int cmd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool SetWindowText(IntPtr h,string text);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(IntPtr h, uint colorKey, byte alpha, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,IntPtr l);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")] private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll")] private static extern bool ImmGetOpenStatus(IntPtr hIMC);
        [DllImport("imm32.dll")] private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool open);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr h);
        [DllImport("gdi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);
        [DllImport("gdi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool RemoveFontResourceEx(string fileName, uint flags, IntPtr reserved);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        const int WM_IME_CONTROL = 0x0283;
        const int IMC_GETOPENSTATUS = 0x0005;
        const int IMC_SETOPENSTATUS = 0x0006;
        internal static void BroadcastFontChange()
        {
            IntPtr ignored;
            SendMessageTimeout(new IntPtr(0xffff), 0x001D, IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out ignored);
        }
        internal static bool TryGetImeOpen(IntPtr hWnd, out bool open)
        {
            open = false;
            if (hWnd == IntPtr.Zero) return false;
            foreach (IntPtr candidate in ImeCandidates(hWnd))
            {
                IntPtr imeWindow = IntPtr.Zero;
                try
                {
                    imeWindow = ImmGetDefaultIMEWnd(candidate);
                    IntPtr result;
                    if (imeWindow != IntPtr.Zero && SendMessageTimeout(imeWindow, WM_IME_CONTROL,
                        new IntPtr(IMC_GETOPENSTATUS), IntPtr.Zero, 0x0002, 80, out result) != IntPtr.Zero)
                    {
                        open = result != IntPtr.Zero;
                        return true;
                    }
                }
                catch (DllNotFoundException) { return false; }
                IntPtr himc = IntPtr.Zero;
                try
                {
                    himc = ImmGetContext(candidate);
                    if (himc == IntPtr.Zero) continue;
                    open = ImmGetOpenStatus(himc);
                    return true;
                }
                catch (DllNotFoundException) { return false; }
                finally { if (himc != IntPtr.Zero) ImmReleaseContext(candidate, himc); }
            }
            return false;
        }

        internal static bool TryGetImeContext(IntPtr hWnd, out IntPtr context)
        {
            context = IntPtr.Zero;
            if (hWnd == IntPtr.Zero) return false;
            try
            {
                context = ImmGetContext(hWnd);
                if (context == IntPtr.Zero) return false;
                ImmReleaseContext(hWnd, context);
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        internal static bool TryDetachImeContext(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            try
            {
                IntPtr context = ImmGetContext(hWnd);
                if (context == IntPtr.Zero) return true;
                ImmReleaseContext(hWnd, context);
                ImmAssociateContext(hWnd, IntPtr.Zero);
                IntPtr after = ImmGetContext(hWnd);
                if (after != IntPtr.Zero)
                {
                    ImmReleaseContext(hWnd, after);
                    return false;
                }
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        internal static bool TryRestoreImeContext(IntPtr hWnd, IntPtr context, bool open)
        {
            if (hWnd == IntPtr.Zero || context == IntPtr.Zero) return false;
            try
            {
                ImmAssociateContext(hWnd, context);
                IntPtr actual = ImmGetContext(hWnd);
                if (actual == IntPtr.Zero) return false;
                ImmReleaseContext(hWnd, actual);
                return TrySetImeOpen(hWnd, open);
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        internal static bool TrySetImeOpen(IntPtr hWnd, bool open)
        {
            if (hWnd == IntPtr.Zero) return false;
            foreach (IntPtr candidate in ImeCandidates(hWnd))
            {
                IntPtr imeWindow = IntPtr.Zero;
                try
                {
                    imeWindow = ImmGetDefaultIMEWnd(candidate);
                    IntPtr result;
                    if (imeWindow != IntPtr.Zero && SendMessageTimeout(imeWindow, WM_IME_CONTROL,
                        new IntPtr(IMC_SETOPENSTATUS), new IntPtr(open ? 1 : 0), 0x0002, 80, out result) != IntPtr.Zero)
                    {
                        bool actual;
                        if (TryGetImeOpen(candidate, out actual) && actual == open) return true;
                    }
                }
                catch (DllNotFoundException) { return false; }
                IntPtr himc = IntPtr.Zero;
                try
                {
                    himc = ImmGetContext(candidate);
                    if (himc == IntPtr.Zero) continue;
                    if (ImmSetOpenStatus(himc, open)) return true;
                }
                catch (DllNotFoundException) { return false; }
                finally { if (himc != IntPtr.Zero) ImmReleaseContext(candidate, himc); }
            }
            return false;
        }
        static IEnumerable<IntPtr> ImeCandidates(IntPtr preferred)
        {
            // A delayed mode report must never switch another application's
            // input method after its window became foreground.
            if (preferred != IntPtr.Zero) yield return preferred;
        }
    }
}
