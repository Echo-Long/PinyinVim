using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PinyinVim.Native;
class WorkerTests
{
    static int checks;
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
    static void Main()
    {
        var worker=new BoundedWorker();
        Check(worker.Submit(()=>42,-1,500).Result==42,"normal result");
        Thread.Sleep(10);
        Check(worker.Submit<int>(()=>{throw new Exception("provider failure");},-1,500).Result==-1,"exception fallback");
        Thread.Sleep(10);
        var entered=new ManualResetEvent(false);
        var release=new ManualResetEvent(false);
        var watch=Stopwatch.StartNew();
        var stalled=worker.Submit(()=>{entered.Set();release.WaitOne();return 10;},-1,70);
        Check(entered.WaitOne(500),"worker started");
        Check(stalled.Result==-1 && watch.ElapsedMilliseconds<500,"timeout returns while provider stalled");
        int forbidden=0;
        Check(worker.Submit(()=>{Interlocked.Increment(ref forbidden);return 20;},-2,50).Result==-2,"busy requests do not queue");
        release.Set();Thread.Sleep(30);
        Check(forbidden==0,"timed-out requests do not execute later");
        Check(worker.Submit(()=>30,-1,500).Result==30,"worker recovers when provider returns");
        Console.WriteLine("worker tests: "+checks+" passed; no windows or input injection used");
    }
}
