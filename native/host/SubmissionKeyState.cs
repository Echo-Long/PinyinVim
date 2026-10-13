using System;
using System.Collections.Generic;

namespace PinyinVim.Native
{
    // Physical state of the keys belonging to one submission. Injected key-up
    // events must not be mistaken for the user's physical release.
    internal sealed class SubmissionKeyState
    {
        internal static readonly int[] Modifiers = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };
        readonly HashSet<int> heldTriggers = new HashSet<int>();
        readonly HashSet<int> ownedModifiers = new HashSet<int>();
        readonly HashSet<int> physicalModifiers = new HashSet<int>();
        readonly object gate = new object();

        internal void Arm(int trigger, Func<int, bool> pressed)
        {
            lock (gate)
            {
                if (pressed(trigger)) heldTriggers.Add(trigger);
                foreach (int key in Modifiers)
                    if (pressed(key)) { physicalModifiers.Add(key); ownedModifiers.Add(key); }
            }
        }

        internal bool PhysicalEvent(int key, bool up)
        {
            lock (gate)
            {
                if (Array.IndexOf(Modifiers, key) >= 0)
                {
                    if (up) physicalModifiers.Remove(key);
                    else
                    {
                        if (!physicalModifiers.Contains(key)) ownedModifiers.Remove(key);
                        physicalModifiers.Add(key);
                    }
                }
                if (!heldTriggers.Contains(key)) return false;
                if (up) heldTriggers.Remove(key);
                return true; // Only the original trigger's repeats and final release.
            }
        }

        internal bool OwnsModifier(int key)
        {
            lock (gate) return ownedModifiers.Contains(key) && physicalModifiers.Contains(key);
        }

        internal bool OwnsTrigger(int key) { lock (gate) return heldTriggers.Contains(key); }
        internal bool AllowsModifier(int key) { lock (gate) return ownedModifiers.Contains(key); }

        internal static bool AcceptsInput(Func<int, bool> pressed, Func<int, bool> ownsTrigger, Func<int, bool> ownsModifier)
        {
            if (pressed(1) || pressed(2) || (pressed(13) && !ownsTrigger(13))) return false;
            foreach (int key in Modifiers) if (pressed(key) && !ownsModifier(key)) return false;
            return true;
        }

        internal bool HasHeldTrigger { get { lock (gate) return heldTriggers.Count > 0; } }

        internal int[] HeldTriggers()
        {
            lock (gate) { var keys = new int[heldTriggers.Count]; heldTriggers.CopyTo(keys); return keys; }
        }

        internal int[] RestoreModifiers()
        {
            lock (gate)
            {
                var keys = new List<int>();
                foreach (int key in Modifiers) if (ownedModifiers.Contains(key) && physicalModifiers.Contains(key)) keys.Add(key);
                return keys.ToArray();
            }
        }
    }
}
