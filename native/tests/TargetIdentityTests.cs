using System;
using System.Reflection;
using PinyinVim.Native;
class TargetIdentityTests {
    static int checks;
    static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
    static void Main() {
        var target = new InputTarget(new IntPtr(9), new IntPtr(10), 17, true, "uia", null, new[] {1,2}, 1);
        var type = typeof(NativeWindows);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var snapshotType = type.GetNestedType("FocusSnapshot", BindingFlags.NonPublic);
        var snapshot = Activator.CreateInstance(snapshotType, true);
        Action<string,object> set = (name,value) => snapshotType.GetField(name,flags).SetValue(snapshot,value);
        set("Window",new IntPtr(9)); set("ProcessId",17); set("HasKeyboardFocus",true);
        set("Enabled",true); set("Password",false); set("Writable",true); set("RuntimeId",new[] {1,2});
        var matches = type.GetMethod("IsMatchingUiaTarget",BindingFlags.NonPublic|BindingFlags.Static);
        Func<bool> match = () => (bool)matches.Invoke(null,new[] {(object)target,snapshot});
        Check(match(),"same writable focused control accepted");
        set("RuntimeId",new[] {9,9}); Check(!match(),"different input in same window rejected");
        set("RuntimeId",new[] {1,2}); set("Window",new IntPtr(99)); Check(!match(),"different window rejected");
        set("Window",new IntPtr(9)); set("Password",true); Check(!match(),"password input rejected");
        set("Password",false); set("Writable",false); Check(!match(),"readonly input rejected");
        set("Writable",true); set("HasKeyboardFocus",false); Check(!match(),"unfocused control rejected");
        Check(type.GetMethod("IsCompatibleUiaFocus",BindingFlags.NonPublic|BindingFlags.Static)==null,"no same-window identity bypass");
        set("HasKeyboardFocus",true);
        var capture = type.GetMethod("IsCapturableUiaTarget",BindingFlags.NonPublic|BindingFlags.Static);
        var virtualTarget = new InputTarget(new IntPtr(9),IntPtr.Zero,17,false,"unknown-focus",null,null,1);
        Func<IntPtr,bool> capturable = foreground => (bool)capture.Invoke(null,new[] {(object)virtualTarget,snapshot,foreground});
        Check(capturable(new IntPtr(9)),"UIA logical focus does not require a native focus HWND");
        Check(!capturable(new IntPtr(99)),"foreground race still rejects capture");
        set("RuntimeId",new int[0]); Check(!capturable(new IntPtr(9)),"missing UIA identity rejects capture");
        set("RuntimeId",new[] {1,2}); set("ProcessId",99); Check(!capturable(new IntPtr(9)),"different root owner rejects capture");
        var container = type.GetMethod("IsFocusContainer",BindingFlags.NonPublic|BindingFlags.Static);
        Func<System.Windows.Automation.ControlType,bool> searchable = controlType => (bool)container.Invoke(null,new object[] {controlType});
        Check(searchable(System.Windows.Automation.ControlType.Window),"window focus permits scoped discovery");
        Check(searchable(System.Windows.Automation.ControlType.Pane),"pane focus permits scoped discovery");
        Check(!searchable(System.Windows.Automation.ControlType.Button),"button focus does not search for an input");
        Check(!searchable(System.Windows.Automation.ControlType.Edit),"specific edit focus cannot redirect to a different edit");
        Console.WriteLine("target identity: "+checks+" checks passed; no target apps or input used");
    }
}
