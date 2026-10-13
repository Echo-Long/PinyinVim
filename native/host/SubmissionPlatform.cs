using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PinyinVim.Native
{
    public interface ISubmissionInput : IDisposable
    {
        bool Ready { get; }
        bool OwnsTrigger(int key);
        bool OwnsModifier(int key);
        bool AllowsModifier(int key);
        int[] RestoreModifiers();
        void Complete();
    }

    public interface ISubmissionPlatform
    {
        ISubmissionInput CaptureKeys(int trigger);
        Task<bool> CopyAsync(string text);
        Task<PasteResult> DeliverAsync(InputTarget target, IntPtr editor, bool paste, ISubmissionInput input);
    }

    internal sealed class WindowsSubmissionPlatform : ISubmissionPlatform, IDisposable
    {
        WindowsSubmissionInput input;
        public ISubmissionInput CaptureKeys(int trigger)
        {
            if (input == null || input.Closed) input = new WindowsSubmissionInput();
            input.Arm(trigger);
            return input;
        }

        public async Task<bool> CopyAsync(string text)
        {
            for (int n = 0; n < 6; n++)
            {
                try { Clipboard.SetText(text, TextDataFormat.UnicodeText); return true; }
                catch (ExternalException) { }
                await Task.Delay(8);
            }
            return false;
        }

        public Task<PasteResult> DeliverAsync(InputTarget target, IntPtr editor, bool paste, ISubmissionInput keys)
        {
            return NativeWindows.RestoreForSubmissionAsync(target, editor, paste, keys);
        }

        public void Dispose() { if (input != null) input.Dispose(); }
    }

    internal sealed class WindowsSubmissionInput : ISubmissionInput
    {
        internal static readonly UIntPtr InputMarker = new UIntPtr(0x50495631u);
        internal static readonly UIntPtr RestoreMarker = new UIntPtr(0x50495632u);
        readonly SubmissionKeyState state = new SubmissionKeyState();
        readonly HookProc callback;
        IntPtr hook;
        bool failed, completed;
        internal bool Closed { get; private set; }
        public bool Ready { get { return !failed && !Closed; } }

        internal WindowsSubmissionInput()
        {
            callback = Filter;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            failed = hook == IntPtr.Zero;
        }

        internal void Arm(int trigger)
        {
            completed = false;
            if (failed) return;
            state.Arm(trigger, NativeWindows.IsKeyDown);
            // Release only the submitting key, while the editor still owns the
            // foreground. Ctrl stays physical; a scoped hook consumes repeats.
            if (!NativeWindows.ReleaseSubmissionTriggers(state.HeldTriggers())) failed = true;
        }

        public bool OwnsModifier(int key) { return state.OwnsModifier(key); }
        public bool AllowsModifier(int key) { return state.AllowsModifier(key); }
        public bool OwnsTrigger(int key) { return state.OwnsTrigger(key); }
        public int[] RestoreModifiers() { return state.RestoreModifiers(); }

        IntPtr Filter(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return CallNextHookEx(hook, code, message, data);
            var key = (KeyEvent)Marshal.PtrToStructure(data, typeof(KeyEvent));
            bool up = message.ToInt32() == 0x101 || message.ToInt32() == 0x105;
            bool injected = (key.flags & 0x10) != 0;
            if (injected)
            {
                // Physical release may race with a queued modifier restoration.
                // Suppress a stale restoration instead of leaving Ctrl stuck.
                if (key.extra == RestoreMarker && !up && !state.OwnsModifier((int)key.key)) return new IntPtr(1);
                return CallNextHookEx(hook, code, message, data);
            }
            bool suppress = state.PhysicalEvent((int)key.key, up);
            if (completed && !state.HasHeldTrigger) Dispose();
            return suppress ? new IntPtr(1) : CallNextHookEx(hook, code, message, data);
        }

        public void Complete()
        {
            completed = true;
            // Completion does not wait for key-up. Keep only the repeat filter
            // until this physical press ends, then immediately remove it.
            if (!state.HasHeldTrigger) Dispose();
        }

        public void Dispose()
        {
            if (Closed) return;
            Closed = true;
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }

        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        struct KeyEvent { internal uint key, scan, flags, time; internal UIntPtr extra; }
        [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string module);
    }
}
