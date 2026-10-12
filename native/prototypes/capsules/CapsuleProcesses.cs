using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PinyinVim.Native
{
    internal sealed partial class Host
    {
        readonly Dictionary<int, long> capsuleProcesses = new Dictionary<int, long>();
        DateTime capsuleProcessScan = DateTime.MinValue;

        bool CapsuleOwnsProcess(Process process)
        {
            long started;
            return capsuleProcesses.TryGetValue(process.Id, out started) && process.StartTime.ToUniversalTime().Ticks == started;
        }

        void CaptureCapsuleProcesses()
        {
            if (editor == null || editor.HasExited || (DateTime.UtcNow - capsuleProcessScan).TotalMilliseconds < 500) return;
            capsuleProcessScan = DateTime.UtcNow;
            var ids = CapsuleProcessTree.Descendants(editor.Id);
            bool changed = false;
            foreach (int id in ids)
            {
                if (capsuleProcesses.ContainsKey(id)) continue;
                try { using (Process process = Process.GetProcessById(id)) {
                    capsuleProcesses[id] = process.StartTime.ToUniversalTime().Ticks; changed = true;
                }} catch (ArgumentException) { } catch (System.ComponentModel.Win32Exception) { }
            }
            if (changed) try {
                var record = new Dictionary<string, object>();
                foreach (var entry in capsuleProcesses) record[entry.Key.ToString()] = entry.Value.ToString();
                File.WriteAllText(Path.Combine(profile, "owned-processes.json"), json.Serialize(record), new UTF8Encoding(false));
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        void StopCapsuleOwnedProcesses()
        {
            foreach (var entry in capsuleProcesses)
                try { using (Process process = Process.GetProcessById(entry.Key)) {
                    if (!CapsuleOwnsProcess(process)) continue;
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(1000); }
                }} catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }

        void RecoverCapsuleProcesses()
        {
            // A crashed host leaves an ownership record. PID + creation time
            // and the exact runtime path must all match before recovery kills
            // a process. Shared runtime paths alone are never ownership proof.
            try {
                var previous = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(profile, "owned-processes.json")));
                foreach (var entry in previous)
                {
                    int id; long started;
                    if (!Int32.TryParse(entry.Key, out id) || !Int64.TryParse(Convert.ToString(entry.Value), out started)) continue;
                    try { using (Process process = Process.GetProcessById(id)) {
                        if (process.StartTime.ToUniversalTime().Ticks != started) continue;
                        string path = process.MainModule.FileName;
                        string runtime = Path.GetFullPath(Path.Combine(root, "runtime")) + Path.DirectorySeparatorChar;
                        if (!path.StartsWith(runtime, StringComparison.OrdinalIgnoreCase)) continue;
                        process.Kill(); process.WaitForExit(1000);
                    }} catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                }
            } catch (Exception) { }
        }
    }

    internal static class CapsuleProcessTree
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Entry {
            internal uint Size, Usage, ProcessId;
            internal UIntPtr Heap;
            internal uint ModuleId, Threads, ParentId;
            internal int Priority;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Executable;
        }
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot, ref Entry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot, ref Entry entry);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        internal static HashSet<int> Descendants(int root)
        {
            var result = new HashSet<int> { root };
            var parents = new Dictionary<int, int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) return result;
            try {
                var entry = new Entry { Size = (uint)Marshal.SizeOf(typeof(Entry)) };
                if (Process32First(snapshot, ref entry)) do {
                    parents[(int)entry.ProcessId] = (int)entry.ParentId;
                } while (Process32Next(snapshot, ref entry));
                bool changed;
                do { changed = false; foreach (var entryPair in parents)
                    if (result.Contains(entryPair.Value) && result.Add(entryPair.Key)) changed = true;
                } while (changed);
            } finally { CloseHandle(snapshot); }
            return result;
        }
    }
}
