using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    public async Task<int> RunTimelineTrimChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;bool playing=_playing;
        var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;string workspace=_animationWorkspaceMode;
        void Check(bool condition,string text){Require(condition,"Timeline trim: "+text);checks++;}
        try
        {
            SetAnimationWorkspaceMode("Clips");_playing=false;ClearObjects();_duration=8;_scrub.MaxValue=8;SetTime(0);
            var entry=AddObject("actor",Vector3.Zero);string entryId=entry.Data.Id;
            string path=MotionCatalog.MaterializeSource(MotionCatalog.Find("human-walk-gesture")!);
            var previous=new MotionClipData{MotionPath=path,Name="Previous",Start=0,Duration=3.4,SourceScale=.01f,BlendIn=0,Loop=false};
            var clip=new MotionClipData{MotionPath=path,Name="Trim target",Start=3,Duration=3,SourceOffset=.5,SourceScale=.01f,Speed=.75,BlendIn=.3,Loop=false};
            var next=new MotionClipData{MotionPath=path,Name="Next",Start=5.8,Duration=1.5,SourceScale=.01f,BlendIn=.2,Loop=false};
            string clipId=clip.Id,nextId=next.Id;entry.Data.MotionClips=new(){previous,clip,next};RebuildTrack(entry);SelectTimelineClip(entry,clip);
            for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var fixture=Snapshot();
            void Reattach(){entry=_entries.Single(e=>e.Data.Id==entryId);clip=entry.Data.MotionClips.Single(c=>c.Id==clipId);next=entry.Data.MotionClips.Single(c=>c.Id==nextId);}
            void Reset(){Restore(fixture,"");Reattach();SelectTimelineClip(entry,clip);}
            void Drag(bool left,params double[] changes)
            {
                var rect=_timeline.MotionClipRect(entryId,clipId);double pixelsPerSecond=rect.Size.X/clip.Duration;
                var point=new Vector2(left?rect.Position.X+.5f:rect.End.X-.5f,rect.Position.Y+8);
                _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
                foreach(double change in changes)_timeline._GuiInput(new InputEventMouseMotion{Position=point+Vector2.Right*(float)(pixelsPerSecond*change)});
                _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point});
            }
            int count=_undo.Count;double outPoint=clip.Start+clip.Duration,sourceOut=clip.SourceOffset+clip.Duration*clip.Speed;
            Drag(true,.5);
            Check(Math.Abs(clip.Start-3.5)<.00001&&Math.Abs(clip.Duration-2.5)<.00001,"left edge changes timeline IN and duration");
            Check(Math.Abs(clip.SourceOffset-.875)<.00001,"source IN advances by the trimmed timeline interval times playback speed");
            Check(Math.Abs(clip.Start+clip.Duration-outPoint)<.00001&&Math.Abs(clip.SourceOffset+clip.Duration*clip.Speed-sourceOut)<.00001,"both timeline and source OUT remain fixed while trimming IN");
            Check(!clip.Loop&&clip.BlendIn==0&&Math.Abs(next.BlendIn-.2)<.00001,"loop mode and valid following fade remain intact while a removed overlap disappears");
            Check(_undo.Count==count+1,"one IN-edge drag creates one undo record");
            SetTime(clip.Start+.25);var animated=(RiggedActor)entry.Node;
            var isolated=new RiggedActor();_world.AddChild(isolated);isolated.SetMotionClips(new[]{new MotionClipData{MotionPath=path,Name="Offset comparison",SourceOffset=clip.SourceOffset,Duration=clip.Duration,SourceScale=clip.SourceScale,Speed=clip.Speed,Loop=false,BlendIn=0}});isolated.EvaluateTimeline(.25);
            var a=animated.GetNode<Skeleton3D>("PreparedHumanoid");var b=isolated.GetNode<Skeleton3D>("PreparedHumanoid");
            Check(Enumerable.Range(0,a.GetBoneCount()).All(i=>Math.Abs(a.GetBonePoseRotation(i).Dot(b.GetBonePoseRotation(i)))>.99999f),"the retargeted skeleton actually plays the new source IN");isolated.QueueFree();
            Undo();Reattach();Check(Math.Abs(clip.Start-3)<.00001&&Math.Abs(clip.SourceOffset-.5)<.00001&&Math.Abs(clip.Duration-3)<.00001,"undo restores source IN, start and duration together");
            Redo();Reattach();Check(Math.Abs(clip.Start-3.5)<.00001&&Math.Abs(clip.SourceOffset-.875)<.00001,"redo restores the source trim");
            Drag(true,-20);Check(clip.SourceOffset>=0&&clip.SourceOffset<.00001&&clip.Start>=0&&Math.Abs(clip.Start+clip.Duration-outPoint)<.00001,"extending IN stops at source zero without moving OUT");
            Reset();Drag(true,20);Check(clip.Duration>=1.0/_fps-.00001&&Math.Abs(clip.Start+clip.Duration-6)<.00001,"IN cannot pass OUT or leave less than one scene frame");
            Reset();Drag(false,-1);Check(Math.Abs(clip.Start-3)<.00001&&Math.Abs(clip.SourceOffset-.5)<.00001&&Math.Abs(clip.Duration-2)<.00001,"OUT-edge trimming keeps source IN and start unchanged");Check(next.BlendIn==0,"trimming OUT removes a following fade that no longer overlaps");
            Reset();count=_undo.Count;Drag(false,-1,0);Check(Math.Abs(clip.Duration-3)<.00001&&Math.Abs(next.BlendIn-.2)<.00001&&_undo.Count==count+1,"reversing a live OUT drag restores its original fade without extra undo records");
            Reset();var tooltip=_timeline._GetTooltip(_timeline.MotionClipRect(entryId,clipId).Position+new Vector2(.5f,8));Check(tooltip.Contains("Trim IN")&&tooltip.Contains("Source IN"),"left handle explains source IN in its tooltip");
            GD.Print("TIMELINE_TRIM_OK checks="+checks);return checks;
        }
        finally
        {
            Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_playing=playing;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);SetAnimationWorkspaceMode(workspace);
        }
    }
}
