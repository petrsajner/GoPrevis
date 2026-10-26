using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    bool CameraMovementActive(CameraEntry camera)=>camera.Movement.Mode is "Orbit" or "Path";
    bool CameraPositionControlled(CameraEntry camera)=>CameraFollowActive(camera)||CameraMovementActive(camera);
    bool CameraRotationControlled(CameraEntry camera)=>CameraLookAtActive(camera)||CameraMovementActive(camera)&&camera.Movement.AimAtTarget;
    Vector3 CameraMovementCenter(CameraEntry camera,double time)
    {
        var data=camera.Movement;var target=_entries.FirstOrDefault(e=>e.Data.Id==data.TargetId);
        return(target==null?Vector(data.TargetPosition):SampleTrackingTarget(target,time).Position)+Vector(data.TargetOffset);
    }
    Vector3 CameraBasePosition(CameraEntry camera,double time)=>CameraMovementActive(camera)
        ?CameraMovementRuntime.Position(camera.Movement,time,CameraMovementCenter(camera,time))
        :TransformLayerRuntime.EvaluateVector(camera.Animation.Position,time,camera.BasePosition);
    Vector3 CameraAimCenter(CameraEntry camera,double time)=>camera.Movement.AimPathEnabled?ActorPathRuntime.Evaluate(camera.Movement.AimPath,time).Position:CameraMovementCenter(camera,time);
    Quaternion CameraBaseRotation(CameraEntry camera,double time)
    {
        var fallback=Quaternion.FromEuler(TransformLayerRuntime.EvaluateVector(camera.Animation.Rotation,time,camera.BaseRotation,true)*Mathf.Pi/180);
        return CameraMovementActive(camera)?CameraMovementRuntime.Aim(camera.Movement,CameraBasePosition(camera,time),CameraAimCenter(camera,time),fallback):fallback;
    }
    bool TryEditCameraMovement(CameraEntry camera,string group,Vector3 desired)
    {
        if(!CameraMovementActive(camera))return false;
        if(group=="Position")
        {camera.Movement.PositionOffset=Array3(Vector(camera.Movement.PositionOffset)+desired-CameraBasePosition(camera,_time));RefreshCameraMovementInspector();return true;}
        if(group=="Rotation"&&camera.Movement.AimAtTarget&&!CameraLookAtActive(camera))
        {
            var offset=camera.Movement.RotationOffset;camera.Movement.RotationOffset=new float[3];Quaternion basis;
            try{basis=CameraBaseRotation(camera,_time);}finally{camera.Movement.RotationOffset=offset;}
            camera.Movement.RotationOffset=Array3((basis.Inverse()*Quaternion.FromEuler(desired*Mathf.Pi/180)).GetEuler()*180/Mathf.Pi);RefreshCameraMovementInspector();return true;
        }
        return false;
    }
    bool TryApplyNativeOrbitPreset(CameraEntry camera,string id,Vector3 target)
    {
        if(id is not("orbit" or "ascending-orbit" or "descending-orbit"))return false;
        Vector3 start=camera.Rig.GlobalPosition,delta=start-target;float distance=Math.Max(.5f,delta.Length());Quaternion original=camera.Rig.Quaternion;
        var movement=new CameraMovementData{Mode="Orbit",Duration=_duration,TargetPosition=Array3(target),TargetOffset=new float[3],Radius=Math.Max(.2f,new Vector2(delta.X,delta.Z).Length()),StartAngleDegrees=Mathf.RadToDeg(Mathf.Atan2(delta.X,delta.Z)),OrbitDegrees=id=="orbit"?90:120,StartHeight=delta.Y,EndHeight=delta.Y+(id=="ascending-orbit"?distance*.5f:id=="descending-orbit"?-distance*.3f:0)};
        if(_selected is {} subject){movement.TargetId=subject.Data.Id;movement.TargetOffset=Array3(target-SampleTrackingTarget(subject,0).Position);}
        camera.Movement=movement;var aim=CameraBaseRotation(camera,0);movement.RotationOffset=Array3((aim.Inverse()*original).GetEuler()*180/Mathf.Pi);
        camera.Animation=new();camera.LensKeys.Clear();camera.Rig.ClearKeys();camera.AnimationInitialized=true;camera.BaseLens=camera.Rig.FocalLengthMm;return true;
    }
}
