using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace PinyinVim.Native
{
    public sealed class InputTarget
    {
        public IntPtr Window { get; private set; }
        public IntPtr Focus { get; private set; }
        public int ProcessId { get; private set; }
        public bool Editable { get; private set; }
        public string Reason { get; private set; }
        public AutomationElement Element { get; private set; }
        public int[] RuntimeId { get; private set; }
        public long SessionId { get; private set; }
        internal bool IsUiaTarget { get { return Element != null; } }

        internal InputTarget(IntPtr window, IntPtr focus, int processId, bool editable,
            string reason, AutomationElement element, int[] runtimeId, long sessionId)
        {
            Window = window;
            Focus = focus;
            ProcessId = processId;
            Editable = editable;
            Reason = reason;
            Element = element;
            RuntimeId = runtimeId;
            SessionId = sessionId;
        }
    }

    public sealed class PasteResult
    {
        public bool Success { get; private set; }
        public string Reason { get; private set; }

        internal PasteResult(bool success, string reason)
        {
            Success = success;
            Reason = reason;
        }
    }

    public static class NativeWindows
    {
        private const uint GA_ROOT = 2;
        private const uint SW_RESTORE = 9;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;
        private const int GWL_STYLE = -16;
        private const long ES_READONLY = 0x0800;
        private const long ES_PASSWORD = 0x0020;
        private const int KEY_RELEASE_TIMEOUT_MS = 800;
        // Browser/desktop chat controls can take a few hundred milliseconds to
        // reactivate after the editor is hidden.  The previous 120/900 ms
        // budget made the one-shot Ctrl+V path fail even though the right
        // element had been captured.  Keep the operation bounded, but leave
        // enough time for Chromium/Electron UIA providers to restore focus.
        private const int FOREGROUND_TIMEOUT_MS = 800;
        private const int PASTE_TIMEOUT_MS = 2500;
        private const int CALLER_TIMEOUT_MS = KEY_RELEASE_TIMEOUT_MS + PASTE_TIMEOUT_MS + 100;

        private static readonly object StateLock = new object();
        private static readonly object PasteLock = new object();
        private static volatile bool initialized;
        private static bool pasteBusy;
        private static long latestSessionId;
        private static readonly int HostProcessId = Process.GetCurrentProcess().Id;
        private static Action<string, double> log;
        private static BoundedWorker inspector;

        public static void Initialize(Action<string, double> metricLogger)
        {
            lock (StateLock)
            {
                if (metricLogger != null) log = metricLogger;
                if (initialized) return;
                initialized = true;
                // No global UI Automation listener: other applications should not
                // pay accessibility-provider costs merely because we are running.
                inspector = new BoundedWorker();
            }
        }

        public static InputTarget CaptureTarget()
        {
            Stopwatch timer = Stopwatch.StartNew();
            long sessionId = Interlocked.Increment(ref latestSessionId);
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                Log("target-capture", timer.Elapsed.TotalMilliseconds);
                return new InputTarget(IntPtr.Zero, IntPtr.Zero, 0, false, "no-foreground-window", null, null, sessionId);
            }

            uint processId;
            GetWindowThreadProcessId(window, out processId);
            GUITHREADINFO info = new GUITHREADINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO));
            // The foreground input queue may belong to a hosted content thread,
            // rather than the thread owning the top-level frame (for example UWP).
            IntPtr focus = GetGUIThreadInfo(0, ref info) ? info.hwndFocus : IntPtr.Zero;
            if (focus == IntPtr.Zero) focus = info.hwndActive;
            if (focus != IntPtr.Zero && GetAncestor(focus, GA_ROOT) != window) focus = IntPtr.Zero;
            LogApplication("target-app-", (int)processId);

            bool editable;
            string reason;
            if (IsNativeEdit(focus, out reason, out editable))
            {
                Log("target-capture", timer.Elapsed.TotalMilliseconds);
                return new InputTarget(window, focus, (int)processId, editable, reason, null, null, sessionId);
            }

            Log("target-capture", timer.Elapsed.TotalMilliseconds);
            return new InputTarget(window, focus, (int)processId, false, "unknown-focus", null, null, sessionId);
        }

        public static async Task<InputTarget> CaptureTargetAsync()
        {
            InputTarget target = CaptureTarget();
            if (target.Editable || target.Reason == "password-field" || target.Reason == "readonly-or-disabled-edit" || target.Window == IntPtr.Zero)
            {
                Log("target-classification-" + target.Reason, 0);
                return target;
            }
            var timer = Stopwatch.StartNew();
            InputTarget result = await inspector.Submit(() =>
            {
                if (GetForegroundWindow() != target.Window) return target;
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused != null)
                {
                    try
                    {
                        var information = focused.Current;
                        Log("target-uia-control-" + information.ControlType.ProgrammaticName.Replace("ControlType.", ""), 0);
                        LogApplication("target-uia-app-", information.ProcessId);
                    }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                }
                FocusSnapshot snapshot = ResolveFocusedInput(target, focused, true);
                if (!IsCapturableUiaTarget(target, snapshot, GetForegroundWindow()))
                {
                    Log("target-uia-rejected-" + (snapshot == null ? "no-editable-element"
                        : snapshot.Password ? "password" : !snapshot.Enabled ? "disabled"
                        : !snapshot.Writable ? "readonly" : !snapshot.HasKeyboardFocus ? "not-focused" : "window-changed"), 0);
                    return target;
                }
                // UIA logical focus is authoritative for UIA controls; a valid
                // focused element need not have its own native focus HWND.
                return new InputTarget(target.Window, target.Focus, target.ProcessId, true, "uia-editable-focus",
                    snapshot.Element, snapshot.RuntimeId, target.SessionId);
            }, target, 350);
            Log("target-uia-capture",timer.Elapsed.TotalMilliseconds);
            Log("target-classification-" + result.Reason, 0);
            return result;
        }

        public static void CancelPaste() { Interlocked.Increment(ref latestSessionId); }

        public static Task<PasteResult> RestoreAndPasteAsync(InputTarget target, IntPtr editorWindow)
        {
            return RestoreAsync(target, editorWindow, true);
        }

        public static Task<PasteResult> RestoreFocusAsync(InputTarget target, IntPtr editorWindow)
        {
            return RestoreAsync(target, editorWindow, false);
        }

        private static Task<PasteResult> RestoreAsync(InputTarget target, IntPtr editorWindow, bool paste)
        {
            TaskCompletionSource<PasteResult> completion = new TaskCompletionSource<PasteResult>();
            if (target == null || (paste && !target.Editable) || target.Window == IntPtr.Zero)
            {
                completion.SetResult(new PasteResult(false, "invalid-target"));
                return completion.Task;
            }

            lock (PasteLock)
            {
                if (pasteBusy)
                {
                    completion.SetResult(new PasteResult(false, "paste-busy"));
                    return completion.Task;
                }
                pasteBusy = true;
            }

            Thread worker = new Thread(delegate()
            {
                Stopwatch timer = Stopwatch.StartNew();
                PasteResult result;
                try { result = RestoreAndPaste(target, editorWindow, timer, paste); }
                catch (ElementNotAvailableException) { result = Failure("uia-target-unavailable"); }
                catch (InvalidOperationException) { result = Failure("uia-provider-failed"); }
                catch (Exception) { result = Failure("unexpected-error"); }
                finally
                {
                    Log(paste ? "restore-paste" : "restore-focus", timer.Elapsed.TotalMilliseconds);
                    lock (PasteLock) pasteBusy = false;
                }
                Log((paste ? "restore-paste-" : "restore-focus-") + result.Reason, timer.Elapsed.TotalMilliseconds);
                completion.TrySetResult(result);
            });
            worker.IsBackground = true;
            worker.Name = "PinyinVim restore and paste";
            worker.SetApartmentState(ApartmentState.MTA);
            try { worker.Start(); }
            catch
            {
                lock (PasteLock) pasteBusy = false;
                completion.TrySetResult(new PasteResult(false, "worker-start-failed"));
            }
            // UIA providers can hang. A timed-out caller invalidates this session,
            // and the worker checks that invalidation before it can send input.
            Task.Delay(CALLER_TIMEOUT_MS).ContinueWith(t =>
            {
                if (completion.TrySetResult(new PasteResult(false, "operation-timeout")))
                    Interlocked.CompareExchange(ref latestSessionId, target.SessionId + 1, target.SessionId);
            });
            return completion.Task;
        }

        private static PasteResult RestoreAndPaste(InputTarget target, IntPtr editorWindow, Stopwatch timer, bool paste)
        {
            if (!IsCurrentSession(target)) return Failure("session-changed");
            if (!IsSameWindow(target)) return Failure("target-window-unavailable");
            while (!InputReleased())
            {
                if (timer.ElapsedMilliseconds >= KEY_RELEASE_TIMEOUT_MS) return Failure("key-release-timeout");
                if (!IsCurrentSession(target) || !IsSameWindow(target)) return Failure("session-changed");
                if (!IsAllowedForeground(target, editorWindow)) return Failure("foreground-changed");
                Thread.Sleep(4);
            }
            if (!IsCurrentSession(target)) return Failure("session-changed");
            // Release time must not consume the focus/provider budget. No wait
            // is added when the keys are already up.
            Stopwatch restoreTimer = Stopwatch.StartNew();

            IntPtr foreground = GetForegroundWindow();
            uint foregroundPid;
            if (foreground != IntPtr.Zero)
            {
                GetWindowThreadProcessId(foreground, out foregroundPid);
                if (!PastePolicy.AllowsForeground(foreground, foregroundPid, target.Window, editorWindow, HostProcessId))
                    return Failure("foreground-changed");
            }
            uint targetThread = GetWindowThreadProcessId(!target.IsUiaTarget && target.Editable && target.Focus != IntPtr.Zero
                ? target.Focus : target.Window, IntPtr.Zero);
            uint currentThread = GetCurrentThreadId();
            IntPtr priorForeground = foreground;
            uint priorThread = priorForeground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(priorForeground, IntPtr.Zero);
            bool attachedCurrent = false;
            bool attachedPrior = false;
            try
            {
                if (!IsCurrentSession(target) || !IsSameWindow(target)) return Failure("target-window-unavailable");
                if (targetThread != currentThread)
                    attachedCurrent = AttachThreadInput(currentThread, targetThread, true);
                if (priorThread != 0 && priorThread != targetThread && priorThread != currentThread)
                    attachedPrior = AttachThreadInput(priorThread, targetThread, true);

                AllowSetForegroundWindow((uint)target.ProcessId);
                if (!IsCurrentSession(target)) return Failure("session-changed");
                if (IsIconic(target.Window)) ShowWindow(target.Window, SW_RESTORE);
                SetForegroundWindow(target.Window);
                Stopwatch activation = Stopwatch.StartNew();
                while (GetForegroundWindow() != target.Window)
                {
                    if (!IsCurrentSession(target)) return Failure("session-changed");
                    if (!IsSameWindow(target)) return Failure("target-window-unavailable");
                    IntPtr active = GetForegroundWindow();
                    if (active != IntPtr.Zero)
                    {
                        uint activePid;
                        GetWindowThreadProcessId(active, out activePid);
                        if (!PastePolicy.AllowsForeground(active, activePid, target.Window, editorWindow, HostProcessId))
                            return Failure("foreground-changed");
                    }
                    if (activation.ElapsedMilliseconds >= FOREGROUND_TIMEOUT_MS) return Failure("foreground-restore-failed");
                    SetForegroundWindow(target.Window);
                    Thread.Sleep(4);
                }

                // Clipboard mode still returns to the invocation window. Unknown
                // or protected controls are never focused explicitly or written.
                if (!paste && !target.Editable)
                {
                    if (!CanContinue(target, restoreTimer)) return Failure(IsCurrentSession(target) ? "operation-timeout" : "session-changed");
                    if (!InputReleased() || GetForegroundWindow() != target.Window) return Failure("focus-or-keys-changed");
                    return new PasteResult(true, "focus-restored");
                }

                if (target.IsUiaTarget)
                {
                    // UIA providers may block; never hold another process's input queue while calling them.
                    if (attachedPrior) { AttachThreadInput(priorThread, targetThread, false); attachedPrior = false; }
                    if (attachedCurrent) { AttachThreadInput(currentThread, targetThread, false); attachedCurrent = false; }
                    if (target.Element == null || target.RuntimeId == null) return Failure("uia-target-unavailable");
                    if (!CanContinue(target, restoreTimer)) return Failure("operation-timeout");
                    if (GetForegroundWindow() != target.Window) return Failure("foreground-changed");
                    string focusRequestFailure = null;
                    string focusFailure = FocusRestorePolicy.Wait(
                        () => !CanContinue(target, restoreTimer) ? (IsCurrentSession(target) ? "operation-timeout" : "session-changed")
                            : GetForegroundWindow() != target.Window ? "foreground-changed"
                            : !InputReleased() ? "focus-or-keys-changed" : null,
                        () => IsMatchingUiaTarget(target, ResolveFocusedInput(target, AutomationElement.FocusedElement, false)),
                        () =>
                        {
                            try { target.Element.SetFocus(); }
                            catch (ElementNotAvailableException) { focusRequestFailure = "uia-target-unavailable"; }
                            catch (InvalidOperationException) { focusRequestFailure = "uia-provider-failed"; }
                        }, () => restoreTimer.ElapsedMilliseconds,
                        milliseconds => Thread.Sleep(milliseconds), 120, FOREGROUND_TIMEOUT_MS);
                    if (focusFailure != null) return Failure(focusFailure == "uia-focus-mismatch" && focusRequestFailure != null
                        ? focusRequestFailure : focusFailure);
                }
                else
                {
                    if (target.Focus == IntPtr.Zero || !IsWindow(target.Focus)) return Failure("native-focus-unavailable");
                    if (!IsExpectedFocus(target))
                    {
                        if (targetThread != currentThread && !attachedCurrent) return Failure("native-focus-unavailable");
                        SetFocus(target.Focus);
                    }
                    if (!IsExpectedFocus(target)) return Failure("native-focus-mismatch");
                    if (attachedPrior) { AttachThreadInput(priorThread, targetThread, false); attachedPrior = false; }
                    if (attachedCurrent) { AttachThreadInput(currentThread, targetThread, false); attachedCurrent = false; }
                }

                if (!paste) return new PasteResult(true, "focus-restored");

                INPUT[] inputs = new INPUT[4];
                inputs[0] = Keyboard(VK_CONTROL, false);
                inputs[1] = Keyboard(VK_V, false);
                inputs[2] = Keyboard(VK_V, true);
                inputs[3] = Keyboard(VK_CONTROL, true);

                // Recheck immediately before the one and only input submission.
                if (!CanContinue(target, restoreTimer)) return Failure(IsCurrentSession(target) ? "operation-timeout" : "session-changed");
                if (!InputReleased()) return Failure("keys-not-released");
                if (!IsSameWindow(target)) return Failure("target-window-unavailable");
                if (GetForegroundWindow() != target.Window) return Failure("foreground-changed");
                if (target.IsUiaTarget)
                {
                    // Use the same scoped discovery on the final check: some
                    // providers report the container even when its editable
                    // descendant owns logical focus. The captured identity and
                    // all writable guards must still match.
                    bool same = IsMatchingUiaTarget(target,
                        ResolveFocusedInput(target, AutomationElement.FocusedElement, false));
                    if (!CanContinue(target, restoreTimer)) return Failure(IsCurrentSession(target) ? "operation-timeout" : "session-changed");
                    if (GetForegroundWindow() != target.Window) return Failure("foreground-changed");
                    // A replacement node is a different target, even inside
                    // the same browser window. Fall back to copied text.
                    if (!same)
                        return Failure("uia-focus-changed");
                }
                else
                {
                    string editReason; bool editable;
                    if (!IsExpectedFocus(target)) return Failure("native-focus-mismatch");
                    if (!IsNativeEdit(target.Focus, out editReason, out editable) || !editable) return Failure("target-readonly-or-disabled");
                }
                if (!InputReleased() || !CanContinue(target, restoreTimer) || GetForegroundWindow() != target.Window)
                    return Failure("focus-or-keys-changed");
                if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length)
                    return Failure("send-input-failed");
                return new PasteResult(true, "success");
            }
            finally
            {
                if (attachedPrior) AttachThreadInput(priorThread, targetThread, false);
                if (attachedCurrent) AttachThreadInput(currentThread, targetThread, false);
            }
        }

        private static PasteResult Failure(string reason) { return new PasteResult(false, reason); }

        private static bool IsCurrentSession(InputTarget target)
        {
            return target != null && Interlocked.Read(ref latestSessionId) == target.SessionId;
        }

        private static bool CanContinue(InputTarget target, Stopwatch timer)
        {
            return IsCurrentSession(target) && timer.ElapsedMilliseconds < PASTE_TIMEOUT_MS;
        }

        private static bool IsAllowedForeground(InputTarget target, IntPtr editorWindow)
        {
            IntPtr active = GetForegroundWindow();
            uint pid = 0;
            if (active != IntPtr.Zero) GetWindowThreadProcessId(active, out pid);
            return PastePolicy.AllowsForeground(active, pid, target.Window, editorWindow, HostProcessId);
        }

        private static bool IsMatchingUiaTarget(InputTarget target, FocusSnapshot snapshot)
        {
            return IsCapturableUiaTarget(target, snapshot, target.Window)
                && RuntimeIdsEqual(target.RuntimeId, snapshot.RuntimeId);
        }

        private static bool IsCapturableUiaTarget(InputTarget target, FocusSnapshot snapshot, IntPtr foreground)
        {
            return target != null && snapshot != null && snapshot.HasKeyboardFocus && snapshot.Enabled && !snapshot.Password
                && snapshot.Writable && snapshot.Window == target.Window && snapshot.ProcessId == target.ProcessId
                && foreground == target.Window && snapshot.RuntimeId != null && snapshot.RuntimeId.Length > 0;
        }

        private static bool IsFocusContainer(ControlType type)
        {
            return type == ControlType.Window || type == ControlType.Pane
                || type == ControlType.Custom || type == ControlType.Group;
        }

        private static FocusSnapshot ResolveFocusedInput(InputTarget target, AutomationElement focused, bool trace)
        {
            FocusSnapshot direct = InspectFocus(focused, target.Window, target.ProcessId);
            return FocusRestorePolicy.Discover(direct,
                () => focused != null && GetForegroundWindow() == target.Window && IsFocusContainer(focused.Current.ControlType),
                () =>
                {
                    // Search only the original foreground window. An editable
                    // control merely existing there is insufficient: the
                    // provider must explicitly report its keyboard focus.
                    var condition = new AndCondition(
                        new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true),
                        new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)));
                    AutomationElement root = AutomationElement.FromHandle(target.Window);
                    AutomationElementCollection children = root.FindAll(TreeScope.Descendants, condition);
                    AutomationElement child = children.Count == 1 ? children[0] : null;
                    if (trace) Log(children.Count == 0 ? "target-uia-focused-descendant-absent"
                        : children.Count == 1 ? "target-uia-focused-descendant-found" : "target-uia-focused-descendant-ambiguous", 0);
                    return InspectFocus(child, target.Window, target.ProcessId);
                }, candidate => IsCapturableUiaTarget(target, candidate, GetForegroundWindow()));
        }

        internal static bool RuntimeIdsEqual(int[] left, int[] right)
        {
            return PastePolicy.SameRuntimeId(left, right);
        }

        private static bool IsSameWindow(InputTarget target)
        {
            if (!IsWindow(target.Window)) return false;
            uint processId;
            GetWindowThreadProcessId(target.Window, out processId);
            return processId == (uint)target.ProcessId && GetAncestor(target.Window, GA_ROOT) == target.Window;
        }

        private static bool IsExpectedFocus(InputTarget target)
        {
            if (target.Focus == IntPtr.Zero) return false;
            uint threadId = GetWindowThreadProcessId(target.Focus, IntPtr.Zero);
            GUITHREADINFO info = new GUITHREADINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO));
            return GetGUIThreadInfo(threadId, ref info) && info.hwndFocus == target.Focus;
        }

        private static bool IsNativeEdit(IntPtr hwnd, out string reason, out bool editable)
        {
            reason = "unknown-focus";
            editable = false;
            if (hwnd == IntPtr.Zero) return false;
            char[] name = new char[128];
            int length = GetClassName(hwnd, name, name.Length);
            if (length <= 0) return false;
            string className = new string(name, 0, length);
            bool edit = String.Equals(className, "Edit", StringComparison.OrdinalIgnoreCase)
                || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
                || className.StartsWith("RICHEDIT", StringComparison.OrdinalIgnoreCase)
                || className.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase);
            if (!edit) return false;

            long style = GetWindowLongValue(hwnd, GWL_STYLE).ToInt64();
            if ((style & ES_PASSWORD) != 0)
            {
                reason = "password-field";
                return true;
            }
            if ((style & ES_READONLY) != 0 || !IsWindowEnabled(hwnd))
            {
                reason = "readonly-or-disabled-edit";
                return true;
            }
            editable = true;
            reason = "native-edit-focus";
            return true;
        }

        private static FocusSnapshot InspectFocus(AutomationElement element, IntPtr fallbackWindow, int fallbackProcessId)
        {
            try
            {
                if (element == null) return null;
                AutomationElement.AutomationElementInformation info = element.Current;
                IntPtr native = new IntPtr(info.NativeWindowHandle);
                IntPtr root = native == IntPtr.Zero ? IntPtr.Zero : GetAncestor(native, GA_ROOT);
                if (root == IntPtr.Zero) root = native;
                var ancestor = element;
                for (int depth = 0; root == IntPtr.Zero && ancestor != null && depth < 30; depth++)
                {
                    ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
                    if (ancestor == null) break;
                    IntPtr handle = new IntPtr(ancestor.Current.NativeWindowHandle);
                    if (handle != IntPtr.Zero) root = GetAncestor(handle, GA_ROOT);
                }
                // Chromium/Electron often reports NativeWindowHandle=0 for a
                // contenteditable descendant.  The foreground root captured
                // before opening the editor is still the correct containment
                // boundary in that case.
                if (root == IntPtr.Zero) root = fallbackWindow;
                uint rootPid;
                GetWindowThreadProcessId(root, out rootPid);
                if (rootPid == 0) rootPid = (uint)fallbackProcessId;
                bool writable = false;
                bool readOnlyKnown = false;
                object pattern;
                if (info.ControlType != ControlType.Edit && info.ControlType != ControlType.Document && info.ControlType != ControlType.ComboBox) return null;
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                {
                    writable = !((ValuePattern)pattern).Current.IsReadOnly;
                    readOnlyKnown = true;
                }
                else if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                {
                    object readOnly = ((TextPattern)pattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                    if (readOnly is bool)
                    {
                        writable = !(bool)readOnly;
                        readOnlyKnown = true;
                    }
                }
                // Some browser providers expose only TextPattern and return
                // NotSupported for IsReadOnly.  A keyboard-focusable Edit or
                // Document is the best available indication that Ctrl+V is
                // accepted; explicit read-only signals above still win.
                if (!readOnlyKnown)
                    writable = info.IsKeyboardFocusable && !info.IsPassword;

                FocusSnapshot result = new FocusSnapshot();
                result.Window = root;
                result.FocusWindow = native;
                result.ProcessId = (int)rootPid;
                result.HasKeyboardFocus = info.HasKeyboardFocus;
                result.Enabled = info.IsEnabled;
                result.Password = info.IsPassword;
                result.Writable = writable;
                result.Element = element;
                result.RuntimeId = element.GetRuntimeId();
                result.Reason = info.IsPassword ? "password-field" : writable ? "uia-editable-focus" : "readonly-or-unknown-input";
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static INPUT Keyboard(ushort key, bool keyUp)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.union.keyboard.wVk = key;
            input.union.keyboard.dwFlags = keyUp ? KEYEVENTF_KEYUP : 0;
            return input;
        }

        private static void Log(string name, double milliseconds)
        {
            Action<string, double> callback = log;
            if (callback == null) return;
            try { callback(name, milliseconds); }
            catch { }
        }

        private static void LogApplication(string prefix, int processId)
        {
            // Process names help correlate compatibility failures. Never log
            // window titles, control names, values, selected text or clipboard.
            try { using (var process = Process.GetProcessById(processId)) Log(prefix + process.ProcessName, 0); }
            catch { }
        }

        internal static void LogMetric(string name, double milliseconds)
        {
            Log(name, milliseconds);
        }

        internal static bool InputReleased()
        {
            foreach (int key in new[] { 0x01, 0x02, 0x10, 0x11, 0x12, 0x0D })
                if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
            return true;
        }
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

        private static IntPtr GetWindowLongValue(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));
        }

        private sealed class FocusSnapshot
        {
            internal IntPtr Window;
            internal IntPtr FocusWindow;
            internal int ProcessId;
            internal bool HasKeyboardFocus;
            internal bool Enabled;
            internal bool Password;
            internal bool Writable;
            internal string Reason;
            internal AutomationElement Element;
            internal int[] RuntimeId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public uint cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, char[] name, int maxCount);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachState);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }

}
