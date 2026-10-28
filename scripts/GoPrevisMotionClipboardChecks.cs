using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task<int> RunMotionClipboardChecks(Action<bool,string> report,string? folder=null)
    {
        var original=Snapshot();string path=_projectPath,mode=_animationWorkspaceMode;double time=_time;bool playing=_playing;
        var undo=_undo.ToArray();var redo=_redo.ToArray();var clipboard=_motionClipboard;string? selectedObject=_selected?.Data.Id,selectedCamera=_selectedCamera?.Id;int count=0;
        void Check(bool value,string message){report(value,"Motion clipboard: "+message);count++;}
        async Task Settle(){for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Click(Vector2 at,bool right=false)
        {_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=right?MouseButton.Right:MouseButton.Left,Pressed=true,Position=at});if(!right)_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=at});}
        void Shortcut(Key key){GetViewport().GuiReleaseFocus();_timeline.GrabFocus();_UnhandledInput(new InputEventKey{Keycode=key,Pressed=true,CtrlPressed=true});}
        try
        {
            StopHistoryPlayback();StopPoseEditing();ClearObjects();SetAnimationWorkspaceMode("Clips");_duration=8;_scrub.MaxValue=8;SetTime(0);_motionClipboard=null;
            var human=AddObject("actor",Vector3.Zero,"Clipboard runner");string humanId=human.Data.Id;
            var motion=MotionCatalog.Find("human-walk-gesture")!;AppendMotion(human,MotionCatalog.MaterializeSource(motion),motion.SourceScale);
            var source=human.Data.MotionClips[0];source.Start=1;source.Duration=1.25;source.SourceOffset=.3;source.Speed=1.2;source.BlendIn=.2;source.Loop=true;source.InPlace=true;
            source.PropertyAnimation.Channels["Speed"]=new();PropertyCurveRuntime.Upsert(source.PropertyAnimation.Channels["Speed"],1,1);PropertyCurveRuntime.Upsert(source.PropertyAnimation.Channels["Speed"],2.25,2);
            RebuildTrack(human);var target=AddObject("actor",new Vector3(2,0,0),"Clipboard target");string targetId=target.Data.Id;
            human.Data.Path=new(){Enabled=true,Duration=8,Points=new(){new float[]{0,0,0},new float[]{2,0,0}}};human.Data.PoseCorrections.Root.Add(new(){Time=0,Value=new float[]{0,.05f,0}});
            var fixture=CloneAnimationState(Snapshot());string sourceId=source.Id;Select(human);SetTime(1.4);await Settle();
            Click(_timeline.MotionClipRect(humanId,sourceId).GetCenter(),true);
            var labels=_timeline.MotionClipboardMenuLabels();Check(labels.Contains("Duplicate")&&labels.Contains("Copy")&&labels.Contains("Paste")&&labels.Contains("Delete motion clip"),"right-clicking a real clip exposes Duplicate, Copy, Paste and Delete");
            if(folder!=null&&DisplayServer.GetName()!="headless")
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"motion-context-menu.png"));}
            int history=_undo.Count;_timeline.InvokeMotionClipboardMenu(2);Check(_motionClipboard!=null&&_undo.Count==history,"Copy snapshots the selected motion without changing the document history");
            _timeline.InvokeMotionClipboardMenu(1);human=_entries.Single(e=>e.Data.Id==humanId);var duplicate=human.Data.MotionClips.Single(c=>c.Id!=sourceId);
            Check(Math.Abs(duplicate.Start-2.25)<1e-9&&duplicate.Duration==source.Duration&&duplicate.SourceOffset==source.SourceOffset&&duplicate.Speed==source.Speed&&duplicate.Loop==source.Loop&&duplicate.BlendIn==source.BlendIn&&duplicate.InPlace==source.InPlace,"Duplicate starts exactly after the chosen clip and retains its source and trim settings");
            Check(duplicate.Id!=sourceId&&!ReferenceEquals(source.PropertyAnimation,duplicate.PropertyAnimation)&&!ReferenceEquals(source.PropertyAnimation.Channels["Speed"],duplicate.PropertyAnimation.Channels["Speed"]),"duplicate clip and animation curves have independent identities and storage");
            Check(duplicate.PropertyAnimation.Channels["Speed"].Keys.All(k=>k.Time>=2.25)&&Math.Abs(duplicate.PropertyAnimation.Channels["Speed"].Keys[1].Time-3.5)<1e-9,"motion parameter keys move with the duplicated clip");
            Check(_undo.Count==history+1&&human.Data.Path.Enabled&&human.Data.PoseCorrections.Root.Count==1,"Duplicate is one Undo step and preserves the route and body corrections");
            SetTime(1.4);var firstPose=((RiggedActor)human.Node).CaptureEvaluatedPose();SetTime(duplicate.Start+.4);var secondPose=((RiggedActor)human.Node).CaptureEvaluatedPose();
            Check(firstPose.BoneRotations.Keys.All(b=>Math.Abs(PoseCorrectionRuntime.ReadRotation(firstPose.BoneRotations[b]).Dot(PoseCorrectionRuntime.ReadRotation(secondPose.BoneRotations[b])))>.9999),"the duplicated BVH clip evaluates the same skeletal phase with its animated rate");
            Undo();Check(_entries.Single(e=>e.Data.Id==humanId).Data.MotionClips.Count==1,"Undo removes only the duplicate");Redo();Check(_entries.Single(e=>e.Data.Id==humanId).Data.MotionClips.Count==2,"Redo restores the duplicate");

            Restore(CloneAnimationState(fixture),"");human=_entries.Single(e=>e.Data.Id==humanId);target=_entries.Single(e=>e.Data.Id==targetId);source=human.Data.MotionClips[0];SetTime(1.4);await Settle();
            Click(_timeline.MotionClipRect(humanId,sourceId).GetCenter());history=_undo.Count;Shortcut(Key.C);Check(_motionClipboard?.Clip.Id==sourceId&&_undo.Count==history,"Ctrl+C copies the selected clip through the actual app shortcut route");
            _timeline.DeleteSelectedItems();Check(human.Data.MotionClips.Count==0&&_motionClipboard!=null,"clipboard contents survive deletion of the source clip");
            Select(target);SetTime(4);_timeline.GrabFocus();history=_undo.Count;Shortcut(Key.V);var pasted=target.Data.MotionClips.Single();
            Check(pasted.Start==4&&pasted.SourceOffset==.3&&pasted.Duration==1.25&&_undo.Count==history+1,"Ctrl+V pastes onto an empty compatible human at the playhead in one Undo step");
            Click(_timeline.MotionClipRect(targetId,pasted.Id).GetCenter());Shortcut(Key.D);Check(target.Data.MotionClips.Count==2&&target.Data.MotionClips.Any(c=>Math.Abs(c.Start-5.25)<1e-9),"Ctrl+D duplicates the selected motion instead of duplicating its character");
            var prop=AddObject("car",new Vector3(3,0,0),"Incompatible clipboard prop");int entries=_entries.Count;history=_undo.Count;
            Check(!PasteMotionClip(prop,0)&&prop.Data.MotionClips.Count==0&&_undo.Count==history&&_entries.Count==entries,"incompatible paste leaves the scene and history untouched");
            var fox=AddFox(new Vector3(0,0,2),true);fox.Data.MotionClips[0].Duration=1;RebuildTrack(fox);var secondFox=AddFox(new Vector3(2,0,2),false);CopyMotionClip(fox,fox.Data.MotionClips[0]);
            history=_undo.Count;Check(PasteMotionClip(secondFox,2)&&secondFox.NativeTrack!.ClipCount==1&&_undo.Count==history+1,"native animal clips paste onto the matching empty rig and rebuild real playback");
            history=_undo.Count;Check(!PasteMotionClip(target,2)&&_undo.Count==history,"native animal motion cannot be pasted onto a human skeleton");

            human=_entries.Single(e=>e.Data.Id==humanId);
            var trim=CopyMotionClipData(source);trim.Start=2;trim.Duration=2;trim.SourceOffset=1;trim.Speed=1;trim.BlendIn=0;trim.Loop=false;trim.PropertyAnimation=new();
            var trimRate=trim.PropertyAnimation.Channels["Speed"]=new();PropertyCurveRuntime.Upsert(trimRate,0,2);PropertyCurveRuntime.Upsert(trimRate,8,2);
            human.Data.MotionClips=new(){trim};RebuildTrack(human);SelectTimelineClip(human,trim);SetTime(2.5);await Settle();
            var retainedPose=((RiggedActor)human.Node).CaptureEvaluatedPose();double retainedClock=MotionClipClock.SourceTime(trim.PropertyAnimation,2.5,trim.Start,trim.Duration,trim.SourceOffset,trim.Speed);
            void DragIn(double to)
            {
                var rect=_timeline.MotionClipRect(humanId,trim.Id);var press=new Vector2(rect.Position.X+2,rect.GetCenter().Y);
                var release=press+new Vector2((float)((to-trim.Start)*rect.Size.X/trim.Duration),0);
                _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=press});
                _timeline._GuiInput(new InputEventMouseMotion{Position=release,Relative=release-press});
                _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=release});
            }
            history=_undo.Count;DragIn(1);SetTime(2.5);var trimmedPose=((RiggedActor)human.Node).CaptureEvaluatedPose();
            Check(Math.Abs(trim.Start-1.5)<1e-9&&Math.Abs(trim.SourceOffset)<1e-9,"dragging IN cannot extend past source zero when an animated playback rate is twice the static rate");
            Check(Math.Abs(MotionClipClock.SourceTime(trim.PropertyAnimation,2.5,trim.Start,trim.Duration,trim.SourceOffset,trim.Speed)-retainedClock)<1e-9&&retainedPose.BoneRotations.Keys.All(b=>Math.Abs(PoseCorrectionRuntime.ReadRotation(retainedPose.BoneRotations[b]).Dot(PoseCorrectionRuntime.ReadRotation(trimmedPose.BoneRotations[b])))>.9999),"integrated IN trimming preserves the retained source phase and actual evaluated skeletal pose");
            Check(_undo.Count==history+1,"a constrained IN-handle drag creates one Undo step");
            Undo();human=_entries.Single(e=>e.Data.Id==humanId);trim=human.Data.MotionClips.Single();
            Check(Math.Abs(trim.Start-2)<1e-9&&Math.Abs(trim.SourceOffset-1)<1e-9,"one Undo restores the original animated-rate IN boundary");
            trim.PropertyAnimation.Channels["Speed"].Enabled=false;RebuildTrack(human);SelectTimelineClip(human,trim);await Settle();
            history=_undo.Count;DragIn(0);Check(Math.Abs(trim.Start-1)<1e-9&&Math.Abs(trim.SourceOffset)<1e-9&&_undo.Count==history+1,"disabled Speed curves obey the static source clock during actual IN-handle dragging");

            trim.Start=2;trim.Duration=2;trim.SourceOffset=1;trim.PropertyAnimation.Channels["Speed"].Enabled=true;
            var remap=trim.PropertyAnimation.Channels["SourceOffset"]=new();var remapA=PropertyCurveRuntime.Upsert(remap,0,.2);var remapB=PropertyCurveRuntime.Upsert(remap,8,1.8);remapA.Interpolation="Bezier";remapA.OutSlope=.15;remapB.InSlope=.1;
            RebuildTrack(human);SelectTimelineClip(human,trim);await Settle();
            var beforeRemap=CopyMotionClipData(trim);history=_undo.Count;DragIn(2.5);
            double remapError=Enumerable.Range(0,13).Max(i=>Math.Abs(MotionClipClock.SourceTime(beforeRemap.PropertyAnimation,2.5+i/8.0,2,2,1,1)-MotionClipClock.SourceTime(trim.PropertyAnimation,2.5+i/8.0,trim.Start,trim.Duration,trim.SourceOffset,trim.Speed)));
            Check(remapError<1e-8&&_undo.Count==history+1,$"trimming IN later preserves a Bezier SourceOffset remap and animated playback-rate phase (error {remapError:R}, IN {trim.Start:R}, offset {trim.SourceOffset:R}, history {_undo.Count-history})");
            double remapStart=trim.Start;DragIn(1);Check(Math.Abs(trim.Start-remapStart)<1e-9,"an animated SourceOffset curve conservatively prevents earlier IN expansion instead of altering its remap");

            var beforeMove=CopyMotionClipData(trim);var bodyRect=_timeline.MotionClipRect(humanId,trim.Id);var bodyPress=bodyRect.GetCenter();var bodyRelease=bodyPress+new Vector2((float)(.5*bodyRect.Size.X/trim.Duration),0);history=_undo.Count;
            _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=bodyPress});
            _timeline._GuiInput(new InputEventMouseMotion{Position=bodyRelease,Relative=bodyRelease-bodyPress});
            _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=bodyRelease});
            Check(Math.Abs(trim.Start-beforeMove.Start-.5)<1e-8&&beforeMove.PropertyAnimation.Channels.All(p=>p.Value.Keys.All(k=>Math.Abs(trim.PropertyAnimation.Channels[p.Key].Keys.Single(other=>other.Id==k.Id).Time-k.Time-.5)<1e-8)),"dragging a clip body shifts animated playback-rate and SourceOffset keys by the same time delta");
            Check(Enumerable.Range(0,9).All(i=>Math.Abs(MotionClipClock.SourceTime(beforeMove.PropertyAnimation,beforeMove.Start+i/8.0,beforeMove.Start,beforeMove.Duration,beforeMove.SourceOffset,beforeMove.Speed)-MotionClipClock.SourceTime(trim.PropertyAnimation,trim.Start+i/8.0,trim.Start,trim.Duration,trim.SourceOffset,trim.Speed))<1e-8)&&_undo.Count==history+1,"body dragging preserves the remapped source phase in one Undo step");

            var timed=new MotionClipData{Start=3,Duration=2,SourceOffset=.4,Speed=1};var rate=timed.PropertyAnimation.Channels["Speed"]=new();
            var begin=PropertyCurveRuntime.Upsert(rate,0,1);var end=PropertyCurveRuntime.Upsert(rate,8,3);begin.Interpolation="Bezier";begin.OutSlope=.2;begin.OutWeight=.25;end.InSlope=.1;end.InWeight=.3;
            var earlier=CopyMotionClipData(timed);ShiftMotionPropertyKeys(earlier,-3);earlier.Start=0;
            Check(Enumerable.Range(0,17).All(i=>Math.Abs(MotionClipClock.SourceTime(timed.PropertyAnimation,3+i/8.0,3,2,.4,1)-MotionClipClock.SourceTime(earlier.PropertyAnimation,i/8.0,0,2,.4,1))<1e-7),"pasting earlier preserves a Bezier playback-rate curve whose pre-roll key would otherwise move below zero");
            begin.Interpolation="Stop";earlier=CopyMotionClipData(timed);ShiftMotionPropertyKeys(earlier,-3);
            Check(Enumerable.Range(0,17).All(i=>Math.Abs(MotionClipClock.SourceTime(timed.PropertyAnimation,3+i/8.0,3,2,.4,1)-MotionClipClock.SourceTime(earlier.PropertyAnimation,i/8.0,0,2,.4,1))<1e-7),"earlier paste also preserves stopped/eased source-clock timing across the zero boundary");
        }
        finally
        {
            _timeline.HideMotionClipboardMenu();Restore(original,"");_projectPath=path;SetTime(time);RestoreInteractionSelection(selectedObject,selectedCamera);_playing=playing;_motionClipboard=clipboard;SetAnimationWorkspaceMode(mode);
            _undo.Clear();_redo.Clear();foreach(var state in undo.Reverse())_undo.Push(state);foreach(var state in redo.Reverse())_redo.Push(state);
        }
        return count;
    }
}
