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
    public async Task RunPoseCorrectionChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();
        int checks=0,timelineChecks=0;bool passed=false;string? failure=null;var observations=new List<object>();var presets=new HashSet<string>();
        void Check(bool value,string message){observations.Add(new{message,passed=value});Require(value,"Pose corrections: "+message);checks++;}
        async Task Settle(){for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        static bool Same(Quaternion a,Quaternion b)=>Math.Abs(a.Normalized().Dot(b.Normalized()))>.9999f;
        try
        {
            _playing=false;StopPoseEditing();ClearObjects();_duration=8;_scrub.MaxValue=8;SetTime(0);await Settle();
            var entry=AddObject("actor",Vector3.Zero,"Corrected performer");string actorId=entry.Data.Id;var actor=(RiggedActor)entry.Node;
            var motion=MotionCatalog.Find("human-walk-gesture")!;AppendMotion(entry,MotionCatalog.MaterializeSource(motion),motion.SourceScale);Select(entry);SetTime(2);await Settle();
            string clipsBefore=JsonSerializer.Serialize(entry.Data.MotionClips);var skeleton=actor.GetNode<Skeleton3D>("PreparedHumanoid");int head=skeleton.FindBone("Head");
            var baseHeadAtTwo=skeleton.GetBonePoseRotation(head);var transform=entry.Node.Transform;StartPoseEditing();
            Check(entry.Data.MotionClips.Count==1&&JsonSerializer.Serialize(entry.Data.MotionClips)==clipsBefore,"entering pose editing retains the original mocap clips and their timing");
            Check(entry.Data.StaticPose==null&&entry.Node.Transform.IsEqualApprox(transform),"entering correction mode neither bakes a static pose nor relocates the figure");
            SelectPoseBone("Head");int history=_undo.Count;var angles=actor.GetPoseBoneRotationDegrees("Head");BeginPoseGesture();UpdatePoseBoneRotation(angles+new Vector3(8,30,-4));EndPoseGesture();
            var editedHead=skeleton.GetBonePoseRotation(head);var delta=(baseHeadAtTwo.Inverse()*editedHead).Normalized();
            Check(!Same(baseHeadAtTwo,editedHead)&&entry.Data.MotionClips.Count==1&&entry.Data.StaticPose==null,"turning the head creates a correction while source motion remains active");
            Check(entry.Data.PoseCorrections.Bones.TryGetValue("Head",out var headKeys)&&headKeys.Count==2&&headKeys.Any(k=>Math.Abs(k.Time)<.000001)&&headKeys.Any(k=>Math.Abs(k.Time-2)<.000001),"the first late head edit creates one identity-at-zero key and one current-frame key");
            Check(entry.Data.PoseCorrections.Bones["Head"].All(k=>k.Rotation.Length==4&&Math.Abs(PoseCorrectionRuntime.ReadRotation(k.Rotation).LengthSquared()-1)<.0001f),"each joint key stores one finite normalized quaternion for all rotation axes");
            Check(_undo.Count==history+1,"a pose-correction gesture creates exactly one undo step");
            var standing=AddObject("actor",new Vector3(3,0,0),"Unanimated correction");var standingActor=(RiggedActor)standing.Node;var standingSkeleton=standingActor.GetNode<Skeleton3D>("PreparedHumanoid");int standingHead=standingSkeleton.FindBone("Head");var neutralHead=standingSkeleton.GetBonePoseRotation(standingHead);
            standing.Data.PoseCorrections.Bones["Head"]=new();PoseCorrectionRuntime.UpsertRotation(standing.Data.PoseCorrections.Bones["Head"],0,Quaternion.Identity);PoseCorrectionRuntime.UpsertRotation(standing.Data.PoseCorrections.Bones["Head"],2,headTurnForDefault());
            Quaternion headTurnForDefault()=>new(Vector3.Up,Mathf.DegToRad(40));
            SetTime(2);var standingOnce=standingSkeleton.GetBonePoseRotation(standingHead);SetTime(2);var standingTwice=standingSkeleton.GetBonePoseRotation(standingHead);SetTime(0);SetTime(2);
            Check(Same(standingOnce,standingTwice)&&Same(standingOnce,standingSkeleton.GetBonePoseRotation(standingHead))&&Same(standingOnce,(neutralHead*headTurnForDefault()).Normalized()),"default unanimated figures reset their neutral source instead of accumulating correction deltas");
            Check(standing.Data.StaticPose==null&&standing.Data.MotionClips.Count==0,"default-figure correction keys do not manufacture a static-pose or motion source");
            _entries.Remove(standing);_world.RemoveChild(standing.Node);standing.Node.QueueFree();RefreshObjects();Select(entry);
            var source=new RiggedActor();_world.AddChild(source);source.SetMotionClips(entry.Data.MotionClips);
            foreach(double sample in new[]{0d,1,2})
            {
                SetTime(sample);source.EvaluateTimeline(sample);var sourceSkeleton=source.GetNode<Skeleton3D>("PreparedHumanoid");var amount=Quaternion.Identity.Slerp(delta,(float)(sample/2));var expected=(sourceSkeleton.GetBonePoseRotation(head)*amount).Normalized();
                Check(Same(expected,skeleton.GetBonePoseRotation(head)),"head interpolation composes above the source at "+sample+" seconds");
                Check(Enumerable.Range(0,skeleton.GetBoneCount()).Where(i=>i!=head).All(i=>Same(sourceSkeleton.GetBonePoseRotation(i),skeleton.GetBonePoseRotation(i))),"unkeyed joints retain the source pose at "+sample+" seconds");
            }
            Undo();entry=_entries.Single(e=>e.Data.Id==actorId);actor=(RiggedActor)entry.Node;
            Check(!PoseCorrectionRuntime.HasKeys(entry.Data.PoseCorrections)&&entry.Data.MotionClips.Count==1,"Undo removes the correction without removing mocap");
            Redo();entry=_entries.Single(e=>e.Data.Id==actorId);actor=(RiggedActor)entry.Node;skeleton=actor.GetNode<Skeleton3D>("PreparedHumanoid");
            Check(entry.Data.PoseCorrections.Bones["Head"].Any(k=>Math.Abs(k.Time-2)<.000001)&&entry.Data.MotionClips.Count==1,"Redo restores the pose key above its existing source");
            Select(entry);StartPoseEditing();SetTime(3);SelectPoseBone("RightHand");var target=actor.GetPoseJointPosition("RightHand")+new Vector3(-.025f,.25f,-.02f);BeginPoseGesture();UpdatePoseEffectorWorld("RightHand",actor.GlobalTransform*target);EndPoseGesture();
            Check(new[]{"RightUpperArm","RightLowerArm","RightHand"}.All(bone=>entry.Data.PoseCorrections.Bones.TryGetValue(bone,out var keys)&&keys.Any(k=>Math.Abs(k.Time-3)<.000001)),"hand IK keys all changed shoulder, elbow and wrist corrections together");
            Check(entry.Data.PoseCorrections.Bones["Head"].Count==2,"editing a hand does not stamp an unrelated head channel");
            Check(actor.GetPoseJointPosition("RightHand").DistanceTo(target)<.002f&&entry.Data.MotionClips.Count==1,"an IK correction reaches its target while its source clip stays intact");
            DropMotion("human-cmu-walk",entry,entry.Node.Position);source.SetMotionClips(entry.Data.MotionClips);var incoming=entry.Data.MotionClips.OrderBy(c=>c.Start).Last();
            for(int i=0;i<=8;i++)
            {
                double sample=incoming.Start+incoming.BlendIn*i/8;SetTime(sample);source.EvaluateTimeline(sample);var sourceSkeleton=source.GetNode<Skeleton3D>("PreparedHumanoid");
                var expected=(sourceSkeleton.GetBonePoseRotation(head)*PoseCorrectionRuntime.EvaluateRotation(entry.Data.PoseCorrections.Bones["Head"],sample)).Normalized();
                Check(Same(expected,skeleton.GetBonePoseRotation(head)),"head correction remains above a source crossfade sample "+i);
                Check(new[]{"LeftUpperArm","LeftLowerArm","LeftUpperLeg","LeftLowerLeg","RightUpperLeg","RightLowerLeg"}.All(bone=>Same(sourceSkeleton.GetBonePoseRotation(sourceSkeleton.FindBone(bone)),skeleton.GetBonePoseRotation(skeleton.FindBone(bone)))),"unkeyed limbs remain sourced through crossfade sample "+i);
            }
            entry.Data.Path=new ActorPathData{Enabled=true,OrientToPath=true,Duration=8,Points=new(){new float[]{-2,0,1},new float[]{0,.3f,2},new float[]{2,.1f,0}}};entry.Data.PathOffset=new[]{.1f,.2f,0};entry.Data.Rotation=new float[]{0,25,0};
            TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Position,0,Vector3.Zero);TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Position,8,new Vector3(.4f,0,0));
            TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Rotation,0,new Vector3(0,10,0));TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Rotation,8,new Vector3(0,50,0));
            entry.Data.PoseCorrections.Bones["Hips"]=new();PoseCorrectionRuntime.UpsertRotation(entry.Data.PoseCorrections.Bones["Hips"],0,Quaternion.Identity);PoseCorrectionRuntime.UpsertRotation(entry.Data.PoseCorrections.Bones["Hips"],4,new Quaternion(Vector3.Up,Mathf.DegToRad(20)));
            TransformLayerRuntime.Upsert(entry.Data.PoseCorrections.Root,0,Vector3.Zero);TransformLayerRuntime.Upsert(entry.Data.PoseCorrections.Root,4,new Vector3(.02f,.05f,0));SetTime(4);source.EvaluateTimeline(4,true);
            var travel=ActorPathRuntime.Evaluate(entry.Data.Path,4);var expectedPosition=travel.Position+Vector(entry.Data.PathOffset)+new Vector3(.2f,0,0);
            Check(entry.Node.Position.DistanceTo(expectedPosition)<.001f,"path travel and keyed object-position corrections remain separate from skeletal corrections");
            var expectedFacing=new Basis(Vector3.Up,Mathf.DegToRad(55))*travel.Tangent;var facing=entry.Node.Basis*source.AnimatedForward;facing.Y=0;expectedFacing.Y=0;
            Check(facing.Normalized().Dot(expectedFacing.Normalized())>.999f&&actor.AnimatedForward.Dot(source.AnimatedForward)>.9999f,"manual hips corrections remain above source-facing metadata and final path/manual heading");
            var sourceRoot=source.GetNode<Skeleton3D>("PreparedHumanoid").GetBonePosePosition(0);Check(skeleton.GetBonePosePosition(0).DistanceTo(sourceRoot+new Vector3(.02f,.05f,0))<.001f,"keyed pelvis offsets are applied after source root filtering without duplicating path travel");
            SetTime(1.25);var reference=CaptureLibraryBonePose(entry);var world=entry.Node.Transform;SetTime(7);SetTime(.2);SetTime(1.25);
            Check(LibraryPoseEqual(reference,CaptureLibraryBonePose(entry))&&entry.Node.Transform.IsEqualApprox(world),"random reverse seeking does not accumulate pose corrections or alter final world placement");
            source.EvaluateTimeline(1.25,true);source.ApplyPoseCorrections(entry.Data.PoseCorrections,1.25);var first=source.CaptureEvaluatedPose();source.ApplyPoseCorrections(entry.Data.PoseCorrections,1.25);var second=source.CaptureEvaluatedPose();
            Check(first.BoneRotations.Keys.All(bone=>Same(PoseCorrectionRuntime.ReadBone(first,bone),PoseCorrectionRuntime.ReadBone(second,bone)))&&!PoseCorrectionRuntime.RootChanged(first,second),"repeating correction evaluation does not apply its delta twice");source.QueueFree();
            var middleActor=new RiggedActor();_world.AddChild(middleActor);
            foreach(var chain in new[]{("RightUpperArm","RightLowerArm","RightHand"),("RightUpperLeg","RightLowerLeg","RightFoot")})
            {
                middleActor.SetStaticPose(PoseCatalog.Find("pose-sit-upright")!.Pose);var rig=middleActor.GetNode<Skeleton3D>("PreparedHumanoid");var beforeMiddle=middleActor.GetPoseJointPosition(chain.Item2);var beforeEnd=middleActor.GetPoseJointPosition(chain.Item3);var localMiddle=rig.GetBonePoseRotation(rig.FindBone(chain.Item2));var localEnd=rig.GetBonePoseRotation(rig.FindBone(chain.Item3));float length=middleActor.GetPoseJointPosition(chain.Item1).DistanceTo(beforeMiddle);
                middleActor.MovePoseJoint(chain.Item2,beforeMiddle+new Vector3(-.1f,.06f,-.08f));
                Check(middleActor.GetPoseJointPosition(chain.Item2).DistanceTo(beforeMiddle)>.005f&&middleActor.GetPoseJointPosition(chain.Item3).DistanceTo(beforeEnd)>.005f,chain.Item2+" drags its descendants rather than pinning its hand or foot");
                Check(Same(localMiddle,rig.GetBonePoseRotation(rig.FindBone(chain.Item2)))&&Same(localEnd,rig.GetBonePoseRotation(rig.FindBone(chain.Item3)))&&Math.Abs(length-middleActor.GetPoseJointPosition(chain.Item1).DistanceTo(middleActor.GetPoseJointPosition(chain.Item2)))<.0001f,chain.Item2+" parent IK keeps child-local poses and bone length intact");
            }
            middleActor.QueueFree();Select(entry);SetTime(2);var shown=actor.CaptureEvaluatedPose();var sourceMetadata=JsonSerializer.Serialize(entry.Data.MotionClips);var correctionMetadata=JsonSerializer.Serialize(entry.Data.PoseCorrections);
            var preset=SaveCapturedPose("Correction check "+Guid.NewGuid().ToString("N"));presets.Add(preset.Id);
            Check(shown.BoneRotations.Keys.All(bone=>Same(PoseCorrectionRuntime.ReadBone(shown,bone),PoseCorrectionRuntime.ReadBone(preset.Pose,bone))),"saving a library pose captures the displayed mocap plus corrections");
            Check(JsonSerializer.Serialize(entry.Data.MotionClips)==sourceMetadata&&JsonSerializer.Serialize(entry.Data.PoseCorrections)==correctionMetadata,"saving a pose leaves source clips and correction animation intact");
            var finalPose=CaptureLibraryBonePose(entry);var finalTransform=entry.Node.Transform;
            string directory=ProjectSettings.GlobalizePath("res://output/pose-corrections");Directory.CreateDirectory(directory);string project=Path.Combine(directory,"keyed-pose.genprevis");SaveTo(project);
            var document=JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(project))!;Check(document.Version==10&&document.Objects.Single(o=>o.Id==actorId).PoseCorrections.Bones.Count>=4,"project format 10 stores source-independent correction tracks alongside mocap");
            LoadProjectFromPath(project);SetTime(2);await Settle();entry=_entries.Single(e=>e.Data.Id==actorId);actor=(RiggedActor)entry.Node;
            Check(entry.Data.MotionClips.Count==2&&LibraryPoseEqual(finalPose,CaptureLibraryBonePose(entry))&&entry.Node.Transform.IsEqualApprox(finalTransform),"source blends, corrections, paths and final object keys survive portable save/reopen");
            var legacy=Snapshot();foreach(var item in legacy.Objects)item.PoseCorrections=new();
            foreach(int version in new[]{1,2,3,4}){legacy.Version=version;Restore(legacy,"");SetTime(1);Check(_entries.All(e=>!PoseCorrectionRuntime.HasKeys(e.Data.PoseCorrections)),"project version "+version+" opens with identity corrections");}
            timelineChecks=await RunPoseKeyTimelineChecks();Check(timelineChecks>=13,"pose correction diamonds pass actual timeline selection, retiming, delete and Undo/Redo checks");
            passed=true;GD.Print("POSE_CORRECTIONS_OK checks="+checks+" timeline="+timelineChecks);
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            StopPoseEditing();foreach(string id in presets){SavedPoseLibrary.Delete(id);_userPoses.RemoveAll(p=>p.Id==id);}string report=ProjectSettings.GlobalizePath("res://output/pose-correction-report.json");Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,timelineChecks,failure,observations},new JsonSerializerOptions{WriteIndented=true}));
            Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
}
