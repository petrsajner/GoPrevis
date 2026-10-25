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
    /// <summary>Real manual editing, IK, gesture history, seating and reusable-preset regression.</summary>
    public async Task RunManualPoseChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;bool wasPlaying=_playing;
        var undo=_undo.ToArray();var redo=_redo.ToArray();bool originalMode=_poseEditing;string originalBone=_poseBone,originalTool=_poseTool;
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();var presetIds=new HashSet<string>();
        void Check(bool condition,string description){observations.Add(new{check=description,passed=condition});Require(condition,"Manual pose: "+description);checks++;}
        async Task Settle(){for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        static float RotationAgreement(Quaternion a,Quaternion b)=>Math.Abs(a.Normalized().Dot(b.Normalized()));
        static bool SameRotation(Quaternion a,Quaternion b)=>RotationAgreement(a,b)>.9999f;
        void Store(Entry entry,RiggedActor actor)=>entry.Data.StaticPose=actor.GetStaticPoseSnapshot();
        try
        {
            _playing=false;await Settle();ClearObjects();SetTime(0);
            var source=PoseCatalog.Find("pose-sit-upright")!;DropPose(source.Id,null,Vector3.Zero);
            var entry=_selected!;var actor=(RiggedActor)entry.Node;var skeleton=Descendants(actor).OfType<Skeleton3D>().Single();
            StartPoseEditing();Check(_poseEditing,"a prepared human can enter the pose editor");
            Check(actor.PoseBoneNames.Count==21&&actor.PoseBoneNames.Contains("Head")&&actor.PoseBoneNames.Contains("LeftHand")&&actor.PoseBoneNames.Contains("RightFoot"),"head, spine and both limb chains are editable");

            var originalBones=CaptureLibraryBonePose(entry);var leftFoot=actor.GetPoseJointPosition("LeftFoot");var rightFoot=actor.GetPoseJointPosition("RightFoot");
            var transform=entry.Node.Transform;int head=skeleton.FindBone("Head");var headRotation=actor.GetPoseBoneRotationDegrees("Head");
            actor.SetPoseBoneRotationDegrees("Head",headRotation+new Vector3(12,32,-6));
            var edited=CaptureLibraryBonePose(entry);
            Check(!originalBones[head].IsEqualApprox(edited[head]),"head rotation changes the selected local joint");
            Check(Enumerable.Range(0,edited.Length).Where(index=>index!=head).All(index=>originalBones[index].IsEqualApprox(edited[index])),"turning the head leaves every other local bone untouched");
            Check(actor.GetPoseJointPosition("LeftFoot").DistanceTo(leftFoot)<.00001f&&actor.GetPoseJointPosition("RightFoot").DistanceTo(rightFoot)<.00001f&&entry.Node.Transform.IsEqualApprox(transform),"head editing leaves feet and the scene-object transform fixed");
            var headBefore=actor.GetPoseJointPosition("Head");actor.SetPoseBoneRotationDegrees("Chest",actor.GetPoseBoneRotationDegrees("Chest")+new Vector3(8,10,0));
            Check(actor.GetPoseJointPosition("Head").DistanceTo(headBefore)>.005f&&actor.GetPoseJointPosition("LeftFoot").DistanceTo(leftFoot)<.00001f,"torso editing moves its head subtree without dragging the legs");

            foreach(string end in new[]{"LeftHand","RightHand","LeftFoot","RightFoot"})
            {
                actor.SetStaticPose(source.Pose);string middle=actor.GetPoseBoneParentName(end)!,start=actor.GetPoseBoneParentName(middle)!;
                Vector3 a=actor.GetPoseJointPosition(start),b=actor.GetPoseJointPosition(middle),c=actor.GetPoseJointPosition(end);
                float first=a.DistanceTo(b),second=b.DistanceTo(c);var endOrientation=actor.GetPoseJointGlobalRotation(end);
                Vector3 target=a+(c-a)*.88f+new Vector3(end.StartsWith("Left",StringComparison.Ordinal)?.015f:-.015f,.015f,-.015f);
                Vector3 actual=actor.MovePoseEndEffector(end,target);
                Check(actual.DistanceTo(target)<.002f&&actor.GetPoseJointPosition(end).DistanceTo(target)<.002f,end+" IK reaches an attainable target");
                Check(Math.Abs(actor.GetPoseJointPosition(start).DistanceTo(actor.GetPoseJointPosition(middle))-first)<.0001f&&Math.Abs(actor.GetPoseJointPosition(middle).DistanceTo(actor.GetPoseJointPosition(end))-second)<.0001f,end+" IK preserves both bone lengths");
                Check(SameRotation(endOrientation,actor.GetPoseJointGlobalRotation(end)),end+" IK retains the hand or foot orientation");
                var nearPose=CaptureLibraryBonePose(entry);actor.MovePoseEndEffector(end,target);
                Check(nearPose.Zip(CaptureLibraryBonePose(entry)).All(pair=>pair.First.IsEqualApprox(pair.Second)),end+" repeated target does not flip the bend or accumulate drift");
                Vector3 far=a+(target-a).Normalized()*20;actual=actor.MovePoseEndEffector(end,far);
                Check(actual.DistanceTo(a)<=first+second+.0001f&&CaptureLibraryBonePose(entry).All(PoseTransformFinite),end+" unreachable extension clamps to finite fixed-length geometry");
                actual=actor.MovePoseEndEffector(end,a);
                Check(actual.DistanceTo(a)>=Math.Abs(first-second)-.0001f&&actual.DistanceTo(a)<Math.Abs(first-second)+.002f&&CaptureLibraryBonePose(entry).All(PoseTransformFinite),end+" fully folded target remains finite and respects its minimum reach");
            }
            actor.SetStaticPose(source.Pose);var initialOffset=actor.GetPoseRootOffset();leftFoot=actor.GetPoseJointPosition("LeftFoot");rightFoot=actor.GetPoseJointPosition("RightFoot");
            var desired=initialOffset+new Vector3(.02f,-.04f,-.01f);actor.MovePosePelvis(desired,true);
            Check(actor.GetPoseRootOffset().DistanceTo(desired)<.002f,"pelvis moves to a reachable absolute seating offset");
            Check(actor.GetPoseJointPosition("LeftFoot").DistanceTo(leftFoot)<.001f&&actor.GetPoseJointPosition("RightFoot").DistanceTo(rightFoot)<.001f,"pelvis adjustment keeps both feet planted");
            actor.MovePosePelvis(new Vector3(20,20,20),true);
            Check(actor.GetPoseRootOffset().DistanceTo(new Vector3(20,20,20))>1&&actor.GetPoseJointPosition("LeftFoot").DistanceTo(leftFoot)<.002f&&actor.GetPoseJointPosition("RightFoot").DistanceTo(rightFoot)<.002f&&CaptureLibraryBonePose(entry).All(PoseTransformFinite),"unreachable pelvis movement clamps instead of stretching legs or sliding feet");
            actor.SetStaticPose(source.Pose);var beforeInvalid=CaptureLibraryBonePose(entry);bool rejected=false;
            try{actor.SetPoseBoneRotationDegrees("Head",new Vector3(float.NaN,0,0));}catch(ArgumentException){rejected=true;}
            Check(rejected&&LibraryPoseEqual(beforeInvalid,CaptureLibraryBonePose(entry)),"invalid manual rotation fails atomically");
            rejected=false;try{actor.MovePoseEndEffector("LeftHand",new Vector3(0,float.PositiveInfinity,0));}catch(ArgumentException){rejected=true;}
            Check(rejected&&LibraryPoseEqual(beforeInvalid,CaptureLibraryBonePose(entry)),"invalid IK target fails atomically");

            // Exercise the application callbacks, including a scaled and rotated actor.
            actor.SetStaticPose(source.Pose);Store(entry,actor);entry.Data.Position=new float[]{1.25f,.15f,-.5f};entry.Data.Rotation=new float[]{0,37,0};entry.Data.Scale=new float[]{.9f,1.1f,1.2f};SetTime(0);Select(entry);StartPoseEditing();SelectPoseBone("RightHand");
            transform=entry.Node.Transform;Vector3 localTarget=actor.GetPoseJointPosition("RightHand")+new Vector3(-.03f,.03f,-.02f),worldTarget=entry.Node.GlobalTransform*localTarget;
            int undoCount=_undo.Count;BeginPoseGesture();UpdatePoseEffectorWorld("RightHand",worldTarget);EndPoseGesture();
            Check((entry.Node.GlobalTransform*actor.GetPoseJointPosition("RightHand")).DistanceTo(worldTarget)<.004f,"world-space hand editing accounts for object rotation and nonuniform body scale");
            Check(entry.Node.Transform.IsEqualApprox(transform)&&entry.Data.StaticPose!=null&&_undo.Count==undoCount+1,"one manual drag stores one undo item and leaves the object transform unchanged");

            string entryId=entry.Data.Id;var beforeTurn=CaptureLibraryBonePose(entry);SelectPoseBone("Head");var turn=actor.GetPoseBoneRotationDegrees("Head")+new Vector3(0,22,0);undoCount=_undo.Count;
            BeginPoseGesture();UpdatePoseBoneRotation(turn);UpdatePoseBoneRotation(turn+new Vector3(0,3,0));EndPoseGesture();var afterTurn=CaptureLibraryBonePose(entry);
            Check(!LibraryPoseEqual(beforeTurn,afterTurn)&&_undo.Count==undoCount+1,"multiple rotation updates form a single undo gesture");
            Undo();entry=_entries.First(e=>e.Data.Id==entryId);actor=(RiggedActor)entry.Node;
            Check(LibraryPoseEqual(beforeTurn,CaptureLibraryBonePose(entry)),"undo restores the complete previous pose");
            Redo();entry=_entries.First(e=>e.Data.Id==entryId);actor=(RiggedActor)entry.Node;
            Check(LibraryPoseEqual(afterTurn,CaptureLibraryBonePose(entry)),"redo restores the edited pose");

            // Exercise the actual screen-plane drag path, rather than an IK-only substitute.
            Select(entry);StartPoseEditing();SelectPoseBone("RightHand");_poseTool="Move";FrameSelected();await Settle();
            Vector3 beforeHand=actor.GetPoseJointPosition("RightHand");Vector2 point=_editorCamera.UnprojectPosition(entry.Node.GlobalTransform*beforeHand);transform=entry.Node.Transform;undoCount=_undo.Count;
            bool handled=HandlePoseViewportInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
            handled&=HandlePoseViewportInput(new InputEventMouseMotion{Position=point+new Vector2(18,-12),Relative=new Vector2(18,-12)});
            handled&=HandlePoseViewportInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point+new Vector2(18,-12)});
            Check(handled&&actor.GetPoseJointPosition("RightHand").DistanceTo(beforeHand)>.005f,"viewport hand-marker drag edits the real limb");
            Check(entry.Node.Transform.IsEqualApprox(transform)&&_undo.Count==undoCount+1,"viewport pose drag neither moves the whole object nor creates per-frame undo entries");

            // Grab visible palm/foot geometry away from joint dots, exactly as a user does.
            var editorTransform=_editorCamera.Transform;float editorFov=_editorCamera.Fov;
            foreach(string end in new[]{"RightHand","RightFoot"})
            {
                Select(entry);StartPoseEditing();SelectPoseBone("Head");_poseTool="Rotate";
                Vector3 jointWorld=entry.Node.GlobalTransform*actor.GetPoseJointPosition(end);
                _editorCamera.Fov=35;_editorCamera.GlobalPosition=jointWorld+actor.GlobalBasis.Orthonormalized()*new Vector3(-.85f,.45f,-1.05f);_editorCamera.LookAt(jointWorld);await Settle();
                Vector2 jointScreen=PoseViewportToInput(_editorCamera.UnprojectPosition(jointWorld)),bodyPoint=Vector2.Zero;bool found=false;var bodyPickAttempts=new List<object>();
                foreach(var attachment in Descendants(actor).OfType<BoneAttachment3D>().Where(a=>a.BoneName==end))
                {
                    foreach(var mesh in Descendants(attachment).OfType<MeshInstance3D>().Where(m=>end.EndsWith("Hand",StringComparison.Ordinal)?m.Mesh is CapsuleMesh:m.Mesh is BoxMesh))
                    {
                        var bounds=mesh.GetAabb();var axes=end.EndsWith("Hand",StringComparison.Ordinal)?new[]{Vector3.Up*bounds.Size.Y}:new[]{Vector3.Back*bounds.Size.Z,Vector3.Right*bounds.Size.X};
                        foreach(var axis in axes)
                        {
                            foreach(float fraction in new[]{0f,.25f,-.25f,.34f,-.34f})
                            {
                                Vector3 world=mesh.GlobalTransform*(bounds.GetCenter()+axis*fraction);
                                var candidate=PoseViewportToInput(_editorCamera.UnprojectPosition(world));string? marker=PoseViewportHitBone(actor,candidate),body=PoseViewportHitBody(actor,candidate);int hitAxis=PoseViewportHitAxis(actor,candidate,false);
                                bodyPickAttempts.Add(new{mesh=mesh.Name.ToString(),point=new[]{candidate.X,candidate.Y},marker,hitAxis,body});
                                if(candidate.DistanceTo(jointScreen)>12&&hitAxis<0&&body==end){bodyPoint=candidate;found=true;break;}
                            }
                            if(found)break;
                        }
                        if(found)break;
                    }
                    if(found)break;
                }
                var wristPoint=jointScreen;
                if(!found){var diagnostic=new{bodyPick=end,jointPoint=new[]{wristPoint.X,wristPoint.Y},bodyPickAttempts};observations.Add(diagnostic);GD.Print("MANUAL_POSE_BODY_PICK_DIAGNOSTIC "+JsonSerializer.Serialize(diagnostic));}
                Check(found&&bodyPoint.DistanceTo(wristPoint)>12,end+" can be grabbed on its actual mesh outside its own joint marker");
                observations.Add(new{grabbedBody=end,bodyPoint=new[]{bodyPoint.X,bodyPoint.Y},ownJointDistance=bodyPoint.DistanceTo(wristPoint),overlappingMarker=PoseViewportHitBone(actor,bodyPoint)});
                string middle=actor.GetPoseBoneParentName(end)!,upper=actor.GetPoseBoneParentName(middle)!;var currentSkeleton=actor.GetNode<Skeleton3D>("PreparedHumanoid");
                var localRotations=new[]{upper,middle,end}.Select(name=>currentSkeleton.GetBonePoseRotation(currentSkeleton.FindBone(name))).ToArray();
                float firstLength=actor.GetPoseJointPosition(upper).DistanceTo(actor.GetPoseJointPosition(middle)),secondLength=actor.GetPoseJointPosition(middle).DistanceTo(actor.GetPoseJointPosition(end));
                Vector3 beforePosition=actor.GetPoseJointPosition(end);transform=entry.Node.Transform;undoCount=_undo.Count;
                handled=_sceneContainer.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=bodyPoint})==Godot.Error.Ok;
                Check(actor.GetPoseJointPosition(end).DistanceTo(beforePosition)<.00001f,end+" mesh grab starts without snapping its joint to the cursor");
                var drag=new Vector2(32,-22);handled&=_sceneContainer.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseMotion{Position=bodyPoint+drag,Relative=drag})==Godot.Error.Ok;handled&=_sceneContainer.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=bodyPoint+drag})==Godot.Error.Ok;
                var afterPoint=PoseViewportToInput(_editorCamera.UnprojectPosition(entry.Node.GlobalTransform*actor.GetPoseJointPosition(end)));
                Check(handled&&_poseBone==end&&_poseTool=="Move"&&(afterPoint-wristPoint).DistanceTo(drag)<3,end+" body drag follows the mouse through the whole-chain IK route");
                Check(new[]{upper,middle,end}.Select((name,index)=>RotationAgreement(localRotations[index],currentSkeleton.GetBonePoseRotation(currentSkeleton.FindBone(name)))<.999999f).All(changed=>changed),end+" drag updates shoulder/hip, elbow/knee and wrist/ankle rotations together");
                Check(Math.Abs(firstLength-actor.GetPoseJointPosition(upper).DistanceTo(actor.GetPoseJointPosition(middle)))<.0001f&&Math.Abs(secondLength-actor.GetPoseJointPosition(middle).DistanceTo(actor.GetPoseJointPosition(end)))<.0001f,end+" body drag keeps both limb lengths fixed");
                Check(entry.Node.Transform.IsEqualApprox(transform)&&_undo.Count==undoCount+1,end+" body drag leaves the whole figure fixed and adds only one undo item");
            }
            _editorCamera.Transform=editorTransform;_editorCamera.Fov=editorFov;

            // Manually placed hips must survive custom-preset capture, including X and Z.
            var offset=actor.GetPoseRootOffset()+new Vector3(.045f,.015f,-.035f);actor.SetPoseRootOffset(offset);Store(entry,actor);SetTime(0);
            var editedPose=CaptureLibraryBonePose(entry);Select(entry);var preset=SaveCapturedPose("Manual pose check "+Guid.NewGuid().ToString("N"));presetIds.Add(preset.Id);
            Check(SavedPoseLibrary.Load().Any(p=>p.Id==preset.Id)&&Vector(preset.Pose.RootPosition).DistanceTo(offset)<.0001f,"saved custom pose retains deliberate XYZ pelvis placement");
            DropPose(preset.Id,entry,entry.Node.Position);SetTime(3);
            Check(LibraryPoseEqual(editedPose,CaptureLibraryBonePose(entry)),"custom edited pose reapplies without a source motion file");
            var child=AddCatalogModel(ContentCatalog.Find("human-child")!,new Vector3(-2,0,0),false);var childScale=child.Node.Scale;DropPose(preset.Id,child,child.Node.Position);
            Check(child.Node.Scale.IsEqualApprox(childScale)&&((RiggedActor)child.Node).GetPoseRootOffset().DistanceTo(offset)<.0001f,"custom edited pose retains target figure proportions");
            _entries.Remove(child);_world.RemoveChild(child.Node);child.Node.QueueFree();Select(entry);

            // Entering corrections must preserve the live source and the visible frame.
            entry.Data.Position=new float[]{.5f,0,.5f};entry.Data.Rotation=new float[]{0,25,0};entry.Data.Scale=new float[]{1,1,1};entry.Data.Path=new();entry.Data.TransformAnimation=new();SetTime(0);DropMotion("cmu-02-01",entry,entry.Node.Position);SetTime(.9);Select(entry);
            var hipWorld=entry.Node.GlobalTransform*actor.GetPoseJointPosition("Hips");var liveBasis=entry.Node.GlobalBasis;var liveRotations=CaptureLibraryBonePose(entry).Select(p=>p.Basis.GetRotationQuaternion()).ToArray();
            rejected=false;try{actor.SetPoseBoneRotationDegrees("Head",Vector3.Zero);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"the rig prevents changing a live motion source outside correction staging");
            var liveClips=JsonSerializer.Serialize(entry.Data.MotionClips);var livePath=entry.Data.MotionPath;var liveBase=JsonSerializer.Serialize(entry.Data.StaticPose);
            StartPoseEditing();if(entry.Data.MotionClips.Count>0){BeginPoseGesture();EndPoseGesture();}
            Check(entry.Data.MotionClips.Count>0&&JsonSerializer.Serialize(entry.Data.MotionClips)==liveClips&&entry.Data.MotionPath==livePath&&JsonSerializer.Serialize(entry.Data.StaticPose)==liveBase,"entering the correction editor retains source clips, asset path and base pose unchanged");
            Check((entry.Node.GlobalTransform*actor.GetPoseJointPosition("Hips")).DistanceTo(hipWorld)<.001f&&entry.Node.GlobalBasis.IsEqualApprox(liveBasis),"entering live corrections preserves the visible figure's world placement");
            Check(liveRotations.Zip(CaptureLibraryBonePose(entry)).All(pair=>SameRotation(pair.First,pair.Second.Basis.GetRotationQuaternion())),"entering live corrections retains every displayed local bone rotation");

            entry.Data.Path=new ActorPathData{Enabled=true,OrientToPath=true,Duration=8,Points=new(){new float[]{-1,0,1},new float[]{1,0,2},new float[]{2,0,0}}};SetTime(0);DropMotion("cmu-02-01",entry,entry.Node.Position);SetTime(.75);Select(entry);
            var pathWorldJoints=actor.PoseBoneNames.ToDictionary(name=>name,name=>entry.Node.GlobalTransform*actor.GetPoseJointPosition(name));
            StartPoseEditing();if(entry.Data.MotionClips.Count>0){BeginPoseGesture();EndPoseGesture();}
            Check(entry.Data.Path.Enabled&&pathWorldJoints.All(pair=>(entry.Node.GlobalTransform*actor.GetPoseJointPosition(pair.Key)).DistanceTo(pair.Value)<.001f),"entering path-driven corrections preserves every displayed joint, source clip and path layer");
            ApplyPoseToEntry(entry,source);SetTime(.75);Select(entry);StartPoseEditing();SelectPoseBone("Hips");_poseTool="Rotate";transform=entry.Node.Transform;
            Vector3 beforeFacing=entry.Node.GlobalBasis*(actor.GetPoseJointGlobalRotation("Hips")*Vector3.Forward);var hipsRotation=actor.GetPoseBoneRotationDegrees("Hips");
            BeginPoseGesture();UpdatePoseBoneRotation(hipsRotation+new Vector3(0,30,0));EndPoseGesture();Vector3 afterFacing=entry.Node.GlobalBasis*(actor.GetPoseJointGlobalRotation("Hips")*Vector3.Forward);
            Check(entry.Node.Transform.IsEqualApprox(transform)&&Math.Abs(Mathf.RadToDeg(beforeFacing.AngleTo(afterFacing))-30)<.5f,"manual pelvis yaw remains visible above path heading without rotating the scene object");

            // Fit a real imported chair by adjusting hips while keeping its feet on the ground.
            ApplyPoseToEntry(entry,source);entry.Data.Position=new float[3];entry.Data.Rotation=new float[3];entry.Data.Scale=new float[]{1,1,1};entry.Data.Path=new();entry.Data.TransformAnimation=new();SetTime(0);
            var chair=AddCatalogModel(ContentCatalog.Find("kenney-furniture-chair")!,Vector3.Zero,false);chair.Data.Rotation=new float[]{0,180,0};chair.Node.RotationDegrees=new Vector3(0,180,0);
            var chairBounds=PropFactory.CalculateBounds(chair.Node);Vector3 center=chair.Node.GlobalTransform*chairBounds.GetCenter();entry.Data.Position=new[]{center.X,0,center.Z};SetTime(0);await Settle();await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var ray=PhysicsRayQueryParameters3D.Create(new Vector3(center.X,.75f,center.Z),new Vector3(center.X,.1f,center.Z),1);ray.Exclude=new Godot.Collections.Array<Rid>{actor.GetNode<StaticBody3D>("PickBody").GetRid()};
            var hit=_world.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            Check(hit.Count>0,"a real imported chair exposes a seat surface for precise blocking");
            float seatY=hit["position"].AsVector3().Y;leftFoot=entry.Node.GlobalTransform*actor.GetPoseJointPosition("LeftFoot");rightFoot=entry.Node.GlobalTransform*actor.GetPoseJointPosition("RightFoot");
            var rootOffset=actor.GetPoseRootOffset();rootOffset.Y=seatY+.113f-.94f;actor.MovePosePelvis(rootOffset,true);Store(entry,actor);SetTime(0);await Settle();
            float supportY=(entry.Node.GlobalTransform*actor.GetPoseJointPosition("LeftUpperLeg")).Y-.068f;
            Check(Math.Abs(supportY-seatY)<.03f&&(entry.Node.GlobalTransform*actor.GetPoseJointPosition("LeftFoot")).DistanceTo(leftFoot)<.002f&&(entry.Node.GlobalTransform*actor.GetPoseJointPosition("RightFoot")).DistanceTo(rightFoot)<.002f,"pelvis can meet a specific chair seat while both feet remain planted");
            actor.SetPoseBoneRotationDegrees("Head",new Vector3(-5,24,0));actor.MovePoseEndEffector("RightHand",actor.GetPoseJointPosition("RightHand")+new Vector3(-.025f,.025f,-.02f));Store(entry,actor);SetTime(0);

            Select(entry);DuplicateSelected();var duplicate=_selected!;Check(duplicate.Data.StaticPose!=null&&LibraryPoseEqual(CaptureLibraryBonePose(entry),CaptureLibraryBonePose(duplicate))&&!ReferenceEquals(entry.Data.StaticPose,duplicate.Data.StaticPose),"duplicating a manually adjusted figure retains an independent complete pose");
            _entries.Remove(duplicate);_world.RemoveChild(duplicate.Node);duplicate.Node.QueueFree();Select(entry);SetTime(0);await Settle();
            var savedPose=CaptureLibraryBonePose(entry);var savedTransform=entry.Node.Transform;var savedRoot=actor.GetPoseRootOffset();string id=entry.Data.Id;
            string directory=ProjectSettings.GlobalizePath("user://manual-pose-checks");Directory.CreateDirectory(directory);string project=Path.Combine(directory,"manual-adjusted-pose.goprevis");SaveTo(project);LoadProjectFromPath(project);SetTime(4);await Settle();entry=_entries.First(e=>e.Data.Id==id);actor=(RiggedActor)entry.Node;
            Check(LibraryPoseEqual(savedPose,CaptureLibraryBonePose(entry))&&entry.Node.Transform.IsEqualApprox(savedTransform)&&actor.GetPoseRootOffset().DistanceTo(savedRoot)<.0001f,"manual joint and pelvis edits survive portable save/reopen and time changes");
            Check(entry.Data.StaticPose!=null&&entry.Data.MotionClips.Count==0&&string.IsNullOrEmpty(entry.Data.MotionPath),"manually edited scenes remain static and source independent");
            Select(chair=_entries.First(e=>e.Data.LibraryModelId=="kenney-furniture-chair"));StartPoseEditing();Check(!_poseEditing,"static props do not expose a humanoid pose editor");
            SelectCamera(_cameras[0]);StartPoseEditing();Check(!_poseEditing,"cameras do not expose a humanoid pose editor");

            if(DisplayServer.GetName()!="headless")
            {
                var camera=_cameras[0];SetActiveCamera(camera.Id);camera.Rig.ClearKeys();camera.Rig.SetPose(new Vector3(center.X+2.2f,1.7f,center.Z-3.2f),new Vector3(center.X,.8f,center.Z));SyncCameraFromRig(camera);SetTime(0);await Settle();SaveTo(project);
                string image=ProjectSettings.GlobalizePath("res://output/manual-pose-chair.png");Directory.CreateDirectory(Path.GetDirectoryName(image)!);await RenderFrame(image);Check(Image.LoadFromFile(image).GetWidth()==1920,"adjusted pose and real chair render as a full-HD camera reference");
            }
            passed=true;GD.Print("MANUAL_POSE_OK checks="+checks);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally
        {
            EndPoseGesture();foreach(string id in presetIds){SavedPoseLibrary.Delete(id);_userPoses.RemoveAll(p=>p.Id==id);}
            string report=ProjectSettings.GlobalizePath("res://output/manual-pose-workflow-report.json");Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,JsonSerializer.Serialize(new{status=passed?"passed":"failed",timestampUtc=DateTime.UtcNow,checksPassed=checks,failure,observations},new JsonSerializerOptions{WriteIndented=true}));
            _poseEditing=originalMode;_poseBone=originalBone;_poseTool=originalTool;Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_playing=wasPlaying;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
}
