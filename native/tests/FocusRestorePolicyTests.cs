using System;
using PinyinVim.Native;

class FocusRestorePolicyTests
{
    static int checks;
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static void Main()
    {
        int searches = 0;
        bool foreground = true;
        Func<string, bool> valid = result => foreground && result == "editable";
        Func<string> search = () => { searches++; return "editable"; };
        Check(FocusRestorePolicy.Discover("editable", () => true, search, valid) == "editable" && searches == 0,
            "direct focused input avoids descendant search");
        Check(FocusRestorePolicy.Discover("window", () => true, search, valid) == "editable" && searches == 1,
            "focused container can resolve a validated editable descendant");
        searches = 0;
        Check(FocusRestorePolicy.Discover("button", () => false, search, valid) == "button" && searches == 0,
            "non-container focus never searches for unrelated text fields");
        Check(FocusRestorePolicy.Discover("window", () => true, () => "readonly", valid) == "window",
            "invalid descendant cannot make a container editable");
        Check(FocusRestorePolicy.Discover("window", () => true, () => null, valid) == "window",
            "absent focused descendant retains original classification");
        Check(FocusRestorePolicy.Discover("window", () => true, () => { foreground = false; return "editable"; }, valid) == "window",
            "foreground change during provider search rejects candidate");
        long time = 0;
        int requests = 0;
        string cancellation = null;
        Action<int> pause = milliseconds => time += milliseconds;
        Action restore = () => requests++;
        Func<string> guard = () => cancellation;
        Func<string> runNatural = () => FocusRestorePolicy.Wait(guard, () => time >= 80, restore, () => time, pause, 120, 800);
        Check(runNatural() == null && requests == 0 && time == 80, "natural asynchronous focus avoids SetFocus");
        time = 0; requests = 0;
        Check(FocusRestorePolicy.Wait(guard, () => requests > 0 && time >= 260, restore, () => time, pause, 120, 800) == null
            && requests == 1 && time == 260, "delayed provider focus is observed after exactly one request");
        time = 0; requests = 0;
        Check(FocusRestorePolicy.Wait(guard, () => false, restore, () => time, pause, 120, 800) == "uia-focus-mismatch"
            && requests == 1 && time == 800, "replacement control never satisfies captured identity and wait is bounded");
        time = 0; requests = 0; cancellation = "foreground-changed";
        Check(runNatural() == "foreground-changed" && requests == 0 && time == 0, "external app cancels before focus writes");
        cancellation = null;
        Check(FocusRestorePolicy.Wait(guard, () => { cancellation = "session-changed"; return true; }, restore,
            () => time, pause, 120, 800) == "session-changed", "cancellation during provider read defeats apparent success");
        time = 0; requests = 0; cancellation = null;
        Check(FocusRestorePolicy.Wait(guard, () => false, () => { requests++; cancellation = "focus-or-keys-changed"; },
            () => time, pause, 120, 800) == "focus-or-keys-changed" && requests == 1 && time == 120,
            "user interaction during SetFocus cancels without another focus write");
        time = 0; requests = 0; cancellation = null;
        Check(FocusRestorePolicy.Wait(guard, () => { time = 801; return true; }, restore, () => time, pause, 120, 800)
            == "uia-focus-mismatch" && requests == 0, "late provider success cannot cross the focus deadline");
        time = 0; requests = 0;
        Check(FocusRestorePolicy.Wait(() => time >= 60 ? "operation-timeout" : null, () => false, restore,
            () => time, pause, 120, 800) == "operation-timeout" && requests == 0 && time == 60,
            "overall operation budget also bounds natural focus observation");
        Console.WriteLine("focus discovery/restoration: " + checks + " checks passed; no windows or input used");
    }
}
