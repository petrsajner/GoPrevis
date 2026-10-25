using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenPrevis;

public partial class GenPrevisApp
{
    public async Task<int> RunAnimalRepairChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;int checks=0;var results=new List<object>();
        string folder=Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/animal-repair-checks"));Directory.CreateDirectory(folder);
        void Check(bool condition,string name){Require(condition,"Animal repair: "+name);checks++;}
        try
        {
            _playing=false;StopPoseEditing();ClearObjects();SetTime(0);
            foreach(var model in ContentCatalog.ForCategory("Animals"))
            {
                var entry=AddCatalogModel(model,Vector3.Zero,false);var rig=entry.NativeTrack!;string id=entry.Data.Id;
                if(rig.FootEffectors.Count>0)
                {
                    Check(rig.FootEffectors.Count==4,model.Name+" exposes all four actual deforming foot joints");
                    foreach(string foot in rig.FootEffectors)
                    {
                        SetTime(0);var before=rig.GetPoseJointWorld(foot);var skin=rig.FootSkinPoints(foot);
                        Check(skin.Length>4,model.Name+" "+foot+" has real weighted mesh vertices");
                        rig.MovePoseJointWorld(foot,before+new Vector3(.05f,.16f,.02f));var after=rig.GetPoseJointWorld(foot);var moved=rig.FootSkinPoints(foot);
                        Check(after.Y-before.Y>.08f,model.Name+" "+foot+" can be lifted with its complete ancestor chain");
                        Check(moved.Average(p=>p.Y)-skin.Average(p=>p.Y)>.06,model.Name+" "+foot+" moves the actual weighted foot mesh off the ground");
                    }
                }
                var motions=MotionCatalog.ForFamily(model.ActorFamily).ToArray();float maximumError=0;
                foreach(var motion in motions)
                {
                    rig.SetClips(new[]{new MotionClipData{Name=motion.Name,SourceAnimation=motion.SourceAnimation,Duration=motion.Duration,Loop=false}});
                    for(int sample=0;sample<=4;sample++){double t=motion.Duration*sample/4;rig.Evaluate(t);maximumError=Math.Max(maximumError,rig.AuthoredPoseError(motion.SourceAnimation!,t));}
                    Check(maximumError<.001f,model.Name+" "+motion.SourceAnimation+" keeps authored joint geometry above locomotion alignment (error="+maximumError+")");
                }
                results.Add(new{model=model.Name,feet=rig.FootEffectors.Count,motions=motions.Length,maximumSourcePoseError=maximumError});
                ClearObjects();SetTime(0);
            }
            var donkey=AddCatalogModel(ContentCatalog.Find("animal-donkey")!,Vector3.Zero,true);Select(donkey);SetTime(0);
            Check(donkey.Data.MotionClips.Single().IsPlaceholder,"a newly placed animal marks its automatic idle as a replaceable placeholder");
            DropMotion("animal-donkey-gallop",donkey,Vector3.Zero);
            var clip=donkey.Data.MotionClips.Single();Check(clip.Start==0&&clip.Duration>=_duration&&clip.Loop&&!clip.IsPlaceholder,"Donkey Gallop immediately replaces idle and loops across the shot");
            SetTime(.1);var a=donkey.NativeTrack!.CaptureEvaluatedPose();SetTime(.25);var b=donkey.NativeTrack.CaptureEvaluatedPose();SetTime(4.1);var c=donkey.NativeTrack.CaptureEvaluatedPose();SetTime(4.25);var d=donkey.NativeTrack.CaptureEvaluatedPose();
            Check(PoseCorrectionRuntime.ChangedBones(a,b).Count>2&&PoseCorrectionRuntime.ChangedBones(c,d).Count>2,"the donkey runs immediately and is still animating after four seconds");
            donkey.Data.MotionClips.Clear();RebuildTrack(donkey);SetTime(0);Select(donkey);StartPoseEditing();string effector=donkey.NativeTrack.FootEffectors.First();SelectPoseBone(effector);BeginPoseGesture();UpdatePoseEffectorWorld(effector,donkey.NativeTrack.GetPoseJointWorld(effector)+Vector3.Up*.18f);EndPoseGesture();
            var expected=donkey.NativeTrack.GetPoseJointWorld(effector);string file=Path.Combine(folder,"lifted-donkey.genprevis");string donkeyId=donkey.Data.Id;SaveTo(file);LoadProjectFromPath(file);SetTime(0);donkey=_entries.Single(e=>e.Data.Id==donkeyId);
            Check(donkey.NativeTrack!.GetPoseJointWorld(effector).DistanceTo(expected)<.001f,"lifted foot corrections survive project save and reopen");
            if(DisplayServer.GetName()!="headless")
            {
                Select(donkey);FrameSelected();StartPoseEditing();SelectPoseBone(effector);
                for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"lifted-donkey-ui.png"));
            }
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,models=results},_json));
            GD.Print("ANIMAL_REPAIR_OK checks="+checks);return checks;
        }
        finally{StopPoseEditing();Restore(original,"");_projectPath=project;SetTime(time);}
    }
    async Task RunAnimalRepairChecksAndQuit(){try{await RunAnimalRepairChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("ANIMAL_REPAIR_FAILED "+ex);GetTree().Quit(1);}}
}
