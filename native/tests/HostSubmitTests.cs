using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PinyinVim.Native;
class HostSubmitTests {
    [STAThread] static void Main(string[] args) {
        try { Run(args); }
        catch(Exception error) {
            for(Exception current=error; current!=null; current=current.InnerException) {
                Console.Error.WriteLine(current.GetType().FullName);
                Console.Error.WriteLine(current.Message);
            }
            Environment.Exit(1);
        }
    }
    static void Run(string[] args) {
        string profile = Path.Combine(Path.GetTempPath(), "PinyinVim-submit-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        Environment.SetEnvironmentVariable("PINYINVIM_CAPSULE_TEST_PROFILE",profile);
        // The referenced copy and LoadFrom have separate CLR contexts. Use
        // the reference context, after proving it is the exact tested binary.
        var assembly = typeof(ISubmissionPlatform).Assembly;
        if(assembly.ManifestModule.ModuleVersionId!=Assembly.LoadFrom(args[0]).ManifestModule.ModuleVersionId)
            throw new Exception("Host test reference does not match the candidate binary");
        var type = assembly.GetType("PinyinVim.Native.Host",true);
        // Actual protocol and test sink, without a Form constructor or HWND.
        var host = FormatterServices.GetUninitializedObject(type);
        var fields = BindingFlags.Instance|BindingFlags.NonPublic;
        type.GetField("profile",fields).SetValue(host,profile);
        type.GetField("logLock",fields).SetValue(host,new object());
        type.GetField("sendLock",fields).SetValue(host,new object());
        type.GetField("json",fields).SetValue(host,new JavaScriptSerializer());
        type.GetField("settings",fields).SetValue(host,new Dictionary<string,object> {{"notifications",false}});
        type.GetField("token",fields).SetValue(host,"isolated-test");
        var sessionType = assembly.GetType("PinyinVim.Native.EditorSession",true);
        var session = Activator.CreateInstance(sessionType,true);
        sessionType.GetMethod("BeginForContract",fields).Invoke(session,new object[] {7L,false});
        type.GetField("activeSession",fields).SetValue(host,session);
        using(var transport = new MemoryStream()) using(var writer = new StreamWriter(transport) {AutoFlush=true}) {
            type.GetField("writer",fields).SetValue(host,writer);
            var message = new Dictionary<string,object> {{"kind","submit-request"},{"session",7},{"requestId","first"},{"text","fixture"}};
            var receive = type.GetMethod("OnMessage",fields);
            for(int i=0;i<2;i++) ((Task)receive.Invoke(host,new object[] {message})).GetAwaiter().GetResult();
            message["requestId"]="queued-second";
            ((Task)receive.Invoke(host,new object[] {message})).GetAwaiter().GetResult();
            int count=0;
            foreach(string line in File.ReadAllLines(Path.Combine(profile,"latency.jsonl"))) if(line.Contains("capsule-test-submit")) count++;
            if(count!=1) throw new Exception("A completed session accepted "+count+" submissions");
            string replies=System.Text.Encoding.UTF8.GetString(transport.ToArray());
            if(!replies.Contains("already-submitted") || !replies.Contains("copied")) throw new Exception("Missing distinct acknowledgement outcomes");
        }
        Console.WriteLine("actual host: duplicate request and queued second submit rejected; isolated sink accepted once");
        CheckImmediateSubmission(type,host,session,sessionType,fields,true);
        CheckImmediateSubmission(type,host,session,sessionType,fields,false);
        CheckImmediateSubmission(type,host,session,sessionType,fields,true,"Ctrl+Alt+F9");
        CheckPasteKeys();
    }

    static void CheckImmediateSubmission(Type type, object host, object session, Type sessionType, BindingFlags fields, bool copySucceeds, string hotkey = "Ctrl+Enter")
    {
        Console.WriteLine("checking actual asynchronous submit: copy="+copySucceeds);
        var platform=new FixturePlatform();
        platform.ExpectedTrigger=hotkey=="Ctrl+Enter"?13:120;
        ((Dictionary<string,object>)type.GetField("settings",fields).GetValue(host))["submitHotkey"]=hotkey;
        type.GetField("submissionPlatform",fields).SetValue(host,platform);
        type.GetField("visible",fields).SetValue(host,true);
        sessionType.GetMethod("BeginForContract",fields).Invoke(session,new object[]{8L,true});
        sessionType.GetProperty("Visible").SetValue(session,true,null);
        var target=(InputTarget)Activator.CreateInstance(typeof(InputTarget),BindingFlags.Instance|BindingFlags.NonPublic,null,
            new object[]{new IntPtr(99),new IntPtr(99),1,true,"fixture",null,null,1L},null);
        type.GetField("target",fields).SetValue(host,target);
        var submit=(Task<bool>)type.GetMethod("Submit",fields).Invoke(host,new object[]{"accepted 中文"});
        if ((bool)type.GetField("visible",fields).GetValue(host) || (bool)sessionType.GetProperty("Visible").GetValue(session,null))
            throw new Exception("Editor stayed visible while clipboard/target work was pending");
        if (submit.IsCompleted || platform.Deliveries!=0 || platform.CopyText!="accepted 中文")
            throw new Exception("Submission did not capture once and hide before asynchronous delivery");
        platform.Copy.SetResult(copySucceeds);
        if (copySucceeds)
        {
            if (!platform.Delivered.Task.Wait(3000)) throw new Exception("Background delivery did not start");
            if (submit.IsCompleted || platform.Input.Completed || platform.Deliveries!=1 || !platform.DeliveredWhileHeld)
                throw new Exception("Host waited for physical key-up or repeated the accepted request");
            platform.Result.SetResult(Result(true,"success"));
        }
        if (!submit.Wait(3000) || submit.Result!=copySucceeds || !platform.Input.Completed)
            throw new Exception("Submission cleanup/copy failure contract failed");
        if (!copySucceeds && platform.Deliveries!=0) throw new Exception("Failed copy attempted a paste");
        Console.WriteLine("actual host: hides before pending copy/target, completes while Ctrl+Enter remains held; copy="+copySucceeds);
    }

    static PasteResult Result(bool success,string reason)
    {
        return (PasteResult)Activator.CreateInstance(typeof(PasteResult),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{success,reason},null);
    }

    static void CheckPasteKeys()
    {
        var method=typeof(NativeWindows).GetMethod("PasteInputs",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(var held in new[]{new int[0],new[]{0xA2},new[]{0xA3},new[]{0xA2,0xA0,0xA5},new[]{0xA2,0xA3,0xA0,0xA1,0xA4,0xA5}})
        {
            var input=new FixtureInput{Modifiers=held};
            var keys=(Array)method.Invoke(null,new object[]{input});
            int pasteDown=0,pasteUp=0;
            var logical=new HashSet<int>(held);
            foreach(object key in keys)
            {
                var union=key.GetType().GetField("union").GetValue(key);
                var keyboard=union.GetType().GetField("keyboard").GetValue(union);
                int code=Convert.ToInt32(keyboard.GetType().GetField("wVk").GetValue(keyboard));
                uint flags=Convert.ToUInt32(keyboard.GetType().GetField("dwFlags").GetValue(keyboard));
                if(code==13) throw new Exception("Paste batch generated Enter");
                if(code==0x56) {
                    if((flags&2)==0) {
                        pasteDown++;
                        if(!(logical.Contains(0xA2)||logical.Contains(0xA3)) || logical.Contains(0xA0)||logical.Contains(0xA1)||logical.Contains(0xA4)||logical.Contains(0xA5))
                            throw new Exception("Paste was sent with incorrect modifiers");
                    } else pasteUp++;
                }
                int actual=code==0x11?0xA2:code;
                if((flags&2)==0)logical.Add(actual);else logical.Remove(actual);
            }
            if(pasteDown!=1 || pasteUp!=1) throw new Exception("Expected exactly one Ctrl+V chord");
            foreach(int modifier in new[]{0xA0,0xA1,0xA2,0xA3,0xA4,0xA5})
                if(logical.Contains(modifier)!=(Array.IndexOf(held,modifier)>=0)) throw new Exception("Held modifier state changed after paste");
        }
        Console.WriteLine("actual paste batches: left/right Ctrl and Shift/Alt chords contain one paste and no Enter");
    }

    sealed class FixtureInput : ISubmissionInput
    {
        public int[] Modifiers=new[]{0xA2};
        public int Trigger=13;
        public bool Completed,PhysicallyHeld=true;
        public bool Ready {get{return true;}}
        public bool OwnsTrigger(int key){return PhysicallyHeld && key==Trigger;}
        public bool OwnsModifier(int key){return PhysicallyHeld && Array.IndexOf(Modifiers,key)>=0;}
        public bool AllowsModifier(int key){return OwnsModifier(key);}
        public int[] RestoreModifiers(){return PhysicallyHeld?Modifiers:new int[0];}
        public void Complete(){Completed=true;}
        public void Dispose(){}
    }
    sealed class FixturePlatform : ISubmissionPlatform
    {
        public readonly FixtureInput Input=new FixtureInput();
        public readonly TaskCompletionSource<bool> Copy=new TaskCompletionSource<bool>();
        public readonly TaskCompletionSource<bool> Delivered=new TaskCompletionSource<bool>();
        public readonly TaskCompletionSource<PasteResult> Result=new TaskCompletionSource<PasteResult>();
        public int Deliveries;
        public string CopyText;
        public bool DeliveredWhileHeld;
        public int ExpectedTrigger=13;
        public ISubmissionInput CaptureKeys(int trigger){if(trigger!=ExpectedTrigger)throw new Exception("Submit key mismatch");Input.Trigger=trigger;return Input;}
        public Task<bool> CopyAsync(string text){CopyText=text;return Copy.Task;}
        public Task<PasteResult> DeliverAsync(InputTarget target,IntPtr editor,bool paste,ISubmissionInput input)
        {
            Deliveries++;DeliveredWhileHeld=Input.PhysicallyHeld;
            if(!paste || input!=Input)throw new Exception("Captured input/route mismatch");
            Delivered.SetResult(true);return Result.Task;
        }
    }
}
