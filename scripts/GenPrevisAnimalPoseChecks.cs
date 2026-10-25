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
    public async Task<int> RunAnimalPoseChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;string? savedId=null;
        var observations=new List<object>();bool passed=false;string? failure=null;
        void Check(bool valid,string message){observations.Add(new{message,passed=valid});Require(valid,"Animal pose: "+message);checks++;}
        try
        {
            StopPoseEditing();_playing=false;ClearObjects();SetTime(0);
            foreach(var model in ContentCatalog.ForCategory("Animals"))
            {
                var animal=AddCatalogModel(model,Vector3.Zero,false);var rig=animal.NativeTrack;
                Check(rig!=null&&rig.PoseBoneNames.Count>5,model.Name+" exposes editable original-rig joints");
                rig!.Evaluate(0);Check(rig.PoseBoneNames.All(name=>rig.GetPoseJointWorld(name).IsFinite()),model.Name+" forward kinematics produces finite world-space joints");
                Check(rig.PoseBoneNames.All(name=>!name.StartsWith("PoleTarget",StringComparison.OrdinalIgnoreCase)&&(!name.StartsWith("IK",StringComparison.OrdinalIgnoreCase)||rig.IsFootEffector(name))),model.Name+" hides imported helper controllers from body editing");
                if(model.Id is "animal-horse" or "fox_actor")
                {
                    string headJoint=rig.DefaultPoseBone;var before=rig.GetPoseBoneRotation(headJoint);rig.SetPoseBoneRotationDegrees(headJoint,rig.GetPoseBoneRotationDegrees(headJoint)+new Vector3(0,15,0));
                    Check(PoseCorrectionRuntime.RotationDifferenceSquared(before,rig.GetPoseBoneRotation(headJoint))>.00001f,model.Name+" accepts local head rotation on its own native rig");
                    string foot=rig.PoseBoneNames.First(name=>(rig.IsFootEffector(name)||rig.IsEndJoint(name))&&(name.Contains("IK",StringComparison.OrdinalIgnoreCase)||name.Contains("LowerLeg",StringComparison.OrdinalIgnoreCase)||name.Contains("Hand",StringComparison.OrdinalIgnoreCase)||name.Contains("Foot",StringComparison.OrdinalIgnoreCase))&&rig.ParentPoseBone(name) is {} parent&&rig.GetPoseJointWorld(name).DistanceTo(rig.GetPoseJointWorld(parent))>.02f);
                    string parentJoint=rig.ParentPoseBone(foot)!;Vector3 parentPosition=rig.GetPoseJointWorld(parentJoint),footPosition=rig.GetPoseJointWorld(foot);float boneLength=footPosition.DistanceTo(parentPosition);Vector3 reachable=parentPosition+new Quaternion(Vector3.Up,.04f)*(footPosition-parentPosition);
                    rig.MovePoseJointWorld(foot,reachable);Check(rig.GetPoseJointWorld(foot).DistanceTo(reachable)<.001f,model.Name+" solves a native foot IK target in world units");
                    Check(Math.Abs(rig.GetPoseJointWorld(foot).DistanceTo(rig.GetPoseJointWorld(parentJoint))-boneLength)<.0001f,model.Name+" IK preserves limb length despite its distinct source scale");
                }
            }
            ClearObjects();SetTime(0);var entry=AddCatalogModel(ContentCatalog.Find("animal-wolf")!,new Vector3(1,0,2),false);string id=entry.Data.Id;Select(entry);
            var motion=MotionCatalog.All.First(m=>m.ActorFamily==entry.Data.ActorFamily&&m.Name.Contains("walk",StringComparison.OrdinalIgnoreCase));DropMotion(motion.Id,entry,entry.Node.Position);SetTime(.65);StartPoseEditing();
            var native=entry.NativeTrack!;string head=native.DefaultPoseBone;SelectPoseBone(head);string clipData=JsonSerializer.Serialize(entry.Data.MotionClips);var initial=native.GetPoseBoneRotation(head);int undoCount=_undo.Count;
            BeginPoseGesture();UpdatePoseBoneRotation(native.GetPoseBoneRotationDegrees(head)+new Vector3(0,18,0));EndPoseGesture();
            Check(_poseEditing&&_posePanel.Visible&&_poseJointChoice.ItemCount==native.PoseBoneNames.Count,"the shared Pose inspector edits a selected animal");
            Check(_undo.Count==undoCount+1,"a complete joint gesture makes exactly one Undo action");
            Check(PoseCorrectionRuntime.RotationDifferenceSquared(initial,native.GetPoseBoneRotation(head))>.00001f,"head rotation changes actual native bone transforms");
            Check(JsonSerializer.Serialize(entry.Data.MotionClips)==clipData,"manual animal correction preserves source motion data");
            Check(entry.Data.PoseCorrections.Bones.TryGetValue(head,out var headKeys)&&headKeys.Count==2&&headKeys[0].Time==0,"a first late native edit gets a neutral time-zero key");
            Quaternion expected=PoseCorrectionRuntime.ReadBone(native.CaptureSourcePose(),head)*PoseCorrectionRuntime.EvaluateRotation(entry.Data.PoseCorrections.Bones[head],_time);
            Check(PoseCorrectionRuntime.RotationDifferenceSquared(expected,native.GetPoseBoneRotation(head))<.00000001f,"native local corrections are added after the evaluated source pose");
            var expectedPose=native.CaptureEvaluatedPose();SetTime(1.4);var laterSource=native.CaptureSourcePose();SetTime(.65);
            Check(PoseCorrectionRuntime.ChangedBones(expectedPose,native.CaptureEvaluatedPose()).Count==0,"native corrected poses are deterministic under reverse seeking");
            Check(PoseCorrectionRuntime.ChangedBones(laterSource,native.CaptureSourcePose()).Count>0,"the source animal walk continues animating beneath corrections");
            Undo();entry=_entries.Single(e=>e.Data.Id==id);native=entry.NativeTrack!;Check(!PoseCorrectionRuntime.HasKeys(entry.Data.PoseCorrections)&&_poseEditing,"Undo restores uncorrected native motion and the Pose context");
            Redo();entry=_entries.Single(e=>e.Data.Id==id);native=entry.NativeTrack!;Check(PoseCorrectionRuntime.HasKeys(entry.Data.PoseCorrections),"Redo restores native keyed corrections");
            var pathBefore=entry.Data.Path;entry.Data.Path=new ActorPathData{Enabled=true,OrientToPath=true,Points=new(){new[]{0f,0f,0f},new[]{3f,0f,1f}},Duration=3};SetTime(.65);
            var pathRotation=entry.Node.RotationDegrees;string rootBone=native.RootBoneName;Quaternion rootWorldBefore=native.GetPoseJointWorldBasis(rootBone).GetRotationQuaternion();SelectPoseBone(rootBone);BeginPoseGesture();UpdatePoseBoneRotation(native.GetPoseBoneRotationDegrees(rootBone)+new Vector3(0,22,0));EndPoseGesture();
            Check(entry.Node.RotationDegrees.DistanceTo(pathRotation)<.001f,"path orientation uses source facing rather than cancelling a manual animal root turn");
            Check(PoseCorrectionRuntime.RotationDifferenceSquared(rootWorldBefore,native.GetPoseJointWorldBasis(rootBone).GetRotationQuaternion())>.0001f,"a keyed native root rotation remains visible in the final world pose above the path");entry.Data.Path=pathBefore;SetTime(.65);
            string end=native.PoseBoneNames.First(name=>name.StartsWith("FrontLowerLeg",StringComparison.Ordinal));string middle=native.ParentPoseBone(end)!;string upper=native.ParentPoseBone(middle)!;
            SetTime(.65);var lowerPosition=native.GetPoseJointWorld(end);var target=lowerPosition+new Vector3(.04f,.045f,.04f);float beforeError=lowerPosition.DistanceTo(target);
            var bones=native.PoseBoneNames.ToDictionary(name=>name,name=>native.GetPoseJointWorld(name));float lengthA=bones[end].DistanceTo(bones[middle]),lengthB=bones[middle].DistanceTo(bones[upper]);
            SelectPoseBone(end);BeginPoseGesture();UpdatePoseEffectorWorld(end,target);EndPoseGesture();
            Check(native.GetPoseJointWorld(end).DistanceTo(target)<beforeError*.5f,"dragging a native foot solves its ancestor IK chain toward the target");
            Check(Math.Abs(native.GetPoseJointWorld(end).DistanceTo(native.GetPoseJointWorld(middle))-lengthA)<.0001f&&Math.Abs(native.GetPoseJointWorld(middle).DistanceTo(native.GetPoseJointWorld(upper))-lengthB)<.0001f,"native IK retains the original limb lengths");
            var childRotation=native.GetPoseBoneRotation(end);var childBefore=native.GetPoseJointWorld(end);var middleBefore=native.GetPoseJointWorld(middle);SelectPoseBone(middle);BeginPoseGesture();UpdatePoseEffectorWorld(middle,middleBefore+new Vector3(.03f,.04f,0));EndPoseGesture();
            Check(native.GetPoseJointWorld(end).DistanceTo(childBefore)>.005f,"children follow a moved middle joint rather than staying pinned");
            Check(PoseCorrectionRuntime.RotationDifferenceSquared(childRotation,native.GetPoseBoneRotation(end))<.00000001f,"moving a middle joint retains its child's local pose");
            entry.Node.Scale=new Vector3(1.15f,1.15f,1.15f);entry.Data.Scale=Array3(entry.Node.Scale);entry.Node.RotationDegrees=new Vector3(0,50,0);entry.Data.Rotation=Array3(entry.Node.RotationDegrees);SetTime(.65);
            var scaledPosition=native.GetPoseJointWorld(end);SelectPoseBone(end);BeginPoseGesture();UpdatePoseEffectorWorld(end,scaledPosition+new Vector3(.02f,.03f,.02f));EndPoseGesture();Check(native.PoseBoneNames.All(name=>native.GetPoseJointWorld(name).IsFinite()),"native IK supports rotated and scaled scene objects");
            var validPose=native.CaptureEvaluatedPose();bool rejected=false;try{native.MovePoseJointWorld(head,new Vector3(float.NaN,0,0));}catch(FormatException){rejected=true;}Check(rejected&&PoseCorrectionRuntime.ChangedBones(validPose,native.CaptureEvaluatedPose()).Count==0,"invalid native targets fail without corrupting the pose");
            SelectPoseBone(head);SetPoseTool("Rotate");FrameSelected();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(PoseViewportProject(native.GetPoseJointWorld(head),out var point))
            {
                Quaternion before=native.GetPoseBoneRotation(head);undoCount=_undo.Count;
                _sceneContainer.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
                _sceneContainer.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseMotion{Position=point+new Vector2(30,-10),Relative=new Vector2(30,-10),ButtonMask=MouseButtonMask.Left});
                _sceneContainer.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point+new Vector2(30,-10)});
                Check(PoseCorrectionRuntime.RotationDifferenceSquared(before,native.GetPoseBoneRotation(head))>.000001f&&_undo.Count==undoCount+1,"actual viewport joint dragging rotates the animal with one Undo gesture");
            }
            else Check(false,"the native head marker projects into the editing viewport");
            string portable=ProjectSettings.GlobalizePath("user://animal-pose-checks/edited-animal.previs");Directory.CreateDirectory(Path.GetDirectoryName(portable)!);var timing=entry.Data.MotionClips.Select(clip=>(clip.Id,clip.SourceAnimation,clip.Start,clip.Duration,clip.SourceOffset,clip.Speed,clip.BlendIn,clip.Loop,clip.InPlace)).ToArray();SaveTo(portable);var beforeSave=native.CaptureEvaluatedPose();LoadProjectFromPath(portable);SetTime(.65);entry=_entries.Single(e=>e.Data.Id==id);native=entry.NativeTrack!;
            Check(PoseCorrectionRuntime.ChangedBones(beforeSave,native.CaptureEvaluatedPose()).Count==0&&timing.SequenceEqual(entry.Data.MotionClips.Select(clip=>(clip.Id,clip.SourceAnimation,clip.Start,clip.Duration,clip.SourceOffset,clip.Speed,clip.BlendIn,clip.Loop,clip.InPlace))),"native keys, source motion and final pose survive portable save/reopen");
            Select(entry);var saved=SaveAnimalCapturedPose("Animal validation "+Guid.NewGuid().ToString("N"));savedId=saved.Id;
            Check(saved.Pose.RigModelId==entry.Data.LibraryModelId&&SavedPoseLibrary.Load().Any(pose=>pose.Id==savedId),"native poses save to My poses with an explicit compatible model ID");
            var human=AddCatalogModel(ContentCatalog.Find("human-average")!,Vector3.Zero,false);var horse=AddCatalogModel(ContentCatalog.Find("animal-horse")!,Vector3.Zero,false);
            Check(!CompatiblePose(human,saved)&&!CompatiblePose(horse,saved)&&CompatiblePose(entry,saved),"native saved poses refuse humans and unrelated animal rigs");
            ApplyPoseToEntry(entry,saved);SetTime(0);var staticPose=native.CaptureEvaluatedPose();SetTime(5);
            Check(entry.Data.MotionClips.Count==0&&PoseCorrectionRuntime.ChangedBones(staticPose,native.CaptureEvaluatedPose()).Count==0,"a native library pose is a reusable static fallback without motion dependencies");
            var snapshot=Snapshot();Restore(snapshot,"");entry=_entries.Single(e=>e.Data.Id==id);native=entry.NativeTrack!;SetTime(5);Check(entry.Data.StaticPose?.RigModelId=="animal-wolf"&&PoseCorrectionRuntime.ChangedBones(staticPose,native.CaptureEvaluatedPose()).Count==0,"native static poses retain compatibility and bone rotations through project restore");
            foreach(var extra in _entries.Where(item=>item.Data.Id!=id).ToArray()){_entries.Remove(extra);_world.RemoveChild(extra.Node);extra.Node.QueueFree();}foreach(var camera in _cameras)camera.Rig.Visible=false;RefreshObjects();
            Select(entry);StartPoseEditing();SelectPoseBone(native.DefaultPoseBone);SetPoseTool("Rotate");BeginPoseGesture();UpdatePoseBoneRotation(native.GetPoseBoneRotationDegrees(_poseBone)+new Vector3(0,12,0));EndPoseGesture();_timeline.ExpandPoseCorrections(id);
            if(DisplayServer.GetName()!="headless")
            {
                for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Directory.CreateDirectory("output");GetViewport().GetTexture().GetImage().SavePng("output/animal-pose-editor.png");Check(_poseViewportOverlay?.Visible==true,"native pose handles render in the actual application viewport");
            }
            passed=true;
        }
        catch(Exception ex){failure=ex.Message;throw;}
        finally
        {
            if(savedId!=null){SavedPoseLibrary.Delete(savedId);_userPoses.RemoveAll(pose=>pose.Id==savedId);}
            StopPoseEditing();Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_undo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);_redo.Clear();foreach(var item in redo.Reverse())_redo.Push(item);
            Directory.CreateDirectory("output");File.WriteAllText("output/animal-pose-report.json",JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));
        }
        GD.Print("ANIMAL_POSE_OK checks="+checks);return checks;
    }
    async Task RunAnimalPoseChecksAndQuit(){try{await RunAnimalPoseChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("ANIMAL_POSE_FAILED "+ex);GetTree().Quit(1);}}
}
