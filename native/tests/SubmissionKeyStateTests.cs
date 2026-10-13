using System;
using System.Collections.Generic;
using PinyinVim.Native;

class SubmissionKeyStateTests
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static void Main()
    {
        foreach (int ctrl in new[] {0xA2,0xA3})
        foreach (int duration in new[] {0,100,900,5000,30000})
        {
            var down = new HashSet<int> {13,ctrl};
            var state = new SubmissionKeyState();
            state.Arm(13, down.Contains);
            // Input admission must be independent of physical hold duration.
            Check(SubmissionKeyState.AcceptsInput(down.Contains,state.OwnsTrigger,state.OwnsModifier),"accept held chord: "+duration);
            Check(state.PhysicalEvent(13,false),"swallow trigger autorepeat");
            int repeats=0;
            for(int elapsed=500;elapsed<duration;elapsed+=33)
            {
                if(!state.PhysicalEvent(13,false) || !SubmissionKeyState.AcceptsInput(down.Contains,state.OwnsTrigger,state.OwnsModifier))
                    throw new Exception("Long hold leaked a repeat or prevented delivery at "+elapsed);
                repeats++;
            }
            Check(duration<500 || repeats>0,"simulated hold traversed the auto-repeat interval");
            Check(!state.PhysicalEvent('A',false),"unrelated key passes");
            state.PhysicalEvent(ctrl,true); down.Remove(ctrl);
            Check(!state.OwnsModifier(ctrl),"do not restore a physically released modifier");
            Check(SubmissionKeyState.AcceptsInput(down.Contains,state.OwnsTrigger,state.OwnsModifier),"Ctrl releases before Enter");
            Check(state.PhysicalEvent(13,true),"consume final trigger release"); down.Remove(13);
            Check(!state.HasHeldTrigger && !state.PhysicalEvent(13,false),"next physical Enter belongs to the target");
        }
        {
            var down = new HashSet<int> {13,0xA2};
            var state = new SubmissionKeyState(); state.Arm(13,down.Contains);
            state.PhysicalEvent(13,true); down.Remove(13);
            Check(SubmissionKeyState.AcceptsInput(down.Contains,state.OwnsTrigger,state.OwnsModifier),"Enter releases before Ctrl");
            Check(state.OwnsModifier(0xA2),"preserve still-held left Ctrl");
            state.PhysicalEvent(0xA2,true); down.Remove(0xA2);
            state.PhysicalEvent(0xA2,false); down.Add(0xA2);
            Check(!state.OwnsModifier(0xA2),"later Ctrl press is not the original press");
            Check(!SubmissionKeyState.AcceptsInput(down.Contains,state.OwnsTrigger,state.OwnsModifier),"cancel when new input replaces the original chord");
        }
        {
            var asyncDown=new HashSet<int>{13,0xA2};
            var state=new SubmissionKeyState();state.Arm(13,asyncDown.Contains);
            state.PhysicalEvent(0xA2,true);
            Check(!state.OwnsModifier(0xA2),"release already reached the hook; no modifier restoration");
            Check(SubmissionKeyState.AcceptsInput(asyncDown.Contains,state.OwnsTrigger,state.AllowsModifier),"accept release before Windows updates asynchronous key state");
            state.PhysicalEvent(0xA2,false);
            Check(!SubmissionKeyState.AcceptsInput(asyncDown.Contains,state.OwnsTrigger,state.AllowsModifier),"a subsequent physical press is not the release race");
        }
        foreach (int modifier in SubmissionKeyState.Modifiers)
        {
            var down = new HashSet<int> {120,modifier}; // Custom F9 submission.
            var state = new SubmissionKeyState(); state.Arm(120,down.Contains);
            Check(state.PhysicalEvent(120,false),"custom submission key repeat filtered");
            Check(state.OwnsModifier(modifier),"left/right modifiers captured");
            Check(Array.IndexOf(state.RestoreModifiers(),modifier)>=0,"held modifier restored");
            state.PhysicalEvent(modifier,true);
            Check(Array.IndexOf(state.RestoreModifiers(),modifier)<0,"release race removes stale restoration");
        }
        {
            var down = new HashSet<int>(); var state = new SubmissionKeyState(); state.Arm(13,down.Contains);
            Check(SubmissionKeyState.AcceptsInput(down.Contains,state.OwnsTrigger,state.OwnsModifier),"button/quick tap needs no held-key handling");
            down.Add(0xA0);
            Check(!SubmissionKeyState.AcceptsInput(down.Contains,state.OwnsTrigger,state.OwnsModifier),"new Shift press is protected");
            down.Clear(); down.Add(1);
            Check(!SubmissionKeyState.AcceptsInput(down.Contains,state.OwnsTrigger,state.OwnsModifier),"unrelated mouse drag is protected");
        }
        Console.WriteLine("submission key state: "+checks+" checks passed; physical input is simulated");
    }
}
