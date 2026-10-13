using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;

class NotificationSettingsTests
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    [STAThread] static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("PINYINVIM_CAPSULE_TEST_PROFILE", null);
        var assembly = Assembly.LoadFrom(args[0]);
        var hostType = assembly.GetType("PinyinVim.Native.Host", true);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var host = FormatterServices.GetUninitializedObject(hostType);
        string profile = Path.Combine(Path.GetTempPath(), "PinyinVim-notifications-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        hostType.GetField("profile", flags).SetValue(host, profile);
        hostType.GetField("root", flags).SetValue(host, Path.GetDirectoryName(args[0]));
        hostType.GetField("json", flags).SetValue(host, new JavaScriptSerializer());
        var load = hostType.GetMethod("LoadSettings", flags);
        var defaults = (Dictionary<string, object>)load.Invoke(host, null);
        Check(!(bool)defaults["notifications"], "Fresh profiles must disable notifications");
        Check((int)defaults["backgroundTransparency"] == 50 && (int)defaults["capsuleTransparency"] == 20 && (int)defaults["blurRadius"] == 5,
            "Fresh appearance must use 50% background, 20% capsules and 5px blur");
        string settingsPath = Path.Combine(profile, "settings.json");
        foreach (string json in new[] {"{\"words\":false}", "{\"notifications\":true}", "{\"notifications\":\"false\"}"}) {
            File.WriteAllText(settingsPath, json);
            var loaded = (Dictionary<string, object>)load.Invoke(host, null);
            Check((bool)loaded["notifications"] == (json == "{\"notifications\":true}"), "Saved opt-in must survive; missing or malformed values must default off");
            if (!json.Contains("notifications")) Check(!(bool)loaded["words"], "Legacy word preference must survive");
        }
        File.WriteAllText(settingsPath, "{\"notifications\":false,\"theme\":\"lavender\",\"clearOnClose\":true}");
        var settings = (Dictionary<string, object>)load.Invoke(host, null);
        Check(!(bool)settings["notifications"], "Notification opt-out must survive loading");
        Check((string)settings["theme"] == "lavender" && (bool)settings["clearOnClose"], "Loading opt-out changed unrelated preferences");
        hostType.GetField("settings", flags).SetValue(host, settings);
        var notify = hostType.GetMethod("ShowNotification", flags);
        foreach(var unknown in new[]{new Dictionary<string,object>(), new Dictionary<string,object>{{"notifications","on"}}}) {
            hostType.GetField("settings",flags).SetValue(host,unknown);
            notify.Invoke(host,new object[]{1800,"fixture","fixture",ToolTipIcon.Info});
            Check(true,"Unknown or malformed notification preference must not dispatch");
        }
        hostType.GetField("settings",flags).SetValue(host,null);
        notify.Invoke(host,new object[]{1800,"fixture","fixture",ToolTipIcon.Info});
        Check(true,"Uninitialized preferences must not dispatch");
        hostType.GetField("settings",flags).SetValue(host,settings);
        foreach (var kind in new[] {ToolTipIcon.Info, ToolTipIcon.Error}) {
            // No tray exists: a muted notification must return before OS dispatch.
            notify.Invoke(host, new object[] {1800, "fixture", "fixture", kind});
            Check(true, "Muted notification reached tray");
        }
        var sessionType = assembly.GetType("PinyinVim.Native.EditorSession", true);
        var session = Activator.CreateInstance(sessionType, true);
        sessionType.GetMethod("BeginForContract", flags).Invoke(session, new object[] {7L, false});
        hostType.GetField("activeSession", flags).SetValue(host, session);
        var copied = new Dictionary<string, object> {{"kind", "clipboard-copied"}, {"session", 7}};
        ((Task)hostType.GetMethod("OnMessage", flags).Invoke(host, new object[] {copied})).GetAwaiter().GetResult();
        Check(true, "Muted explicit copy reached tray");
        var preferenceType = hostType.GetNestedType("PreferencesWindow", BindingFlags.NonPublic);
        var window = (Window)Activator.CreateInstance(preferenceType, flags, null, new[] {host}, null);
        preferenceType.GetMethod("Select", flags).Invoke(window, new object[] {1});
        var editors = (IDictionary)preferenceType.GetField("editors", flags).GetValue(window);
        var toggle = editors["notifications"] as ToggleButton;
        Check(toggle != null && toggle.IsChecked == false, "Settings UI must show persisted opt-out");
        settings["notifications"] = true;
        preferenceType.GetMethod("SyncValues", flags).Invoke(window, null);
        Check(toggle.IsChecked == true, "Settings UI must reflect re-enabled notifications");
        var invalid = new Dictionary<string, object>(settings) {{"unrelated", "fixture"}};
        invalid["notifications"] = "off";
        object[] candidate = {invalid, null};
        string before = File.ReadAllText(settingsPath);
        Check(!(bool)hostType.GetMethod("SaveCapsulePreferences", flags).Invoke(host, candidate), "Non-boolean notification setting must be rejected");
        Check(File.ReadAllText(settingsPath) == before, "Rejected notification setting changed saved preferences");
        window.Close();
        using (var stream = assembly.GetManifestResourceStream("PinyinVim.AppIcon")) {
            Check(stream != null && stream.Length > 0, "Application icon must be embedded");
            using (var expected = new Icon(stream, 32, 32))
            using (var actual = (Icon)hostType.GetMethod("MakeIcon", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null))
            using (var expectedBitmap = expected.ToBitmap())
            using (var actualBitmap = actual.ToBitmap()) {
                bool equal = actual.Width == 32 && actual.Height == 32;
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                    equal &= expectedBitmap.GetPixel(x, y) == actualBitmap.GetPixel(x, y);
                Check(equal, "Tray/window icon does not match the embedded application icon");
            }
        }
        Console.WriteLine("notification preferences, muted dispatch, actual settings UI and icon: " + checks + " passed");
    }
}
