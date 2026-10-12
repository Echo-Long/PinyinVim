using System;
using System.Threading;
using System.Threading.Tasks;

namespace PinyinVim.Native
{
    // One MTA worker, no growing queue. A hung accessibility provider cannot
    // block the UI, create more threads, or execute a timed-out request later.
    public sealed class BoundedWorker
    {
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly object gate = new object();
        Action pending;
        bool busy;
        public BoundedWorker()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "PinyinVim on-demand accessibility" };
            thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        }
        public Task<T> Submit<T>(Func<T> action, T fallback, int timeoutMs)
        {
            var completion = new TaskCompletionSource<T>();
            lock (gate)
            {
                if (busy) { completion.SetResult(fallback); return completion.Task; }
                busy = true;
                var timer = new Timer(s => completion.TrySetResult(fallback), null, timeoutMs, Timeout.Infinite);
                pending = () =>
                {
                    try { if (!completion.Task.IsCompleted) completion.TrySetResult(action()); }
                    catch { completion.TrySetResult(fallback); }
                    finally { timer.Dispose(); lock (gate) busy = false; }
                };
            }
            signal.Set(); return completion.Task;
        }
        void Run()
        {
            while (true)
            {
                signal.WaitOne(); Action action;
                lock (gate) { action = pending; pending = null; }
                if (action != null) action();
            }
        }
    }
}
