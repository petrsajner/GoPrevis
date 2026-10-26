using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task RunContentExpansionChecksAndQuit(bool prepare)
    {try{await RunContentExpansionChecks(prepare);GetTree().Quit(0);}catch(Exception e){GD.PushError("CONTENT_EXPANSION_FAILED "+e);GetTree().Quit(1);}}
    public async Task RunContentExpansionChecks(bool prepare)
    {
        string folder=Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/content-expansion-checks/run-"+Guid.NewGuid().ToString("N")[..10]));Directory.CreateDirectory(folder);int checks=0;var observations=new List<object>();
        void Check(bool yes,string message){Require(yes,"Content expansion: "+message);checks++;observations.Add(new{message,passed=yes});GD.Print("PASS Content expansion: "+message);}
        async Task Settle(){for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Render(Node3D node,Aabb bounds,string output,bool imageOnly=false)
        {
            var view=new SubViewport{Size=new Vector2I(240,200),World3D=_world.GetWorld3D(),RenderTargetUpdateMode=SubViewport.UpdateMode.Once};AddChild(view);var camera=new Camera3D{Current=true,CullMask=1,Fov=36,Near=.005f,Far=500};view.AddChild(camera);
            var center=node.GlobalTransform*bounds.GetCenter();float radius=Math.Max(.2f,(bounds.Size*node.GlobalBasis.Scale).Length());camera.GlobalPosition=center+new Vector3(.75f,.4f,1.2f).Normalized()*radius*1.7f;camera.LookAt(center);using var image=await CaptureShotImage(view,System.Threading.CancellationToken.None);image.SavePng(output);view.QueueFree();
        }
        try
        {
            StopHistoryPlayback();ClearObjects();_projectPath="";var ual=MotionCatalog.All.Where(m=>m.Id.StartsWith("human-ual",StringComparison.Ordinal)).ToArray();Check(ual.Length==84,"both free Standard archives provide 42 playable animations each");
            var human=AddObject("actor",Vector3.Zero,"Animation validation");var actor=(RiggedActor)human.Node;
            foreach(var motion in ual)
            {
                human.Data.MotionClips=new(){new(){MotionPath=MotionCatalog.MaterializeSource(motion),Name=motion.Name,Duration=motion.Duration,SourceScale=motion.SourceScale,Loop=motion.Loop}};RebuildTrack(human);SetTime(Math.Min(motion.Duration*.4,_duration));
                var pose=actor.CaptureEvaluatedPose();Check(pose.BoneRotations.Count>=21&&pose.BoneRotations.Values.All(q=>q.Length==4&&q.All(float.IsFinite)),motion.Id+" retargets to a finite complete prepared-human pose");
                if(prepare)await Render(human.Node,actor.GetPreparedPoseBounds(),ProjectSettings.GlobalizePath("res://assets/content/thumbnails/"+motion.Id+".png"));
            }
            var walk=MotionCatalog.Find("human-ual1-walk-loop")!;human.Data.MotionClips=new(){new(){MotionPath=MotionCatalog.MaterializeSource(walk),Name="UAL walk",SourceScale=1,Loop=true,InPlace=true,Duration=8}};human.Data.Path=new(){Enabled=true,OrientToPath=true,Duration=8,Points=new(){new[]{-2f,0f,1f},new[]{0f,0f,0f},new[]{2f,0f,-1f}},PointTimes=new(){0,4,8}};RebuildTrack(human);SetTime(2);
            var gait=MeasureActualGait(human,.1,1.8);GD.Print("UAL_GAIT "+JsonSerializer.Serialize(gait));Check(gait.FaceDirectionDot>.6f&&gait.ToeDirectionDot>.5f&&gait.OppositionScore>0,"new walking motion faces forward and plants actual feet opposite travel on an oriented route");
            ClearObjects();string manifestPath=ProjectSettings.GlobalizePath("res://assets/content/manifests/common-animal-models.json");var manifest=JsonNode.Parse(Godot.FileAccess.GetFileAsString("res://assets/content/manifests/common-animal-models.json"))!.AsArray();
            var heights=new Dictionary<string,float>{{"animal-cat",.62f},{"animal-dog-common",.85f},{"animal-pig",.95f},{"animal-sheep",1.15f},{"animal-rabbit",.7f},{"animal-eagle",.8f}};
            foreach(var baseModel in ContentCatalog.All.Where(m=>m.ActorFamily.StartsWith("animal-common-",StringComparison.Ordinal)).ToArray())
            {
                var model=baseModel;var entry=AddCatalogModel(model,Vector3.Zero,true);SetTime(0);var bounds=PreparedEntryBounds(entry);
                if(prepare)
                {
                    float scale=baseModel.Scale*heights[model.Id]/bounds.Size.Y;manifest.Single(n=>n!["Id"]!.GetValue<string>()==model.Id)!["Scale"]=scale;
                    _entries.Remove(entry);_world.RemoveChild(entry.Node);entry.Node.QueueFree();model=baseModel with{Scale=scale};entry=AddCatalogModel(model,Vector3.Zero,true);SetTime(0);bounds=PreparedEntryBounds(entry);
                }
                var native=entry.NativeTrack!;Check(native.PoseBoneNames.Count>=18,model.Name+" has a multi-joint editable skeleton");Check(Math.Abs(bounds.Size.Y-heights[model.Id])<.03,model.Name+" has a normalized world height");
                Check(native.PoseBoneNames.All(n=>!n.StartsWith("Control_",StringComparison.Ordinal)),model.Name+" hides unweighted control helpers from posing");
                var foot=native.PoseBoneNames.FirstOrDefault(n=>n.Contains("FrontFoot",StringComparison.Ordinal)||n.Contains("FrontPaw",StringComparison.Ordinal)||n=="Foot_R");
                Check(foot!=null,model.Name+" exposes an actual deforming foot or claw");var before=native.FootSkinPoints(foot!);var joint=native.GetPoseJointWorld(foot!);native.MovePoseJointWorld(foot!,joint+Vector3.Up*.06f);var after=native.FootSkinPoints(foot!);
                Check(before.Length>0&&before.Length==after.Length&&before.Zip(after).Average(pair=>pair.First.DistanceTo(pair.Second))>.005,model.Name+" foot IK moves the weighted paw geometry");
                SetTime(0);foreach(var motion in MotionCatalog.ForFamily(model.ActorFamily))
                {
                    entry.Data.MotionClips=new(){new(){MotionPath=entry.Data.ModelPath!,Name=motion.Name,SourceAnimation=motion.SourceAnimation,Duration=motion.Duration,Loop=motion.Loop}};RebuildTrack(entry);SetTime(Math.Min(motion.Duration*.35,_duration));
                    Check(native.CaptureEvaluatedPose().BoneRotations.Values.All(q=>q.All(float.IsFinite)),motion.Name+" plays on its own rig");
                    if(prepare)await Render(entry.Node,PreparedEntryBounds(entry),ProjectSettings.GlobalizePath("res://assets/content/thumbnails/"+motion.Id+".png"));
                }
                entry.Data.MotionClips.Clear();RebuildTrack(entry);SetTime(0);
                if(prepare)await Render(entry.Node,PreparedEntryBounds(entry),ProjectSettings.GlobalizePath("res://assets/content/thumbnails/"+model.Id+".png"));
                _entries.Remove(entry);_world.RemoveChild(entry.Node);entry.Node.QueueFree();await Settle();
            }
            if(prepare)File.WriteAllText(manifestPath,manifest.ToJsonString(new JsonSerializerOptions{WriteIndented=true})+"\n");
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,ual=ual.Length,animals=heights.Count,observations},_json));GD.Print("CONTENT_EXPANSION_OK checks="+checks+" output="+folder);
        }
        finally{StopHistoryPlayback();}
    }
}
