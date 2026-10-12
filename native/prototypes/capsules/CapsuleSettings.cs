using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PinyinVim.Native
{
    internal sealed class CapsuleShortcut
    {
        internal uint Modifiers, Key;
        internal string Name, VimKey;
        internal static bool TryParse(string text, bool global, out CapsuleShortcut shortcut, out string error)
        {
            shortcut = null; error = "快捷键格式应为 Ctrl+Shift+X、Ctrl+Alt+H、F9 或 Esc";
            if (String.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().Split('+');
            uint modifiers = 0;
            for (int i = 0; i < parts.Length - 1; ++i)
            {
                uint modifier;
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": modifier = 2; break;
                    case "alt": modifier = 1; break;
                    case "shift": modifier = 4; break;
                    default: return false;
                }
                if ((modifiers & modifier) != 0) return false;
                modifiers |= modifier;
            }
            string keyName = parts[parts.Length - 1].Trim().ToUpperInvariant();
            uint key = 0;
            if (keyName == "ESC" || keyName == "ESCAPE") { key = 27; keyName = "Esc"; }
            else if (keyName.Length == 1 && keyName[0] >= 'A' && keyName[0] <= 'Z') key = keyName[0];
            else if (keyName.Length == 1 && keyName[0] >= '0' && keyName[0] <= '9') key = keyName[0];
            else if (keyName == "ENTER" || keyName == "RETURN") { key = 13; keyName = "Enter"; }
            else if (keyName.StartsWith("F"))
            {
                int number;
                if (Int32.TryParse(keyName.Substring(1), out number) && number >= 1 && number <= 12) { key = (uint)(111 + number); keyName = "F" + number; }
            }
            if (key == 0) return false;
            if (global && (modifiers & 3) == 0) { error = "呼出快捷键需包含 Ctrl 或 Alt"; return false; }
            if (!global && modifiers == 0 && key != 27 && (key < 112 || key > 123)) { error = "编辑器快捷键应使用功能键或带 Ctrl/Alt 的组合"; return false; }
            if (!global && modifiers == 4) { error = "编辑器快捷键不能只包含 Shift"; return false; }
            if ((global && (modifiers & 2) != 0 && (key == 'C' || key == 'V' || key == 13)) || (modifiers == 1 && key == 115))
            { error = "此组合用于复制、粘贴、提交、设置或窗口关闭，请选择其他快捷键"; return false; }
            string name = ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "") + ((modifiers & 4) != 0 ? "Shift+" : "") + keyName;
            string vimName = keyName == "Enter" ? "CR" : keyName;
            if (key >= 'A' && key <= 'Z') vimName = keyName.ToLowerInvariant();
            string vimKey = "<" + ((modifiers & 2) != 0 ? "C-" : "") + ((modifiers & 1) != 0 ? "M-" : "") + ((modifiers & 4) != 0 ? "S-" : "") + vimName + ">";
            shortcut = new CapsuleShortcut { Modifiers = modifiers, Key = key, Name = name, VimKey = vimKey };
            error = ""; return true;
        }
    }

    internal sealed partial class Host
    {
        // Theme ids are persisted instead of raw colors so the Lua renderer
        // and the WPF settings page share a stable, auditable contract.
        internal static readonly Dictionary<string,string> CapsuleThemeNames = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            {"mist-blue", "雾蓝"}, {"lemon", "柠檬黄"}, {"lavender", "薰衣草"},
            {"mint", "薄荷绿"}, {"peach", "蜜桃"}, {"rose", "玫瑰"},
            {"sky", "晴空蓝"}, {"sand", "砂岩"}, {"forest", "森林"},
            {"graphite", "石墨"}
        };
        static readonly Dictionary<string,string> ShortcutDefaults = new Dictionary<string,string> {
            {"submit","Ctrl+Enter"},{"settings","F2"},{"copy","Ctrl+C"},{"paste","Ctrl+V"}
        };
        CapsuleShortcut capsuleShowShortcut;
        int capsuleHotkeyId = 1;
        string capsuleStartupNotice;

        void LoadCapsuleSettings(Dictionary<string, object> values, Dictionary<string, object> saved)
        {
            values["showHotkey"] = "Ctrl+Shift+X"; values["hideHotkey"] = "Esc";
            values["backgroundTransparency"] = 50; values["capsuleTransparency"] = 20;
            values["blurRadius"] = 5; values["backgroundImagePath"] = ""; values["theme"] = "mist-blue";
            foreach (var pair in ShortcutDefaults) values[pair.Key + "Hotkey"] = pair.Value;
            if (saved != null)
            {
                foreach (string name in new[] { "showHotkey", "hideHotkey", "backgroundTransparency", "capsuleTransparency", "blurRadius", "backgroundImagePath", "theme", "submitHotkey", "settingsHotkey", "copyHotkey", "pasteHotkey" })
                    if (saved.ContainsKey(name)) values[name] = saved[name];
                int shell, panel;
                if (!saved.ContainsKey("backgroundTransparency") && !saved.ContainsKey("capsuleTransparency") &&
                    ReadPercentage(saved, "shellOpacity", out shell) && ReadPercentage(saved, "editorOpacity", out panel))
                {
                    // The old built-in pairs used a coupled, incorrectly
                    // composed opacity model. Restore the accepted palette.
                    // Preserve deliberate custom values as independent layers.
                    if (!((shell == 35 && panel == 45) || (shell == 65 && panel == 75)))
                    { values["backgroundTransparency"] = 100 - shell; values["capsuleTransparency"] = 100 - panel; }
                }
            }
            int savedVersion, savedBackground, savedCapsule;
            if (saved != null && saved.ContainsKey("appearanceVersion") && Int32.TryParse(Convert.ToString(saved["appearanceVersion"]),out savedVersion) && savedVersion < 3 &&
                ReadPercentage(values,"backgroundTransparency",out savedBackground) && savedBackground == 35 &&
                ReadPercentage(values,"capsuleTransparency",out savedCapsule) && savedCapsule == 0)
            { values["backgroundTransparency"] = 50; values["capsuleTransparency"] = 20; }
            foreach (var pair in ShortcutDefaults) {
                CapsuleShortcut parsed; string detail;
                if (!CapsuleShortcut.TryParse(GetString(values, pair.Key + "Hotkey"), false, out parsed, out detail))
                    CapsuleShortcut.TryParse(pair.Value, false, out parsed, out detail);
                values[pair.Key + "Hotkey"] = parsed.Name; values[pair.Key + "Notation"] = parsed.VimKey;
            }
            CapsuleShortcut show, hide; string error;
            if (!CapsuleShortcut.TryParse(GetString(values, "showHotkey"), true, out show, out error))
                CapsuleShortcut.TryParse("Ctrl+Shift+X", true, out show, out error);
            if (!CapsuleShortcut.TryParse(GetString(values, "hideHotkey"), false, out hide, out error))
                CapsuleShortcut.TryParse("Esc", false, out hide, out error);
            values["showHotkey"] = show.Name; values["hideHotkey"] = hide.Name; values["hideNotation"] = hide.VimKey;
            int percentage;
            if (!ReadPercentage(values, "backgroundTransparency", out percentage)) values["backgroundTransparency"] = 50;
            if (!ReadPercentage(values, "capsuleTransparency", out percentage)) values["capsuleTransparency"] = 20;
            if (!ReadRange(values, "blurRadius", 0, 24, out percentage)) values["blurRadius"] = 5;
            if (!values.ContainsKey("backgroundImagePath") || values["backgroundImagePath"] == null) values["backgroundImagePath"] = "";
            string theme = GetString(values, "theme");
            if (!CapsuleThemeNames.ContainsKey(theme)) values["theme"] = "mist-blue";
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {show.Name, hide.Name};
            bool conflict = show.Name == hide.Name;
            foreach (var pair in ShortcutDefaults) conflict |= !used.Add(GetString(values,pair.Key + "Hotkey"));
            if (conflict) {
                values["showHotkey"] = "Ctrl+Shift+X"; values["hideHotkey"] = "Esc"; values["hideNotation"] = "<Esc>";
                foreach (var pair in ShortcutDefaults) {
                    CapsuleShortcut parsed; string detail;
                    CapsuleShortcut.TryParse(pair.Value,false,out parsed,out detail);
                    values[pair.Key + "Hotkey"] = parsed.Name; values[pair.Key + "Notation"] = parsed.VimKey;
                }
            }
            values["appearanceVersion"] = 3;
        }

        void InitializeCapsuleHotkey()
        {
            string error;
            CapsuleShortcut.TryParse(GetString(settings, "showHotkey"), true, out capsuleShowShortcut, out error);
            if (CapsuleNative.IsTestInstance) return;
            if (Win.RegisterHotKey(Handle, capsuleHotkeyId, capsuleShowShortcut.Modifiers | 0x4000, capsuleShowShortcut.Key)) return;
            foreach (string alternative in new[] { "Ctrl+Alt+F10", "Ctrl+Alt+F11", "Ctrl+Alt+F12" })
            {
                CapsuleShortcut next; CapsuleShortcut.TryParse(alternative, true, out next, out error);
                if (!Win.RegisterHotKey(Handle, capsuleHotkeyId, next.Modifiers | 0x4000, next.Key)) continue;
                capsuleStartupNotice = settings["showHotkey"] + " 已被占用，暂用 " + next.Name;
                capsuleShowShortcut = next; settings["showHotkey"] = next.Name;
                return;
            }
            throw new InvalidOperationException("呼出快捷键和备用快捷键均被占用。请退出旧版托盘实例后重试。");
        }

        static bool ReadPercentage(Dictionary<string, object> values, string name, out int percentage)
        {
            object value; percentage = 0;
            return values.TryGetValue(name, out value) && Int32.TryParse(Convert.ToString(value), out percentage) && percentage >= 0 && percentage <= 100;
        }

        static bool ReadRange(Dictionary<string, object> values, string name, int minimum, int maximum, out int result)
        {
            object value; result = 0;
            return values.TryGetValue(name, out value) && Int32.TryParse(Convert.ToString(value), out result) && result >= minimum && result <= maximum;
        }

        bool ApplyCapsuleSettings(Dictionary<string, object> next, out string error)
        {
            CapsuleShortcut show, hide;
            if (!CapsuleShortcut.TryParse(GetString(next, "showHotkey"), true, out show, out error) ||
                !CapsuleShortcut.TryParse(GetString(next, "hideHotkey"), false, out hide, out error)) return false;
            int background, capsule;
            if (!ReadPercentage(next, "backgroundTransparency", out background) || !ReadPercentage(next, "capsuleTransparency", out capsule))
            { error = "背景与胶囊透明度各为 0–100；0 不透明，100 完全透明"; return false; }
            int blur;
            if (!ReadRange(next, "blurRadius", 0, 24, out blur))
            { error = "模糊半径范围为 0–24"; return false; }
            string theme = GetString(next, "theme");
            if (!CapsuleThemeNames.ContainsKey(theme))
            { error = "主题色无效，请从设置页选择预设主题"; return false; }
            string imagePath = GetString(next, "backgroundImagePath");
            if (imagePath.Length > 1024) { error = "背景图路径过长"; return false; }
            next["blurRadius"] = blur; next["backgroundImagePath"] = imagePath; next["theme"] = theme.ToLowerInvariant();
            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {show.Name, hide.Name};
            if (show.Name == hide.Name) { error = "呼出与收起快捷键不能相同"; return false; }
            foreach (var pair in ShortcutDefaults) {
                CapsuleShortcut parsed;
                string key = pair.Key + "Hotkey";
                if (!next.ContainsKey(key)) next[key] = pair.Value;
                if (!CapsuleShortcut.TryParse(GetString(next,key),false,out parsed,out error)) return false;
                if (parsed.Key == 27) { error = "Esc 保留编辑器原生退出模式语义"; return false; }
                if (!occupied.Add(parsed.Name)) { error = parsed.Name + " 与另一项快捷键冲突"; return false; }
                next[key] = parsed.Name; next[pair.Key + "Notation"] = parsed.VimKey;
            }
            if (capsuleShowShortcut != null && show.Name != capsuleShowShortcut.Name && !CapsuleNative.IsTestInstance)
            {
                int newId = capsuleHotkeyId == 1 ? 2 : 1;
                // Register the replacement before releasing the active key.
                // A rejected edit therefore leaves the working shortcut intact.
                if (!Win.RegisterHotKey(Handle, newId, show.Modifiers | 0x4000, show.Key))
                { error = show.Name + " 已被其他程序占用，已保留原快捷键"; return false; }
                Win.UnregisterHotKey(Handle, capsuleHotkeyId); capsuleHotkeyId = newId;
            }
            capsuleShowShortcut = show;
            next["showHotkey"] = show.Name; next["hideHotkey"] = hide.Name; next["hideNotation"] = hide.VimKey;
            next["appearanceVersion"] = 3;
            next.Remove("shellOpacity"); next.Remove("editorOpacity");
            error = ""; return true;
        }

        void RejectCapsuleSettings(Dictionary<string, object> message, string error)
        {
            Send(new Dictionary<string, object> { { "kind", "settings" }, { "settings", settings } });
            Send(new Dictionary<string, object> { { "kind", "capsule-notice" }, { "text", error } });
            SendAck(message, "settings-ack", false, error);
        }
    }
}
