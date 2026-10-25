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
    public async Task<int> RunCameraTrackingChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();bool playing=_playing;int checks=0;bool passed=false;string? failure=null;
        var observations=new List<object>();string output=ProjectSettings.GlobalizePath("res://output/camera-tracking-checks");Directory.CreateDirectory(output);
        void Check(bool condition,string name){Require(condition,"Camera tracking: "+name);checks++;observations.Add(new{name,passed=condition});}
        try
        {
            _playing=false;StopPoseEditing();ClearObjects();ClearCameras();
            var car=AddObject("car",Vector3.Zero,"Tracking car");string carId=car.Data.Id;
            car.Data.Path=new ActorPathData{Enabled=true,Duration=8,Points=new(){new[]{-5f,0,6},new[]{-5f,0,0},new[]{2f,0,-4},new[]{8f,0,-4}},PointTimes=new(){0,2,5,8}};
            var camera=CreateCamera("Live tracking",null,true);string cameraId=camera.Id;SelectCamera(camera);SyncCameraFromRig(camera);
            TransformLayerRuntime.Upsert(camera.Animation.Position,0,new Vector3(50,20,30));TransformLayerRuntime.Upsert(camera.Animation.Position,8,new Vector3(75,10,50));
            TransformLayerRuntime.Upsert(camera.Animation.Rotation,0,Vector3.Zero);TransformLayerRuntime.Upsert(camera.Animation.Rotation,8,new Vector3(0,40,0));
            string source=JsonSerializer.Serialize(camera.Animation);
            camera.Tracking=new CameraTrackingData{FollowEnabled=true,FollowTargetId=carId,FollowOffset=new[]{-5.8f,3.5f,-7.9f},LookAtEnabled=true,LookAtTargetId=carId,LookAtOffset=new[]{0f,.8f,-.65f}};
            SetTime(3);var target=SampleTrackingTarget(car,3);var expected=target.Position+target.Basis*Vector(camera.Tracking.FollowOffset);
            Check(camera.Rig.GlobalPosition.DistanceTo(expected)<.0001,"Follow uses the evaluated object path and metre offsets, overriding retained absolute position keys");
            var aim=target.Position+target.Basis*Vector(camera.Tracking.LookAtOffset);
            Check((-camera.Rig.GlobalBasis.Z).Dot((aim-camera.Rig.GlobalPosition).Normalized())>.99999f,"Look at aims at the selected object's evaluated offset");
            Check(ActiveCameraTrackingLabel=="Follow + Look at","live constraint status is available beside the camera preview");
            var other=AddObject("box",new Vector3(2,1,2),"Independent aim target");var followPosition=camera.Rig.GlobalPosition;camera.Tracking.LookAtTargetId=other.Data.Id;SetTime(3);
            var otherPose=SampleTrackingTarget(other,3);var otherAim=otherPose.Position+otherPose.Basis*Vector(camera.Tracking.LookAtOffset);
            Check(camera.Rig.GlobalPosition.IsEqualApprox(followPosition)&&(-camera.Rig.GlobalBasis.Z).Dot((otherAim-camera.Rig.GlobalPosition).Normalized())>.99999f,"Follow and Look at can use different object targets independently");
            camera.Tracking.LookAtTargetId=carId;camera.Tracking.LookAtEnabled=false;SetTime(3);
            Check(Math.Abs(camera.Rig.Quaternion.Dot(Basis.FromEuler(TransformLayerRuntime.EvaluateVector(camera.Animation.Rotation,3,camera.BaseRotation,true)*Mathf.Pi/180).GetRotationQuaternion()))>.99999f,"Follow alone retains the original animated camera rotation");
            camera.Tracking.FollowEnabled=false;camera.Tracking.LookAtEnabled=true;SetTime(3);
            Check(camera.Rig.GlobalPosition.IsEqualApprox(TransformLayerRuntime.EvaluateVector(camera.Animation.Position,3,camera.BasePosition)),"Look at alone retains the original animated camera position");
            camera.Tracking.FollowEnabled=true;camera.Tracking.TargetRelativeOffsets=false;car.Data.Scale=new[]{2f,1.5f,.5f};SetTime(3);
            Check(camera.Rig.GlobalPosition.IsEqualApprox(SampleTrackingTarget(car,3).Position+Vector(camera.Tracking.FollowOffset)),"world-space offsets remain in metres even for rotated and nonuniformly scaled targets");
            camera.Tracking.TargetRelativeOffsets=true;car.Data.Scale=new[]{1f,1f,1f};SetTime(3);
            var before=camera.Rig.GlobalTransform;car.Data.Path.Points[1][0]-=2;SetTime(3);
            Check(camera.Rig.GlobalPosition.DistanceTo(before.Origin)>.2f,"editing a target path immediately changes the live camera without rewriting keys");
            Check(JsonSerializer.Serialize(camera.Animation)==source,"Follow and Look at retain all original source camera keys unchanged");
            SetTime(4.37);var exact=camera.Rig.GlobalTransform;SetTime(0);SetTime(7);SetTime(4.37);Check(camera.Rig.GlobalTransform.IsEqualApprox(exact),"reverse seeking reproduces an exact unsmoothed camera pose");
            camera.Tracking.FollowSmoothness=.45;camera.Tracking.LookAtSmoothness=.25;SetTime(4.37);var smooth=camera.Rig.GlobalTransform;
            Check(smooth.Origin.DistanceTo(exact.Origin)>.05f,"positive smoothness visibly eases behind the desired follow position");
            foreach(double t in new[]{.1,1.8,6.9,2.2,0,7.9})SetTime(t);SetTime(4.37);
            Check(camera.Rig.GlobalTransform.IsEqualApprox(smooth),"combined position and rotation smoothing depends only on absolute time, not prior playback frames");
            int fps=_fps;bool frameIndependent=true;foreach(int rate in new[]{12,15,25,30,60}){_fps=rate;SetTime(4.37);frameIndependent&=camera.Rig.GlobalTransform.IsEqualApprox(smooth);}_fps=fps;
            Check(frameIndependent,"smooth tracking uses the same absolute pose for animation and film frame rates");
            var small=CameraTrackingRuntime.Filter(t=>new TrackingTargetPose(new Vector3((float)t,0,0),Basis.Identity),4,.2).Position;
            var large=CameraTrackingRuntime.Filter(t=>new TrackingTargetPose(new Vector3((float)t,0,0),Basis.Identity),4,.8).Position;
            Check(large.X<small.X&&small.X<4,"larger smoothness produces a larger causal follow lag");
            var initial=CameraTrackingRuntime.Filter(t=>new TrackingTargetPose(new Vector3((float)t,0,0),Basis.Identity),0,1).Position;
            Check(initial.IsEqualApprox(Vector3.Zero),"the smoothing filter starts from the target's held time-zero pose without startup jumps");
            camera.Tracking.FollowSmoothness=0;camera.Tracking.LookAtSmoothness=0;SetTime(3);SelectCamera(camera);
            var desired=camera.Rig.GlobalPosition+new Vector3(.3f,.2f,-.1f);PushUndo();ApplySelectedManualTransform("Position",desired);
            Check(camera.Rig.GlobalPosition.DistanceTo(desired)<.0001,"ordinary camera XYZ/gizmo editing adjusts the live follow offset");
            var desiredRotation=camera.Rig.GlobalRotationDegrees+new Vector3(2,7,0);PushUndo();ApplySelectedManualTransform("Rotation",desiredRotation);
            Check(Math.Abs(camera.Rig.GlobalBasis.Orthonormalized().GetRotationQuaternion().Dot(Basis.FromEuler(desiredRotation*Mathf.Pi/180).GetRotationQuaternion()))>.99999f,"manual camera rotation adds an angle correction above Look at");
            RefreshCameraTrackingInspector();UpdateTransformFields();
            Check(_parameterKeyButtons["Position"].Disabled&&_parameterKeyButtons["Rotation"].Disabled,"controlled source-channel key buttons explain that live offsets are being edited");
            CaptureSelectedTransformKey("Position");CaptureSelectedTransformKey("Rotation");
            Check(JsonSerializer.Serialize(camera.Animation)==source,"key capture cannot accidentally bake or replace controlled source camera channels");
            _trackingExpanded=true;RefreshCameraTrackingInspector();
            Check(_trackingPanel!.Visible&&_trackingContent!.Visible&&_trackingHeader!.Text.Contains(car.Data.Name),"the camera inspector identifies the enabled target and exposes its live settings");
            var offset=camera.Tracking.FollowOffset.ToArray();int history=_undo.Count;EditCameraTracking(data=>data.FollowOffset[1]+=.4f);Undo();camera=_cameras.Single(item=>item.Id==cameraId);car=_entries.Single(item=>item.Data.Id==carId);
            Check(camera.Tracking.FollowOffset.SequenceEqual(offset),"Undo restores a live camera parameter without removing its target binding");
            Redo();camera=_cameras.Single(item=>item.Id==cameraId);car=_entries.Single(item=>item.Data.Id==carId);
            Check(Math.Abs(camera.Tracking.FollowOffset[1]-offset[1]-.4f)<.0001,"Redo restores the live follow parameter");
            SetTime(3);var restoredPose=camera.Rig.GlobalTransform;string file=Path.Combine(output,"tracking.previs");SaveTo(file);LoadProjectFromPath(file);SetTime(3);camera=_cameras.Single(item=>item.Id==cameraId);car=_entries.Single(item=>item.Data.Id==carId);
            Check(camera.Rig.GlobalTransform.IsEqualApprox(restoredPose)&&camera.Tracking.FollowTargetId==carId&&Snapshot().Version==ProjectData.CurrentVersion,"project current format reopens bindings, offsets and the exact live camera pose");
            var duplicate=SnapshotCamera(camera);duplicate.Id=Guid.NewGuid().ToString("N");var copy=CreateCamera("Tracking copy",duplicate,false);copy.Tracking.FollowOffset[0]+=3;
            Check(Math.Abs(copy.Tracking.FollowOffset[0]-camera.Tracking.FollowOffset[0]-3)<.0001,"duplicated camera tracking settings have independent offset arrays");
            camera.Tracking.FollowTargetId="missing-target";camera.Tracking.LookAtTargetId="missing-target";SetTime(3);
            Check(camera.Rig.GlobalTransform.IsFinite()&&camera.Rig.GlobalPosition.IsEqualApprox(TransformLayerRuntime.EvaluateVector(camera.Animation.Position,3,camera.BasePosition)),"a missing/deleted target safely falls back to the camera's retained animation");
            camera.Tracking.FollowTargetId=carId;camera.Tracking.LookAtTargetId=carId;SetTime(3);SelectCamera(camera);ApplyCameraFraming("front-wide");
            Check(!camera.Tracking.FollowEnabled&&!camera.Tracking.LookAtEnabled,"choosing a framing preset deliberately releases live constraints so the preset is visible");
            camera.Tracking.FollowEnabled=true;camera.Tracking.LookAtEnabled=true;ApplyIllustratedCameraMove("static");
            Check(!camera.Tracking.FollowEnabled&&!camera.Tracking.LookAtEnabled,"choosing a movement preset deliberately releases live constraints with Undo support");

            var human=AddObject("actor",Vector3.Zero,"Tracking human");LoadMotion(human,SamplePath("samples/real/CMU_02_01_Walk.bvh"),.055f);SetTime(1.4);
            var actor=(RiggedActor)human.Node;var humanPose=JsonSerializer.Serialize(actor.CaptureEvaluatedPose());var metadata=actor.SampleTrackingMotion(1.4,false,human.Data.PoseCorrections);
            Check(metadata.Root.DistanceTo(actor.RootMotionOffset)<.0001&&metadata.Facing.Dot(actor.AnimatedForward)>.99999f,"the stateless human sampler matches current blended root locomotion and source heading");
            _=actor.SampleTrackingMotion(.2,false,human.Data.PoseCorrections);
            Check(JsonSerializer.Serialize(actor.CaptureEvaluatedPose())==humanPose,"historical human target samples never modify the displayed skeleton");
            var wolf=AddCatalogModel(ContentCatalog.Find("animal-wolf")!,Vector3.Zero,true);var native=wolf.NativeTrack!;SetTime(1.4);var nativePose=JsonSerializer.Serialize(native.CaptureEvaluatedPose());
            var a=native.SampleTrackingMotion(1.4,false,wolf.Data.PoseCorrections,wolf.Data.StaticPose);var worldA=native.GetPoseJointWorld(native.RootBoneName);SetTime(0);var b=native.SampleTrackingMotion(0,false,wolf.Data.PoseCorrections,wolf.Data.StaticPose);var worldB=native.GetPoseJointWorld(native.RootBoneName);
            Check((a.Root-b.Root).DistanceTo(worldA-worldB)<.001f,"the stateless native sampler reads actual animation-track root displacement");
            SetTime(1.4);_=native.SampleTrackingMotion(.3,false,wolf.Data.PoseCorrections,wolf.Data.StaticPose);
            Check(JsonSerializer.Serialize(native.CaptureEvaluatedPose())==nativePose,"native history sampling never seeks the player or changes displayed bones");
            bool rejected=false;try{new CameraTrackingData{FollowOffset=new[]{float.NaN,0,0}}.ValidatedCopy();}catch(ArgumentException){rejected=true;}
            Check(rejected,"invalid tracking values fail validation before state changes");
            SelectCamera(camera);_trackingExpanded=true;RefreshCameraTrackingInspector();SetTime(3);
            if(DisplayServer.GetName()!="headless")
            {for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"camera-tracking-ui.png"));}
            passed=true;GD.Print("CAMERA_TRACKING_OK checks="+checks);return checks;
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            Restore(original,"");_projectPath=project;SetTime(time);_playing=playing;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));
        }
    }
    async Task RunCameraTrackingChecksAndQuit(){try{await RunCameraTrackingChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("CAMERA_TRACKING_FAILED "+ex);GetTree().Quit(1);}}
}
