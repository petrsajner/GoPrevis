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
    async Task RunTimelineDeletionChecksAndQuit()
    {try{await RunTimelineDeletionChecks();GetTree().Quit(0);}catch(Exception error){GD.PushError("TIMELINE_DELETE_FAILED "+error);GetTree().Quit(1);}}
    public async Task RunTimelineDeletionChecks()
    {
        string folder=Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/timeline-deletion-checks/run-"+Guid.NewGuid().ToString("N")[..10]));Directory.CreateDirectory(folder);
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        void Check(bool value,string message){Require(value,"Timeline delete: "+message);checks++;observations.Add(new{message,passed=value});GD.Print("PASS Timeline delete: "+message);}
        async Task Settle(){for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Click(Vector2 p,bool right=false){_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=right?MouseButton.Right:MouseButton.Left,Pressed=true,Position=p});if(!right)_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=p});}
        Vector2 At(Rect2 row,double time)=>new(row.Position.X+row.Size.X*(float)(time/_duration),row.Position.Y+11);
        Vector2 MovePoint(){var rect=_timeline.CameraMoveRect(Wide().Id);return new Vector2(rect.Position.X+rect.Size.X*.42f,rect.GetCenter().Y);}
        void Delete(){GetViewport().GuiReleaseFocus();_timeline.GrabFocus();_UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Delete});}
        void Header(string kind,string id,string channel="All",bool right=false){var row=_timeline.KeyRowRect(kind,id,channel);Click(new Vector2(row.Position.X-45,row.Position.Y+11),right);}
        Entry Human()=>_entries.Single(e=>e.Data.Id=="ai-walker");Entry Car()=>_entries.Single(e=>e.Data.Id=="ai-car");Entry Dog()=>_entries.Single(e=>e.Data.Id=="timeline-dog");CameraEntry Wide()=>_cameras.Single(c=>c.Id=="ai-wide");
        async Task Screenshot(string name){await Settle();if(DisplayServer.GetName()!="headless"){await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,name));}}
        try
        {
            var plan=AiFixturePlan();var humanPlan=plan.Objects.Single(o=>o.Id=="ai-walker");humanPlan.Motions[0].Duration=4;humanPlan.Motions.Add(new(){MotionId="human-walk-gesture",Start=3.5,Duration=4.5,BlendIn=.5,Loop=true,InPlace=true});
            var dogModel=ContentCatalog.Find("animal-husky")!;var dogMotion=MotionCatalog.ForFamily(dogModel.ActorFamily).First(m=>m.SourceAnimation=="Walk");
            plan.Objects.Add(new(){Id="timeline-dog",AssetId=dogModel.Id,Motions=new(){new(){MotionId=dogMotion.Id,Duration=8,Loop=true,InPlace=true}}});
            await ApplyAiPlan(plan,true);var human=Human();human.Data.Path=new(){Enabled=true,OrientToPath=true,Duration=8,Points=new(){new[]{-3f,0f,1f},new[]{-1f,0f,0f},new[]{1f,0f,-2f}},PointTimes=new(){0,4,8}};
            human.Data.PoseCorrections.Root.Add(new(){Time=1,Value=new[]{0f,.1f,0f}});human.Data.PoseCorrections.Bones["RightHand"]=new(){new(){Time=2}};
            TransformLayerRuntime.Upsert(Car().Data.TransformAnimation.Position,1,Vector3.One);TransformLayerRuntime.Upsert(Car().Data.TransformAnimation.Position,6,new Vector3(2,0,1));
            TransformLayerRuntime.Upsert(Car().Data.TransformAnimation.Rotation,2,new Vector3(0,15,0));TransformLayerRuntime.Upsert(Car().Data.TransformAnimation.Scale,3,Vector3.One*1.1f);
            var camera=Wide();camera.Movement=new(){Mode="Orbit",TargetId="ai-car",Duration=8,Radius=6};TransformLayerRuntime.Upsert(camera.Animation.Rotation,2,new Vector3(-10,0,0));camera.LensKeys.Add(new(){Time=4,Value=50});
            string project=Path.Combine(folder,"Timeline fixture.goprevis");SaveTo(project);var fixture=Snapshot();string text=File.ReadAllText(project);
            async Task Reset(){StopPoseEditing();Restore(AiClone(fixture),"");_projectPath=project;SetTime(2);_undo.Clear();_redo.Clear();MarkProjectClean(text);await Settle();}
            await Reset();human=Human();string clip=human.Data.MotionClips[0].Id,other=human.Data.MotionClips[1].Id;string pathBefore=JsonSerializer.Serialize(human.Data.Path),poseBefore=JsonSerializer.Serialize(human.Data.PoseCorrections);
            Click(_timeline.MotionClipRect(human.Data.Id,clip).GetCenter());Delete();human=Human();
            Check(human.Data.MotionClips.Count==1&&human.Data.MotionClips[0].Id==other,"Delete removes the selected human library motion clip");
            Check(_entries.Count==5&&_cameras.Count==2&&JsonSerializer.Serialize(human.Data.Path)==pathBefore&&JsonSerializer.Serialize(human.Data.PoseCorrections)==poseBefore,"motion deletion preserves the character, cameras, path and additive pose keys");
            Check(human.Data.MotionClips[0].BlendIn==0&&_undo.Count==1&&_projectDirty,"deleting a predecessor repairs the remaining blend and records one dirty Undo step");
            Undo();Check(Human().Data.MotionClips.Count==2&&!_projectDirty,"Undo restores both motions and the saved state");Redo();Check(Human().Data.MotionClips.Count==1,"Redo removes the same clip again");
            await Reset();human=Human();Click(_timeline.MotionClipRect(human.Data.Id,human.Data.MotionClips[0].Id).GetCenter());Click(_timeline.MotionClipRect(human.Data.Id,human.Data.MotionClips[1].Id).GetCenter(),true);
            Check(_timeline.ItemMenuVisible&&_selectedClipId==human.Data.MotionClips[1].Id,"right-click selects the clip under the pointer and exposes its Delete action");await Screenshot("motion-context-menu.png");_timeline.InvokeItemMenuDelete();
            Check(Human().Data.MotionClips.Count==1&&Human().Data.MotionClips[0].Id==clip,"the context action removes the targeted clip rather than an earlier selection");
            await Settle();_UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Delete});Check(Human().Data.MotionClips.Count==1&&_entries.Count==5&&_timeline.SelectedItemKind=="","after deletion nothing else is selected, so another Delete removes nothing");
            Click(_timeline.MotionClipRect(Human().Data.Id,clip).GetCenter());Delete();human=Human();SetTime(.1);var poseA=((RiggedActor)human.Node).CaptureEvaluatedPose();SetTime(.7);var poseB=((RiggedActor)human.Node).CaptureEvaluatedPose();
            Check(human.Data.MotionClips.Count==0&&human.Data.MotionPath==null&&poseA.BoneRotations["LeftUpperLeg"].SequenceEqual(poseB.BoneRotations["LeftUpperLeg"]),"removing the final motion stops its actual leg animation and clears the fallback source");
            string empty=Path.Combine(folder,"Removed human motion.goprevis");SaveTo(empty);LoadProjectFromPath(empty);Check(Human().Data.MotionClips.Count==0&&Human().Data.MotionPath==null,"the removed final human clip stays removed after save/reopen");
            await Reset();var dog=Dog();Click(_timeline.MotionClipRect(dog.Data.Id,dog.Data.MotionClips[0].Id).GetCenter());Delete();dog=Dog();
            Check(dog.Data.MotionClips.Count==0&&dog.NativeTrack!.ClipCount==0,"the same Delete route removes native animal motion without deleting its model");Undo();Check(Dog().NativeTrack!.ClipCount==1,"Undo restores the native animal clip and playback track");
            await Reset();dog=Dog();dog.NativeTrack=null;dog.Data.MotionClips.Clear();dog.Data.MotionPath=null;dog.Animation=dog.Player!.GetAnimationList().First(a=>a!="RESET");_timeline.UpdateMinimumHeight();Click(At(_timeline.KeyRowRect("Motion",dog.Data.Id),3),true);_timeline.InvokeItemMenuDelete();
            Check(dog.Animation==""&&dog.Data.ModelAnimation=="","the full legacy imported-animation span can also be removed");
            string legacy=Path.Combine(folder,"Removed imported animation.goprevis");SaveTo(legacy);LoadProjectFromPath(legacy);Check(Dog().Animation==""&&Dog().Data.MotionClips.Count==0,"reopening an explicitly stopped imported animation does not auto-start its first source again");
            await Reset();Header("Motion",Human().Data.Id,right:true);_timeline.InvokeItemMenuDelete();Check(Human().Data.MotionClips.Count==0&&_entries.Count==5,"a motion track's context action removes all of its clips only");
            await Reset();Click(MovePoint());Delete();camera=Wide();
            Check(camera.Movement.Mode=="None"&&camera.Animation.Rotation.Count==1&&camera.LensKeys.Count==1&&_cameras.Count==2,"camera move Delete preserves its camera, retained transform keys and lens keys");Undo();Check(Wide().Movement.Mode=="Orbit","Undo restores the exact native camera move");
            await Reset();Click(MovePoint(),true);Check(_timeline.ItemMenuVisible,"a camera move also exposes a context Delete action");_timeline.InvokeItemMenuDelete();Check(Wide().Movement.Mode=="None","the context action removes the camera move");
            await Reset();var row=_timeline.KeyRowRect("Path",Human().Data.Id);Click(At(row,4),true);
            Check(Human().Data.Path.Points.Count==2&&Human().Data.Path.PointTimes.Count==2&&Human().Data.Path.Enabled,"right-click removes an individual path point and its matching arrival key");Undo();Check(Human().Data.Path.Points.Count==3,"Undo restores the path point");
            await Reset();Click(At(_timeline.KeyRowRect("Path",Human().Data.Id),2));Delete();Check(Human().Data.Path.Points.Count==0&&Human().Data.MotionClips.Count==2,"selecting the path span and Delete removes the route while preserving motion clips");
            await Reset();Header("Path",Human().Data.Id,right:true);_timeline.InvokeItemMenuDelete();Check(Human().Data.Path.Points.Count==0,"the path track also supports context deletion");
            await Reset();_timeline.ExpandObject(Car().Data.Id);await Settle();Click(At(_timeline.KeyRowRect("Transform",Car().Data.Id,"Position"),1));Delete();
            Check(Car().Data.TransformAnimation.Position.Count==1&&Car().Data.TransformAnimation.Rotation.Count==1,"Delete on a position key removes that XYZ key without deleting other channels");
            await Reset();_timeline.ExpandObject(Car().Data.Id);await Settle();Header("Transform",Car().Data.Id,"Position",true);_timeline.InvokeItemMenuDelete();
            Check(Car().Data.TransformAnimation.Position.Count==0&&Car().Data.TransformAnimation.Rotation.Count==1,"expanded parameter track deletion clears only its own keys");
            await Reset();Header("Transform",Car().Data.Id);Delete();Check(Car().Data.TransformAnimation.Position.Count==0&&Car().Data.TransformAnimation.Rotation.Count==0&&Car().Data.TransformAnimation.Scale.Count==0&&Car().Data.Path.Enabled,"whole transform-track deletion preserves the object and its path");
            await Reset();Car().Data.TransformAnimation=new();TransformLayerRuntime.Upsert(Car().Data.TransformAnimation.Position,20,Vector3.One);Header("Transform",Car().Data.Id);Delete();Check(Car().Data.TransformAnimation.Position.Count==0,"a selected track can clear stored keys beyond the current shot duration too");
            await Reset();Header("Transform",Wide().Id,right:true);_timeline.InvokeItemMenuDelete();Check(Wide().Movement.Mode=="None"&&Wide().Animation.Rotation.Count==0&&Wide().LensKeys.Count==0&&_cameras.Count==2,"selecting the whole camera animation clears its move and keys together while retaining the camera");
            await Reset();Click(new Vector2(10,_timeline.KeyRowRect("Transform",Wide().Id).Position.Y+11));await Settle();Click(At(_timeline.KeyRowRect("Transform",Wide().Id,"Lens"),4),true);Check(Wide().LensKeys.Count==0&&Wide().Animation.Rotation.Count==1&&Wide().Movement.Mode=="Orbit","right-click removes a lens key independently of rotation and camera movement");
            await Reset();_timeline.ExpandPoseCorrections(Human().Data.Id);await Settle();Click(At(_timeline.KeyRowRect("Pose",Human().Data.Id,"Head"),4),true);
            Check(Human().Data.PoseCorrections.Bones["Head"].Count==1&&Human().Data.PoseCorrections.Bones["RightHand"].Count==1,"right-click removes only the targeted pose-correction key");
            await Reset();_timeline.ExpandPoseCorrections(Human().Data.Id);await Settle();Header("Pose",Human().Data.Id,"Head",true);_timeline.InvokeItemMenuDelete();
            Check(!Human().Data.PoseCorrections.Bones.ContainsKey("Head")&&Human().Data.PoseCorrections.Bones.ContainsKey("RightHand"),"joint-track deletion preserves other joint corrections");
            await Reset();Header("Pose",Human().Data.Id,right:true);_timeline.InvokeItemMenuDelete();Check(Human().Data.PoseCorrections.Bones.Count==0&&Human().Data.PoseCorrections.Root.Count==0&&Human().Data.MotionClips.Count==2,"deleting the pose group clears corrections while leaving mocap intact");
            await Reset();Click(_timeline.MotionClipRect(Human().Data.Id,clip).GetCenter());_name.GrabFocus();_UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Delete});Check(Human().Data.MotionClips.Count==2,"Delete while editing text cannot remove timeline animation");
            GetViewport().GuiReleaseFocus();_timeline.ClearKeySelection();_timeline.GrabFocus();Delete();Check(_entries.Count==5&&_cameras.Count==2,"Delete in an empty focused timeline cannot delete scene entities");
            await Reset();Click(_timeline.MotionClipRect(Human().Data.Id,clip).GetCenter());Click(At(_timeline.KeyRowRect("Path",Human().Data.Id),4));Delete();Check(Human().Data.MotionClips.Count==2&&Human().Data.Path.Points.Count==2,"selecting a key replaces the prior clip deletion target");
            await Reset();Click(_timeline.MotionClipRect(Human().Data.Id,clip).GetCenter());await Screenshot("selected-motion-clip.png");passed=true;GD.Print("TIMELINE_DELETE_OK checks="+checks+" output="+folder);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally{File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));}
    }
}
