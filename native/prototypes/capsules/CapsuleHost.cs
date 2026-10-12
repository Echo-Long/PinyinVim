using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PinyinVim.Native
{
    internal sealed class CapsulePresentationException : InvalidOperationException
    {
        internal CapsulePresentationException(string message) : base(message) { }
    }
    internal sealed partial class Host
    {
        IntPtr capsuleHook;
        Timer capsuleTimer;
        int capsuleCols, capsuleRows, capsuleLastWidth, capsuleLastHeight;
        int capsuleLastEditorRows;
        double capsuleLastGapRows;
        double capsuleCellWidth, capsuleCellHeight;
        uint capsuleDpi;
        Dictionary<string, object> capsuleBounds;
        string capsuleFont = "";
        bool capsuleFontChanged;
        bool capsuleLastDragging;
        int capsuleGeometryRevision, capsuleAppliedRevision;
        bool capsulePresentationReady;
        long capsulePendingShow;
        bool capsuleSessionPrepared;

        void ShowCapsuleSettings()
        {
            if (disposed || quitting || submitting) return;
            Metric("capsule-settings-request", ready ? 1 : 0);
            OpenPreferencesWindow();
        }

        Task<bool> SubmitCapsule(string text)
        {
            if (String.IsNullOrEmpty(text)) return Task.FromResult(false);
            if (!CapsuleNative.IsTestInstance) return Submit(text);
            // Disposable hidden fixtures exercise the authenticated host path
            // without writing to the user's clipboard or sending paste keys.
            System.IO.File.WriteAllText(System.IO.Path.Combine(profile, "test-submitted.txt"), text);
            Metric("capsule-test-submit", 0);
            HideEditor();
            return Task.FromResult(true);
        }

        void StartCapsuleFontTelemetry(Process process)
        {
            // Pinned Neovide 0.16.2 reports its own shaped physical cell size.
            // Filtered stderr is consumed, never written to disk or used for
            // text/input handling. This avoids a second font measurement model.
            Task.Run(() => {
                try {
                    string line;
                    while ((line = process.StandardError.ReadLine()) != null) {
                        if (!line.Contains("Updated font dimensions:")) continue;
                        // GridScale debug output contains a transform matrix,
                        // not a (width,height) pair. Preserve only this metric
                        // line for diagnosing a pinned-version format mismatch.
                        try { System.IO.File.WriteAllText(System.IO.Path.Combine(profile, "font-metric.txt"), line); }
                        catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { }
                        var match = Regex.Match(line, @"matrix:\s*\[\(\s*([0-9]+(?:\.[0-9]+)?),\s*0(?:\.0+)?,\s*0(?:\.0+)?\),\s*\(\s*0(?:\.0+)?,\s*([0-9]+(?:\.[0-9]+)?)");
                        if (!match.Success) continue;
                        double width = Double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                        double height = Double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                        if (width < 1 || height < 1 || width > 300 || height > 600) continue;
                        if (disposed) break;
                        BeginInvoke((Action)(() => {
                            capsuleCellWidth = width; capsuleCellHeight = height;
                            capsuleFontChanged = true;
                            capsuleLastWidth = capsuleLastHeight = 0;
                            UpdateCapsuleGeometry();
                        }));
                    }
                } catch (InvalidOperationException) { }
                catch (System.IO.IOException) { }
            });
        }

        void StartCapsulePolicy()
        {
            if (capsuleHook != IntPtr.Zero) return;
            long style = Win.GetWindowLongPtr(editorWindow, -16).ToInt64();
            // Resize hit testing and the pointer gesture are handled by the
            // capsule policy itself. Do not retain WS_THICKFRAME here: Winit's
            // undecorated shadow path treats it as a second resize frame and
            // produces a client/non-client mismatch at the bottom edge.
            Win.SetWindowLongPtr(editorWindow, -16, new IntPtr(style & ~0x00CF0000L));
            double dpi = Win.WindowDpi(editorWindow) / 96.0;
            CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.Edge", (int)Math.Round(3 * dpi));
            CapsuleNative.SetHandle(editorWindow, "PinyinCapsules.Host", Handle);
            CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.TestInstance", CapsuleNative.IsTestInstance ? 1 : 0);
            if (CapsuleNative.IsTestInstance) Win.SetWindowLongPtr(editorWindow, -20,
                new IntPtr(Win.GetWindowLongPtr(editorWindow, -20).ToInt64() | 0x080000A0L));
            capsuleHook = CapsuleNative.InstallCapsulePolicy(editorWindow);
            if (capsuleHook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法安装原型窗口策略");
            CapsuleNative.UpdatePolicy(editorWindow, false);
            BindPreferencesOwner();
            // USER timers have a 10 ms minimum; 16 ms can round up to two
            // scheduler ticks. Sample at 10 ms without changing system timers.
            capsuleTimer = new Timer { Interval = 10 };
            capsuleTimer.Tick += (sender, args) => UpdateCapsuleGeometry();
            capsuleTimer.Start();
        }

        void CapsuleMetrics(Dictionary<string, object> message)
        {
            capsuleCols = Convert.ToInt32(message["columns"]);
            capsuleRows = Convert.ToInt32(message["rows"]);
            object bounds;
            if (message.TryGetValue("bounds", out bounds)) capsuleBounds = bounds as Dictionary<string, object>;
            // Geometry metrics can be emitted again by Neovim after a redraw.
            // A redraw may still carry an older host revision; never let that
            // acknowledgement move backwards or a cold session can wait for
            // a presentation that was already applied.
            if (message.ContainsKey("revision"))
                capsuleAppliedRevision = Math.Max(capsuleAppliedRevision, Convert.ToInt32(message["revision"]));
            string font = GetString(message, "font");
            if (font != capsuleFont)
            {
                capsuleFont = font;
                capsuleLastWidth = capsuleLastHeight = 0;
            }
        }

        void UpdateCapsuleGeometry()
        {
            CaptureCapsuleProcesses();
            TryShowCapsuleSession();
            if (!ready || editorWindow == IntPtr.Zero || capsuleCols < 1 || capsuleRows < 1) return;
            CapsuleNative.Rect client, outer;
            if (!CapsuleNative.GetClientRect(editorWindow, out client) || !CapsuleNative.GetWindowRect(editorWindow, out outer)) return;
            int width = client.Right, height = client.Bottom;
            int frameX = outer.Right - outer.Left - width, frameY = outer.Bottom - outer.Top - height;
            if (width < 1 || height < 1) return;
            uint currentDpi = Win.WindowDpi(editorWindow);
            double dpi = currentDpi / 96.0;
            if (capsuleDpi != 0 && capsuleDpi != currentDpi)
            {
                capsuleLastWidth = capsuleLastHeight = 0;
            }
            capsuleDpi = currentDpi;
            double zoom = 1;
            bool calibrated = capsuleFontChanged;
            if (capsuleCellWidth <= 0 || capsuleCellHeight <= 0) return;
            if (capsuleCellWidth > 0)
            {
                // Keep the editor usable on small displays while retaining a
                // stable 45-column text surface. The screen-relative floor
                // prevents an ultra-wide/high-DPI monitor from producing a
                // physically tiny minimum window.
                int cellMinimum = (int)Math.Ceiling(45 * capsuleCellWidth) + 1 + frameX;
                var screenArea = Screen.FromHandle(editorWindow).WorkingArea;
                int screenMinimum = (int)Math.Round(screenArea.Width * 0.12);
                CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.MinWidth", Math.Max(cellMinimum, screenMinimum));
                CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.BaseRowHeight", (int)Math.Round(capsuleCellHeight));
                CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.BaseCellWidth1000", (int)Math.Round(capsuleCellWidth * 1000));
                CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.LineSpace", 6);
                CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.FrameX", frameX);
                CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.FrameY", frameY);
                CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.Edge", (int)Math.Round(3 * dpi));
                if (capsuleBounds != null)
                {
                    double cellWidth = capsuleCellWidth;
                    double cellHeight = capsuleCellHeight;
                    foreach (string name in new[] { "editor", "message", "mode", "pending", "settings", "submit" })
                    {
                        var box = (Dictionary<string, object>)capsuleBounds[name];
                        string prefix = "PinyinCapsules." + Char.ToUpperInvariant(name[0]) + name.Substring(1);
                        CapsuleNative.SetNumber(editorWindow, prefix + "Left", (int)Math.Floor(Convert.ToDouble(box["left"]) * cellWidth));
                        CapsuleNative.SetNumber(editorWindow, prefix + "Right", (int)Math.Ceiling(Convert.ToDouble(box["right"]) * cellWidth));
                        CapsuleNative.SetNumber(editorWindow, prefix + "Top", (int)Math.Floor(Convert.ToDouble(box["top"]) * cellHeight));
                        CapsuleNative.SetNumber(editorWindow, prefix + "Bottom", (int)Math.Ceiling(Convert.ToDouble(box["bottom"]) * cellHeight));
                    }
                }
                CapsuleNative.UpdatePolicy(editorWindow, false);
                if (calibrated)
                {
                    // Apply the native policy once on calibration. Subsequent
                    // resize limits are enforced in the owner WndProc itself.
                    Win.SetWindowPos(editorWindow, IntPtr.Zero, 0, 0, outer.Right - outer.Left, outer.Bottom - outer.Top, 0x0016);
                    KeepCapsuleOnScreen();
                    capsuleFontChanged = false;
                    capsuleLastWidth = capsuleLastHeight = 0;
                    return; // Read the adjusted client bounds on the next tick.
                }
            }
            double rowHeight = Math.Ceiling(capsuleCellHeight);
            double gapRows = capsuleCellWidth / rowHeight;
            int editorRows = capsuleCellHeight > 0 ? Math.Max(1, (int)Math.Floor((double)height / rowHeight - 3 - 3 * gapRows + .02)) : Math.Max(1, capsuleRows - 4);
            bool dragging = CapsuleNative.GetNumber(editorWindow, "PinyinCapsules.PointerEdge") != 0;
            if (width == capsuleLastWidth && height == capsuleLastHeight && editorRows == capsuleLastEditorRows && Math.Abs(gapRows - capsuleLastGapRows) < .0001 && dragging == capsuleLastDragging) return;
            capsuleLastWidth = width; capsuleLastHeight = height;
            capsuleLastDragging = dragging;
            capsuleLastEditorRows = editorRows; capsuleLastGapRows = gapRows;
            Send(new Dictionary<string, object> { {"kind", "capsule-geometry"}, {"width", width}, {"height", height}, {"zoom", zoom}, {"gap_rows", gapRows}, {"editor_rows", editorRows}, {"cell_width", capsuleCellWidth}, {"cell_height", capsuleCellHeight}, {"dragging", dragging}, {"revision", ++capsuleGeometryRevision} });
        }

        async Task AwaitCapsulePresentation()
        {
            var timeout = Stopwatch.StartNew();
            while (!disposed && (capsuleGeometryRevision == 0 || capsuleAppliedRevision < capsuleGeometryRevision))
            {
                if (timeout.ElapsedMilliseconds > 5000) throw new CapsulePresentationException("编辑器字体和布局未完成就绪，已阻止显示临时会话。详情见 profile/bridge-errors.log。");
                await Task.Delay(25);
            }
            capsulePresentationReady = !disposed;
        }

        void ScheduleCapsuleShow()
        {
            capsulePendingShow = session;
            TryShowCapsuleSession();
        }

        void KeepCapsuleOnScreen()
        {
            CapsuleNative.Rect rect;
            if (!CapsuleNative.GetWindowRect(editorWindow, out rect)) return;
            var bounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var area = Screen.FromRectangle(bounds).WorkingArea;
            int x = bounds.Width <= area.Width ? Math.Min(Math.Max(area.Left, bounds.Left), area.Right - bounds.Width) : area.Left;
            int y = bounds.Height <= area.Height ? Math.Min(Math.Max(area.Top, bounds.Top), area.Bottom - bounds.Height) : area.Top;
            if (x != bounds.Left || y != bounds.Top) Win.SetWindowPos(editorWindow, IntPtr.Zero, x, y, 0, 0, 0x0015);
        }

        void TryShowCapsuleSession()
        {
            if (CapsuleNative.IsTestInstance) {
                CapsuleNative.SetNumber(Handle,"PinyinCapsules.ShowState",(capsuleSessionPrepared?1:0)|(capsulePendingShow!=0&&capsulePendingShow==session?2:0)|(!capsuleFontChanged?4:0)|(capsuleAppliedRevision>=capsuleGeometryRevision?8:0)|(ready?16:0)|(capsulePresentationReady?32:0));
                CapsuleNative.SetNumber(Handle,"PinyinCapsules.GeometrySent",capsuleGeometryRevision);
                CapsuleNative.SetNumber(Handle,"PinyinCapsules.GeometryApplied",capsuleAppliedRevision);
                CapsuleNative.SetNumber(Handle,"PinyinCapsules.LastWidth",capsuleLastWidth);
                CapsuleNative.SetNumber(Handle,"PinyinCapsules.LastHeight",capsuleLastHeight);
            }
            if (!capsuleSessionPrepared || capsulePendingShow == 0 || capsulePendingShow != session || disposed || quitting || capsuleFontChanged || capsuleAppliedRevision < capsuleGeometryRevision) return;
            CapsuleNative.Rect client;
            if (!CapsuleNative.GetClientRect(editorWindow, out client) || client.Right != capsuleLastWidth || client.Bottom != capsuleLastHeight) return;
            if (CapsuleNative.IsTestInstance) {
                visible = true; activeSession.Visible = true;
                Metric("capsule-test-show-ready", invocation == null ? 0 : invocation.Elapsed.TotalMilliseconds);
                capsulePendingShow = 0; invocation = null;
                return; // Contract fixtures must never activate the desktop.
            }
            // Mark the renderer ready before showing it. The native backdrop
            // may defer its fractional-client overlay until this point so a
            // second composition target cannot hold up the presentation gate.
            CapsuleNative.SetNumber(editorWindow, "PinyinCapsules.PresentationReady", 1);
            Win.ShowWindow(editorWindow, 5); Win.SetForegroundWindow(editorWindow);
            visible = true; activeSession.Visible = true; capsulePendingShow = 0;
            StopBootstrapHideThread();
            if (backgroundHideTimer != null) { backgroundHideTimer.Stop(); backgroundHideTimer.Dispose(); backgroundHideTimer = null; }
            if (invocation != null) { Metric("hotkey-to-show", invocation.Elapsed.TotalMilliseconds); invocation = null; }
        }

        void StopCapsulePolicy()
        {
            ClosePreferencesWindow();
            if (capsuleTimer != null) { capsuleTimer.Stop(); capsuleTimer.Dispose(); capsuleTimer = null; }
            if (capsuleHook != IntPtr.Zero)
            {
                CapsuleNative.UpdatePolicy(editorWindow, true);
                CapsuleNative.UnhookWindowsHookEx(capsuleHook); capsuleHook = IntPtr.Zero;
            }
        }

        bool CapsuleNativeClose(ref Message message)
        {
            if (message.Msg == CapsuleNative.HoverRequest) {
                int index = message.WParam.ToInt32();
                if (message.LParam == editorWindow && index >= 0 && index <= 4 && !disposed && !quitting) {
                    string[] panels = {"", "mode", "pending", "settings", "submit"};
                    Send(new Dictionary<string,object> {{"kind","capsule-hover"},{"panel",panels[index]}});
                }
                return true;
            }
            if (message.Msg == CapsuleNative.TestPreferencesAction && CapsuleNative.IsTestInstance) {
                if (message.WParam.ToInt32() == 0) { if (preferences != null) preferences.Close(); }
                else { OpenPreferencesWindow(); preferences.TestAction(message.WParam.ToInt32()); }
                return true;
            }
            if (message.Msg == CapsuleNative.TestTrayAction && CapsuleNative.IsTestInstance) {
                int index = message.WParam.ToInt32();
                if (index >= 0 && index < 2 && tray != null && !disposed && !quitting)
                    ((ToolStripMenuItem)tray.ContextMenuStrip.Items[index]).PerformClick();
                return true;
            }
            if (message.Msg == CapsuleNative.OpenRequest)
            {
                if (!disposed && !quitting) Open(null);
                return true;
            }
            if (message.Msg != CapsuleNative.HideRequest) return false;
            if (message.LParam == editorWindow && !disposed && !quitting) HideAndRestore();
            return true;
        }
    }

    internal static class CapsuleNative
    {
        static IntPtr appliedBackgroundImageWindow;
        static string appliedBackgroundImagePath;
        internal static readonly int HoverRequest = (int)RegisterWindowMessage("PinyinVim.Capsules.HoverChanged");
        internal static readonly int TestPreferencesAction = (int)RegisterWindowMessage("PinyinVim.Capsules.TestPreferencesAction");
        internal static bool IsTestInstance { get { return !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PINYINVIM_CAPSULE_TEST_PROFILE")); } }
        internal static string TestInstanceSuffix { get { return IsTestInstance ? "." + Environment.GetEnvironmentVariable("PINYINVIM_CAPSULE_TEST_PROFILE").GetHashCode().ToString("X") : ""; } }
        internal static string ProfileDirectory(string root)
        {
            string testProfile = Environment.GetEnvironmentVariable("PINYINVIM_CAPSULE_TEST_PROFILE");
            if (String.IsNullOrEmpty(testProfile)) return System.IO.Path.Combine(root, "prototype-profile");
            string resolved = System.IO.Path.GetFullPath(testProfile);
            if (!resolved.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Test profile must be within the temporary directory");
            return resolved;
        }
        delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        // Winit's zero-size, layered event target needs WS_VISIBLE for WM_PAINT
        // delivery during the native sizing loop. It is not an auxiliary UI.
        internal static void HideRendererWindows(int processId)
        {
            EnumWindows((window, parameter) => {
                uint owner; GetWindowThreadProcessId(window, out owner);
                if (owner == (uint)processId && Win.IsNeovideRenderWindow(window)) {
                    if (IsTestInstance) Win.SetWindowLongPtr(window, -20,
                        new IntPtr(Win.GetWindowLongPtr(window, -20).ToInt64() | 0x080000A0L));
                    Win.ShowWindow(window, 0);
                }
                return true;
            }, IntPtr.Zero);
        }
        internal static void ApplyNativeFrame(IntPtr window, bool dark, bool glass)
        {
            ApplyNativeFrame(window, dark, glass, 12, 55, "");
        }
        internal static void ApplyNativeFrame(IntPtr window, bool dark, bool glass, int blurRadius)
        {
            ApplyNativeFrame(window, dark, glass, blurRadius, 50, "");
        }
        internal static void ApplyNativeFrame(IntPtr window, bool dark, bool glass, int blurRadius, int backgroundTransparency, string imagePath)
        {
            int round = 2, border = -2, backdrop = 1, immersive = dark ? 1 : 0;
            // This HWND is a borderless DirectComposition surface. Keep
            // DWM's non-client compositor active for the invisible
            // shadow/rounded perimeter. Disabling it leaves the extended
            // frame area uninitialized; on a light desktop that area appears
            // as a solid white strip below the otherwise correct client
            // surface.
            int ncRendering = 2; // DWMWA_NCRENDERING_POLICY = DWMNCRP_ENABLED
            DwmSetWindowAttribute(window, 2, ref ncRendering, sizeof(int));
            // DWM owns the antialiased outer silhouette. Do not mix a GDI
            // window region or caption/acrylic colors into this client surface.
            DwmSetWindowAttribute(window, 33, ref round, sizeof(int));
            DwmSetWindowAttribute(window, 34, ref border, sizeof(int));
            DwmSetWindowAttribute(window, 38, ref backdrop, sizeof(int));
            DwmSetWindowAttribute(window, 20, ref immersive, sizeof(int));
            // Neovide 0.16.2 uses a premultiplied DirectComposition swapchain
            // and WS_EX_NOREDIRECTIONBITMAP. Winit deliberately skips legacy
            // DwmEnableBlurBehindWindow on that path. Extending a glass frame
            // across the client and enabling legacy blur recreated an opaque
            // backing surface: alpha then appeared as RGB multiplied by black.
            // Leave the swapchain's alpha path alone. No layered-window alpha,
            // GDI plate, frame extension or system material is added here.
            SetNumber(window, "PinyinCapsules.DirectComposition", (Win.GetWindowLongPtr(window, -20).ToInt64() & 0x200000) != 0 ? 1 : 0);
            // Ordinary BackdropBrush + Gaussian effect sits below Neovide's
            // existing target. HostBackdropBrush remains disabled: its
            // desktop samples were opaque black on this machine.
            SetNumber(window, "PinyinCapsules.BackgroundTransparency", Math.Max(0, Math.Min(100, backgroundTransparency)));
            SetNumber(window, "PinyinCapsules.BlurRadius", Math.Max(0, Math.Min(24, blurRadius)));
            imagePath = imagePath ?? "";
            if (appliedBackgroundImageWindow != window) {
                appliedBackgroundImageWindow = window;
                appliedBackgroundImagePath = null;
            }
            if (appliedBackgroundImagePath == null || !String.Equals(appliedBackgroundImagePath, imagePath, StringComparison.OrdinalIgnoreCase)) {
                if (ApplyNativeBackgroundImage(window, imagePath)) appliedBackgroundImagePath = imagePath;
            }
            IntPtr result;
            SendMessageTimeout(window, BackdropMessage, new IntPtr(glass ? 1 : 0), IntPtr.Zero, 2, 2000, out result);
        }
        internal static void GrantSettingsForeground(IntPtr window)
        {
            IntPtr result;
            SendMessageTimeout(window, SettingsForegroundMessage, IntPtr.Zero, IntPtr.Zero, 2, 200, out result);
        }
        internal static void ApplySettingsFrame(IntPtr window)
        {
            int round=2, border=-2, backdrop=1, ncRendering=2;
            DwmSetWindowAttribute(window,33,ref round,sizeof(int));
            DwmSetWindowAttribute(window,34,ref border,sizeof(int));
            DwmSetWindowAttribute(window,38,ref backdrop,sizeof(int));
            DwmSetWindowAttribute(window,2,ref ncRendering,sizeof(int));
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [DllImport("CapsuleWindowPolicy.dll", SetLastError = true)] internal static extern IntPtr InstallCapsulePolicy(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
        static readonly uint SetupMessage = RegisterWindowMessage("PinyinVim.Capsules.SetupWindowPolicy");
        static readonly uint BackdropMessage = RegisterWindowMessage("PinyinVim.Capsules.ApplyHostBackdrop");
        static readonly uint SettingsForegroundMessage = RegisterWindowMessage("PinyinVim.Capsules.GrantSettingsForeground");
        internal static readonly int HideRequest = (int)RegisterWindowMessage("PinyinVim.Capsules.HideRequested");
        internal static readonly int OpenRequest = (int)RegisterWindowMessage("PinyinVim.Capsules.OpenExisting");
        internal static readonly int TestTrayAction = (int)RegisterWindowMessage("PinyinVim.Capsules.TestTrayAction");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        internal static bool OpenExistingInstance()
        {
            IntPtr host = FindWindow(null, "拼音浮笺 · 胶囊后台" + TestInstanceSuffix);
            if (host == IntPtr.Zero) return false;
            uint owner; GetWindowThreadProcessId(host, out owner);
            try { using (Process running = Process.GetProcessById((int)owner)) {
                string directory = System.IO.Path.GetDirectoryName(running.MainModule.FileName);
                if (!String.Equals(System.IO.Path.GetFullPath(directory).TrimEnd('\\'), AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            }} catch (ArgumentException) { return false; } catch (System.ComponentModel.Win32Exception) { return false; }
            return PostMessage(host, OpenRequest, IntPtr.Zero, IntPtr.Zero);
        }
        internal static void UpdatePolicy(IntPtr window, bool remove)
        {
            IntPtr result;
            SendMessageTimeout(window, SetupMessage, new IntPtr(remove ? 1 : 0), IntPtr.Zero, 2, 200, out result);
        }
        internal static void SetNumber(IntPtr window, string name, int value) { SetProp(window, name, new IntPtr(value)); }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);
        internal static int GetNumber(IntPtr window, string name) { return GetProp(window, name).ToInt32(); }
        internal static void SetHandle(IntPtr window, string name, IntPtr value) { SetProp(window, name, value); }
        [StructLayout(LayoutKind.Sequential)] struct CopyData { internal IntPtr Data; internal int Size; internal IntPtr Pointer; }
        internal static bool ApplyNativeBackgroundImage(IntPtr window, string path)
        {
            if (window == IntPtr.Zero) return false;
            path = path ?? "";
            IntPtr memory = Marshal.StringToHGlobalUni(path);
            IntPtr packet = IntPtr.Zero;
            try {
                CopyData data = new CopyData { Data = new IntPtr(0x50564D49), Size = (path.Length + 1) * 2, Pointer = memory };
                packet = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CopyData)));
                Marshal.StructureToPtr(data, packet, false);
                IntPtr result;
                return SendMessageTimeout(window, 0x004A, IntPtr.Zero, packet, 2, 2000, out result) != IntPtr.Zero;
            } finally { if (packet != IntPtr.Zero) Marshal.FreeHGlobal(packet); Marshal.FreeHGlobal(memory); }
        }
    }
}
