using System;

namespace PinyinVim.Native.Tests
{
    internal static class PastePolicyTests
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Main()
        {
            Check(PastePolicy.AllowsForeground(new IntPtr(9), 17, new IntPtr(9), new IntPtr(3), 99), "target foreground");
            Check(PastePolicy.AllowsForeground(new IntPtr(3), 99, new IntPtr(9), new IntPtr(3), 99), "editor foreground");
            Check(!PastePolicy.AllowsForeground(new IntPtr(7), 88, new IntPtr(9), new IntPtr(3), 99), "foreign foreground");
            Check(PastePolicy.SameRuntimeId(new[] { 1, 2 }, new[] { 1, 2 }), "runtime identity");
            Check(!PastePolicy.SameRuntimeId(new[] { 1, 2 }, new[] { 1, 3 }), "different runtime identity");
            Check(PastePolicy.FailureMessage("key-release-timeout").IndexOf("按键") >= 0, "key message");
            Check(PastePolicy.FailureMessage("foreground-changed").IndexOf("窗口") >= 0, "window message");
            Check(PastePolicy.FailureMessage("uia-focus-mismatch").IndexOf("已复制到剪贴板") >= 0, "fallback explicitly names clipboard");
            Check(PastePolicy.FailureMessage("uia-provider-failed").IndexOf("超时") < 0, "provider refusal is not reported as timeout");
            Check(PastePolicy.SubmissionNotice(false,false,false,null)==null,"failed copy has no copied success notice");
            Check(PastePolicy.SubmissionNotice(true,false,false,null).Contains("已复制到剪贴板"),"non-input and unknown input copy notify");
            Check(PastePolicy.SubmissionNotice(true,true,false,"uia-focus-mismatch").Contains("未自动粘贴"),"paste fallback notifies copied result");
            Check(PastePolicy.SubmissionNotice(true,true,true,null).Contains("发送粘贴"),"paste dispatch notice does not claim receipt");
            Console.WriteLine("Paste policy: 13 focused checks passed");
        }
    }
}
