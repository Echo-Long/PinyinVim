using System;
using PinyinVim.Native;
namespace PinyinVim.Native {
    internal static class Win {
        internal static bool Open = true;
        internal static IntPtr Context = new IntPtr(12);
        internal static bool TryGetImeOpen(IntPtr w, out bool open) { open = Open; return true; }
        internal static bool TryGetImeContext(IntPtr w, out IntPtr context) { context = Context; return Context != IntPtr.Zero; }
        internal static bool TryDetachImeContext(IntPtr w) { Context = IntPtr.Zero; Open = false; return true; }
        internal static bool TryRestoreImeContext(IntPtr w, IntPtr context, bool open) { Context = context; Open = open; return true; }
        internal static bool TrySetImeOpen(IntPtr w, bool open) { Open = open; return true; }
    }
}
class ImeControllerTests {
    static int checks;
    static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
    static void Main() {
        var c = new ImeStateController { Enabled = true };
        var w = new IntPtr(1);
        Check(c.Apply(w,"NORMAL") && !Win.Open, "Normal forces English");
        Check(c.Apply(w,"INSERT") && Win.Open, "Insert restores saved Chinese state");
        Win.Open = false;
        Check(c.Apply(w,"INSERT") && !Win.Open, "cursor report preserves user's manual change");
        Check(c.Apply(w,"VISUAL") && !Win.Open, "Visual uses English");
        Check(c.Apply(w,"INSERT") && !Win.Open, "next Insert restores last Insert state");
        Win.Open = true;
        c.Apply(w,"NORMAL");
        Check(c.Apply(w,"COMMAND") && Win.Open, "search and command input permit Chinese");
        c.Apply(w,"NORMAL");
        Check(c.Apply(w,"REPLACE") && Win.Open, "Replace permits Chinese");
        c.Enabled = false; Win.Open = false;
        Check(!c.Apply(w,"INSERT") && !Win.Open, "disabled policy leaves user's IME state untouched");
        Console.WriteLine("IME controller: " + checks + " lifecycle checks passed (fake platform; no real IME)");
    }
}
