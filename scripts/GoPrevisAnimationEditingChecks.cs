using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task RunAnimationEditingChecksAndQuit()
    {
        string folder=ProjectSettings.GlobalizePath("res://output/animation-editing-checks/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(folder);
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        void Check(bool yes,string text){Require(yes,"Animation editing: "+text);checks++;observations.Add(new{description=text,passed=yes});}
        try
        {
            await RunAnimationPanelEditChecks(Check);await RunSelectedKeyDragChecks(Check);await RunMotionClipboardChecks(Check,folder);
            await RunMotionSourceRangeChecks(folder,Check);passed=true;GD.Print("ANIMATION_EDITING_OK checks="+checks+" output="+folder);GetTree().Quit(0);
        }
        catch(Exception error){failure=error.ToString();GD.PushError("ANIMATION_EDITING_FAILED "+error);GetTree().Quit(1);}
        finally{File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));}
    }
    async Task RunMotionSourceRangeChecks(string folder,Action<bool,string> check)
    {
        var original=Snapshot();string project=_projectPath,mode=_animationWorkspaceMode;double time=_time;
        try
        {
            ClearObjects();ClearCameras();CreateCamera("Source handles camera",null,true);_duration=2;_scrub.MaxValue=2;SetAnimationWorkspaceMode("Clips");
            var actor=AddObject("actor",Vector3.Zero,"Recorded motion");var source=MotionCatalog.Find("human-walk-gesture")!;
            AppendMotion(actor,MotionCatalog.MaterializeSource(source),source.SourceScale);var clip=actor.Data.MotionClips[0];double length=MotionSourceLength(actor,clip);
            clip.Start=1;clip.Duration=.5;clip.SourceOffset=.4;clip.Speed=1;clip.Loop=false;_duration=2;_scrub.MaxValue=2;_refreshing=true;_durationField.Value=2;_refreshing=false;RebuildTrack(actor);SelectTimelineClip(actor,clip);SetTime(1);
            for(int i=0;i<6;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var available=MotionAvailableData(actor,clip);check(Math.Abs(length-((RiggedActor)actor.Node).MotionSourceDuration(clip))<1e-9&&length>.9,"the source range reads the actual prepared BVH duration");
            check(Math.Abs(available.Start-.6)<1e-9&&Math.Abs(available.End-(1+length-.4))<1e-9,"grey source handles include both trimmed IN and unused OUT data");
            var active=_timeline.MotionClipRect(actor.Data.Id,clip.Id);var full=_timeline.MotionSourceRect(actor.Data.Id,clip.Id);
            check(full.Position.X<active.Position.X&&full.End.X>active.End.X,"the recorded source draws wider than the active trimmed block");
            var fitButton=_motionSourceViewButton!;float activeWidth=active.Size.X;fitButton.EmitSignal(Godot.Button.SignalName.Pressed);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);full=_timeline.MotionSourceRect(actor.Data.Id,clip.Id);
            check(full.Size.X>0&&fitButton.IsVisibleInTree()&&fitButton.Text=="Shot range"&&_timeline.MotionClipRect(actor.Data.Id,clip.Id).Size.X<activeWidth&&full.End.X<=_timeline.Size.X,"Fit source reveals source data beyond the shot without changing the clip or shot length");
            fitButton.EmitSignal(Godot.Button.SignalName.Pressed);check(Math.Abs(_timeline.MotionClipRect(actor.Data.Id,clip.Id).Size.X-activeWidth)<.01&&fitButton.Text=="Fit source"&&_duration==2,"Shot range restores the original ruler and document duration");
            var speed=new ScalarAnimationCurve();PropertyCurveRuntime.Upsert(speed,clip.Start,0);PropertyCurveRuntime.Upsert(speed,clip.Start+clip.Duration,0);clip.PropertyAnimation.Channels["Speed"]=speed;
            available=MotionAvailableData(actor,clip);check(Math.Abs(available.Start-clip.Start)<1e-8&&Math.Abs(available.End-clip.Start-clip.Duration)<1e-8&&available.EndPaused,"paused source boundaries do not invent enormous unused source tails");
            speed.Enabled=false;available=MotionAvailableData(actor,clip);check(!available.VariableTiming&&Math.Abs(available.Start-.6)<1e-8,"disabled playback curves retain the normal recorded source extent");
            var offset=new ScalarAnimationCurve();PropertyCurveRuntime.Upsert(offset,clip.Start,.4);PropertyCurveRuntime.Upsert(offset,clip.Start+clip.Duration,.9);clip.PropertyAnimation.Channels["SourceOffset"]=offset;
            available=MotionAvailableData(actor,clip);check(available.VariableTiming&&Math.Abs(available.Start-.8)<.001,"grey source projection includes animated source-offset rate as well as playback speed");
            clip.PropertyAnimation.Channels.Clear();RebuildTrack(actor);
            var native=AddFox(new Vector3(2,0,0),true);
            if(native?.NativeTrack!=null&&native.Data.MotionClips.Count>0){var c=native.Data.MotionClips[0];check(MotionSourceLength(native,c)>0,"native animal clips expose their real embedded source animation length");}
            SelectTimelineClip(actor,clip);SetTime(1);for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var image=GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(folder,"motion-source-handles.png"));
        }
        finally{Restore(original,"");_projectPath=project;SetTime(time);SetAnimationWorkspaceMode(mode);}
    }
}
