using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    bool CameraFollowActive(CameraEntry camera)=>camera.Tracking.FollowEnabled&&_entries.Any(entry=>entry.Data.Id==camera.Tracking.FollowTargetId);
    bool CameraLookAtActive(CameraEntry camera)=>camera.Tracking.LookAtEnabled&&_entries.Any(entry=>entry.Data.Id==camera.Tracking.LookAtTargetId);
    string ActiveCameraTrackingLabel
    {
        get{var camera=_cameras.FirstOrDefault(item=>item.Rig==_shot);if(camera==null)return "";bool follow=CameraFollowActive(camera),look=CameraLookAtActive(camera);return follow&&look?"Follow + Look at":follow?"Follow":look?"Look at":"";}
    }

    TrackingTargetPose SampleTrackingTarget(Entry entry,double time)
    {
        bool path=entry.Data.Path.Enabled&&entry.Data.Path.Points.Count>=2;
        var motion=entry.Node is RiggedActor actor?actor.SampleTrackingMotion(time,path,entry.Data.PoseCorrections):entry.NativeTrack is {} native?native.SampleTrackingMotion(time,path,entry.Data.PoseCorrections,entry.Data.StaticPose):(Vector3.Zero,Vector3.Forward);
        var value=TransformLayerRuntime.Evaluate(Vector(entry.Data.Position),Vector(entry.Data.Rotation),Vector(entry.Data.Scale,Vector3.One),entry.Data.TransformAnimation,entry.Data.Path,time,motion.Item2);
        var basis=Basis.FromEuler(value.RotationDegrees*Mathf.Pi/180);var scaled=new Basis(basis.X*value.Scale.X,basis.Y*value.Scale.Y,basis.Z*value.Scale.Z);var localPosition=value.Position+(path?Vector(entry.Data.PathOffset):Vector3.Zero)+scaled*motion.Item1;
        var facing=motion.Item2;facing.Y=0;if(facing.LengthSquared()<.00001f)facing=Vector3.Forward;
        var semanticBasis=basis*Basis.LookingAt(facing.Normalized(),Vector3.Up);
        var parent=entry.Node.GetParentNode3D();return parent==null?new(localPosition,semanticBasis):new(parent.GlobalTransform*localPosition,parent.GlobalBasis.Orthonormalized()*semanticBasis);
    }
    (Vector3 Position,Quaternion Rotation) EvaluateTrackedCameraPose(CameraEntry camera,double time)
    {
        var tracking=camera.Tracking;
        var follow=tracking.FollowEnabled?_entries.FirstOrDefault(entry=>entry.Data.Id==tracking.FollowTargetId):null;
        var look=tracking.LookAtEnabled?_entries.FirstOrDefault(entry=>entry.Data.Id==tracking.LookAtTargetId):null;
        var cache=new Dictionary<(string,long),TrackingTargetPose>();
        TrackingTargetPose Sample(Entry entry,double t)
        {
            var key=(entry.Data.Id,(long)Math.Round(t*1000000));if(!cache.TryGetValue(key,out var pose))cache[key]=pose=SampleTrackingTarget(entry,t);return pose;
        }
        Vector3 Position(double t)=>follow==null?CameraBasePosition(camera,t):CameraTrackingRuntime.Follow(s=>Sample(follow,s),t,tracking);
        Quaternion SourceRotation(double t)=>CameraBaseRotation(camera,t);
        Quaternion Look(double t)
        {
            var pose=Sample(look!,t);var offset=Vector(tracking.LookAtOffset);var aim=pose.Position+(tracking.TargetRelativeOffsets?pose.Basis*offset:offset);
            return CameraTrackingRuntime.LookRotation(Position(t),aim,SourceRotation(t));
        }
        var rotation=look==null?SourceRotation(time):CameraTrackingRuntime.FilterRotation(Look,time,tracking.LookAtSmoothness)*Basis.FromEuler(Vector(tracking.RotationOffset)*Mathf.Pi/180).GetRotationQuaternion();
        return(Position(time),rotation.Normalized());
    }
    void ApplyCameraTracking(CameraEntry camera,double time)
    {
        if(!CameraFollowActive(camera)&&!CameraLookAtActive(camera))return;
        var result=EvaluateTrackedCameraPose(camera,time);camera.Rig.GlobalPosition=result.Position;camera.Rig.Quaternion=result.Rotation;
    }
    bool TryEditConstrainedCamera(CameraEntry camera,string group,Vector3 desired)
    {
        if(TryEditCameraMovement(camera,group,desired))return true;
        if(group=="Position"&&CameraFollowActive(camera))
        {
            var entry=_entries.First(entry=>entry.Data.Id==camera.Tracking.FollowTargetId);
            var filtered=CameraTrackingRuntime.Filter(t=>SampleTrackingTarget(entry,t),_time,camera.Tracking.FollowSmoothness);
            var offset=desired-filtered.Position;
            if(camera.Tracking.TargetRelativeOffsets)
            {
                var basis=filtered.Basis;offset=Math.Abs(basis.Determinant())>.00001f?basis.Inverse()*offset:SampleTrackingTarget(entry,_time).Basis.Inverse()*offset;
            }
            camera.Tracking.FollowOffset=Array3(offset);return true;
        }
        if(group=="Rotation"&&CameraLookAtActive(camera))
        {
            var stored=camera.Tracking.RotationOffset;camera.Tracking.RotationOffset=new float[3];
            Quaternion basis;try{basis=EvaluateTrackedCameraPose(camera,_time).Rotation;}finally{camera.Tracking.RotationOffset=stored;}
            var rotation=(basis.Inverse()*Basis.FromEuler(desired*Mathf.Pi/180).GetRotationQuaternion()).Normalized();camera.Tracking.RotationOffset=Array3(new Basis(rotation).GetEuler()*180/Mathf.Pi);return true;
        }
        return false;
    }
    void DisableCameraTrackingForPreset(CameraEntry camera)
    {camera.Movement=new();camera.Tracking.FollowEnabled=false;camera.Tracking.LookAtEnabled=false;RefreshCameraTrackingInspector();}
}
