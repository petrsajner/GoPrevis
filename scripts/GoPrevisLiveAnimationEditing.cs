using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    string? LiveAnimationPath(CameraEntry c,string group)
        =>group=="Position"?CameraFollowActive(c)?"Tracking.FollowOffset":CameraMovementActive(c)?"Movement.PositionOffset":null
            :group=="Rotation"?CameraLookAtActive(c)?"Tracking.RotationOffset":CameraRotationControlled(c)?"Movement.RotationOffset":null:null;
    bool TryEditLiveAnimatedCamera(CameraEntry c,string group,Vector3 desired)
    {
        string? path=LiveAnimationPath(c,group);if(path==null)return false;
        float[] values;
        var originalTracking=c.Tracking;var originalMovement=c.Movement;
        try
        {
            using(var evaluation=EvaluatePropertyState(_time,false))
            {
                c.Tracking=CloneAnimationState(c.Tracking);c.Movement=CloneAnimationState(c.Movement);
                if(!TryEditConstrainedCamera(c,group,desired))return false;
                values=(path=="Tracking.FollowOffset"?c.Tracking.FollowOffset:path=="Tracking.RotationOffset"?c.Tracking.RotationOffset:path=="Movement.PositionOffset"?c.Movement.PositionOffset:c.Movement.RotationOffset).ToArray();
            }
        }
        finally{c.Tracking=originalTracking;c.Movement=originalMovement;}
        var bindings=AnimationBindings();for(int axis=0;axis<3;axis++)
        {var b=bindings.First(b=>b.TargetId==c.Id&&b.Path==path+"."+"XYZ"[axis]);if(Math.Abs(b.Read()-values[axis])>1e-6)SetAnimationBindingValue(b,values[axis]);}
        SetTime(_time);return true;
    }
    AnimationChannelBinding? SelectedAxisBinding(string group,int axis)
    {
        string? id=_selectedCamera?.Id??_selected?.Data.Id;if(id==null)return null;
        string path=(_selectedCamera is {} c?LiveAnimationPath(c,group):null)??"Transform."+group;
        return AnimationBindings().FirstOrDefault(b=>b.TargetId==id&&b.Path==path+"."+"XYZ"[axis]);
    }
    bool StoreScalarCameraPose(CameraEntry camera,string group,Vector3 value)
    {
        string prefix="Transform."+group+".";
        if(!_autoKey&&!camera.PropertyAnimation.Channels.Keys.Any(k=>k.StartsWith(prefix)))return false;
        foreach(var b in AnimationBindings(camera.Id).Where(b=>b.Path.StartsWith(prefix)))
        {
            int axis="XYZ".IndexOf(b.Path[^1]);double desired=value[axis];
            if(group=="Rotation")desired=b.Read()+Mathf.Wrap((float)(desired-b.Read()),-180,180);
            if(Math.Abs(desired-b.Read())>1e-7)SetAnimationBindingValue(b,desired);
        }
        return true;
    }
    void StoreCameraView(CameraEntry camera,Vector3 position,Vector3 rotation,bool forceKeys)
    {
        EnsureCameraAnimation(camera);
        if(!TryEditLiveAnimatedCamera(camera,"Position",position)&&!StoreScalarCameraPose(camera,"Position",position))
        {if(forceKeys||_autoKey||camera.Animation.Position.Count>0)TransformLayerRuntime.Upsert(camera.Animation.Position,_time,position);else camera.BasePosition=position;}
        if(!TryEditLiveAnimatedCamera(camera,"Rotation",rotation)&&!StoreScalarCameraPose(camera,"Rotation",rotation))
        {if(forceKeys||_autoKey||camera.Animation.Rotation.Count>0)TransformLayerRuntime.Upsert(camera.Animation.Rotation,_time,rotation);else camera.BaseRotation=rotation;}
        SetTime(_time);
    }
    Button AnimatedParameterKey(string path,bool scene=false,bool activeCamera=false,bool group=false)
    {
        var button=Button("◇",()=>
        {
            string id=scene?"scene":activeCamera?ActiveCameraId:_selectedCamera?.Id??_selected?.Data.Id??"scene";
            var channels=AnimationBindings().Where(b=>b.TargetId==id&&(group?b.Path.StartsWith(path+"."):b.Path==path)).ToList();if(channels.Count==0)return;
            PushUndo();foreach(var b in channels)PropertyCurveRuntime.Upsert(b.EnsureCurve(),_time,b.Read());SetTime(_time);
        });button.CustomMinimumSize=new Vector2(21,22);button.TooltipText="Key "+path+" at the playhead";return button;
    }
}
