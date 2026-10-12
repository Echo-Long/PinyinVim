using System;

namespace PinyinVim.Native
{
    internal static class FocusRestorePolicy
    {
        // A provider may report a container as FocusedElement. Only fall back
        // to a scoped search when permitted, and validate its result again.
        internal static T Discover<T>(T direct, Func<bool> maySearch, Func<T> search, Func<T, bool> valid) where T : class
        {
            if (valid(direct) || !maySearch()) return direct;
            T candidate = search();
            return valid(candidate) ? candidate : direct;
        }

        // First let the application restore its own logical focus. If needed,
        // ask the captured control once, then observe readiness without retrying
        // either focus writes or text input. Every provider call is followed by
        // another cancellation/deadline check.
        internal static string Wait(Func<string> guard, Func<bool> ready, Action restore,
            Func<long> elapsed, Action<int> pause, int naturalFocusMs, int timeoutMs)
        {
            long started = elapsed();
            bool requested = false;
            while (true)
            {
                string failure = guard();
                if (failure != null) return failure;
                if (elapsed() - started >= timeoutMs) return "uia-focus-mismatch";
                bool focused = ready();
                failure = guard();
                if (failure != null) return failure;
                if (elapsed() - started >= timeoutMs) return "uia-focus-mismatch";
                if (focused) return null;
                if (!requested && elapsed() - started >= naturalFocusMs)
                {
                    restore();
                    requested = true;
                    failure = guard();
                    if (failure != null) return failure;
                }
                pause(20);
            }
        }
    }
}
