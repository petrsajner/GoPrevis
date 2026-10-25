using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    bool _viewportShift,_viewportCtrl,_middleZoom,_zoomUndo,_sceneTransformGesture,_numericGesture,_numericUndoPushed;

    void BeginNumericGesture(){_numericGesture=true;_numericUndoPushed=false;_playing=false;_play.Text="▶";}
    void EndNumericGesture(){_numericGesture=false;_numericUndoPushed=false;}
    void BeginSceneTransformGesture(){if(_sceneTransformGesture)return;PushUndo();_sceneTransformGesture=true;_playing=false;_play.Text="▶";}
    void EndSceneTransformGesture(){_sceneTransformGesture=false;UpdateTransformFields();UpdateSelection();RefreshTimelineSize();}

    void ApplySelectedManualTransform(string group,Vector3 value)
    {
        if(!value.IsFinite()||group=="Scale"&&(value.X<=0||value.Y<=0||value.Z<=0))throw new ArgumentException("Transform values must be finite; scale must be positive.");
        if(_selectedCamera is {} camera)
        {
            if(TryEditConstrainedCamera(camera,group,value)){SetTime(_time);RefreshCameraTrackingInspector();return;}
            EnsureCameraAnimation(camera);var list=TransformLayerRuntime.Channel(camera.Animation,group);
            if(_autoKey||list.Count>0)TransformLayerRuntime.Upsert(list,_time,value);
            else if(group=="Position")camera.BasePosition=value;else if(group=="Rotation")camera.BaseRotation=value;else camera.BaseScale=value;
        }
        else if(_selected is {} entry)
        {
            var list=TransformLayerRuntime.Channel(entry.Data.TransformAnimation,group);
            var baseline=group=="Position"?(entry.Data.Path.Enabled?Vector(entry.Data.PathOffset):Vector(entry.Data.Position)):group=="Rotation"?Vector(entry.Data.Rotation):Vector(entry.Data.Scale,Vector3.One);
            if(_autoKey||list.Count>0)
            {
                var correction=group=="Scale"?new Vector3(value.X/baseline.X,value.Y/baseline.Y,value.Z/baseline.Z):value-baseline;
                TransformLayerRuntime.Upsert(list,_time,correction);
            }
            else if(group=="Position"){if(entry.Data.Path.Enabled)entry.Data.PathOffset=Array3(value);else entry.Data.Position=Array3(value);}
            else if(group=="Rotation")entry.Data.Rotation=Array3(value);else entry.Data.Scale=Array3(value);
        }
        SetTime(_time);
    }
    void SetSelectedTransformFromGizmo(string group,Vector3 finalLocal)
    {
        if(_selectedCamera is {} camera)
        {
            var parent=camera.Rig.GetParentNode3D();var value=finalLocal;
            if(parent!=null&&group=="Position")value=parent.GlobalTransform*finalLocal;
            if(parent!=null&&group=="Rotation")value=(parent.GlobalBasis.Orthonormalized()*Basis.FromEuler(finalLocal*Mathf.Pi/180)).GetEuler()*180/Mathf.Pi;
            ApplySelectedManualTransform(group,value);return;
        }
        if(_selected is not {} entry)return;
        Vector3 current=group=="Position"?entry.Node.Position:group=="Rotation"?entry.Node.RotationDegrees:entry.Node.Scale;
        Vector3 manual=ManualValue(entry,group);
        if(group=="Scale")manual=finalLocal;
        else
        {
            var delta=finalLocal-current;if(group=="Rotation")for(int i=0;i<3;i++)delta[i]=Mathf.Wrap(delta[i],-180,180);
            manual+=delta;
        }
        ApplySelectedManualTransform(group,manual);
    }
    void RestoreInteractionSelection(string? objectId,string? cameraId)
    {
        if(cameraId!=null){var camera=_cameras.FirstOrDefault(c=>c.Id==cameraId);if(camera!=null){SelectCamera(camera);return;}}
        if(objectId!=null){var entry=_entries.FirstOrDefault(e=>e.Data.Id==objectId);if(entry!=null)Select(entry);}
    }
    void UpdateViewportModifiers(InputEvent input)
    {
        if(input is InputEventKey control&&(control.Keycode==Key.Ctrl||control.PhysicalKeycode==Key.Ctrl))_viewportCtrl=control.Pressed;
        else if(input is InputEventWithModifiers controlModifiers)_viewportCtrl=controlModifiers.CtrlPressed;
        if(input is InputEventKey key&&(key.Keycode==Key.Shift||key.PhysicalKeycode==Key.Shift))_viewportShift=key.Pressed;
        else if(input is InputEventWithModifiers modifiers)_viewportShift=modifiers.ShiftPressed;
        UpdatePoseModifierState(input);
    }
    bool HandleViewportZoom(InputEvent input)
    {
        if(input is InputEventMouseButton {ButtonIndex:MouseButton.Middle} button)
        {
            if(!button.Pressed){bool was=_middleZoom;_middleZoom=false;_zoomUndo=false;return was;}
            if(!button.AltPressed)return false;
            ReleasePoseViewportDrag();ReleaseObjectGizmoDrag();_middleZoom=true;_middleDown=false;_zoomUndo=false;return true;
        }
        if(!_middleZoom||input is not InputEventMouseMotion motion)return false;
        if(_pilot&&!_zoomUndo){PushUndo();_zoomUndo=true;_playing=false;_play.Text="▶";}
        float previous=_distance;_distance=Math.Clamp(_distance*Mathf.Exp(motion.Relative.Y*.008f),.7f,100);
        if(_pilot){_editorCamera.Position+=_editorCamera.Basis.Z*(_distance-previous);_shot.GlobalTransform=_editorCamera.GlobalTransform;StorePilotedCameraPose();}
        else UpdateOrbit();
        return true;
    }
    PhysicsRayQueryParameters3D ScenePickQuery(Vector3 from,Vector3 to)
    {
        var query=PhysicsRayQueryParameters3D.Create(from,to,1);query.Exclude=new Godot.Collections.Array<Rid>(_cameras.Where(c=>!c.Rig.IsVisibleInTree()).SelectMany(c=>Descendants(c.Rig).OfType<CollisionObject3D>()).Select(c=>c.GetRid()));return query;
    }
    async Task RunInteractionChecksAndQuit()
    {
        try{await RunInteractionChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("INTERACTION_FAILED "+ex);GetTree().Quit(1);}
    }
}
