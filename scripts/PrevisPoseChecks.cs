using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    /// <summary>Checks static poses through the actual library, rig, transform and project workflows.</summary>
    public async Task RunPoseLibraryChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;bool playing=_playing;
        var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;bool passed=false;string? failure=null;
        var observations=new List<object>();
        var createdUserPoseIds=new HashSet<string>(StringComparer.Ordinal);
        void Check(bool condition,string description)
        {
            observations.Add(new {check=description,passed=condition});Require(condition,"Static poses: "+description);checks++;
        }
        async Task Settle(){for(int frame=0;frame<2;frame++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        try
        {
            _playing=false;await Settle();ClearObjects();SetTime(0);
            var catalog=PoseCatalog.All;
            Check(catalog.Count>=40,"a broad pose catalog is bundled");
            Check(catalog.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()==catalog.Count,"catalog pose IDs are unique");
            foreach(string category in new[]{"Sitting","Kneeling","Crouching","Lying"})
                Check(catalog.Any(p=>p.Category.Equals(category,StringComparison.OrdinalIgnoreCase)),category+" poses are discoverable");
            var sitting=catalog.First(p=>p.Category.Equals("Sitting",StringComparison.OrdinalIgnoreCase));
            var lying=catalog.First(p=>p.Category.Equals("Lying",StringComparison.OrdinalIgnoreCase));
            var kneeling=catalog.First(p=>p.Category.Equals("Kneeling",StringComparison.OrdinalIgnoreCase));

            DropPose(sitting.Id,null,new Vector3(1.5f,0,.75f));
            var entry=_selected!;var actor=(RiggedActor)entry.Node;var skeleton=Descendants(actor).OfType<Skeleton3D>().Single();
            Check(_entries.Count==1&&entry.Data.ActorFamily=="human"&&entry.Data.LibraryModelId=="human-average","empty-ground pose drop creates a prepared human");
            Check(entry.Data.StaticPose!=null&&entry.Data.MotionClips.Count==0&&string.IsNullOrEmpty(entry.Data.MotionPath),"pose placement creates no motion clip or BVH dependency");
            Check(entry.Node.Position.IsEqualApprox(new Vector3(1.5f,0,.75f)),"pose placement preserves its ground-drop location");

            foreach(var pose in catalog)
            {
                actor.SetStaticPose(pose.Pose);actor.EvaluateTimeline(0);
                var reference=CaptureLibraryBonePose(entry);
                bool finite=reference.Length==21&&reference.All(PoseTransformFinite);
                Check(finite,"finite prepared-rig pose: "+pose.Name);
                actor.EvaluateTimeline(7.25);actor.EvaluateTimeline(.15);actor.EvaluateTimeline(0);
                Check(LibraryPoseEqual(reference,CaptureLibraryBonePose(entry)),"time-independent pose: "+pose.Name);
            }

            DropPose(sitting.Id,entry,entry.Node.Position);SetTime(0);await Settle();
            var seatedBones=CaptureLibraryBonePose(entry);var seatedBounds=PropFactory.CalculateBounds(entry.Node);
            float hipHeight=skeleton.GetBoneGlobalPose(skeleton.FindBone("Hips")).Origin.Y;
            Check(hipHeight>.25f&&hipHeight<.8f,"seated figure has a lowered pelvis suitable for chair placement");
            Check(seatedBounds.Size.Y<1.65f&&seatedBounds.Size.Y>.75f,"seated figure is visibly shorter than a standing figure");
            foreach(double time in new[]{0d,.5,3.25,7.9,1.5}){SetTime(time);Check(LibraryPoseEqual(seatedBones,CaptureLibraryBonePose(entry)),"seated pose holds at scene time "+time);}

            DropPose(lying.Id,entry,entry.Node.Position);await Settle();var lyingBounds=PropFactory.CalculateBounds(entry.Node);
            Check(lyingBounds.Size.Y<.8f&&Math.Max(lyingBounds.Size.X,lyingBounds.Size.Z)>1.25f,"lying figure lies horizontally rather than retaining standing geometry");
            Check(lyingBounds.Position.Y>-.08f,"lying figure does not sink substantially below the floor");
            var payload=new Godot.Collections.Dictionary{{"previs_type","pose"},{"id",sitting.Id}};
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var screenBounds=ProjectedActorBounds(entry);int beforeDrop=_entries.Count;
            var surface=(SceneDropViewport)_sceneContainer;
            Check(surface._CanDropData(screenBounds.GetCenter(),payload),"viewport accepts a pose dragged onto the visible lying actor");
            surface._DropData(screenBounds.GetCenter(),payload);
            Check(_entries.Count==beforeDrop&&entry.Data.StaticPose?.CatalogId==sitting.Id,"viewport pose drop updates the existing actor without creating a duplicate");

            var child=AddCatalogModel(ContentCatalog.Find("human-child")!,new Vector3(-2,0,0),false);var childScale=child.Node.Scale;
            DropPose(kneeling.Id,child,child.Node.Position);
            Check(child.Node.Scale.IsEqualApprox(childScale)&&child.Data.LibraryModelId=="human-child","pose application retains child body proportions");
            var prop=AddObject("chair",new Vector3(3,0,0));int beforeInvalid=_entries.Count;
            DropPose(sitting.Id,prop,prop.Node.Position);
            Check(_entries.Count==beforeInvalid&&prop.Data.StaticPose==null,"static furniture rejects a human skeleton pose");

            DropPose(sitting.Id,entry,entry.Node.Position);
            var storedPose=CaptureLibraryBonePose(entry);SetTime(0);DropMotion("cmu-02-01",entry,entry.Node.Position);SetTime(.65);
            Check(entry.Data.MotionClips.Count==1&&!LibraryPoseEqual(storedPose,CaptureLibraryBonePose(entry)),"a motion source overrides the stored static pose");
            entry.Data.MotionClips.Clear();RebuildTrack(entry);SetTime(2.5);
            Check(entry.Data.StaticPose!=null&&LibraryPoseEqual(storedPose,CaptureLibraryBonePose(entry)),"removing the last motion restores the static pose");

            SetTime(0);DropMotion("cmu-02-01",entry,entry.Node.Position);SetTime(.8);
            var captured=actor.BakeCurrentPose("Captured frame test");
            var animatedBones=CaptureLibraryBonePose(entry);
            Select(entry);var userPose=SaveCapturedPose("Pose check capture "+Guid.NewGuid().ToString("N"));createdUserPoseIds.Add(userPose.Id);
            Check(_userPoses.Any(p=>p.Id==userPose.Id)&&SavedPoseLibrary.Load().Any(p=>p.Id==userPose.Id),"current-frame capture persists a reusable custom preset");
            Check(entry.Data.MotionClips.Count==1&&LibraryPoseEqual(animatedBones,CaptureLibraryBonePose(entry)),"saving a custom pose preserves the selected actor's existing animation");
            ClearSelectedPose();SetTime(.8);
            Check(entry.Data.StaticPose==null&&entry.Data.MotionClips.Count==1&&LibraryPoseEqual(animatedBones,CaptureLibraryBonePose(entry)),"clearing a stored pose leaves active motion intact");
            var isolated=new RiggedActor();_world.AddChild(isolated);isolated.SetStaticPose(captured);isolated.EvaluateTimeline(0);
            var isolatedSkeleton=Descendants(isolated).OfType<Skeleton3D>().Single();
            var isolatedBones=Enumerable.Range(0,isolatedSkeleton.GetBoneCount()).Select(isolatedSkeleton.GetBonePose).ToArray();
            Check(animatedBones.Zip(isolatedBones).All(pair=>Math.Abs(pair.First.Basis.GetRotationQuaternion().Dot(pair.Second.Basis.GetRotationQuaternion()))>.9999f),"captured motion frame retains all local bone rotations without loading a source");
            isolated.EvaluateTimeline(6);Check(isolated.GetStaticPoseSnapshot()!=null&&isolated.MotionTrackClipCount==0,"captured frame remains a source-independent static pose");isolated.QueueFree();
            DropPose(userPose.Id,entry,entry.Node.Position);
            Check(entry.Data.StaticPose!=null&&entry.Data.MotionClips.Count==0&&string.IsNullOrEmpty(entry.Data.MotionPath),"custom preset applies as a static figure with no source dependency");

            DropPose(sitting.Id,entry,entry.Node.Position);
            entry.Data.Path=new ActorPathData{Enabled=true,Duration=8,OrientToPath=true,Points=new(){new float[]{-2,0,1},new float[]{0,0,2},new float[]{3,0,0}}};
            entry.Data.PathOffset=new float[]{.15f,.2f,0};entry.Data.Rotation=new float[]{0,30,0};
            TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Position,0,Vector3.Zero);TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Position,8,new Vector3(.4f,0,0));
            TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Rotation,0,Vector3.Zero);TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Rotation,8,new Vector3(0,40,0));
            SetTime(4);var expected=ActorPathRuntime.Evaluate(entry.Data.Path,4);
            Check(entry.Node.Position.DistanceTo(expected.Position+Vector(entry.Data.PathOffset)+new Vector3(.2f,0,0))<.001f,"path and keyed manual position remain above a static pose");
            var forward=entry.Node.Basis*actor.AnimatedForward;forward.Y=0;var expectedForward=new Basis(Vector3.Up,Mathf.DegToRad(50))*expected.Tangent;expectedForward.Y=0;
            Check(forward.Normalized().Dot(expectedForward.Normalized())>.998f,"path heading and manual yaw remain above a static pose");
            var finalTransform=entry.Node.Transform;SetTime(7);SetTime(4);Check(entry.Node.Transform.IsEqualApprox(finalTransform),"static pose plus final transform seeks deterministically");
            Select(entry);DuplicateSelected();var duplicate=_selected!;
            Check(duplicate!=entry&&duplicate.Data.StaticPose!=null&&duplicate.Data.MotionClips.Count==0,"duplicating a posed figure retains its source-independent pose");
            Check(!ReferenceEquals(duplicate.Data.StaticPose,entry.Data.StaticPose),"duplicate pose data is independent of the original");

            Select(entry);SetTime(4);await Settle();var expectedBones=CaptureLibraryBonePose(entry);var expectedBounds=PropFactory.CalculateBounds(entry.Node);string id=entry.Data.Id;
            string directory=ProjectSettings.GlobalizePath("user://pose-checks");Directory.CreateDirectory(directory);string project=Path.Combine(directory,"static-poses.previs");
            SaveTo(project);var saved=JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(project))!;
            var savedEntry=saved.Objects.First(o=>o.Id==id);
            Check(saved.Version==6&&savedEntry.StaticPose!=null&&savedEntry.MotionClips.Count==0&&string.IsNullOrEmpty(savedEntry.MotionPath),"static pose serializes inside the version 6 project format without source assets");
            LoadProjectFromPath(project);SetTime(4);await Settle();entry=_entries.First(e=>e.Data.Id==id);
            Check(LibraryPoseEqual(expectedBones,CaptureLibraryBonePose(entry)),"static bone pose survives portable save and reopen");
            Check(entry.Node.Transform.IsEqualApprox(finalTransform)&&PropFactory.CalculateBounds(entry.Node).Size.DistanceTo(expectedBounds.Size)<.005f,"static pose dimensions and final world transform survive reopen");

            Select(entry);entry.Data.Path=new();entry.Data.TransformAnimation=new();entry.Data.Rotation=new float[3];ClearSelectedPose();SetTime(0);
            Check(entry.Data.StaticPose==null&&entry.Data.MotionClips.Count==0,"clear pose restores the unanimated default figure");
            var legacy=Snapshot();foreach(var item in legacy.Objects)item.StaticPose=null;
            foreach(int oldVersion in new[]{1,2,3}){legacy.Version=oldVersion;Restore(legacy,"");SetTime(0);Check(_entries.All(e=>e.Data.StaticPose==null),"project format "+oldVersion+" without static pose data retains neutral behavior");}
            passed=true;GD.Print($"POSE_LIBRARY_OK poses={catalog.Count} checks={checks}");
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally
        {
            foreach(string poseId in createdUserPoseIds){SavedPoseLibrary.Delete(poseId);_userPoses.RemoveAll(p=>p.Id==poseId);}
            string reportPath=ProjectSettings.GlobalizePath("res://output/pose-workflow-report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);File.WriteAllText(reportPath,JsonSerializer.Serialize(new{status=passed?"passed":"failed",timestampUtc=DateTime.UtcNow,checksPassed=checks,failure,observations},new JsonSerializerOptions{WriteIndented=true}));
            Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_playing=playing;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }

    static bool PoseTransformFinite(Transform3D pose)
    {
        bool VectorFinite(Vector3 value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z);
        var q=pose.Basis.GetRotationQuaternion();return VectorFinite(pose.Origin)&&VectorFinite(pose.Basis.X)&&VectorFinite(pose.Basis.Y)&&VectorFinite(pose.Basis.Z)&&float.IsFinite(q.LengthSquared())&&Math.Abs(q.LengthSquared()-1)<.001f;
    }
}
