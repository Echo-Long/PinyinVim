using System;

namespace PinyinVim.Native
{
    internal static class PastePolicy
    {
        internal static bool AllowsForeground(IntPtr foreground, uint processId, IntPtr target, IntPtr editor, int hostProcessId)
        {
            return foreground == IntPtr.Zero || foreground == target || foreground == editor
                || processId == (uint)hostProcessId;
        }

        internal static bool SameRuntimeId(int[] left, int[] right)
        {
            if (left == null || right == null || left.Length == 0 || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        internal static string FailureMessage(string reason)
        {
            switch (reason)
            {
                case "key-release-timeout": case "keys-not-released": case "focus-or-keys-changed": case "input-state-changed":
                    return "回填期间输入状态发生变化，未自动粘贴。文字已复制到剪贴板，草稿已保留。";
                case "input-isolation-unavailable":
                    return "未能隔离本次提交按键，未自动粘贴。文字已复制到剪贴板，草稿已保留。";
                case "foreground-changed": case "session-changed":
                    return "当前窗口已切换，本次自动粘贴已取消。文字已复制到剪贴板，草稿已保留。";
                case "operation-timeout":
                    return "目标应用响应超时，未自动粘贴。文字已复制到剪贴板，草稿已保留。";
                case "paste-busy":
                    return "上一次回填仍在处理中，未自动粘贴。文字已复制到剪贴板，草稿已保留。";
                case "uia-provider-failed":
                    return "目标应用未能恢复原输入框，未自动粘贴。文字已复制到剪贴板，草稿已保留。";
                case "send-input-failed":
                    return "Windows 未能发送粘贴按键。文字已复制到剪贴板，草稿已保留。";
                case "foreground-restore-failed":
                    return "未能切回原窗口。文字已复制到剪贴板，草稿已保留。";
                default:
                    return "未能确认原输入框，未自动粘贴。文字已复制到剪贴板，草稿已保留。";
            }
        }

        internal static string SubmissionNotice(bool copied, bool pasteRequested, bool pasteSent, string failure)
        {
            if (!copied) return null;
            if (pasteSent) return "文字已复制到剪贴板，并已向原输入框发送粘贴。";
            return pasteRequested ? FailureMessage(failure) : "文字已复制到剪贴板，请手动粘贴。草稿已保留。";
        }
    }
}
