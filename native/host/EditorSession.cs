using System;
using System.Collections.Generic;

namespace PinyinVim.Native
{
    internal enum SubmitRoute
    {
        Clipboard,
        Paste
    }

    /// <summary>
    /// The host-side contract for one editor invocation.  The previous host
    /// kept these values in unrelated fields, so a late bridge message could
    /// act on a newer window.  Keeping the identity and route together makes
    /// stale requests rejectable at one boundary.
    /// </summary>
    internal sealed class EditorSession
    {
        public long Id { get; private set; }
        public InputTarget Target { get; private set; }
        public SubmitRoute Route { get; private set; }
        public string Mode { get; private set; }
        public bool Visible { get; set; }
        readonly HashSet<string> submitRequests = new HashSet<string>(StringComparer.Ordinal);
        bool submitInFlight, submitted;

        public bool HasTarget { get { return Target != null && Target.Window != IntPtr.Zero; } }
        public bool TargetValidated { get { return Target != null && Target.Editable; } }
        public string RouteValue { get { return Route == SubmitRoute.Paste ? "paste" : "clipboard"; } }
        public string RouteLabel { get { return Route == SubmitRoute.Paste ? "粘贴提交" : "剪贴板提交"; } }

        public void Begin(long id, InputTarget target)
        {
            Id = id;
            Target = target;
            Route = target != null && target.Editable ? SubmitRoute.Paste : SubmitRoute.Clipboard;
            Mode = "NORMAL";
            Visible = false;
            ResetSubmissions();
        }

        internal void BeginForContract(long id, bool targetValidated)
        {
            Id = id;
            Target = null;
            Route = targetValidated ? SubmitRoute.Paste : SubmitRoute.Clipboard;
            Mode = "NORMAL";
            Visible = false;
            ResetSubmissions();
        }

        public bool IsCurrent(object value)
        {
            long parsed;
            return value != null && long.TryParse(Convert.ToString(value), out parsed) && parsed == Id && Id != 0;
        }

        public void SetMode(string mode)
        {
            if (!String.IsNullOrEmpty(mode)) Mode = mode;
        }

        public bool TryBeginSubmit(string requestId, out string reason)
        {
            reason = "";
            if (String.IsNullOrEmpty(requestId) || requestId.Length > 128) { reason = "invalid-request"; return false; }
            if (submitted) { reason = "already-submitted"; return false; }
            if (submitInFlight) { reason = "submit-busy"; return false; }
            if (!submitRequests.Add(requestId)) { reason = "duplicate-request"; return false; }
            submitInFlight = true;
            return true;
        }

        public void FinishSubmit(bool delivered)
        {
            submitInFlight = false;
            submitted = submitted || delivered;
        }

        void ResetSubmissions()
        {
            submitRequests.Clear(); submitInFlight = false; submitted = false;
        }

        public void Reset()
        {
            Id = 0;
            Target = null;
            Route = SubmitRoute.Clipboard;
            Mode = "NORMAL";
            Visible = false;
            ResetSubmissions();
        }
    }
}
