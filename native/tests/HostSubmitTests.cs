using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
class HostSubmitTests {
    [STAThread] static void Main(string[] args) {
        string profile = Path.Combine(Path.GetTempPath(), "PinyinVim-submit-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        Environment.SetEnvironmentVariable("PINYINVIM_CAPSULE_TEST_PROFILE",profile);
        var assembly = Assembly.LoadFrom(args[0]);
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
    }
}
