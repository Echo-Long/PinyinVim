using System;
using PinyinVim.Native;

namespace PinyinVim.Native
{
    internal sealed class InputTarget
    {
        public IntPtr Window { get; set; }
        public bool Editable { get; set; }
    }
}

internal static class EditorSessionTests
{
    static int failures;
    static int checks;

    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
        {
            failures++;
            Console.WriteLine("FAIL: " + message);
        }
    }

    public static int Main()
    {
        var paste = new EditorSession();
        paste.BeginForContract(7, true);
        Check(paste.RouteValue == "paste", "validated target selects paste route");
        Check(paste.RouteLabel == "粘贴提交", "validated target label is paste submit");
        Check(paste.IsCurrent(7), "current session accepts its id");
        Check(!paste.IsCurrent(6), "stale session id is rejected");
        string reason;
        Check(paste.TryBeginSubmit("submit-1", out reason), "first request accepted");
        Check(!paste.TryBeginSubmit("submit-2", out reason) && reason == "submit-busy", "in-flight request rejected");
        paste.FinishSubmit(false);
        Check(!paste.TryBeginSubmit("submit-1", out reason) && reason == "duplicate-request", "failed request replay rejected");
        Check(paste.TryBeginSubmit("submit-3", out reason), "new request may retry an actual failure");
        paste.FinishSubmit(true);
        Check(!paste.TryBeginSubmit("submit-3", out reason), "completed request replay rejected");
        Check(!paste.TryBeginSubmit("submit-4", out reason) && reason == "already-submitted", "queued distinct request cannot submit a completed session");
        paste.BeginForContract(9, true);
        Check(!paste.TryBeginSubmit("", out reason) && reason == "invalid-request", "missing identity rejected");
        Check(paste.TryBeginSubmit("submit-1", out reason), "new invocation resets deduplication");
        paste.FinishSubmit(true);

        var clipboard = new EditorSession();
        clipboard.BeginForContract(8, false);
        Check(clipboard.RouteValue == "clipboard", "unvalidated target selects clipboard route");
        Check(clipboard.RouteLabel == "剪贴板提交", "clipboard label is explicit");
        clipboard.SetMode("INSERT");
        Check(clipboard.Mode == "INSERT", "mode belongs to the same session object");
        clipboard.Reset();
        Check(!clipboard.IsCurrent(8), "reset invalidates the old session");

        Console.WriteLine(failures == 0 ? "editor session tests: " + checks + " passed" : "editor session tests: " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}
