using System;
using System.Threading;

namespace PinyinVim.Native
{
    /// <summary>
    /// Serializes IME transitions and verifies every write.  Neovim emits
    /// InsertEnter/InsertLeavePre events; the host no longer infers those
    /// transitions from a delayed redraw state.
    /// </summary>
    internal sealed class ImeStateController
    {
        readonly object gate = new object();
        bool initialized;
        bool inInsert;
        bool hasSavedState;
        bool savedOpen;
        bool hasSavedContext;
        IntPtr savedContext;

        public bool Enabled { get; set; }

        public void Reset()
        {
            Reset(IntPtr.Zero);
        }

        public void Reset(IntPtr window)
        {
            lock (gate)
            {
                if (window != IntPtr.Zero && hasSavedContext)
                    Win.TryRestoreImeContext(window, savedContext, savedOpen);
                initialized = false;
                inInsert = false;
                hasSavedState = false;
                savedOpen = false;
                hasSavedContext = false;
                savedContext = IntPtr.Zero;
            }
        }

        public bool Apply(IntPtr window, string mode)
        {
            if (!Enabled || window == IntPtr.Zero || String.IsNullOrEmpty(mode)) return false;
            lock (gate)
            {
                if (!initialized)
                {
                    bool current;
                    if (Win.TryGetImeOpen(window, out current))
                    {
                        savedOpen = current;
                        hasSavedState = true;
                    }
                    IntPtr context;
                    if (Win.TryGetImeContext(window, out context))
                    {
                        savedContext = context;
                        hasSavedContext = true;
                    }
                    initialized = true;
                }

                bool insert = mode == "INSERT" || mode == "REPLACE" || mode == "COMMAND" || mode.StartsWith("INSERT-");
                if (insert)
                {
                    // CursorMovedI/mode reports must not restore the old state
                    // over an IME change the user made during this input session.
                    if (inInsert) return true;
                    bool ok = hasSavedContext
                        ? Win.TryRestoreImeContext(window, savedContext, hasSavedState ? savedOpen : true)
                        : (hasSavedState ? SetVerified(window, savedOpen) : true);
                    if (!ok && hasSavedState) ok = SetVerified(window, savedOpen);
                    inInsert = true;
                    return ok;
                }

                if (inInsert)
                {
                    bool current;
                    if (Win.TryGetImeOpen(window, out current))
                    {
                        savedOpen = current;
                        hasSavedState = true;
                    }
                    IntPtr context;
                    if (Win.TryGetImeContext(window, out context))
                    {
                        savedContext = context;
                        hasSavedContext = true;
                    }
                    inInsert = false;
                }

                // Normal, Visual and Select transitions all
                // pass through this barrier.  Removing the editor's HIMC is
                // stronger than closing the IME: some Windows IMEs leave a
                // composition-capable context behind after IMC_SETOPENSTATUS.
                if (Win.TryDetachImeContext(window)) return true;
                // Keep the open-status fallback for IMEs/windows that do not
                // expose an associateable context to this host process.
                return SetVerified(window, false);
            }
        }

        bool SetVerified(IntPtr window, bool open)
        {
            if (!Win.TrySetImeOpen(window, open)) return false;
            for (int i = 0; i < 5; i++)
            {
                bool actual;
                if (Win.TryGetImeOpen(window, out actual) && actual == open) return true;
                Thread.Sleep(5);
            }
            return false;
        }
    }
}
